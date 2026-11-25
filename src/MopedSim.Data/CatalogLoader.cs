using System;
using System.Collections.Generic;
using System.IO;
using MopedSim.Core;
using Newtonsoft.Json.Linq;

namespace MopedSim.Data
{
    /// <summary>
    /// Loads data/catalogs/*.json into a <see cref="CatalogDatabase"/> of MopedSim.Core types and validates cross references.
    /// <para>
    /// Loading is strict about structure (malformed JSON, missing required fields, wrong JSON types, duplicate IDs,
    /// unknown enum strings all throw <see cref="CatalogLoadException"/>). Cross-catalog references and the
    /// MopedSim.Core slot/fastener contract are checked separately by <see cref="Validate"/>.
    /// </para>
    /// </summary>
    public static class CatalogLoader
    {
        public const string PartsFileName = "parts.json";
        public const string ArchetypesFileName = "archetypes.json";
        public const string FaultsFileName = "faults.json";
        public const string ZonesFileName = "zones.json";
        public const string JobsFileName = "jobs.json";

        /// <summary>
        /// Floor for <see cref="BikeArchetypeSpec.BaseDryMassKg"/> (unmodelled frame hardware) when the catalog's whole-bike
        /// baseDryMassKg minus the stock parts' mass would otherwise drop below it.
        /// </summary>
        public const float MinResidualHardwareMassKg = 2.0f;

        /// <summary>Highest catalog schemaVersion this loader understands.</summary>
        public const int SupportedSchemaVersion = 1;

        /// <summary>
        /// Reads the five catalog files from <paramref name="catalogsDir"/> (e.g. "data/catalogs").
        /// </summary>
        public static CatalogDatabase LoadFromDirectory(string catalogsDir)
        {
            if (catalogsDir == null || catalogsDir.Length == 0)
            {
                throw new CatalogLoadException("catalogsDir", "catalog directory path is empty.");
            }
            if (!Directory.Exists(catalogsDir))
            {
                throw new CatalogLoadException(catalogsDir, "catalog directory does not exist.");
            }

            return LoadFromJson(
                ReadCatalogFile(catalogsDir, PartsFileName),
                ReadCatalogFile(catalogsDir, ArchetypesFileName),
                ReadCatalogFile(catalogsDir, FaultsFileName),
                ReadCatalogFile(catalogsDir, ZonesFileName),
                ReadCatalogFile(catalogsDir, JobsFileName));
        }

        /// <summary>
        /// Loads from a directory and throws <see cref="CatalogLoadException"/> listing every error if <see cref="Validate"/> fails.
        /// </summary>
        public static CatalogDatabase LoadAndValidateFromDirectory(string catalogsDir)
        {
            CatalogDatabase db = LoadFromDirectory(catalogsDir);
            CatalogValidationReport report = Validate(db);
            if (!report.IsValid)
            {
                throw new CatalogLoadException(catalogsDir, report.ToString());
            }
            return db;
        }

        /// <summary>
        /// Parses the five catalog JSON documents (contents of parts.json, archetypes.json, faults.json, zones.json, jobs.json).
        /// </summary>
        public static CatalogDatabase LoadFromJson(string partsJson, string archetypesJson, string faultsJson, string zonesJson, string jobsJson)
        {
            JObject partsRoot = JsonRead.ParseRootObject(partsJson, PartsFileName);
            JObject archetypesRoot = JsonRead.ParseRootObject(archetypesJson, ArchetypesFileName);
            JObject faultsRoot = JsonRead.ParseRootObject(faultsJson, FaultsFileName);
            JObject zonesRoot = JsonRead.ParseRootObject(zonesJson, ZonesFileName);
            JObject jobsRoot = JsonRead.ParseRootObject(jobsJson, JobsFileName);

            int schemaVersion = ReadRootSchemaVersion(partsRoot, PartsFileName);
            int[] others =
            {
                ReadRootSchemaVersion(archetypesRoot, ArchetypesFileName),
                ReadRootSchemaVersion(faultsRoot, FaultsFileName),
                ReadRootSchemaVersion(zonesRoot, ZonesFileName),
                ReadRootSchemaVersion(jobsRoot, JobsFileName),
            };
            for (int i = 0; i < others.Length; i++)
            {
                if (others[i] != schemaVersion)
                {
                    throw new CatalogLoadException("catalogs", "catalog files disagree on schemaVersion (" + schemaVersion + " vs " + others[i] + ").");
                }
            }

            var parts = new List<PartDefinition>();
            var partMeta = new List<PartMetadata>();
            ParseParts(partsRoot, parts, partMeta);

            var partIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Count; i++)
            {
                partIndex[parts[i].Id] = i;
            }

