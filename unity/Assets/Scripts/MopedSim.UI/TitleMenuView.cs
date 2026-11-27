using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>One selectable starting bike on the New Game page (all strings pre-formatted from catalog data by the caller).</summary>
    public sealed class TitleArchetypeOption
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public string PriceText = string.Empty;
        public string StatsText = string.Empty;
    }

    /// <summary>
    /// Runtime-built title screen: game name, Continue (with save summary; disabled without a valid save), New Game ->
    /// archetype picker, Settings, Quit. Keyboard/gamepad navigation uses the scene EventSystem (arrows/d-pad, Enter/A).
    /// Event-driven only: no Update loop, no per-frame allocations.
    /// </summary>
    public sealed class TitleMenuView : MonoBehaviour
    {
        public const string GameTitle = "MOPEDGAME";
        public const string Tagline = "Two-stroke riding, wrenching and hustling in the Bay";

        private static readonly Color Backdrop = new Color(0.05f, 0.06f, 0.09f, 1f);
        private static readonly Color CardColor = new Color(0.10f, 0.11f, 0.15f, 0.97f);
        private static readonly Color Accent = new Color(0.95f, 0.62f, 0.18f, 1f);

        private RectTransform _backdrop;
        private RectTransform _mainCard;
        private RectTransform _pickerCard;
        private Button _continueButton;
        private Text _summaryLabel;
        private Button _newGameButton;
        private Button _backButton;
        private readonly List<Button> _archetypeButtons = new List<Button>(4);
        private readonly List<string> _archetypeIds = new List<string>(4);
        private Transform _pickerRow;
        private Font _font;
        private bool _pickerOpen;

        public event Action ContinueRequested;
        public event Action<string> NewGameRequested;
        public event Action SettingsRequested;
        public event Action QuitRequested;

        public bool IsBuilt { get { return _backdrop != null; } }
        public bool IsOpen { get { return _backdrop != null && _backdrop.gameObject.activeSelf; } }
        public bool IsPickerOpen { get { return _pickerOpen; } }
        public bool ContinueEnabled { get { return _continueButton != null && _continueButton.interactable; } }
        public int ArchetypeCount { get { return _archetypeIds.Count; } }
        public string ContinueSummaryText { get { return _summaryLabel != null ? _summaryLabel.text : string.Empty; } }

        public void Build(Transform parent)
        {
            if (_backdrop != null)
            {
                return;
            }

            _font = MenuWidgets.LoadFont();
            _backdrop = MenuWidgets.CreateOverlayCanvas(parent, "TitleMenu_Canvas", 200, Backdrop);

            Text name = MenuWidgets.CreateLabel(_backdrop, "GameName", GameTitle, 96, TextAnchor.MiddleCenter, Accent, 120f, _font);
            MenuWidgets.PinCentered(name.rectTransform, new Vector2(0f, 360f), new Vector2(1400f, 120f));
            Text tag = MenuWidgets.CreateLabel(_backdrop, "Tagline", Tagline, 26, TextAnchor.MiddleCenter, new Color(0.75f, 0.78f, 0.85f, 1f), 36f, _font);
            MenuWidgets.PinCentered(tag.rectTransform, new Vector2(0f, 280f), new Vector2(1400f, 36f));

            _mainCard = MenuWidgets.CreateCard(_backdrop, "MainCard", new Vector2(520f, 460f), CardColor, 24, 12f);
            _mainCard.anchoredPosition = new Vector2(0f, -60f);
            _continueButton = MenuWidgets.CreateButton(_mainCard, "Btn_Continue", "Continue", new Color(0.20f, 0.52f, 0.28f, 1f), 28, 64f, _font, OnContinue, out Text _);
            _summaryLabel = MenuWidgets.CreateLabel(_mainCard, "ContinueSummary", string.Empty, 18, TextAnchor.MiddleCenter, new Color(0.80f, 0.85f, 0.92f, 1f), 48f, _font);
            _newGameButton = MenuWidgets.CreateButton(_mainCard, "Btn_NewGame", "New Game", new Color(0.22f, 0.40f, 0.62f, 1f), 28, 64f, _font, ShowPicker, out Text _);
            MenuWidgets.CreateButton(_mainCard, "Btn_Settings", "Settings", new Color(0.26f, 0.27f, 0.32f, 1f), 28, 64f, _font, OnSettings, out Text _);
            MenuWidgets.CreateButton(_mainCard, "Btn_Quit", "Quit", new Color(0.50f, 0.28f, 0.20f, 1f), 28, 64f, _font, OnQuit, out Text _);

            _pickerCard = MenuWidgets.CreateCard(_backdrop, "PickerCard", new Vector2(1500f, 560f), CardColor, 24, 16f);
            _pickerCard.anchoredPosition = new Vector2(0f, -70f);
            MenuWidgets.CreateLabel(_pickerCard, "PickerTitle", "CHOOSE YOUR BIKE", 30, TextAnchor.MiddleCenter, Color.white, 44f, _font);
            GameObject row = new GameObject("ArchetypeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_pickerCard, false);
            row.GetComponent<LayoutElement>().preferredHeight = 400f;
            HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            _pickerRow = row.transform;
            _backButton = MenuWidgets.CreateButton(_pickerCard, "Btn_Back", "Back", new Color(0.26f, 0.27f, 0.32f, 1f), 24, 52f, _font, delegate { HandleBack(); }, out Text _);

            _pickerCard.gameObject.SetActive(false);
            _backdrop.gameObject.SetActive(false);
            SetContinueSummary(false, string.Empty);
        }

        /// <summary>Rebuilds the archetype cards (event-driven; may allocate).</summary>
        public void SetArchetypes(IReadOnlyList<TitleArchetypeOption> options)
        {
            if (_pickerRow == null)
            {
                return;
            }

            for (int i = 0; i < _archetypeButtons.Count; i++)
            {
                if (_archetypeButtons[i] != null)
                {
                    Destroy(_archetypeButtons[i].gameObject);
                }
            }

            _archetypeButtons.Clear();
            _archetypeIds.Clear();
            int count = options != null ? options.Count : 0;
            for (int i = 0; i < count; i++)
            {
                TitleArchetypeOption o = options[i];
                string id = o.Id;
                string text = "<size=34><b>" + o.DisplayName + "</b></size>\n<color=#F2A22E><size=26>" + o.PriceText + "</size></color>\n\n"
                    + "<size=18>" + o.StatsText + "</size>\n\n<size=18><color=#C8CEDA>" + o.Description + "</color></size>";
                Button b = MenuWidgets.CreateButton(_pickerRow, "Archetype_" + id, text, new Color(0.22f, 0.40f, 0.62f, 1f), 20, 400f, _font, delegate { OnArchetypeChosen(id); }, out Text label);
                label.alignment = TextAnchor.UpperLeft;
                _archetypeButtons.Add(b);
                _archetypeIds.Add(id);
            }
        }

        public void SetContinueSummary(bool available, string summary)
        {
            if (_continueButton == null)
            {
                return;
            }

            _continueButton.interactable = available;
            _summaryLabel.text = available ? (summary ?? string.Empty) : "No saved game found";
        }

        public void Show()
        {
            if (_backdrop == null)
            {
                return;
            }

            _backdrop.gameObject.SetActive(true);
            ShowMain();
        }

        public void Hide()
        {
            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>Hides/shows the menu cards while keeping the backdrop (used while the Settings panel is layered on top).</summary>
        public void SetCardsVisible(bool visible)
        {
            if (_mainCard == null)
            {
                return;
            }

            _mainCard.gameObject.SetActive(visible && !_pickerOpen);
            _pickerCard.gameObject.SetActive(visible && _pickerOpen);
            if (visible)
            {
                FocusDefault();
            }
        }

        public void ShowMain()
        {
            _pickerOpen = false;
            _mainCard.gameObject.SetActive(true);
            _pickerCard.gameObject.SetActive(false);
            FocusDefault();
        }

        public void ShowPicker()
        {
            _pickerOpen = true;
            _mainCard.gameObject.SetActive(false);
            _pickerCard.gameObject.SetActive(true);
            FocusDefault();
        }

        /// <summary>Cancel/back: closes the archetype picker. Returns true when it consumed the press.</summary>
        public bool HandleBack()
        {
            if (!_pickerOpen)
            {
                return false;
            }

            ShowMain();
            return true;
        }

        /// <summary>Picks an archetype card by catalog id (what clicking/Enter on the card does).</summary>
        public void ChooseArchetype(string archetypeId)
        {
            OnArchetypeChosen(archetypeId);
        }

        public void PressContinue()
        {
            OnContinue();
        }

        private void FocusDefault()
        {
            if (_pickerOpen)
            {
                MenuWidgets.Focus(_archetypeButtons.Count > 0 ? (Selectable)_archetypeButtons[0] : _backButton);
            }
            else
            {
                MenuWidgets.Focus(_continueButton.interactable ? _continueButton : _newGameButton);
            }
        }

        private void OnContinue()
        {
            Action h = ContinueRequested;
            if (h != null && _continueButton.interactable)
            {
                h();
            }
        }

        private void OnArchetypeChosen(string id)
        {
            Action<string> h = NewGameRequested;
            if (h != null)
            {
                h(id);
            }
        }

        private void OnSettings()
        {
            Action h = SettingsRequested;
            if (h != null)
            {
                h();
            }
        }

        private void OnQuit()
        {
            Action h = QuitRequested;
            if (h != null)
            {
                h();
            }
        }
    }
}
