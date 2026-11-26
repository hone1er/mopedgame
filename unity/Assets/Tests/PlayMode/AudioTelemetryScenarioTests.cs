using System.Collections;
using MopedSim.Audio;
using MopedSim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M5 exit outcome #3 (PROJECT_GUIDE.md section 7): a live TwoStrokeAudioDriver (real AudioSource component) fed by real
    /// BikeSimulation telemetry reacts to the #54 lean pre-seize ping, #88 four-stroking blubber, expansion-pipe powerband
    /// and a loose exhaust flange, allocates no GC memory per telemetry tick, and logs no console errors.
    /// Offline renders go through the same TwoStrokeDsp path OnAudioFilterRead uses, so no audio hardware is required.
    /// The Core recipes (fixture, seed, dt, input, step budget) mirror tests/MopedSim.Core.Tests so Gate 1 already proves them.
    /// </summary>
    public sealed class AudioTelemetryScenarioTests
    {
        private const float Dt = 1f / 60f;
        private const int OfflineSampleRate = 48000;

        private GameObject _audioGo;

        [TearDown]
        public void TearDown()
        {
            if (_audioGo != null)
            {
                Object.Destroy(_audioGo);
                _audioGo = null;
            }
        }

        private TwoStrokeAudioDriver CreateDriver()
        {
            _audioGo = new GameObject("TwoStrokeAudioUnderTest");
            _audioGo.AddComponent<AudioSource>();
            TwoStrokeAudioDriver driver = _audioGo.AddComponent<TwoStrokeAudioDriver>();
            driver.SetDiagnosticHints(true);
            return driver;
        }

        private static TwoStrokeSynthState RenderOffline(in TwoStrokeAudioParameters p, float seconds, float[] scratch)
        {
            TwoStrokeSynthState voice = TwoStrokeSynthState.Create(OfflineSampleRate, 0xA0D10u);
            int blocks = (int)(seconds * OfflineSampleRate / scratch.Length) + 1;
            for (int i = 0; i < blocks; i++)
            {
                TwoStrokeDsp.Render(in p, ref voice, scratch, 1);
            }

            return voice;
        }

        [UnityTest]
        public IEnumerator LeanJet54_PreSeizeDetonation_DrivesIrregularPing_ThenSilencesOnSeize()
        {
            TwoStrokeAudioDriver driver = CreateDriver();
            yield return null;

            BikeSimulation bike = MopedScenarioFixtures.Create70ccKittedBike("jet_54");
            var rng = new SeededSimRandom(101);
            var wot = new RideInputState(throttle: 1.0f);

            bool sawDetonation = false;
            bool sawSeize = false;
            float pingAtDetonation = 0f;
            TwoStrokeAudioParameters detonationParams = TwoStrokeAudioParameters.Silent;
            for (int i = 0; i < 60 * 120; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(Dt, in wot, rng);
                driver.ApplyTelemetry(in snap);
                if (!sawDetonation && snap.IsDetonating && !snap.IsSeized)
                {
                    sawDetonation = true;
                    pingAtDetonation = driver.PingCue;
                    detonationParams = driver.CurrentParameters;
                }

                if (snap.IsSoftSeized)
                {
                    sawSeize = true;
                    break;
                }
            }

            Assert.IsTrue(sawDetonation, "#54 lean 70cc at WOT must detonate before seizing");
            Assert.AreEqual(1f, pingAtDetonation, 1e-6f, "IsDetonating drives full ping energy");
            Assert.IsTrue(detonationParams.EngineRunning);

            TwoStrokeSynthState voice = RenderOffline(in detonationParams, 1f, new float[1024]);
            Assert.Greater(voice.PingEventCount, 10, "detonation telemetry must produce audible ping impulses");
            Assert.Less(voice.PingEventCount, voice.FiringEventCount, "pings are irregular, not on every combustion event");

            Assert.IsTrue(sawSeize, "#54 run ends in a soft seize");
            Assert.AreEqual(0f, driver.PingCue, "a seized engine cannot keep pinging");
            Assert.IsFalse(driver.CurrentParameters.EngineRunning);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RichJet88_FourStroking_DrivesBlubberModulation()
        {
            TwoStrokeAudioDriver driver = CreateDriver();
            yield return null;

            BikeSimulation bike = MopedScenarioFixtures.Create70ccKittedBike("jet_88");
            var rng = new SeededSimRandom(303);
            var wot = new RideInputState(throttle: 1.0f);
            for (int i = 0; i < 60 * 30; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(Dt, in wot, rng);
                driver.ApplyTelemetry(in snap);
            }

            Assert.IsTrue(driver.LastTelemetry.IsFourStroking, "#88 on the 70cc kit must four-stroke");
            Assert.Less(driver.LastTelemetry.EffectiveAfr, TwoStrokeAudioParameters.FourStrokeAfrThreshold);
            Assert.Greater(driver.FourStrokeCue, 0.5f);

            TwoStrokeAudioParameters p = driver.CurrentParameters;
            Assert.GreaterOrEqual(p.BlubberRateHz, TwoStrokeAudioParameters.BlubberMinHz);
            Assert.LessOrEqual(p.BlubberRateHz, TwoStrokeAudioParameters.BlubberMaxHz);
            Assert.AreEqual(0f, p.PingEnergy, "a rich, cool engine does not ping");

            TwoStrokeSynthState voice = RenderOffline(in p, 1f, new float[1024]);
            Assert.Greater(voice.SkippedFireCount, 0, "four-stroking skips combustion events");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ExpansionChamber_EnteringPowerband_DrivesPipeRing()
        {
            TwoStrokeAudioDriver driver = CreateDriver();
            yield return null;

            var catalog = MopedScenarioFixtures.CreateCatalog();
            BikeSimulation bike = MopedScenarioFixtures.Create70ccKittedBike("jet_72");
            Assert.IsTrue(bike.TrySwapPart(SlotIds.Exhaust, new PartInstance(catalog["pipe_topend_expansion"])).Succeeded);

            var rng = new SeededSimRandom(1414);
            var wot = new RideInputState(throttle: 1.0f);
            bool hitPowerband = false;
            int ticksBelowPowerband = 0;
            float maxCueBelowPowerband = 0f;
            for (int i = 0; i < 60 * 25; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(Dt, in wot, rng);
                driver.ApplyTelemetry(in snap);
                if (snap.IsPowerbandActive)
                {
                    hitPowerband = true;
                    break;
                }

                ticksBelowPowerband++;
                maxCueBelowPowerband = Mathf.Max(maxCueBelowPowerband, driver.PowerbandCue);
            }

            Assert.IsTrue(hitPowerband, "expansion chamber must come on the pipe at WOT");
            Assert.Greater(ticksBelowPowerband, 0, "the run should start below the powerband");
            Assert.AreEqual(0f, maxCueBelowPowerband, "no pipe ring below the powerband");
            Assert.Greater(driver.PowerbandCue, 0.4f);
            Assert.Greater(driver.CurrentParameters.ResonanceFrequencyHz, 1800f);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator LooseExhaustFlange_DrivesLowEndPops_ScaledByLeakFactor()
        {
            TwoStrokeAudioDriver driver = CreateDriver();
            yield return null;

            var cruise = new RideInputState(throttle: 0.5f);
            BikeSimulation healthy = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var rngHealthy = new SeededSimRandom(2121);
            for (int i = 0; i < 120; i++)
            {
                EngineTelemetrySnapshot snap = healthy.Step(Dt, in cruise, rngHealthy);
                driver.ApplyTelemetry(in snap);
            }

            Assert.IsTrue(driver.CurrentParameters.EngineRunning);
            Assert.AreEqual(0f, driver.LeakCue, "a sealed exhaust flange does not pop");

            BikeSimulation leaky = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            leaky.InduceFault(FaultIds.ExhaustLoose);
            var rngLeaky = new SeededSimRandom(2121);
            for (int i = 0; i < 120; i++)
            {
                EngineTelemetrySnapshot snap = leaky.Step(Dt, in cruise, rngLeaky);
                driver.ApplyTelemetry(in snap);
            }

            float leakFactor = driver.LastTelemetry.ExhaustLeakFactor;
            Assert.Greater(leakFactor, 0.1f, "loose flange nuts must leak");
            Assert.IsTrue(driver.CurrentParameters.EngineRunning);
            Assert.AreEqual(leakFactor, driver.LeakCue, 1e-5f, "pop gain is scaled by ExhaustLeakFactor");

            TwoStrokeSynthState voice = RenderOffline(driver.CurrentParameters, 1f, new float[1024]);
            Assert.Greater(voice.PopEventCount, 0, "a leaking flange produces unmuffled pops");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ApplyTelemetry_AndDspRender_AllocateNoGcMemory()
        {
            TwoStrokeAudioDriver driver = CreateDriver();
            yield return null;

            BikeSimulation bike = MopedScenarioFixtures.Create70ccKittedBike("jet_88");
            var rng = new SeededSimRandom(77);
            var wot = new RideInputState(throttle: 1.0f);
            for (int i = 0; i < 240; i++)
            {
                EngineTelemetrySnapshot warm = bike.Step(Dt, in wot, rng);
                driver.ApplyTelemetry(in warm);
                driver.UpdateTelemetry(in warm);
            }

            EngineTelemetrySnapshot t = bike.LatestTelemetry;
            int before = driver.TelemetryApplyCount;
            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    driver.ApplyTelemetry(in t);
                    driver.UpdateTelemetry(in t);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(before + 480, driver.TelemetryApplyCount);

            // The audio-thread path (OnAudioFilterRead -> TwoStrokeDsp.Render) must be allocation-free as well.
            TwoStrokeAudioParameters p = driver.CurrentParameters;
            var buffer = new float[2048];
            TwoStrokeSynthState voice = TwoStrokeSynthState.Create(OfflineSampleRate, 9u);
            TwoStrokeDsp.Render(in p, ref voice, buffer, 2);
            Assert.That(() =>
            {
                for (int i = 0; i < 16; i++)
                {
                    TwoStrokeDsp.Render(in p, ref voice, buffer, 2);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.Greater(driver.CurrentRpmCue, 0f);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
