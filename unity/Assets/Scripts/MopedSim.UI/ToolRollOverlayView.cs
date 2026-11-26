using System;
using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Pre-ride Tool Roll packing overlay (base 6 slots). Raises pack/unpack/toggle intents; the App layer decides.
    /// </summary>
    public sealed class ToolRollOverlayView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private sealed class GeneratedToolRow
        {
            public GameObject Root;
            public Text NameLabel;
            public Button ToggleButton;
            public Text ButtonLabel;
            public string BoundItemId = string.Empty;
        }

        [SerializeField] private Text _slotsLabel;
        [SerializeField] private Text _itemsList;
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Button _rowTemplate;

        private readonly List<string> _available = new List<string>();
        private readonly List<string> _packed = new List<string>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GeneratedToolRow> _generatedRows = new List<GeneratedToolRow>(18);

        private GameObject _panelRoot;
        private Transform _builtParent;
        private RectTransform _generatedListRoot;
        private Button _closeButton;
        private Font _font;

        private int _capacity = 6;
        private bool _hasRendered;

        /// <summary>Raised when the player asks to pack an item.</summary>
        public event Action<string> PackRequested;

        /// <summary>Raised when the player asks to unpack an item.</summary>
        public event Action<string> UnpackRequested;

        /// <summary>Raised when the player clicks a generated Pack/Remove toggle button.</summary>
        public event Action<string> ToggleToolRollItemRequested;

        /// <summary>Raised when the player clicks Close on the Tool Roll overlay.</summary>
        public event Action CloseRequested;

        /// <summary>Number of packed items.</summary>
        public int PackedCount { get { return _packed.Count; } }

        /// <summary>Tool roll slot capacity.</summary>
        public int Capacity { get { return _capacity; } }

        /// <summary>True when packed count has reached capacity.</summary>
        public bool IsFull { get { return _packed.Count >= _capacity; } }

        /// <summary>Case-insensitive ordinal membership test on packed items.</summary>
        public bool Contains(string itemId)
        {
            if (itemId == null) { return false; }
            for (int i = 0; i < _packed.Count; i++)
            {
                if (string.Equals(_packed[i], itemId, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Builds a dark interactive Tool Roll overlay card under <paramref name="root"/>.
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
                _generatedRows.Clear();
            }

            _builtParent = parent;
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _panelRoot = new GameObject("ToolRoll_OverlayCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _panelRoot.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)_panelRoot.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -64f);
            rt.sizeDelta = new Vector2(360f, 440f);

            _panelRoot.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.92f);

            VerticalLayoutGroup vLayout = _panelRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(12, 12, 10, 10);
            vLayout.spacing = 6f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            GameObject headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerRow.transform.SetParent(_panelRoot.transform, false);
            headerRow.GetComponent<LayoutElement>().preferredHeight = 32f;
            HorizontalLayoutGroup headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 8f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;

            Text title = CreateLabel(headerRow.transform, "Title", "TOOL ROLL", 18, TextAnchor.MiddleLeft, Color.white, 30f, _font);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;

            if (_slotsLabel == null)
            {
                _slotsLabel = CreateLabel(headerRow.transform, "Slots", _packed.Count + " / " + _capacity, 16, TextAnchor.MiddleRight, new Color(0.75f, 0.90f, 0.98f, 1f), 30f, _font);
                _slotsLabel.GetComponent<LayoutElement>().preferredWidth = 72f;
            }

            _closeButton = CreateButton(headerRow.transform, "Btn_Close", "Close", new Color(0.30f, 0.24f, 0.24f, 1f), _font, TriggerClose);
            _closeButton.GetComponent<LayoutElement>().preferredWidth = 68f;

            GameObject listGo = new GameObject("ToolRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_panelRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 260f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 4f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _generatedListRoot = (RectTransform)listGo.transform;

            _hasRendered = false;
            RebuildRows();
        }

        /// <summary>Sets the items offered for packing and rebuilds rows.</summary>
        public void SetAvailableItems(IReadOnlyList<string> itemIds)
        {
            _available.Clear();
            if (itemIds != null)
            {
                for (int i = 0; i < itemIds.Count; i++) { _available.Add(itemIds[i]); }
            }
            _hasRendered = false;
            RebuildRows();
        }

        /// <summary>Convenience overload displaying the active tool roll directly from a SimulationWorld.</summary>
        public void Render(SimulationWorld world)
        {
            if (world == null)
            {
                Render(null, 0);
                return;
            }

            Render(world.ActiveToolRoll, world.ToolRollCapacity);
        }

        /// <summary>Displays the current packed loadout. Dirty-checked so unchanged steady-state calls allocate 0 B.</summary>
        public void Render(IReadOnlyList<string> packedItemIds, int capacity)
        {
            int clampedCap = Mathf.Max(0, capacity);
            if (_hasRendered && clampedCap == _capacity && IsPackedUnchanged(packedItemIds))
            {
                return;
            }

            _hasRendered = true;
            _capacity = clampedCap;
            _packed.Clear();
            if (packedItemIds != null)
            {
                for (int i = 0; i < packedItemIds.Count; i++) { _packed.Add(packedItemIds[i]); }
            }

            if (_slotsLabel != null)
            {
                _slotsLabel.text = _packed.Count + " / " + _capacity;
            }
            if (_itemsList != null)
            {
                List<string> names = new List<string>(_packed.Count);
                for (int i = 0; i < _packed.Count; i++) { names.Add(GetFriendlyName(_packed[i])); }
                _itemsList.text = string.Join("\n", names);
            }
            RebuildRows();
        }

        /// <summary>Raises PackRequested or UnpackRequested depending on current packed state.</summary>
        public void RequestToggle(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) { return; }
            if (Contains(itemId))
            {
                Action<string> handler = UnpackRequested;
                if (handler != null) { handler(itemId); }
            }
            else
            {
                Action<string> handler = PackRequested;
                if (handler != null) { handler(itemId); }
            }
        }

        /// <summary>Raises ToggleToolRollItemRequested (falls back to RequestToggle when only legacy listeners are bound).</summary>
        public void TriggerToggleItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            Action<string> handler = ToggleToolRollItemRequested;
            if (handler != null)
            {
                handler(itemId);
                return;
            }

            RequestToggle(itemId);
        }

        /// <summary>Raises CloseRequested.</summary>
        public void TriggerClose()
        {
            Action handler = CloseRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>Maps a ToolIds constant to a readable name; falls back to the id.</summary>
        public static string GetFriendlyName(string itemId)
        {
            switch (itemId)
            {
                case ToolIds.SparkPlugWrench: return "Spark Plug Wrench";
                case ToolIds.Flathead: return "Flathead Screwdriver";
                case ToolIds.NeedleNose: return "Needle-Nose Pliers";
                case ToolIds.FeelerGauge: return "Feeler Gauge";
                case ToolIds.Wrench10mm: return "10mm Wrench";
                case ToolIds.Wrench15mm: return "15mm Wrench";
                case ToolIds.TorqueWrench: return "Torque Wrench";
                case ToolIds.FlywheelPuller: return "Flywheel Puller";
                case ToolIds.VariatorStrap: return "Variator Strap Wrench";
                case ToolIds.PenetratingOil: return "Penetrating Oil";
                case ToolIds.WireBrush: return "Wire Brush";
                case ToolIds.CarbCleaner: return "Carb Cleaner";
                case ToolIds.SparePlug: return "Spare Spark Plug";
                case ToolIds.SpareMasterLink: return "Spare Master Link";
                case ToolIds.SpareBelt: return "Spare Drive Belt";
                case ToolIds.SpareFlangeNut: return "Spare Flange Nut";
                case ToolIds.SpareCondenser: return "Spare Condenser";
                case ToolIds.SpareMainJet: return "Spare Main Jet";
                default: return itemId ?? string.Empty;
            }
        }

        private bool IsPackedUnchanged(IReadOnlyList<string> packedItemIds)
        {
            int count = packedItemIds != null ? packedItemIds.Count : 0;
            if (count != _packed.Count)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(packedItemIds[i], _packed[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildRows()
        {
            RebuildLegacyTemplateRows();
            RebuildGeneratedRows();
        }

        private void RebuildLegacyTemplateRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) { DestroyObjectSafe(_rows[i]); }
            }
            _rows.Clear();

            if (_rowContainer == null || _rowTemplate == null) { return; }

            for (int i = 0; i < _available.Count; i++)
            {
                string id = _available[i];
                Button row = UnityEngine.Object.Instantiate(_rowTemplate, _rowContainer);
                row.gameObject.SetActive(true);
                Text label = row.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = (Contains(id) ? "[x] " : "[ ] ") + GetFriendlyName(id);
                }
                row.onClick.AddListener(() => RequestToggle(id));
                _rows.Add(row.gameObject);
            }
        }

        private void RebuildGeneratedRows()
        {
            if (_generatedListRoot == null)
            {
                return;
            }

            for (int i = 0; i < _available.Count; i++)
            {
                string id = _available[i];
                bool packed = Contains(id);
                GeneratedToolRow row = GetOrCreateGeneratedRow(i);
                row.BoundItemId = id;
                row.Root.name = "ToolRow_" + id;
                row.Root.SetActive(true);
                row.NameLabel.text = (packed ? "[x] " : "[ ] ") + GetFriendlyName(id);
                row.ButtonLabel.text = packed ? "Remove" : "Pack";
                row.ToggleButton.interactable = packed || !IsFull;
            }

            for (int i = _available.Count; i < _generatedRows.Count; i++)
            {
                if (_generatedRows[i].Root != null && _generatedRows[i].Root.activeSelf)
                {
                    _generatedRows[i].Root.SetActive(false);
                }
            }
        }

        private GeneratedToolRow GetOrCreateGeneratedRow(int index)
        {
            while (_generatedRows.Count <= index)
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                GeneratedToolRow entry = new GeneratedToolRow();
                GameObject rowGo = new GameObject("ToolRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_generatedListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 0.94f);
                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 32f;
                le.minHeight = 28f;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(8, 8, 2, 2);
                hLayout.spacing = 6f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                Text nameLabel = CreateLabel(rowGo.transform, "Name", string.Empty, 13, TextAnchor.MiddleLeft, Color.white, 26f, _font);
                nameLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

                Button toggleBtn = CreateButton(rowGo.transform, "Btn_Toggle", "Pack", new Color(0.22f, 0.44f, 0.62f, 1f), _font, null);
                toggleBtn.GetComponent<LayoutElement>().preferredWidth = 76f;

                GeneratedToolRow captured = entry;
                toggleBtn.onClick.AddListener(delegate { TriggerToggleItem(captured.BoundItemId); });

                entry.Root = rowGo;
                entry.NameLabel = nameLabel;
                entry.ToggleButton = toggleBtn;
                entry.ButtonLabel = toggleBtn.GetComponentInChildren<Text>();
                _generatedRows.Add(entry);
            }

            return _generatedRows[index];
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
            text.fontSize = 13;
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
