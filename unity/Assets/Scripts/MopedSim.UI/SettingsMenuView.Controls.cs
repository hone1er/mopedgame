using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>Which device column of the Controls section a rebind capture targets.</summary>
    public enum BindingColumn
    {
        Keyboard = 0,
        Gamepad = 1
    }

    /// <summary>
    /// Controls section of the Settings menu: one row per bindable action showing the keyboard key and gamepad button,
    /// a capture state machine (Begin -> TryCommit / Cancel) driven by the App-layer input poller, and Reset to defaults.
    /// The view itself never reads input devices; it only receives the captured key/button name.
    /// </summary>
    public sealed partial class SettingsMenuView
    {
        private const string CapturePromptKey = "press a key...";
        private const string CapturePromptButton = "press a button...";

        private GameObject _controlsPanel;
        private CanvasGroup _mainGroup;
        private Button _openControlsButton;
        private Text _controlsStatusLabel;
        private Button[] _keyboardRowButtons;
        private Text[] _keyboardRowTexts;
        private Button[] _gamepadRowButtons;
        private Text[] _gamepadRowTexts;

        private bool _isCapturing;
        private string _captureActionId = string.Empty;
        private BindingColumn _captureColumn;
        private int _captureSerial;
        private int _captureEndedFrame = -1;

        /// <summary>True while a Controls row is waiting for the next key or button press.</summary>
        public bool IsCapturing { get { return _isCapturing; } }

        /// <summary>Canonical action ID being rebound (empty when not capturing).</summary>
        public string CaptureActionId { get { return _captureActionId; } }

        /// <summary>Device column being rebound.</summary>
        public BindingColumn CaptureColumn { get { return _captureColumn; } }

        /// <summary>Increments every time capture begins; pollers use it to arm once per capture (after all inputs are released).</summary>
        public int CaptureSerial { get { return _captureSerial; } }

        /// <summary>True while the Controls panel is open.</summary>
        public bool ControlsPanelOpen { get { return _controlsPanel != null && _controlsPanel.activeSelf; } }

        /// <summary>Visible status line of the Controls panel (conflict / reserved / prompt messages).</summary>
        public string ControlsStatusText { get { return _controlsStatusLabel != null ? _controlsStatusLabel.text : string.Empty; } }

        /// <summary>Raised when capture mode starts or ends.</summary>
        public event Action<bool> CaptureStateChanged;

        /// <summary>
        /// Enters capture mode for <paramref name="actionId"/> (canonical ID) in <paramref name="column"/>.
        /// Returns false when the action is not bindable in that column.
        /// </summary>
        public bool BeginCapture(string actionId, BindingColumn column)
        {
            EnsureInitialized();
            if (!IsBindable(actionId, column))
            {
                return false;
            }

            bool wasCapturing = _isCapturing;
            _isCapturing = true;
            _captureActionId = actionId;
            _captureColumn = column;
            _captureSerial++;
            SetControlsStatus(column == BindingColumn.Keyboard
                ? "Press a key for " + GetActionLabel(actionId) + " (Esc cancels)"
                : "Press a gamepad button for " + GetActionLabel(actionId) + " (Start or Esc cancels)");
            RefreshControlsUi();
            if (!wasCapturing)
            {
                RaiseCaptureStateChanged(true);
            }

            return true;
        }

        /// <summary>Leaves capture mode without changing any binding.</summary>
        public void CancelCapture()
        {
            if (!_isCapturing)
            {
                return;
            }

            EndCapture();
            SetControlsStatus(string.Empty);
        }

        /// <summary>
        /// Commits a captured key name (keyboard column) or gamepad button name (gamepad column) to the capturing action.
        /// Capture ends on both success and rejection; rejections (duplicate or reserved input) are shown in the status line.
        /// </summary>
        public bool TryCommitCapture(string inputName)
        {
            if (!_isCapturing)
            {
                return false;
            }

            string actionId = _captureActionId;
            BindingColumn column = _captureColumn;
            EndCapture();

            string conflict;
            bool ok = column == BindingColumn.Keyboard
                ? TryRebindAction(actionId, inputName, out conflict)
                : TryRebindGamepadAction(actionId, inputName, out conflict);

            if (ok)
            {
                SetControlsStatus(GetActionLabel(actionId) + " bound to " + FormatInputName(column, inputName));
            }
            else if (string.Equals(conflict, SettingsPreferencesStore.ReservedConflict, StringComparison.Ordinal))
            {
                SetControlsStatus(FormatInputName(column, inputName) + " is reserved and cannot be bound");
            }
            else if (!string.IsNullOrEmpty(conflict))
            {
                SetControlsStatus(FormatInputName(column, inputName) + " is already bound to " + GetActionLabel(conflict));
            }
            else
            {
                SetControlsStatus("Could not bind " + FormatInputName(column, inputName));
            }

            RefreshControlsUi();
            return ok;
        }

        /// <summary>
        /// Attempts to rebind the gamepad button for <paramref name="actionId"/>. Rejects duplicate or reserved buttons;
        /// emits <see cref="KeyBindingsChanged"/> only on success.
        /// </summary>
        public bool TryRebindGamepadAction(string actionId, string buttonName, out string conflict)
        {
            EnsureInitialized();
            bool rebound = SettingsPreferencesStore.TrySetGamepadBinding(_current, actionId, buttonName, out conflict);
            if (!rebound)
            {
                _lastConflictAction = string.Equals(conflict, SettingsPreferencesStore.ReservedConflict, StringComparison.Ordinal)
                    ? string.Empty
                    : conflict ?? string.Empty;
                SetConflictText(_lastConflictAction);
                return false;
            }

            _lastConflictAction = string.Empty;
            SetConflictText(string.Empty);
            EmitKeyBindingsChanged();
            return true;
        }

        /// <summary>Restores all keyboard and gamepad bindings to defaults and notifies listeners.</summary>
        public void ResetControlBindings()
        {
            EnsureInitialized();
            if (_isCapturing)
            {
                EndCapture();
            }

            SettingsPreferencesStore.ResetKeyBindings(_current);
            SettingsPreferencesStore.ResetGamepadBindings(_current);
            _lastConflictAction = string.Empty;
            SetConflictText(string.Empty);
            SetControlsStatus("Controls reset to defaults");
            RefreshControlsUi();
            EmitKeyBindingsChanged();
        }

        /// <summary>Shows the Controls panel and focuses its first row for keyboard/gamepad navigation.</summary>
        public void OpenControlsPanel()
        {
            if (_controlsPanel == null)
            {
                return;
            }

            _controlsPanel.SetActive(true);
            if (_mainGroup != null)
            {
                _mainGroup.interactable = false;
            }

            RefreshControlsUi();
            SelectForNavigation(_keyboardRowButtons != null && _keyboardRowButtons.Length > 0 ? _keyboardRowButtons[0] : null);
        }

        /// <summary>Hides the Controls panel (cancelling any capture) and returns focus to the main menu.</summary>
        public void CloseControlsPanel()
        {
            CancelCapture();
            if (_controlsPanel != null)
            {
                _controlsPanel.SetActive(false);
            }

            if (_mainGroup != null)
            {
                _mainGroup.interactable = true;
            }

            SetControlsStatus(string.Empty);
            SelectForNavigation(_openControlsButton);
        }

        private void OnDisable()
        {
            if (_isCapturing)
            {
                EndCapture();
            }

            if (_controlsPanel != null)
            {
                _controlsPanel.SetActive(false);
            }

            if (_mainGroup != null)
            {
                _mainGroup.interactable = true;
            }
        }

        // The press that commits a capture (Enter/Space/A) is also a UI Submit on the still-selected row; ignore that
        // activation on the frame capture just ended so it does not immediately re-enter capture mode.
        private void BeginCaptureFromRow(string actionId, BindingColumn column)
        {
            if (Time.frameCount == _captureEndedFrame)
            {
                return;
            }

            BeginCapture(actionId, column);
        }

        private void EndCapture()
        {
            _captureEndedFrame = Time.frameCount;
            _isCapturing = false;
            _captureActionId = string.Empty;
            RefreshControlsUi();
            RaiseCaptureStateChanged(false);
        }

        private void RaiseCaptureStateChanged(bool capturing)
        {
            Action<bool> handler = CaptureStateChanged;
            if (handler != null)
            {
                handler(capturing);
            }
        }

        private static bool IsBindable(string actionId, BindingColumn column)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return false;
            }

            if (column == BindingColumn.Gamepad)
            {
                return GamepadBindingActionIds.IndexOf(actionId) >= 0;
            }

            for (int i = 0; i < KeyBindingActionIds.All.Count; i++)
            {
                if (string.Equals(KeyBindingActionIds.All[i], actionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildControlsSection(Font font)
        {
            _mainGroup = _modalRoot.GetComponent<CanvasGroup>();
            if (_mainGroup == null)
            {
                _mainGroup = _modalRoot.AddComponent<CanvasGroup>();
            }

            Transform footer = _modalRoot.transform.Find("Footer");
            if (footer != null)
            {
                _openControlsButton = CreateButton(footer, "Btn_Controls", "Controls", new Color(0.22f, 0.40f, 0.62f, 1f), font, OpenControlsPanel);
                _openControlsButton.transform.SetSiblingIndex(0);
            }

            int actionCount = KeyBindingActionIds.All.Count;
            _controlsPanel = new GameObject("SettingsMenu_Controls", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(CanvasGroup), typeof(ContentSizeFitter));
            _controlsPanel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _controlsPanel.transform.SetParent(_modalRoot.transform, false);
            _controlsPanel.GetComponent<LayoutElement>().ignoreLayout = true;
            _controlsPanel.GetComponent<CanvasGroup>().ignoreParentGroups = true;
            RectTransform rt = (RectTransform)_controlsPanel.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(640f, 0f);
            _controlsPanel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.98f);

            VerticalLayoutGroup layout = _controlsPanel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateLabel(_controlsPanel.transform, "Title", "CONTROLS", 22, TextAnchor.MiddleLeft, Color.white, 30f, font);
            BuildControlsHeaderRow(_controlsPanel.transform, font);

            _keyboardRowButtons = new Button[actionCount];
            _keyboardRowTexts = new Text[actionCount];
            _gamepadRowButtons = new Button[actionCount];
            _gamepadRowTexts = new Text[actionCount];
            for (int i = 0; i < actionCount; i++)
            {
                BuildControlsRow(_controlsPanel.transform, i, KeyBindingActionIds.All[i], font);
            }

            _controlsStatusLabel = CreateLabel(_controlsPanel.transform, "ControlsStatusLabel", string.Empty, 13, TextAnchor.MiddleLeft, new Color(0.95f, 0.75f, 0.40f, 1f), 24f, font);

            GameObject footerGo = new GameObject("ControlsFooter", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_controlsPanel.transform, false);
            footerGo.GetComponent<LayoutElement>().preferredHeight = 40f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;
            CreateButton(footerGo.transform, "Btn_ResetControls", "Reset to defaults", new Color(0.50f, 0.28f, 0.20f, 1f), font, ResetControlBindings);
            CreateButton(footerGo.transform, "Btn_ControlsBack", "Back", new Color(0.26f, 0.27f, 0.32f, 1f), font, CloseControlsPanel);

            _controlsPanel.SetActive(false);
            RefreshControlsUi();
        }

        private static void BuildControlsHeaderRow(Transform parent, Font font)
        {
            GameObject row = CreateControlsRowContainer(parent, "HeaderRow");
            Text action = CreateLabel(row.transform, "ActionHeader", "Action", 13, TextAnchor.MiddleLeft, new Color(0.70f, 0.90f, 1f, 1f), 22f, font);
            action.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Text keyboard = CreateLabel(row.transform, "KeyboardHeader", "Keyboard", 13, TextAnchor.MiddleCenter, new Color(0.70f, 0.90f, 1f, 1f), 22f, font);
            keyboard.GetComponent<LayoutElement>().preferredWidth = 180f;
            Text gamepad = CreateLabel(row.transform, "GamepadHeader", "Gamepad", 13, TextAnchor.MiddleCenter, new Color(0.70f, 0.90f, 1f, 1f), 22f, font);
            gamepad.GetComponent<LayoutElement>().preferredWidth = 180f;
        }

        private void BuildControlsRow(Transform parent, int index, string actionId, Font font)
        {
            GameObject row = CreateControlsRowContainer(parent, "ControlsRow_" + actionId);
            Text label = CreateLabel(row.transform, "Label", GetActionLabel(actionId), 14, TextAnchor.MiddleLeft, Color.white, 28f, font);
            label.GetComponent<LayoutElement>().flexibleWidth = 1f;

            Color keyColor = new Color(0.20f, 0.30f, 0.46f, 1f);
            string capturedId = actionId;
            Button kb = CreateButton(row.transform, "Btn_Key_" + actionId, string.Empty, keyColor, font, delegate { BeginCaptureFromRow(capturedId, BindingColumn.Keyboard); });
            kb.GetComponent<LayoutElement>().preferredWidth = 180f;
            _keyboardRowButtons[index] = kb;
            _keyboardRowTexts[index] = kb.GetComponentInChildren<Text>();

            Button pad = CreateButton(row.transform, "Btn_Pad_" + actionId, string.Empty, keyColor, font, delegate { BeginCaptureFromRow(capturedId, BindingColumn.Gamepad); });
            pad.GetComponent<LayoutElement>().preferredWidth = 180f;
            pad.interactable = GamepadBindingActionIds.IndexOf(actionId) >= 0;
            _gamepadRowButtons[index] = pad;
            _gamepadRowTexts[index] = pad.GetComponentInChildren<Text>();
        }

        private static GameObject CreateControlsRowContainer(Transform parent, string name)
        {
            GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 28f;
            HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return row;
        }

        private void RefreshControlsUi()
        {
            if (_keyboardRowTexts == null || _gamepadRowTexts == null || _current == null)
            {
                return;
            }

            for (int i = 0; i < _keyboardRowTexts.Length; i++)
            {
                string actionId = KeyBindingActionIds.All[i];
                bool capturingThis = _isCapturing && string.Equals(_captureActionId, actionId, StringComparison.Ordinal);

                if (_keyboardRowTexts[i] != null)
                {
                    _keyboardRowTexts[i].text = capturingThis && _captureColumn == BindingColumn.Keyboard
                        ? CapturePromptKey
                        : FormatInputName(BindingColumn.Keyboard, SettingsPreferencesStore.GetKeyBinding(_current, actionId));
                }

                if (_gamepadRowTexts[i] != null)
                {
                    if (GamepadBindingActionIds.IndexOf(actionId) < 0)
                    {
                        _gamepadRowTexts[i].text = "Left Stick (fixed)";
                    }
                    else
                    {
                        _gamepadRowTexts[i].text = capturingThis && _captureColumn == BindingColumn.Gamepad
                            ? CapturePromptButton
                            : FormatInputName(BindingColumn.Gamepad, SettingsPreferencesStore.GetGamepadBinding(_current, actionId));
                    }
                }
            }
        }

        private void SetControlsStatus(string text)
        {
            if (_controlsStatusLabel != null)
            {
                _controlsStatusLabel.text = text ?? string.Empty;
            }
        }

        private static void SelectForNavigation(Button button)
        {
            EventSystem es = EventSystem.current;
            if (es != null && button != null)
            {
                es.SetSelectedGameObject(button.gameObject);
            }
        }

        private static string GetActionLabel(string actionId)
        {
            switch (actionId)
            {
                case KeyBindingActionIds.Throttle: return "Throttle";
                case KeyBindingActionIds.SteerLeft: return "Steer Left";
                case KeyBindingActionIds.SteerRight: return "Steer Right";
                case KeyBindingActionIds.RearBrake: return "Rear Brake";
                case KeyBindingActionIds.FrontBrake: return "Front Brake";
                case KeyBindingActionIds.PedalAssist: return "Pedal Assist";
                case KeyBindingActionIds.AeroTuck: return "Aero Tuck";
                case KeyBindingActionIds.Choke: return "Choke";
                case KeyBindingActionIds.Decompressor: return "Decompressor";
                case KeyBindingActionIds.Interact: return "Interact";
                case KeyBindingActionIds.ToggleCamera: return "Toggle Camera";
                default: return actionId ?? string.Empty;
            }
        }

        private static string FormatInputName(BindingColumn column, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            if (column == BindingColumn.Gamepad)
            {
                switch (name)
                {
                    case "North": return "North (Y)";
                    case "South": return "South (A)";
                    case "West": return "West (X)";
                    case "East": return "East (B)";
                    case "DpadUp": return "D-Pad Up";
                    case "DpadDown": return "D-Pad Down";
                    case "DpadLeft": return "D-Pad Left";
                    case "DpadRight": return "D-Pad Right";
                    case "LeftShoulder": return "Left Bumper";
                    case "RightShoulder": return "Right Bumper";
                    case "LeftStick": return "Left Stick Click";
                    case "RightStick": return "Right Stick Click";
                    case "LeftTrigger": return "Left Trigger";
                    case "RightTrigger": return "Right Trigger";
                    default: return name;
                }
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
