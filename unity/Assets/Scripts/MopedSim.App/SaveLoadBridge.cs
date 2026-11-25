using System;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Data;

namespace MopedSim.App
{
    /// <summary>
    /// Maps runtime MopedSim.Core state (SimulationWorld + BikeSimulation) to and from the versioned MopedSim.Data SaveState.
    /// Serialization, schema validation, and migrations are owned by MopedSim.Data.SaveMigrator; restoration only dispatches
    /// public Core commands (TrySwapPart, TryTorqueFastener, Refuel, InduceFault) so Core keeps every rule.
    /// Called on save/load events only (allocates; never per frame).
    /// </summary>
    public static class SaveLoadBridge
    {
        /// <summary>
        /// Captures the world, the active bike, garage inventory, and settings into a SaveState.
        /// </summary>
        public static SaveState Capture(
            SimulationWorld world,
            string bikeInstanceId,
            IReadOnlyList<string> inventoryPartIds,
            uint seed,
            bool diagnosticHintsEnabled)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            SaveState state = new SaveState();
            state.Seed = seed;
            state.CashUsd = world.CashUsd;
            state.ActiveZoneId = world.CurrentZone.Id;
            state.ToolRollCapacity = world.ToolRollCapacity;
            state.DiagnosticHintsEnabled = diagnosticHintsEnabled;
            state.ActiveBikeInstanceId = bikeInstanceId ?? string.Empty;

            IReadOnlyList<string> roll = world.ActiveToolRoll;
            for (int i = 0; i < roll.Count; i++)
            {
                state.ActiveToolRoll.Add(roll[i]);
            }

            if (inventoryPartIds != null)
            {
                for (int i = 0; i < inventoryPartIds.Count; i++)
                {
                    InventoryItemState item = new InventoryItemState();
                    item.InstanceId = inventoryPartIds[i] + "#" + i;
                    item.PartId = inventoryPartIds[i];
                    state.Inventory.Add(item);
                }
            }

            if (world.ActiveBike != null)
            {
                state.Bikes.Add(CaptureBike(world.ActiveBike, bikeInstanceId));
            }

            return state;
        }

        public static BikeSaveState CaptureBike(BikeSimulation bike, string instanceId)
        {
            if (bike == null)
            {
                throw new ArgumentNullException(nameof(bike));
            }

            BikeSaveState saved = new BikeSaveState();
            saved.InstanceId = instanceId ?? string.Empty;
            saved.ArchetypeId = bike.Archetype.Id;
            saved.Nickname = bike.Archetype.DisplayName;
            saved.OdometerKm = bike.OdometerMeters / 1000.0;

            saved.FuelTank.FuelLiters = bike.Fuel.FuelLevelLiters;
            saved.FuelTank.PremixRatio = bike.Fuel.PremixRatio > 0f ? 1f / bike.Fuel.PremixRatio : 0f;
            saved.FuelTank.PetcockMode = bike.Fuel.PetcockMode;
            saved.FuelTank.RustContamination = bike.Fuel.RustContamination;

            IReadOnlyList<MountSlotNode> slots = bike.Assembly.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                PartInstance part = slots[i].InstalledPart;
                if (part == null)
                {
                    continue;
                }

                InstalledPartSaveState p = new InstalledPartSaveState();
                p.SlotId = slots[i].Spec.SlotId;
                p.PartId = part.PartId;
                p.Condition = part.Condition;
                p.Wear = part.Wear;
                p.CarbonClog = part.CarbonAccumulation;
                p.InstalledArrowTowardExhaust = part.ArrowTowardExhaust;
                if (string.Equals(p.SlotId, SlotIds.SparkPlug, StringComparison.Ordinal))
                {
                    p.FoulingLevel = bike.SparkPlug.FoulingLevel;
                    p.InsulatorColorIndex = bike.SparkPlug.InsulatorColorIndex;
                }

                if (string.Equals(p.SlotId, SlotIds.Ignition, StringComparison.Ordinal))
                {
                    p.PointsGapMm = bike.Ignition.PointsGapMm;
                }

                saved.InstalledParts.Add(p);
            }

            IReadOnlyList<FastenerState> fasteners = bike.Assembly.Fasteners;
            for (int i = 0; i < fasteners.Count; i++)
            {
                FastenerState f = fasteners[i];
                FastenerSaveState fs = new FastenerSaveState();
                fs.FastenerId = f.FastenerId;
                fs.CurrentTorqueNm = f.CurrentTorqueNm;
                fs.IsStripped = f.IsStripped;
                fs.IsSeized = f.IsSeized;
                fs.HasPenetratingOil = f.PenetratingOilApplied;
                saved.Fasteners.Add(fs);
            }

