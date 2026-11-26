#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Loose parts owned by the player (the milk-crate parts inventory). Deterministic instance ids via an internal counter.
    /// Persisted by MopedSim.Data as SaveState.inventory (partId, condition, wear).
    /// </summary>
    public sealed class GarageInventory
    {
        private readonly List<PartInstance> _items = new List<PartInstance>();
        private int _nextSerial = 1;

        public IReadOnlyList<PartInstance> Items
        {
            get { return _items; }
        }

        public int Count
        {
            get { return _items.Count; }
        }

        /// <summary>
        /// Returns a fresh, unused instance id of the form "{prefix}_{serial:000}".
        /// </summary>
        public string NextInstanceId(string prefix)
        {
            string safe = prefix == null || prefix.Length == 0 ? "part" : prefix;
            while (true)
            {
                string candidate = safe + "_" + _nextSerial.ToString("000");
                _nextSerial++;
                if (FindByInstanceId(candidate) == null)
                {
                    return candidate;
                }
            }
        }

        public void Add(PartInstance part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }
            if (part.InstanceId.Length == 0 || FindByInstanceId(part.InstanceId) != null)
            {
                part.InstanceId = NextInstanceId(part.PartId);
            }
            _items.Add(part);
        }

        public bool Remove(PartInstance part)
        {
            return part != null && _items.Remove(part);
        }

        public PartInstance? FindByInstanceId(string instanceId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return _items[i];
                }
            }
            return null;
        }

        /// <summary>First (oldest) owned instance of a catalog part id, or null.</summary>
        public PartInstance? FindFirstByPartId(string partId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].PartId, partId, StringComparison.Ordinal))
                {
                    return _items[i];
                }
            }
            return null;
        }

        /// <summary>Removes and returns the first owned instance of <paramref name="partId"/>, or null.</summary>
        public PartInstance? TakeFirstByPartId(string partId)
        {
            PartInstance? found = FindFirstByPartId(partId);
            if (found != null)
            {
                _items.Remove(found);
            }
            return found;
        }

        public int CountOf(string partId)
        {
            int count = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].PartId, partId, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        public void Clear()
        {
            _items.Clear();
        }
    }
}
