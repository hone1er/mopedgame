#nullable enable

namespace MopedSim.Core
{
    /// <summary>
    /// Mechanical and garage inventory categories for parts, tools, and spares.
    /// </summary>
    public enum PartCategory
    {
        Frame = 0,
        Fork = 1,
        EngineCase = 2,
        Cylinder = 3,
        CylinderHead = 4,
        Piston = 5,
        SparkPlug = 6,
        IntakeManifold = 7,
        Carburetor = 8,
        AirFilter = 9,
        MainJet = 10,
        PilotJet = 11,
        FuelTank = 12,
        Petcock = 13,
        InlineFilter = 14,
        Exhaust = 15,
        Ignition = 16,
        Clutch = 17,
        Variator = 18,
        Gearbox = 19,
        DriveBelt = 20,
        Chain = 21,
        SprocketFront = 22,
        SprocketRear = 23,
        Wheel = 24,
        RearShocks = 25,
        Handlebars = 26,
        Seat = 27,
        Headlight = 28,
        CargoRack = 29,
        Gasket = 30,
        ToolRollItem = 31,
        Consumable = 32,
        FuelLine = 33,
        VariatorRoller = 34,
        Fender = 35,
        Brake = 36,
        Cable = 37,
        PremixOil = 38,
        GarageStation = 39
    }

    /// <summary>
    /// Supported moped transmission architectures.
    /// </summary>
    public enum DrivetrainType
    {
        SingleSpeedCentrifugal = 0,
        VariatedBelt = 1,
        TwoSpeedAuto = 2
    }

    /// <summary>
    /// Supported ignition architectures.
    /// </summary>
    public enum IgnitionType
    {
        Points6V = 0,
        Cdi12V = 1
    }

    /// <summary>
    /// Fuel tank petcock valve positions.
    /// </summary>
    public enum PetcockMode
    {
        Off = 0,
        On = 1,
        Reserve = 2
    }

    /// <summary>
    /// Spark plug ceramic insulator diagnostic color zones.
    /// </summary>
    public enum PlugColorZone
    {
        ChalkWhite = 0,
        TanLean = 1,
        ChocolateBrown = 2,
        DarkRich = 3,
        WetBlack = 4
    }

    /// <summary>
    /// Cylinder head temperature operating zones.
    /// </summary>
    public enum ThermalZone
    {
        Cold = 0,
        Warming = 1,
        Optimal = 2,
        RunningHot = 3,
        PingWarning = 4,
        SoftSeize = 5,
        HardSeize = 6
    }

    /// <summary>
    /// Outcome status of a garage or roadside wrenching operation.
    /// </summary>
    public enum WrenchActionStatus
    {
        Success = 0,
        Blocked = 1,
        PartNotFound = 2,
        FastenerNotFound = 3,
        IncompatiblePart = 4,
        FastenerStripped = 5,
        FastenerSeized = 6,
        MissingTool = 7,
        InvalidOrientation = 8,
        InvalidSpec = 9,
        SlotAlreadyOccupied = 10,
        SlotEmpty = 11
    }

    /// <summary>
    /// Enumerated mechanical faults corresponding to PROJECT_GUIDE.md §2.3.
    /// </summary>
    public enum FaultType
    {
        FouledPlug = 0,
        CloggedJet = 1,
        CondenserHeatSoak = 2,
        PointsDrift = 3,
        AirLeak = 4,
        ThrownChain = 5,
        SnappedBelt = 6,
        ExhaustLoose = 7,
        SoftSeize = 8,
        HardSeize = 9
    }
}
