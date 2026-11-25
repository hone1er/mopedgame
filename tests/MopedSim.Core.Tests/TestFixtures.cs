using System;
using System.Collections.Generic;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    internal static class TestFixtures
    {
        public static Dictionary<string, PartDefinition> CreateCatalog()
        {
            var catalog = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);

            void Add(PartDefinition part)
            {
                catalog[part.Id] = part;
            }

            Add(new PartDefinition("frame_maxer_e50", "Maxer E50 Frame", PartCategory.Frame, new[] { "E50_Frame" }, 14.0f, 180m));
            Add(new PartDefinition("frame_bramble_pa50", "Bramble PA50 Frame", PartCategory.Frame, new[] { "PA50_Frame" }, 15.0f, 220m));
            Add(new PartDefinition("frame_targa_a35", "Targa A35 Frame", PartCategory.Frame, new[] { "A35_Frame" }, 15.5f, 210m));

            Add(new PartDefinition("fork_stock_e50", "Stock Fork", PartCategory.Fork, new[] { "Standard_Steerer" }, 4.2f, 65m));
            Add(new PartDefinition("wheel_16_spoke", "16\" Spoked Wheel", PartCategory.Wheel, new[] { "16in_Axle" }, 5.0f, 75m,
                new PartStats { WheelRadiusM = 0.27f }));
            Add(new PartDefinition("bars_stock_upright", "Upright Stock Bars", PartCategory.Handlebars, new[] { "Standard_BarClamp" }, 1.1f, 30m,
                new PartStats { AeroDragMultiplier = 1.0f, TuckDragBonusMultiplier = 1.0f }));
            Add(new PartDefinition("bars_cafe_clipon", "Low Cafe Clip-Ons", PartCategory.Handlebars, new[] { "Standard_BarClamp" }, 1.0f, 55m,
                new PartStats { AeroDragMultiplier = 0.90f, TuckDragBonusMultiplier = 1.12f }));
            Add(new PartDefinition("bars_bmx_riser", "Tall BMX Riser Bars", PartCategory.Handlebars, new[] { "Standard_BarClamp" }, 1.2f, 45m,
                new PartStats { AeroDragMultiplier = 1.12f, TuckDragBonusMultiplier = 0.85f }));

            Add(new PartDefinition("light_round_6v", "Round 6V Headlight", PartCategory.Headlight, new[] { "Standard_LightBracket" }, 0.6f, 25m,
                new PartStats { HeadlightMaxOutput = 0.78f }));
            Add(new PartDefinition("seat_solo_spring", "Spring Solo Saddle", PartCategory.Seat, new[] { "Standard_SeatPost" }, 1.8f, 40m));
            Add(new PartDefinition("rack_milk_crate", "West Oakland Milk Crate", PartCategory.CargoRack, new[] { "Rear_Rack_Mount" }, 1.5f, 20m,
                new PartStats { CargoSlots = 4 }));

            Add(new PartDefinition("case_e50", "Maxer E50 Crankcase", PartCategory.EngineCase, new[] { "E50_Case" }, 6.8f, 150m));
            Add(new PartDefinition("case_pa50", "Bramble PA50 Crankcase", PartCategory.EngineCase, new[] { "PA50_Case" }, 7.5f, 175m));
            Add(new PartDefinition("case_a35", "Targa A35 Crankcase", PartCategory.EngineCase, new[] { "A35_Case" }, 7.8f, 170m));

            Add(new PartDefinition("cyl_stock_50", "49cc Cast Iron Cylinder", PartCategory.Cylinder, new[] { "Moped_TopEnd_Studs" }, 2.1f, 80m,
                new PartStats
                {
                    DisplacementCc = 49f,
                    CompressionRatio = 9.0f,
                    PortFlowEfficiency = 1.0f,
                    CoolingFinEfficiency = 1.0f,
                    BoreMm = 38f,
                    StrokeMm = 43f
                }));
            Add(new PartDefinition("cyl_kit_70", "Fonte 70cc Performance Kit", PartCategory.Cylinder, new[] { "Moped_TopEnd_Studs" }, 2.3f, 165m,
                new PartStats
                {
                    DisplacementCc = 70f,
                    CompressionRatio = 10.6f,
                    PortFlowEfficiency = 1.16f,
                    CoolingFinEfficiency = 1.05f,
                    BoreMm = 45f,
                    StrokeMm = 43f
                }));

            Add(new PartDefinition("head_stock", "Stock 50cc Cylinder Head", PartCategory.CylinderHead, new[] { "Moped_Head_Studs" }, 1.0f, 45m,
                new PartStats { CompressionRatio = 9.0f }));
            Add(new PartDefinition("head_highcomp_70", "High-Comp 70cc Head", PartCategory.CylinderHead, new[] { "Moped_Head_Studs" }, 1.1f, 75m,
                new PartStats { CompressionRatio = 10.8f }));

            Add(new PartDefinition("piston_50", "38mm Stock Piston", PartCategory.Piston, new[] { "Moped_Piston_50" }, 0.18f, 35m,
                new PartStats { PistonRingGapMm = 0.25f, OptimalRingGapMinMm = 0.15f, OptimalRingGapMaxMm = 0.45f }));
            Add(new PartDefinition("piston_70", "45mm 2-Ring 70cc Piston", PartCategory.Piston, new[] { "Moped_Piston_70", "Moped_Piston_50" }, 0.22f, 55m,
                new PartStats { PistonRingGapMm = 0.25f, OptimalRingGapMinMm = 0.15f, OptimalRingGapMaxMm = 0.45f }));

            Add(new PartDefinition("plug_b6hs", "NGK B6HS Hot Plug", PartCategory.SparkPlug, new[] { "M14_SparkPlug" }, 0.06f, 6m,
                new PartStats { HeatRange = 6, ElectrodeGapMm = 0.60f }));
            Add(new PartDefinition("plug_b8hs", "NGK B8HS Cold Plug", PartCategory.SparkPlug, new[] { "M14_SparkPlug" }, 0.06f, 7m,
                new PartStats { HeatRange = 8, ElectrodeGapMm = 0.60f }));

            Add(new PartDefinition("intake_stock", "Stock 12mm Intake", PartCategory.IntakeManifold, new[] { "Moped_Intake_2Bolt" }, 0.35f, 20m));
            Add(new PartDefinition("intake_reed_15mm", "15mm Reed Intake", PartCategory.IntakeManifold, new[] { "Moped_Intake_2Bolt" }, 0.42f, 55m));

            Add(new PartDefinition("carb_bing_12", "Bavaria 12mm Carburetor", PartCategory.Carburetor, new[] { "Spigot_Carb" }, 0.45f, 65m,
                new PartStats { VenturiMm = 12f, MainJetSize = 60, PilotJetSize = 38, NeedleClipPosition = 3, IdleScrewTurns = 1.5f }));
            Add(new PartDefinition("carb_sha_15", "Milano SHA-15 Carburetor", PartCategory.Carburetor, new[] { "Spigot_Carb" }, 0.48f, 80m,
                new PartStats { VenturiMm = 15f, MainJetSize = 72, PilotJetSize = 40, NeedleClipPosition = 3, IdleScrewTurns = 1.5f }));

            Add(new PartDefinition("jet_54", "Main Jet #54", PartCategory.MainJet, new[] { "M5_MainJet" }, 0.01f, 4m, new PartStats { MainJetSize = 54 }));
            Add(new PartDefinition("jet_60", "Main Jet #60", PartCategory.MainJet, new[] { "M5_MainJet" }, 0.01f, 4m, new PartStats { MainJetSize = 60 }));
            Add(new PartDefinition("jet_72", "Main Jet #72", PartCategory.MainJet, new[] { "M5_MainJet" }, 0.01f, 4m, new PartStats { MainJetSize = 72 }));
            Add(new PartDefinition("jet_88", "Main Jet #88", PartCategory.MainJet, new[] { "M5_MainJet" }, 0.01f, 4m, new PartStats { MainJetSize = 88 }));

            Add(new PartDefinition("filter_stock_box", "Stock Air Box", PartCategory.AirFilter, new[] { "Carb_Inlet" }, 0.25f, 18m,
                new PartStats { AirFilterRestriction = 0.14f, MoistureSensitivity = 0.15f }));
            Add(new PartDefinition("filter_pod_mesh", "Open Wire Mesh Pod", PartCategory.AirFilter, new[] { "Carb_Inlet" }, 0.12f, 24m,
                new PartStats { AirFilterRestriction = 0.02f, MoistureSensitivity = 0.70f }));

            Add(new PartDefinition("tank_stock", "3.2L Steel Tank", PartCategory.FuelTank, new[] { "Frame_Tank" }, 2.2f, 70m,
                new PartStats { TankCapacityLiters = 3.2f, ReserveLiters = 0.6f, FloatBowlCapacityLiters = 0.04f }));
            Add(new PartDefinition("filter_inline", "Clear Inline Fuel Filter", PartCategory.InlineFilter, new[] { "FuelLine_6mm" }, 0.04f, 6m));

            Add(new PartDefinition("pipe_stock_baffled", "Stock Baffled Cigar Pipe", PartCategory.Exhaust, new[] { "Moped_Exhaust_2Bolt" }, 2.8f, 55m,
                new PartStats
                {
                    FlowEfficiency = 1.0f,
                    PeakRpmStart = 2800f,
                    PeakRpmEnd = 5800f,
                    NoiseDb = 78f,
                    TorqueCurve = new List<TorqueCurvePoint>
                    {
                        new TorqueCurvePoint(1500f, 0.85f),
                        new TorqueCurvePoint(3500f, 1.02f),
                        new TorqueCurvePoint(5500f, 1.00f),
                        new TorqueCurvePoint(6800f, 0.72f),
                        new TorqueCurvePoint(9000f, 0.48f)
                    }
                }));
            Add(new PartDefinition("pipe_circuit_e50_proma", "Bay Circuit Torque Pipe", PartCategory.Exhaust, new[] { "Moped_Exhaust_2Bolt" }, 2.4f, 135m,
                new PartStats
                {
                    FlowEfficiency = 1.18f,
                    PeakRpmStart = 4800f,
                    PeakRpmEnd = 7900f,
                    NoiseDb = 92f,
                    TorqueCurve = new List<TorqueCurvePoint>
                    {
                        new TorqueCurvePoint(2500f, 0.86f),
                        new TorqueCurvePoint(5500f, 1.24f),
                        new TorqueCurvePoint(7800f, 1.18f),
                        new TorqueCurvePoint(9500f, 0.72f)
                    }
                }));
            Add(new PartDefinition("pipe_topend_expansion", "P4 Hand-Cone Expansion Chamber", PartCategory.Exhaust, new[] { "Moped_Exhaust_2Bolt" }, 2.2f, 195m,
                new PartStats
                {
                    FlowEfficiency = 1.26f,
                    PeakRpmStart = 6500f,
                    PeakRpmEnd = 9800f,
                    NoiseDb = 98f,
                    TorqueCurve = new List<TorqueCurvePoint>
                    {
                        new TorqueCurvePoint(2500f, 0.75f),
                        new TorqueCurvePoint(5000f, 0.84f),
                        new TorqueCurvePoint(7200f, 1.38f),
                        new TorqueCurvePoint(9200f, 1.30f),
                        new TorqueCurvePoint(10800f, 0.75f)
                    }
                }));

            Add(new PartDefinition("stator_points_6v", "6V Points & Condenser Stator", PartCategory.Ignition, new[] { "Moped_Stator" }, 0.9f, 50m,
                new PartStats { IgnitionType = IgnitionType.Points6V, InitialPointsGapMm = 0.35f, CondenserHeatThresholdC = 95f, MaxRpmRating = 8500f }));
            Add(new PartDefinition("stator_cdi_12v", "12V Electronic CDI Kit", PartCategory.Ignition, new[] { "Moped_Stator" }, 0.85f, 160m,
                new PartStats { IgnitionType = IgnitionType.Cdi12V, MaxRpmRating = 11500f }));

            Add(new PartDefinition("clutch_e50_stock", "E50 Wet 3-Shoe Clutch", PartCategory.Clutch, new[] { "E50_Clutch" }, 1.1f, 65m,
                new PartStats { DrivetrainType = DrivetrainType.SingleSpeedCentrifugal, ClutchEngageRpm = 2400f, ClutchSlipHeatRate = 1.0f }));
            Add(new PartDefinition("variator_pa50_stock", "PA50 Dual-Pulley Variator", PartCategory.Variator, new[] { "PA50_Variator" }, 1.6f, 120m,
                new PartStats { DrivetrainType = DrivetrainType.VariatedBelt, ClutchEngageRpm = 2400f, RollerWeightGrams = 6.5f, ContraSpringRate = 1.0f }));
            Add(new PartDefinition("gearbox_a35_2spd", "A35 2-Speed Auto Gearbox", PartCategory.Gearbox, new[] { "A35_Gearbox" }, 1.8f, 140m,
                new PartStats { DrivetrainType = DrivetrainType.TwoSpeedAuto, ClutchEngageRpm = 2200f, GearRatio1 = 2.25f, GearRatio2 = 1.18f, ShiftRpmThreshold = 4700f }));

            Add(new PartDefinition("sprocket_front_13", "13T Front Sprocket", PartCategory.SprocketFront, new[] { "Front_Spline" }, 0.25f, 14m,
                new PartStats { FrontSprocketTeeth = 13 }));
            Add(new PartDefinition("sprocket_rear_45", "45T Rear Sprocket", PartCategory.SprocketRear, new[] { "Rear_Hub_4Bolt" }, 0.75f, 24m,
                new PartStats { RearSprocketTeeth = 45 }));

            Add(new PartDefinition("gasket_base", "Base Gasket", PartCategory.Gasket, new[] { "Moped_BaseGasket" }, 0.02f, 4m));
            Add(new PartDefinition("gasket_head", "Copper Head Gasket", PartCategory.Gasket, new[] { "Moped_HeadGasket" }, 0.03f, 8m));
            Add(new PartDefinition("gasket_intake", "Intake Manifold Gasket", PartCategory.Gasket, new[] { "Moped_IntakeGasket" }, 0.01f, 3m));
            Add(new PartDefinition("gasket_exhaust", "Exhaust Crush Gasket", PartCategory.Gasket, new[] { "Moped_ExhaustGasket" }, 0.02f, 5m));

            return catalog;
        }

        public static BikeArchetypeSpec CreateArchetype(string archetypeId)
        {
            DrivetrainType dt = DrivetrainType.SingleSpeedCentrifugal;
            string name = "Maxer E50";
            string frame = "frame_maxer_e50";
            string engineCase = "case_e50";
            string drivetrainPart = "clutch_e50_stock";
            PartCategory driveCat = PartCategory.Clutch;
            string driveIface = "E50_Clutch";

            if (string.Equals(archetypeId, ArchetypeIds.BramblePA50, StringComparison.Ordinal))
            {
                dt = DrivetrainType.VariatedBelt;
                name = "Bramble PA50";
                frame = "frame_bramble_pa50";
                engineCase = "case_pa50";
                drivetrainPart = "variator_pa50_stock";
                driveCat = PartCategory.Variator;
                driveIface = "PA50_Variator";
            }
            else if (string.Equals(archetypeId, ArchetypeIds.TargaA35, StringComparison.Ordinal))
            {
                dt = DrivetrainType.TwoSpeedAuto;
                name = "Targa A35";
                frame = "frame_targa_a35";
                engineCase = "case_a35";
                drivetrainPart = "gearbox_a35_2spd";
                driveCat = PartCategory.Gearbox;
                driveIface = "A35_Gearbox";
            }

            var spec = new BikeArchetypeSpec
            {
                Id = archetypeId,
                DisplayName = name,
                DrivetrainType = dt,
                BaseDryMassKg = 2.0f,
                WheelRadiusM = 0.27f,
                WheelbaseM = 1.15f,
                DragAreaCdA = 0.56f,
                RollingResistanceCoeff = 0.014f,
                MountSlots = new List<MountSlotSpec>
                {
                    new MountSlotSpec(SlotIds.FrameChassis, null, PartCategory.Frame),
                    new MountSlotSpec(SlotIds.ForkAssembly, SlotIds.FrameChassis, PartCategory.Fork),
                    new MountSlotSpec(SlotIds.FrontWheel, SlotIds.ForkAssembly, PartCategory.Wheel),
                    new MountSlotSpec(SlotIds.RearWheel, SlotIds.FrameChassis, PartCategory.Wheel),
                    new MountSlotSpec(SlotIds.Handlebars, SlotIds.ForkAssembly, PartCategory.Handlebars),
                    new MountSlotSpec(SlotIds.Headlight, SlotIds.ForkAssembly, PartCategory.Headlight),
                    new MountSlotSpec(SlotIds.Seat, SlotIds.FrameChassis, PartCategory.Seat),
                    new MountSlotSpec(SlotIds.CargoRack, SlotIds.FrameChassis, PartCategory.CargoRack),
                    new MountSlotSpec(SlotIds.FuelTank, SlotIds.FrameChassis, PartCategory.FuelTank),
                    new MountSlotSpec(SlotIds.InlineFilter, SlotIds.FuelTank, PartCategory.InlineFilter),
                    new MountSlotSpec(SlotIds.EngineCradle, SlotIds.FrameChassis, PartCategory.EngineCase),
                    new MountSlotSpec(SlotIds.Ignition, SlotIds.EngineCradle, PartCategory.Ignition),
                    new MountSlotSpec(SlotIds.DrivetrainSide, SlotIds.EngineCradle, driveCat, new[] { driveIface }),
                    new MountSlotSpec(SlotIds.FrontSprocket, SlotIds.EngineCradle, PartCategory.SprocketFront),
                    new MountSlotSpec(SlotIds.RearSprocket, SlotIds.RearWheel, PartCategory.SprocketRear),
                    new MountSlotSpec(SlotIds.BaseGasket, SlotIds.EngineCradle, PartCategory.Gasket),
                    new MountSlotSpec(SlotIds.Piston, SlotIds.EngineCradle, PartCategory.Piston,
                        new[] { "Moped_Piston_50", "Moped_Piston_70" },
                        requiresEmptySlotIds: new[] { SlotIds.Cylinder }),
                    new MountSlotSpec(SlotIds.Cylinder, SlotIds.EngineCradle, PartCategory.Cylinder,
                        new[] { "Moped_TopEnd_Studs" },
                        blockedBySlotIds: new[] { SlotIds.CylinderHead, SlotIds.Exhaust, SlotIds.IntakeManifold },
                        blockedByFastenerIds: new[] { FastenerIds.HeadNut1, FastenerIds.HeadNut2, FastenerIds.HeadNut3, FastenerIds.HeadNut4 }),
                    new MountSlotSpec(SlotIds.HeadGasket, SlotIds.Cylinder, PartCategory.Gasket),
                    new MountSlotSpec(SlotIds.CylinderHead, SlotIds.Cylinder, PartCategory.CylinderHead,
                        new[] { "Moped_Head_Studs" },
                        blockedByFastenerIds: new[] { FastenerIds.HeadNut1, FastenerIds.HeadNut2, FastenerIds.HeadNut3, FastenerIds.HeadNut4 }),
                    new MountSlotSpec(SlotIds.SparkPlug, SlotIds.CylinderHead, PartCategory.SparkPlug, new[] { "M14_SparkPlug" }),
                    new MountSlotSpec(SlotIds.ExhaustGasket, SlotIds.Cylinder, PartCategory.Gasket),
                    new MountSlotSpec(SlotIds.Exhaust, SlotIds.Cylinder, PartCategory.Exhaust,
                        new[] { "Moped_Exhaust_2Bolt" },
                        blockedByFastenerIds: new[] { FastenerIds.ExhaustFlange1, FastenerIds.ExhaustFlange2, FastenerIds.ExhaustFrameBracket }),
                    new MountSlotSpec(SlotIds.IntakeGasket, SlotIds.Cylinder, PartCategory.Gasket),
                    new MountSlotSpec(SlotIds.IntakeManifold, SlotIds.Cylinder, PartCategory.IntakeManifold,
                        new[] { "Moped_Intake_2Bolt" },
                        blockedByFastenerIds: new[] { FastenerIds.Intake1, FastenerIds.Intake2 }),
                    new MountSlotSpec(SlotIds.Carburetor, SlotIds.IntakeManifold, PartCategory.Carburetor,
                        new[] { "Spigot_Carb" },
                        blockedByFastenerIds: new[] { FastenerIds.CarbClamp }),
                    new MountSlotSpec(SlotIds.MainJet, SlotIds.Carburetor, PartCategory.MainJet, new[] { "M5_MainJet" }),
                    new MountSlotSpec(SlotIds.AirFilter, SlotIds.Carburetor, PartCategory.AirFilter, new[] { "Carb_Inlet" })
                },
                Fasteners = new List<FastenerSpec>
                {
                    new FastenerSpec(FastenerIds.EngineMount1, SlotIds.EngineCradle, 22f, 3f, 34f, 0.02f),
                    new FastenerSpec(FastenerIds.HeadNut1, SlotIds.CylinderHead, 12f, 2f, 18f, 0.06f),
                    new FastenerSpec(FastenerIds.HeadNut2, SlotIds.CylinderHead, 12f, 2f, 18f, 0.06f),
                    new FastenerSpec(FastenerIds.HeadNut3, SlotIds.CylinderHead, 12f, 2f, 18f, 0.06f),
                    new FastenerSpec(FastenerIds.HeadNut4, SlotIds.CylinderHead, 12f, 2f, 18f, 0.06f),
                    new FastenerSpec(FastenerIds.SparkPlugThread, SlotIds.SparkPlug, 18f, 3f, 28f, 0.02f),
                    new FastenerSpec(FastenerIds.ExhaustFlange1, SlotIds.Exhaust, 9f, 1.5f, 15f, 0.14f),
                    new FastenerSpec(FastenerIds.ExhaustFlange2, SlotIds.Exhaust, 9f, 1.5f, 15f, 0.14f),
                    new FastenerSpec(FastenerIds.ExhaustFrameBracket, SlotIds.Exhaust, 14f, 2f, 24f, 0.08f),
                    new FastenerSpec(FastenerIds.Intake1, SlotIds.IntakeManifold, 9f, 1.5f, 15f, 0.08f),
                    new FastenerSpec(FastenerIds.Intake2, SlotIds.IntakeManifold, 9f, 1.5f, 15f, 0.08f),
                    new FastenerSpec(FastenerIds.CarbClamp, SlotIds.Carburetor, 6f, 1.5f, 10f, 0.10f)
                },
                DefaultPartIds = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { SlotIds.FrameChassis, frame },
                    { SlotIds.ForkAssembly, "fork_stock_e50" },
                    { SlotIds.FrontWheel, "wheel_16_spoke" },
                    { SlotIds.RearWheel, "wheel_16_spoke" },
                    { SlotIds.Handlebars, "bars_stock_upright" },
                    { SlotIds.Headlight, "light_round_6v" },
                    { SlotIds.Seat, "seat_solo_spring" },
                    { SlotIds.FuelTank, "tank_stock" },
                    { SlotIds.InlineFilter, "filter_inline" },
                    { SlotIds.EngineCradle, engineCase },
                    { SlotIds.Ignition, "stator_points_6v" },
                    { SlotIds.DrivetrainSide, drivetrainPart },
                    { SlotIds.FrontSprocket, "sprocket_front_13" },
                    { SlotIds.RearSprocket, "sprocket_rear_45" },
                    { SlotIds.BaseGasket, "gasket_base" },
                    { SlotIds.Piston, "piston_50" },
                    { SlotIds.Cylinder, "cyl_stock_50" },
                    { SlotIds.HeadGasket, "gasket_head" },
                    { SlotIds.CylinderHead, "head_stock" },
                    { SlotIds.SparkPlug, "plug_b6hs" },
                    { SlotIds.ExhaustGasket, "gasket_exhaust" },
                    { SlotIds.Exhaust, "pipe_stock_baffled" },
                    { SlotIds.IntakeGasket, "gasket_intake" },
                    { SlotIds.IntakeManifold, "intake_stock" },
                    { SlotIds.Carburetor, "carb_bing_12" },
                    { SlotIds.MainJet, "jet_60" },
                    { SlotIds.AirFilter, "filter_stock_box" }
                }
            };

            return spec;
        }

        public static BikeSimulation CreateStockBike(string archetypeId = ArchetypeIds.MaxerE50)
        {
            var catalog = CreateCatalog();
            var spec = CreateArchetype(archetypeId);
            return new BikeSimulation(spec, catalog);
        }

        public static BikeSimulation Create70ccKittedBike(string mainJetId)
        {
            var catalog = CreateCatalog();
            var spec = CreateArchetype(ArchetypeIds.MaxerE50);
            spec.DefaultPartIds[SlotIds.Cylinder] = "cyl_kit_70";
            spec.DefaultPartIds[SlotIds.Piston] = "piston_70";
            spec.DefaultPartIds[SlotIds.CylinderHead] = "head_highcomp_70";
            spec.DefaultPartIds[SlotIds.SparkPlug] = "plug_b8hs";
            spec.DefaultPartIds[SlotIds.IntakeManifold] = "intake_reed_15mm";
            spec.DefaultPartIds[SlotIds.Carburetor] = "carb_sha_15";
            spec.DefaultPartIds[SlotIds.MainJet] = mainJetId;
            spec.DefaultPartIds[SlotIds.Exhaust] = "pipe_circuit_e50_proma";
            spec.DefaultPartIds[SlotIds.AirFilter] = "filter_pod_mesh";
            return new BikeSimulation(spec, catalog);
        }
    }
}
