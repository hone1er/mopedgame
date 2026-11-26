using System;
using System.Text;
using UnityEngine;
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

        [SerializeField] private Text _statusText;
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
            dockRt.sizeDelta = new Vector2(-24f, 108f);

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

            // Bottom button row.
            GameObject rowGo = new GameObject("DockButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(_dockRoot.transform, false);
            LayoutElement rowLe = rowGo.GetComponent<LayoutElement>();
            rowLe.preferredHeight = 52f;
            rowLe.minHeight = 44f;
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

            CreateDockButton(_buttonRowRoot, "Btn_Ride", "Ride", primaryRide, font, TriggerRide);
            CreateDockButton(_buttonRowRoot, "Btn_Shop", "Shop", stationColor, font, TriggerOpenShop);
            CreateDockButton(_buttonRowRoot, "Btn_SwapMeet", "Swap Meet", stationColor, font, TriggerOpenSwapMeet);
            CreateDockButton(_buttonRowRoot, "Btn_Jobs", "Jobs", stationColor, font, TriggerOpenJobs);
            CreateDockButton(_buttonRowRoot, "Btn_Paint", "Paint", stationColor, font, TriggerCyclePaint);
            CreateDockButton(_buttonRowRoot, "Btn_DynoPull", "Dyno Pull", stationColor, font, TriggerDynoPull);
            CreateDockButton(_buttonRowRoot, "Btn_PlugChop", "Plug Chop", stationColor, font, TriggerPlugChop);
            CreateDockButton(_buttonRowRoot, "Btn_Refuel", "Refuel", stationColor, font, TriggerRefuel);
            CreateDockButton(_buttonRowRoot, "Btn_SwapJet", "Swap Jet", stationColor, font, TriggerSwapJet);
            CreateDockButton(_buttonRowRoot, "Btn_Save", "Save", utilityColor, font, TriggerSave);
            CreateDockButton(_buttonRowRoot, "Btn_Settings", "Settings", utilityColor, font, TriggerOpenSettings);
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
