#nullable enable

namespace MopedSim.Core
{
    /// <summary>
    /// Simulates 6V points/condenser ignition (gap drift, >95C condenser heat soak, RPM headlight) vs 12V CDI.
    /// </summary>
    public sealed class IgnitionSystem
    {
        public IgnitionType Type { get; set; } = IgnitionType.Points6V;
        public float PointsGapMm { get; set; } = 0.35f;
        public float CondenserHealth { get; set; } = 1.0f; // < 0.70 aged/weak condenser is prone to >95C heat soak
        public float CondenserHeatThresholdC { get; set; } = 95f;
        public float MaxRpmRating { get; set; } = 8500f;
        public float PointsWearRatePerSecAt6kRpm { get; set; } = 0.00015f;

        public bool IsCondenserHeatSoaked { get; private set; }
        public bool IsPointsOutOfSpec { get; private set; }
        public float MisfireFactor { get; private set; }
        public float HeadlightOutput { get; private set; }
        public bool HasSpark { get; private set; } = true;

        /// <summary>
        /// Updates spark quality, condenser heat soak, points drift, and headlight brightness.
        /// </summary>
        public void Step(
            float dt,
            float rpm,
            float crankcaseTempC,
            float plugFoulingLevel,
            float plugElectrodeGapMm,
            bool isPlugInstalled,
            bool isStatorInstalled,
            float moistureFactor,
            ISimRandom rng)
        {
            if (!isStatorInstalled || !isPlugInstalled)
            {
                HasSpark = false;
                MisfireFactor = 1.0f;
                HeadlightOutput = 0f;
                return;
            }

            // Fouled plug or wildly out-of-spec electrode gap cuts spark
            if (plugFoulingLevel >= 0.85f || plugElectrodeGapMm < 0.25f || plugElectrodeGapMm > 1.25f)
            {
                HasSpark = false;
                MisfireFactor = 1.0f;
                HeadlightOutput = Type == IgnitionType.Cdi12V && rpm > 400f ? 0.95f : 0f;
                return;
            }

            float misfire = plugFoulingLevel * 0.35f + SimMath.Clamp01(moistureFactor) * 0.22f;

            if (Type == IgnitionType.Points6V)
            {
                // Rubbing-block wear drifts points gap gradually while spinning
                if (dt > 0f && rpm > 400f)
                {
                    PointsGapMm = SimMath.Max(0.05f, PointsGapMm - dt * (rpm / 6000f) * PointsWearRatePerSecAt6kRpm);
                }

                // Condenser heat-soak hysteresis: cuts out above threshold (>95C), recovers 10C cooler (<85C)
                if (CondenserHealth < 0.70f)
                {
                    if (crankcaseTempC > CondenserHeatThresholdC)
                    {
                        IsCondenserHeatSoaked = true;
                    }
                    else if (IsCondenserHeatSoaked && crankcaseTempC < CondenserHeatThresholdC - 10f)
                    {
                        IsCondenserHeatSoaked = false;
                    }
                }
                else
                {
                    IsCondenserHeatSoaked = false;
                }

                // Points gap spec [0.30, 0.45] mm
                IsPointsOutOfSpec = PointsGapMm < 0.30f || PointsGapMm > 0.45f;
                if (PointsGapMm < 0.14f || PointsGapMm > 0.65f || IsCondenserHeatSoaked)
                {
                    HasSpark = false;
                    MisfireFactor = 1.0f;
                    HeadlightOutput = rpm > 400f ? SimMath.Clamp(0.12f + (rpm - 900f) / 6000f * 0.62f, 0.10f, 0.80f) : 0f;
                    return;
                }

                if (IsPointsOutOfSpec)
                {
                    float gapDelta = PointsGapMm < 0.30f ? (0.30f - PointsGapMm) : (PointsGapMm - 0.45f);
                    float rpmLoad = SimMath.Clamp01(rpm / 6500f);
                    misfire += SimMath.Clamp01(gapDelta * 4.5f * (0.35f + 0.65f * rpmLoad));
                }

                // 6V headlight brightness scales visibly with engine RPM
                HeadlightOutput = rpm > 400f
                    ? SimMath.Clamp(0.14f + (rpm - 900f) / 5600f * 0.66f, 0.12f, 0.84f)
                    : 0f;
            }
            else
            {
                // 12V Electronic CDI: immune to points drift and condenser heat soak, steady bright headlight
                IsCondenserHeatSoaked = false;
                IsPointsOutOfSpec = false;
                HeadlightOutput = rpm > 400f ? 0.96f : 0f;
            }

            // Deterministic stochastic miss jitter when misfire is borderline
            if (misfire > 0.15f && rng.NextDouble() < misfire * 0.25)
            {
                misfire = SimMath.Clamp01(misfire + 0.15f);
            }

            MisfireFactor = SimMath.Clamp01(misfire);
            HasSpark = MisfireFactor < 0.92f;
        }

        /// <summary>
        /// Sets points gap during roadside or garage tune-up (optimal 0.35 mm).
        /// </summary>
        public void SetPointsGap(float gapMm = 0.35f)
        {
            PointsGapMm = SimMath.Clamp(gapMm, 0.05f, 0.90f);
            IsPointsOutOfSpec = PointsGapMm < 0.30f || PointsGapMm > 0.45f;
        }

        /// <summary>
        /// Replaces aged 6V condenser with a fresh unit.
        /// </summary>
        public void ReplaceCondenser()
        {
            CondenserHealth = 1.0f;
            IsCondenserHeatSoaked = false;
        }
    }
}
