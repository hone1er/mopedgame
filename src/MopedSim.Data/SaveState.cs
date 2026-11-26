#nullable enable

using System.Collections.Generic;
using MopedSim.Core;
using Newtonsoft.Json;

namespace MopedSim.Data
{
    /// <summary>
    /// Root player save document matching data/schemas/save_state.schema.json (schemaVersion 1).
    /// Property names, required-ness, and nesting mirror the schema exactly; unknown JSON members are rejected on load.
    /// </summary>
    public sealed class SaveState
    {
        /// <summary>Save schema version written by this build and targeted by <see cref="SaveMigrator"/>.</summary>
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonProperty("seed", Required = Required.Always)]
        public uint Seed { get; set; }

        [JsonProperty("cashUsd", Required = Required.Always)]
        public decimal CashUsd { get; set; }

        [JsonProperty("reputation", Required = Required.Always)]
        public int Reputation { get; set; }

        [JsonProperty("activeBikeInstanceId", Required = Required.Always)]
        public string ActiveBikeInstanceId { get; set; } = string.Empty;

        [JsonProperty("activeZoneId", Required = Required.Always)]
        public string ActiveZoneId { get; set; } = string.Empty;

        [JsonProperty("toolRollCapacity", Required = Required.Always)]
        public int ToolRollCapacity { get; set; }

        [JsonProperty("bikes", Required = Required.Always)]
        public List<BikeSaveState> Bikes { get; set; } = new List<BikeSaveState>();

        [JsonProperty("inventory", Required = Required.Always)]
        public List<InventoryItemState> Inventory { get; set; } = new List<InventoryItemState>();

        /// <summary>Part ids (Tool / Spare catalog items) packed in the tool roll.</summary>
        [JsonProperty("activeToolRoll", Required = Required.Always)]
        public List<string> ActiveToolRoll { get; set; } = new List<string>();

        [JsonProperty("completedGigs", Required = Required.Always)]
        public List<CompletedGigState> CompletedGigs { get; set; } = new List<CompletedGigState>();

        [JsonProperty("unlockedZones", Required = Required.Always)]
        public List<string> UnlockedZones { get; set; } = new List<string>();

        [JsonProperty("diagnosticHintsEnabled", Required = Required.Always)]
        public bool DiagnosticHintsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Persistent state of one owned moped. Empty <see cref="InstalledParts"/> and <see cref="Fasteners"/> mean
    /// "factory stock": the archetype's catalog default parts with every fastener at target torque.
    /// </summary>
    public sealed class BikeSaveState
    {
        [JsonProperty("instanceId", Required = Required.Always)]
        public string InstanceId { get; set; } = string.Empty;

        [JsonProperty("archetypeId", Required = Required.Always)]
        public string ArchetypeId { get; set; } = string.Empty;

        [JsonProperty("nickname", Required = Required.Always)]
        public string Nickname { get; set; } = string.Empty;

        [JsonProperty("odometerKm", Required = Required.Always)]
        public double OdometerKm { get; set; }

        [JsonProperty("paintHex", Required = Required.Always)]
        public string PaintHex { get; set; } = string.Empty;

        /// <summary>Weathered patina 0..1. Optional in JSON (saves written before it existed load as 0); always written.</summary>
        [JsonProperty("patinaLevel", Required = Required.DisallowNull)]
        public float PatinaLevel { get; set; }

