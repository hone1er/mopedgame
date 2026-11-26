using System;

namespace MopedSim.Audio
{
    /// <summary>
    /// Mutable per-voice DSP state (oscillator phases, resonators, envelopes, seeded noise). A plain struct so the audio
    /// thread owns it by value with zero GC allocations. Event counters are exposed for deterministic tests.
    /// </summary>
    public struct TwoStrokeSynthState
    {
        internal int SampleRate;
        internal float InvSampleRate;
        internal float SmoothCoeff;
        internal bool Primed;
        internal uint Noise;

        internal double CrankPhase;
        internal double BlubberPhase;
        internal double ChatterPhase;
        internal double SquealPhase;
        internal double ThumpPhase;

        internal float SmFiringHz;
        internal float SmEngineAmp;
        internal float SmBlubberDepth;
        internal float SmResonance;
        internal float SmWind;
        internal float SmChatter;

        internal float BurstEnv;
        internal bool FireSkipped;

        internal float ResY1;
        internal float ResY2;
        internal float ResCachedHz;
        internal float ResB1;
        internal float ResB2;
        internal float ResNorm;

        internal float PingY1;
        internal float PingY2;
        internal float PingB1;
        internal float PingB2;
        internal float PingNorm;
        internal float PingExcite;

        internal float PopEnv;
        internal float PopLp;
        internal float WindLp1;
        internal float WindLp2;

        internal int FiringEvents;
        internal int SkippedFires;
        internal int PingEvents;
        internal int PopEvents;

        public int SampleRateHz { get { return SampleRate; } }
        public int FiringEventCount { get { return FiringEvents; } }
        public int SkippedFireCount { get { return SkippedFires; } }
        public int PingEventCount { get { return PingEvents; } }
        public int PopEventCount { get { return PopEvents; } }

        /// <summary>Creates a deterministic voice for the given output rate and noise seed.</summary>
        public static TwoStrokeSynthState Create(int sampleRate, uint seed)
        {
            var s = new TwoStrokeSynthState();
            s.SampleRate = sampleRate > 0 ? sampleRate : TwoStrokeDsp.FallbackSampleRate;
            s.InvSampleRate = 1f / s.SampleRate;
            // ~12 ms one-pole parameter smoothing: removes zipper noise when telemetry updates at 50-60 Hz.
            s.SmoothCoeff = 1f - (float)Math.Exp(-s.InvSampleRate / 0.012);
            s.Noise = seed != 0u ? seed : 0x9E3779B9u;
            s.ResCachedHz = -1f;
            return s;
        }
    }

    /// <summary>
    /// Pure, deterministic procedural 2-stroke synthesizer. No UnityEngine usage; runs identically inside
    /// OnAudioFilterRead and in EditMode/PlayMode tests rendering offline buffers.
    /// Layers: firing-rate harmonics + exhaust pulse grit, four-stroking blubber AM (18-35 Hz) with skipped fires,
    /// expansion-pipe high-Q ring burst, irregular detonation ping resonator, unmuffled flange-leak pops,
    /// centrifugal clutch chatter, wind roar, belt squeal and thrown-chain clatter.
    /// </summary>
    public static class TwoStrokeDsp
    {
        public const int FallbackSampleRate = 48000;

        private const double TwoPi = Math.PI * 2.0;
        private const float PipeRingRadius = 0.9992f;
        private const float PingRingRadius = 0.9985f;

