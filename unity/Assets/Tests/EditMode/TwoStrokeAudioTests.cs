using System;
using MopedSim.Audio;
using MopedSim.Core;
using NUnit.Framework;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// M5 exit outcome #3 (PROJECT_GUIDE.md section 7): MopedSim.Audio reacts deterministically to telemetry. Exercises the
    /// pure TwoStrokeAudioParameters mapping and TwoStrokeDsp offline buffers; no audio hardware or Awake required.
    /// </summary>
    public sealed class TwoStrokeAudioTests
    {
        private const int SampleRate = 48000;
        private const uint Seed = 0xC0FFEEu;

        private static EngineTelemetrySnapshot Snapshot(
            float rpm,
            float throttle,
            float afr = 12.8f,
            float cht = 150f,
            bool fourStroking = false,
            bool detonating = false,
            bool powerband = false,
            bool stalled = false,
            bool seized = false,
            float exhaustLeak = 0f,
            float clutchEngagement = 1f,
            float speedMps = 0f,
            float detonationRisk = 0f)
        {
            return new EngineTelemetrySnapshot(
                rpm: rpm,
                rearWheelTorqueNm: 0f,
                engineBrakeTorqueNm: 0f,
                effectiveAfr: afr,
                chtCelsius: cht,
                fuelLitersRemaining: 2f,
                isReserveActive: false,
                isFourStroking: fourStroking,
                isDetonating: detonating,
                isPowerbandActive: powerband,
                isStalled: stalled,
                isSeized: seized,
                exhaustLeakFactor: exhaustLeak,
                headlightOutput: 0f,
                currentGear: 1,
                throttlePosition: throttle,
                vehicleSpeedMps: speedMps,
                detonationRisk: detonationRisk,
                clutchEngagement: clutchEngagement);
        }

        private static float[] Render(in TwoStrokeAudioParameters p, float seconds, uint seed = Seed)
        {
            var buffer = new float[(int)(SampleRate * seconds)];
            TwoStrokeSynthState state = TwoStrokeSynthState.Create(SampleRate, seed);
            TwoStrokeDsp.Render(in p, ref state, buffer, 1);
            return buffer;
        }

        private static TwoStrokeSynthState RenderState(in TwoStrokeAudioParameters p, float seconds, uint seed = Seed)
        {
            var buffer = new float[(int)(SampleRate * seconds)];
            TwoStrokeSynthState state = TwoStrokeSynthState.Create(SampleRate, seed);
            TwoStrokeDsp.Render(in p, ref state, buffer, 1);
            return state;
        }

        /// <summary>Normalized Goertzel power of one frequency bin over a buffer region (mean removed).</summary>
        private static double BandPower(float[] x, int start, double hz, bool energyEnvelope = false)
        {
            int count = x.Length - start;
            double mean = 0.0;
            for (int i = start; i < x.Length; i++)
            {
                mean += energyEnvelope ? x[i] * (double)x[i] : x[i];
            }

            mean /= count;
            double w = 2.0 * Math.PI * hz / SampleRate;
            double coeff = 2.0 * Math.Cos(w);
            double s1 = 0.0;
            double s2 = 0.0;
            for (int i = start; i < x.Length; i++)
            {
                double v = (energyEnvelope ? x[i] * (double)x[i] : x[i]) - mean;
                double s0 = v + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return power / ((double)count * count);
        }

        private static double Rms(float[] x, int start)
        {
            double acc = 0.0;
            for (int i = start; i < x.Length; i++)
            {
                acc += x[i] * (double)x[i];
            }

            return Math.Sqrt(acc / (x.Length - start));
        }

        [Test]
        public void Parameters_AreDeterministic_ForIdenticalTelemetry()
        {
            EngineTelemetrySnapshot t = Snapshot(6200f, 0.9f, afr: 10.4f, cht: 214f, fourStroking: true, powerband: true, exhaustLeak: 0.4f, speedMps: 9f);
            TwoStrokeAudioParameters a = TwoStrokeAudioParameters.FromTelemetry(in t);
            TwoStrokeAudioParameters b = TwoStrokeAudioParameters.FromTelemetry(in t);
            Assert.AreEqual(a.FiringFrequencyHz, b.FiringFrequencyHz);
            Assert.AreEqual(a.BlubberDepth, b.BlubberDepth);
            Assert.AreEqual(a.ResonanceGain, b.ResonanceGain);
            Assert.AreEqual(a.PingEnergy, b.PingEnergy);
            Assert.AreEqual(a.LeakPopGain, b.LeakPopGain);
            Assert.AreEqual(a.WindRoarGain, b.WindRoarGain);

            float[] bufA = Render(in a, 0.25f);
            float[] bufB = Render(in b, 0.25f);
            for (int i = 0; i < bufA.Length; i++)
            {
                Assert.AreEqual(BitConverter.SingleToInt32Bits(bufA[i]), BitConverter.SingleToInt32Bits(bufB[i]), "sample " + i + " diverged");
            }
        }

        [Test]
        public void IdleVsWot_FiringFrequencyAndHarmonicProfileTrackRpmAndThrottle()
        {
            EngineTelemetrySnapshot idleT = Snapshot(1700f, 0f, clutchEngagement: 0f);
            EngineTelemetrySnapshot wotT = Snapshot(7000f, 1f);
            TwoStrokeAudioParameters idle = TwoStrokeAudioParameters.FromTelemetry(in idleT);
            TwoStrokeAudioParameters wot = TwoStrokeAudioParameters.FromTelemetry(in wotT);

            Assert.AreEqual(1700f / 60f, idle.FiringFrequencyHz, 1e-3f, "2-stroke fires once per revolution");
            Assert.AreEqual(7000f / 60f, wot.FiringFrequencyHz, 1e-3f);
            Assert.Greater(wot.EngineAmplitude, idle.EngineAmplitude);
            Assert.Greater(wot.Harmonic2Gain, idle.Harmonic2Gain);
            Assert.Greater(wot.Harmonic3Gain, idle.Harmonic3Gain);
            Assert.Greater(wot.ExhaustNoiseGain, idle.ExhaustNoiseGain);

            float[] idleBuf = Render(in idle, 1f);
            float[] wotBuf = Render(in wot, 1f);
            int skip = SampleRate / 10;
            double idleFund = BandPower(idleBuf, skip, idle.FiringFrequencyHz);
            double wotFund = BandPower(wotBuf, skip, wot.FiringFrequencyHz);

            Assert.Greater(idleFund, BandPower(idleBuf, skip, wot.FiringFrequencyHz) * 4.0, "idle energy should sit at the idle firing rate");
            Assert.Greater(wotFund, BandPower(wotBuf, skip, idle.FiringFrequencyHz) * 4.0, "WOT energy should sit at the WOT firing rate");
            Assert.Greater(Rms(wotBuf, skip), Rms(idleBuf, skip) * 1.3, "WOT must be louder than idle");
        }

        [Test]
        public void FourStroking_AddsBlubberAmplitudeModulation_In18To35HzBand()
        {
            EngineTelemetrySnapshot cleanT = Snapshot(5200f, 1f, afr: 12.6f);
            EngineTelemetrySnapshot richT = Snapshot(5200f, 1f, afr: 10.1f, fourStroking: true);
            TwoStrokeAudioParameters clean = TwoStrokeAudioParameters.FromTelemetry(in cleanT);
            TwoStrokeAudioParameters rich = TwoStrokeAudioParameters.FromTelemetry(in richT);

            Assert.AreEqual(0f, clean.BlubberDepth);
            Assert.Greater(rich.BlubberDepth, 0.5f);
            Assert.GreaterOrEqual(rich.BlubberRateHz, TwoStrokeAudioParameters.BlubberMinHz);
            Assert.LessOrEqual(rich.BlubberRateHz, TwoStrokeAudioParameters.BlubberMaxHz);

            // AFR below 11.2 alone (flag not yet latched by Core) still blubbers; a stalled engine never does.
            EngineTelemetrySnapshot afrOnlyT = Snapshot(5200f, 1f, afr: 10.9f);
            EngineTelemetrySnapshot deadT = Snapshot(0f, 0f, afr: 0f, stalled: true);
            Assert.Greater(TwoStrokeAudioParameters.FromTelemetry(in afrOnlyT).BlubberDepth, 0.5f);
            Assert.AreEqual(0f, TwoStrokeAudioParameters.FromTelemetry(in deadT).BlubberDepth);

            float[] cleanBuf = Render(in clean, 1.5f);
            float[] richBuf = Render(in rich, 1.5f);
            int skip = SampleRate / 10;
            double cleanEnv = BandPower(cleanBuf, skip, rich.BlubberRateHz, true);
            double richEnv = BandPower(richBuf, skip, rich.BlubberRateHz, true);
            Assert.Greater(richEnv, cleanEnv * 8.0, "rich mixture must amplitude-modulate the engine at the blubber rate");
            Assert.Greater(RenderState(in rich, 1.5f).SkippedFireCount, 0, "four-stroking skips combustion events");
            Assert.AreEqual(0, RenderState(in clean, 1.5f).SkippedFireCount);
        }

        [Test]
        public void PowerbandActive_AddsHighQPipeRing_AtResonanceFrequency()
        {
            EngineTelemetrySnapshot offT = Snapshot(7400f, 1f);
            EngineTelemetrySnapshot onT = Snapshot(7400f, 1f, powerband: true);
            TwoStrokeAudioParameters off = TwoStrokeAudioParameters.FromTelemetry(in offT);
            TwoStrokeAudioParameters on = TwoStrokeAudioParameters.FromTelemetry(in onT);

            Assert.AreEqual(0f, off.ResonanceGain);
            Assert.Greater(on.ResonanceGain, 0.4f);
            Assert.Greater(on.ResonanceFrequencyHz, 1800f);

            float[] offBuf = Render(in off, 1f);
            float[] onBuf = Render(in on, 1f);
            int skip = SampleRate / 10;
            Assert.Greater(BandPower(onBuf, skip, on.ResonanceFrequencyHz), BandPower(offBuf, skip, on.ResonanceFrequencyHz) * 10.0,
                "powerband must ring the expansion chamber at its resonance");
        }

        [Test]
        public void LeanPreSeize_TriggersIrregularMetallicPing_OnlyWhileCombusting()
        {
            EngineTelemetrySnapshot coolT = Snapshot(6800f, 1f, cht: 170f);
            EngineTelemetrySnapshot detT = Snapshot(6800f, 1f, afr: 15.2f, cht: 224f, detonating: true);
            EngineTelemetrySnapshot hotT = Snapshot(6800f, 1f, cht: 212f);
            EngineTelemetrySnapshot riskT = Snapshot(6800f, 1f, cht: 180f, detonationRisk: 0.45f);
            EngineTelemetrySnapshot seizedHotT = Snapshot(0f, 0f, cht: 236f, seized: true);
            TwoStrokeAudioParameters cool = TwoStrokeAudioParameters.FromTelemetry(in coolT);
            TwoStrokeAudioParameters det = TwoStrokeAudioParameters.FromTelemetry(in detT);

            Assert.AreEqual(0f, cool.PingEnergy);
            Assert.AreEqual(1f, det.PingEnergy);
            Assert.Greater(TwoStrokeAudioParameters.FromTelemetry(in hotT).PingEnergy, 0f, "CHT > 206 C pings");
            Assert.Greater(TwoStrokeAudioParameters.FromTelemetry(in riskT).PingEnergy, 0f, "DetonationRisk > 0.3 pings");
            Assert.AreEqual(0f, TwoStrokeAudioParameters.FromTelemetry(in seizedHotT).PingEnergy, "a seized engine cannot detonate");
            Assert.Less(TwoStrokeAudioParameters.FromTelemetry(in detT, diagnosticHints: false).PingEnergy, det.PingEnergy);

            float[] coolBuf = Render(in cool, 1f);
            float[] detBuf = Render(in det, 1f);
            int skip = SampleRate / 10;
            Assert.Greater(BandPower(detBuf, skip, TwoStrokeAudioParameters.PingFrequencyHz),
                BandPower(coolBuf, skip, TwoStrokeAudioParameters.PingFrequencyHz) * 5.0, "ping must add metallic high-frequency energy");

            TwoStrokeSynthState detState = RenderState(in det, 1f);
            Assert.AreEqual(0, RenderState(in cool, 1f).PingEventCount);
            Assert.Greater(detState.PingEventCount, 10);
            Assert.Less(detState.PingEventCount, detState.FiringEventCount, "pings are irregular, not on every combustion event");
        }

        [Test]
        public void ExhaustFlangeLeak_AddsLowEndPopPulses_ScaledByLeakFactor()
        {
            EngineTelemetrySnapshot sealedT = Snapshot(3000f, 0.5f, exhaustLeak: 0f);
            EngineTelemetrySnapshot smallT = Snapshot(3000f, 0.5f, exhaustLeak: 0.2f);
            EngineTelemetrySnapshot looseT = Snapshot(3000f, 0.5f, exhaustLeak: 0.84f);
            TwoStrokeAudioParameters sealedP = TwoStrokeAudioParameters.FromTelemetry(in sealedT);
            TwoStrokeAudioParameters small = TwoStrokeAudioParameters.FromTelemetry(in smallT);
            TwoStrokeAudioParameters loose = TwoStrokeAudioParameters.FromTelemetry(in looseT);

            Assert.AreEqual(0f, sealedP.LeakPopGain);
            Assert.AreEqual(0.84f, loose.LeakPopGain, 1e-5f);
            Assert.Greater(loose.LeakPopGain, small.LeakPopGain);

            int skip = SampleRate / 10;
            float[] sealedBuf = Render(in sealedP, 1f);
            float[] looseBuf = Render(in loose, 1f);
            Assert.Greater(BandPower(looseBuf, skip, TwoStrokeAudioParameters.PopThumpFrequencyHz),
                BandPower(sealedBuf, skip, TwoStrokeAudioParameters.PopThumpFrequencyHz) * 10.0, "leak pops must add low-end thump energy");

            int sealedPops = RenderState(in sealedP, 1f).PopEventCount;
            int smallPops = RenderState(in small, 1f).PopEventCount;
            int loosePops = RenderState(in loose, 1f).PopEventCount;
            Assert.AreEqual(0, sealedPops);
            Assert.Greater(smallPops, 0);
            Assert.Greater(loosePops, smallPops, "pop rate scales with ExhaustLeakFactor");
        }

        [Test]
        public void ClutchChatter_AndWindRoar_TrackDrivetrainAndRoadSpeed()
        {
            EngineTelemetrySnapshot lockedT = Snapshot(5000f, 0.8f, clutchEngagement: 1f);
            EngineTelemetrySnapshot slippingT = Snapshot(3200f, 0.8f, clutchEngagement: 0.5f);
            EngineTelemetrySnapshot openT = Snapshot(1700f, 0f, clutchEngagement: 0f);
            Assert.AreEqual(0f, TwoStrokeAudioParameters.FromTelemetry(in lockedT).ClutchChatterGain);
            Assert.AreEqual(0f, TwoStrokeAudioParameters.FromTelemetry(in openT).ClutchChatterGain);
            Assert.Greater(TwoStrokeAudioParameters.FromTelemetry(in slippingT).ClutchChatterGain, 0.5f);

            EngineTelemetrySnapshot slowT = Snapshot(4000f, 0.6f, speedMps: 4f);
            EngineTelemetrySnapshot fastT = Snapshot(4000f, 0.6f, speedMps: 13f);
            EngineTelemetrySnapshot coastDeadT = Snapshot(0f, 0f, stalled: true, speedMps: 13f);
            TwoStrokeAudioParameters slow = TwoStrokeAudioParameters.FromTelemetry(in slowT);
            TwoStrokeAudioParameters fast = TwoStrokeAudioParameters.FromTelemetry(in fastT);
            TwoStrokeAudioParameters coastDead = TwoStrokeAudioParameters.FromTelemetry(in coastDeadT);
            Assert.Greater(fast.WindRoarGain, slow.WindRoarGain);
            Assert.AreEqual(fast.WindRoarGain, coastDead.WindRoarGain, 1e-6f, "wind roar is independent of engine state");
            Assert.IsFalse(coastDead.EngineRunning);

            float[] coastBuf = Render(in coastDead, 0.5f);
            Assert.Greater(Rms(coastBuf, SampleRate / 10), 0.005, "a stalled coast at speed still roars");
        }

        [Test]
        public void StalledAndStationary_IsSilent_AndOutputStaysBounded()
        {
            EngineTelemetrySnapshot deadT = Snapshot(0f, 0f, stalled: true);
            TwoStrokeAudioParameters dead = TwoStrokeAudioParameters.FromTelemetry(in deadT);
            Assert.IsFalse(dead.EngineRunning);
            Assert.AreEqual(0f, dead.FiringFrequencyHz);
            Assert.AreEqual(0.0, Rms(Render(in dead, 0.25f), 0), 1e-9);

            EngineTelemetrySnapshot everythingT = Snapshot(8200f, 1f, afr: 9.8f, cht: 240f, fourStroking: true, detonating: true,
                powerband: true, exhaustLeak: 1f, clutchEngagement: 0.5f, speedMps: 20f);
            TwoStrokeAudioParameters everything = TwoStrokeAudioParameters.FromTelemetry(in everythingT, masterVolume: 1f);
            float[] loud = Render(in everything, 1f);
            for (int i = 0; i < loud.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(loud[i]) || float.IsInfinity(loud[i]), "non-finite sample at " + i);
                Assert.LessOrEqual(Math.Abs(loud[i]), 1f);
            }
        }

        [Test]
        public void Render_FillsEveryInterleavedChannel_WithTheMonoSample()
        {
            EngineTelemetrySnapshot t = Snapshot(4500f, 0.7f, powerband: true);
            TwoStrokeAudioParameters p = TwoStrokeAudioParameters.FromTelemetry(in t);
            var stereo = new float[2048];
            TwoStrokeSynthState stereoState = TwoStrokeSynthState.Create(SampleRate, Seed);
            TwoStrokeDsp.Render(in p, ref stereoState, stereo, 2);

            TwoStrokeSynthState monoState = TwoStrokeSynthState.Create(SampleRate, Seed);
            for (int f = 0; f < stereo.Length / 2; f++)
            {
                float mono = TwoStrokeDsp.SynthesizeSample(in p, ref monoState);
                Assert.AreEqual(mono, stereo[f * 2]);
                Assert.AreEqual(mono, stereo[f * 2 + 1]);
            }
        }
    }
}