            var archetypes = new List<BikeArchetypeSpec>();
            var archetypeMeta = new List<ArchetypeMetadata>();
            ParseArchetypes(archetypesRoot, parts, partMeta, partIndex, archetypes, archetypeMeta);

            var faults = new List<FaultDefinition>();
            var faultMeta = new List<FaultMetadata>();
            ParseFaults(faultsRoot, faults, faultMeta);

            var zones = new List<ZoneEnvironmentSpec>();
            var zoneMeta = new List<ZoneMetadata>();
            ParseZones(zonesRoot, zones, zoneMeta);

            var jobs = new List<JobDefinition>();
            var jobMeta = new List<JobMetadata>();
            ParseJobs(jobsRoot, jobs, jobMeta);

            return new CatalogDatabase(schemaVersion, parts, partMeta, archetypes, archetypeMeta, faults, faultMeta, zones, zoneMeta, jobs, jobMeta);
        }

        /// <summary>
        /// Checks cross-catalog references, archetype DAG integrity, category-mapping safety, and the MopedSim.Core
        /// slot/fastener contract (see <see cref="CoreIdContract"/>). Never throws for data problems.
        /// </summary>
        public static CatalogValidationReport Validate(CatalogDatabase db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }
            return new CatalogValidationReport(CatalogValidator.Run(db));
        }

        private static string ReadCatalogFile(string dir, string fileName)
        {
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                throw new CatalogLoadException(fileName, "catalog file not found at '" + path + "'.");
            }
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                throw new CatalogLoadException(fileName, "failed to read '" + path + "' (" + ex.Message + ")", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new CatalogLoadException(fileName, "failed to read '" + path + "' (" + ex.Message + ")", ex);
            }
        }

        private static int ReadRootSchemaVersion(JObject root, string source)
        {
            int version = JsonRead.RequireInt(root, "schemaVersion", source + ": <root>");
            if (version < 1 || version > SupportedSchemaVersion)
            {
                throw new CatalogLoadException(source, "unsupported catalog schemaVersion " + version + " (supported: 1.." + SupportedSchemaVersion + ").");
            }
            return version;
        }

        private static int ReadEntrySchemaVersion(JObject entry, string ctx)
        {
            int version = JsonRead.OptionalInt(entry, "schemaVersion", ctx, 1);
            if (version < 1 || version > SupportedSchemaVersion)
            {
                throw JsonRead.Fail(ctx, "unsupported entry schemaVersion " + version + ".");
            }
            return version;
        }

        private static void CheckUnique(HashSet<string> seen, string id, string ctx)
        {
            if (!seen.Add(id))
            {
                throw JsonRead.Fail(ctx, "duplicate id '" + id + "'.");
            }
        }

        // ------------------------------------------------------------------ parts

        private static void ParseParts(JObject root, List<PartDefinition> parts, List<PartMetadata> meta)
        {
            JArray arr = JsonRead.RequireArray(root, "parts", PartsFileName + ": <root>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
            {
                string ctx = PartsFileName + ": parts[" + i + "]";
                JObject entry = JsonRead.AsObject(arr[i], ctx);
                string id = JsonRead.RequireString(entry, "id", ctx);
                ctx = PartsFileName + ": parts[" + i + "] (" + id + ")";
                CheckUnique(seen, id, ctx);

                string rawCategory = JsonRead.RequireString(entry, "category", ctx);
                JObject stats = JsonRead.OptionalObject(entry, "stats", ctx) ?? new JObject();
                string statsCtx = JsonRead.Ctx(ctx, "stats");

                var def = new PartDefinition
                {
                    SchemaVersion = ReadEntrySchemaVersion(entry, ctx),
                    Id = id,
                    DisplayName = JsonRead.RequireString(entry, "displayName", ctx),
                    Category = CategoryMapping.MapPartCategory(rawCategory, stats, ctx),
                    CompatibleInterfaces = JsonRead.OptionalStringList(entry, "compatibleInterfaces", ctx).ToArray(),
                    MeshAssetPath = JsonRead.OptionalString(entry, "meshAssetPath", ctx) ?? string.Empty,
                    RequiredSockets = JsonRead.OptionalStringList(entry, "requiredSockets", ctx).ToArray(),
                    MassKg = (float)JsonRead.RequireNumber(entry, "massKg", ctx),
                    PriceUsd = (decimal)JsonRead.RequireNumber(entry, "priceUsd", ctx),
                    Stats = StatsMapping.MapStats(rawCategory, stats, statsCtx),
                };
                parts.Add(def);

                var numeric = new Dictionary<string, double>(StringComparer.Ordinal);
                var text = new Dictionary<string, string>(StringComparer.Ordinal);
                var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (JProperty prop in stats.Properties())
                {
                    JToken? value = prop.Value;
                    if (value == null)
                    {
                        continue;
                    }
                    switch (value.Type)
                    {
                        case JTokenType.Integer:
                        case JTokenType.Float:
                            numeric[prop.Name] = JsonRead.AsNumber(value, JsonRead.Ctx(statsCtx, prop.Name));
                            break;
                        case JTokenType.String:
                            text[prop.Name] = (string?)value ?? string.Empty;
                            break;
                        case JTokenType.Boolean:
                            flags[prop.Name] = value.Value<bool>();
                            break;
                    }
                }

                meta.Add(new PartMetadata(
                    id,
                    rawCategory,
                    def.MeshAssetPath,
                    JsonRead.OptionalBool(entry, "has3dMesh", ctx, def.MeshAssetPath.Length > 0),
                    JsonRead.OptionalString(entry, "assetClass", ctx) ?? string.Empty,
                    Array.AsReadOnly(def.RequiredSockets),
                    JsonRead.OptionalInt(entry, "toolRollSlots", ctx, 0),
                    JsonRead.OptionalString(entry, "description", ctx) ?? string.Empty,
                    numeric,
                    text,
                    flags));
            }
        }

        // ------------------------------------------------------------- archetypes

        private static void ParseArchetypes(
            JObject root,
            List<PartDefinition> parts,
            List<PartMetadata> partMeta,
            Dictionary<string, int> partIndex,
            List<BikeArchetypeSpec> archetypes,
            List<ArchetypeMetadata> meta)
        {
            JArray arr = JsonRead.RequireArray(root, "archetypes", ArchetypesFileName + ": <root>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
            {
                string ctx = ArchetypesFileName + ": archetypes[" + i + "]";
                JObject entry = JsonRead.AsObject(arr[i], ctx);
                string id = JsonRead.RequireString(entry, "id", ctx);
                ctx = ArchetypesFileName + ": archetypes[" + i + "] (" + id + ")";
                CheckUnique(seen, id, ctx);

                string architecture = JsonRead.RequireString(entry, "drivetrainArchitecture", ctx);
                DrivetrainType drivetrain = CategoryMapping.MapArchetypeDrivetrain(architecture, JsonRead.Ctx(ctx, "drivetrainArchitecture"));

                var spec = new BikeArchetypeSpec
                {
                    SchemaVersion = ReadEntrySchemaVersion(entry, ctx),
                    Id = id,
                    DisplayName = JsonRead.RequireString(entry, "displayName", ctx),
                    DrivetrainType = drivetrain,
                    BaseDryMassKg = (float)JsonRead.RequireNumber(entry, "baseDryMassKg", ctx),
                    WheelbaseM = (float)JsonRead.RequireNumber(entry, "wheelbaseMeters", ctx),
                };

                // --- mount slots
                JArray slotArr = JsonRead.RequireArray(entry, "mountSlots", ctx);
                var slotIds = new HashSet<string>(StringComparer.Ordinal);
                var slotMeta = new List<SlotMetadata>();
                var fastenerOwners = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int s = 0; s < slotArr.Count; s++)
                {
                    string sCtx = ctx + ".mountSlots[" + s + "]";
                    JObject slot = JsonRead.AsObject(slotArr[s], sCtx);
                    string slotId = JsonRead.RequireString(slot, "slotId", sCtx);
                    sCtx = sCtx + " (" + slotId + ")";
                    if (!slotIds.Add(slotId))
                    {
                        throw JsonRead.Fail(sCtx, "duplicate slotId '" + slotId + "'.");
                    }

                    List<string> acceptedCategories = JsonRead.OptionalStringList(slot, "acceptedCategories", sCtx);
                    if (acceptedCategories.Count == 0)
                    {
                        throw JsonRead.Fail(sCtx, "'acceptedCategories' must list at least one catalog category.");
                    }
                    for (int c = 0; c < acceptedCategories.Count; c++)
                    {
                        if (!CategoryMapping.IsKnownCatalogCategory(acceptedCategories[c]))
                        {
                            throw JsonRead.Fail(sCtx, "unknown catalog category '" + acceptedCategories[c] + "' in acceptedCategories.");
                        }
                    }

                    List<string> interfaces = JsonRead.OptionalStringList(slot, "acceptedInterfaces", sCtx);
                    List<string> blockedBy = JsonRead.OptionalStringList(slot, "blockedBy", sCtx);
                    List<string> fastenerIds = JsonRead.OptionalStringList(slot, "fastenerIds", sCtx);
                    string? parentSlotId = JsonRead.OptionalString(slot, "parentSlotId", sCtx);
                    if (parentSlotId != null && parentSlotId.Length == 0)
                    {
                        parentSlotId = null;
                    }
                    string? defaultPartId = JsonRead.OptionalString(slot, "defaultPartId", sCtx);
                    if (defaultPartId != null && defaultPartId.Length == 0)
                    {
                        defaultPartId = null;
                    }

                    PartCategory accepted = CategoryMapping.ResolveSlotCategory(
                        slotId, acceptedCategories, interfaces, defaultPartId, drivetrain, parts, partMeta, partIndex);

                    spec.MountSlots.Add(new MountSlotSpec(
                        slotId,
                        parentSlotId,
                        accepted,
                        interfaces.ToArray(),
                        blockedBy.ToArray(),
                        null,
                        null));

                    if (defaultPartId != null)
                    {
                        spec.DefaultPartIds[slotId] = defaultPartId;
                    }

                    for (int f = 0; f < fastenerIds.Count; f++)
                    {
                        // First owner wins for Core; double ownership is reported by Validate.
                        if (!fastenerOwners.ContainsKey(fastenerIds[f]))
                        {
                            fastenerOwners[fastenerIds[f]] = slotId;
                        }
                    }

                    slotMeta.Add(new SlotMetadata(
                        slotId,
                        JsonRead.OptionalString(slot, "displayName", sCtx) ?? slotId,
                        JsonRead.OptionalString(slot, "attachSocket", sCtx),
                        acceptedCategories.AsReadOnly(),
                        fastenerIds.AsReadOnly(),
                        defaultPartId,
                        JsonRead.OptionalBool(slot, "requiresOrientationCheck", sCtx, false),
                        JsonRead.OptionalBool(slot, "requiresCarbBowlOpen", sCtx, false)));
                }

                // --- fasteners (owner slot comes from mountSlots[].fastenerIds)
                JArray fastArr = JsonRead.RequireArray(entry, "fasteners", ctx);
                var fastenerIdsSeen = new HashSet<string>(StringComparer.Ordinal);
                var fastenerMeta = new List<FastenerMetadata>();
                for (int f = 0; f < fastArr.Count; f++)
                {
                    string fCtx = ctx + ".fasteners[" + f + "]";
                    JObject fastener = JsonRead.AsObject(fastArr[f], fCtx);
                    string fastenerId = JsonRead.RequireString(fastener, "fastenerId", fCtx);
                    fCtx = fCtx + " (" + fastenerId + ")";
                    if (!fastenerIdsSeen.Add(fastenerId))
                    {
                        throw JsonRead.Fail(fCtx, "duplicate fastenerId '" + fastenerId + "'.");
                    }

                    string owner = fastenerOwners.TryGetValue(fastenerId, out var ownerSlot) ? ownerSlot : string.Empty;
                    spec.Fasteners.Add(new FastenerSpec(
                        fastenerId,
                        owner,
                        (float)JsonRead.RequireNumber(fastener, "targetTorqueNm", fCtx),
                        (float)JsonRead.RequireNumber(fastener, "toleranceNm", fCtx),
                        (float)JsonRead.RequireNumber(fastener, "stripThresholdNm", fCtx),
                        (float)JsonRead.OptionalNumber(fastener, "vibrationLoosenRate", fCtx, 0.05),
                        JsonRead.OptionalBool(fastener, "defaultSeized", fCtx, false)));

                    string? requiredTool = JsonRead.OptionalString(fastener, "requiredToolId", fCtx);
                    fastenerMeta.Add(new FastenerMetadata(
                        fastenerId,
                        JsonRead.OptionalString(fastener, "displayName", fCtx) ?? fastenerId,
                        JsonRead.OptionalBool(fastener, "canSeize", fCtx, false),
                        requiredTool != null && requiredTool.Length > 0 ? requiredTool : null));
                }

                // BikeSimulation adds BaseDryMassKg + mounted part mass, while the catalog's baseDryMassKg is the whole
                // stock bike. Store only the residual (frame hardware, wiring, stand) so stock mass is not counted twice.
                float catalogDryMassKg = spec.BaseDryMassKg;
                float stockPartsMassKg = 0f;
                for (int s = 0; s < spec.MountSlots.Count; s++)
                {
                    if (spec.DefaultPartIds.TryGetValue(spec.MountSlots[s].SlotId, out var stockId)
                        && partIndex.TryGetValue(stockId, out var stockIdx))
                    {
                        stockPartsMassKg += parts[stockIdx].MassKg;
                    }
                }
                spec.BaseDryMassKg = Math.Max(MinResidualHardwareMassKg, catalogDryMassKg - stockPartsMassKg);

                // Wheel radius comes from the stock rear wheel part when available (Core default otherwise).
                if (spec.DefaultPartIds.TryGetValue(SlotIds.RearWheel, out var rearWheelId)
                    && partIndex.TryGetValue(rearWheelId, out var rearWheelIdx)
                    && parts[rearWheelIdx].Stats.WheelRadiusM > 0f)
                {
                    spec.WheelRadiusM = parts[rearWheelIdx].Stats.WheelRadiusM;
                }

                // --- fuel mix
                JObject fuel = JsonRead.RequireObject(entry, "defaultFuelMix", ctx);
                string fuelCtx = JsonRead.Ctx(ctx, "defaultFuelMix");
                var fuelMix = new FuelMixSpec(
                    (float)JsonRead.RequireNumber(fuel, "fuelLiters", fuelCtx),
                    (float)JsonRead.RequireNumber(fuel, "premixRatio", fuelCtx),
                    CategoryMapping.MapPetcockMode(JsonRead.RequireString(fuel, "petcockMode", fuelCtx), JsonRead.Ctx(fuelCtx, "petcockMode")),
                    (float)JsonRead.OptionalNumber(fuel, "rustContamination", fuelCtx, 0.0));

                archetypes.Add(spec);
                meta.Add(new ArchetypeMetadata(
                    id,
                    JsonRead.OptionalString(entry, "inspiredBy", ctx) ?? string.Empty,
                    JsonRead.OptionalString(entry, "description", ctx) ?? string.Empty,
                    architecture,
                    catalogDryMassKg,
                    stockPartsMassKg,
                    (float)JsonRead.OptionalNumber(entry, "saddleHeightMeters", ctx, 0.0),
                    (float)JsonRead.RequireNumber(entry, "fuelCapacityLiters", ctx),
                    (float)JsonRead.RequireNumber(entry, "reserveThresholdLiters", ctx),
                    JsonRead.RequireInt(entry, "defaultToolRollSlots", ctx),
                    (decimal)JsonRead.OptionalNumber(entry, "basePriceUsd", ctx, 0.0),
                    fuelMix,
                    slotMeta.AsReadOnly(),
                    fastenerMeta.AsReadOnly()));
            }
        }

        // ----------------------------------------------------------------- faults

        private static void ParseFaults(JObject root, List<FaultDefinition> faults, List<FaultMetadata> meta)
        {
            JArray arr = JsonRead.RequireArray(root, "faults", FaultsFileName + ": <root>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
            {
                string ctx = FaultsFileName + ": faults[" + i + "]";
                JObject entry = JsonRead.AsObject(arr[i], ctx);
                string id = JsonRead.RequireString(entry, "id", ctx);
                ctx = FaultsFileName + ": faults[" + i + "] (" + id + ")";
                CheckUnique(seen, id, ctx);

                List<string> symptoms = JsonRead.OptionalStringList(entry, "symptoms", ctx);
                var requiredTools = new List<string>();
                var requiredSpares = new List<string>();
                var options = new List<FaultRepairOption>();
                JArray? optArr = entry["repairOptions"] as JArray;
                if (entry["repairOptions"] != null && entry["repairOptions"]!.Type != JTokenType.Null && optArr == null)
                {
                    throw JsonRead.Fail(ctx, "'repairOptions' must be an array.");
                }
                if (optArr != null)
                {
                    for (int o = 0; o < optArr.Count; o++)
                    {
                        string oCtx = ctx + ".repairOptions[" + o + "]";
                        JObject opt = JsonRead.AsObject(optArr[o], oCtx);
                        List<string> tools = JsonRead.OptionalStringList(opt, "requiredTools", oCtx);
                        List<string> spares = JsonRead.OptionalStringList(opt, "consumedSpares", oCtx);
                        AddDistinct(requiredTools, tools);
                        AddDistinct(requiredSpares, spares);
                        options.Add(new FaultRepairOption(
                            JsonRead.RequireString(opt, "optionId", oCtx),
                            JsonRead.OptionalString(opt, "description", oCtx) ?? string.Empty,
                            tools.AsReadOnly(),
                            spares.AsReadOnly(),
                            (float)JsonRead.OptionalNumber(opt, "durationSeconds", oCtx, 0.0)));
                    }
                }

                var numericRules = new Dictionary<string, double>(StringComparer.Ordinal);
                var flagRules = new Dictionary<string, bool>(StringComparer.Ordinal);
                JObject? rules = JsonRead.OptionalObject(entry, "triggerRules", ctx);
                if (rules != null)
                {
                    foreach (JProperty prop in rules.Properties())
                    {
                        string rCtx = JsonRead.Ctx(JsonRead.Ctx(ctx, "triggerRules"), prop.Name);
                        JToken? value = prop.Value;
                        if (value == null)
                        {
                            continue;
                        }
                        if (value.Type == JTokenType.Boolean)
                        {
                            flagRules[prop.Name] = value.Value<bool>();
                        }
                        else
                        {
                            numericRules[prop.Name] = JsonRead.AsNumber(value, rCtx);
                        }
                    }
                }

                faults.Add(new FaultDefinition
                {
                    SchemaVersion = ReadEntrySchemaVersion(entry, ctx),
                    Id = id,
                    DisplayName = JsonRead.RequireString(entry, "displayName", ctx),
                    FaultType = CategoryMapping.MapFaultType(id, ctx),
                    RootCause = JsonRead.OptionalString(entry, "rootCause", ctx) ?? string.Empty,
                    Symptoms = string.Join("\n", symptoms),
                    IsFieldRepairable = JsonRead.OptionalBool(entry, "isRoadsideRepairable", ctx, true),
                    RequiredTools = requiredTools,
                    RequiredSpares = requiredSpares,
                    GarageFixDescription = JsonRead.OptionalString(entry, "garagePermanentFix", ctx) ?? string.Empty,
                });

                meta.Add(new FaultMetadata(
                    id,
                    JsonRead.OptionalString(entry, "severity", ctx) ?? string.Empty,
                    JsonRead.OptionalString(entry, "diagnosticHint", ctx) ?? string.Empty,
                    symptoms.AsReadOnly(),
                    options.AsReadOnly(),
                    numericRules,
                    flagRules));
            }
        }

        private static void AddDistinct(List<string> target, List<string> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!target.Contains(source[i]))
                {
                    target.Add(source[i]);
                }
            }
        }

        // ------------------------------------------------------------------ zones

        private static void ParseZones(JObject root, List<ZoneEnvironmentSpec> zones, List<ZoneMetadata> meta)
        {
            JArray arr = JsonRead.RequireArray(root, "zones", ZonesFileName + ": <root>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
            {
                string ctx = ZonesFileName + ": zones[" + i + "]";
                JObject entry = JsonRead.AsObject(arr[i], ctx);
                string id = JsonRead.RequireString(entry, "id", ctx);
                ctx = ZonesFileName + ": zones[" + i + "] (" + id + ")";
                CheckUnique(seen, id, ctx);

                zones.Add(new ZoneEnvironmentSpec
                {
                    SchemaVersion = ReadEntrySchemaVersion(entry, ctx),
                    Id = id,
                    DisplayName = JsonRead.RequireString(entry, "displayName", ctx),
                    AmbientTemperatureC = (float)JsonRead.RequireNumber(entry, "ambientTempCelsius", ctx),
                    AmbientAirDensity = (float)JsonRead.RequireNumber(entry, "ambientAirDensity", ctx),
                    MaxGradePercent = (float)JsonRead.RequireNumber(entry, "maxGradePercent", ctx),
                });

                var landmarks = new List<ZoneLandmark>();
                JArray? lmArr = entry["landmarks"] as JArray;
                if (lmArr != null)
                {
                    for (int l = 0; l < lmArr.Count; l++)
                    {
                        string lCtx = ctx + ".landmarks[" + l + "]";
                        JObject lm = JsonRead.AsObject(lmArr[l], lCtx);
                        landmarks.Add(new ZoneLandmark(
                            JsonRead.RequireString(lm, "id", lCtx),
                            JsonRead.OptionalString(lm, "displayName", lCtx) ?? string.Empty,
                            JsonRead.OptionalString(lm, "landmarkType", lCtx) ?? string.Empty,
                            JsonRead.OptionalVector3(lm, "position", lCtx)));
                    }
                }

                meta.Add(new ZoneMetadata(
                    id,
                    JsonRead.RequireString(entry, "sceneName", ctx),
                    JsonRead.OptionalString(entry, "description", ctx) ?? string.Empty,
                    (decimal)JsonRead.RequireNumber(entry, "towFeeUsd", ctx),
                    (float)JsonRead.OptionalNumber(entry, "fogDensity", ctx, 0.0),
                    (float)JsonRead.OptionalNumber(entry, "headwindMps", ctx, 0.0),
                    JsonRead.OptionalStringList(entry, "primarySurfaces", ctx).AsReadOnly(),
                    JsonRead.OptionalBool(entry, "hasMuniRails", ctx, false),
                    JsonRead.OptionalStringList(entry, "connectedZones", ctx).AsReadOnly(),
                    landmarks.AsReadOnly()));
            }
        }

        // ------------------------------------------------------------------- jobs

        private static void ParseJobs(JObject root, List<JobDefinition> jobs, List<JobMetadata> meta)
        {
            JArray arr = JsonRead.RequireArray(root, "jobs", JobsFileName + ": <root>");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count; i++)
            {
                string ctx = JobsFileName + ": jobs[" + i + "]";
                JObject entry = JsonRead.AsObject(arr[i], ctx);
                string id = JsonRead.RequireString(entry, "id", ctx);
                ctx = JobsFileName + ": jobs[" + i + "] (" + id + ")";
                CheckUnique(seen, id, ctx);

                string startZone = JsonRead.RequireString(entry, "startZoneId", ctx);
                int cargoSlots = JsonRead.OptionalInt(entry, "requiredCargoSlots", ctx, 0);

                jobs.Add(new JobDefinition
                {
                    SchemaVersion = ReadEntrySchemaVersion(entry, ctx),
                    Id = id,
                    DisplayName = JsonRead.RequireString(entry, "displayName", ctx),
                    ZoneId = startZone,
                    RequiredCargoSlots = cargoSlots,
                    RewardUsd = (decimal)JsonRead.RequireNumber(entry, "basePayoutUsd", ctx),
                    TimeLimitSeconds = (float)JsonRead.OptionalNumber(entry, "timeLimitSeconds", ctx, 0.0),
                });

                var checkpoints = new List<JobCheckpoint>();
                JArray? cpArr = entry["checkpoints"] as JArray;
                if (cpArr != null)
                {
                    for (int c = 0; c < cpArr.Count; c++)
                    {
                        string cCtx = ctx + ".checkpoints[" + c + "]";
                        JObject cp = JsonRead.AsObject(cpArr[c], cCtx);
                        checkpoints.Add(new JobCheckpoint(
                            JsonRead.RequireString(cp, "id", cCtx),
                            JsonRead.OptionalString(cp, "displayName", cCtx) ?? string.Empty,
                            JsonRead.RequireString(cp, "zoneId", cCtx),
                            JsonRead.RequireInt(cp, "order", cCtx),
                            JsonRead.OptionalVector3(cp, "position", cCtx),
                            (float)JsonRead.OptionalNumber(cp, "radiusMeters", cCtx, 0.0),
                            (float)JsonRead.OptionalNumber(cp, "gradePercent", cCtx, 0.0)));
                    }
                }

                meta.Add(new JobMetadata(
                    id,
                    JsonRead.OptionalString(entry, "jobType", ctx) ?? string.Empty,
                    JsonRead.OptionalString(entry, "difficulty", ctx) ?? string.Empty,
                    JsonRead.OptionalString(entry, "description", ctx) ?? string.Empty,
                    startZone,
                    JsonRead.RequireString(entry, "targetZoneId", ctx),
                    cargoSlots,
                    (float)JsonRead.OptionalNumber(entry, "requiredCargoKg", ctx, 0.0),
                    JsonRead.OptionalStringList(entry, "recommendedPartIds", ctx).AsReadOnly(),
                    (decimal)JsonRead.OptionalNumber(entry, "bonusPayoutUsd", ctx, 0.0),
                    JsonRead.OptionalInt(entry, "reputationReward", ctx, 0),
                    checkpoints.AsReadOnly()));
            }
        }
    }
}
