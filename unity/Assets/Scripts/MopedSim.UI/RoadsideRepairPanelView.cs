using System;
using System.Collections.Generic;
using System.Text;
using MopedSim.Core;
using MopedSim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Interactive roadside breakdown repair card. Displays active faults from Core, required tools/spares
    /// vs. the packed Tool Roll, per-fault Repair buttons, Buddy Tow, and Resume Ride.
    /// Emits Action events only; App executes Core repairs and recovery.
    /// </summary>
    public sealed class RoadsideRepairPanelView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private sealed class FaultRowEntry
        {
            public GameObject Root;
            public Text InfoText;
            public Button RepairButton;
            public Text ButtonLabel;
            public string BoundFaultId = string.Empty;
        }

        [SerializeField] private Text _titleText;
        [SerializeField] private Text _summaryText;
        [SerializeField] private RectTransform _faultListRoot;
        [SerializeField] private Button _buddyTowButton;
        [SerializeField] private Button _resumeRideButton;

        private readonly List<FaultRowEntry> _rowPool = new List<FaultRowEntry>(8);
        private readonly List<string> _cachedFaultIds = new List<string>(8);
        private readonly List<bool> _cachedRepairable = new List<bool>(8);
        private readonly List<string> _cachedToolRoll = new List<string>(10);
        private readonly StringBuilder _sb = new StringBuilder(192);

        private GameObject _panelRoot;
        private Transform _builtParent;
        private Font _font;
        private bool _hasRendered;
        private int _displayedFaultCount;
        private string _displayedSummaryText = string.Empty;

        public event Action<string> RepairFaultRequested;
        public event Action BuddyTowRequested;
        public event Action ResumeRideRequested;

        public int DisplayedFaultCount
        {
            get { return _displayedFaultCount; }
        }

        public string DisplayedSummaryText
        {
            get { return _displayedSummaryText; }
        }

        public void TriggerRepairFault(string faultId)
        {
            if (string.IsNullOrEmpty(faultId))
            {
                return;
            }

            Action<string> handler = RepairFaultRequested;
            if (handler != null)
            {
                handler(faultId);
            }
        }

        public void TriggerBuddyTow()
        {
            Action handler = BuddyTowRequested;
            if (handler != null)
            {
                handler();
            }
        }

        public void TriggerResumeRide()
        {
            Action handler = ResumeRideRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Builds a centered dark roadside repair card under <paramref name="root"/>.
        /// Idempotent for the same parent RectTransform.
        /// </summary>
        public void BuildDefaultLayout(RectTransform root)
        {
            RectTransform parent = root != null ? root : transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (_panelRoot != null && _builtParent == parent)
            {
                return;
            }

            if (_panelRoot != null)
            {
                DestroyObjectSafe(_panelRoot);
                _panelRoot = null;
                _rowPool.Clear();
            }

            _builtParent = parent;
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _panelRoot = new GameObject("RoadsideRepair_Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _panelRoot.transform.SetParent(parent, false);
            RectTransform cardRt = (RectTransform)_panelRoot.transform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = Vector2.zero;
            cardRt.sizeDelta = new Vector2(620f, 440f);

            Image bg = _panelRoot.GetComponent<Image>();
            bg.color = new Color(0.09f, 0.10f, 0.12f, 0.94f);

            VerticalLayoutGroup vLayout = _panelRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 14, 14);
            vLayout.spacing = 10f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            _titleText = CreateLabel(_panelRoot.transform, "Title", "ROADSIDE REPAIR", 22, TextAnchor.MiddleLeft, Color.white, 30f, _font);
            _summaryText = CreateLabel(_panelRoot.transform, "Summary", _displayedSummaryText, 15, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.92f, 1f), 26f, _font);

            GameObject listGo = new GameObject("FaultRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_panelRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 200f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 6f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _faultListRoot = (RectTransform)listGo.transform;

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_panelRoot.transform, false);
            LayoutElement footerLe = footerGo.GetComponent<LayoutElement>();
            footerLe.preferredHeight = 44f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 12f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            _buddyTowButton = CreateButton(footerGo.transform, "Btn_BuddyTow", "Buddy Tow", new Color(0.62f, 0.28f, 0.16f, 1f), _font, TriggerBuddyTow);
            _resumeRideButton = CreateButton(footerGo.transform, "Btn_ResumeRide", "Resume Ride", new Color(0.18f, 0.52f, 0.28f, 1f), _font, TriggerResumeRide);

            _hasRendered = false;
        }

        /// <summary>
        /// Mirrors active bike faults and tool-roll readiness. Dirty-checked so identical consecutive calls
        /// allocate zero GC memory and reuse pooled row objects.
        /// </summary>
        public void RenderFaults(BikeSimulation bike, SimulationWorld world, CatalogDatabase catalog)
        {
            IReadOnlyList<FaultState> activeFaults = bike != null ? bike.GetActiveFaults() : null;
            IReadOnlyList<string> toolRoll = world != null ? world.ActiveToolRoll : null;

            if (_hasRendered && IsStateUnchanged(activeFaults, toolRoll))
            {
                return;
            }

            _hasRendered = true;
            CacheState(activeFaults, toolRoll);

            int faultCount = activeFaults != null ? activeFaults.Count : 0;
            _displayedFaultCount = faultCount;

            if (faultCount == 0)
            {
                for (int i = 0; i < _rowPool.Count; i++)
                {
                    if (_rowPool[i].Root != null && _rowPool[i].Root.activeSelf)
                    {
                        _rowPool[i].Root.SetActive(false);
                    }
                }

                _displayedSummaryText = "No active faults — ready to resume riding.";
                if (_summaryText != null)
                {
                    _summaryText.text = _displayedSummaryText;
                }

                return;
            }

            int repairableCount = 0;
            for (int i = 0; i < faultCount; i++)
            {
                FaultState fault = activeFaults[i];
                if (fault.IsFieldRepairable)
                {
                    repairableCount++;
                }

                FaultDefinition def = null;
                if (catalog != null && !string.IsNullOrEmpty(fault.FaultId))
                {
                    catalog.Faults.TryGetValue(fault.FaultId, out def);
                }

                if (_faultListRoot != null)
                {
                    FaultRowEntry entry = GetOrCreateRow(i);
                    entry.BoundFaultId = fault.FaultId;
                    entry.Root.name = "FaultRow_" + fault.FaultId;
                    entry.Root.SetActive(true);
                    entry.InfoText.text = FormatFaultRowText(fault, def, toolRoll);
                    entry.RepairButton.interactable = fault.IsFieldRepairable;
                    entry.ButtonLabel.text = fault.IsFieldRepairable ? "Repair" : "Garage Only";
                }
            }

            for (int i = faultCount; i < _rowPool.Count; i++)
            {
                if (_rowPool[i].Root != null && _rowPool[i].Root.activeSelf)
                {
                    _rowPool[i].Root.SetActive(false);
                }
            }

            _sb.Length = 0;
            _sb.Append(faultCount).Append(faultCount == 1 ? " active fault (" : " active faults (");
            _sb.Append(repairableCount).Append(" roadside repairable)  |  Tool Roll: ");
            _sb.Append(toolRoll != null ? toolRoll.Count : 0).Append(" items");
            _displayedSummaryText = _sb.ToString();
            if (_summaryText != null)
            {
                _summaryText.text = _displayedSummaryText;
            }
        }

        private bool IsStateUnchanged(IReadOnlyList<FaultState> faults, IReadOnlyList<string> toolRoll)
        {
            int fCount = faults != null ? faults.Count : 0;
            if (fCount != _cachedFaultIds.Count)
            {
                return false;
            }

            for (int i = 0; i < fCount; i++)
            {
                if (!string.Equals(faults[i].FaultId, _cachedFaultIds[i], StringComparison.Ordinal)
                    || faults[i].IsFieldRepairable != _cachedRepairable[i])
                {
                    return false;
                }
            }

            int tCount = toolRoll != null ? toolRoll.Count : 0;
            if (tCount != _cachedToolRoll.Count)
            {
                return false;
            }

            for (int i = 0; i < tCount; i++)
            {
                if (!string.Equals(toolRoll[i], _cachedToolRoll[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void CacheState(IReadOnlyList<FaultState> faults, IReadOnlyList<string> toolRoll)
        {
            _cachedFaultIds.Clear();
            _cachedRepairable.Clear();
            if (faults != null)
            {
                for (int i = 0; i < faults.Count; i++)
                {
                    _cachedFaultIds.Add(faults[i].FaultId);
                    _cachedRepairable.Add(faults[i].IsFieldRepairable);
                }
            }

            _cachedToolRoll.Clear();
            if (toolRoll != null)
            {
                for (int i = 0; i < toolRoll.Count; i++)
                {
                    _cachedToolRoll.Add(toolRoll[i]);
                }
            }
        }

        private FaultRowEntry GetOrCreateRow(int index)
        {
            while (_rowPool.Count <= index)
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                FaultRowEntry entry = new FaultRowEntry();
                GameObject rowGo = new GameObject("FaultRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_faultListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f, 0.95f);

                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 56f;
                le.minHeight = 48f;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(10, 10, 6, 6);
                hLayout.spacing = 10f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                GameObject infoGo = new GameObject("Info", typeof(RectTransform), typeof(LayoutElement));
                infoGo.transform.SetParent(rowGo.transform, false);
                LayoutElement infoLe = infoGo.GetComponent<LayoutElement>();
                infoLe.flexibleWidth = 1f;
                Text infoText = infoGo.AddComponent<Text>();
                infoText.font = _font;
                infoText.fontSize = 14;
                infoText.alignment = TextAnchor.MiddleLeft;
                infoText.color = Color.white;
                infoText.raycastTarget = false;

                Button repairBtn = CreateButton(rowGo.transform, "Btn_Repair", "Repair", new Color(0.22f, 0.45f, 0.68f, 1f), _font, null);
                LayoutElement btnLe = repairBtn.GetComponent<LayoutElement>();
                btnLe.preferredWidth = 120f;
                btnLe.flexibleWidth = 0f;

                FaultRowEntry captured = entry;
                repairBtn.onClick.AddListener(delegate { TriggerRepairFault(captured.BoundFaultId); });

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.RepairButton = repairBtn;
                entry.ButtonLabel = repairBtn.GetComponentInChildren<Text>();
                _rowPool.Add(entry);
            }

            return _rowPool[index];
        }

        private string FormatFaultRowText(FaultState fault, FaultDefinition def, IReadOnlyList<string> toolRoll)
        {
            _sb.Length = 0;
            string name = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : fault.FaultId;
            _sb.Append(name);
            if (!fault.IsFieldRepairable)
            {
                _sb.Append(" [GARAGE REBUILD REQUIRED]");
                return _sb.ToString();
            }

            if (def != null)
            {
                AppendRequirementList("  Tools: ", def.RequiredTools, toolRoll);
                AppendRequirementList("  Spares: ", def.RequiredSpares, toolRoll);
            }

            return _sb.ToString();
        }

        private void AppendRequirementList(string prefix, List<string> required, IReadOnlyList<string> toolRoll)
        {
            if (required == null || required.Count == 0)
            {
                return;
            }

            _sb.Append(prefix);
            for (int i = 0; i < required.Count; i++)
            {
                if (i > 0)
                {
                    _sb.Append(", ");
                }

                string itemId = required[i];
                bool have = HasToolRollItem(toolRoll, itemId);
                _sb.Append(ToolRollOverlayView.GetFriendlyName(itemId));
                _sb.Append(have ? " [OK]" : " [MISSING]");
            }
        }

        private static bool HasToolRollItem(IReadOnlyList<string> toolRoll, string requiredId)
        {
            if (toolRoll == null || string.IsNullOrEmpty(requiredId))
            {
                return false;
            }

            for (int i = 0; i < toolRoll.Count; i++)
            {
                if (string.Equals(toolRoll[i], requiredId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
            text.fontSize = 15;
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
