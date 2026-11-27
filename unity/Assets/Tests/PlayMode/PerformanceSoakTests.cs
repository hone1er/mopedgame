using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MopedSim.App;
using MopedSim.Garage;
using MopedSim.World;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M7 frame-stability soak on the real generated scenes: boots Bootstrap (+ Garage, UI_Overlay, Zone_WestOakland), presses F1,
    /// then rides with throttle on and a gentle steer pattern for MOPED_SOAK_SECONDS game seconds (default 90; set 3600 for the
    /// full 60-minute soak). Every 30 game seconds an extra zone scene is streamed in and out to exercise additive load/unload.
    /// Samples ProfilerRecorder (main thread time, GC allocated in frame, total used memory) plus per-frame unscaled deltas, asserts
    /// steady-state GC / memory-growth / worst-frame / console budgets, and writes a JSON summary to logs/perf/.
    /// Presentation-only: no simulation math lives here.
    /// </summary>
    public sealed class PerformanceSoakTests
    {
        private const float DefaultSoakSeconds = 90f;
        private const float WarmupSeconds = 8f;
        private const float ChurnIntervalSeconds = 30f;
        private const float ChurnSettleSeconds = 3f;
        private const string WorkingZone = "Zone_WestOakland";
        private const string ChurnZone = "Zone_OaklandHills";
        private const int MaxBootFrames = 1800;

        // Editor play mode (test runner coroutine + editor overhead) is far noisier than a player; budgets are generous.
        private const float DefaultMaxSteadyGcBytesP95 = 8192f;
        private const float DefaultMaxMemoryGrowthMb = 128f;
        private const float WorstFrameBoundSeconds = 1.0f;

        private static readonly string[] RuntimeScenes =
        {
            "Bootstrap", "Garage", "UI_Overlay", "Zone_WestOakland", "Zone_OaklandHills", "Zone_BayCrossing", "Zone_SF_Mission"
        };

        private SimulationMode _previousMode;
        private int _previousTargetFrameRate;
        private int _previousVSyncCount;
        private static double s_churnWorstFrameSeconds;

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            _previousTargetFrameRate = Application.targetFrameRate;
            _previousVSyncCount = QualitySettings.vSyncCount;
            Application.targetFrameRate = 60;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = RuntimeScenes.Length - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneByName(RuntimeScenes[i]);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                    while (op != null && !op.isDone)
                    {
                        yield return null;
                    }
                }
            }

            Physics.simulationMode = _previousMode;
            Application.targetFrameRate = _previousTargetFrameRate;
            Time.captureFramerate = 0;
            QualitySettings.vSyncCount = _previousVSyncCount;
        }

        [UnityTest]
        [Timeout(14400000)]
        public IEnumerator RideSoak_FrameTimeGcAndMemory_StayWithinBudget()
        {
            float soakSeconds = ReadEnvFloat("MOPED_SOAK_SECONDS", DefaultSoakSeconds);
            float maxGcP95 = ReadEnvFloat("MOPED_SOAK_MAX_GC_BYTES_P95", DefaultMaxSteadyGcBytesP95);
            float maxGrowthMb = ReadEnvFloat("MOPED_SOAK_MAX_MEMORY_GROWTH_MB", DefaultMaxMemoryGrowthMb);
            Assert.Greater(soakSeconds, WarmupSeconds + 30f, "MOPED_SOAK_SECONDS must leave room for warmup plus two non-overlapping 10 s memory windows.");

            AsyncOperation boot = SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Additive);
            while (!boot.isDone)
            {
                yield return null;
            }

            AppCompositionRoot app = FindInScene<AppCompositionRoot>("Bootstrap");
            Assert.IsNotNull(app, "Bootstrap.unity must contain the AppCompositionRoot.");
            int frames = 0;
            while ((!app.IsBootstrapped || app.Zones == null || !app.Zones.IsZoneLoaded(WorkingZone)) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(app.IsBootstrapped, "App did not bootstrap from the real catalogs.");
            Assert.IsTrue(app.Zones.IsZoneLoaded(WorkingZone), WorkingZone + " did not stream in.");
            BikeVisualAssembler assembler = FindInScene<BikeVisualAssembler>("Garage");
            frames = 0;
            while (assembler != null && (assembler.IsSyncing || assembler.MountedPartCount == 0) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            InputAdapter input = app.Input;
            Assert.IsNotNull(input);
            input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, true));
            yield return null; // AppCompositionRoot.Update consumes the F1 press exactly once.
            input.SetTestOverride(default(FrameControlInputs));
            yield return null;
            Assert.AreEqual(GameSessionState.Ride, app.Session.CurrentState, "F1 must start the ride.");

            // Headless batch mode ignores targetFrameRate (frames run uncapped), so pin game time to 60 Hz with captureFramerate instead:
            // each frame advances exactly 1/60 game second and the measured frame time is the real wall-clock cost of that frame.
            QualitySettings.vSyncCount = 0;
            Time.captureFramerate = 60;

            ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            ProfilerRecorder gcInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            ProfilerRecorder usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            try
            {
                int capacity = Mathf.Clamp((int)(soakSeconds * 130f), 4096, 2000000);
                List<float> frameMs = new List<float>(capacity);
                List<float> mainMs = new List<float>(capacity);
                List<float> gcBytes = new List<float>(capacity);
                List<float> memStart = new List<float>(1024);
                List<float> memEnd = new List<float>(1024);
                float peakMemoryMb = 0f;
                int churnCycles = 0;
                float nextChurn = WarmupSeconds + ChurnIntervalSeconds;
                float settleUntil = 0f;
                float worstFrame = 0f;
                float worstChurnFrame = 0f;
                bool useRecorders = false;
                bool sourceDecided = false;
                long lastManagedAlloc = GC.GetAllocatedBytesForCurrentThread();

                double lastRealtime = Time.realtimeSinceStartupAsDouble;
                float t0 = Time.time;
                float steerPhase = 0f;
                while (Time.time - t0 < soakSeconds)
                {
                    float elapsed = Time.time - t0;
                    steerPhase += Time.unscaledDeltaTime;
                    float steer = 0.35f * Mathf.Sin(steerPhase * 0.6f);
                    input.SetTestOverride(new FrameControlInputs(0.9f, 0f, 0f, steer, false, true, false, false, false, false));

                    if (elapsed >= nextChurn)
                    {
                        // Stream a neighbouring zone in then out; frames around the load/unload are excluded from GC/frame stats.
                        nextChurn += ChurnIntervalSeconds;
                        s_churnWorstFrameSeconds = 0d;
                        yield return ChurnZoneOnce(app.Zones);
                        if (s_churnWorstFrameSeconds > 0d)
                        {
                            churnCycles++;
                            worstChurnFrame = Mathf.Max(worstChurnFrame, (float)s_churnWorstFrameSeconds);
                        }

                        lastRealtime = Time.realtimeSinceStartupAsDouble;
                        settleUntil = (Time.time - t0) + ChurnSettleSeconds;
                        continue;
                    }

                    yield return null;
                    double nowRealtime = Time.realtimeSinceStartupAsDouble;
                    float realFrameSeconds = (float)(nowRealtime - lastRealtime);
                    lastRealtime = nowRealtime;
                    elapsed = Time.time - t0;
                    // ProfilerRecorder counters read 0 in batch-mode/-nographics editor runs; fall back to the managed-heap and
                    // native allocator counters, which are always live. recorderReportedNonZero in the summary says which source fed each series.
                    long managedAlloc = GC.GetAllocatedBytesForCurrentThread();
                    float managedDelta = managedAlloc - lastManagedAlloc;
                    lastManagedAlloc = managedAlloc;
                    if (!sourceDecided && elapsed >= WarmupSeconds)
                    {
                        // Pick the counter source once for the whole run so series are never mixed.
                        sourceDecided = true;
                        useRecorders = usedMemory.Valid && usedMemory.LastValue > 0L && gcInFrame.Valid;
                    }

                    long usedBytes = useRecorders ? usedMemory.LastValue
                        : UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() + GC.GetTotalMemory(false);
                    float memMb = usedBytes / (1024f * 1024f);
                    peakMemoryMb = Mathf.Max(peakMemoryMb, memMb);
                    if (elapsed < WarmupSeconds)
                    {
                        continue;
                    }

                    if (elapsed < WarmupSeconds + 10f)
                    {
                        memStart.Add(memMb);
                    }

                    if (elapsed > soakSeconds - 10f)
                    {
                        memEnd.Add(memMb);
                    }

                    if (elapsed < settleUntil)
                    {
                        continue;
                    }

                    frameMs.Add(realFrameSeconds * 1000f);
                    worstFrame = Mathf.Max(worstFrame, realFrameSeconds);
                    long mainNs = mainThread.Valid ? mainThread.LastValue : 0L;
                    mainMs.Add(mainNs / 1.0e6f);
                    gcBytes.Add(useRecorders ? gcInFrame.LastValue : managedDelta);
                }

                input.SetTestOverride(default(FrameControlInputs));

                Assert.Greater(frameMs.Count, 100, "Too few steady-state frames were sampled.");
                frameMs.Sort();
                mainMs.Sort();
                gcBytes.Sort();
                float memStartMb = Average(memStart);
                float memEndMb = Average(memEnd);
                float growthMb = memEndMb - memStartMb;
                float gcP95 = Percentile(gcBytes, 0.95f);
                float gcP50 = Percentile(gcBytes, 0.50f);
                float gcMax = Percentile(gcBytes, 1f);

                string path = WriteSummary(soakSeconds, churnCycles, frameMs, mainMs, gcBytes, memStartMb, memEndMb, peakMemoryMb, growthMb,
                    useRecorders && mainThread.Valid, useRecorders, useRecorders);
                TestContext.Progress.WriteLine("[PerformanceSoak] summary: " + path);

                Assert.LessOrEqual(gcP95, maxGcP95, "Steady-state GC allocation p95 per frame was " + gcP95 + " bytes (p50 " + gcP50 + ", max " + gcMax + ").");
                Assert.LessOrEqual(growthMb, maxGrowthMb, "Total used memory grew " + growthMb + " MB over the run (" + memStartMb + " -> " + memEndMb + ").");
                Assert.Less(worstFrame, WorstFrameBoundSeconds, "Worst steady-state frame took " + worstFrame + " s.");
                Assert.Less(worstChurnFrame, WorstFrameBoundSeconds * 3f, "Worst zone load/unload frame took " + worstChurnFrame + " s.");
                Assert.GreaterOrEqual(churnCycles, soakSeconds >= WarmupSeconds + ChurnIntervalSeconds + 5f ? 1 : 0, "Zone churn never ran.");
                Assert.AreEqual(GameSessionState.Ride, app.Session.CurrentState, "Session left the Ride state during the soak.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                mainThread.Dispose();
                gcInFrame.Dispose();
                usedMemory.Dispose();
            }
        }

        private static IEnumerator ChurnZoneOnce(ZoneStreamManager zones)
        {
            if (zones.IsZoneLoaded(ChurnZone))
            {
                yield break;
            }

            double last = Time.realtimeSinceStartupAsDouble;
            System.Threading.Tasks.Task<bool> load = zones.LoadZoneAdditiveAsync(ChurnZone);
            while (!load.IsCompleted)
            {
                yield return null;
                last = TrackChurnFrame(last);
            }

            Assert.IsTrue(load.Result, ChurnZone + " failed to stream in.");
            yield return null;
            last = TrackChurnFrame(last);
            System.Threading.Tasks.Task<bool> unload = zones.UnloadZoneAsync(ChurnZone);
            while (!unload.IsCompleted)
            {
                yield return null;
                last = TrackChurnFrame(last);
            }

            Assert.IsTrue(unload.Result, ChurnZone + " failed to stream out.");
        }

        private static double TrackChurnFrame(double last)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            s_churnWorstFrameSeconds = Math.Max(s_churnWorstFrameSeconds, now - last);
            return now;
        }

        private static string WriteSummary(float soakSeconds, int churnCycles, List<float> frameMs, List<float> mainMs, List<float> gcBytes,
            float memStartMb, float memEndMb, float peakMb, float growthMb, bool mainValid, bool gcValid, bool memValid)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "perf"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "soak_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
            CultureInfo c = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\n");
            sb.Append("  \"soakGameSeconds\": ").Append(soakSeconds.ToString("F1", c)).Append(",\n");
            sb.Append("  \"sampledFrames\": ").Append(frameMs.Count).Append(",\n");
            sb.Append("  \"zoneChurnCycles\": ").Append(churnCycles).Append(",\n");
            sb.Append("  \"editor\": ").Append(Application.isEditor ? "true" : "false").Append(",\n");
            sb.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"recorderReportedNonZero\": { \"mainThread\": ").Append(mainValid ? "true" : "false").Append(", \"gc\": ").Append(gcValid ? "true" : "false")
                .Append(", \"memory\": ").Append(memValid ? "true" : "false").Append(" },\n");
            AppendDist(sb, "frameMs", frameMs, c);
            AppendDist(sb, "mainThreadMs", mainMs, c);
            AppendDist(sb, "gcAllocBytesPerFrame", gcBytes, c);
            sb.Append("  \"memoryMb\": { \"start\": ").Append(memStartMb.ToString("F2", c)).Append(", \"end\": ").Append(memEndMb.ToString("F2", c))
                .Append(", \"peak\": ").Append(peakMb.ToString("F2", c)).Append(", \"growth\": ").Append(growthMb.ToString("F2", c)).Append(" }\n");
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static void AppendDist(StringBuilder sb, string name, List<float> values, CultureInfo c)
        {
            sb.Append("  \"").Append(name).Append("\": { \"mean\": ").Append(Average(values).ToString("F3", c))
                .Append(", \"p50\": ").Append(Percentile(values, 0.50f).ToString("F3", c))
                .Append(", \"p95\": ").Append(Percentile(values, 0.95f).ToString("F3", c))
                .Append(", \"p99\": ").Append(Percentile(values, 0.99f).ToString("F3", c))
                .Append(", \"max\": ").Append(Percentile(values, 1f).ToString("F3", c)).Append(" },\n");
        }

        private static float Average(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            return (float)(sum / values.Count);
        }

        private static float Percentile(List<float> values, float p)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            // Callers sort the list ascending once before asking for percentiles.
            int index = Mathf.Clamp(Mathf.CeilToInt(p * values.Count) - 1, 0, values.Count - 1);
            return values[index];
        }

        private static float ReadEnvFloat(string name, float fallback)
        {
            string raw = Environment.GetEnvironmentVariable(name);
            float parsed;
            return !string.IsNullOrEmpty(raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0f
                ? parsed
                : fallback;
        }

        private static T FindInScene<T>(string sceneName) where T : Component
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, sceneName + " is not loaded.");
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T found = roots[i].GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
