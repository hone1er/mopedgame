using System;
using System.Collections.Generic;
using MopedSim.Core;

namespace MopedSim.Data
{
    /// <summary>
    /// Metric position (1 unit = 1 m) of a landmark or checkpoint as authored in zones.json / jobs.json.
    /// </summary>
    public readonly struct CatalogVector3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public CatalogVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Presentation / pipeline metadata for a part that has no home in <see cref="PartDefinition"/>.
    /// </summary>
    public sealed class PartMetadata
    {
        public string PartId { get; }

        /// <summary>Exact catalog category string from parts.json (e.g. "Brake", "Sprocket"), before mapping to <see cref="PartCategory"/>.</summary>
        public string CatalogCategory { get; }

        public string MeshAssetPath { get; }
        public bool Has3dMesh { get; }
        public string AssetClass { get; }
        public IReadOnlyList<string> RequiredSockets { get; }
        public int ToolRollSlots { get; }
        public string Description { get; }

        /// <summary>Every numeric stat exactly as authored (including ones Core does not consume, e.g. lumenOutput).</summary>
        public IReadOnlyDictionary<string, double> NumericStats { get; }

        /// <summary>Every string stat exactly as authored (e.g. portTimingBias, stockPaintHex).</summary>
        public IReadOnlyDictionary<string, string> TextStats { get; }

        /// <summary>Every boolean stat exactly as authored (e.g. isReedValve, hasChoke).</summary>
        public IReadOnlyDictionary<string, bool> FlagStats { get; }

        internal PartMetadata(
            string partId,
            string catalogCategory,
            string meshAssetPath,
            bool has3dMesh,
            string assetClass,
            IReadOnlyList<string> requiredSockets,
            int toolRollSlots,
            string description,
            IReadOnlyDictionary<string, double> numericStats,
            IReadOnlyDictionary<string, string> textStats,
            IReadOnlyDictionary<string, bool> flagStats)
        {
            PartId = partId;
            CatalogCategory = catalogCategory;
            MeshAssetPath = meshAssetPath;
            Has3dMesh = has3dMesh;
            AssetClass = assetClass;
            RequiredSockets = requiredSockets;
            ToolRollSlots = toolRollSlots;
            Description = description;
            NumericStats = numericStats;
            TextStats = textStats;
            FlagStats = flagStats;
        }
    }

    /// <summary>
    /// Default fuel load for a new bike of an archetype (archetypes.json defaultFuelMix).
    /// PremixRatio uses the catalog/save convention "X:1" (e.g. 50 = 50:1), not Core's oil fraction.
    /// </summary>
    public sealed class FuelMixSpec
    {
        public float FuelLiters { get; }
        public float PremixRatio { get; }
        public PetcockMode PetcockMode { get; }
        public float RustContamination { get; }

        public FuelMixSpec(float fuelLiters, float premixRatio, PetcockMode petcockMode, float rustContamination)
        {
            FuelLiters = fuelLiters;
            PremixRatio = premixRatio;
            PetcockMode = petcockMode;
            RustContamination = rustContamination;
        }
    }

    /// <summary>
    /// Catalog-only mount slot details (display name, glTF attach socket, raw categories, garage flags).
    /// </summary>
    public sealed class SlotMetadata
    {
        public string SlotId { get; }
        public string DisplayName { get; }
        public string? AttachSocket { get; }
        public IReadOnlyList<string> AcceptedCatalogCategories { get; }
        public IReadOnlyList<string> FastenerIds { get; }
        public string? DefaultPartId { get; }
        public bool RequiresOrientationCheck { get; }
        public bool RequiresCarbBowlOpen { get; }

        internal SlotMetadata(
            string slotId,
            string displayName,
            string? attachSocket,
            IReadOnlyList<string> acceptedCatalogCategories,
            IReadOnlyList<string> fastenerIds,
            string? defaultPartId,
            bool requiresOrientationCheck,
            bool requiresCarbBowlOpen)
        {
            SlotId = slotId;
            DisplayName = displayName;
            AttachSocket = attachSocket;
            AcceptedCatalogCategories = acceptedCatalogCategories;
            FastenerIds = fastenerIds;
            DefaultPartId = defaultPartId;
            RequiresOrientationCheck = requiresOrientationCheck;
            RequiresCarbBowlOpen = requiresCarbBowlOpen;
        }
    }

    /// <summary>
    /// Catalog-only fastener details (display name, seize capability, required tool part id).
    /// </summary>
    public sealed class FastenerMetadata
    {
        public string FastenerId { get; }
        public string DisplayName { get; }
        public bool CanSeize { get; }
        public string? RequiredToolId { get; }

