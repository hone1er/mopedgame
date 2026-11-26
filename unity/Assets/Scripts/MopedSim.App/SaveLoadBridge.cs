using System;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Data;

namespace MopedSim.App
{
    /// <summary>
    /// Maps runtime MopedSim.Core state (SimulationWorld + BikeSimulation) to and from the versioned MopedSim.Data SaveState.
    /// Serialization, schema validation, and migrations are owned by MopedSim.Data.SaveMigrator; restoration only dispatches
    /// public Core commands (TrySwapPart, RestoreFastenerState, Refuel, InduceFault) so Core keeps every rule.
    /// Called on save/load events only (allocates; never per frame).
    /// </summary>
    public static class SaveLoadBridge
    {
        /// <summary>
        /// Fallback frame paint when neither the baseline save nor MopedSim.Data's default supplies one.
        /// </summary>
        public const string FallbackPaintHex = "#C8553D";

        /// <summary>
        /// Captures the world, the active bike, garage inventory, and settings into a SaveState.
        /// Reputation and the Core <see cref="GarageInventory"/> (<c>world.Inventory</c>, the source of truth for owned parts) are
        /// written from <paramref name="world"/>. <paramref name="inventoryPartIds"/> is the legacy id-only list: each id is
        /// written only for copies beyond what <c>world.Inventory</c> already holds, so callers that mirror the world inventory
        /// never duplicate entries. Paint / patina / decal come from <paramref name="customization"/> when given, otherwise they
        /// are carried over from <paramref name="baseline"/> (the last loaded save) or SaveMigrator.CreateNewDefault, as are
        /// nickname, unlocked zones and completed gigs.
        /// </summary>
        public static SaveState Capture(
            SimulationWorld world,
            string bikeInstanceId,
            IReadOnlyList<string> inventoryPartIds,
            uint seed,
            bool diagnosticHintsEnabled,
            SaveState baseline = null,
            BikeCustomizationState customization = null)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            SaveState state = baseline ?? SaveMigrator.CreateNewDefault(seed);
            BikeSaveState prior = FindBike(state, bikeInstanceId);
            if (prior == null && state.Bikes.Count > 0)
            {
                prior = state.Bikes[0];
            }

            string paintHex = prior != null && !string.IsNullOrEmpty(prior.PaintHex) ? prior.PaintHex : FallbackPaintHex;
            float patina = prior != null ? prior.PatinaLevel : 0f;
            string decal = prior != null ? prior.DecalStyle : null;
            string nickname = prior != null ? prior.Nickname : string.Empty;
            state.Bikes.Clear();
            state.ActiveToolRoll.Clear();
            state.Inventory.Clear();

            state.Seed = seed;
            state.CashUsd = world.CashUsd;
            state.Reputation = world.Reputation > 0 ? world.Reputation : 0;
            state.ActiveZoneId = world.CurrentZone.Id;
            state.ToolRollCapacity = world.ToolRollCapacity;
            state.DiagnosticHintsEnabled = diagnosticHintsEnabled;
            state.ActiveBikeInstanceId = bikeInstanceId ?? string.Empty;

            IReadOnlyList<string> roll = world.ActiveToolRoll;
            for (int i = 0; i < roll.Count; i++)
            {
                state.ActiveToolRoll.Add(roll[i]);
            }

            CaptureInventory(state, world.Inventory, inventoryPartIds);

            if (world.ActiveBike != null)
            {
                BikeSaveState bike = CaptureBike(world.ActiveBike, bikeInstanceId);
                if (customization != null)
                {
                    bike.ApplyCustomization(customization);
                }
                else
                {
                    bike.PaintHex = paintHex;
                    bike.PatinaLevel = patina;
                    bike.DecalStyle = decal;
                }

                if (!string.IsNullOrEmpty(nickname))
                {
                    bike.Nickname = nickname;
                }

                state.Bikes.Add(bike);
            }

            if (!string.IsNullOrEmpty(state.ActiveZoneId) && !state.UnlockedZones.Contains(state.ActiveZoneId))
            {
                state.UnlockedZones.Add(state.ActiveZoneId);
            }

            return state;
        }

