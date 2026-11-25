#nullable enable

namespace MopedSim.Core
{
    /// <summary>
    /// Immutable per-tick rider and environmental input snapshot passed from Unity or test harnesses into BikeSimulation.Step.
    /// </summary>
    public readonly struct RideInputState
    {
        /// <summary>
        /// Standard normalized sea-level relative air density (1.0 = 15C sea level; >1.0 = cool dense fog; &lt;1.0 = elevation).
        /// </summary>
        public const float StandardAirDensity = 1.0f;

        /// <summary>
        /// Default neutral riding input on dry level asphalt at sea level.
        /// </summary>
        public static RideInputState Default
        {
            get { return new RideInputState(0f); }
        }

        public readonly float Throttle;        // 0..1
        public readonly float FrontBrake;      // 0..1
        public readonly float RearBrake;       // 0..1
        public readonly float Steer;           // -1..1
        public readonly bool AeroTuck;
        public readonly bool PedalAssist;
        public readonly bool Choke;
        public readonly bool Decompressor;
        public readonly float RoadSlopeRadians;
        public readonly float SurfaceGrip;
        public readonly float SurfaceRoughness;
        public readonly float AmbientAirDensity;

        /// <summary>
        /// Constructs an immutable riding input snapshot.
        /// </summary>
        public RideInputState(
            float throttle,
            float frontBrake = 0f,
            float rearBrake = 0f,
            float steer = 0f,
            bool aeroTuck = false,
            bool pedalAssist = false,
            bool choke = false,
            bool decompressor = false,
            float roadSlopeRadians = 0f,
            float surfaceGrip = 1.0f,
            float surfaceRoughness = 0f,
            float ambientAirDensity = StandardAirDensity)
        {
            Throttle = SimMath.Clamp01(throttle);
            FrontBrake = SimMath.Clamp01(frontBrake);
            RearBrake = SimMath.Clamp01(rearBrake);
            Steer = SimMath.Clamp(steer, -1f, 1f);
            AeroTuck = aeroTuck;
            PedalAssist = pedalAssist;
            Choke = choke;
            Decompressor = decompressor;
            RoadSlopeRadians = roadSlopeRadians;
            SurfaceGrip = SimMath.Clamp(surfaceGrip, 0f, 1.5f);
            SurfaceRoughness = SimMath.Clamp(surfaceRoughness, 0f, 2f);
            AmbientAirDensity = ambientAirDensity <= 0f ? StandardAirDensity : ambientAirDensity;
        }
    }
}
