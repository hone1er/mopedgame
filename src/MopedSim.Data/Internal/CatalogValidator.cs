using System;
using System.Collections.Generic;
using MopedSim.Core;

namespace MopedSim.Data
{
    /// <summary>
    /// Cross-reference, DAG, category-mapping, and Core-contract checks behind <see cref="CatalogLoader.Validate"/>.
    /// </summary>
    internal static class CatalogValidator
    {
        public static List<string> Run(CatalogDatabase db)
        {
            var errors = new List<string>();

            if (db.PartList.Count == 0)
            {
                errors.Add("parts.json: catalog contains no parts.");
            }
            if (db.ArchetypeList.Count == 0)
            {
                errors.Add("archetypes.json: catalog contains no archetypes.");
            }
            if (db.FaultList.Count == 0)
            {
                errors.Add("faults.json: catalog contains no faults.");
            }
            if (db.ZoneList.Count == 0)
            {
                errors.Add("zones.json: catalog contains no zones.");
            }
            if (db.JobList.Count == 0)
            {
                errors.Add("jobs.json: catalog contains no jobs.");
            }

            ValidateParts(db, errors);
            for (int i = 0; i < db.ArchetypeList.Count; i++)
            {
                ValidateArchetype(db, db.ArchetypeList[i], errors);
            }
            ValidateFaults(db, errors);
            ValidateZones(db, errors);
            ValidateJobs(db, errors);
            return errors;
        }

        private static void ValidateParts(CatalogDatabase db, List<string> errors)
        {
            for (int i = 0; i < db.PartList.Count; i++)
            {
                PartDefinition part = db.PartList[i];
                string ctx = "parts.json[" + part.Id + "]";
                if (!db.PartMeta.TryGetValue(part.Id, out var meta))
                {
                    errors.Add(ctx + ": missing PartMetadata.");
                    continue;
                }
                if (part.MassKg < 0f)
                {
                    errors.Add(ctx + ": massKg must be >= 0.");
                }
                if (part.PriceUsd < 0m)
                {
                    errors.Add(ctx + ": priceUsd must be >= 0.");
                }
                if (meta.Has3dMesh && part.MeshAssetPath.Length == 0)
                {
                    errors.Add(ctx + ": has3dMesh is true but meshAssetPath is empty.");
                }
                for (int p = 1; p < part.Stats.TorqueCurve.Count; p++)
                {
                    if (part.Stats.TorqueCurve[p].Rpm <= part.Stats.TorqueCurve[p - 1].Rpm)
                    {
                        errors.Add(ctx + ": torqueCurve rpm values must be strictly ascending.");
                        break;
                    }
                }
                if ((part.Stats.IsTool || part.Stats.IsSpare) && part.Stats.ToolCapabilityTag.Length == 0)
                {
                    errors.Add(ctx + ": tool/spare item requires stats.toolTag.");
                }
            }
        }

        private static void ValidateArchetype(CatalogDatabase db, BikeArchetypeSpec spec, List<string> errors)
        {
            string ctx = "archetypes.json[" + spec.Id + "]";
            if (!db.ArchetypeMeta.TryGetValue(spec.Id, out var meta))
            {
                errors.Add(ctx + ": missing ArchetypeMetadata.");
                return;
            }

            var slotOrder = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < spec.MountSlots.Count; i++)
            {
                slotOrder[spec.MountSlots[i].SlotId] = i;
            }
            var fastenerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < spec.Fasteners.Count; i++)
            {
                fastenerIds.Add(spec.Fasteners[i].FastenerId);
            }

