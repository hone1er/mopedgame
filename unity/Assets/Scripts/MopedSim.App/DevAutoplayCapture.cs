using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MopedSim.Core;
using MopedSim.Garage;
using MopedSim.Ride;
using UnityEngine;

namespace MopedSim.App
{
    /// <summary>
    /// Dev-only autoplay + screenshot harness. Self-installs ONLY when the command line contains
    /// <c>-autoplayCapture &lt;outDir&gt;</c>; otherwise nothing is instantiated. Drives the real built game through the core loop
    /// via the same InputAdapter test-override seam the PlayMode tests use, writes NN_name.png screenshots plus report.json, then quits
    /// (exit code 0 on success, 1 when any step failed or an error/exception was logged). Presentation-only: no sim math.
    /// </summary>
    public sealed class DevAutoplayCapture : MonoBehaviour
    {
        public const string CommandLineFlag = "-autoplayCapture";

        /// <summary>Opt-in: keep the title screen, capture it + the archetype picker + the pause menu, then New Game with the default bike.</summary>
        public const string TitleCommandLineFlag = "-autoplayTitle";

        /// <summary>Opt-in: open the gig dispatch board (J), accept an eligible West Oakland gig and ride with the active-gig HUD.</summary>
        public const string GigCommandLineFlag = "-autoplayGig";
        private const string GigJobId = "gig_west_oakland_coffee";
        private const float BootTimeoutSeconds = 60f;
        private const float RideSeconds = 5f;
        private const float SteerSeconds = 3f;

        [Serializable]
        private sealed class StepRecord
        {
            public string name;
            public bool passed;
            public string note;
            public string screenshots;
            public string state;
            public float speedMps;
            public float rpm;
            public float odometerM;
            public Vector3 position;
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public int exitCode;
            public float totalSeconds;
            public float fpsMin;
            public float fpsAvg;
            public float fpsMax;
            public int frameSamples;
            public List<StepRecord> steps = new List<StepRecord>();
            public List<string> exceptions = new List<string>();
        }

        private string _outDir;
        private readonly Report _report = new Report();
        private AppCompositionRoot _app;
        private MopedBikeController _ride;
        private Vector3 _rideStartPos;
        private float _rideStartYaw;
        private float _fpsMin = float.MaxValue;
        private float _fpsMax;
        private double _fpsSum;
        private int _fpsCount;
        private bool _measuring;
        private bool _titleFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string outDir = TryGetOutDir(Environment.GetCommandLineArgs());
            if (outDir == null)
            {
                return;
            }

            GameObject go = new GameObject("DevAutoplayCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<DevAutoplayCapture>().Begin(outDir);
        }

