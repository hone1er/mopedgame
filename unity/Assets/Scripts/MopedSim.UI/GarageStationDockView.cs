using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Bottom garage action dock providing clickable buttons for every garage station and modal,
    /// plus a dirty-checked status readout strip. Strictly decoupled from Garage/App assemblies:
    /// emits Action events only.
    /// </summary>
    public sealed class GarageStationDockView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        /// <summary>Shows a per-button hint line on pointer hover / keyboard-gamepad selection (also for disabled buttons).</summary>
        private sealed class DockHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
        {
            public GarageStationDockView Owner;
            public string Hint = string.Empty;

            public void OnPointerEnter(PointerEventData eventData) { Owner.SetHint(Hint); }
            public void OnPointerExit(PointerEventData eventData) { Owner.SetHint(string.Empty); }
            public void OnSelect(BaseEventData eventData) { Owner.SetHint(Hint); }
            public void OnDeselect(BaseEventData eventData) { Owner.SetHint(string.Empty); }
        }

        public const string DefaultHint = "Hover a station button for what it does. [F1/Tab] Ride";

        [SerializeField] private Text _statusText;
        [SerializeField] private Text _hintText;
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>(16);
        private readonly Dictionary<string, Text> _buttonLabels = new Dictionary<string, Text>(16);
        private readonly Dictionary<string, DockHint> _buttonHints = new Dictionary<string, DockHint>(16);
        private string _displayedHint = string.Empty;
        [SerializeField] private RectTransform _buttonRowRoot;

        private readonly StringBuilder _statusBuilder = new StringBuilder(160);
        private GameObject _dockRoot;
        private Transform _builtParent;

        private bool _hasRenderedStatus;
        private decimal _lastCash;
        private int _lastRep;
        private string _lastJetId = string.Empty;
        private int _lastFuelPctInt = int.MinValue;
        private int _lastDynoTenths = int.MinValue;
        private int _lastPeakRpmInt = int.MinValue;
        private string _lastFeedback = string.Empty;
        private string _displayedStatusText = string.Empty;

        public event Action RideRequested;
        public event Action OpenShopRequested;
        public event Action OpenSwapMeetRequested;
        public event Action OpenJobsRequested;
        public event Action OpenSettingsRequested;
        public event Action CyclePaintRequested;
        public event Action DynoPullRequested;
        public event Action PlugChopRequested;
        public event Action RefuelRequested;
        public event Action SwapJetRequested;
        public event Action SaveRequested;
        public event Action CycleMixRatioRequested;
        public event Action OpenInventoryRequested;

        public string DisplayedStatusText
        {
            get { return _displayedStatusText; }
        }

        public string LastFeedback
        {
            get { return _lastFeedback; }
        }

        public void TriggerRide() { InvokeEvent(RideRequested); }
        public void TriggerOpenShop() { InvokeEvent(OpenShopRequested); }
        public void TriggerOpenSwapMeet() { InvokeEvent(OpenSwapMeetRequested); }
        public void TriggerOpenJobs() { InvokeEvent(OpenJobsRequested); }
        public void TriggerOpenSettings() { InvokeEvent(OpenSettingsRequested); }
        public void TriggerCyclePaint() { InvokeEvent(CyclePaintRequested); }
        public void TriggerDynoPull() { InvokeEvent(DynoPullRequested); }
        public void TriggerPlugChop() { InvokeEvent(PlugChopRequested); }
        public void TriggerRefuel() { InvokeEvent(RefuelRequested); }
        public void TriggerSwapJet() { InvokeEvent(SwapJetRequested); }
        public void TriggerSave() { InvokeEvent(SaveRequested); }
        public void TriggerCycleMixRatio() { InvokeEvent(CycleMixRatioRequested); }
        public void TriggerOpenInventory() { InvokeEvent(OpenInventoryRequested); }

        /// <summary>Hint line currently shown under the status strip.</summary>
        public string DisplayedHint { get { return _displayedHint; } }

        /// <summary>Returns a dock button by name (e.g. "Btn_DynoPull"), or null.</summary>
        public Button GetButton(string buttonName)
        {
            Button b;
            return buttonName != null && _buttons.TryGetValue(buttonName, out b) ? b : null;
        }

        /// <summary>Enables / disables a station button; a non-null <paramref name="hint"/> replaces the hover hint (use "coming soon" text when disabled), null keeps it.</summary>
        public void SetButtonState(string buttonName, bool interactable, string hint)
        {
            Button b = GetButton(buttonName);
            if (b != null) { b.interactable = interactable; }
            DockHint h;
            if (hint != null && buttonName != null && _buttonHints.TryGetValue(buttonName, out h)) { h.Hint = hint; }
        }

        /// <summary>Changes the station caption + action label of a dock button (event-driven; allocates the formatted string).</summary>
        public void SetButtonLabel(string buttonName, string station, string action)
        {
            Text t;
            if (buttonName != null && _buttonLabels.TryGetValue(buttonName, out t)) { t.text = FormatLabel(station, action); }
        }

        /// <summary>Sets the hint line (blank falls back to the default hint).</summary>
        public void SetHint(string hint)
        {
            string text = string.IsNullOrEmpty(hint) ? DefaultHint : hint;
            if (string.Equals(text, _displayedHint, StringComparison.Ordinal)) { return; }
            _displayedHint = text;
            if (_hintText != null) { _hintText.text = text; }
        }

        private static string FormatLabel(string station, string action)
        {
            return "<size=11><color=#9FB0C0>" + station + "</color></size>\n" + action;
        }

        /// <summary>
        /// Builds the bottom translucent garage station dock bar under <paramref name="root"/>.
        /// Idempotent: repeated calls for the same root reuse the existing hierarchy.
        /// </summary>
        public void BuildDefaultLayout(RectTransform root)
        {
            RectTransform parent = root != null ? root : transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (_dockRoot != null && _builtParent == parent)
            {
                return;
            }

            if (_dockRoot != null)
            {
                DestroyObjectSafe(_dockRoot);
                _dockRoot = null;
            }

            _builtParent = parent;
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _dockRoot = new GameObject("GarageStationDock_Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _dockRoot.transform.SetParent(parent, false);
            RectTransform dockRt = (RectTransform)_dockRoot.transform;
            dockRt.anchorMin = new Vector2(0f, 0f);
            dockRt.anchorMax = new Vector2(1f, 0f);
            dockRt.pivot = new Vector2(0.5f, 0f);
            dockRt.anchoredPosition = new Vector2(0f, 8f);
            dockRt.sizeDelta = new Vector2(-24f, 140f);

            Image dockBg = _dockRoot.GetComponent<Image>();
            dockBg.color = new Color(0.08f, 0.09f, 0.11f, 0.88f);

            VerticalLayoutGroup vLayout = _dockRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(12, 12, 8, 8);
            vLayout.spacing = 6f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            // Top status readout bar.
            GameObject statusGo = new GameObject("DockStatusText", typeof(RectTransform), typeof(LayoutElement));
            statusGo.transform.SetParent(_dockRoot.transform, false);
            LayoutElement statusLe = statusGo.GetComponent<LayoutElement>();
            statusLe.preferredHeight = 28f;
            statusLe.minHeight = 24f;
            Text statusText = statusGo.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 16;
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.color = new Color(0.92f, 0.94f, 0.96f, 1f);
            statusText.raycastTarget = false;
            statusText.text = _displayedStatusText;
            _statusText = statusText;

            // Hint line (hover / focus tooltip, including "coming soon" notes for disabled stations).
            GameObject hintGo = new GameObject("DockHintText", typeof(RectTransform), typeof(LayoutElement));
            hintGo.transform.SetParent(_dockRoot.transform, false);
            LayoutElement hintLe = hintGo.GetComponent<LayoutElement>();
            hintLe.preferredHeight = 22f;
            hintLe.minHeight = 20f;
            Text hintText = hintGo.AddComponent<Text>();
            hintText.font = font;
            hintText.fontSize = 14;
            hintText.alignment = TextAnchor.MiddleLeft;
            hintText.color = new Color(0.98f, 0.82f, 0.38f, 1f);
            hintText.raycastTarget = false;
            hintText.text = DefaultHint;
            _hintText = hintText;
            _displayedHint = DefaultHint;

            // Bottom button row.
            GameObject rowGo = new GameObject("DockButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(_dockRoot.transform, false);
            LayoutElement rowLe = rowGo.GetComponent<LayoutElement>();
            rowLe.preferredHeight = 58f;
            rowLe.minHeight = 50f;
            HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 6f;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = true;
            hLayout.childForceExpandHeight = true;
            _buttonRowRoot = (RectTransform)rowGo.transform;

            Color primaryRide = new Color(0.18f, 0.56f, 0.28f, 1f);
            Color stationColor = new Color(0.20f, 0.24f, 0.30f, 1f);
            Color utilityColor = new Color(0.26f, 0.24f, 0.20f, 1f);

            _buttons.Clear();
            _buttonLabels.Clear();
            _buttonHints.Clear();
            AddButton("Btn_Ride", "GO", "Ride", primaryRide, font, TriggerRide, "Take the bike out. [F1 / Tab]");
            AddButton("Btn_PlugChop", "WORKSTAND", "Plug Chop", stationColor, font, TriggerPlugChop, "Pull the plug and read its colour (see the SPARK PLUG READING panel). Colour builds while the engine runs - dyno pull first.");
            AddButton("Btn_Paint", "WORKSTAND", "Paint", stationColor, font, TriggerCyclePaint, "Cycle the paint-booth swatches. [F4]");
            AddButton("Btn_DynoPull", "DYNO", "Dyno Pull", stationColor, font, TriggerDynoPull, "Run the bike flat out on the roller: power, AFR and head temp. Uses fuel and heats the engine.");
            AddButton("Btn_MixRatio", "PREMIX", "Mix 50:1", stationColor, font, TriggerCycleMixRatio, "Pick the fuel : oil ratio for the next refuel (32:1, 40:1, 50:1 or straight gas).");
            AddButton("Btn_Refuel", "PREMIX", "Refuel", stationColor, font, TriggerRefuel, "Mix a 5 L can at the selected ratio and pour it into the tank.");
            AddButton("Btn_SwapJet", "WORKBENCH", "Swap Jet", stationColor, font, TriggerSwapJet, "Fit the next main jet size from the parts crate. Re-check AFR on the dyno.");
            AddButton("Btn_Inventory", "WORKBENCH", "Owned Parts", stationColor, font, TriggerOpenInventory, "Coming soon: install owned parts from here. For now swap parts at the workstand.");
            _buttons["Btn_Inventory"].interactable = false;
            AddButton("Btn_Shop", "SHOP", "Shop", stationColor, font, TriggerOpenShop, "Buy parts and tools. [F3]");
            AddButton("Btn_SwapMeet", "SHOP", "Swap Meet", stationColor, font, TriggerOpenSwapMeet, "Used parts, rotating stock.");
            AddButton("Btn_Jobs", "JOBS", "Jobs", stationColor, font, TriggerOpenJobs, "Courier job board.");
            AddButton("Btn_Save", "GAME", "Save", utilityColor, font, TriggerSave, "Write the save file.");
            AddButton("Btn_Settings", "GAME", "Settings", utilityColor, font, TriggerOpenSettings, "Controls, audio, graphics. [F2]");
        }

        /// <summary>
        /// Updates the dock status bar. Dirty-checked so identical consecutive frames skip formatting and allocate 0 B GC.
        /// </summary>
        public void RenderStatus(
            decimal cashUsd,
            int rep,
            string activeJetId,
            float fuelPct,
            float dynoHp,
            float peakRpm,
            string lastFeedback)
        {
            string safeJet = activeJetId ?? string.Empty;
            string safeFeedback = lastFeedback ?? string.Empty;
            float clampedFuel = (float.IsNaN(fuelPct) || float.IsInfinity(fuelPct)) ? 0f : Mathf.Clamp(fuelPct, 0f, 100f);
            float safeDyno = (float.IsNaN(dynoHp) || float.IsInfinity(dynoHp) || dynoHp < 0f) ? 0f : dynoHp;
            float safeRpm = (float.IsNaN(peakRpm) || float.IsInfinity(peakRpm) || peakRpm < 0f) ? 0f : peakRpm;

            int fuelPctInt = Mathf.RoundToInt(clampedFuel);
            int dynoTenths = Mathf.RoundToInt(safeDyno * 10f);
            int peakRpmInt = Mathf.RoundToInt(safeRpm);

            if (_hasRenderedStatus
                && cashUsd == _lastCash
                && rep == _lastRep
                && fuelPctInt == _lastFuelPctInt
                && dynoTenths == _lastDynoTenths
                && peakRpmInt == _lastPeakRpmInt
                && string.Equals(safeJet, _lastJetId, StringComparison.Ordinal)
                && string.Equals(safeFeedback, _lastFeedback, StringComparison.Ordinal))
            {
                return;
            }

            _hasRenderedStatus = true;
            _lastCash = cashUsd;
            _lastRep = rep;
            _lastJetId = safeJet;
            _lastFuelPctInt = fuelPctInt;
            _lastDynoTenths = dynoTenths;
            _lastPeakRpmInt = peakRpmInt;
            _lastFeedback = safeFeedback;

            _statusBuilder.Length = 0;
            _statusBuilder.Append("Cash: $").Append(cashUsd.ToString("F2"));
            _statusBuilder.Append("  |  Rep: ").Append(rep);
            _statusBuilder.Append("  |  Jet: ").Append(safeJet.Length > 0 ? safeJet : "--");
            _statusBuilder.Append("  |  Fuel: ").Append(fuelPctInt).Append('%');
            _statusBuilder.Append("  |  Dyno: ").Append(dynoTenths / 10).Append('.').Append(dynoTenths % 10).Append(" hp @ ").Append(peakRpmInt).Append(" rpm");
            if (safeFeedback.Length > 0)
            {
                _statusBuilder.Append("  |  ").Append(safeFeedback);
            }

            _displayedStatusText = _statusBuilder.ToString();
            if (_statusText != null)
            {
                _statusText.text = _displayedStatusText;
            }
        }

        private void AddButton(string name, string station, string action, Color color, Font font, UnityEngine.Events.UnityAction onClick, string hint)
        {
            Button btn = CreateDockButton(_buttonRowRoot, name, FormatLabel(station, action), color, font, onClick);
            _buttons[name] = btn;
            _buttonLabels[name] = btn.GetComponentInChildren<Text>();
            DockHint h = btn.gameObject.AddComponent<DockHint>();
            h.Owner = this;
            h.Hint = hint;
            _buttonHints[name] = h;
        }

        private static Button CreateDockButton(
            RectTransform parent,
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
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            GameObject textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 15;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.text = label;
            return btn;
        }

        private static void InvokeEvent(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
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
