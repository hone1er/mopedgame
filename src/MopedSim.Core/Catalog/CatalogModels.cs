#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Single RPM/multiplier point on a piecewise-linear exhaust torque curve.
    /// </summary>
    public sealed class TorqueCurvePoint
    {
        public float Rpm { get; set; }
        public float Multiplier { get; set; }

        public TorqueCurvePoint()
        {
        }

        public TorqueCurvePoint(float rpm, float multiplier)
        {
            Rpm = rpm;
            Multiplier = multiplier;
        }
    }

    /// <summary>
    /// Data-driven mechanical, thermal, fuel, ignition, drivetrain, and chassis stats for a PartDefinition.
    /// </summary>
    public sealed class PartStats
    {
        // Cylinder / Piston
        public float DisplacementCc { get; set; }
        public float CompressionRatio { get; set; }
        public float PortFlowEfficiency { get; set; } = 1.0f;
        public float CoolingFinEfficiency { get; set; } = 1.0f;
        public float BoreMm { get; set; }
        public float StrokeMm { get; set; }
        public float PistonRingGapMm { get; set; } = 0.25f;
        public float OptimalRingGapMinMm { get; set; } = 0.15f;
        public float OptimalRingGapMaxMm { get; set; } = 0.45f;

        // Carburetor / Jets / Air Filter
        public float VenturiMm { get; set; } = 12f;
        public int MainJetSize { get; set; }
        public int PilotJetSize { get; set; } = 38;
        public float IdleScrewTurns { get; set; } = 1.5f;
        public int NeedleClipPosition { get; set; } = 3;
        public float AirFilterRestriction { get; set; }
        public float MoistureSensitivity { get; set; }

        // Exhaust
        public float FlowEfficiency { get; set; } = 1.0f;
        public float PeakRpmStart { get; set; } = 3500f;
        public float PeakRpmEnd { get; set; } = 6500f;
        public float NoiseDb { get; set; } = 82f;
        public List<TorqueCurvePoint> TorqueCurve { get; set; } = new List<TorqueCurvePoint>();

        // Ignition & Spark Plug
        public IgnitionType IgnitionType { get; set; } = IgnitionType.Points6V;
        public int HeatRange { get; set; } = 6;
        public float ElectrodeGapMm { get; set; } = 0.60f;
        public float InitialPointsGapMm { get; set; } = 0.35f;
        public float CondenserHeatThresholdC { get; set; } = 95f;
        public float MaxRpmRating { get; set; } = 8500f;

        // Fuel Tank
        public float TankCapacityLiters { get; set; } = 3.2f;
        public float ReserveLiters { get; set; } = 0.6f;
        public float FloatBowlCapacityLiters { get; set; } = 0.04f;

        // Drivetrain
        public DrivetrainType DrivetrainType { get; set; } = DrivetrainType.SingleSpeedCentrifugal;
        public float ClutchEngageRpm { get; set; } = 2400f;
        public float ClutchSlipHeatRate { get; set; } = 1.0f;
        public float RollerWeightGrams { get; set; } = 6.5f;
        public float ContraSpringRate { get; set; } = 1.0f;
        public float GearRatio1 { get; set; } = 2.25f;
        public float GearRatio2 { get; set; } = 1.18f;
        public float ShiftRpmThreshold { get; set; } = 4600f;
        public int FrontSprocketTeeth { get; set; } = 13;
        public int RearSprocketTeeth { get; set; } = 45;

        // Chassis / Accessories
        public float AeroDragMultiplier { get; set; } = 1.0f;
        public float TuckDragBonusMultiplier { get; set; } = 1.0f;
        public int CargoSlots { get; set; }
        public float HeadlightMaxOutput { get; set; } = 0.75f;
        public float WheelRadiusM { get; set; } = 0.27f;

        // Tool Roll & Spares
        public bool IsTool { get; set; }
        public bool IsSpare { get; set; }
        public string ToolCapabilityTag { get; set; } = string.Empty;
    }

    /// <summary>
    /// Immutable catalog definition for an engine, chassis, drivetrain, or tool roll item.
    /// </summary>
    public sealed class PartDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public PartCategory Category { get; set; }
        public string[] CompatibleInterfaces { get; set; } = Array.Empty<string>();
        public string MeshAssetPath { get; set; } = string.Empty;
        public string[] RequiredSockets { get; set; } = Array.Empty<string>();
        public float MassKg { get; set; }
        public decimal PriceUsd { get; set; }
        public PartStats Stats { get; set; } = new PartStats();

        public PartDefinition()
        {
        }

        public PartDefinition(
            string id,
            string displayName,
            PartCategory category,
            string[] compatibleInterfaces,
            float massKg,
            decimal priceUsd,
            PartStats? stats = null,
            string meshAssetPath = "",
            string[]? requiredSockets = null)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Category = category;
            CompatibleInterfaces = compatibleInterfaces ?? Array.Empty<string>();
            MassKg = massKg;
            PriceUsd = priceUsd;
            Stats = stats ?? new PartStats();
            MeshAssetPath = meshAssetPath ?? string.Empty;
            RequiredSockets = requiredSockets ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Mutable runtime instance of a catalog part tracking wear, jet overrides, orientation, and contamination.
    /// </summary>
    public sealed class PartInstance
    {
        public string InstanceId { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public PartDefinition Definition { get; set; } = new PartDefinition();
        public float Wear { get; set; }
        public float Condition { get; set; } = 1.0f;
        public float CarbonAccumulation { get; set; }
        public float RustContamination { get; set; }
        public bool ArrowTowardExhaust { get; set; } = true;
        public float RingGapMm { get; set; } = 0.25f;
        public int MainJetOverride { get; set; }
        public int PilotJetOverride { get; set; }
        public int NeedleClipOverride { get; set; }
        public float IdleScrewTurnsOverride { get; set; }

        public PartInstance()
        {
        }

        public PartInstance(PartDefinition definition, string instanceId = "")
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            PartId = definition.Id;
            InstanceId = string.IsNullOrEmpty(instanceId) ? definition.Id : instanceId;
            RingGapMm = definition.Stats.PistonRingGapMm;
        }
    }

    /// <summary>
    /// Declarative mount slot definition in a bike archetype's assembly DAG.
    /// </summary>
    public sealed class MountSlotSpec
    {
        public string SlotId { get; set; } = string.Empty;
        public string? ParentSlotId { get; set; }
        public PartCategory AcceptedCategory { get; set; }
        public string[] RequiredInterfaces { get; set; } = Array.Empty<string>();
        public string[] BlockedBySlotIds { get; set; } = Array.Empty<string>();
        public string[] BlockedByFastenerIds { get; set; } = Array.Empty<string>();
        public string[] RequiresEmptySlotIds { get; set; } = Array.Empty<string>();

        public MountSlotSpec()
        {
        }

        public MountSlotSpec(
            string slotId,
            string? parentSlotId,
            PartCategory acceptedCategory,
            string[]? requiredInterfaces = null,
            string[]? blockedBySlotIds = null,
            string[]? blockedByFastenerIds = null,
            string[]? requiresEmptySlotIds = null)
        {
            SlotId = slotId ?? string.Empty;
            ParentSlotId = parentSlotId;
            AcceptedCategory = acceptedCategory;
            RequiredInterfaces = requiredInterfaces ?? Array.Empty<string>();
            BlockedBySlotIds = blockedBySlotIds ?? Array.Empty<string>();
            BlockedByFastenerIds = blockedByFastenerIds ?? Array.Empty<string>();
            RequiresEmptySlotIds = requiresEmptySlotIds ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Declarative fastener torque and loosening specification.
    /// </summary>
    public sealed class FastenerSpec
    {
        public string FastenerId { get; set; } = string.Empty;
        public string OwnerSlotId { get; set; } = string.Empty;
        public float TargetTorqueNm { get; set; } = 10f;
        public float ToleranceNm { get; set; } = 2f;
        public float StripThresholdNm { get; set; } = 16f;
        public float VibrationLoosenRate { get; set; } = 0.05f;
        public bool DefaultSeized { get; set; }

        public FastenerSpec()
        {
        }

        public FastenerSpec(
            string fastenerId,
            string ownerSlotId,
            float targetTorqueNm = 10f,
            float toleranceNm = 2f,
            float stripThresholdNm = 16f,
            float vibrationLoosenRate = 0.05f,
            bool defaultSeized = false)
        {
            FastenerId = fastenerId ?? string.Empty;
            OwnerSlotId = ownerSlotId ?? string.Empty;
            TargetTorqueNm = targetTorqueNm;
            ToleranceNm = toleranceNm;
            StripThresholdNm = stripThresholdNm;
            VibrationLoosenRate = vibrationLoosenRate;
            DefaultSeized = defaultSeized;
        }
    }

    /// <summary>
    /// Data-driven chassis archetype blueprint (e.g., Maxer E50, Bramble PA50, Targa A35).
    /// </summary>
    public sealed class BikeArchetypeSpec
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DrivetrainType DrivetrainType { get; set; }
        public float BaseDryMassKg { get; set; } = 52f;
        public float WheelRadiusM { get; set; } = 0.27f;
        public float WheelbaseM { get; set; } = 1.16f;
        public float DragAreaCdA { get; set; } = 0.56f;
        public float RollingResistanceCoeff { get; set; } = 0.015f;

        /// <summary>Fuel loaded into a freshly built bike (catalog defaultFuelMix.fuelLiters; clamped to tank capacity).</summary>
        public float DefaultFuelLiters { get; set; } = 3.0f;

        /// <summary>Premix oil fraction of a freshly built bike's fuel (0.025 = 40:1). Catalogs author X:1; see ThermalModel.PremixFromRatio.</summary>
        public float DefaultPremixOilFraction { get; set; } = 0.025f;

        public PetcockMode DefaultPetcockMode { get; set; } = PetcockMode.On;

        /// <summary>Rust contamination of the stock fuel tank (0..1), applied to the tank PartInstance.</summary>
        public float DefaultTankRustContamination { get; set; }

        public List<MountSlotSpec> MountSlots { get; set; } = new List<MountSlotSpec>();
        public List<FastenerSpec> Fasteners { get; set; } = new List<FastenerSpec>();
        public Dictionary<string, string> DefaultPartIds { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Fault catalog metadata entry aligned with data/catalogs/faults.json.
    /// </summary>
    public sealed class FaultDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public FaultType FaultType { get; set; }
        public string RootCause { get; set; } = string.Empty;
        public string Symptoms { get; set; } = string.Empty;
        public bool IsFieldRepairable { get; set; } = true;
        public List<string> RequiredTools { get; set; } = new List<string>();
        public List<string> RequiredSpares { get; set; } = new List<string>();
        public string GarageFixDescription { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bay Area zone environmental parameters (e.g., Karl the Fog air density, moisture, steepness).
    /// </summary>
    public sealed class ZoneEnvironmentSpec
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public float AmbientTemperatureC { get; set; } = 18f;
        public float AmbientAirDensity { get; set; } = RideInputState.StandardAirDensity;
        public float BaseSurfaceGrip { get; set; } = 1.0f;
        public float MaxGradePercent { get; set; } = 8f;
        public float DistanceFromGarageMiles { get; set; } = 1.5f;
    }

    /// <summary>
    /// Courier gig, hill climb, or rally job catalog entry.
    /// </summary>
    public sealed class JobDefinition
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public int RequiredCargoSlots { get; set; }
        public decimal RewardUsd { get; set; }
        public float TimeLimitSeconds { get; set; }
    }
}
