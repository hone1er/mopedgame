using System;
using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Parts Alley swap meet stall. Lists the Core <see cref="SwapMeetMarket"/> rotation, lets the player inspect a selected offer
    /// (the market decides what the inspection reveals) and raises <see cref="PurchaseRequested"/>; MopedSim.App performs the
    /// purchase through <see cref="SwapMeetMarket.TryBuyOffer"/>, so pricing, defects, and cash rules never live here. Labels are
    /// rebuilt only on refresh / selection / result events (never per frame).
    /// </summary>
    public sealed class SwapMeetView : MonoBehaviour
    {
        [SerializeField] private Text _offersLabel;
        [SerializeField] private Text _selectionLabel;
        [SerializeField] private Text _inspectionLabel;
        [SerializeField] private Text _resultLabel;

        private readonly List<SwapMeetOffer> _offers = new List<SwapMeetOffer>(8);
        private SwapMeetMarket _market;
        private int _selectedIndex = -1;
        private SwapMeetInspection _lastInspection;
        private bool _hasInspection;

        /// <summary>Raised with the selected offer id when the player asks to buy it.</summary>
        public event Action<string> PurchaseRequested;

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
            if (_offersLabel == null)
            {
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(64 * (_offers.Count + 1));
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
    }
}
