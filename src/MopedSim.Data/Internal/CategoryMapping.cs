using System;
using System.Collections.Generic;
using MopedSim.Core;
using Newtonsoft.Json.Linq;

namespace MopedSim.Data
{
    /// <summary>
    /// Maps catalog vocabulary (part.schema.json categories, drivetrain strings, petcock modes, fault ids) onto MopedSim.Core enums.
    /// Unknown strings always throw; nothing silently falls back to a default enum value.
    /// </summary>
    internal static class CategoryMapping
    {
        /// <summary>
        /// Catalog categories with no dedicated <see cref="PartCategory"/> value. They share <see cref="PartCategory.Consumable"/>
        /// and are kept apart at install time by interface matching; <see cref="CatalogValidator"/> proves no slot can confuse them.
        /// </summary>
        internal static readonly string[] CategoriesWithoutCoreEnum =
        {
            "FuelLine", "VariatorRoller", "Fender", "Brake", "Cable", "PremixOil", "GarageStation",
        };

        private static readonly HashSet<string> KnownCatalogCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Frame", "Fork", "Crankcase", "Cylinder", "CylinderHead", "Piston", "SparkPlug", "Intake", "Carburetor",
            "MainJet", "PilotJet", "AirFilter", "FuelTank", "Petcock", "FuelLine", "FuelFilter", "Exhaust", "Ignition",
            "Drivetrain", "VariatorRoller", "DriveBelt", "Sprocket", "DriveChain", "Wheel", "RearShock", "Handlebar",
            "Seat", "Headlight", "CargoRack", "Fender", "Brake", "Cable", "Gasket", "PremixOil", "Tool", "Spare",
            "GarageStation",
        };

        public static bool IsKnownCatalogCategory(string category)
        {
            return category != null && KnownCatalogCategories.Contains(category);
        }

        public static PartCategory MapPartCategory(string rawCategory, JObject stats, string ctx)
        {
            switch (rawCategory)
            {
                case "Drivetrain":
                {
                    string? type = JsonRead.OptionalString(stats, "drivetrainType", JsonRead.Ctx(ctx, "stats"));
                    if (type == null)
                    {
                        throw JsonRead.Fail(ctx, "Drivetrain part requires stats.drivetrainType.");
                    }
                    return DrivetrainCategory(MapPartDrivetrain(type, JsonRead.Ctx(ctx, "stats.drivetrainType")));
                }
                case "Sprocket":
                {
                    string? position = JsonRead.OptionalString(stats, "position", JsonRead.Ctx(ctx, "stats"));
                    switch (position)
                    {
                        case "Front":
                            return PartCategory.SprocketFront;
                        case "Rear":
                            return PartCategory.SprocketRear;
                        default:
                            throw JsonRead.Fail(ctx, "Sprocket part requires stats.position 'Front' or 'Rear', got '" + (position ?? "<missing>") + "'.");
                    }
                }
                default:
                {
                    if (TryMapSimpleCategory(rawCategory, out var category))
                    {
                        return category;
                    }
                    throw JsonRead.Fail(ctx, "unknown part category '" + rawCategory + "'.");
                }
            }
        }

        /// <summary>
        /// Picks the single Core category a slot accepts: the stock part's category, else the first interface-compatible
        /// catalog part's category, else a name-based mapping of the slot's first accepted catalog category.
        /// </summary>
        public static PartCategory ResolveSlotCategory(
            string slotId,
            List<string> acceptedCategories,
            List<string> acceptedInterfaces,
            string? defaultPartId,
            DrivetrainType archetypeDrivetrain,
            List<PartDefinition> parts,
            List<PartMetadata> partMeta,
            Dictionary<string, int> partIndex)
        {
            if (defaultPartId != null && partIndex.TryGetValue(defaultPartId, out var defaultIdx))
            {
                return parts[defaultIdx].Category;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (acceptedCategories.Contains(partMeta[i].CatalogCategory)
                    && InterfacesOverlap(acceptedInterfaces, parts[i].CompatibleInterfaces))
                {
                    return parts[i].Category;
                }
            }

            string first = acceptedCategories[0];
            if (first == "Drivetrain")
            {
                return DrivetrainCategory(archetypeDrivetrain);
            }
            if (first == "Sprocket")
            {
                return slotId.IndexOf("REAR", StringComparison.Ordinal) >= 0 ? PartCategory.SprocketRear : PartCategory.SprocketFront;
            }
            if (TryMapSimpleCategory(first, out var category))
            {
                return category;
            }
            throw new CatalogLoadException(CatalogLoader.ArchetypesFileName, "slot " + slotId + ": unknown catalog category '" + first + "'.");
        }

