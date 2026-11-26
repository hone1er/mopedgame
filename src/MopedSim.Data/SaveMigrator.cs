#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MopedSim.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MopedSim.Data
{
    /// <summary>
    /// Creates, serializes, validates, and sequentially migrates versioned <see cref="SaveState"/> documents
    /// (data/schemas/save_state.schema.json). Deterministic: no clock, no global RNG.
    /// </summary>
    public static class SaveMigrator
    {
        /// <summary>Same as <see cref="SaveState.CurrentSchemaVersion"/>.</summary>
        public const int CurrentSchemaVersion = SaveState.CurrentSchemaVersion;

        /// <summary>Oldest save schemaVersion that can still be migrated forward.</summary>
        public const int MinimumSupportedSchemaVersion = 1;

        public const string DefaultStarterArchetypeId = ArchetypeIds.MaxerE50;
        public const string DefaultStartZoneId = "Zone_WestOakland";
        public const string DefaultBikeInstanceId = "bike_001";
        public const string DefaultPaintHex = "#C42828";

        /// <summary>Mirrors <see cref="SimulationWorld.CashUsd"/>'s initial value.</summary>
        public const decimal DefaultStartingCashUsd = 250m;

        /// <summary>Mirrors <see cref="SimulationWorld.BaseToolRollCapacity"/> (save_state.schema.json toolRollCapacity minimum).</summary>
        public const int MinToolRollCapacity = SimulationWorld.BaseToolRollCapacity;

        /// <summary>Mirrors <see cref="SimulationWorld.MaxToolRollCapacity"/> (save_state.schema.json toolRollCapacity maximum).</summary>
        public const int MaxToolRollCapacity = SimulationWorld.MaxToolRollCapacity;

        /// <summary>
        /// Upgrade steps keyed by source version: step[v] turns a v document into a v+1 document.
        /// Empty while v1 is the only schema; add an entry (and fixtures) whenever <see cref="CurrentSchemaVersion"/> bumps.
        /// </summary>
        private static readonly Dictionary<int, Func<JObject, JObject>> MigrationSteps = new Dictionary<int, Func<JObject, JObject>>();

        private static readonly Regex ZoneIdPattern = new Regex("^Zone_[A-Za-z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex ArchetypeIdPattern = new Regex("^arch_[a-z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex PaintHexPattern = new Regex("^#[0-9A-Fa-f]{6}\\z", RegexOptions.CultureInvariant);
        private static readonly Regex SlotIdPattern = new Regex("^SLOT_[A-Z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex PartIdPattern = new Regex("^[A-Za-z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex FastenerIdPattern = new Regex("^(BOLT|THREAD)_[A-Z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex FaultIdPattern = new Regex("^fault_[a-z0-9_]+\\z", RegexOptions.CultureInvariant);
        private static readonly Regex JobIdPattern = new Regex("^[a-z0-9_]+\\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// New-game save without catalog access. The bike's installedParts and fasteners are empty, which by contract
        /// means "factory stock" (archetype default parts, fasteners at target torque). Fuel comes from Core FuelSystem defaults.
        /// </summary>
        public static SaveState CreateNewDefault(uint seed, string starterArchetypeId = DefaultStarterArchetypeId)
        {
            if (starterArchetypeId == null || !ArchetypeIdPattern.IsMatch(starterArchetypeId))
            {
                throw new ArgumentException("Starter archetype id must match ^arch_[a-z0-9_]+$.", nameof(starterArchetypeId));
            }

            var coreFuel = new FuelSystem();
            var bike = new BikeSaveState
            {
                InstanceId = DefaultBikeInstanceId,
                ArchetypeId = starterArchetypeId,
                Nickname = "Starter Moped",
                OdometerKm = 0.0,
                PaintHex = DefaultPaintHex,
                PatinaLevel = 0f,
                FuelTank = new FuelTankSaveState
                {
                    FuelLiters = coreFuel.FuelLevelLiters,
                    PremixRatio = coreFuel.PremixRatio > 0f ? (float)Math.Round(1.0 / coreFuel.PremixRatio, 3) : 0f,
                    PetcockMode = coreFuel.PetcockMode,
                    RustContamination = coreFuel.RustContamination,
                },
            };

            return NewStateAround(seed, bike, SimulationWorld.BaseToolRollCapacity);
        }

        /// <summary>
        /// New-game save with the starter bike fully populated from the catalog: every stock part, every fastener at
        /// target torque, the archetype's default fuel mix, stock paint, and tool-roll size.
        /// </summary>
        public static SaveState CreateNewDefault(uint seed, CatalogDatabase catalog, string starterArchetypeId = DefaultStarterArchetypeId)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }
            if (starterArchetypeId == null
                || !catalog.Archetypes.TryGetValue(starterArchetypeId, out var archetype)
                || !catalog.ArchetypeMeta.TryGetValue(starterArchetypeId, out var meta))
            {
                throw new ArgumentException("Unknown starter archetype '" + (starterArchetypeId ?? "<null>") + "'.", nameof(starterArchetypeId));
            }

            var bike = new BikeSaveState
            {
                InstanceId = DefaultBikeInstanceId,
                ArchetypeId = archetype.Id,
                Nickname = archetype.DisplayName,
                OdometerKm = 0.0,
                PaintHex = DefaultPaintHex,
                PatinaLevel = 0f,
                FuelTank = new FuelTankSaveState
                {
                    FuelLiters = meta.DefaultFuelMix.FuelLiters,
                    PremixRatio = meta.DefaultFuelMix.PremixRatio,
                    PetcockMode = meta.DefaultFuelMix.PetcockMode,
                    RustContamination = meta.DefaultFuelMix.RustContamination,
                },
            };

            float stockInsulatorColor = new SparkPlugState().InsulatorColorIndex;
            for (int i = 0; i < archetype.MountSlots.Count; i++)
            {
                string slotId = archetype.MountSlots[i].SlotId;
                if (!archetype.DefaultPartIds.TryGetValue(slotId, out var partId) || !catalog.TryGetPart(partId, out var part))
                {
                    continue;
                }

                bool isPointsIgnition = part.Category == PartCategory.Ignition
                    && part.Stats.IgnitionType == IgnitionType.Points6V
                    && part.Stats.InitialPointsGapMm > 0f;
                bike.InstalledParts.Add(new InstalledPartSaveState
                {
                    SlotId = slotId,
                    PartId = part.Id,
                    Condition = 1f,
                    Wear = 0f,
                    CarbonClog = 0f,
                    FoulingLevel = 0f,
                    InsulatorColorIndex = part.Category == PartCategory.SparkPlug ? stockInsulatorColor : 0f,
                    PointsGapMm = isPointsIgnition ? part.Stats.InitialPointsGapMm : 0f,
                    InstalledArrowTowardExhaust = true,
                });

                if (part.Category == PartCategory.Frame
                    && catalog.PartMeta.TryGetValue(part.Id, out var frameMeta)
                    && frameMeta.TextStats.TryGetValue("stockPaintHex", out var paint)
                    && PaintHexPattern.IsMatch(paint))
                {
                    bike.PaintHex = paint;
                }
            }

            for (int i = 0; i < archetype.Fasteners.Count; i++)
            {
                FastenerSpec f = archetype.Fasteners[i];
                bike.Fasteners.Add(new FastenerSaveState
                {
                    FastenerId = f.FastenerId,
                    CurrentTorqueNm = f.TargetTorqueNm,
                    IsStripped = false,
                    IsSeized = f.DefaultSeized,
                    HasPenetratingOil = false,
                });
            }

            int toolRoll = meta.DefaultToolRollSlots >= MinToolRollCapacity && meta.DefaultToolRollSlots <= MaxToolRollCapacity
                ? meta.DefaultToolRollSlots
                : SimulationWorld.BaseToolRollCapacity;
            return NewStateAround(seed, bike, toolRoll);
        }

        /// <summary>
        /// Parses a save document, migrates it step by step up to <see cref="CurrentSchemaVersion"/>, and validates it.
        /// Throws <see cref="SaveMigrationException"/> for malformed JSON, missing/unsupported schemaVersion,
        /// unknown or missing members, or schema constraint violations.
        /// </summary>
        public static SaveState DeserializeAndMigrate(string json)
        {
            if (json == null)
            {
                throw new SaveMigrationException("Save JSON is null.");
            }

            JObject root = ParseObject(json);
            JToken? versionToken = root["schemaVersion"];
            if (versionToken == null || versionToken.Type == JTokenType.Null)
            {
                throw new SaveMigrationException("Save is missing required 'schemaVersion'.");
            }
            if (versionToken.Type != JTokenType.Integer)
            {
                throw new SaveMigrationException("Save 'schemaVersion' must be an integer, got " + versionToken.Type + ".");
            }

            long version = versionToken.Value<long>();
            if (version > CurrentSchemaVersion)
            {
                throw new SaveMigrationException(
                    "Save schemaVersion " + version + " is newer than this build supports (" + CurrentSchemaVersion + ").");
            }
            if (version < MinimumSupportedSchemaVersion)
            {
                throw new SaveMigrationException(
                    "Save schemaVersion " + version + " is not supported (minimum " + MinimumSupportedSchemaVersion + ").");
            }

            int current = (int)version;
            while (current < CurrentSchemaVersion)
            {
                if (!MigrationSteps.TryGetValue(current, out var step))
                {
                    throw new SaveMigrationException("No migration path from save schemaVersion " + current + ".");
                }
                root = step(root);
                current++;
                root["schemaVersion"] = current;
            }

            // Newtonsoft would silently coerce "5" -> uint, 1.5 -> int, "true" -> bool, 5 -> string; enforce schema JSON types first.
            var typeErrors = new List<string>();
            SaveJsonTypes.Check(root, SaveJsonTypes.Root, "$", typeErrors);
            if (typeErrors.Count > 0)
            {
                throw new SaveMigrationException("Save does not match save_state.schema.json types:\n  " + string.Join("\n  ", typeErrors));
            }

            SaveState? state;
            try
            {
                JsonSerializer serializer = JsonSerializer.Create(CreateSettings(Formatting.None));
                using (JsonReader reader = root.CreateReader())
                {
                    state = serializer.Deserialize<SaveState>(reader);
                }
            }
            catch (JsonException ex)
            {
                throw new SaveMigrationException("Save does not match save_state.schema.json: " + ex.Message, ex);
            }
            catch (OverflowException ex)
            {
                throw new SaveMigrationException("Save contains an out-of-range number: " + ex.Message, ex);
            }

            if (state == null)
            {
                throw new SaveMigrationException("Save JSON deserialized to null.");
            }

            ThrowIfInvalid(state);
            return state;
        }

        /// <summary>
        /// Serializes to camelCase JSON in schema property order (deterministic for identical state).
        /// </summary>
        public static string Serialize(SaveState state, bool indented = true)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            return JsonConvert.SerializeObject(state, CreateSettings(indented ? Formatting.Indented : Formatting.None));
        }

        /// <summary>
        /// Reads and migrates a save file. File-system errors (e.g. <see cref="FileNotFoundException"/>) propagate unchanged
        /// so callers can tell "no save yet" from "corrupt save" (<see cref="SaveMigrationException"/>).
        /// </summary>
        public static SaveState LoadFromFile(string path)
        {
            if (path == null || path.Length == 0)
            {
                throw new ArgumentException("Save path is empty.", nameof(path));
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            return DeserializeAndMigrate(json);
        }

        /// <summary>
        /// Validates then writes the save atomically (temp file + replace), creating the parent directory if needed.
        /// </summary>
        public static void SaveToFile(string path, SaveState state)
        {
            if (path == null || path.Length == 0)
            {
                throw new ArgumentException("Save path is empty.", nameof(path));
            }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            ThrowIfInvalid(state);

            string fullPath = Path.GetFullPath(path);
            string? dir = Path.GetDirectoryName(fullPath);
            if (dir != null && dir.Length > 0)
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = fullPath + ".tmp";
            File.WriteAllText(tempPath, Serialize(state, indented: true), new UTF8Encoding(false));
            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }

        /// <summary>
        /// Checks every save_state.schema.json constraint plus internal consistency (active bike exists, unique ids).
        /// Returns an empty list when valid.
        /// </summary>
        public static IReadOnlyList<string> Validate(SaveState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var errors = new List<string>();
            if (state.SchemaVersion != CurrentSchemaVersion)
            {
                errors.Add("schemaVersion must be " + CurrentSchemaVersion + ", got " + state.SchemaVersion + ".");
            }
            if (state.CashUsd < 0m)
            {
                errors.Add("cashUsd must be >= 0.");
            }
            if (state.Reputation < 0)
            {
                errors.Add("reputation must be >= 0.");
            }
            if (IsNullOrEmpty(state.ActiveBikeInstanceId))
            {
                errors.Add("activeBikeInstanceId must be a non-empty string.");
            }
            CheckPattern(errors, "activeZoneId", state.ActiveZoneId, ZoneIdPattern);
            if (state.ToolRollCapacity < MinToolRollCapacity || state.ToolRollCapacity > MaxToolRollCapacity)
            {
                errors.Add("toolRollCapacity must be within " + MinToolRollCapacity + ".." + MaxToolRollCapacity + ".");
            }

            if (state.Bikes == null || state.Bikes.Count == 0)
            {
                errors.Add("bikes must contain at least one bike.");
            }
            else
            {
                var bikeIds = new HashSet<string>(StringComparer.Ordinal);
                bool activeFound = false;
                for (int i = 0; i < state.Bikes.Count; i++)
                {
                    ValidateBike(state.Bikes[i], "bikes[" + i + "]", errors, bikeIds);
                    if (state.Bikes[i] != null && string.Equals(state.Bikes[i].InstanceId, state.ActiveBikeInstanceId, StringComparison.Ordinal))
                    {
                        activeFound = true;
                    }
                }
                if (!IsNullOrEmpty(state.ActiveBikeInstanceId) && !activeFound)
                {
                    errors.Add("activeBikeInstanceId '" + state.ActiveBikeInstanceId + "' does not match any bike.");
                }
            }

            if (state.Inventory == null)
            {
                errors.Add("inventory is required.");
            }
            else
            {
                var itemIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < state.Inventory.Count; i++)
                {
                    string ctx = "inventory[" + i + "]";
                    InventoryItemState? item = state.Inventory[i];
                    if (item == null)
                    {
                        errors.Add(ctx + " is null.");
                        continue;
                    }
                    if (IsNullOrEmpty(item.InstanceId))
                    {
                        errors.Add(ctx + ".instanceId must be a non-empty string.");
                    }
                    else if (!itemIds.Add(item.InstanceId))
                    {
                        errors.Add(ctx + ".instanceId '" + item.InstanceId + "' is duplicated.");
                    }
                    CheckPattern(errors, ctx + ".partId", item.PartId, PartIdPattern);
                    if (item.Quantity < 1)
                    {
                        errors.Add(ctx + ".quantity must be >= 1.");
                    }
                    CheckUnit(errors, ctx + ".condition", item.Condition);
                    CheckUnit(errors, ctx + ".wear", item.Wear);
                }
            }

            if (state.ActiveToolRoll == null)
            {
                errors.Add("activeToolRoll is required.");
            }
            else
            {
                for (int i = 0; i < state.ActiveToolRoll.Count; i++)
                {
                    CheckPattern(errors, "activeToolRoll[" + i + "]", state.ActiveToolRoll[i], PartIdPattern);
                }
            }

            if (state.CompletedGigs == null)
            {
                errors.Add("completedGigs is required.");
            }
            else
            {
                for (int i = 0; i < state.CompletedGigs.Count; i++)
                {
                    string ctx = "completedGigs[" + i + "]";
                    CompletedGigState? gig = state.CompletedGigs[i];
                    if (gig == null)
                    {
                        errors.Add(ctx + " is null.");
                        continue;
                    }
                    CheckPattern(errors, ctx + ".jobId", gig.JobId, JobIdPattern);
                    CheckNonNegative(errors, ctx + ".bestTimeSeconds", gig.BestTimeSeconds);
                    if (gig.Completions < 1)
                    {
                        errors.Add(ctx + ".completions must be >= 1.");
                    }
                    if (IsNullOrEmpty(gig.LastCompletedUtc))
                    {
                        errors.Add(ctx + ".lastCompletedUtc must be a non-empty string.");
                    }
                }
            }

            if (state.UnlockedZones == null || state.UnlockedZones.Count == 0)
            {
                errors.Add("unlockedZones must contain at least one zone.");
            }
            else
            {
                for (int i = 0; i < state.UnlockedZones.Count; i++)
                {
                    CheckPattern(errors, "unlockedZones[" + i + "]", state.UnlockedZones[i], ZoneIdPattern);
                }
            }

            return errors;
        }

        /// <summary>
        /// Checks that every id in the save resolves against the catalog (archetypes, parts, archetype slots/fasteners,
        /// zones, jobs, faults). Returns an empty list when consistent.
        /// </summary>
        public static IReadOnlyList<string> ValidateAgainstCatalog(SaveState state, CatalogDatabase catalog)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var errors = new List<string>();
            if (!catalog.Zones.ContainsKey(state.ActiveZoneId))
            {
                errors.Add("activeZoneId: unknown zone '" + state.ActiveZoneId + "'.");
            }
            for (int i = 0; i < state.UnlockedZones.Count; i++)
            {
                if (!catalog.Zones.ContainsKey(state.UnlockedZones[i]))
                {
                    errors.Add("unlockedZones[" + i + "]: unknown zone '" + state.UnlockedZones[i] + "'.");
                }
            }
            for (int i = 0; i < state.Bikes.Count; i++)
            {
                BikeSaveState bike = state.Bikes[i];
                string ctx = "bikes[" + i + "]";
                if (!catalog.Archetypes.TryGetValue(bike.ArchetypeId, out var archetype))
                {
                    errors.Add(ctx + ".archetypeId: unknown archetype '" + bike.ArchetypeId + "'.");
                    continue;
                }
                var slotIds = new HashSet<string>(StringComparer.Ordinal);
                for (int s = 0; s < archetype.MountSlots.Count; s++)
                {
                    slotIds.Add(archetype.MountSlots[s].SlotId);
                }
                var fastenerIds = new HashSet<string>(StringComparer.Ordinal);
                for (int f = 0; f < archetype.Fasteners.Count; f++)
                {
                    fastenerIds.Add(archetype.Fasteners[f].FastenerId);
                }
                for (int p = 0; p < bike.InstalledParts.Count; p++)
                {
                    InstalledPartSaveState installed = bike.InstalledParts[p];
                    if (!slotIds.Contains(installed.SlotId))
                    {
                        errors.Add(ctx + ".installedParts[" + p + "]: slot '" + installed.SlotId + "' is not in archetype " + archetype.Id + ".");
                    }
                    if (!catalog.Parts.ContainsKey(installed.PartId))
                    {
                        errors.Add(ctx + ".installedParts[" + p + "]: unknown part '" + installed.PartId + "'.");
                    }
                }
                for (int f = 0; f < bike.Fasteners.Count; f++)
                {
                    if (!fastenerIds.Contains(bike.Fasteners[f].FastenerId))
                    {
                        errors.Add(ctx + ".fasteners[" + f + "]: fastener '" + bike.Fasteners[f].FastenerId + "' is not in archetype " + archetype.Id + ".");
                    }
                }
                for (int a = 0; a < bike.ActiveFaultIds.Count; a++)
                {
                    if (!catalog.Faults.ContainsKey(bike.ActiveFaultIds[a]))
                    {
                        errors.Add(ctx + ".activeFaultIds[" + a + "]: unknown fault '" + bike.ActiveFaultIds[a] + "'.");
                    }
                }
            }
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                if (!catalog.Parts.ContainsKey(state.Inventory[i].PartId))
                {
                    errors.Add("inventory[" + i + "]: unknown part '" + state.Inventory[i].PartId + "'.");
                }
            }
            for (int i = 0; i < state.ActiveToolRoll.Count; i++)
            {
                if (!catalog.TryGetPart(state.ActiveToolRoll[i], out var tool) || tool.Category != PartCategory.ToolRollItem)
                {
                    errors.Add("activeToolRoll[" + i + "]: '" + state.ActiveToolRoll[i] + "' is not a Tool/Spare part.");
                }
            }
            for (int i = 0; i < state.CompletedGigs.Count; i++)
            {
                if (!catalog.Jobs.ContainsKey(state.CompletedGigs[i].JobId))
                {
                    errors.Add("completedGigs[" + i + "]: unknown job '" + state.CompletedGigs[i].JobId + "'.");
                }
            }
            return errors;
        }

        private static SaveState NewStateAround(uint seed, BikeSaveState bike, int toolRollCapacity)
        {
            var state = new SaveState
            {
                SchemaVersion = CurrentSchemaVersion,
                Seed = seed,
                CashUsd = DefaultStartingCashUsd,
                Reputation = 0,
                ActiveBikeInstanceId = bike.InstanceId,
                ActiveZoneId = DefaultStartZoneId,
                ToolRollCapacity = toolRollCapacity,
                DiagnosticHintsEnabled = true,
            };
            state.Bikes.Add(bike);
            state.UnlockedZones.Add(DefaultStartZoneId);
            return state;
        }

        private static void ValidateBike(BikeSaveState? bike, string ctx, List<string> errors, HashSet<string> bikeIds)
        {
            if (bike == null)
            {
                errors.Add(ctx + " is null.");
                return;
            }
            if (IsNullOrEmpty(bike.InstanceId))
            {
                errors.Add(ctx + ".instanceId must be a non-empty string.");
            }
            else if (!bikeIds.Add(bike.InstanceId))
            {
                errors.Add(ctx + ".instanceId '" + bike.InstanceId + "' is duplicated.");
            }
            CheckPattern(errors, ctx + ".archetypeId", bike.ArchetypeId, ArchetypeIdPattern);
            if (IsNullOrEmpty(bike.Nickname))
            {
                errors.Add(ctx + ".nickname must be a non-empty string.");
            }
            if (!(bike.OdometerKm >= 0.0) || double.IsInfinity(bike.OdometerKm))
            {
                errors.Add(ctx + ".odometerKm must be a finite number >= 0.");
            }
            CheckPattern(errors, ctx + ".paintHex", bike.PaintHex, PaintHexPattern);
            CheckUnit(errors, ctx + ".patinaLevel", bike.PatinaLevel);
            if (bike.DecalStyle != null && !BikeCustomizationState.IsValidDecalStyle(bike.DecalStyle))
            {
                errors.Add(ctx + ".decalStyle must be at most " + BikeCustomizationState.MaxDecalStyleLength + " characters.");
            }

            FuelTankSaveState? tank = bike.FuelTank;
            if (tank == null)
            {
                errors.Add(ctx + ".fuelTank is required.");
            }
            else
            {
                CheckNonNegative(errors, ctx + ".fuelTank.fuelLiters", tank.FuelLiters);
                CheckNonNegative(errors, ctx + ".fuelTank.premixRatio", tank.PremixRatio);
                CheckUnit(errors, ctx + ".fuelTank.rustContamination", tank.RustContamination);
                if (tank.PetcockMode != PetcockMode.Off && tank.PetcockMode != PetcockMode.On && tank.PetcockMode != PetcockMode.Reserve)
                {
                    errors.Add(ctx + ".fuelTank.petcockMode is not a valid PetcockMode.");
                }
            }

            if (bike.InstalledParts == null)
            {
                errors.Add(ctx + ".installedParts is required.");
            }
            else
            {
                var slots = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < bike.InstalledParts.Count; i++)
                {
                    string pCtx = ctx + ".installedParts[" + i + "]";
                    InstalledPartSaveState? part = bike.InstalledParts[i];
                    if (part == null)
                    {
                        errors.Add(pCtx + " is null.");
                        continue;
                    }
                    CheckPattern(errors, pCtx + ".slotId", part.SlotId, SlotIdPattern);
                    if (!IsNullOrEmpty(part.SlotId) && !slots.Add(part.SlotId))
                    {
                        errors.Add(pCtx + ".slotId '" + part.SlotId + "' is occupied twice.");
                    }
                    CheckPattern(errors, pCtx + ".partId", part.PartId, PartIdPattern);
                    CheckUnit(errors, pCtx + ".condition", part.Condition);
                    CheckUnit(errors, pCtx + ".wear", part.Wear);
                    CheckUnit(errors, pCtx + ".carbonClog", part.CarbonClog);
                    CheckUnit(errors, pCtx + ".foulingLevel", part.FoulingLevel);
                    CheckUnit(errors, pCtx + ".insulatorColorIndex", part.InsulatorColorIndex);
                    if (!(part.PointsGapMm >= 0f && part.PointsGapMm <= 2f))
                    {
                        errors.Add(pCtx + ".pointsGapMm must be within 0..2.");
                    }
                }
            }

            if (bike.Fasteners == null)
            {
                errors.Add(ctx + ".fasteners is required.");
            }
            else
            {
                var fasteners = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < bike.Fasteners.Count; i++)
                {
                    string fCtx = ctx + ".fasteners[" + i + "]";
                    FastenerSaveState? f = bike.Fasteners[i];
                    if (f == null)
                    {
                        errors.Add(fCtx + " is null.");
                        continue;
                    }
                    CheckPattern(errors, fCtx + ".fastenerId", f.FastenerId, FastenerIdPattern);
                    if (!IsNullOrEmpty(f.FastenerId) && !fasteners.Add(f.FastenerId))
                    {
                        errors.Add(fCtx + ".fastenerId '" + f.FastenerId + "' is duplicated.");
                    }
                    CheckNonNegative(errors, fCtx + ".currentTorqueNm", f.CurrentTorqueNm);
                }
            }

            if (bike.ActiveFaultIds == null)
            {
                errors.Add(ctx + ".activeFaultIds is required.");
            }
            else
            {
                for (int i = 0; i < bike.ActiveFaultIds.Count; i++)
                {
                    CheckPattern(errors, ctx + ".activeFaultIds[" + i + "]", bike.ActiveFaultIds[i], FaultIdPattern);
                }
            }
        }

        private static void ThrowIfInvalid(SaveState state)
        {
            IReadOnlyList<string> errors = Validate(state);
            if (errors.Count > 0)
            {
                throw new SaveMigrationException("Save state is invalid:\n  " + string.Join("\n  ", errors));
            }
        }

        private static JObject ParseObject(string json)
        {
            try
            {
                using (var textReader = new StringReader(json))
                using (var reader = new JsonTextReader(textReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Decimal;
                    JToken root = JToken.ReadFrom(reader);
                    if (reader.Read() && reader.TokenType != JsonToken.Comment)
                    {
                        throw new SaveMigrationException("Save JSON has unexpected trailing content.");
                    }
                    if (!(root is JObject obj))
                    {
                        throw new SaveMigrationException("Save JSON root must be an object, got " + root.Type + ".");
                    }
                    return obj;
                }
            }
            catch (JsonException ex)
            {
                throw new SaveMigrationException("Save is not valid JSON: " + ex.Message, ex);
            }
        }

        private static JsonSerializerSettings CreateSettings(Formatting formatting)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = formatting,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                Culture = CultureInfo.InvariantCulture,
            };
            settings.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
            return settings;
        }

        private static bool IsNullOrEmpty(string? value)
        {
            return value == null || value.Length == 0;
        }

        private static void CheckPattern(List<string> errors, string ctx, string? value, Regex pattern)
        {
            if (value == null || !pattern.IsMatch(value))
            {
                errors.Add(ctx + " '" + (value ?? "<null>") + "' must match " + pattern + ".");
            }
        }

        private static void CheckUnit(List<string> errors, string ctx, float value)
        {
            if (!(value >= 0f && value <= 1f))
            {
                errors.Add(ctx + " must be within 0..1.");
            }
        }

        private static void CheckNonNegative(List<string> errors, string ctx, float value)
        {
            if (!(value >= 0f) || float.IsInfinity(value))
            {
                errors.Add(ctx + " must be a finite number >= 0.");
            }
        }
    }

    /// <summary>
    /// JSON token-type shape of save_state.schema.json (schemaVersion 1), checked before deserialization so that
    /// wrongly-typed values are rejected instead of coerced. Keep in sync with <see cref="SaveState"/>.
    /// </summary>
    internal static class SaveJsonTypes
    {
        internal enum Kind
        {
            Integer,
            Number,
            String,
            Boolean,
            Object,
            ArrayOfObjects,
            ArrayOfStrings,
        }

        internal sealed class Shape
        {
            public readonly Dictionary<string, KeyValuePair<Kind, Shape?>> Members = new Dictionary<string, KeyValuePair<Kind, Shape?>>(StringComparer.Ordinal);

            public Shape Add(string name, Kind kind, Shape? child = null)
            {
                Members[name] = new KeyValuePair<Kind, Shape?>(kind, child);
                return this;
            }
        }

        internal static readonly Shape Root = BuildRoot();

        public static void Check(JObject obj, Shape shape, string path, List<string> errors)
        {
            foreach (JProperty prop in obj.Properties())
            {
                if (!shape.Members.TryGetValue(prop.Name, out var expected))
                {
                    continue; // unknown members are rejected by MissingMemberHandling.Error with a precise message
                }
                string childPath = path + "." + prop.Name;
                JToken? value = prop.Value;
                if (value == null || value.Type == JTokenType.Null)
                {
                    continue; // nulls are rejected by Required.Always
                }
                switch (expected.Key)
                {
                    case Kind.Integer:
                        if (value.Type != JTokenType.Integer)
                        {
                            errors.Add(childPath + " must be an integer, got " + value.Type + ".");
                        }
                        break;
                    case Kind.Number:
                        if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                        {
                            errors.Add(childPath + " must be a number, got " + value.Type + ".");
                        }
                        break;
                    case Kind.String:
                        if (value.Type != JTokenType.String)
                        {
                            errors.Add(childPath + " must be a string, got " + value.Type + ".");
                        }
                        break;
                    case Kind.Boolean:
                        if (value.Type != JTokenType.Boolean)
                        {
                            errors.Add(childPath + " must be a boolean, got " + value.Type + ".");
                        }
                        break;
                    case Kind.Object:
                        if (value is JObject child)
                        {
                            Check(child, expected.Value!, childPath, errors);
                        }
                        else
                        {
                            errors.Add(childPath + " must be an object, got " + value.Type + ".");
                        }
                        break;
                    case Kind.ArrayOfObjects:
                    case Kind.ArrayOfStrings:
                        if (!(value is JArray arr))
                        {
                            errors.Add(childPath + " must be an array, got " + value.Type + ".");
                            break;
                        }
                        for (int i = 0; i < arr.Count; i++)
                        {
                            string itemPath = childPath + "[" + i + "]";
                            JToken item = arr[i];
                            if (expected.Key == Kind.ArrayOfStrings)
                            {
                                if (item.Type != JTokenType.String)
                                {
                                    errors.Add(itemPath + " must be a string, got " + item.Type + ".");
                                }
                            }
                            else if (item is JObject itemObj)
                            {
                                Check(itemObj, expected.Value!, itemPath, errors);
                            }
                            else
                            {
                                errors.Add(itemPath + " must be an object, got " + item.Type + ".");
                            }
                        }
                        break;
                }
            }
        }

        private static Shape BuildRoot()
        {
            Shape fuelTank = new Shape()
                .Add("fuelLiters", Kind.Number)
                .Add("premixRatio", Kind.Number)
                .Add("petcockMode", Kind.String)
                .Add("rustContamination", Kind.Number);
            Shape installedPart = new Shape()
                .Add("slotId", Kind.String)
                .Add("partId", Kind.String)
                .Add("condition", Kind.Number)
                .Add("wear", Kind.Number)
                .Add("carbonClog", Kind.Number)
                .Add("foulingLevel", Kind.Number)
                .Add("insulatorColorIndex", Kind.Number)
                .Add("pointsGapMm", Kind.Number)
                .Add("installedArrowTowardExhaust", Kind.Boolean);
            Shape fastener = new Shape()
                .Add("fastenerId", Kind.String)
                .Add("currentTorqueNm", Kind.Number)
                .Add("isStripped", Kind.Boolean)
                .Add("isSeized", Kind.Boolean)
                .Add("hasPenetratingOil", Kind.Boolean);
            Shape bike = new Shape()
                .Add("instanceId", Kind.String)
                .Add("archetypeId", Kind.String)
                .Add("nickname", Kind.String)
                .Add("odometerKm", Kind.Number)
                .Add("paintHex", Kind.String)
                .Add("patinaLevel", Kind.Number)
                .Add("decalStyle", Kind.String)
                .Add("fuelTank", Kind.Object, fuelTank)
                .Add("installedParts", Kind.ArrayOfObjects, installedPart)
                .Add("fasteners", Kind.ArrayOfObjects, fastener)
                .Add("activeFaultIds", Kind.ArrayOfStrings);
            Shape inventoryItem = new Shape()
                .Add("instanceId", Kind.String)
                .Add("partId", Kind.String)
                .Add("quantity", Kind.Integer)
                .Add("condition", Kind.Number)
                .Add("wear", Kind.Number);
            Shape gig = new Shape()
                .Add("jobId", Kind.String)
                .Add("bestTimeSeconds", Kind.Number)
                .Add("completions", Kind.Integer)
                .Add("lastCompletedUtc", Kind.String);
            return new Shape()
                .Add("schemaVersion", Kind.Integer)
                .Add("seed", Kind.Integer)
                .Add("cashUsd", Kind.Number)
                .Add("reputation", Kind.Integer)
                .Add("activeBikeInstanceId", Kind.String)
                .Add("activeZoneId", Kind.String)
                .Add("toolRollCapacity", Kind.Integer)
                .Add("bikes", Kind.ArrayOfObjects, bike)
                .Add("inventory", Kind.ArrayOfObjects, inventoryItem)
                .Add("activeToolRoll", Kind.ArrayOfStrings)
                .Add("completedGigs", Kind.ArrayOfObjects, gig)
                .Add("unlockedZones", Kind.ArrayOfStrings)
                .Add("diagnosticHintsEnabled", Kind.Boolean);
        }
    }
}
