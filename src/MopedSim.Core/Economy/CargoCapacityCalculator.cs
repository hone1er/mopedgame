#nullable enable

using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Cargo capacity of the part mounted in SLOT_CARGO_RACK (all zeros when the slot is empty or missing).
    /// </summary>
    public readonly struct CargoCapacity
    {
        private readonly string? _rackPartId;

        public readonly int Slots;
        public readonly float MaxKg;

        /// <summary>True when a part occupies SLOT_CARGO_RACK.</summary>
        public readonly bool HasRack;

        /// <summary>True when every fastener owned by SLOT_CARGO_RACK is installed (informational for the garage UI).</summary>
        public readonly bool IsSecured;

        /// <summary>Catalog part id of the mounted rack; empty when unmounted.</summary>
        public string RackPartId
        {
            get { return _rackPartId ?? string.Empty; }
        }

        public CargoCapacity(int slots, float maxKg, bool hasRack, bool isSecured, string rackPartId)
        {
            Slots = slots < 0 ? 0 : slots;
            MaxKg = maxKg > 0f ? maxKg : 0f;
            HasRack = hasRack;
            IsSecured = isSecured;
            _rackPartId = rackPartId ?? string.Empty;
        }

        public static CargoCapacity None
        {
            get { return new CargoCapacity(0, 0f, false, false, string.Empty); }
        }
    }

    /// <summary>
    /// Computes courier cargo capacity from the mounted SLOT_CARGO_RACK part's catalog stats
    /// (<see cref="PartStats.CargoSlots"/> / <see cref="PartStats.CargoMaxKg"/>), e.g. rack_rear_wire or rack_milk_crate.
    /// Purely data-driven: never switches on part ids.
    /// </summary>
    public static class CargoCapacityCalculator
    {
        public static CargoCapacity Compute(BikeSimulation bike)
        {
            if (bike == null)
            {
                throw new ArgumentNullException(nameof(bike));
            }
            return Compute(bike.Assembly);
        }

        public static CargoCapacity Compute(BikeAssemblyGraph assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            PartInstance? rack = assembly.GetInstalledPart(SlotIds.CargoRack);
            if (rack == null)
            {
                return CargoCapacity.None;
            }

            bool secured = true;
            var fasteners = assembly.Fasteners;
            for (int i = 0; i < fasteners.Count; i++)
            {
                FastenerState f = fasteners[i];
                if (string.Equals(f.OwnerSlotId, SlotIds.CargoRack, StringComparison.Ordinal) && !f.IsInstalled)
                {
                    secured = false;
                    break;
                }
            }

            PartStats stats = rack.Definition.Stats;
            return new CargoCapacity(stats.CargoSlots, stats.CargoMaxKg, true, secured, rack.PartId);
        }

        /// <summary>
        /// Checks a capacity against a job's cargo requirement. Slots are checked before kg.
        /// </summary>
        public static JobRejectReason CheckRequirement(CargoCapacity capacity, int requiredSlots, float requiredKg)
        {
            if (requiredSlots > 0 && capacity.Slots < requiredSlots)
            {
                return JobRejectReason.InsufficientCargoSlots;
            }
            if (requiredKg > 0f && capacity.MaxKg + 1e-4f < requiredKg)
            {
                return JobRejectReason.InsufficientCargoKg;
            }
            return JobRejectReason.None;
        }

        public static bool CanCarry(CargoCapacity capacity, JobDefinition job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }
            return CheckRequirement(capacity, job.RequiredCargoSlots, job.RequiredCargoKg) == JobRejectReason.None;
        }
    }
}