        /// <summary>Returns the directory after <c>-autoplayCapture</c>, or null when the flag is absent/empty.</summary>
        public static string TryGetOutDir(string[] args)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], CommandLineFlag, StringComparison.Ordinal) && !string.IsNullOrEmpty(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private void Begin(string outDir)
        {
            _outDir = outDir;
            Directory.CreateDirectory(_outDir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                _report.exceptions.Add("[" + type + "] " + condition);
            }
        }

        private void Update()
        {
            if (!_measuring)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            float fps = 1f / dt;
            _fpsMin = Mathf.Min(_fpsMin, fps);
            _fpsMax = Mathf.Max(_fpsMax, fps);
            _fpsSum += fps;
            _fpsCount++;
        }

        private IEnumerator Run()
        {
            float start = Time.realtimeSinceStartup;

            yield return Step("boot_garage_overview", BootRoutine);
            _measuring = true; // Exclude the one-off boot/stream-in hitch from fps stats.
            yield return Step("garage_closeups", GarageCloseupsRoutine);
            yield return Step("settings_and_shop", PanelsRoutine);
            if (HasTitleFlag())
            {
                yield return Step("pause_menu", PauseRoutine);
            }

            yield return Step("toggle_to_ride", ToRideRoutine);
            yield return Step("ride_throttle", RideStraightRoutine);
            yield return Step("steer_right_left", SteerRoutine);
            yield return Step("chase_camera", ChaseRoutine);
            if (HasFlag(GigCommandLineFlag))
            {
                yield return Step("gig_board_and_active", GigRoutine);
            }

            yield return Step("breakdown_roadside_repair", BreakdownRoutine);
            yield return Step("back_to_garage", BackToGarageRoutine);

            _measuring = false;
            Finish(Time.realtimeSinceStartup - start);
        }

        /// <summary>Runs one step body, recording (instead of propagating) any exception so the script always reaches report.json.</summary>
        private IEnumerator Step(string name, Func<StepRecord, IEnumerator> body)
        {
            StepRecord record = new StepRecord { name = name, passed = false, note = string.Empty, screenshots = string.Empty };
            _report.steps.Add(record);
            if (name != "boot_garage_overview" && (_app == null || !_app.IsBootstrapped))
            {
                record.note = "skipped: app not bootstrapped";
                yield break;
            }

            IEnumerator it = body(record);
            while (true)
            {
                bool more;
                try
                {
                    more = it.MoveNext();
                }
                catch (Exception e)
                {
                    record.passed = false;
                    record.note = "exception: " + e.Message;
                    _report.exceptions.Add("[StepException] " + name + ": " + e);
                    break;
                }

                if (!more)
                {
                    break;
                }

                yield return it.Current;
            }

            Sample(record);
        }

        // ------------------------------------------------------------------ Steps

        private IEnumerator BootRoutine(StepRecord r)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < BootTimeoutSeconds)
            {
                if (_app == null)
                {
                    _app = FindAnyObjectByType<AppCompositionRoot>();
                }

                if (_app != null && _app.IsBootstrapped && _app.Zones != null && _app.Zones.IsZoneLoaded("Zone_WestOakland"))
                {
                    break;
                }

                yield return null;
            }

            if (_app == null || !_app.IsBootstrapped)
            {
                r.note = "AppCompositionRoot did not bootstrap within " + BootTimeoutSeconds + " s";
                yield break;
            }

            _ride = FindAnyObjectByType<MopedBikeController>();
            // Pack the repair kit so the roadside repair step can succeed (same as the release-candidate PlayMode test).
            _app.TryPackToolRollItem(ToolIds.SparkPlugWrench);
            _app.TryPackToolRollItem(ToolIds.SparePlug);
            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return Settle(2.0f);
            if (HasTitleFlag())
            {
                yield return TitleRoutine(r);
            }

            yield return Capture(r, "01_boot_garage");
            r.passed = _app.Session.CurrentState == GameSessionState.Garage && _ride != null && !_titleFailed;
            if (!r.passed)
            {
                r.note = "Expected Garage state and a MopedBikeController.";
            }
        }

        private static bool HasTitleFlag()
        {
            return HasFlag(TitleCommandLineFlag);
        }

        private static bool HasFlag(string flag)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
        }

        private IEnumerator TitleRoutine(StepRecord r)
        {
            SessionMenuController menus = _app.Menus;
            if (menus == null || !menus.IsTitleOpen)
            {
                r.note = "Title screen is not open.";
                _titleFailed = true;
                yield break;
            }

            yield return Capture(r, "00_title");
            menus.TitleView.ShowPicker();
            yield return Settle(0.4f);
            yield return Capture(r, "00_archetype_pick");
            _app.StartNewGame(ArchetypeIds.MaxerE50);
            yield return Settle(0.5f);
            if (menus.IsTitleOpen)
            {
                r.note = "Title did not close after New Game.";
                _titleFailed = true;
            }
        }

        private IEnumerator PauseRoutine(StepRecord r)
        {
            SessionMenuController menus = _app.EnsureMenus();
            menus.Pause();
            yield return Settle(0.5f);
            bool frozen = Mathf.Approximately(Time.timeScale, 0f) && menus.IsPauseOpen;
            yield return Capture(r, "03_pause");
            menus.Resume();
            yield return Settle(0.3f);
            r.passed = frozen && Mathf.Approximately(Time.timeScale, 1f) && !menus.IsPauseOpen;
            if (!r.passed)
            {
                r.note = "frozen=" + frozen + " timeScaleAfter=" + Time.timeScale;
            }
        }

        private IEnumerator GarageCloseupsRoutine(StepRecord r)
        {
            GarageWorkstandController stand = FindAnyObjectByType<GarageWorkstandController>();
            if (stand == null)
            {
                r.note = "No GarageWorkstandController.";
                yield break;
            }

            for (int i = 0; i < 3; i++)
            {
                stand.Orbit(i == 0 ? 0f : 70f, 0f);
                yield return Settle(0.6f);
                yield return Capture(r, "02_garage_yaw" + i);
            }

            r.passed = true;
        }

        private IEnumerator PanelsRoutine(StepRecord r)
        {
            yield return PressHotkey(false, true, false);
            bool settings = _app.IsSettingsMenuOpen;
            yield return Settle(0.5f);
            yield return Capture(r, "03_settings");
            yield return PressHotkey(false, true, false);
            yield return PressHotkey(false, false, true);
            bool shop = _app.IsShopOpen;
            yield return Settle(0.5f);
            yield return Capture(r, "03_shop");
            yield return PressHotkey(false, false, true);
            r.passed = settings && shop && !_app.IsSettingsMenuOpen && !_app.IsShopOpen;
            if (!r.passed)
            {
                r.note = "settings=" + settings + " shop=" + shop;
            }
        }

        private IEnumerator ToRideRoutine(StepRecord r)
        {
            yield return PressHotkey(true, false, false);
            yield return Settle(1.0f);
            yield return Capture(r, "04_ride_start");
            _rideStartPos = _ride.transform.position;
            _rideStartYaw = _ride.transform.eulerAngles.y;
            r.passed = _app.Session.CurrentState == GameSessionState.Ride;
        }

        private IEnumerator RideStraightRoutine(StepRecord r)
        {
            SetRide(1f, 0f);
            yield return RideFor(RideSeconds);
            yield return Capture(r, "05_ride_straight");
            r.passed = _ride.SpeedMps > 2f;
            if (!r.passed)
            {
                r.note = "Speed only " + _ride.SpeedMps.ToString("F2", CultureInfo.InvariantCulture) + " m/s.";
            }
        }

        private IEnumerator SteerRoutine(StepRecord r)
        {
            SetRide(0.6f, 0.12f);
            yield return RideFor(SteerSeconds);
            yield return Capture(r, "06_steer_right");
            SetRide(0.6f, -0.12f);
            yield return RideFor(SteerSeconds);
            yield return Capture(r, "06_steer_left");
            bool upright = Mathf.Abs(_ride.LeanAngleDeg) < 45f && _ride.SpeedMps > 1f;
            r.passed = _app.Session.CurrentState == GameSessionState.Ride && upright;
            if (!upright)
            {
                r.note = "Bike fell/stopped while steering (lean " + _ride.LeanAngleDeg.ToString("F0", CultureInfo.InvariantCulture)
                    + " deg, speed " + _ride.SpeedMps.ToString("F1", CultureInfo.InvariantCulture) + " m/s).";
            }
        }

        private IEnumerator ChaseRoutine(StepRecord r)
        {
            // Steering earlier can leave the road; return to the ride-start pose so the camera step is shot on the road.
            if (_rideStartPos != Vector3.zero)
            {
                _ride.ResetUprightAt(_rideStartPos, _rideStartYaw);
            }

            SetRide(1f, 0f);
            yield return RideFor(2f);
            MopedCameraRig rig = FindAnyObjectByType<MopedCameraRig>();
            if (rig != null)
            {
                MopedCameraRig.CameraMode original = rig.Mode;
                rig.Mode = MopedCameraRig.CameraMode.Chase;
                yield return RideFor(0.6f);
                yield return Capture(r, "07_cam_chase");
                rig.Mode = MopedCameraRig.CameraMode.CloseChase;
                yield return RideFor(0.6f);
                yield return Capture(r, "07_cam_close");
                rig.Mode = MopedCameraRig.CameraMode.Handlebar;
                yield return RideFor(0.6f);
                yield return Capture(r, "07_cam_handlebar");
                rig.Mode = original;
            }

            yield return Capture(r, "07_chase_camera");
            r.passed = _ride.SpeedMps > 1f;
            if (!r.passed)
            {
                r.note = "Bike stopped (lean " + _ride.LeanAngleDeg.ToString("F0", CultureInfo.InvariantCulture) + " deg, grounded=" + _ride.IsGrounded + ").";
            }
        }

        private IEnumerator GigRoutine(StepRecord r)
        {
            if (_rideStartPos != Vector3.zero)
            {
                _ride.ResetUprightAt(_rideStartPos, _rideStartYaw);
            }

            SetRide(0f, 0f);
            yield return Settle(0.4f);
            yield return PressJobBoardKey();
            yield return Settle(0.6f);
            JobDispatchController dispatch = _app.JobDispatch;
            bool opened = _app.IsJobBoardOpen && Mathf.Approximately(Time.timeScale, 0f);
            yield return Capture(r, "11_job_board");
            if (!opened || dispatch == null || dispatch.View == null)
            {
                r.note = "Job board did not open on J (open=" + _app.IsJobBoardOpen + " timeScale=" + Time.timeScale + ").";
                yield break;
            }

            dispatch.View.TriggerAcceptJob(GigJobId);
            yield return Settle(0.4f);
            bool accepted = _app.Courier != null && _app.Courier.HasActiveJob && !_app.IsJobBoardOpen && Mathf.Approximately(Time.timeScale, 1f);
            if (!accepted)
            {
                r.note = "Accepting " + GigJobId + " did not start the gig (status=" + (_app.Courier != null ? _app.Courier.Status.ToString() : "no courier")
                    + " boardOpen=" + _app.IsJobBoardOpen + ").";
                yield break;
            }

            SetRide(0.7f, 0f);
            yield return RideFor(3f);
            yield return Capture(r, "11_gig_active");
            bool stillActive = _app.Courier.HasActiveJob;
            _app.Input.SetTestOverride(default(FrameControlInputs));
            _app.AbandonJob();
            yield return Settle(0.3f);
            r.passed = stillActive;
            if (!stillActive)
            {
                r.note = "Gig ended while riding (status=" + _app.Courier.Status + ").";
            }
        }

        private IEnumerator PressJobBoardKey()
        {
            _app.Input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, toggleJobBoardPressed: true));
            yield return null;
            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return null;
        }

        private IEnumerator BreakdownRoutine(StepRecord r)
        {
            BikeSimulation bike = _app.ActiveBike;
            bike.InduceFault(FaultIds.FouledPlug);
            SetRide(0.8f, 0f);
            float t0 = Time.time;
            while (!bike.LatestTelemetry.IsStalled && Time.time - t0 < 30f)
            {
                yield return null;
            }

            if (!bike.LatestTelemetry.IsStalled)
            {
                r.note = "Engine never stalled after induced fouled plug.";
                yield break;
            }

            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return Capture(r, "08_breakdown");
            if (!_app.PullOverForRoadsideRepair())
            {
                r.note = "PullOverForRoadsideRepair refused.";
                yield break;
            }

            yield return Settle(0.8f);
            yield return Capture(r, "08_roadside_repair");
            RoadsideRepairResult result = _app.AttemptRoadsideRepair(FaultIds.FouledPlug);
            r.passed = result.Succeeded;
            r.note = result.Message ?? string.Empty;
            yield return Settle(0.5f);
        }

        private IEnumerator BackToGarageRoutine(StepRecord r)
        {
            _app.Input.SetTestOverride(default(FrameControlInputs));
            if (_app.Session.CurrentState != GameSessionState.Garage)
            {
                yield return PressHotkey(true, false, false);
            }

            yield return Settle(1.0f);
            yield return Capture(r, "09_back_to_garage");
            r.passed = _app.Session.CurrentState == GameSessionState.Garage;
        }

        // ------------------------------------------------------------------ Helpers

        private void SetRide(float throttle, float steer)
        {
            _app.Input.SetTestOverride(new FrameControlInputs(throttle, 0f, 0f, steer, false, true, false, false, false, false));
        }

        private static IEnumerator RideFor(float seconds)
        {
            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator Settle(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
            }
        }

        private IEnumerator PressHotkey(bool garageRide, bool settings, bool shop)
        {
            _app.Input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, garageRide, settings, shop, false));
            yield return null;
            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return null;
        }

        private IEnumerator Capture(StepRecord record, string fileStem)
        {
            yield return new WaitForEndOfFrame();
            string file = fileStem + ".png";
            Texture2D tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
            tex.Apply(false);
            try
            {
                File.WriteAllBytes(Path.Combine(_outDir, file), tex.EncodeToPNG());
            }
            finally
            {
                Destroy(tex);
            }

            record.screenshots = record.screenshots.Length == 0 ? file : record.screenshots + "," + file;
            Debug.Log("[Autoplay] captured " + file + " state=" + _app.Session.CurrentState);
        }

        private void Sample(StepRecord r)
        {
            if (_app == null)
            {
                return;
            }

            r.state = _app.Session.CurrentState.ToString();
            if (_app.ActiveBike != null)
            {
                r.rpm = _app.ActiveBike.LatestTelemetry.Rpm;
                r.odometerM = _app.ActiveBike.OdometerMeters;
            }

            if (_ride != null)
            {
                r.speedMps = _ride.SpeedMps;
                r.position = _ride.transform.position;
            }

            Debug.Log("[Autoplay] step " + r.name + " passed=" + r.passed + " " + r.note);
        }

        private void Finish(float totalSeconds)
        {
            bool allPassed = true;
            for (int i = 0; i < _report.steps.Count; i++)
            {
                allPassed &= _report.steps[i].passed;
            }

            _report.passed = allPassed && _report.exceptions.Count == 0;
            _report.exitCode = _report.passed ? 0 : 1;
            _report.totalSeconds = totalSeconds;
            _report.frameSamples = _fpsCount;
            _report.fpsMin = _fpsCount > 0 ? _fpsMin : 0f;
            _report.fpsMax = _fpsMax;
            _report.fpsAvg = _fpsCount > 0 ? (float)(_fpsSum / _fpsCount) : 0f;
            File.WriteAllText(Path.Combine(_outDir, "report.json"), JsonUtility.ToJson(_report, true));
            Application.logMessageReceived -= OnLog;
            Application.Quit(_report.exitCode);
        }
    }
}
