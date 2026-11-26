using System;
using MopedSim.Core;

namespace MopedSim.Audio
{
    /// <summary>
    /// Immutable, deterministic audio-cue snapshot derived from one <see cref="EngineTelemetrySnapshot"/>.
    /// This is a pure presentation mapping (telemetry flags/values -> synth layer gains); every mechanical decision
    /// (four-stroking, detonation, powerband, leak severity) is already made by MopedSim.Core. No UnityEngine usage, so it is
    /// testable in EditMode/PlayMode without audio hardware.
    /// </summary>
    public readonly struct TwoStrokeAudioParameters
    {
        /// <summary>PROJECT_GUIDE.md section 2.7: rich four-stroking blubber cue threshold.</summary>
        public const float FourStrokeAfrThreshold = 11.2f;

        /// <summary>PROJECT_GUIDE.md section 2.7: lean pre-seize ping cue threshold.</summary>
        public const float PreSeizeChtThresholdCelsius = 206f;

        /// <summary>PROJECT_GUIDE.md section 2.7: detonation-risk ping cue threshold.</summary>
        public const float DetonationRiskThreshold = 0.3f;

        public const float BlubberMinHz = 18f;
        public const float BlubberMaxHz = 35f;
        public const float PingFrequencyHz = 6400f;
        public const float PopThumpFrequencyHz = 65f;
        public const float DefaultMasterVolume = 0.35f;
        public const float DefaultIdleRpm = 1700f;

        private const float RedlineRpmForTimbre = 9000f;
        private const float WindFullScaleSpeedMps = 16f;

        public readonly bool EngineRunning;
        public readonly float MasterVolume;

        /// <summary>Combustion events per second (a 2-stroke fires once per crank revolution: Rpm / 60).</summary>
        public readonly float FiringFrequencyHz;

        /// <summary>Engine layer loudness (0 when not running), driven by throttle load.</summary>
        public readonly float EngineAmplitude;

        /// <summary>Harmonic profile: 2nd/3rd order gains and exhaust-pulse noise grit, brighter with throttle and RPM.</summary>
        public readonly float Harmonic2Gain;
        public readonly float Harmonic3Gain;
        public readonly float ExhaustNoiseGain;

        /// <summary>Four-stroking amplitude-modulation depth (0..1) and its 18-35 Hz modulation rate.</summary>
        public readonly float BlubberDepth;
        public readonly float BlubberRateHz;

        /// <summary>Expansion-pipe powerband high-Q ring gain and ring frequency.</summary>
        public readonly float ResonanceGain;
        public readonly float ResonanceFrequencyHz;

        /// <summary>Lean pre-seize detonation ping impulse energy (0..1).</summary>
        public readonly float PingEnergy;

        /// <summary>Exhaust flange leak unmuffled pop gain (0..1), scaled by ExhaustLeakFactor.</summary>
        public readonly float LeakPopGain;

        /// <summary>Centrifugal clutch slip chatter gain (0..1) and judder rate.</summary>
        public readonly float ClutchChatterGain;
        public readonly float ClutchChatterRateHz;

        /// <summary>Road-speed wind roar gain (0..1). Independent of engine state so coasting with a dead engine still roars.</summary>
        public readonly float WindRoarGain;

        public readonly float BeltSquealGain;
        public readonly float ChainClatterGain;

        public TwoStrokeAudioParameters(
            bool engineRunning,
            float masterVolume,
            float firingFrequencyHz,
            float engineAmplitude,
            float harmonic2Gain,
            float harmonic3Gain,
            float exhaustNoiseGain,
            float blubberDepth,
            float blubberRateHz,
            float resonanceGain,
            float resonanceFrequencyHz,
            float pingEnergy,
            float leakPopGain,
            float clutchChatterGain,
            float clutchChatterRateHz,
            float windRoarGain,
            float beltSquealGain,
            float chainClatterGain)
        {
            EngineRunning = engineRunning;
            MasterVolume = masterVolume;
            FiringFrequencyHz = firingFrequencyHz;
            EngineAmplitude = engineAmplitude;
            Harmonic2Gain = harmonic2Gain;
            Harmonic3Gain = harmonic3Gain;
            ExhaustNoiseGain = exhaustNoiseGain;
            BlubberDepth = blubberDepth;
            BlubberRateHz = blubberRateHz;
            ResonanceGain = resonanceGain;
            ResonanceFrequencyHz = resonanceFrequencyHz;
            PingEnergy = pingEnergy;
            LeakPopGain = leakPopGain;
            ClutchChatterGain = clutchChatterGain;
            ClutchChatterRateHz = clutchChatterRateHz;
            WindRoarGain = windRoarGain;
            BeltSquealGain = beltSquealGain;
            ChainClatterGain = chainClatterGain;
        }

        /// <summary>Fully silent parameter set (engine off, stationary).</summary>
        public static TwoStrokeAudioParameters Silent
        {
            get
            {
                return new TwoStrokeAudioParameters(
                    false, DefaultMasterVolume, 0f, 0f, 0f, 0f, 0f, 0f, BlubberMinHz, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// Deterministically maps a telemetry snapshot to synth layer gains. Zero allocations.
        /// </summary>
        public static TwoStrokeAudioParameters FromTelemetry(
            in EngineTelemetrySnapshot telemetry,
            float masterVolume = DefaultMasterVolume,
            float idleRpm = DefaultIdleRpm,
            bool diagnosticHints = true)
        {
            // Mirrors the HUD convention: snapshot constructors default IsRunning=false, so derive from stall/seize + RPM.
            bool running = !telemetry.IsStalled && !telemetry.IsSeized && telemetry.Rpm > 200f;
            float throttle = Clamp01(telemetry.ThrottlePosition);
            float safeIdle = idleRpm > 1f ? idleRpm : DefaultIdleRpm;
            float rpm = running ? Math.Max(telemetry.Rpm, safeIdle * 0.6f) : 0f;
            float rpmNorm = running ? Clamp01((rpm - safeIdle) / Math.Max(1f, RedlineRpmForTimbre - safeIdle)) : 0f;

            float firingHz = rpm / 60f;
            float engineAmp = running ? 0.35f + 0.65f * throttle : 0f;
            float h2 = 0.22f + 0.18f * throttle + 0.10f * rpmNorm;
            float h3 = 0.08f + 0.16f * throttle + 0.12f * rpmNorm;
            float grit = 0.25f + 0.35f * throttle;

            float blubberDepth = 0f;
            if (running)
            {
                float afr = telemetry.EffectiveAfr;
                bool richAfr = afr > 0f && afr < FourStrokeAfrThreshold;
                if (telemetry.IsFourStroking || richAfr)
                {
                    float richness = richAfr ? Clamp01((FourStrokeAfrThreshold - afr) / 2f) : 0f;
                    blubberDepth = 0.55f + 0.35f * richness;
                }
            }

            float blubberRate = BlubberMinHz + (BlubberMaxHz - BlubberMinHz) * rpmNorm;

            float resonanceGain = running && telemetry.IsPowerbandActive ? 0.45f + 0.35f * throttle : 0f;
            float resonanceHz = 1800f + rpm * 0.12f;

            // Detonation is a combustion event: a dead or seized engine cannot ping, however hot the head is.
            float ping = 0f;
            if (running)
            {
                if (telemetry.IsDetonating)
                {
                    ping = 1f;
                }
                else if (telemetry.ChtCelsius > PreSeizeChtThresholdCelsius || telemetry.DetonationRisk > DetonationRiskThreshold)
                {
                    float chtTerm = (telemetry.ChtCelsius - PreSeizeChtThresholdCelsius) / 24f;
                    ping = Math.Max(0.15f, Clamp01(Math.Max(chtTerm, telemetry.DetonationRisk)));
                }

                if (!diagnosticHints)
                {
                    ping *= 0.6f;
                }
            }

            float leak = running ? Clamp01(telemetry.ExhaustLeakFactor) : 0f;

            float engagement = Clamp01(telemetry.ClutchEngagement);
            float chatter = 0f;
            if (running && engagement > 0.02f && engagement < 0.98f)
            {
                chatter = 4f * engagement * (1f - engagement) * Clamp01(throttle * 1.5f);
            }

            float chatterRate = 9f + 6f * engagement;

            float speedNorm = Clamp01(Math.Max(0f, telemetry.VehicleSpeedMps) / WindFullScaleSpeedMps);
            float wind = speedNorm * speedNorm;

            float squeal = telemetry.IsBeltSnapped ? 1f : Clamp01(telemetry.MisfireFactor * 0.25f);
            float chain = telemetry.IsChainThrown ? 1f : 0f;

            return new TwoStrokeAudioParameters(
                running,
                Clamp01(masterVolume),
                firingHz,
                engineAmp,
                h2,
                h3,
                grit,
                blubberDepth,
                blubberRate,
                resonanceGain,
                resonanceHz,
                ping,
                leak,
                chatter,
                chatterRate,
                wind,
                squeal,
                chain);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f || float.IsNaN(v))
            {
                return 0f;
            }

            return v > 1f ? 1f : v;
        }
    }
}
