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
    /// Parts terminal / shop listing. Raises purchase and install intents only; App owns the economy and wrenching.
    /// </summary>
    public sealed class PartsCatalogShopView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private sealed class GeneratedPartRow
        {
            public GameObject Root;
            public Text InfoText;
            public Button BuyButton;
            public Text BuyButtonLabel;
            public Button InstallButton;
            public Text InstallButtonLabel;
            public string BoundPartId = string.Empty;
        }

        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Button _rowTemplate;
        [SerializeField] private Text _cashLabel;

        private readonly List<PartDefinition> _catalog = new List<PartDefinition>();
        private readonly List<PartDefinition> _visible = new List<PartDefinition>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GeneratedPartRow> _generatedRows = new List<GeneratedPartRow>(24);
        private readonly Dictionary<string, int> _ownedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _mountedPartIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder _sb = new StringBuilder(160);

        private GameObject _modalRoot;
        private Transform _builtParent;
        private RectTransform _generatedListRoot;
        private Text _feedbackLabel;
        private Button _closeButton;
        private Font _font;

        private bool _filterActive;
        private PartCategory _filter;
        private decimal _cash;
        private string _displayedFeedback = string.Empty;
        private bool _hasRenderedCatalog;
        private decimal _lastRenderedCash;
        private int _lastRenderedCatalogCount = -1;
        private int _lastRenderedInventoryCount = -1;
        private int _lastRenderedMountedCount = -1;
        private string _lastRenderedFeedback = string.Empty;

        /// <summary>Raised when the player asks to buy an affordable part (legacy typed event).</summary>
        public event Action<PartDefinition> PurchaseRequested;

        /// <summary>Raised with the catalog part ID when the player clicks Buy.</summary>
        public event Action<string> BuyPartRequested;

        /// <summary>Raised with the catalog part ID when the player clicks Install/Swap.</summary>
        public event Action<string> InstallPartRequested;

        /// <summary>Raised when the player closes the parts shop modal.</summary>
        public event Action CloseRequested;

        /// <summary>Number of parts passing the current filter.</summary>
        public int VisibleCount { get { return _visible.Count; } }

        /// <summary>Most recent shop feedback banner text.</summary>
        public string DisplayedFeedback { get { return _displayedFeedback; } }

        /// <summary>Stores a copy of the catalog and rebuilds the list.</summary>
        public void SetCatalog(IReadOnlyList<PartDefinition> parts)
        {
            _catalog.Clear();
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++) { _catalog.Add(parts[i]); }
            }
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Filters the list to one category.</summary>
        public void SetCategoryFilter(PartCategory category)
        {
            _filterActive = true;
            _filter = category;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Removes any category filter.</summary>
        public void ClearCategoryFilter()
        {
            _filterActive = false;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Sets displayed cash.</summary>
        public void SetCash(decimal cashUsd)
        {
            _cash = cashUsd;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Display affordance only: price is within displayed cash.</summary>
        public bool CanAfford(PartDefinition part)
        {
            return part != null && part.PriceUsd <= _cash;
        }

        /// <summary>Gets a visible part by index, or null when out of range.</summary>
        public PartDefinition GetVisible(int index)
        {
            if (index < 0 || index >= _visible.Count) { return null; }
            return _visible[index];
        }

        /// <summary>Raises PurchaseRequested for a visible, affordable part. Returns true if raised.</summary>
        public bool RequestPurchase(string partId)
        {
            if (string.IsNullOrEmpty(partId)) { return false; }
            for (int i = 0; i < _visible.Count; i++)
            {
                PartDefinition part = _visible[i];
                if (!string.Equals(part.Id, partId, StringComparison.Ordinal)) { continue; }
                if (!CanAfford(part)) { return false; }
                Action<PartDefinition> handler = PurchaseRequested;
                if (handler != null) { handler(part); }
                return true;
            }
            return false;
        }

        /// <summary>Raises BuyPartRequested (or falls back to RequestPurchase when only legacy listeners are bound).</summary>
        public void TriggerBuyPart(string partId)
        {
            if (string.IsNullOrEmpty(partId))
            {
                return;
            }

            Action<string> handler = BuyPartRequested;
            if (handler != null)
            {
                handler(partId);
                return;
            }

            RequestPurchase(partId);
        }

        /// <summary>Raises InstallPartRequested for an owned/swappable part.</summary>
        public void TriggerInstallPart(string partId)
        {
            if (string.IsNullOrEmpty(partId))
            {
                return;
            }

            Action<string> handler = InstallPartRequested;
            if (handler != null)
            {
                handler(partId);
            }
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

        /// <summary>
        /// Builds a dark clickable parts catalog shop modal under <paramref name="root"/>.
        /// Idempotent for the same parent RectTransform.
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
                _generatedRows.Clear();
            }

            _builtParent = parent;
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _modalRoot = new GameObject("PartsCatalogShop_Modal", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _modalRoot.transform.SetParent(parent, false);
            RectTransform modalRt = (RectTransform)_modalRoot.transform;
            modalRt.anchorMin = new Vector2(0.5f, 0.5f);
            modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.pivot = new Vector2(0.5f, 0.5f);
            modalRt.anchoredPosition = Vector2.zero;
            modalRt.sizeDelta = new Vector2(780f, 520f);

            _modalRoot.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

            VerticalLayoutGroup vLayout = _modalRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 14, 14);
            vLayout.spacing = 8f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            GameObject headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerRow.transform.SetParent(_modalRoot.transform, false);
            headerRow.GetComponent<LayoutElement>().preferredHeight = 36f;
            HorizontalLayoutGroup headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 10f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;

            Text title = CreateLabel(headerRow.transform, "Title", "PARTS CATALOG SHOP", 20, TextAnchor.MiddleLeft, Color.white, 34f, _font);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;

            Text cashText = CreateLabel(headerRow.transform, "CashHeader", "$" + _cash.ToString("F2"), 20, TextAnchor.MiddleRight, new Color(0.45f, 0.92f, 0.55f, 1f), 34f, _font);
            cashText.GetComponent<LayoutElement>().preferredWidth = 160f;
            _cashLabel = cashText;

            _closeButton = CreateButton(headerRow.transform, "Btn_Close", "Close", new Color(0.32f, 0.24f, 0.24f, 1f), _font, TriggerClose);
            _closeButton.GetComponent<LayoutElement>().preferredWidth = 96f;

            _feedbackLabel = CreateLabel(_modalRoot.transform, "Feedback", _displayedFeedback, 14, TextAnchor.MiddleLeft, new Color(0.92f, 0.84f, 0.45f, 1f), 24f, _font);

            GameObject listGo = new GameObject("ShopRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_modalRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 320f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 5f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _generatedListRoot = (RectTransform)listGo.transform;

            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>
        /// Renders the live catalog, player cash, garage inventory counts, and mounted part status.
        /// Dirty-checked so steady-state repeat calls skip layout/string rebuilds.
        /// </summary>
        public void RenderCatalog(CatalogDatabase catalog, SimulationWorld world, BikeSimulation bike, string feedback)
        {
            IReadOnlyList<PartDefinition> parts = catalog != null ? catalog.PartList : null;
            int catalogCount = parts != null ? parts.Count : 0;
            decimal cash = world != null ? world.CashUsd : _cash;
            int inventoryCount = world != null ? world.Inventory.Count : 0;
            int mountedCount = CountMountedParts(bike);
            string safeFeedback = feedback ?? string.Empty;

            if (_hasRenderedCatalog
                && cash == _lastRenderedCash
                && catalogCount == _lastRenderedCatalogCount
                && inventoryCount == _lastRenderedInventoryCount
                && mountedCount == _lastRenderedMountedCount
                && string.Equals(safeFeedback, _lastRenderedFeedback, StringComparison.Ordinal))
            {
                return;
            }

            _hasRenderedCatalog = true;
            _lastRenderedCash = cash;
            _lastRenderedCatalogCount = catalogCount;
            _lastRenderedInventoryCount = inventoryCount;
            _lastRenderedMountedCount = mountedCount;
            _lastRenderedFeedback = safeFeedback;

            _cash = cash;
            _displayedFeedback = safeFeedback;
            if (_feedbackLabel != null)
            {
                _feedbackLabel.text = _displayedFeedback;
            }

            _catalog.Clear();
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    _catalog.Add(parts[i]);
                }
            }

            _ownedCounts.Clear();
            if (world != null)
            {
                IReadOnlyList<PartInstance> items = world.Inventory.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    string pid = items[i].PartId;
                    int existing;
                    _ownedCounts.TryGetValue(pid, out existing);
                    _ownedCounts[pid] = existing + 1;
                }
            }

            _mountedPartIds.Clear();
            if (bike != null)
            {
                IReadOnlyList<MountSlotNode> slots = bike.Assembly.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    PartInstance installed = slots[i].InstalledPart;
                    if (installed != null && !string.IsNullOrEmpty(installed.PartId))
                    {
                        _mountedPartIds.Add(installed.PartId);
                    }
                }
            }

            Refresh();
        }

        private void Refresh()
        {
            _visible.Clear();
            for (int i = 0; i < _catalog.Count; i++)
            {
                PartDefinition part = _catalog[i];
                if (part == null) { continue; }
                if (_filterActive && part.Category != _filter) { continue; }
                _visible.Add(part);
            }

            if (_cashLabel != null) { _cashLabel.text = "$" + _cash.ToString("F2"); }

            RefreshLegacyTemplateRows();
            RefreshGeneratedRows();
        }

        private void RefreshLegacyTemplateRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) { DestroyObjectSafe(_rows[i]); }
            }
            _rows.Clear();

            if (_rowContainer == null || _rowTemplate == null) { return; }

            for (int i = 0; i < _visible.Count; i++)
            {
                PartDefinition part = _visible[i];
                string id = part.Id;
                Button row = UnityEngine.Object.Instantiate(_rowTemplate, _rowContainer);
                row.gameObject.SetActive(true);
                row.interactable = CanAfford(part);
                Text label = row.GetComponentInChildren<Text>();
                if (label != null) { label.text = part.DisplayName + "  $" + part.PriceUsd.ToString("F2"); }
                row.onClick.AddListener(() => RequestPurchase(id));
                _rows.Add(row.gameObject);
            }
        }

        private void RefreshGeneratedRows()
        {
            if (_generatedListRoot == null)
            {
                return;
            }

            for (int i = 0; i < _visible.Count; i++)
            {
                PartDefinition part = _visible[i];
                GeneratedPartRow row = GetOrCreateGeneratedRow(i);
                row.BoundPartId = part.Id;
                row.Root.name = "PartRow_" + part.Id;
                row.Root.SetActive(true);

                int owned;
                _ownedCounts.TryGetValue(part.Id, out owned);
                bool mounted = _mountedPartIds.Contains(part.Id);

                _sb.Length = 0;
                _sb.Append(part.DisplayName).Append("  [").Append(part.Category.ToString()).Append(']');
                if (mounted)
                {
                    _sb.Append("  (Mounted)");
                }
                if (owned > 0)
                {
                    _sb.Append("  x").Append(owned).Append(" shelf");
                }

                row.InfoText.text = _sb.ToString();
                row.BuyButton.interactable = CanAfford(part);
                row.BuyButtonLabel.text = "Buy $" + part.PriceUsd.ToString("0");
                row.InstallButton.interactable = !mounted;
                row.InstallButtonLabel.text = mounted ? "Mounted" : (owned > 0 ? "Install" : "Swap");
            }

            for (int i = _visible.Count; i < _generatedRows.Count; i++)
            {
                if (_generatedRows[i].Root != null && _generatedRows[i].Root.activeSelf)
                {
                    _generatedRows[i].Root.SetActive(false);
                }
            }
        }

        private GeneratedPartRow GetOrCreateGeneratedRow(int index)
        {
            while (_generatedRows.Count <= index)
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                GeneratedPartRow entry = new GeneratedPartRow();
                GameObject rowGo = new GameObject("PartRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_generatedListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.21f, 0.94f);
                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 40f;
                le.minHeight = 36f;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(8, 8, 4, 4);
                hLayout.spacing = 8f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                Text infoText = CreateLabel(rowGo.transform, "Info", string.Empty, 14, TextAnchor.MiddleLeft, Color.white, 32f, _font);
                infoText.GetComponent<LayoutElement>().flexibleWidth = 1f;

                Button buyBtn = CreateButton(rowGo.transform, "Btn_Buy", "Buy", new Color(0.20f, 0.50f, 0.28f, 1f), _font, null);
                buyBtn.GetComponent<LayoutElement>().preferredWidth = 104f;

                Button installBtn = CreateButton(rowGo.transform, "Btn_Install", "Install", new Color(0.22f, 0.42f, 0.64f, 1f), _font, null);
                installBtn.GetComponent<LayoutElement>().preferredWidth = 92f;

                GeneratedPartRow captured = entry;
                buyBtn.onClick.AddListener(delegate { TriggerBuyPart(captured.BoundPartId); });
                installBtn.onClick.AddListener(delegate { TriggerInstallPart(captured.BoundPartId); });

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.BuyButton = buyBtn;
                entry.BuyButtonLabel = buyBtn.GetComponentInChildren<Text>();
                entry.InstallButton = installBtn;
                entry.InstallButtonLabel = installBtn.GetComponentInChildren<Text>();
                _generatedRows.Add(entry);
            }

            return _generatedRows[index];
        }

        private static int CountMountedParts(BikeSimulation bike)
        {
            if (bike == null)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<MountSlotNode> slots = bike.Assembly.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].InstalledPart != null)
                {
                    count++;
                }
            }

            return count;
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
