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
        [SerializeField] private Button _pedalHomeButton;
        [SerializeField] private Text _messageText;

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
        public event Action PedalHomeRequested;

        public int DisplayedFaultCount
        {
            get { return _displayedFaultCount; }
        }

        public string DisplayedSummaryText
        {
            get { return _displayedSummaryText; }
        }

        /// <summary>Last result line (repair / tow outcome) shown under the fault list.</summary>
        public string DisplayedMessage
        {
            get { return _messageText != null ? _messageText.text : string.Empty; }
        }

        /// <summary>Enables or disables the tow / resume / pedal-home footer (e.g. while a recovery is resolving).</summary>
        public void SetFooterInteractable(bool buddyTow, bool resume, bool pedalHome)
        {
            if (_buddyTowButton != null) { _buddyTowButton.interactable = buddyTow; }
            if (_resumeRideButton != null) { _resumeRideButton.interactable = resume; }
            if (_pedalHomeButton != null) { _pedalHomeButton.interactable = pedalHome; }
        }

        /// <summary>Shows the outcome of the last repair / recovery attempt. Event-driven (allocates only the string assignment).</summary>
        public void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message ?? string.Empty;
            }
        }

        /// <summary>Gives keyboard / gamepad focus to the first interactable repair button (or the tow button).</summary>
        public void FocusDefault()
        {
            Selectable target = null;
            for (int i = 0; i < _rowPool.Count && target == null; i++)
            {
                FaultRowEntry e = _rowPool[i];
                if (e.Root != null && e.Root.activeSelf && e.RepairButton != null && e.RepairButton.IsInteractable())
                {
                    target = e.RepairButton;
                }
            }

            if (target == null) { target = _resumeRideButton != null && _resumeRideButton.IsInteractable() ? _resumeRideButton : _buddyTowButton; }
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && target != null && target.gameObject.activeInHierarchy && target.IsInteractable())
            {
                es.SetSelectedGameObject(target.gameObject);
            }
        }

        public void TriggerPedalHome()
        {
            Action handler = PedalHomeRequested;
            if (handler != null)
            {
                handler();
            }
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
            cardRt.sizeDelta = new Vector2(760f, 560f);

            Image bg = _panelRoot.GetComponent<Image>();
            bg.color = new Color(0.09f, 0.10f, 0.12f, 0.94f);

            VerticalLayoutGroup vLayout = _panelRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 14, 14);
            vLayout.spacing = 10f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            _titleText = CreateLabel(_panelRoot.transform, "Title", "BROKEN DOWN - ROADSIDE REPAIR", 24, TextAnchor.MiddleLeft, Color.white, 30f, _font);
            _summaryText = CreateLabel(_panelRoot.transform, "Summary", _displayedSummaryText, 15, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.92f, 1f), 26f, _font);

            GameObject listGo = new GameObject("FaultRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_panelRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 260f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 6f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = false; // rows keep their own height instead of stretching to fill the list
            listLayout.childForceExpandWidth = true;
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childForceExpandHeight = false;
            _faultListRoot = (RectTransform)listGo.transform;

            _messageText = CreateLabel(_panelRoot.transform, "Message", string.Empty, 15, TextAnchor.MiddleLeft, new Color(0.98f, 0.76f, 0.20f, 1f), 44f, _font);
            _messageText.horizontalOverflow = HorizontalWrapMode.Wrap;

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_panelRoot.transform, false);
            LayoutElement footerLe = footerGo.GetComponent<LayoutElement>();
            footerLe.preferredHeight = 48f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 12f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            _buddyTowButton = CreateButton(footerGo.transform, "Btn_BuddyTow", "Call a Tow (to Garage)", new Color(0.62f, 0.28f, 0.16f, 1f), _font, TriggerBuddyTow);
            _pedalHomeButton = CreateButton(footerGo.transform, "Btn_PedalHome", "Pedal Home (free)", new Color(0.42f, 0.34f, 0.18f, 1f), _font, TriggerPedalHome);
            _resumeRideButton = CreateButton(footerGo.transform, "Btn_ResumeRide", "Resume Ride (Esc)", new Color(0.18f, 0.52f, 0.28f, 1f), _font, TriggerResumeRide);

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

                _displayedSummaryText = "No active faults - ready to resume riding.";
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
                    FaultMetadata meta = null;
                    if (catalog != null && !string.IsNullOrEmpty(fault.FaultId))
                    {
                        catalog.FaultMeta.TryGetValue(fault.FaultId, out meta);
                    }

                    entry.InfoText.text = FormatFaultRowText(fault, def, meta, toolRoll, out bool anyOptionReady);
                    entry.RepairButton.interactable = fault.IsFieldRepairable;
                    // Display hint only: Core decides the repair; a click with missing items returns its explanation.
                    entry.ButtonLabel.text = !fault.IsFieldRepairable ? "Garage Only" : (anyOptionReady ? "Repair" : "Missing items");
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
                ((RectTransform)rowGo.transform).sizeDelta = new Vector2(0f, 128f);

                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 128f;
                le.minHeight = 100f;

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
                infoText.fontSize = 15;
                infoText.alignment = TextAnchor.MiddleLeft;
                infoText.color = Color.white;
                infoText.raycastTarget = false;
                infoText.supportRichText = true;
                infoText.horizontalOverflow = HorizontalWrapMode.Wrap;

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

        private string FormatFaultRowText(FaultState fault, FaultDefinition def, FaultMetadata meta, IReadOnlyList<string> toolRoll, out bool anyOptionReady)
        {
            anyOptionReady = false;
            _sb.Length = 0;
            string name = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : fault.FaultId;
            _sb.Append("<b>").Append(name).Append("</b>");

            // Diagnosis: the catalog's one-line hint, else the first symptom line.
            string diagnosis = meta != null ? meta.DiagnosticHint : string.Empty;
            if (string.IsNullOrEmpty(diagnosis) && def != null && !string.IsNullOrEmpty(def.Symptoms))
            {
                int nl = def.Symptoms.IndexOf('\n');
                diagnosis = nl > 0 ? def.Symptoms.Substring(0, nl) : def.Symptoms;
            }

            if (!string.IsNullOrEmpty(diagnosis))
            {
                _sb.Append("\n<color=#B8C4D0>").Append(diagnosis).Append("</color>");
            }

            if (!fault.IsFieldRepairable)
            {
                _sb.Append("\n<color=#FF6B5F>Cannot be fixed on the road - call a tow or pedal home, then rebuild in the garage.</color>");
                return _sb.ToString();
            }

            if (meta != null && meta.RepairOptions != null && meta.RepairOptions.Count > 0)
            {
                for (int i = 0; i < meta.RepairOptions.Count; i++)
                {
                    FaultRepairOption option = meta.RepairOptions[i];
                    _sb.Append("\n").Append(Humanize(option.OptionId)).Append(": ");
                    bool ready = AppendNeeds(option.RequiredTools, option.ConsumedSpares, toolRoll);
                    anyOptionReady |= ready;
                }
            }
            else if (def != null)
            {
                _sb.Append("\nNeeds: ");
                anyOptionReady = AppendNeeds(def.RequiredTools, def.RequiredSpares, toolRoll);
            }

            return _sb.ToString();
        }

        private bool AppendNeeds(IReadOnlyList<string> tools, IReadOnlyList<string> spares, IReadOnlyList<string> toolRoll)
        {
            bool all = true;
            bool first = true;
            all &= AppendItems(tools, toolRoll, ref first);
            all &= AppendItems(spares, toolRoll, ref first);
            if (first)
            {
                _sb.Append("nothing");
            }

            return all;
        }

        private bool AppendItems(IReadOnlyList<string> items, IReadOnlyList<string> toolRoll, ref bool first)
        {
            bool all = true;
            if (items == null)
            {
                return true;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!first)
                {
                    _sb.Append(", ");
                }

                first = false;
                bool have = HasToolRollItem(toolRoll, items[i]);
                all &= have;
                _sb.Append(have ? "<color=#6FDC7A>" : "<color=#FF6B5F>");
                _sb.Append(Humanize(items[i]));
                _sb.Append(have ? " (packed)" : " (MISSING)");
                _sb.Append("</color>");
            }

            return all;
        }

        /// <summary>"clean_plug_with_brush" -> "Clean plug with brush"; "SparkPlugWrench" -> "Spark Plug Wrench".</summary>
        private static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(id.Length + 6);
            bool snake = id.IndexOf('_') >= 0;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_')
                {
                    sb.Append(' ');
                }
                else if (!snake && i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1]))
                {
                    sb.Append(' ').Append(c);
                }
                else
                {
                    sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
                }
            }

            return sb.ToString();
        }

        /// <summary>Catalog ids ("SparkPlugWrench") and tool-roll ids ("tool_spark_plug_wrench") compare equal after this display-only normalisation.</summary>
        private static string Normalize(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            string n = id.Replace("_", string.Empty).ToLowerInvariant();
            return n.StartsWith("tool", StringComparison.Ordinal) ? n.Substring(4) : n;
        }

        private static bool HasToolRollItem(IReadOnlyList<string> toolRoll, string requiredId)
        {
            if (toolRoll == null || string.IsNullOrEmpty(requiredId))
            {
                return false;
            }

            string want = Normalize(requiredId);
            for (int i = 0; i < toolRoll.Count; i++)
            {
                if (string.Equals(Normalize(toolRoll[i]), want, StringComparison.Ordinal))
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
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);
            btn.colors = cb;
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
            text.fontSize = 16;
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