        /// <summary>Synthesizes one mono sample and advances the voice state. Zero allocations.</summary>
        public static float SynthesizeSample(in TwoStrokeAudioParameters p, ref TwoStrokeSynthState s)
        {
            if (s.SampleRate <= 0)
            {
                s = TwoStrokeSynthState.Create(FallbackSampleRate, 0u);
            }

            float k = s.SmoothCoeff;
            if (!s.Primed)
            {
                s.SmFiringHz = p.FiringFrequencyHz;
                s.SmEngineAmp = p.EngineAmplitude;
                s.SmBlubberDepth = p.BlubberDepth;
                s.SmResonance = p.ResonanceGain;
                s.SmWind = p.WindRoarGain;
                s.SmChatter = p.ClutchChatterGain;
                s.Primed = true;
            }
            else
            {
                s.SmFiringHz += (p.FiringFrequencyHz - s.SmFiringHz) * k;
                s.SmEngineAmp += (p.EngineAmplitude - s.SmEngineAmp) * k;
                s.SmBlubberDepth += (p.BlubberDepth - s.SmBlubberDepth) * k;
                s.SmResonance += (p.ResonanceGain - s.SmResonance) * k;
                s.SmWind += (p.WindRoarGain - s.SmWind) * k;
                s.SmChatter += (p.ClutchChatterGain - s.SmChatter) * k;
            }

            float inv = s.InvSampleRate;
            float amp = s.SmEngineAmp;
            float sum = 0f;

            // --- Engine core: crank phase wraps once per combustion event (2-stroke fires every revolution). ---
            bool fired = false;
            if (s.SmFiringHz > 0.5f)
            {
                s.CrankPhase += s.SmFiringHz * inv;
                if (s.CrankPhase >= 1.0)
                {
                    s.CrankPhase -= Math.Floor(s.CrankPhase);
                    fired = true;
                }
            }

            if (fired && p.EngineRunning)
            {
                s.FiringEvents++;
                // Four-stroking: a rich charge fails to light every other stroke, so some combustion events are skipped.
                s.FireSkipped = s.SmBlubberDepth > 0.01f && NextUnit(ref s.Noise) < 0.35f * s.SmBlubberDepth;
                if (s.FireSkipped)
                {
                    s.SkippedFires++;
                }
                else
                {
                    s.BurstEnv = 1f;
                    if (s.SmResonance > 0.001f)
                    {
                        // Expansion chamber return wave: excite the high-Q pipe ring on each live pulse.
                        s.ResY1 += s.SmResonance;
                    }

                    if (p.PingEnergy > 0.01f && NextUnit(ref s.Noise) < 0.15f + 0.6f * p.PingEnergy)
                    {
                        s.PingEvents++;
                        float jitter = 0.92f + 0.16f * NextUnit(ref s.Noise);
                        SetResonator(TwoStrokeAudioParameters.PingFrequencyHz * jitter, PingRingRadius, inv, out s.PingB1, out s.PingB2, out s.PingNorm);
                        s.PingExcite = p.PingEnergy * (0.55f + 0.45f * NextUnit(ref s.Noise));
                    }

                    if (p.LeakPopGain > 0.01f && NextUnit(ref s.Noise) < 0.85f * p.LeakPopGain)
                    {
                        s.PopEvents++;
                        s.PopEnv = p.LeakPopGain * (0.6f + 0.4f * NextUnit(ref s.Noise));
                        s.ThumpPhase = 0.0;
                    }
                }
            }

            if (amp > 0.0001f)
            {
                double ph = s.CrankPhase * TwoPi;
                float tone = (float)(Math.Sin(ph) * 0.55 + Math.Sin(ph * 2.0) * p.Harmonic2Gain + Math.Sin(ph * 3.0) * p.Harmonic3Gain);
                if (s.FireSkipped)
                {
                    tone *= 0.35f;
                }

                float burstDecay = 1f - Math.Min(0.5f, s.SmFiringHz * inv * 12f);
                s.BurstEnv *= burstDecay;
                float engine = (tone * 0.6f + s.BurstEnv * NextNoise(ref s.Noise) * p.ExhaustNoiseGain) * amp;

                if (s.SmBlubberDepth > 0.001f)
                {
                    s.BlubberPhase += p.BlubberRateHz * inv;
                    if (s.BlubberPhase >= 1.0)
                    {
                        s.BlubberPhase -= Math.Floor(s.BlubberPhase);
                    }

                    float lfo = 0.5f + 0.5f * (float)Math.Sin(s.BlubberPhase * TwoPi);
                    engine *= 1f - s.SmBlubberDepth * lfo;
                }

                sum += engine;
            }
            else
            {
                s.BurstEnv = 0f;
            }

            // --- Expansion-pipe powerband ring (two-pole resonator, high Q -> metallic ring burst). ---
            if (s.SmResonance > 0.0005f || Math.Abs(s.ResY1) > 1e-5f)
            {
                if (Math.Abs(p.ResonanceFrequencyHz - s.ResCachedHz) > 1f)
                {
                    SetResonator(p.ResonanceFrequencyHz, PipeRingRadius, inv, out s.ResB1, out s.ResB2, out s.ResNorm);
                    s.ResCachedHz = p.ResonanceFrequencyHz;
                }

                float y = s.ResB1 * s.ResY1 - s.ResB2 * s.ResY2;
                s.ResY2 = s.ResY1;
                s.ResY1 = y;
                sum += y * s.ResNorm * 0.35f * Math.Max(amp, 0.2f);
            }

            // --- Lean pre-seize detonation ping (irregular, sharp metallic resonator impulses). ---
            if (s.PingExcite > 0f || Math.Abs(s.PingY1) > 1e-5f)
            {
                float y = s.PingB1 * s.PingY1 - s.PingB2 * s.PingY2 + s.PingExcite;
                s.PingExcite = 0f;
                s.PingY2 = s.PingY1;
                s.PingY1 = y;
                sum += y * s.PingNorm * 0.55f;
            }

            // --- Exhaust flange leak: unmuffled low-end "pop" (low-passed noise burst + 65 Hz thump). ---
            if (s.PopEnv > 0.0005f)
            {
                s.PopLp += (NextNoise(ref s.Noise) - s.PopLp) * 0.05f;
                s.ThumpPhase += TwoStrokeAudioParameters.PopThumpFrequencyHz * inv;
                float thump = (float)Math.Sin(s.ThumpPhase * TwoPi);
                sum += s.PopEnv * (s.PopLp * 1.6f + thump * 0.9f);
                s.PopEnv *= 1f - inv / 0.025f;
            }
            else
            {
                s.PopEnv = 0f;
            }

            // --- Centrifugal clutch slip chatter (square judder over shoe grind). ---
            if (s.SmChatter > 0.001f)
            {
                s.ChatterPhase += p.ClutchChatterRateHz * inv;
                if (s.ChatterPhase >= 1.0)
                {
                    s.ChatterPhase -= Math.Floor(s.ChatterPhase);
                }

                float gate = s.ChatterPhase < 0.5 ? 1f : -1f;
                sum += gate * (0.6f + 0.4f * NextNoise(ref s.Noise)) * 0.12f * s.SmChatter;
            }

            // --- Wind roar: low-passed noise whose cutoff opens with road speed. Survives a stalled coast. ---
            if (s.SmWind > 0.0005f)
            {
                float cutoff = 0.02f + 0.08f * s.SmWind;
                s.WindLp1 += (NextNoise(ref s.Noise) - s.WindLp1) * cutoff;
                s.WindLp2 += (s.WindLp1 - s.WindLp2) * cutoff;
                sum += s.WindLp2 * 2.2f * s.SmWind;
            }

            // --- Belt squeal & thrown-chain clatter (only while the engine is turning the drivetrain). ---
            if (p.EngineRunning && p.BeltSquealGain > 0.01f)
            {
                double vibrato = 1.0 + 0.01 * Math.Sin(s.CrankPhase * TwoPi);
                s.SquealPhase += (2200.0 + 600.0 * p.BeltSquealGain) * vibrato * inv;
                if (s.SquealPhase >= 1.0)
                {
                    s.SquealPhase -= Math.Floor(s.SquealPhase);
                }

                sum += (float)Math.Sin(s.SquealPhase * TwoPi) * 0.12f * p.BeltSquealGain;
            }

            if (p.EngineRunning && p.ChainClatterGain > 0.01f)
            {
                sum += NextNoise(ref s.Noise) * 0.18f * p.ChainClatterGain * (float)Math.Abs(Math.Sin(s.CrankPhase * Math.PI * 9.0));
            }

            return (float)Math.Tanh(sum * p.MasterVolume * 2.5f);
        }

