using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Top-level simulation world holding active moped, tool roll loadout, garage cash, and Bay Area zone conditions.
    /// </summary>
    public sealed class SimulationWorld
    {
        public const int BaseToolRollCapacity = 6;
        public const int MaxToolRollCapacity = 10;

        private readonly List<string> _activeToolRoll = new List<string>();
        private readonly Dictionary<string, BikeSimulation> _bikes = new Dictionary<string, BikeSimulation>(StringComparer.Ordinal);

        public ISimRandom Random { get; }
        public BikeSimulation? ActiveBike { get; private set; }
        public ZoneEnvironmentSpec CurrentZone { get; private set; }
        public decimal CashUsd { get; set; } = 250m;
        public float TotalElapsedSeconds { get; private set; }

        public IReadOnlyList<string> ActiveToolRoll
        {
            get { return _activeToolRoll; }
        }

        public int ToolRollCapacity
        {
            get
            {
                int bonus = ActiveBike != null ? ActiveBike.Assembly.GetCargoCapacitySlots() : 0;
                int total = BaseToolRollCapacity + bonus;
                return total > MaxToolRollCapacity ? MaxToolRollCapacity : total;
            }
        }

        public SimulationWorld(ISimRandom random, ZoneEnvironmentSpec? initialZone = null)
        {
            Random = random ?? throw new ArgumentNullException(nameof(random));
            CurrentZone = initialZone ?? new ZoneEnvironmentSpec
            {
                Id = "Zone_WestOakland",
                DisplayName = "West Oakland",
                AmbientTemperatureC = 20f,
                AmbientAirDensity = RideInputState.StandardAirDensity,
                BaseSurfaceGrip = 1.0f,
                DistanceFromGarageMiles = 0.5f
            };
        }

        public void RegisterBike(string bikeId, BikeSimulation bike, bool setActive = true)
        {
            if (bikeId == null || bikeId.Length == 0)
            {
                throw new ArgumentException("Bike id cannot be empty.", nameof(bikeId));
            }
            if (bike == null)
            {
                throw new ArgumentNullException(nameof(bike));
            }
            _bikes[bikeId] = bike;
            if (setActive || ActiveBike == null)
            {
                ActiveBike = bike;
                ActiveBike.AmbientTemperatureC = CurrentZone.AmbientTemperatureC;
            }
        }

        public bool SetActiveBike(string bikeId)
        {
            if (bikeId != null && _bikes.TryGetValue(bikeId, out var bike) && bike != null)
            {
                ActiveBike = bike;
                ActiveBike.AmbientTemperatureC = CurrentZone.AmbientTemperatureC;
                return true;
            }
            return false;
        }

        public void SetZone(ZoneEnvironmentSpec zone)
        {
            CurrentZone = zone ?? throw new ArgumentNullException(nameof(zone));
            if (ActiveBike != null)
            {
                ActiveBike.AmbientTemperatureC = zone.AmbientTemperatureC;
            }
        }

        public bool TryPackToolRollItem(string itemId)
        {
            if (itemId == null || itemId.Length == 0 || _activeToolRoll.Count >= ToolRollCapacity)
            {
                return false;
            }
            _activeToolRoll.Add(itemId);
            return true;
        }

        public bool RemoveToolRollItem(string itemId)
        {
            return _activeToolRoll.Remove(itemId);
        }

        public void ClearToolRoll()
        {
            _activeToolRoll.Clear();
        }

        public EngineTelemetrySnapshot Step(float dt, in RideInputState input)
        {
            if (ActiveBike == null)
            {
                throw new InvalidOperationException("No active bike registered in SimulationWorld.");
            }
            TotalElapsedSeconds += dt;
            return ActiveBike.Step(dt, in input, Random);
        }

        public RoadsideRepairResult TryRoadsideRepairActiveBike(string faultId)
        {
            if (ActiveBike == null)
            {
                return new RoadsideRepairResult(false, faultId, FaultType.FouledPlug, "No active bike.");
            }
            RoadsideRepairResult result = ActiveBike.TryRoadsideRepair(faultId, _activeToolRoll);
            if (result.Succeeded && result.ConsumedSpare)
            {
                ConsumeSpareForFault(faultId);
            }
            return result;
        }

        public RecoveryResult RecoverActiveBikeWithBuddyTow()
        {
            if (ActiveBike == null)
            {
                return new RecoveryResult(false, "BuddyTow", 0f, 0f, 0m, "No active bike.");
            }
            RecoveryResult res = ActiveBike.BuddyTow(CurrentZone.DistanceFromGarageMiles);
            if (CashUsd >= res.CashCostUsd)
            {
                CashUsd -= res.CashCostUsd;
                return res;
            }
            return new RecoveryResult(false, "BuddyTow", CurrentZone.DistanceFromGarageMiles, 0f, res.CashCostUsd, "Insufficient cash for Buddy Tow — use Pedal of Shame.");
        }

        private void ConsumeSpareForFault(string faultId)
        {
            switch (faultId)
            {
                case FaultIds.FouledPlug:
                    RemoveMatchingItem(ToolIds.SparePlug, "spareplug");
                    break;
                case FaultIds.CondenserHeatSoak:
                    RemoveMatchingItem(ToolIds.SpareCondenser, "sparecondenser");
                    break;
                case FaultIds.ThrownChain:
                    RemoveMatchingItem(ToolIds.SpareMasterLink, "sparemasterlink", "masterlink");
                    break;
                case FaultIds.SnappedBelt:
                    RemoveMatchingItem(ToolIds.SpareBelt, "sparebelt");
                    break;
                case FaultIds.ExhaustLoose:
                    RemoveMatchingItem(ToolIds.SpareFlangeNut, "spareflangenut");
                    break;
            }
        }

        private void RemoveMatchingItem(string canonicalId, string alias1, string alias2 = "")
        {
            for (int i = 0; i < _activeToolRoll.Count; i++)
            {
                string item = _activeToolRoll[i];
                string normalized = item.Replace("_", string.Empty);
                if (string.Equals(item, canonicalId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, alias1, StringComparison.OrdinalIgnoreCase)
                    || (alias2.Length > 0 && string.Equals(normalized, alias2, StringComparison.OrdinalIgnoreCase)))
                {
                    _activeToolRoll.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