        /// <summary>Optional decal / style tag (max 64 chars). Omitted from JSON when null.</summary>
        [JsonProperty("decalStyle", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? DecalStyle { get; set; }

        [JsonProperty("fuelTank", Required = Required.Always)]
        public FuelTankSaveState FuelTank { get; set; } = new FuelTankSaveState();

        [JsonProperty("installedParts", Required = Required.Always)]
        public List<InstalledPartSaveState> InstalledParts { get; set; } = new List<InstalledPartSaveState>();

        [JsonProperty("fasteners", Required = Required.Always)]
        public List<FastenerSaveState> Fasteners { get; set; } = new List<FastenerSaveState>();

        [JsonProperty("activeFaultIds", Required = Required.Always)]
        public List<string> ActiveFaultIds { get; set; } = new List<string>();

        /// <summary>
        /// Paint booth state of this bike as a Core <see cref="BikeCustomizationState"/>. An invalid stored paint hex falls back to
        /// <see cref="BikeCustomizationState.DefaultPaintHex"/>; patina is clamped and an over-long decal is dropped.
        /// </summary>
        public BikeCustomizationState ToCustomizationState()
        {
            var state = new BikeCustomizationState();
            state.TrySetPaintHex(PaintHex);
            state.SetPatinaLevel(PatinaLevel);
            state.TrySetDecalStyle(DecalStyle);
            return state;
        }

        /// <summary>Copies paint, patina, and decal from <paramref name="customization"/> into this save record.</summary>
        public void ApplyCustomization(BikeCustomizationState customization)
        {
            if (customization == null)
            {
                throw new System.ArgumentNullException(nameof(customization));
            }
            PaintHex = customization.PaintHex;
            PatinaLevel = customization.PatinaLevel;
            DecalStyle = customization.DecalStyle;
        }
    }

    /// <summary>
    /// Fuel tank contents. <see cref="PremixRatio"/> uses the "X:1" convention (50 = 50:1, 0 = straight gas),
    /// not MopedSim.Core FuelSystem's oil fraction (convert with 1 / PremixRatio when PremixRatio &gt; 0).
    /// </summary>
    public sealed class FuelTankSaveState
    {
        [JsonProperty("fuelLiters", Required = Required.Always)]
        public float FuelLiters { get; set; }

        [JsonProperty("premixRatio", Required = Required.Always)]
        public float PremixRatio { get; set; }

        [JsonProperty("petcockMode", Required = Required.Always)]
        public PetcockMode PetcockMode { get; set; } = PetcockMode.On;

        [JsonProperty("rustContamination", Required = Required.Always)]
        public float RustContamination { get; set; }
    }

    /// <summary>
    /// One part mounted in a catalog slot with its wear state.
    /// </summary>
    public sealed class InstalledPartSaveState
    {
        [JsonProperty("slotId", Required = Required.Always)]
        public string SlotId { get; set; } = string.Empty;

        [JsonProperty("partId", Required = Required.Always)]
        public string PartId { get; set; } = string.Empty;

        [JsonProperty("condition", Required = Required.Always)]
        public float Condition { get; set; } = 1f;

        [JsonProperty("wear", Required = Required.Always)]
        public float Wear { get; set; }

        [JsonProperty("carbonClog", Required = Required.Always)]
        public float CarbonClog { get; set; }

        [JsonProperty("foulingLevel", Required = Required.Always)]
        public float FoulingLevel { get; set; }

        [JsonProperty("insulatorColorIndex", Required = Required.Always)]
        public float InsulatorColorIndex { get; set; }

        [JsonProperty("pointsGapMm", Required = Required.Always)]
        public float PointsGapMm { get; set; }

        [JsonProperty("installedArrowTowardExhaust", Required = Required.Always)]
        public bool InstalledArrowTowardExhaust { get; set; } = true;
    }

    /// <summary>
    /// Torque / damage state of one catalog fastener.
    /// </summary>
    public sealed class FastenerSaveState
    {
        [JsonProperty("fastenerId", Required = Required.Always)]
        public string FastenerId { get; set; } = string.Empty;

        [JsonProperty("currentTorqueNm", Required = Required.Always)]
        public float CurrentTorqueNm { get; set; }

        [JsonProperty("isStripped", Required = Required.Always)]
        public bool IsStripped { get; set; }

        [JsonProperty("isSeized", Required = Required.Always)]
        public bool IsSeized { get; set; }

        [JsonProperty("hasPenetratingOil", Required = Required.Always)]
        public bool HasPenetratingOil { get; set; }
    }

    /// <summary>
    /// A stack of loose parts, tools, or spares in the garage inventory.
    /// </summary>
    public sealed class InventoryItemState
    {
        [JsonProperty("instanceId", Required = Required.Always)]
        public string InstanceId { get; set; } = string.Empty;

        [JsonProperty("partId", Required = Required.Always)]
        public string PartId { get; set; } = string.Empty;

        [JsonProperty("quantity", Required = Required.Always)]
        public int Quantity { get; set; } = 1;

        [JsonProperty("condition", Required = Required.Always)]
        public float Condition { get; set; } = 1f;

        [JsonProperty("wear", Required = Required.Always)]
        public float Wear { get; set; }
    }

    /// <summary>
    /// Personal best for a completed job. <see cref="LastCompletedUtc"/> is an opaque caller-supplied timestamp
    /// string (kept verbatim; MopedSim.Data never reads the system clock).
    /// </summary>
    public sealed class CompletedGigState
    {
        [JsonProperty("jobId", Required = Required.Always)]
        public string JobId { get; set; } = string.Empty;

        [JsonProperty("bestTimeSeconds", Required = Required.Always)]
        public float BestTimeSeconds { get; set; }

        [JsonProperty("completions", Required = Required.Always)]
        public int Completions { get; set; } = 1;

        [JsonProperty("lastCompletedUtc", Required = Required.Always)]
        public string LastCompletedUtc { get; set; } = string.Empty;
    }
}