        internal FastenerMetadata(string fastenerId, string displayName, bool canSeize, string? requiredToolId)
        {
            FastenerId = fastenerId;
            DisplayName = displayName;
            CanSeize = canSeize;
            RequiredToolId = requiredToolId;
        }
    }

    /// <summary>
    /// Catalog-only archetype details that do not belong in <see cref="BikeArchetypeSpec"/>.
    /// </summary>
    public sealed class ArchetypeMetadata
    {
        private readonly Dictionary<string, SlotMetadata> _slotsById;
        private readonly Dictionary<string, FastenerMetadata> _fastenersById;

        public string ArchetypeId { get; }
        public string InspiredBy { get; }
        public string Description { get; }
        public string DrivetrainArchitecture { get; }

        /// <summary>Whole stock bike dry mass exactly as authored in archetypes.json (baseDryMassKg).</summary>
        public float CatalogDryMassKg { get; }

        /// <summary>Sum of the stock (default) parts' massKg; BikeArchetypeSpec.BaseDryMassKg holds the residual.</summary>
        public float StockPartsMassKg { get; }

        public float SaddleHeightMeters { get; }
        public float FuelCapacityLiters { get; }
        public float ReserveThresholdLiters { get; }
        public int DefaultToolRollSlots { get; }
        public decimal BasePriceUsd { get; }
        public FuelMixSpec DefaultFuelMix { get; }
        public IReadOnlyList<SlotMetadata> Slots { get; }
        public IReadOnlyList<FastenerMetadata> Fasteners { get; }