            IReadOnlyList<FaultState> faults = bike.GetActiveFaults();
            for (int i = 0; i < faults.Count; i++)
            {
                saved.ActiveFaultIds.Add(faults[i].FaultId);
            }

            return saved;
        }

        /// <summary>
        /// Restores cash and the packed Tool Roll onto a SimulationWorld.
        /// </summary>
        public static void ApplyWorldState(SaveState state, SimulationWorld world)
        {
            if (state == null || world == null)
            {
                return;
            }

            world.CashUsd = state.CashUsd;
            world.ClearToolRoll();
            for (int i = 0; i < state.ActiveToolRoll.Count; i++)
            {
                world.TryPackToolRollItem(state.ActiveToolRoll[i]);
            }
        }

        /// <summary>
        /// Re-applies saved parts, fastener torque, fuel, and persistent faults onto a freshly built BikeSimulation through Core commands.
        /// Returns the number of saved entries Core rejected (0 = clean restore).
        /// </summary>
        public static int ApplyBikeState(BikeSaveState saved, BikeSimulation bike, IReadOnlyDictionary<string, PartDefinition> catalog)
        {
            if (saved == null || bike == null)
            {
                return 0;
            }

            int rejected = 0;
            if (catalog != null)
            {
                for (int i = 0; i < saved.InstalledParts.Count; i++)
                {
                    InstalledPartSaveState p = saved.InstalledParts[i];
                    PartInstance current = bike.Assembly.GetInstalledPart(p.SlotId);
                    PartDefinition def;
                    if (current != null && string.Equals(current.PartId, p.PartId, StringComparison.Ordinal))
                    {
                        current.Condition = p.Condition;
                        current.Wear = p.Wear;
                        current.CarbonAccumulation = p.CarbonClog;
                        continue;
                    }

                    if (!catalog.TryGetValue(p.PartId, out def) || def == null)
                    {
                        rejected++;
                        continue;
                    }

                    PartInstance restored = new PartInstance(def);
                    restored.Condition = p.Condition;
                    restored.Wear = p.Wear;
                    restored.CarbonAccumulation = p.CarbonClog;
                    restored.ArrowTowardExhaust = p.InstalledArrowTowardExhaust;
                    if (!bike.TrySwapPart(p.SlotId, restored).Succeeded)
                    {
                        rejected++;
                    }
                }
            }

            for (int i = 0; i < saved.Fasteners.Count; i++)
            {
                FastenerSaveState fs = saved.Fasteners[i];
                FastenerState f = bike.Assembly.GetFastener(fs.FastenerId);
                if (f == null)
                {
                    rejected++;
                    continue;
                }

                if (fs.IsStripped || !f.IsInstalled)
                {
                    // Core exposes no direct "restore stripped thread" command; stripped studs re-strip via over-torque.
                    if (fs.IsStripped)
                    {
                        bike.TryTorqueFastener(fs.FastenerId, f.StripThresholdNm + 1f);
                    }

                    continue;
                }

                if (Math.Abs(f.CurrentTorqueNm - fs.CurrentTorqueNm) > 0.001f)
                {
                    bike.TryTorqueFastener(fs.FastenerId, fs.CurrentTorqueNm);
                }

                if (fs.IsSeized)
                {
                    bike.Assembly.SetFastenerSeized(fs.FastenerId, true);
                }
            }

            bike.Fuel.FuelLevelLiters = 0f;
            bike.Refuel(saved.FuelTank.FuelLiters, ThermalModel.PremixFromRatio(saved.FuelTank.PremixRatio), saved.FuelTank.RustContamination);
            bike.SetPetcockMode(saved.FuelTank.PetcockMode);

            for (int i = 0; i < saved.ActiveFaultIds.Count; i++)
            {
                bike.InduceFault(saved.ActiveFaultIds[i]);
            }

            return rejected;
        }

        public static string Serialize(SaveState state)
        {
            return SaveMigrator.Serialize(state, true);
        }

        public static SaveState Deserialize(string json)
        {
            return SaveMigrator.DeserializeAndMigrate(json);
        }

        public static void SaveToPath(SaveState state, string path)
        {
            SaveMigrator.SaveToFile(path, state);
        }

        public static SaveState LoadFromPath(string path)
        {
            return SaveMigrator.LoadFromFile(path);
        }
    }
}