        /// <summary>
        /// Fills an interleaved buffer (every channel gets the mono sample), overwriting its contents. Zero allocations.
        /// </summary>
        public static void Render(in TwoStrokeAudioParameters p, ref TwoStrokeSynthState s, float[] interleaved, int channels)
        {
            if (interleaved == null)
            {
                return;
            }

            int ch = channels > 0 ? channels : 1;
            int frames = interleaved.Length / ch;
            for (int f = 0; f < frames; f++)
            {
                float sample = SynthesizeSample(in p, ref s);
                int baseIndex = f * ch;
                for (int c = 0; c < ch; c++)
                {
                    interleaved[baseIndex + c] = sample;
                }
            }
        }

        private static void SetResonator(float frequencyHz, float radius, float invSampleRate, out float b1, out float b2, out float norm)
        {
            double maxHz = 0.45 / invSampleRate;
            double hz = frequencyHz < 20f ? 20.0 : (frequencyHz > maxHz ? maxHz : frequencyHz);
            double w = TwoPi * hz * invSampleRate;
            b1 = (float)(2.0 * radius * Math.Cos(w));
            b2 = radius * radius;
            // A two-pole resonator's impulse response peaks near 1/sin(w); normalize so a unit impulse rings at ~unity.
            norm = (float)Math.Sin(w);
        }

        private static uint Next(ref uint state)
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        private static float NextUnit(ref uint state)
        {
            return (Next(ref state) >> 8) * (1f / 16777216f);
        }

        private static float NextNoise(ref uint state)
        {
            return NextUnit(ref state) * 2f - 1f;
        }
    }
}
