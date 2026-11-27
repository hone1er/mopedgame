using System.Collections;
using MopedSim.App;
using MopedSim.Garage;
using MopedSim.Ride;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// Steering regression on the REAL RideMoped (Bootstrap.unity + Zone_WestOakland: box collider, centre of mass, auto
    /// FixedUpdate stepping and real road) rather than the collider-free PlayModeRig. Guards the "bike lays on its side the
    /// moment you steer at speed" bug and the tip-over auto-recovery safety net.
    /// </summary>
    public sealed class RideRealMopedSteeringTests
    {
        private const int MaxBootFrames = 6000;
        private const string WorkingZone = "Zone_WestOakland";

        private static readonly string[] RuntimeScenes =
        {
            "Bootstrap", "Garage", "UI_Overlay", "Zone_WestOakland", "Zone_OaklandHills", "Zone_BayCrossing", "Zone_SF_Mission"
        };

        private SimulationMode _previousMode;
        private int _previousTargetFrameRate;
        private AppCompositionRoot _app;
        private MopedBikeController _ride;
        private Rigidbody _body;
        private float _maxAbsLean;
        private float _minSpeed;
        private bool _everUngrounded;
        private GameObject _pad;

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            _previousTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_app != null && _app.Input != null)
            {
                _app.Input.ClearTestOverride();
            }

            if (_pad != null)
            {
                Object.Destroy(_pad);
            }

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
        }

        private IEnumerator BootAndStartRide()
        {
            AsyncOperation boot = SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Additive);
            while (!boot.isDone)
            {
                yield return null;
            }

            _app = FindInScene<AppCompositionRoot>("Bootstrap");
            Assert.IsNotNull(_app);
            int frames = 0;
            while ((!_app.IsBootstrapped || _app.Zones == null || !_app.Zones.IsZoneLoaded(WorkingZone)) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(_app.IsBootstrapped);
            Assert.IsTrue(_app.Zones.IsZoneLoaded(WorkingZone));
            BikeVisualAssembler assembler = FindInScene<BikeVisualAssembler>("Garage");
            frames = 0;
            while (assembler != null && (assembler.IsSyncing || assembler.MountedPartCount == 0) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            _app.Input.SetTestOverride(default(FrameControlInputs));
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            _app.Input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, true));
            yield return null;
            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return null;
            Assert.AreEqual(GameSessionState.Ride, _app.Session.CurrentState);

            _ride = FindInScene<MopedBikeController>("Bootstrap");
            _body = _ride.GetComponent<Rigidbody>();
            Assert.IsFalse(_body.isKinematic);

            // The West Oakland road strip is narrow; lay a wide test pad level with it so long steering runs stay on a surface.
            RaycastHit roadHit;
            if (Physics.Raycast(_ride.transform.position + Vector3.up, Vector3.down, out roadHit, 5f))
            {
                GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "SteeringTestPad";
                pad.transform.position = new Vector3(_ride.transform.position.x, roadHit.point.y - 0.51f, _ride.transform.position.z + 150f);
                pad.transform.localScale = new Vector3(600f, 1f, 600f);
                pad.GetComponent<MeshRenderer>().enabled = false;
                _pad = pad;
            }
            _maxAbsLean = 0f;
            _minSpeed = float.MaxValue;
            _everUngrounded = false;
        }

        private void Drive(float throttle, float steer)
        {
            _app.Input.SetTestOverride(new FrameControlInputs(throttle, 0f, 0f, steer, false, true, false, false, false, false));
        }

        private IEnumerator Hold(float throttle, float steer, float seconds)
        {
            Drive(throttle, steer);
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                _maxAbsLean = Mathf.Max(_maxAbsLean, Mathf.Abs(_ride.LeanAngleDeg));
                _minSpeed = Mathf.Min(_minSpeed, _ride.SpeedMps);
                _everUngrounded |= !_ride.IsGrounded;
            }
        }

        private IEnumerator AccelerateTo(float speed, float timeoutSeconds)
        {
            Drive(1f, 0f);
            float end = Time.time + timeoutSeconds;
            while (_ride.SpeedMps < speed && Time.time < end)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(_ride.SpeedMps, speed, "Bike failed to reach " + speed + " m/s.");
        }

        private float YawDeg()
        {
            return _ride.transform.eulerAngles.y;
        }

        private string Diag()
        {
            return " lean=" + _ride.LeanAngleDeg.ToString("F1") + " maxLean=" + _maxAbsLean.ToString("F1") + " speed=" + _ride.SpeedMps.ToString("F1")
                + " minSpeed=" + _minSpeed.ToString("F1") + " grounded=" + _ride.IsGrounded + " pos=" + _ride.transform.position;
        }

        [UnityTest]
        public IEnumerator SteerRight_AtCruise_StaysUpright_TurnsRight_AndSelfRights()
        {
            yield return RunSteerSequence(1f);
        }

        [UnityTest]
        public IEnumerator SteerLeft_AtCruise_StaysUpright_TurnsLeft_AndSelfRights()
        {
            yield return RunSteerSequence(-1f);
        }

        private IEnumerator RunSteerSequence(float sign)
        {
            yield return BootAndStartRide();
            yield return AccelerateTo(10f, 20f);

            float yaw0 = YawDeg();
            yield return Hold(0.6f, 0.15f * sign, 2f);
            Assert.Less(_maxAbsLean, 45f, "0.15 steer must not tip the bike." + Diag());
            Assert.Greater(_minSpeed, 5f, "0.15 steer must not scrub speed." + Diag());
            Assert.Greater(Mathf.DeltaAngle(yaw0, YawDeg()) * sign, 1f, "Heading must move toward the steer side." + Diag());

            yield return Hold(0.6f, 0.5f * sign, 2f);
            Assert.Less(_maxAbsLean, 45f, "0.5 steer must not tip the bike." + Diag());
            Assert.Greater(_minSpeed, 5f, "0.5 steer must not scrub speed." + Diag());

            yield return Hold(0.6f, 1f * sign, 1f);
            Assert.Less(_maxAbsLean, 45f, "Full lock must not tip the bike." + Diag());
            Assert.Greater(_minSpeed, 5f, "Full lock must not scrub speed." + Diag());
            Assert.Greater(Mathf.DeltaAngle(yaw0, YawDeg()) * sign, 15f, "Heading must have turned toward the steer side." + Diag());
            Assert.IsFalse(_everUngrounded, "The bike must stay grounded through the turn." + Diag());

            yield return Hold(0.6f, 0f, 2f);
            Assert.Less(Mathf.Abs(_ride.LeanAngleDeg), 8f, "Lean must return toward upright within 2 s of release." + Diag());
            Assert.Greater(_ride.SpeedMps, 5f, Diag());
        }

        [UnityTest]
        public IEnumerator LowSpeedTightTurn_StaysUpright_AndTurns()
        {
            yield return BootAndStartRide();
            yield return AccelerateTo(2.2f, 10f);

            float yaw0 = YawDeg();
            yield return Hold(0.12f, 1f, 3f);
            Assert.Less(_maxAbsLean, 45f, "Low-speed full lock must not tip the bike." + Diag());
            Assert.Greater(_ride.SpeedMps, 0.5f, "Low-speed turn must keep rolling." + Diag());
            Assert.Greater(Mathf.DeltaAngle(yaw0, YawDeg()), 15f, "Low-speed turn must change heading." + Diag());
        }

        [UnityTest]
        public IEnumerator TipOver_AutoRecovers_UprightAtSamePosition()
        {
            yield return BootAndStartRide();
            yield return Hold(0f, 0f, 1f);
            Vector3 posBefore = _ride.transform.position;

            // Lay the bike on its side (as a crash would) and leave it.
            _ride.transform.rotation = Quaternion.AngleAxis(88f, _ride.transform.forward) * _ride.transform.rotation;
            _body.rotation = _ride.transform.rotation;
            _body.angularVelocity = Vector3.zero;
            float t = 0f;
            while (!_ride.WasRecoveredFromTipOver && t < 6f)
            {
                yield return null;
                t += Time.deltaTime;
            }

            Assert.IsTrue(_ride.WasRecoveredFromTipOver, "Auto-recovery must stand a tipped bike back up within a few seconds.");
            yield return Hold(0f, 0f, 0.5f);
            Assert.Less(Mathf.Abs(_ride.LeanAngleDeg), 15f, Diag());
            Assert.Less(Vector3.ProjectOnPlane(_ride.transform.position - posBefore, Vector3.up).magnitude, 3f, Diag());
        }

        [UnityTest]
        public IEnumerator ManualPickUp_StandsBikeUpright_Immediately()
        {
            yield return BootAndStartRide();
            yield return Hold(0f, 0f, 1f);
            _ride.transform.rotation = Quaternion.AngleAxis(80f, _ride.transform.forward) * _ride.transform.rotation;
            _body.rotation = _ride.transform.rotation;

            _app.Input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, false, false, false, false, true));
            yield return null;
            yield return null;
            _app.Input.SetTestOverride(default(FrameControlInputs));
            yield return null;
            Assert.IsTrue(_ride.WasRecoveredFromTipOver);
            Assert.Less(Vector3.Angle(_ride.transform.up, Vector3.up), 10f, Diag());
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