            // --- slot DAG
            int roots = 0;
            var fastenerOwnerCount = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < spec.MountSlots.Count; i++)
            {
                MountSlotSpec slot = spec.MountSlots[i];
                string sCtx = ctx + "." + slot.SlotId;
                SlotMetadata? slotMeta = meta.GetSlot(slot.SlotId);
                if (slotMeta == null)
                {
                    errors.Add(sCtx + ": missing SlotMetadata.");
                    continue;
                }

                if (slot.ParentSlotId == null)
                {
                    roots++;
                }
                else if (!slotOrder.TryGetValue(slot.ParentSlotId, out var parentIdx))
                {
                    errors.Add(sCtx + ": parentSlotId '" + slot.ParentSlotId + "' is not a slot of this archetype.");
                }
                else if (parentIdx >= i)
                {
                    errors.Add(sCtx + ": parent slot '" + slot.ParentSlotId + "' must be declared before its child (Core installs stock parts in declaration order).");
                }

                for (int b = 0; b < slot.BlockedBySlotIds.Length; b++)
                {
                    string blocker = slot.BlockedBySlotIds[b];
                    if (string.Equals(blocker, slot.SlotId, StringComparison.Ordinal))
                    {
                        errors.Add(sCtx + ": blockedBy references itself.");
                    }
                    else if (!slotOrder.ContainsKey(blocker))
                    {
                        errors.Add(sCtx + ": blockedBy references unknown slot '" + blocker + "'.");
                    }
                }

                for (int f = 0; f < slotMeta.FastenerIds.Count; f++)
                {
                    string fId = slotMeta.FastenerIds[f];
                    if (!fastenerIds.Contains(fId))
                    {
                        errors.Add(sCtx + ": fastenerIds references undeclared fastener '" + fId + "'.");
                    }
                    fastenerOwnerCount[fId] = fastenerOwnerCount.TryGetValue(fId, out var n) ? n + 1 : 1;
                }

                ValidateSlotCategories(db, slot, slotMeta, sCtx, errors);
            }
            if (roots != 1)
            {
                errors.Add(ctx + ": expected exactly one root slot (parentSlotId null), found " + roots + ".");
            }

            // --- fasteners
            for (int i = 0; i < spec.Fasteners.Count; i++)
            {
                FastenerSpec f = spec.Fasteners[i];
                string fCtx = ctx + "." + f.FastenerId;
                int owners = fastenerOwnerCount.TryGetValue(f.FastenerId, out var count) ? count : 0;
                if (owners == 0 || f.OwnerSlotId.Length == 0)
                {
                    errors.Add(fCtx + ": fastener is not listed in any mountSlots[].fastenerIds (no owner slot).");
                }
                else if (owners > 1)
                {
                    errors.Add(fCtx + ": fastener is owned by " + owners + " slots; Core supports exactly one owner.");
                }
                if (!(f.ToleranceNm >= 0f && f.ToleranceNm < f.TargetTorqueNm && f.TargetTorqueNm < f.StripThresholdNm))
                {
                    errors.Add(fCtx + ": torque spec must satisfy 0 <= toleranceNm < targetTorqueNm < stripThresholdNm.");
                }
                if (f.VibrationLoosenRate < 0f)
                {
                    errors.Add(fCtx + ": vibrationLoosenRate must be >= 0.");
                }
                FastenerMetadata? fMeta = meta.GetFastener(f.FastenerId);
                if (fMeta != null)
                {
                    if (fMeta.RequiredToolId != null && !db.Parts.ContainsKey(fMeta.RequiredToolId))
                    {
                        errors.Add(fCtx + ": requiredToolId '" + fMeta.RequiredToolId + "' is not a part id.");
                    }
                    if (f.DefaultSeized && !fMeta.CanSeize)
                    {
                        errors.Add(fCtx + ": defaultSeized is true but canSeize is false.");
                    }
                }
            }

            // --- fuel
            FuelMixSpec fuel = meta.DefaultFuelMix;
            if (fuel.FuelLiters < 0f || fuel.FuelLiters > meta.FuelCapacityLiters)
            {
                errors.Add(ctx + ".defaultFuelMix: fuelLiters must be within 0..fuelCapacityLiters.");
            }
            if (fuel.PremixRatio <= 0f)
            {
                errors.Add(ctx + ".defaultFuelMix: premixRatio must be > 0 (X:1).");
            }
            if (fuel.RustContamination < 0f || fuel.RustContamination > 1f)
            {
                errors.Add(ctx + ".defaultFuelMix: rustContamination must be within 0..1.");
            }

            // --- drivetrain consistency
            if (spec.DefaultPartIds.TryGetValue(SlotIds.DrivetrainSide, out var drivePartId)
                && db.TryGetPart(drivePartId, out var drivePart)
                && drivePart.Stats.DrivetrainType != spec.DrivetrainType)
            {
                errors.Add(ctx + ": stock drivetrain part '" + drivePartId + "' is " + drivePart.Stats.DrivetrainType
                    + " but archetype drivetrainArchitecture is " + spec.DrivetrainType + ".");
            }

            // --- MopedSim.Core contract
            CheckCoreSlots(spec, CoreIdContract.RequiredSlots, ctx, errors);
            if (spec.DrivetrainType != DrivetrainType.VariatedBelt)
            {
                CheckCoreSlots(spec, CoreIdContract.ChainDriveSlots, ctx, errors);
            }
            CheckCoreSlot(spec, SlotIds.DrivetrainSide, CoreIdContract.DrivetrainSideCategory(spec.DrivetrainType), ctx, errors);
            for (int i = 0; i < CoreIdContract.RequiredFastenerIds.Count; i++)
            {
                string fId = CoreIdContract.RequiredFastenerIds[i];
                if (!fastenerIds.Contains(fId))
                {
                    errors.Add(ctx + ": missing fastener '" + fId + "' required by MopedSim.Core (FastenerIds).");
                }
            }
        }

        private static void ValidateSlotCategories(CatalogDatabase db, MountSlotSpec slot, SlotMetadata slotMeta, string ctx, List<string> errors)
        {
            // Default (stock) part.
            if (slotMeta.DefaultPartId != null)
            {
                if (!db.TryGetPart(slotMeta.DefaultPartId, out var def))
                {
                    errors.Add(ctx + ": defaultPartId '" + slotMeta.DefaultPartId + "' is not a part id.");
                }
                else
                {
                    string raw = db.PartMeta[def.Id].CatalogCategory;
                    if (!Contains(slotMeta.AcceptedCatalogCategories, raw))
                    {
                        errors.Add(ctx + ": default part '" + def.Id + "' category '" + raw + "' is not in acceptedCategories.");
                    }
                    if (!CategoryMapping.InterfacesOverlap(slot.RequiredInterfaces, def.CompatibleInterfaces))
                    {
                        errors.Add(ctx + ": default part '" + def.Id + "' shares no interface with acceptedInterfaces.");
                    }
                    if (def.Category != slot.AcceptedCategory)
                    {
                        errors.Add(ctx + ": default part '" + def.Id + "' maps to Core " + def.Category + " but slot accepts " + slot.AcceptedCategory + ".");
                    }
                }
            }

            // Every catalog part must install where the catalog says it can, and nowhere else (Core checks only
            // Core category + interface overlap, and Tool/Spare share PartCategory.ToolRollItem).
            bool anyCandidate = false;
            for (int i = 0; i < db.PartList.Count; i++)
            {
                PartDefinition part = db.PartList[i];
                if (!CategoryMapping.InterfacesOverlap(slot.RequiredInterfaces, part.CompatibleInterfaces))
                {
                    continue;
                }
                string raw = db.PartMeta[part.Id].CatalogCategory;
                bool catalogAllows = Contains(slotMeta.AcceptedCatalogCategories, raw);
                bool coreAllows = part.Category == slot.AcceptedCategory;
                if (catalogAllows)
                {
                    anyCandidate = true;
                }
                if (catalogAllows && !coreAllows)
                {
                    errors.Add(ctx + ": part '" + part.Id + "' (" + raw + ") is catalog-compatible but maps to Core " + part.Category
                        + " while the slot accepts " + slot.AcceptedCategory + "; Core would reject the install.");
                }
                else if (!catalogAllows && coreAllows)
                {
                    errors.Add(ctx + ": part '" + part.Id + "' (" + raw + ") is not in acceptedCategories but maps to Core " + part.Category
                        + " with a matching interface; Core would allow the install.");
                }
            }
            if (!anyCandidate)
            {
                errors.Add(ctx + ": no catalog part matches acceptedCategories + acceptedInterfaces.");
            }
        }

        private static void CheckCoreSlots(BikeArchetypeSpec spec, IReadOnlyList<KeyValuePair<string, PartCategory>> required, string ctx, List<string> errors)
        {
            for (int i = 0; i < required.Count; i++)
            {
                CheckCoreSlot(spec, required[i].Key, required[i].Value, ctx, errors);
            }
        }

        private static void CheckCoreSlot(BikeArchetypeSpec spec, string slotId, PartCategory expected, string ctx, List<string> errors)
        {
            for (int i = 0; i < spec.MountSlots.Count; i++)
            {
                if (string.Equals(spec.MountSlots[i].SlotId, slotId, StringComparison.Ordinal))
                {
                    if (spec.MountSlots[i].AcceptedCategory != expected)
                    {
                        errors.Add(ctx + ": slot '" + slotId + "' accepts Core " + spec.MountSlots[i].AcceptedCategory
                            + " but MopedSim.Core reads it as " + expected + ".");
                    }
                    return;
                }
            }
            errors.Add(ctx + ": missing slot '" + slotId + "' required by MopedSim.Core (SlotIds).");
        }

        private static void ValidateFaults(CatalogDatabase db, List<string> errors)
        {
            for (int i = 0; i < db.FaultList.Count; i++)
            {
                FaultDefinition fault = db.FaultList[i];
                string ctx = "faults.json[" + fault.Id + "]";
                CheckToolRefs(db, fault.RequiredTools, ctx + ".requiredTools", errors);
                CheckToolRefs(db, fault.RequiredSpares, ctx + ".consumedSpares", errors);
            }
        }

        private static void CheckToolRefs(CatalogDatabase db, List<string> ids, string ctx, List<string> errors)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (!db.TryGetPart(ids[i], out var part))
                {
                    errors.Add(ctx + ": unknown tool/spare part id '" + ids[i] + "'.");
                }
                else if (part.Category != PartCategory.ToolRollItem)
                {
                    errors.Add(ctx + ": '" + ids[i] + "' is not a Tool or Spare item.");
                }
            }
        }

        private static void ValidateZones(CatalogDatabase db, List<string> errors)
        {
            for (int i = 0; i < db.ZoneList.Count; i++)
            {
                ZoneEnvironmentSpec zone = db.ZoneList[i];
                string ctx = "zones.json[" + zone.Id + "]";
                if (!db.ZoneMeta.TryGetValue(zone.Id, out var meta))
                {
                    errors.Add(ctx + ": missing ZoneMetadata.");
                    continue;
                }
                if (zone.AmbientAirDensity <= 0f)
                {
                    errors.Add(ctx + ": ambientAirDensity must be > 0.");
                }
                for (int c = 0; c < meta.ConnectedZones.Count; c++)
                {
                    string other = meta.ConnectedZones[c];
                    if (string.Equals(other, zone.Id, StringComparison.Ordinal))
                    {
                        errors.Add(ctx + ".connectedZones: zone lists itself.");
                    }
                    else if (!db.Zones.ContainsKey(other))
                    {
                        errors.Add(ctx + ".connectedZones: unknown zone '" + other + "'.");
                    }
                }
                var landmarkIds = new HashSet<string>(StringComparer.Ordinal);
                for (int l = 0; l < meta.Landmarks.Count; l++)
                {
                    if (!landmarkIds.Add(meta.Landmarks[l].Id))
                    {
                        errors.Add(ctx + ".landmarks: duplicate landmark id '" + meta.Landmarks[l].Id + "'.");
                    }
                }
            }
        }

        private static void ValidateJobs(CatalogDatabase db, List<string> errors)
        {
            for (int i = 0; i < db.JobList.Count; i++)
            {
                JobDefinition job = db.JobList[i];
                string ctx = "jobs.json[" + job.Id + "]";
                if (!db.JobMeta.TryGetValue(job.Id, out var meta))
                {
                    errors.Add(ctx + ": missing JobMetadata.");
                    continue;
                }
                if (!db.Zones.ContainsKey(meta.StartZoneId))
                {
                    errors.Add(ctx + ".startZoneId: unknown zone '" + meta.StartZoneId + "'.");
                }
                if (!db.Zones.ContainsKey(meta.TargetZoneId))
                {
                    errors.Add(ctx + ".targetZoneId: unknown zone '" + meta.TargetZoneId + "'.");
                }
                if (meta.RequiredCargoSlots < 0 || meta.RequiredCargoKg < 0f)
                {
                    errors.Add(ctx + ": cargo requirements must be >= 0.");
                }
                for (int r = 0; r < meta.RecommendedPartIds.Count; r++)
                {
                    if (!db.Parts.ContainsKey(meta.RecommendedPartIds[r]))
                    {
                        errors.Add(ctx + ".recommendedPartIds: unknown part '" + meta.RecommendedPartIds[r] + "'.");
                    }
                }
                for (int c = 0; c < meta.Checkpoints.Count; c++)
                {
                    JobCheckpoint cp = meta.Checkpoints[c];
                    if (!db.Zones.ContainsKey(cp.ZoneId))
                    {
                        errors.Add(ctx + ".checkpoints[" + cp.Id + "]: unknown zone '" + cp.ZoneId + "'.");
                    }
                    if (c > 0 && cp.Order <= meta.Checkpoints[c - 1].Order)
                    {
                        errors.Add(ctx + ".checkpoints[" + cp.Id + "]: order must be strictly ascending.");
                    }
                }
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
