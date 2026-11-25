using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>
    /// Parts terminal / swap meet listing. Raises purchase intents only; App owns the economy.
    /// </summary>
    public sealed class PartsCatalogShopView : MonoBehaviour
    {
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Button _rowTemplate;
        [SerializeField] private Text _cashLabel;

        private readonly List<PartDefinition> _catalog = new List<PartDefinition>();
        private readonly List<PartDefinition> _visible = new List<PartDefinition>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private bool _filterActive;
        private PartCategory _filter;
        private decimal _cash;

        /// <summary>Raised when the player asks to buy an affordable part.</summary>
        public event Action<PartDefinition> PurchaseRequested;

        /// <summary>Number of parts passing the current filter.</summary>
        public int VisibleCount { get { return _visible.Count; } }

        /// <summary>Stores a copy of the catalog and rebuilds the list.</summary>
        public void SetCatalog(IReadOnlyList<PartDefinition> parts)
        {
            _catalog.Clear();
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++) { _catalog.Add(parts[i]); }
            }
            Refresh();
        }

        /// <summary>Filters the list to one category.</summary>
        public void SetCategoryFilter(PartCategory category)
        {
            _filterActive = true;
            _filter = category;
            Refresh();
        }

        /// <summary>Removes any category filter.</summary>
        public void ClearCategoryFilter()
        {
            _filterActive = false;
            Refresh();
        }

        /// <summary>Sets displayed cash.</summary>
        public void SetCash(decimal cashUsd)
        {
            _cash = cashUsd;
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

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) { UnityEngine.Object.Destroy(_rows[i]); }
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
    }
}
