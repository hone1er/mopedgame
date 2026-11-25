using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>
    /// Pre-ride Tool Roll packing overlay (base 6 slots). Raises pack/unpack intents; the App layer decides.
    /// </summary>
    public sealed class ToolRollOverlayView : MonoBehaviour
    {
        [SerializeField] private Text _slotsLabel;
        [SerializeField] private Text _itemsList;
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Button _rowTemplate;

        private readonly List<string> _available = new List<string>();
        private readonly List<string> _packed = new List<string>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _capacity = 6;

        /// <summary>Raised when the player asks to pack an item.</summary>
        public event Action<string> PackRequested;

        /// <summary>Raised when the player asks to unpack an item.</summary>
        public event Action<string> UnpackRequested;

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

        /// <summary>Sets the items offered for packing and rebuilds rows.</summary>
        public void SetAvailableItems(IReadOnlyList<string> itemIds)
        {
            _available.Clear();
            if (itemIds != null)
            {
                for (int i = 0; i < itemIds.Count; i++) { _available.Add(itemIds[i]); }
            }
            RebuildRows();
        }

        /// <summary>Displays the current packed loadout.</summary>
        public void Render(IReadOnlyList<string> packedItemIds, int capacity)
        {
            _capacity = Mathf.Max(0, capacity);
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
                var names = new List<string>(_packed.Count);
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

        private void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) { UnityEngine.Object.Destroy(_rows[i]); }
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
    }
}
