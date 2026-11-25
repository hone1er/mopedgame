namespace MopedSim.Core
{
    /// <summary>
    /// Canonical mount slot identifiers matching BikeAssemblyGraph and data/catalogs/archetypes.json verbatim.
    /// Every constant here is mounted by at least one catalog archetype (enforced by MopedSim.Data.Tests).
    /// </summary>
    public static class SlotIds
    {
        public const string Frame = "SLOT_FRAME";
        public const string ForkAssembly = "SLOT_FORK_ASSEMBLY";
        public const string FrontWheel = "SLOT_FRONT_WHEEL";
        public const string FrontBrake = "SLOT_FRONT_BRAKE";
        public const string FrontBrakeCable = "SLOT_FRONT_BRAKE_CABLE";
        public const string Handlebars = "SLOT_HANDLEBARS";
        public const string ThrottleCable = "SLOT_THROTTLE_CABLE";
        public const string Headlight = "SLOT_HEADLIGHT";
        public const string FrontFender = "SLOT_FRONT_FENDER";
        public const string RearSuspension = "SLOT_REAR_SUSPENSION";
        public const string RearWheel = "SLOT_REAR_WHEEL";
        public const string Chain = "SLOT_CHAIN";
        public const string FrontSprocket = "SLOT_FRONT_SPROCKET";
        public const string RearSprocket = "SLOT_REAR_SPROCKET";
        public const string RearBrake = "SLOT_REAR_BRAKE";
        public const string RearBrakeCable = "SLOT_REAR_BRAKE_CABLE";
        public const string Seat = "SLOT_SEAT";
        public const string CargoRack = "SLOT_CARGO_RACK";
        public const string FuelTank = "SLOT_FUEL_TANK";
        public const string Petcock = "SLOT_PETCOCK";
        public const string FuelLine = "SLOT_FUEL_LINE";
        public const string InlineFilter = "SLOT_INLINE_FILTER";
        public const string EngineCradle = "SLOT_ENGINE_CRADLE";
        public const string Ignition = "SLOT_IGNITION";
        public const string Flywheel = "SLOT_FLYWHEEL";
        public const string DrivetrainSide = "SLOT_DRIVETRAIN_SIDE";
        public const string VariatorRollers = "SLOT_VARIATOR_ROLLERS";
        public const string VariatorBelt = "SLOT_VARIATOR_BELT";
        public const string IntakeManifold = "SLOT_INTAKE_MANIFOLD";
        public const string IntakeGasket = "SLOT_INTAKE_GASKET";
        public const string Carburetor = "SLOT_CARBURETOR";
        public const string MainJet = "SLOT_MAIN_JET";
        public const string PilotJet = "SLOT_PILOT_JET";
        public const string AirFilter = "SLOT_AIR_FILTER";
        public const string Cylinder = "SLOT_CYLINDER";
        public const string BaseGasket = "SLOT_BASE_GASKET";
        public const string Piston = "SLOT_PISTON";
        public const string CylinderHead = "SLOT_CYLINDER_HEAD";
        public const string HeadGasket = "SLOT_HEAD_GASKET";
        public const string SparkPlug = "SLOT_SPARK_PLUG";
        public const string PlugBoot = "SLOT_PLUG_BOOT";
        public const string Exhaust = "SLOT_EXHAUST";
        public const string ExhaustGasket = "SLOT_EXHAUST_GASKET";
    }

    /// <summary>
    /// Canonical fastener identifiers matching Blender BOLT_* nodes, data/catalogs/archetypes.json, and BikeAssemblyGraph.
    /// Every constant here is declared by at least one catalog archetype (enforced by MopedSim.Data.Tests).
    /// </summary>
    public static class FastenerIds
    {
        public const string StemNut = "BOLT_STEM_NUT";
        public const string FrontAxleL = "BOLT_FRONT_AXLE_L";
        public const string FrontAxleR = "BOLT_FRONT_AXLE_R";
        public const string BarClamp1 = "BOLT_BAR_CLAMP_1";
        public const string BarClamp2 = "BOLT_BAR_CLAMP_2";
        public const string BarClamp3 = "BOLT_BAR_CLAMP_3";
        public const string BarClamp4 = "BOLT_BAR_CLAMP_4";
        public const string ShockTopL = "BOLT_SHOCK_TOP_L";
        public const string ShockTopR = "BOLT_SHOCK_TOP_R";
        public const string ShockBotL = "BOLT_SHOCK_BOT_L";
        public const string ShockBotR = "BOLT_SHOCK_BOT_R";
        public const string RearAxleL = "BOLT_REAR_AXLE_L";
        public const string RearAxleR = "BOLT_REAR_AXLE_R";
        public const string SeatPost = "BOLT_SEAT_POST";
        public const string Rack1 = "BOLT_RACK_1";
        public const string Rack2 = "BOLT_RACK_2";
        public const string EngineMount1 = "BOLT_ENGINE_MOUNT_1";
        public const string EngineMount2 = "BOLT_ENGINE_MOUNT_2";
        public const string EngineMount3 = "BOLT_ENGINE_MOUNT_3";
        public const string FlywheelNut = "BOLT_FLYWHEEL_NUT";
        public const string ClutchCover = "BOLT_CLUTCH_COVER";
        public const string Intake1 = "BOLT_INTAKE_1";
        public const string Intake2 = "BOLT_INTAKE_2";
        public const string CarbClamp = "BOLT_CARB_CLAMP";
        public const string FilterClamp = "BOLT_FILTER_CLAMP";
        public const string HeadNut1 = "BOLT_HEAD_NUT_1";
        public const string HeadNut2 = "BOLT_HEAD_NUT_2";
        public const string HeadNut3 = "BOLT_HEAD_NUT_3";
        public const string HeadNut4 = "BOLT_HEAD_NUT_4";
        public const string SparkPlugThread = "THREAD_SPARK_PLUG";
        public const string ExhaustFlange1 = "BOLT_EXHAUST_FLANGE_1";
        public const string ExhaustFlange2 = "BOLT_EXHAUST_FLANGE_2";
        public const string ExhaustFrameBracket = "BOLT_EXHAUST_FRAME_BRACKET";
    }

    /// <summary>
    /// Canonical fault identifiers matching PROJECT_GUIDE.md §2.3 and data/catalogs/faults.json.
    /// </summary>
    public static class FaultIds
    {
        public const string FouledPlug = "fault_fouled_plug";
        public const string CloggedJet = "fault_clogged_jet";
        public const string CondenserHeatSoak = "fault_condenser_heatsoak";
        public const string PointsDrift = "fault_points_drift";
        public const string AirLeak = "fault_air_leak";
        public const string ThrownChain = "fault_thrown_chain";
        public const string SnappedBelt = "fault_snapped_belt";
        public const string ExhaustLoose = "fault_exhaust_loose";
        public const string SoftSeize = "fault_soft_seize";
        public const string HardSeize = "fault_hard_seize";
    }

    /// <summary>
    /// Canonical tool and roadside spare item identifiers.
    /// </summary>
    public static class ToolIds
    {
        public const string SparkPlugWrench = "tool_spark_plug_wrench";
        public const string Flathead = "tool_flathead";
        public const string NeedleNose = "tool_needle_nose";
        public const string FeelerGauge = "tool_feeler_gauge";
        public const string Wrench10mm = "tool_wrench_10mm";
        public const string Wrench15mm = "tool_wrench_15mm";
        public const string TorqueWrench = "tool_torque_wrench";
        public const string FlywheelPuller = "tool_flywheel_puller";
        public const string VariatorStrap = "tool_variator_strap";
        public const string PenetratingOil = "tool_penetrating_oil";
        public const string WireBrush = "tool_wire_brush";
        public const string CarbCleaner = "tool_carb_cleaner";

        public const string SparePlug = "spare_plug";
        public const string SpareMasterLink = "spare_master_link";
        public const string SpareBelt = "spare_belt";
        public const string SpareFlangeNut = "spare_flange_nut";
        public const string SpareCondenser = "spare_condenser";
        public const string SpareMainJet = "spare_main_jet";
    }

    /// <summary>
    /// Canonical chassis archetype identifiers.
    /// </summary>
    public static class ArchetypeIds
    {
        public const string MaxerE50 = "arch_maxer_e50";
        public const string BramblePA50 = "arch_bramble_pa50";
        public const string TargaA35 = "arch_targa_a35";
    }
}
