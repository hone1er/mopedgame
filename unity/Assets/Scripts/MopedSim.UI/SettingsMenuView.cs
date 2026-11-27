using System;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Immutable value snapshot emitted by <see cref="SettingsMenuView.AudioMixChanged"/>. Zero heap allocations.
    /// </summary>
    public readonly struct AudioMixSettings
    {
        public readonly float MasterVolume;
        public readonly float EngineVolume;
        public readonly float FoleyVolume;
        public readonly float AmbienceVolume;

        public AudioMixSettings(float masterVolume, float engineVolume, float foleyVolume, float ambienceVolume)
        {
            MasterVolume = masterVolume;
            EngineVolume = engineVolume;
            FoleyVolume = foleyVolume;
            AmbienceVolume = ambienceVolume;
        }
    }

    /// <summary>
    /// Immutable value snapshot emitted by <see cref="SettingsMenuView.GraphicsPresetChanged"/>. Zero heap allocations.
    /// </summary>
    public readonly struct GraphicsQualitySettings
    {
        public readonly GraphicsPreset Preset;
        public readonly int TargetFrameRate;
        public readonly float FogQualityScale;

        public GraphicsQualitySettings(GraphicsPreset preset, int targetFrameRate, float fogQualityScale)
        {
            Preset = preset;
            TargetFrameRate = targetFrameRate;
            FogQualityScale = fogQualityScale;
        }
    }

    /// <summary>
    /// Event-driven Settings menu view managing Mechanic Diagnostic Hints, audio mix, graphics presets,
    /// and keyboard rebinding. Performs zero per-frame allocations (no Update loop).
    /// </summary>
    public sealed partial class SettingsMenuView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";
        private const string PresetLowLabel = "Low";
        private const string PresetMediumLabel = "Medium";
        private const string PresetHighLabel = "High";

        [Header("UI Controls")]
        [SerializeField] private Toggle _diagnosticHintsToggle;
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _engineVolumeSlider;
        [SerializeField] private Slider _foleyVolumeSlider;
        [SerializeField] private Slider _ambienceVolumeSlider;
        [SerializeField] private Text _graphicsPresetLabel;
        [SerializeField] private Text _conflictStatusLabel;

        private GameObject _modalRoot;
        private Transform _builtParent;
        private GameSettingsData _current = SettingsPreferencesStore.CreateDefaults();
        private DiagnosticHintsPresenter _hintsPresenter;
        private string _lastConflictAction = string.Empty;

        /// <summary>Raised when <see cref="Apply"/> commits the sanitized settings snapshot.</summary>
        public event Action<GameSettingsData> SettingsApplied;

        /// <summary>Raised when Mechanic Diagnostic Hints are toggled.</summary>
        public event Action<bool> DiagnosticHintsToggled;

        /// <summary>Raised when audio mix levels change.</summary>
        public event Action<AudioMixSettings> AudioMixChanged;

        /// <summary>Raised when graphics preset, target frame rate, or fog quality scale changes.</summary>
        public event Action<GraphicsQualitySettings> GraphicsPresetChanged;

        /// <summary>Raised when one or more keyboard bindings change.</summary>
        public event Action<GameSettingsData> KeyBindingsChanged;

        /// <summary>Raised when the player closes the Settings menu.</summary>
        public event Action CloseRequested;

        /// <summary>Current sanitized settings data held by this view.</summary>
        public GameSettingsData CurrentSettings
        {
            get
            {
                EnsureInitialized();
                return _current;
            }
        }

        /// <summary>Canonical action ID that conflicted on the most recent failed rebind (empty when none).</summary>
        public string LastConflictAction
        {
            get { return _lastConflictAction; }
        }

        /// <summary>Convenience readout of the current diagnostic hints flag.</summary>
        public bool DiagnosticHintsEnabled
        {
            get
            {
                EnsureInitialized();
                return _current.DiagnosticHintsEnabled;
            }
        }

        /// <summary>
        /// Builds a dark interactive Settings modal card with toggles, sliders, quality cycle,
        /// Reset, Apply, and Close controls under <paramref name="root"/>. Idempotent for the same parent.
        /// </summary>
        public void BuildDefaultLayout(RectTransform root)
        {
            RectTransform parent = root != null ? root : transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (_modalRoot != null && _builtParent == parent)
            {
                return;
            }

            if (_modalRoot != null)
            {
                DestroyObjectSafe(_modalRoot);
                _modalRoot = null;
            }

            EnsureInitialized();
            _builtParent = parent;
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _modalRoot = new GameObject("SettingsMenu_Modal", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _modalRoot.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)_modalRoot.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(540f, 460f);

            _modalRoot.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

            VerticalLayoutGroup vLayout = _modalRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(18, 18, 16, 16);
            vLayout.spacing = 10f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            CreateLabel(_modalRoot.transform, "Title", "SETTINGS", 22, TextAnchor.MiddleLeft, Color.white, 30f, font);

            _diagnosticHintsToggle = CreateToggleRow(_modalRoot.transform, "Toggle_DiagnosticHints", "Mechanic Diagnostic Hints", font);
            _diagnosticHintsToggle.onValueChanged.AddListener(SetDiagnosticHints);

            _masterVolumeSlider = CreateSliderRow(_modalRoot.transform, "Slider_MasterVolume", "Master Volume", font);
            _masterVolumeSlider.onValueChanged.AddListener(delegate(float val)
            {
                SetAudioMix(val, _current.EngineVolume, _current.FoleyVolume, _current.AmbienceVolume);
            });

            _engineVolumeSlider = CreateSliderRow(_modalRoot.transform, "Slider_EngineVolume", "Engine Volume", font);
            _engineVolumeSlider.onValueChanged.AddListener(delegate(float val)
            {
                SetAudioMix(_current.MasterVolume, val, _current.FoleyVolume, _current.AmbienceVolume);
            });

            _foleyVolumeSlider = CreateSliderRow(_modalRoot.transform, "Slider_FoleyVolume", "Foley Volume", font);
            _foleyVolumeSlider.onValueChanged.AddListener(delegate(float val)
            {
                SetAudioMix(_current.MasterVolume, _current.EngineVolume, val, _current.AmbienceVolume);
            });

            _ambienceVolumeSlider = CreateSliderRow(_modalRoot.transform, "Slider_AmbienceVolume", "Ambience Volume", font);
            _ambienceVolumeSlider.onValueChanged.AddListener(delegate(float val)
            {
                SetAudioMix(_current.MasterVolume, _current.EngineVolume, _current.FoleyVolume, val);
            });

            GameObject qualityRow = new GameObject("QualityRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            qualityRow.transform.SetParent(_modalRoot.transform, false);
            qualityRow.GetComponent<LayoutElement>().preferredHeight = 36f;
            HorizontalLayoutGroup qualityLayout = qualityRow.GetComponent<HorizontalLayoutGroup>();
            qualityLayout.spacing = 10f;
            qualityLayout.childControlWidth = true;
            qualityLayout.childControlHeight = true;
            qualityLayout.childForceExpandWidth = false;
            qualityLayout.childForceExpandHeight = true;

            Text qualityTitle = CreateLabel(qualityRow.transform, "QualityTitle", "Graphics Quality", 15, TextAnchor.MiddleLeft, Color.white, 32f, font);
            qualityTitle.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _graphicsPresetLabel = CreateLabel(qualityRow.transform, "GraphicsPresetLabel", GetPresetLabel(_current.GraphicsPreset), 15, TextAnchor.MiddleCenter, new Color(0.70f, 0.90f, 1f, 1f), 32f, font);
            _graphicsPresetLabel.GetComponent<LayoutElement>().preferredWidth = 100f;

            Button cycleQualityBtn = CreateButton(qualityRow.transform, "Btn_CycleQuality", "Cycle", new Color(0.22f, 0.40f, 0.62f, 1f), font, CycleGraphicsPreset);
            cycleQualityBtn.GetComponent<LayoutElement>().preferredWidth = 100f;

            _conflictStatusLabel = CreateLabel(_modalRoot.transform, "ConflictStatusLabel", _lastConflictAction, 13, TextAnchor.MiddleLeft, new Color(0.95f, 0.45f, 0.40f, 1f), 22f, font);

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_modalRoot.transform, false);
            footerGo.GetComponent<LayoutElement>().preferredHeight = 42f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            CreateButton(footerGo.transform, "Btn_ResetDefaults", "Reset Defaults", new Color(0.50f, 0.28f, 0.20f, 1f), font, ResetAllToDefaults);
            CreateButton(footerGo.transform, "Btn_Apply", "Apply", new Color(0.20f, 0.52f, 0.28f, 1f), font, Apply);
            CreateButton(footerGo.transform, "Btn_Close", "Close", new Color(0.26f, 0.27f, 0.32f, 1f), font, TriggerClose);

            BuildControlsSection(font);
            RefreshUiControls();
        }

        /// <summary>Cycles Low -> Medium -> High -> Low and emits <see cref="GraphicsPresetChanged"/>.</summary>
        public void CycleGraphicsPreset()
        {
            EnsureInitialized();
            GraphicsPreset next;
            switch (_current.GraphicsPreset)
            {
                case GraphicsPreset.Low: next = GraphicsPreset.Medium; break;
                case GraphicsPreset.Medium: next = GraphicsPreset.High; break;
                default: next = GraphicsPreset.Low; break;
            }

            SetGraphicsPreset(next, _current.TargetFrameRate, _current.FogQualityScale);
        }

        /// <summary>Raises <see cref="CloseRequested"/>.</summary>
        public void TriggerClose()
        {
            Action handler = CloseRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Binds the settings model and optional <see cref="DiagnosticHintsPresenter"/>, sanitizing values
        /// and synchronizing the presenter and UI controls.
        /// </summary>
        public void Bind(GameSettingsData settings, DiagnosticHintsPresenter hintsPresenter)
        {
            _current = settings != null
                ? SettingsPreferencesStore.Sanitize(settings)
                : SettingsPreferencesStore.CreateDefaults();
            _hintsPresenter = hintsPresenter;
            _lastConflictAction = string.Empty;

            SyncHintsPresenter();
            RefreshUiControls();
        }

        /// <summary>Toggles Mechanic Diagnostic Hints, updates the bound presenter, and emits <see cref="DiagnosticHintsToggled"/>.</summary>
        public void SetDiagnosticHints(bool enabled)
        {
            EnsureInitialized();
            _current.DiagnosticHintsEnabled = enabled;
            SyncHintsPresenter();
            if (_diagnosticHintsToggle != null)
            {
                _diagnosticHintsToggle.SetIsOnWithoutNotify(enabled);
            }

            Action<bool> handler = DiagnosticHintsToggled;
            if (handler != null)
            {
                handler(enabled);
            }
        }

        /// <summary>Updates and clamps audio mix sliders, then emits <see cref="AudioMixChanged"/>.</summary>
        public void SetAudioMix(float master, float engine, float foley, float ambience)
        {
            EnsureInitialized();
            _current.MasterVolume = master;
            _current.EngineVolume = engine;
            _current.FoleyVolume = foley;
            _current.AmbienceVolume = ambience;
            SettingsPreferencesStore.Sanitize(_current);

            RefreshAudioSliders();
            EmitAudioMixChanged();
        }

        /// <summary>Updates and clamps graphics preset, target frame rate, and fog quality scale, then emits <see cref="GraphicsPresetChanged"/>.</summary>
        public void SetGraphicsPreset(GraphicsPreset preset, int targetFps, float fogScale)
        {
            EnsureInitialized();
            _current.GraphicsPreset = preset;
            _current.TargetFrameRate = targetFps;
            _current.FogQualityScale = fogScale;
            SettingsPreferencesStore.Sanitize(_current);

            RefreshGraphicsLabel();
            EmitGraphicsPresetChanged();
        }

        /// <summary>
        /// Attempts to rebind <paramref name="actionId"/> to <paramref name="newKey"/>.
        /// Rejects duplicate keys with <paramref name="conflict"/> set to the conflicting action ID; emits <see cref="KeyBindingsChanged"/> only on success.
        /// </summary>
        public bool TryRebindAction(string actionId, string newKey, out string conflict)
        {
            EnsureInitialized();
            bool rebound = SettingsPreferencesStore.TrySetKeyBinding(_current, actionId, newKey, out conflict);
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

        /// <summary>
        /// Resets all settings to defaults in place, syncs the bound presenter and UI controls,
        /// and notifies category listeners.
        /// </summary>
        public void ResetAllToDefaults()
        {
            EnsureInitialized();
            SettingsPreferencesStore.CopyDefaultsInto(_current);
            _lastConflictAction = string.Empty;
            CancelCapture();

            SyncHintsPresenter();
            RefreshUiControls();

            Action<bool> hintsHandler = DiagnosticHintsToggled;
            if (hintsHandler != null)
            {
                hintsHandler(_current.DiagnosticHintsEnabled);
            }

            EmitAudioMixChanged();
            EmitGraphicsPresetChanged();
            EmitKeyBindingsChanged();
        }

        /// <summary>Sanitizes current settings, syncs the bound presenter, and emits <see cref="SettingsApplied"/>.</summary>
        public void Apply()
        {
            EnsureInitialized();
            SettingsPreferencesStore.Sanitize(_current);
            SyncHintsPresenter();
            RefreshUiControls();

            Action<GameSettingsData> handler = SettingsApplied;
            if (handler != null)
            {
                handler(_current);
            }
        }

        private void EnsureInitialized()
        {
            if (_current == null)
            {
                _current = SettingsPreferencesStore.CreateDefaults();
            }
        }

        private void SyncHintsPresenter()
        {
            if (_hintsPresenter != null)
            {
                _hintsPresenter.HintsEnabled = _current.DiagnosticHintsEnabled;
            }
        }

        private void RefreshUiControls()
        {
            if (_diagnosticHintsToggle != null)
            {
                _diagnosticHintsToggle.SetIsOnWithoutNotify(_current.DiagnosticHintsEnabled);
            }

            RefreshAudioSliders();
            RefreshGraphicsLabel();
            SetConflictText(_lastConflictAction);
            RefreshControlsUi();
        }

        private void RefreshAudioSliders()
        {
            if (_masterVolumeSlider != null) { _masterVolumeSlider.SetValueWithoutNotify(_current.MasterVolume); }
            if (_engineVolumeSlider != null) { _engineVolumeSlider.SetValueWithoutNotify(_current.EngineVolume); }
            if (_foleyVolumeSlider != null) { _foleyVolumeSlider.SetValueWithoutNotify(_current.FoleyVolume); }
            if (_ambienceVolumeSlider != null) { _ambienceVolumeSlider.SetValueWithoutNotify(_current.AmbienceVolume); }
        }

        private void RefreshGraphicsLabel()
        {
            if (_graphicsPresetLabel != null)
            {
                _graphicsPresetLabel.text = GetPresetLabel(_current.GraphicsPreset);
            }
        }

        private void SetConflictText(string conflictAction)
        {
            if (_conflictStatusLabel != null)
            {
                _conflictStatusLabel.text = conflictAction ?? string.Empty;
            }
        }

        private void EmitAudioMixChanged()
        {
            Action<AudioMixSettings> handler = AudioMixChanged;
            if (handler != null)
            {
                handler(new AudioMixSettings(
                    _current.MasterVolume,
                    _current.EngineVolume,
                    _current.FoleyVolume,
                    _current.AmbienceVolume));
            }
        }

        private void EmitGraphicsPresetChanged()
        {
            Action<GraphicsQualitySettings> handler = GraphicsPresetChanged;
            if (handler != null)
            {
                handler(new GraphicsQualitySettings(
                    _current.GraphicsPreset,
                    _current.TargetFrameRate,
                    _current.FogQualityScale));
            }
        }

        private void EmitKeyBindingsChanged()
        {
            Action<GameSettingsData> handler = KeyBindingsChanged;
            if (handler != null)
            {
                handler(_current);
            }
        }

        private static string GetPresetLabel(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.Low: return PresetLowLabel;
                case GraphicsPreset.High: return PresetHighLabel;
                default: return PresetMediumLabel;
            }
        }

        private static Toggle CreateToggleRow(Transform parent, string name, string label, Font font)
        {
            GameObject rowGo = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().preferredHeight = 32f;
            HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 10f;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;

            GameObject boxGo = new GameObject("ToggleBox", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
            boxGo.transform.SetParent(rowGo.transform, false);
            boxGo.GetComponent<LayoutElement>().preferredWidth = 28f;
            Image boxBg = boxGo.GetComponent<Image>();
            boxBg.color = new Color(0.24f, 0.27f, 0.34f, 1f);

            GameObject checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(boxGo.transform, false);
            RectTransform checkRt = (RectTransform)checkGo.transform;
            checkRt.anchorMin = new Vector2(0.2f, 0.2f);
            checkRt.anchorMax = new Vector2(0.8f, 0.8f);
            checkRt.sizeDelta = Vector2.zero;
            Image checkImg = checkGo.GetComponent<Image>();
            checkImg.color = new Color(0.30f, 0.82f, 0.42f, 1f);

            Toggle toggle = boxGo.GetComponent<Toggle>();
            toggle.targetGraphic = boxBg;
            toggle.graphic = checkImg;

            Text text = CreateLabel(rowGo.transform, "Label", label, 15, TextAnchor.MiddleLeft, Color.white, 28f, font);
            text.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return toggle;
        }

        private static Slider CreateSliderRow(Transform parent, string name, string label, Font font)
        {
            GameObject rowGo = new GameObject(name + "_Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().preferredHeight = 30f;
            HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 10f;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;

            Text title = CreateLabel(rowGo.transform, "Label", label, 14, TextAnchor.MiddleLeft, Color.white, 28f, font);
            title.GetComponent<LayoutElement>().preferredWidth = 150f;

            GameObject sliderGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Slider), typeof(LayoutElement));
            sliderGo.transform.SetParent(rowGo.transform, false);
            sliderGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Image trackBg = sliderGo.GetComponent<Image>();
            trackBg.color = new Color(0.20f, 0.22f, 0.28f, 1f);

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(sliderGo.transform, false);
            RectTransform fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            fillGo.GetComponent<Image>().color = new Color(0.28f, 0.62f, 0.86f, 1f);

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.fillRect = fillRt;
            slider.targetGraphic = trackBg;
            return slider;
        }

        private static Text CreateLabel(
            Transform parent,
            string name,
            string text,
            int fontSize,
            TextAnchor alignment,
            Color color,
            float height,
            Font font)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            Text label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.text = text ?? string.Empty;
            return label;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color color,
            Font font,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            bg.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
            return btn;
        }

        private static void DestroyObjectSafe(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
