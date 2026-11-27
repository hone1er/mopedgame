using System;
using System.Collections.Generic;
using System.Text;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Parts Alley swap meet stall. Lists the Core <see cref="SwapMeetMarket"/> rotation, lets the player inspect a selected offer
    /// (the market decides what the inspection reveals) and raises <see cref="PurchaseRequested"/> / <see cref="BuyOfferRequested"/>;
    /// MopedSim.App performs the purchase through <see cref="SwapMeetMarket.TryBuyOffer"/>, so pricing, defects, and cash rules never
    /// live here. Labels are rebuilt only on refresh / selection / result events (never per frame).
    /// </summary>
    public sealed class SwapMeetView : MonoBehaviour
    {
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        private sealed class SwapMeetRowEntry
        {
            public GameObject Root;
            public Text InfoText;
            public Button InspectButton;
            public Button BuyButton;
            public Text BuyButtonLabel;
            public string BoundOfferId = string.Empty;
        }

        [SerializeField] private Text _offersLabel;
        [SerializeField] private Text _selectionLabel;
        [SerializeField] private Text _inspectionLabel;
        [SerializeField] private Text _resultLabel;

        private readonly List<SwapMeetOffer> _offers = new List<SwapMeetOffer>(8);
        private readonly List<SwapMeetRowEntry> _rowPool = new List<SwapMeetRowEntry>(8);
        private readonly StringBuilder _rowSb = new StringBuilder(160);

        private GameObject _modalRoot;
        private Transform _builtParent;
        private RectTransform _offerListRoot;
        private Button _rotateButton;
        private Button _closeButton;
        private Font _font;

        private SwapMeetMarket _market;
        private int _selectedIndex = -1;
        private SwapMeetInspection _lastInspection;
        private bool _hasInspection;

        /// <summary>Raised with the selected offer id when the player asks to buy it.</summary>
        public event Action<string> PurchaseRequested;

        /// <summary>Raised with the offer id when the player clicks an offer row's Inspect button.</summary>
        public event Action<string> InspectOfferRequested;

        /// <summary>Raised with the offer id when the player clicks an offer row's Buy button.</summary>
        public event Action<string> BuyOfferRequested;

        /// <summary>Raised when the player clicks Rotate Offerings.</summary>
        public event Action RotateRequested;

        /// <summary>Raised when the player closes the swap meet modal.</summary>
        public event Action CloseRequested;

        public SwapMeetMarket Market
        {
            get { return _market; }
        }

        public int OfferCount
        {
            get { return _offers.Count; }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
        }

        /// <summary>Selected offer, or null when nothing is selected.</summary>
        public SwapMeetOffer SelectedOffer
        {
            get { return _selectedIndex >= 0 && _selectedIndex < _offers.Count ? _offers[_selectedIndex] : null; }
        }

        public bool HasInspection
        {
            get { return _hasInspection; }
        }

        public SwapMeetInspection LastInspection
        {
            get { return _lastInspection; }
        }

        public string OffersText
        {
            get { return _offersLabel != null ? _offersLabel.text : string.Empty; }
        }

        public SwapMeetOffer GetOffer(int index)
        {
            return index >= 0 && index < _offers.Count ? _offers[index] : null;
        }

        public void SetMarket(SwapMeetMarket market)
        {
            _market = market;
            RefreshOffers();
        }

        /// <summary>
        /// Builds a dark interactive swap meet modal under <paramref name="root"/>.
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
                _rowPool.Clear();
            }

            _builtParent = parent;
            _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);

            _modalRoot = new GameObject("SwapMeet_Modal", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _modalRoot.transform.SetParent(parent, false);
            RectTransform modalRt = (RectTransform)_modalRoot.transform;
            modalRt.anchorMin = new Vector2(0.5f, 0.5f);
            modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.pivot = new Vector2(0.5f, 0.5f);
            modalRt.anchoredPosition = Vector2.zero;
            modalRt.sizeDelta = new Vector2(780f, 520f);

            _modalRoot.GetComponent<Image>().color = new Color(0.09f, 0.09f, 0.11f, 0.95f);

            VerticalLayoutGroup vLayout = _modalRoot.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 14, 14);
            vLayout.spacing = 8f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;

            CreateLabel(_modalRoot.transform, "Title", "PARTS ALLEY SWAP MEET", 20, TextAnchor.MiddleLeft, Color.white, 30f, _font);

            // Scaffolded stub labels are hidden: always bind fresh ones inside the generated modal.
            _selectionLabel = CreateLabel(_modalRoot.transform, "SelectionLabel", string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.85f, 0.90f, 0.96f, 1f), 22f, _font);

            _inspectionLabel = CreateLabel(_modalRoot.transform, "InspectionLabel", string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.96f, 0.85f, 0.45f, 1f), 22f, _font);

            _resultLabel = CreateLabel(_modalRoot.transform, "ResultLabel", string.Empty, 14, TextAnchor.MiddleLeft, new Color(0.55f, 0.92f, 0.62f, 1f), 22f, _font);

            GameObject listGo = new GameObject("OfferRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listGo.transform.SetParent(_modalRoot.transform, false);
            LayoutElement listLe = listGo.GetComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 260f;
            VerticalLayoutGroup listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 6f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _offerListRoot = (RectTransform)listGo.transform;

            GameObject footerGo = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footerGo.transform.SetParent(_modalRoot.transform, false);
            footerGo.GetComponent<LayoutElement>().preferredHeight = 44f;
            HorizontalLayoutGroup footerLayout = footerGo.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 12f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            _rotateButton = CreateButton(footerGo.transform, "Btn_RotateOfferings", "Rotate Offerings", new Color(0.24f, 0.36f, 0.56f, 1f), _font, TriggerRotate);
            _closeButton = CreateButton(footerGo.transform, "Btn_Close", "Close", new Color(0.28f, 0.26f, 0.28f, 1f), _font, TriggerClose);

            RenderSelection();
        }

        private void OnEnable()
        {
            if (_rowPool.Count > 0 && _rowPool[0].Root != null && _rowPool[0].Root.activeSelf)
            {
                MenuWidgets.Focus(_rowPool[0].InspectButton);
            }
            else
            {
                MenuWidgets.Focus(_rotateButton);
            }
        }

        /// <summary>Re-reads the market's current rotation (call after a rotation advance or a purchase).</summary>
        public void RefreshOffers()
        {
            string selectedId = SelectedOffer != null ? SelectedOffer.OfferId : null;
            _offers.Clear();
            _selectedIndex = -1;
            if (_market != null)
            {
                IReadOnlyList<SwapMeetOffer> offers = _market.Offers;
                for (int i = 0; i < offers.Count; i++)
                {
                    SwapMeetOffer offer = offers[i];
                    if (offer == null)
                    {
                        continue;
                    }

                    _offers.Add(offer);
                    if (selectedId != null && string.Equals(offer.OfferId, selectedId, StringComparison.Ordinal))
                    {
                        _selectedIndex = _offers.Count - 1;
                    }
                }
            }

            if (_selectedIndex < 0)
            {
                _hasInspection = false;
            }

            RenderSelection();
        }

        public bool SelectOffer(int index)
        {
            if (index < 0 || index >= _offers.Count)
            {
                return false;
            }

            if (index != _selectedIndex)
            {
                _selectedIndex = index;
                _hasInspection = false;
                RenderSelection();
            }

            return true;
        }

        public bool SelectOffer(string offerId)
        {
            for (int i = 0; i < _offers.Count; i++)
            {
                if (string.Equals(_offers[i].OfferId, offerId, StringComparison.Ordinal))
                {
                    return SelectOffer(i);
                }
            }

            return false;
        }

        /// <summary>Asks the market to inspect the selected offer (the market owns the reveal rule).</summary>
        public bool InspectSelectedOffer()
        {
            SwapMeetOffer offer = SelectedOffer;
            if (_market == null || offer == null)
            {
                return false;
            }

            _lastInspection = _market.InspectOffer(offer.OfferId);
            _hasInspection = true;
            if (_inspectionLabel != null)
            {
                _inspectionLabel.text = _lastInspection.Description;
            }

            RenderOffers();
            return _lastInspection.Succeeded;
        }

        /// <summary>Raises <see cref="PurchaseRequested"/> for the selected unsold offer. Returns true when raised.</summary>
        public bool RequestPurchase()
        {
            SwapMeetOffer offer = SelectedOffer;
            if (offer == null || offer.IsSold)
            {
                return false;
            }

            Action<string> handler = PurchaseRequested;
            if (handler != null)
            {
                handler.Invoke(offer.OfferId);
            }

            return true;
        }

        /// <summary>Raises InspectOfferRequested for <paramref name="offerId"/> (falls back to InspectSelectedOffer if no listener).</summary>
        public void TriggerInspectOffer(string offerId)
        {
            if (string.IsNullOrEmpty(offerId))
            {
                return;
            }

            SelectOffer(offerId);
            Action<string> handler = InspectOfferRequested;
            if (handler != null)
            {
                handler.Invoke(offerId);
                return;
            }

            InspectSelectedOffer();
        }

        /// <summary>Raises BuyOfferRequested for <paramref name="offerId"/> (falls back to RequestPurchase if no listener).</summary>
        public void TriggerBuyOffer(string offerId)
        {
            if (string.IsNullOrEmpty(offerId))
            {
                return;
            }

            SelectOffer(offerId);
            Action<string> handler = BuyOfferRequested;
            if (handler != null)
            {
                handler.Invoke(offerId);
                return;
            }

            RequestPurchase();
        }

        /// <summary>Raises RotateRequested.</summary>
        public void TriggerRotate()
        {
            Action handler = RotateRequested;
            if (handler != null)
            {
                handler();
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

        /// <summary>Shows the outcome of App's purchase attempt and re-reads the rotation.</summary>
        public void ShowPurchaseResult(in SwapMeetPurchaseResult result)
        {
            if (_resultLabel != null)
            {
                _resultLabel.text = result.Message;
            }

            RefreshOffers();
        }

        private void RenderOffers()
        {
            if (_offersLabel != null)
            {
                StringBuilder sb = new StringBuilder(64 * (_offers.Count + 1));
                for (int i = 0; i < _offers.Count; i++)
                {
                    SwapMeetOffer offer = _offers[i];
                    sb.Append(i == _selectedIndex ? "> " : "  ");
                    sb.Append(offer.Part != null ? offer.Part.DisplayName : offer.PartId);
                    sb.Append("  $").Append(offer.AskingPriceUsd.ToString("F2"));
                    sb.Append(" (list $").Append(offer.ListPriceUsd.ToString("F2")).Append(')');
                    if (offer.IsSold)
                    {
                        sb.Append("  SOLD");
                    }
                    else if (offer.IsInspected)
                    {
                        sb.Append("  inspected");
                    }

                    sb.Append('\n');
                }

                _offersLabel.text = sb.ToString();
            }

            RefreshInteractiveRows();
        }

        private void RefreshInteractiveRows()
        {
            if (_offerListRoot == null)
            {
                return;
            }

            for (int i = 0; i < _offers.Count; i++)
            {
                SwapMeetOffer offer = _offers[i];
                SwapMeetRowEntry row = GetOrCreateRow(i);
                row.BoundOfferId = offer.OfferId;
                row.Root.name = "OfferRow_" + offer.OfferId;
                row.Root.SetActive(true);

                _rowSb.Length = 0;
                _rowSb.Append(offer.Part != null ? offer.Part.DisplayName : offer.PartId);
                _rowSb.Append("  $").Append(offer.AskingPriceUsd.ToString("F2"));
                _rowSb.Append(" (list $").Append(offer.ListPriceUsd.ToString("F2")).Append(')');
                _rowSb.Append("  ").Append(Mathf.RoundToInt(offer.Condition * 100f)).Append('%');
                if (offer.IsSold)
                {
                    _rowSb.Append("  [SOLD]");
                }
                else if (offer.IsInspected)
                {
                    _rowSb.Append(offer.RevealedDefect == SwapMeetDefect.None
                        ? "  [Clean]"
                        : "  [" + offer.RevealedDefect.ToString() + "]");
                }

                row.InfoText.text = _rowSb.ToString();
                row.InspectButton.interactable = !offer.IsSold;
                row.BuyButton.interactable = !offer.IsSold;
                row.BuyButtonLabel.text = offer.IsSold ? "Sold" : "Buy";
            }

            for (int i = _offers.Count; i < _rowPool.Count; i++)
            {
                if (_rowPool[i].Root != null && _rowPool[i].Root.activeSelf)
                {
                    _rowPool[i].Root.SetActive(false);
                }
            }
        }

        private SwapMeetRowEntry GetOrCreateRow(int index)
        {
            while (_rowPool.Count <= index)
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                }

                SwapMeetRowEntry entry = new SwapMeetRowEntry();
                GameObject rowGo = new GameObject("OfferRow", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                rowGo.transform.SetParent(_offerListRoot, false);
                rowGo.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f, 0.94f);
                LayoutElement le = rowGo.GetComponent<LayoutElement>();
                le.preferredHeight = 44f;
                le.minHeight = 38f;

                HorizontalLayoutGroup hLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
                hLayout.padding = new RectOffset(8, 8, 4, 4);
                hLayout.spacing = 8f;
                hLayout.childControlWidth = true;
                hLayout.childControlHeight = true;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = true;

                Text infoText = CreateLabel(rowGo.transform, "Info", string.Empty, 14, TextAnchor.MiddleLeft, Color.white, 34f, _font);
                infoText.GetComponent<LayoutElement>().flexibleWidth = 1f;

                Button inspectBtn = CreateButton(rowGo.transform, "Btn_Inspect", "Inspect", new Color(0.26f, 0.42f, 0.62f, 1f), _font, null);
                inspectBtn.GetComponent<LayoutElement>().preferredWidth = 96f;

                Button buyBtn = CreateButton(rowGo.transform, "Btn_Buy", "Buy", new Color(0.20f, 0.52f, 0.28f, 1f), _font, null);
                buyBtn.GetComponent<LayoutElement>().preferredWidth = 88f;

                SwapMeetRowEntry captured = entry;
                inspectBtn.onClick.AddListener(delegate { TriggerInspectOffer(captured.BoundOfferId); });
                buyBtn.onClick.AddListener(delegate { TriggerBuyOffer(captured.BoundOfferId); });

                entry.Root = rowGo;
                entry.InfoText = infoText;
                entry.InspectButton = inspectBtn;
                entry.BuyButton = buyBtn;
                entry.BuyButtonLabel = buyBtn.GetComponentInChildren<Text>();
                _rowPool.Add(entry);
            }

            return _rowPool[index];
        }

        private void RenderSelection()
        {
            SwapMeetOffer offer = SelectedOffer;
            if (_selectionLabel != null)
            {
                _selectionLabel.text = offer == null
                    ? string.Empty
                    : (offer.Part != null ? offer.Part.DisplayName : offer.PartId) + " — condition " + Mathf.RoundToInt(offer.Condition * 100f) + "%";
            }

            if (_inspectionLabel != null && !_hasInspection)
            {
                _inspectionLabel.text = string.Empty;
            }

            RenderOffers();
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