        internal ArchetypeMetadata(
            string archetypeId,
            string inspiredBy,
            string description,
            string drivetrainArchitecture,
            float catalogDryMassKg,
            float stockPartsMassKg,
            float saddleHeightMeters,
            float fuelCapacityLiters,
            float reserveThresholdLiters,
            int defaultToolRollSlots,
            decimal basePriceUsd,
            FuelMixSpec defaultFuelMix,
            IReadOnlyList<SlotMetadata> slots,
            IReadOnlyList<FastenerMetadata> fasteners)
        {
            ArchetypeId = archetypeId;
            InspiredBy = inspiredBy;
            Description = description;
            DrivetrainArchitecture = drivetrainArchitecture;
            CatalogDryMassKg = catalogDryMassKg;
            StockPartsMassKg = stockPartsMassKg;
            SaddleHeightMeters = saddleHeightMeters;
            FuelCapacityLiters = fuelCapacityLiters;
            ReserveThresholdLiters = reserveThresholdLiters;
            DefaultToolRollSlots = defaultToolRollSlots;
            BasePriceUsd = basePriceUsd;
            DefaultFuelMix = defaultFuelMix;
            Slots = slots;
            Fasteners = fasteners;

            _slotsById = new Dictionary<string, SlotMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                _slotsById[slots[i].SlotId] = slots[i];
            }
            _fastenersById = new Dictionary<string, FastenerMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < fasteners.Count; i++)
            {
                _fastenersById[fasteners[i].FastenerId] = fasteners[i];
            }
        }

        public SlotMetadata? GetSlot(string slotId)
        {
            return slotId != null && _slotsById.TryGetValue(slotId, out var slot) ? slot : null;
        }

        public FastenerMetadata? GetFastener(string fastenerId)
        {
            return fastenerId != null && _fastenersById.TryGetValue(fastenerId, out var fastener) ? fastener : null;
        }
    }

    /// <summary>
    /// Named point of interest inside a zone (garage, shop, plug chop strip, etc.).
    /// </summary>
    public sealed class ZoneLandmark
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string LandmarkType { get; }
        public CatalogVector3 Position { get; }

        internal ZoneLandmark(string id, string displayName, string landmarkType, CatalogVector3 position)
        {
            Id = id;
            DisplayName = displayName;
            LandmarkType = landmarkType;
            Position = position;
        }
    }

    /// <summary>
    /// Catalog-only zone details that do not belong in <see cref="ZoneEnvironmentSpec"/>.
    /// </summary>
    public sealed class ZoneMetadata
    {
        public string ZoneId { get; }
        public string SceneName { get; }
        public string Description { get; }
        public decimal TowFeeUsd { get; }
        public float FogDensity { get; }
        public float HeadwindMps { get; }
        public IReadOnlyList<string> PrimarySurfaces { get; }
        public bool HasMuniRails { get; }
        public IReadOnlyList<string> ConnectedZones { get; }
        public IReadOnlyList<ZoneLandmark> Landmarks { get; }

        internal ZoneMetadata(
            string zoneId,
            string sceneName,
            string description,
            decimal towFeeUsd,
            float fogDensity,
            float headwindMps,
            IReadOnlyList<string> primarySurfaces,
            bool hasMuniRails,
            IReadOnlyList<string> connectedZones,
            IReadOnlyList<ZoneLandmark> landmarks)
        {
            ZoneId = zoneId;
            SceneName = sceneName;
            Description = description;
            TowFeeUsd = towFeeUsd;
            FogDensity = fogDensity;
            HeadwindMps = headwindMps;
            PrimarySurfaces = primarySurfaces;
            HasMuniRails = hasMuniRails;
            ConnectedZones = connectedZones;
            Landmarks = landmarks;
        }
    }

    /// <summary>
    /// Ordered route checkpoint of a courier gig, hill climb, or rally.
    /// </summary>
    public sealed class JobCheckpoint
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string ZoneId { get; }
        public int Order { get; }
        public CatalogVector3 Position { get; }
        public float RadiusMeters { get; }
        public float GradePercent { get; }

        internal JobCheckpoint(string id, string displayName, string zoneId, int order, CatalogVector3 position, float radiusMeters, float gradePercent)
        {
            Id = id;
            DisplayName = displayName;
            ZoneId = zoneId;
            Order = order;
            Position = position;
            RadiusMeters = radiusMeters;
            GradePercent = gradePercent;
        }
    }

    /// <summary>
    /// Catalog-only job details that do not belong in <see cref="JobDefinition"/>.
    /// </summary>
    public sealed class JobMetadata
    {
        public string JobId { get; }
        public string JobType { get; }
        public string Difficulty { get; }
        public string Description { get; }
        public string StartZoneId { get; }
        public string TargetZoneId { get; }
        public int RequiredCargoSlots { get; }
        public float RequiredCargoKg { get; }
        public IReadOnlyList<string> RecommendedPartIds { get; }
        public decimal BonusPayoutUsd { get; }
        public int ReputationReward { get; }
        public IReadOnlyList<JobCheckpoint> Checkpoints { get; }

        internal JobMetadata(
            string jobId,
            string jobType,
            string difficulty,
            string description,
            string startZoneId,
            string targetZoneId,
            int requiredCargoSlots,
            float requiredCargoKg,
            IReadOnlyList<string> recommendedPartIds,
            decimal bonusPayoutUsd,
            int reputationReward,
            IReadOnlyList<JobCheckpoint> checkpoints)
        {
            JobId = jobId;
            JobType = jobType;
            Difficulty = difficulty;
            Description = description;
            StartZoneId = startZoneId;
            TargetZoneId = targetZoneId;
            RequiredCargoSlots = requiredCargoSlots;
            RequiredCargoKg = requiredCargoKg;
            RecommendedPartIds = recommendedPartIds;
            BonusPayoutUsd = bonusPayoutUsd;
            ReputationReward = reputationReward;
            Checkpoints = checkpoints;
        }
    }

    /// <summary>
    /// One roadside repair recipe for a fault (tools needed, spares consumed, duration).
    /// </summary>
    public sealed class FaultRepairOption
    {
        public string OptionId { get; }
        public string Description { get; }
        public IReadOnlyList<string> RequiredTools { get; }
        public IReadOnlyList<string> ConsumedSpares { get; }
        public float DurationSeconds { get; }

        internal FaultRepairOption(string optionId, string description, IReadOnlyList<string> requiredTools, IReadOnlyList<string> consumedSpares, float durationSeconds)
        {
            OptionId = optionId;
            Description = description;
            RequiredTools = requiredTools;
            ConsumedSpares = consumedSpares;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>
    /// Catalog-only fault details that do not belong in <see cref="FaultDefinition"/>.
    /// </summary>
    public sealed class FaultMetadata
    {
        public string FaultId { get; }
        public string Severity { get; }
        public string DiagnosticHint { get; }
        public IReadOnlyList<string> Symptoms { get; }
        public IReadOnlyList<FaultRepairOption> RepairOptions { get; }
        public IReadOnlyDictionary<string, double> NumericTriggerRules { get; }
        public IReadOnlyDictionary<string, bool> FlagTriggerRules { get; }

        internal FaultMetadata(
            string faultId,
            string severity,
            string diagnosticHint,
            IReadOnlyList<string> symptoms,
            IReadOnlyList<FaultRepairOption> repairOptions,
            IReadOnlyDictionary<string, double> numericTriggerRules,
            IReadOnlyDictionary<string, bool> flagTriggerRules)
        {
            FaultId = faultId;
            Severity = severity;
            DiagnosticHint = diagnosticHint;
            Symptoms = symptoms;
            RepairOptions = repairOptions;
            NumericTriggerRules = numericTriggerRules;
            FlagTriggerRules = flagTriggerRules;
        }
    }
}