        public static bool InterfacesOverlap(IReadOnlyList<string> required, IReadOnlyList<string> provided)
        {
            if (required.Count == 0)
            {
                return true;
            }
            for (int i = 0; i < required.Count; i++)
            {
                for (int j = 0; j < provided.Count; j++)
                {
                    // Matches BikeAssemblyGraph.HasMatchingInterface (OrdinalIgnoreCase).
                    if (string.Equals(required[i], provided[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static DrivetrainType MapArchetypeDrivetrain(string value, string ctx)
        {
            switch (value)
            {
                case "SingleSpeedCentrifugal":
                    return DrivetrainType.SingleSpeedCentrifugal;
                case "VariatedBelt":
                    return DrivetrainType.VariatedBelt;
                case "TwoSpeedAuto":
                    return DrivetrainType.TwoSpeedAuto;
                default:
                    throw JsonRead.Fail(ctx, "unknown drivetrainArchitecture '" + value + "'.");
            }
        }

        public static DrivetrainType MapPartDrivetrain(string value, string ctx)
        {
            switch (value)
            {
                case "CentrifugalClutch":
                    return DrivetrainType.SingleSpeedCentrifugal;
                case "Variator":
                    return DrivetrainType.VariatedBelt;
                case "TwoSpeedAuto":
                    return DrivetrainType.TwoSpeedAuto;
                default:
                    throw JsonRead.Fail(ctx, "unknown part drivetrainType '" + value + "'.");
            }
        }

        public static PetcockMode MapPetcockMode(string value, string ctx)
        {
            switch (value)
            {
                case "Off":
                    return PetcockMode.Off;
                case "On":
                    return PetcockMode.On;
                case "Reserve":
                    return PetcockMode.Reserve;
                default:
                    throw JsonRead.Fail(ctx, "unknown petcockMode '" + value + "' (expected Off, On, Reserve).");
            }
        }

        public static FaultType MapFaultType(string faultId, string ctx)
        {
            switch (faultId)
            {
                case FaultIds.FouledPlug:
                    return FaultType.FouledPlug;
                case FaultIds.CloggedJet:
                    return FaultType.CloggedJet;
                case FaultIds.CondenserHeatSoak:
                    return FaultType.CondenserHeatSoak;
                case FaultIds.PointsDrift:
                    return FaultType.PointsDrift;
                case FaultIds.AirLeak:
                    return FaultType.AirLeak;
                case FaultIds.ThrownChain:
                    return FaultType.ThrownChain;
                case FaultIds.SnappedBelt:
                    return FaultType.SnappedBelt;
                case FaultIds.ExhaustLoose:
                    return FaultType.ExhaustLoose;
                case FaultIds.SoftSeize:
                    return FaultType.SoftSeize;
                case FaultIds.HardSeize:
                    return FaultType.HardSeize;
                default:
                    throw JsonRead.Fail(ctx, "fault id '" + faultId + "' has no MopedSim.Core FaultType (see Core FaultIds).");
            }
        }

        private static PartCategory DrivetrainCategory(DrivetrainType type)
        {
            switch (type)
            {
                case DrivetrainType.VariatedBelt:
                    return PartCategory.Variator;
                case DrivetrainType.TwoSpeedAuto:
                    return PartCategory.Gearbox;
                default:
                    return PartCategory.Clutch;
            }
        }

        private static bool TryMapSimpleCategory(string raw, out PartCategory category)
        {
            switch (raw)
            {
                case "Frame": category = PartCategory.Frame; return true;
                case "Fork": category = PartCategory.Fork; return true;
                case "Crankcase": category = PartCategory.EngineCase; return true;
                case "Cylinder": category = PartCategory.Cylinder; return true;
                case "CylinderHead": category = PartCategory.CylinderHead; return true;
                case "Piston": category = PartCategory.Piston; return true;
                case "SparkPlug": category = PartCategory.SparkPlug; return true;
                case "Intake": category = PartCategory.IntakeManifold; return true;
                case "Carburetor": category = PartCategory.Carburetor; return true;
                case "MainJet": category = PartCategory.MainJet; return true;
                case "PilotJet": category = PartCategory.PilotJet; return true;
                case "AirFilter": category = PartCategory.AirFilter; return true;
                case "FuelTank": category = PartCategory.FuelTank; return true;
                case "Petcock": category = PartCategory.Petcock; return true;
                case "FuelFilter": category = PartCategory.InlineFilter; return true;
                case "Exhaust": category = PartCategory.Exhaust; return true;
                case "Ignition": category = PartCategory.Ignition; return true;
                case "DriveBelt": category = PartCategory.DriveBelt; return true;
                case "DriveChain": category = PartCategory.Chain; return true;
                case "Wheel": category = PartCategory.Wheel; return true;
                case "RearShock": category = PartCategory.RearShocks; return true;
                case "Handlebar": category = PartCategory.Handlebars; return true;
                case "Seat": category = PartCategory.Seat; return true;
                case "Headlight": category = PartCategory.Headlight; return true;
                case "CargoRack": category = PartCategory.CargoRack; return true;
                case "Gasket": category = PartCategory.Gasket; return true;
                case "Tool": category = PartCategory.ToolRollItem; return true;
                case "Spare": category = PartCategory.ToolRollItem; return true;
                case "FuelLine":
                case "VariatorRoller":
                case "Fender":
                case "Brake":
                case "Cable":
                case "PremixOil":
                case "GarageStation":
                    category = PartCategory.Consumable;
                    return true;
                default:
                    category = PartCategory.Consumable;
                    return false;
            }
        }
    }

    /// <summary>
    /// Maps parts.json stat keys onto <see cref="PartStats"/>. Only keys with a matching Core field are mapped;
    /// everything else stays available through <see cref="PartMetadata"/> numeric/text/flag stats.
    /// </summary>
    internal static class StatsMapping
    {
        public static PartStats MapStats(string rawCategory, JObject stats, string ctx)
        {
            var s = new PartStats();

            // Top end
            SetFloat(stats, "displacementCc", ctx, v => s.DisplacementCc = v);
            SetFloat(stats, "compressionRatio", ctx, v => s.CompressionRatio = v);
            SetFloat(stats, "boreMm", ctx, v => s.BoreMm = v);
            SetFloat(stats, "strokeMm", ctx, v => s.StrokeMm = v);
            SetFloat(stats, "coolingAreaFactor", ctx, v => s.CoolingFinEfficiency = v);
            SetFloat(stats, "finCoolingEfficiency", ctx, v => s.CoolingFinEfficiency = v);

            // Carb / jets / filter
            SetFloat(stats, "venturiMm", ctx, v => s.VenturiMm = v);
            SetInt(stats, "stockMainJet", ctx, v => s.MainJetSize = v);
            SetInt(stats, "stockPilotJet", ctx, v => s.PilotJetSize = v);
            if (rawCategory == "MainJet")
            {
                SetInt(stats, "jetSize", ctx, v => s.MainJetSize = v);
            }
            else if (rawCategory == "PilotJet")
            {
                SetInt(stats, "jetSize", ctx, v => s.PilotJetSize = v);
            }
            SetFloat(stats, "airFilterRestriction", ctx, v => s.AirFilterRestriction = v);
            SetFloat(stats, "moistureVulnerability", ctx, v => s.MoistureSensitivity = v);

            // Exhaust
            SetFloat(stats, "flowEfficiency", ctx, v => s.FlowEfficiency = v);
            SetFloat(stats, "peakRpmStart", ctx, v => s.PeakRpmStart = v);
            SetFloat(stats, "peakRpmEnd", ctx, v => s.PeakRpmEnd = v);
            SetFloat(stats, "noiseDb", ctx, v => s.NoiseDb = v);
            JToken? curve = stats["torqueCurve"];
            if (curve != null && curve.Type != JTokenType.Null)
            {
                if (!(curve is JArray curveArr))
                {
                    throw JsonRead.Fail(ctx, "'torqueCurve' must be an array.");
                }
                for (int i = 0; i < curveArr.Count; i++)
                {
                    string pCtx = JsonRead.Ctx(ctx, "torqueCurve[" + i + "]");
                    JObject point = JsonRead.AsObject(curveArr[i], pCtx);
                    s.TorqueCurve.Add(new TorqueCurvePoint(
                        (float)JsonRead.RequireNumber(point, "rpm", pCtx),
                        (float)JsonRead.RequireNumber(point, "multiplier", pCtx)));
                }
            }

            // Ignition & plug
            JToken? isCdi = stats["isCdi"];
            if (isCdi != null && isCdi.Type != JTokenType.Null)
            {
                if (isCdi.Type != JTokenType.Boolean)
                {
                    throw JsonRead.Fail(ctx, "'isCdi' must be a boolean.");
                }
                s.IgnitionType = isCdi.Value<bool>() ? IgnitionType.Cdi12V : IgnitionType.Points6V;
            }
            SetInt(stats, "heatRange", ctx, v => s.HeatRange = v);
            SetFloat(stats, "recommendedGapMm", ctx, v => s.ElectrodeGapMm = v);
            SetFloat(stats, "pointsGapTargetMm", ctx, v => s.InitialPointsGapMm = v);
            SetFloat(stats, "condenserHeatSoakThresholdC", ctx, v => s.CondenserHeatThresholdC = v);
            SetFloat(stats, "maxCleanRpm", ctx, v => s.MaxRpmRating = v);

            // Fuel tank
            SetFloat(stats, "capacityLiters", ctx, v => s.TankCapacityLiters = v);
            SetFloat(stats, "reserveThresholdLiters", ctx, v => s.ReserveLiters = v);

            // Drivetrain
            string? drivetrainType = JsonRead.OptionalString(stats, "drivetrainType", ctx);
            if (drivetrainType != null)
            {
                s.DrivetrainType = CategoryMapping.MapPartDrivetrain(drivetrainType, JsonRead.Ctx(ctx, "drivetrainType"));
            }
            SetFloat(stats, "clutchEngageRpm", ctx, v => s.ClutchEngageRpm = v);
            SetFloat(stats, "clutchSlipHeatFactor", ctx, v => s.ClutchSlipHeatRate = v);
            SetFloat(stats, "rollerWeightGrams", ctx, v => s.RollerWeightGrams = v);
            SetFloat(stats, "contraSpringRate", ctx, v => s.ContraSpringRate = v);
            SetFloat(stats, "shiftRpm", ctx, v => s.ShiftRpmThreshold = v);
            JToken? ratios = stats["gearRatios"];
            if (ratios != null && ratios.Type != JTokenType.Null)
            {
                if (!(ratios is JArray ratioArr))
                {
                    throw JsonRead.Fail(ctx, "'gearRatios' must be an array of numbers.");
                }
                if (ratioArr.Count > 2)
                {
                    throw JsonRead.Fail(ctx, "'gearRatios' supports at most 2 gears in MopedSim.Core.");
                }
                if (ratioArr.Count > 0)
                {
                    s.GearRatio1 = (float)JsonRead.AsNumber(ratioArr[0], JsonRead.Ctx(ctx, "gearRatios[0]"));
                }
                if (ratioArr.Count > 1)
                {
                    s.GearRatio2 = (float)JsonRead.AsNumber(ratioArr[1], JsonRead.Ctx(ctx, "gearRatios[1]"));
                }
            }
            if (rawCategory == "Sprocket")
            {
                int teeth = JsonRead.RequireInt(stats, "teethCount", ctx);
                if (JsonRead.OptionalString(stats, "position", ctx) == "Rear")
                {
                    s.RearSprocketTeeth = teeth;
                }
                else
                {
                    s.FrontSprocketTeeth = teeth;
                }
            }

            // Chassis / accessories
            SetFloat(stats, "aeroTuckBonusMultiplier", ctx, v => s.TuckDragBonusMultiplier = v);
            SetInt(stats, "cargoSlots", ctx, v => s.CargoSlots = v);
            SetFloat(stats, "wheelRadiusM", ctx, v => s.WheelRadiusM = v);

            // Tool roll
            s.IsTool = rawCategory == "Tool";
            s.IsSpare = rawCategory == "Spare";
            string? toolTag = JsonRead.OptionalString(stats, "toolTag", ctx);
            if (toolTag != null)
            {
                s.ToolCapabilityTag = toolTag;
            }

            return s;
        }

        private static void SetFloat(JObject stats, string key, string ctx, Action<float> apply)
        {
            JToken? token = stats[key];
            if (token != null && token.Type != JTokenType.Null)
            {
                apply((float)JsonRead.AsNumber(token, JsonRead.Ctx(ctx, key)));
            }
        }

        private static void SetInt(JObject stats, string key, string ctx, Action<int> apply)
        {
            JToken? token = stats[key];
            if (token != null && token.Type != JTokenType.Null)
            {
                apply(JsonRead.AsInt(token, JsonRead.Ctx(ctx, key)));
            }
        }
    }
}
