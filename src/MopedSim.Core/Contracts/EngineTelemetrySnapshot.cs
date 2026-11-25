#nullable enable

namespace MopedSim.Core
{
    /// <summary>
    /// Immutable zero-allocation telemetry snapshot emitted by BikeSimulation.Step for Ride, Audio, UI, and tests.
    /// </summary>
    public readonly struct EngineTelemetrySnapshot
    {
        // Canonical boundary fields required by PROJECT_GUIDE.md §4.3
        public readonly float Rpm;
        public readonly float RearWheelTorqueNm;
        public readonly float EngineBrakeTorqueNm;
        public readonly float EffectiveAfr;
        public readonly float ChtCelsius;
        public readonly float FuelLitersRemaining;
        public readonly bool IsReserveActive;
        public readonly bool IsFourStroking;
        public readonly bool IsDetonating;
        public readonly bool IsPowerbandActive;
        public readonly bool IsStalled;
        public readonly bool IsSeized;
        public readonly float ExhaustLeakFactor;
        public readonly float HeadlightOutput; // 0..1
        public readonly int CurrentGear;       // 0=idle/clutch open, 1..2

        // Extended zero-GC sensory, diagnostic, and drivetrain fields
        public readonly float ThrottlePosition;
        public readonly float VehicleSpeedMps;
        public readonly float VehicleSpeedMph;
        public readonly float CrankcaseTempCelsius;
        public readonly float DetonationRisk;
        public readonly float AirLeakSeverity;
        public readonly float MisfireFactor;
        public readonly float ClutchEngagement;
        public readonly float VariatorRatio;
        public readonly float CompressionHealth;
        public readonly float RiderStamina;
        public readonly bool IsRunning;
        public readonly bool IsHardSeized;
        public readonly bool IsSoftSeized;
        public readonly bool IsSputtering;
        public readonly bool IsChainThrown;
        public readonly bool IsBeltSnapped;
        public readonly float ChassisVibration;
        public readonly float PowerOutputWatts;
        public readonly ThermalZone ThermalZone;
        public readonly float PlugColorIndex;

        /// <summary>
        /// Constructs a complete telemetry snapshot.
        /// </summary>
        public EngineTelemetrySnapshot(
            float rpm,
            float rearWheelTorqueNm,
            float engineBrakeTorqueNm,
            float effectiveAfr,
            float chtCelsius,
            float fuelLitersRemaining,
            bool isReserveActive,
            bool isFourStroking,
            bool isDetonating,
            bool isPowerbandActive,
            bool isStalled,
            bool isSeized,
            float exhaustLeakFactor,
            float headlightOutput,
            int currentGear,
            float throttlePosition = 0f,
            float vehicleSpeedMps = 0f,
            float vehicleSpeedMph = 0f,
            float crankcaseTempCelsius = 25f,
            float detonationRisk = 0f,
            float airLeakSeverity = 0f,
            float misfireFactor = 0f,
            float clutchEngagement = 0f,
            float variatorRatio = 1f,
            float compressionHealth = 1f,
            float riderStamina = 1f,
            bool isRunning = false,
            bool isHardSeized = false,
            bool isSoftSeized = false,
            bool isSputtering = false,
            bool isChainThrown = false,
            bool isBeltSnapped = false,
            float chassisVibration = 0f,
            float powerOutputWatts = 0f,
            ThermalZone thermalZone = ThermalZone.Cold,
            float plugColorIndex = 0.5f)
        {
            Rpm = rpm;
            RearWheelTorqueNm = rearWheelTorqueNm;
            EngineBrakeTorqueNm = engineBrakeTorqueNm;
            EffectiveAfr = effectiveAfr;
            ChtCelsius = chtCelsius;
            FuelLitersRemaining = fuelLitersRemaining;
            IsReserveActive = isReserveActive;
            IsFourStroking = isFourStroking;
            IsDetonating = isDetonating;
            IsPowerbandActive = isPowerbandActive;
            IsStalled = isStalled;
            IsSeized = isSeized;
            ExhaustLeakFactor = exhaustLeakFactor;
            HeadlightOutput = headlightOutput;
            CurrentGear = currentGear;
            ThrottlePosition = throttlePosition;
            VehicleSpeedMps = vehicleSpeedMps;
            VehicleSpeedMph = vehicleSpeedMph;
            CrankcaseTempCelsius = crankcaseTempCelsius;
            DetonationRisk = detonationRisk;
            AirLeakSeverity = airLeakSeverity;
            MisfireFactor = misfireFactor;
            ClutchEngagement = clutchEngagement;
            VariatorRatio = variatorRatio;
            CompressionHealth = compressionHealth;
            RiderStamina = riderStamina;
            IsRunning = isRunning;
            IsHardSeized = isHardSeized;
            IsSoftSeized = isSoftSeized;
            IsSputtering = isSputtering;
            IsChainThrown = isChainThrown;
            IsBeltSnapped = isBeltSnapped;
            ChassisVibration = chassisVibration;
            PowerOutputWatts = powerOutputWatts;
            ThermalZone = thermalZone;
            PlugColorIndex = plugColorIndex;
        }
    }
}