        private static void CaptureInventory(SaveState state, GarageInventory inventory, IReadOnlyList<string> legacyPartIds)
        {
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> owned = new Dictionary<string, int>(StringComparer.Ordinal);
            if (inventory != null)
            {
                IReadOnlyList<PartInstance> items = inventory.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    PartInstance part = items[i];
                    if (part == null || string.IsNullOrEmpty(part.PartId))
                    {
                        continue;
                    }

                    InventoryItemState item = new InventoryItemState();
                    item.InstanceId = UniqueInstanceId(usedIds, string.IsNullOrEmpty(part.InstanceId) ? part.PartId + "#w" + i : part.InstanceId);
                    item.PartId = part.PartId;
                    item.Condition = part.Condition;
                    item.Wear = part.Wear;
                    state.Inventory.Add(item);

                    int count;
                    owned.TryGetValue(part.PartId, out count);
                    owned[part.PartId] = count + 1;
                }
            }

            if (legacyPartIds == null)
            {
                return;
            }

            for (int i = 0; i < legacyPartIds.Count; i++)
            {
                string partId = legacyPartIds[i];
                if (string.IsNullOrEmpty(partId))
                {
                    continue;
                }

                int remaining;
                if (owned.TryGetValue(partId, out remaining) && remaining > 0)
                {
                    // Already captured from world.Inventory.
                    owned[partId] = remaining - 1;
                    continue;
                }

                InventoryItemState item = new InventoryItemState();
                item.InstanceId = UniqueInstanceId(usedIds, partId + "#" + i);
                item.PartId = partId;
                state.Inventory.Add(item);
            }
        }

        private static string UniqueInstanceId(HashSet<string> usedIds, string candidate)
        {
            string id = candidate;
            int suffix = 1;
            while (!usedIds.Add(id))
            {
                id = candidate + "_" + suffix;
                suffix++;
            }

            return id;
        }

        private static BikeSaveState FindBike(SaveState state, string instanceId)
        {
            for (int i = 0; i < state.Bikes.Count; i++)
            {
                if (string.Equals(state.Bikes[i].InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return state.Bikes[i];
                }
            }

            return null;
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
        /// Restores cash, reputation, and the packed Tool Roll onto a SimulationWorld. When <paramref name="catalog"/> is given,
        /// the saved inventory is also rebuilt into <c>world.Inventory</c> as Core <see cref="PartInstance"/>s (unknown part ids are
        /// skipped and counted in the return value); without a catalog the world inventory is left untouched (legacy behavior).
        /// </summary>
        public static int ApplyWorldState(SaveState state, SimulationWorld world, IReadOnlyDictionary<string, PartDefinition> catalog = null)
        {
            if (state == null || world == null)
            {
                return 0;
            }

            world.CashUsd = state.CashUsd;
            world.Reputation = state.Reputation > 0 ? state.Reputation : 0;
            world.ClearToolRoll();
            for (int i = 0; i < state.ActiveToolRoll.Count; i++)
            {
                world.TryPackToolRollItem(state.ActiveToolRoll[i]);
            }

            if (catalog == null)
            {
                return 0;
            }

            int skipped = 0;
            world.Inventory.Clear();
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                InventoryItemState item = state.Inventory[i];
                PartDefinition def;
                if (item == null || string.IsNullOrEmpty(item.PartId) || !catalog.TryGetValue(item.PartId, out def) || def == null)
                {
                    skipped++;
                    continue;
                }

                int quantity = item.Quantity > 0 ? item.Quantity : 1;
                for (int q = 0; q < quantity; q++)
                {
                    PartInstance part = new PartInstance(def, q == 0 ? item.InstanceId ?? string.Empty : string.Empty);
                    part.Condition = item.Condition;
                    part.Wear = item.Wear;
                    world.Inventory.Add(part);
                }
            }

            return skipped;
        }

        /// <summary>
        /// Paint booth state of a saved bike (falls back to Core defaults for a missing / invalid save record).
        /// </summary>
        public static BikeCustomizationState ReadCustomization(BikeSaveState saved)
        {
            return saved != null ? saved.ToCustomizationState() : new BikeCustomizationState();
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
                // Saves carry no explicit "installed" flag: removal zeroes torque, and stripped threads stay installed.
                bool installed = fs.IsStripped || fs.CurrentTorqueNm > 0f;
                WrenchActionResult restored = bike.RestoreFastenerState(
                    fs.FastenerId,
                    installed,
                    fs.CurrentTorqueNm,
                    fs.IsStripped,
                    fs.IsSeized,
                    fs.HasPenetratingOil);
                if (!restored.Succeeded)
                {
                    rejected++;
                }
            }

            bike.RestoreOdometer((float)(saved.OdometerKm * 1000.0));
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
