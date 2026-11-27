using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MopedSim.Core;
using MopedSim.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>Display grouping of catalog parts into the shop's category tabs (presentation only).</summary>
    public enum ShopTab
    {
        All = 0,
        Engine = 1,
        FuelCarb = 2,
        Exhaust = 3,
        Drivetrain = 4,
        Chassis = 5,
        ToolsSpares = 6,
        Cargo = 7
    }

    /// <summary>
    /// Parts terminal / shop. A centered modal with category tabs and a scrollable, keyboard / gamepad navigable list of catalog
    /// parts (name, stat summary, price, state, Buy / Install). Raises buy / install / close intents only: MopedSim.App owns the
    /// economy and wrenching, Core owns prices, fit and install rules. Idle cost is a single selection-reference compare.
    /// </summary>
    public sealed class PartsCatalogShopView : MonoBehaviour
    {
        private const float RowHeight = 62f;
        private const float RowSpacing = 5f;
        private const int TabCount = 8;
        private static readonly string[] TabLabels =
        {
            "All", "Engine", "Fuel / Carb", "Exhaust", "Drivetrain", "Chassis", "Tools", "Cargo"
        };

        private static readonly Color CardColor = new Color(0.08f, 0.09f, 0.12f, 0.97f);
        private static readonly Color RowColor = new Color(0.15f, 0.17f, 0.21f, 0.96f);
        private static readonly Color TabIdle = new Color(0.22f, 0.26f, 0.33f, 1f);
        private static readonly Color TabActive = new Color(0.86f, 0.58f, 0.16f, 1f);
        private static readonly Color BuyColor = new Color(0.20f, 0.50f, 0.28f, 1f);
        private static readonly Color InstallColor = new Color(0.22f, 0.42f, 0.64f, 1f);
        private static readonly Color CashColor = new Color(0.45f, 0.92f, 0.55f, 1f);

        private sealed class GeneratedPartRow
        {
            public GameObject Root;
            public Text InfoText;
            public Text PriceText;
            public Text StateText;
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
        private readonly List<GeneratedPartRow> _generatedRows = new List<GeneratedPartRow>(64);
        private readonly Dictionary<string, int> _ownedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _mountedPartIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder _sb = new StringBuilder(200);
        private readonly Image[] _tabImages = new Image[TabCount];
        private readonly Button[] _tabButtons = new Button[TabCount];

        private GameObject _modalRoot;
        private Transform _builtParent;
        private RectTransform _generatedListRoot;
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private Text _feedbackLabel;
        private Text _fitsLabel;
        private Text _emptyLabel;
        private Button _closeButton;
        private Button _swapMeetButton;
        private Button _fitsButton;
        private GameObject _lastSelected;

        private BikeSimulation _bike;
        private bool _filterActive;
        private PartCategory _filter;
        private ShopTab _tab = ShopTab.All;
        private bool _fitsOnly = true;
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

        /// <summary>Raised with the catalog part ID when the player clicks Install / Swap / Pack.</summary>
        public event Action<string> InstallPartRequested;

        /// <summary>Raised when the player closes the parts shop modal.</summary>
        public event Action CloseRequested;

        /// <summary>Raised when the player clicks the Swap Meet button.</summary>
        public event Action SwapMeetRequested;

        /// <summary>Number of parts passing the current filters.</summary>
        public int VisibleCount { get { return _visible.Count; } }

        /// <summary>Most recent shop feedback banner text.</summary>
        public string DisplayedFeedback { get { return _displayedFeedback; } }

        /// <summary>Currently selected category tab.</summary>
        public ShopTab ActiveTab { get { return _tab; } }

        /// <summary>When true (default) only parts that fit the current bike (or the tool roll) are listed.</summary>
        public bool FitsBikeOnly { get { return _fitsOnly; } }

        /// <summary>Stores a copy of the catalog and rebuilds the list.</summary>
        public void SetCatalog(IReadOnlyList<PartDefinition> parts)
        {
            CopyCatalog(parts);
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Filters the list to one exact part category.</summary>
        public void SetCategoryFilter(PartCategory category)
        {
            _filterActive = true;
            _filter = category;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Removes any exact-category filter.</summary>
        public void ClearCategoryFilter()
        {
            _filterActive = false;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Selects a category tab.</summary>
        public void SelectTab(ShopTab tab)
        {
            _tab = tab;
            _hasRenderedCatalog = false;
            Refresh();
            if (_scroll != null) { _scroll.verticalNormalizedPosition = 1f; }
        }

        /// <summary>Toggles between "fits my bike" and every catalog part.</summary>
        public void SetFitsBikeOnly(bool fitsOnly)
        {
            _fitsOnly = fitsOnly;
            _hasRenderedCatalog = false;
            Refresh();
            if (_scroll != null) { _scroll.verticalNormalizedPosition = 1f; }
        }

        /// <summary>Sets displayed cash.</summary>
        public void SetCash(decimal cashUsd)
        {
            _cash = cashUsd;
            _hasRenderedCatalog = false;
            Refresh();
        }

        /// <summary>Forces the next <see cref="RenderCatalog(IReadOnlyList{PartDefinition}, SimulationWorld, BikeSimulation, string)"/> to rebuild.</summary>
        public void InvalidateCache()
        {
            _hasRenderedCatalog = false;
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

        /// <summary>Raises SwapMeetRequested.</summary>
        public void TriggerSwapMeet()
        {
            Action handler = SwapMeetRequested;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>Which tab a catalog part is listed under, or null when it is not for sale (garage fixtures).</summary>
        public static ShopTab? TabOf(PartDefinition part)
        {
            if (part == null) { return null; }
            if (part.Stats.IsTool || part.Stats.IsSpare) { return ShopTab.ToolsSpares; }
            switch (part.Category)
            {
                case PartCategory.EngineCase:
                case PartCategory.Cylinder:
                case PartCategory.CylinderHead:
                case PartCategory.Piston:
                case PartCategory.SparkPlug:
                case PartCategory.Ignition:
                case PartCategory.Gasket:
                    return ShopTab.Engine;
                case PartCategory.IntakeManifold:
                case PartCategory.Carburetor:
                case PartCategory.AirFilter:
                case PartCategory.MainJet:
                case PartCategory.PilotJet:
                case PartCategory.FuelTank:
                case PartCategory.Petcock:
                case PartCategory.InlineFilter:
                case PartCategory.FuelLine:
                case PartCategory.PremixOil:
                case PartCategory.Consumable:
                    return ShopTab.FuelCarb;
                case PartCategory.Exhaust:
                    return ShopTab.Exhaust;
                case PartCategory.Clutch:
                case PartCategory.Variator:
                case PartCategory.Gearbox:
                case PartCategory.DriveBelt:
                case PartCategory.Chain:
                case PartCategory.SprocketFront:
                case PartCategory.SprocketRear:
                case PartCategory.VariatorRoller:
                    return ShopTab.Drivetrain;
                case PartCategory.CargoRack:
                    return ShopTab.Cargo;
                case PartCategory.ToolRollItem:
                    return ShopTab.ToolsSpares;
                case PartCategory.GarageStation:
                    return null;
                default:
                    return ShopTab.Chassis;
            }
        }

        /// <summary>Builds the centered shop modal under <paramref name="root"/>. Idempotent for the same parent.</summary>
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
            Font font = MenuWidgets.LoadFont();

            RectTransform card = MenuWidgets.CreateCard(parent, "PartsCatalogShop_Modal", new Vector2(1120f, 760f), CardColor, 20, 10f);
            _modalRoot = card.gameObject;

            // Header: title, cash, swap meet, close.
            GameObject headerRow = CreateRow(card, "HeaderRow", 44f, 10f);
            Text title = MenuWidgets.CreateLabel(headerRow.transform, "Title", "PARTS SHOP", 28, TextAnchor.MiddleLeft, Color.white, 44f, font);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _cashLabel = MenuWidgets.CreateLabel(headerRow.transform, "CashHeader", FormatCash(_cash), 28, TextAnchor.MiddleRight, CashColor, 44f, font);
            _cashLabel.GetComponent<LayoutElement>().preferredWidth = 200f;
            Text swapLabel;
            _swapMeetButton = MenuWidgets.CreateButton(headerRow.transform, "Btn_SwapMeet", "Swap Meet", new Color(0.36f, 0.30f, 0.52f, 1f), 18, 44f, font, TriggerSwapMeet, out swapLabel);
            _swapMeetButton.GetComponent<LayoutElement>().preferredWidth = 150f;
            Text closeLabel;
            _closeButton = MenuWidgets.CreateButton(headerRow.transform, "Btn_Close", "Close", new Color(0.46f, 0.24f, 0.24f, 1f), 18, 44f, font, TriggerClose, out closeLabel);
            _closeButton.GetComponent<LayoutElement>().preferredWidth = 110f;

            _feedbackLabel = MenuWidgets.CreateLabel(card, "Feedback", _displayedFeedback, 18, TextAnchor.MiddleLeft, HudStyle.Warn, 28f, font);

            // Category tabs plus the "fits my bike" toggle.
            GameObject tabRow = CreateRow(card, "TabRow", 42f, 6f);
            for (int i = 0; i < TabCount; i++)
            {
                ShopTab tab = (ShopTab)i;
                Text tabText;
                Button tabBtn = MenuWidgets.CreateButton(tabRow.transform, "Tab_" + tab, TabLabels[i], TabIdle, 16, 42f, font, delegate { SelectTab(tab); }, out tabText);
                tabBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
                _tabButtons[i] = tabBtn;
                _tabImages[i] = tabBtn.GetComponent<Image>();
            }

            _fitsButton = MenuWidgets.CreateButton(tabRow.transform, "Btn_FitsToggle", string.Empty, new Color(0.26f, 0.34f, 0.30f, 1f), 16, 42f, font, ToggleFits, out _fitsLabel);
            _fitsButton.GetComponent<LayoutElement>().preferredWidth = 190f;

            BuildScrollArea(card, font);

            MenuWidgets.CreateLabel(card, "Hint", "Arrows / D-pad: move     Enter / A: select     Esc / Start: close", 15, TextAnchor.MiddleCenter, HudStyle.TextDim, 22f, font);

            _hasRenderedCatalog = false;
            Refresh();
            ApplyTabColors();
        }

        /// <summary>Renders the live catalog from a <see cref="CatalogDatabase"/> (see the list overload).</summary>
        public void RenderCatalog(CatalogDatabase catalog, SimulationWorld world, BikeSimulation bike, string feedback)
        {
            RenderCatalog(catalog != null ? catalog.PartList : null, world, bike, feedback);
        }

        /// <summary>
        /// Renders the part list, player cash, garage inventory counts, and mounted part status. Dirty-checked so steady-state
        /// repeat calls skip layout / string rebuilds (call <see cref="InvalidateCache"/> after an action that keeps the counts equal).
        /// </summary>
        public void RenderCatalog(IReadOnlyList<PartDefinition> parts, SimulationWorld world, BikeSimulation bike, string feedback)
        {
            int catalogCount = parts != null ? parts.Count : 0;
            decimal cash = world != null ? world.CashUsd : _cash;
            int inventoryCount = world != null ? world.Inventory.Count : 0;
            int mountedCount = CountMountedParts(bike);
            string safeFeedback = feedback ?? string.Empty;

            if (_hasRenderedCatalog
                && ReferenceEquals(bike, _bike)
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

            _bike = bike;
            _cash = cash;
            _displayedFeedback = safeFeedback;
            if (_feedbackLabel != null)
            {
                _feedbackLabel.text = _displayedFeedback;
            }

            CopyCatalog(parts);

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

        /// <summary>Gives keyboard / gamepad focus to the active category tab.</summary>
        public void FocusDefault()
        {
            int idx = (int)_tab;
            if (idx >= 0 && idx < TabCount) { MenuWidgets.Focus(_tabButtons[idx]); }
        }

        private void OnEnable()
        {
            _lastSelected = null;
            FocusDefault();
        }

        private void Update()
        {
            // Keep the keyboard / gamepad selection scrolled into view. Reference compare only: allocation-free when idle.
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (ReferenceEquals(sel, _lastSelected) || _scroll == null || _generatedListRoot == null)
            {
                return;
            }

            _lastSelected = sel;
            if (sel == null) { return; }
            Transform t = sel.transform;
            while (t != null && t.parent != _generatedListRoot) { t = t.parent; }
            if (t == null) { return; }

            int index = t.GetSiblingIndex();
            int active = _visible.Count;
            float step = RowHeight + RowSpacing;
            float contentH = active * step;
            float viewH = _viewport != null ? _viewport.rect.height : 0f;
            float scrollable = contentH - viewH;
            if (scrollable <= 0f) { return; }

            float top = index * step;
            float bottom = top + RowHeight;
            float offset = (1f - _scroll.verticalNormalizedPosition) * scrollable;
            if (top < offset) { offset = top; }
            else if (bottom > offset + viewH) { offset = bottom - viewH; }
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(offset / scrollable);
        }

        private void CopyCatalog(IReadOnlyList<PartDefinition> parts)
        {
            _catalog.Clear();
            if (parts == null) { return; }
            for (int i = 0; i < parts.Count; i++) { _catalog.Add(parts[i]); }
        }

        private void ToggleFits()
        {
            SetFitsBikeOnly(!_fitsOnly);
        }

        private static bool IsConsumable(PartDefinition part)
        {
            return part.Category == PartCategory.PremixOil || part.Category == PartCategory.Consumable;
        }

        private bool Fits(PartDefinition part)
        {
            // Tools, spares and consumables ride in the tool roll / crate and never mount on a slot.
            if (part.Stats.IsTool || part.Stats.IsSpare || part.Category == PartCategory.ToolRollItem || IsConsumable(part)) { return true; }
            if (_bike == null) { return true; }
            return _bike.Assembly.FindCompatibleSlotId(part) != null;
        }

        private void Refresh()
        {
            _visible.Clear();
            for (int i = 0; i < _catalog.Count; i++)
            {
                PartDefinition part = _catalog[i];
                if (part == null) { continue; }
                ShopTab? tab = TabOf(part);
                if (tab == null) { continue; }
                if (_tab != ShopTab.All && tab.Value != _tab) { continue; }
                if (_filterActive && part.Category != _filter) { continue; }
                if (_fitsOnly && !Fits(part)) { continue; }
                _visible.Add(part);
            }

            if (_cashLabel != null) { _cashLabel.text = FormatCash(_cash); }
            if (_fitsLabel != null) { _fitsLabel.text = _fitsOnly ? "Fits my bike: ON" : "Fits my bike: OFF"; }
            ApplyTabColors();

            RefreshLegacyTemplateRows();
            RefreshGeneratedRows();
            if (_emptyLabel != null) { _emptyLabel.gameObject.SetActive(_visible.Count == 0 && _generatedListRoot != null); }
        }

        private void ApplyTabColors()
        {
            for (int i = 0; i < TabCount; i++)
            {
                if (_tabImages[i] != null) { _tabImages[i].color = i == (int)_tab ? TabActive : TabIdle; }
            }
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
                bool tool = part.Stats.IsTool || part.Stats.IsSpare || part.Category == PartCategory.ToolRollItem;
                string slotId = !tool && _bike != null ? _bike.Assembly.FindCompatibleSlotId(part) : null;
                bool consumable = IsConsumable(part);
                bool fits = tool || consumable || _bike == null || slotId != null;
                bool afford = CanAfford(part);
                bool slotTaken = slotId != null && _bike.Assembly.IsSlotOccupied(slotId);

                _sb.Length = 0;
                _sb.Append("<b>").Append(part.DisplayName).Append("</b>\n<color=#B4C2D2>");
                AppendSummary(_sb, part);
                _sb.Append("</color>");
                row.InfoText.text = _sb.ToString();

                row.PriceText.text = FormatCash(part.PriceUsd);
                row.PriceText.color = afford ? Color.white : HudStyle.Bad;

                _sb.Length = 0;
                if (!fits) { _sb.Append("<color=#F9C233>Won't fit this bike</color>"); }
                else if (!afford) { _sb.Append("<color=#F5483D>Can't afford</color>"); }
                else { _sb.Append("<color=#B4C2D2>For sale</color>"); }
                if (mounted) { _sb.Append("\n<color=#73B8FA>Installed</color>"); }
                if (owned > 0) { _sb.Append("\n<color=#4DDB6B>Owned x").Append(owned).Append("</color>"); }
                row.StateText.text = _sb.ToString();

                row.BuyButton.interactable = afford && fits;
                row.BuyButtonLabel.text = "Buy";

                row.InstallButton.interactable = owned > 0 && fits && !consumable;
                row.InstallButtonLabel.text = tool ? "Pack" : (consumable ? "In crate" : (slotTaken ? "Swap" : "Install"));
            }

            for (int i = _visible.Count; i < _generatedRows.Count; i++)
            {
                if (_generatedRows[i].Root != null && _generatedRows[i].Root.activeSelf)
                {
                    _generatedRows[i].Root.SetActive(false);
                }
            }
        }

        private static void AppendSummary(StringBuilder sb, PartDefinition part)
        {
            PartStats s = part.Stats;
            CultureInfo ci = CultureInfo.InvariantCulture;
            switch (part.Category)
            {
                case PartCategory.Cylinder:
                case PartCategory.Piston:
                    if (s.DisplacementCc > 0f) { sb.Append(s.DisplacementCc.ToString("0.#", ci)).Append(" cc  "); }
                    if (s.CompressionRatio > 0f) { sb.Append("CR ").Append(s.CompressionRatio.ToString("0.#", ci)).Append(":1  "); }
                    break;
                case PartCategory.CylinderHead:
                    if (s.CompressionRatio > 0f) { sb.Append("CR ").Append(s.CompressionRatio.ToString("0.#", ci)).Append(":1  "); }
                    break;
                case PartCategory.Carburetor:
                    sb.Append("Venturi ").Append(s.VenturiMm.ToString("0.#", ci)).Append(" mm  ");
                    break;
                case PartCategory.MainJet:
                    sb.Append("Main jet #").Append(s.MainJetSize).Append("  ");
                    break;
                case PartCategory.PilotJet:
                    sb.Append("Pilot jet #").Append(s.PilotJetSize).Append("  ");
                    break;
                case PartCategory.Exhaust:
                    sb.Append("Peak ").Append((s.PeakRpmStart / 1000f).ToString("0.#", ci)).Append('-').Append((s.PeakRpmEnd / 1000f).ToString("0.#", ci))
                        .Append("k rpm  ").Append(s.NoiseDb.ToString("0", ci)).Append(" dB  ");
                    break;
                case PartCategory.SparkPlug:
                    sb.Append("Heat range ").Append(s.HeatRange).Append("  ");
                    break;
                case PartCategory.Ignition:
                    sb.Append(s.IgnitionType == IgnitionType.Cdi12V ? "12V CDI  " : "6V points  ");
                    break;
                case PartCategory.FuelTank:
                    sb.Append(s.TankCapacityLiters.ToString("0.#", ci)).Append(" L  ");
                    break;
                case PartCategory.AirFilter:
                    sb.Append("Restriction ").Append(s.AirFilterRestriction.ToString("0.##", ci)).Append("  ");
                    break;
                case PartCategory.SprocketFront:
                    sb.Append(s.FrontSprocketTeeth).Append("T  ");
                    break;
                case PartCategory.SprocketRear:
                    sb.Append(s.RearSprocketTeeth).Append("T  ");
                    break;
                case PartCategory.VariatorRoller:
                    sb.Append(s.RollerWeightGrams.ToString("0.#", ci)).Append(" g rollers  ");
                    break;
                case PartCategory.CargoRack:
                    sb.Append(s.CargoSlots).Append(" slots  ").Append(s.CargoMaxKg.ToString("0.#", ci)).Append(" kg max  ");
                    break;
                default:
                    if (s.IsTool) { sb.Append("Tool  "); }
                    else if (s.IsSpare) { sb.Append("Spare  "); }
                    break;
            }

            sb.Append(part.MassKg.ToString("0.0#", ci)).Append(" kg");
        }

        private static string FormatCash(decimal value)
        {
            return "$" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void BuildScrollArea(RectTransform card, Font font)
        {
            GameObject scrollGo = new GameObject("ShopScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            scrollGo.transform.SetParent(card, false);
            scrollGo.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.9f);
            LayoutElement le = scrollGo.GetComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 300f;

            GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            _viewport = (RectTransform)viewportGo.transform;
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(6f, 6f);
            _viewport.offsetMax = new Vector2(-6f, -6f);

            GameObject contentGo = new GameObject("ShopRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(_viewport, false);
            RectTransform content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup v = contentGo.GetComponent<VerticalLayoutGroup>();
            v.spacing = RowSpacing;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _generatedListRoot = content;

            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            _emptyLabel = MenuWidgets.CreateLabel(scrollGo.transform, "EmptyLabel", "Nothing in this category fits your bike. Turn off \"Fits my bike\" to see everything.", 18, TextAnchor.MiddleCenter, HudStyle.TextDim, 40f, font);
            _emptyLabel.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform ert = (RectTransform)_emptyLabel.transform;
            ert.anchorMin = Vector2.zero;
            ert.anchorMax = Vector2.one;
            ert.offsetMin = Vector2.zero;
            ert.offsetMax = Vector2.zero;
            _emptyLabel.gameObject.SetActive(false);
        }

        private static GameObject CreateRow(Transform parent, string name, float height, float spacing)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleHeight = 0f; // the row's child-force-expand would otherwise make it grow and starve the list
            HorizontalLayoutGroup h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return go;
        }

        private GeneratedPartRow GetOrCreateGeneratedRow(int index)
        {
            while (_generatedRows.Count <= index)
            {
                Font font = MenuWidgets.LoadFont();
                GeneratedPartRow entry = new GeneratedPartRow();
                GameObject rowGo = new GameObject("PartRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_generatedListRoot, false);
                rowGo.GetComponent<Image>().color = RowColor;
                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = RowHeight;
                le.minHeight = RowHeight;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(12, 10, 5, 5);
                hLayout.spacing = 10f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                Text infoText = MenuWidgets.CreateLabel(rowGo.transform, "Info", string.Empty, 17, TextAnchor.MiddleLeft, Color.white, RowHeight, font);
                infoText.GetComponent<LayoutElement>().flexibleWidth = 1f;
                infoText.horizontalOverflow = HorizontalWrapMode.Wrap;
                infoText.verticalOverflow = VerticalWrapMode.Truncate;

                Text priceText = MenuWidgets.CreateLabel(rowGo.transform, "Price", string.Empty, 20, TextAnchor.MiddleRight, Color.white, RowHeight, font);
                priceText.GetComponent<LayoutElement>().preferredWidth = 100f;

                Text stateText = MenuWidgets.CreateLabel(rowGo.transform, "State", string.Empty, 15, TextAnchor.MiddleLeft, Color.white, RowHeight, font);
                stateText.GetComponent<LayoutElement>().preferredWidth = 150f;
                stateText.verticalOverflow = VerticalWrapMode.Overflow;

                GeneratedPartRow captured = entry;
                Text buyLabel;
                Button buyBtn = MenuWidgets.CreateButton(rowGo.transform, "Btn_Buy", "Buy", BuyColor, 17, RowHeight, font, delegate { TriggerBuyPart(captured.BoundPartId); }, out buyLabel);
                buyBtn.GetComponent<LayoutElement>().preferredWidth = 92f;

                Text installLabel;
                Button installBtn = MenuWidgets.CreateButton(rowGo.transform, "Btn_Install", "Install", InstallColor, 17, RowHeight, font, delegate { TriggerInstallPart(captured.BoundPartId); }, out installLabel);
                installBtn.GetComponent<LayoutElement>().preferredWidth = 104f;

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.PriceText = priceText;
                entry.StateText = stateText;
                entry.BuyButton = buyBtn;
                entry.BuyButtonLabel = buyLabel;
                entry.InstallButton = installBtn;
                entry.InstallButtonLabel = installLabel;
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
