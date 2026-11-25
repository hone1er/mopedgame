#nullable enable

using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Evaluates piecewise-linear exhaust torque curves, powerband surge, carbon baffle clogging, and flange leaks.
    /// </summary>
    public sealed class ExhaustSystem
    {
        public float TorqueMultiplier { get; private set; } = 1.0f;
        public bool IsPowerbandActive { get; private set; }
        public float CarbonClog { get; set; }
        public float ExhaustLeakFactor { get; private set; }

        /// <summary>
        /// Recomputes cached ExhaustLeakFactor immediately after wrenching, repair, or fault injection.
        /// </summary>
        public void RefreshExhaustLeakFactor(bool hasExhaustPart, float assemblyExhaustLeakSeverity)
        {
            ExhaustLeakFactor = hasExhaustPart ? SimMath.Clamp01(assemblyExhaustLeakSeverity) : 1.0f;
        }

        /// <summary>
        /// Evaluates exhaust scavenging multiplier, powerband activity, and carbon accumulation.
        /// </summary>
        public void Step(
            float dt,
            float rpm,
            float throttle,
            float effectiveAfr,
            float premixRatio,
            bool isRunning,
            PartInstance? exhaustPart,
            float assemblyExhaustLeakSeverity)
        {
            RefreshExhaustLeakFactor(exhaustPart != null, assemblyExhaustLeakSeverity);

            if (exhaustPart == null)
            {
                TorqueMultiplier = 0.72f;
                IsPowerbandActive = false;
                return;
            }

            PartStats stats = exhaustPart.Definition.Stats;
            TorqueMultiplier = EvaluateTorqueCurve(stats.TorqueCurve, rpm);

            IsPowerbandActive = isRunning
                && throttle > 0.45f
                && rpm >= stats.PeakRpmStart
                && rpm <= stats.PeakRpmEnd
                && TorqueMultiplier >= 1.05f;

            // Carbon baffle buildup if running very rich or heavy premix oil
            if (dt > 0f && isRunning && rpm > 1000f)
            {
                float carbonRate = 0f;
                if (effectiveAfr < 11.4f)
                {
                    carbonRate += (11.4f - effectiveAfr) * 0.0012f;
                }
                if (premixRatio > 0.030f)
                {
                    carbonRate += (premixRatio - 0.030f) * 0.08f;
                }
                if (carbonRate > 0f)
                {
                    CarbonClog = SimMath.Clamp01(CarbonClog + carbonRate * dt);
                    exhaustPart.CarbonAccumulation = CarbonClog;
                }
            }
        }

        /// <summary>
        /// Linearly interpolates piecewise torque curve multipliers keyed by RPM.
        /// </summary>
        public static float EvaluateTorqueCurve(IReadOnlyList<TorqueCurvePoint> curve, float rpm)
        {
            if (curve == null || curve.Count == 0)
            {
                return 1.0f;
            }
            if (curve.Count == 1)
            {
                return curve[0].Multiplier;
            }
            if (rpm <= curve[0].Rpm)
            {
                return curve[0].Multiplier;
            }
            int last = curve.Count - 1;
            if (rpm >= curve[last].Rpm)
            {
                return curve[last].Multiplier;
            }

            for (int i = 0; i < last; i++)
            {
                TorqueCurvePoint a = curve[i];
                TorqueCurvePoint b = curve[i + 1];
                if (rpm >= a.Rpm && rpm <= b.Rpm)
                {
                    float span = b.Rpm - a.Rpm;
                    if (span <= 0.001f)
                    {
                        return b.Multiplier;
                    }
                    float t = (rpm - a.Rpm) / span;
                    return a.Multiplier + (b.Multiplier - a.Multiplier) * t;
                }
            }

            return 1.0f;
        }
    }
}
