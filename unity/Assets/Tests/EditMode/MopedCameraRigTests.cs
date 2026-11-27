using NUnit.Framework;
using MopedSim.Ride;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    public sealed class MopedCameraRigTests
    {
        private GameObject _bike;
        private GameObject _camGo;
        private MopedCameraRig _rig;
        private GameObject _wall;

        [SetUp]
        public void SetUp()
        {
            _bike = new GameObject("TestBike");
            _bike.transform.position = new Vector3(0f, 0.5f, 0f);
            _camGo = new GameObject("TestCam");
            Camera cam = _camGo.AddComponent<Camera>();
            _rig = _camGo.AddComponent<MopedCameraRig>();
            _rig.SetTarget(_bike.transform, cam);
        }

        [TearDown]
        public void TearDown()
        {
            if (_wall != null) { Object.DestroyImmediate(_wall); }
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_bike);
        }

        private void Settle(int frames)
        {
            for (int i = 0; i < frames; i++) { _rig.Step(0.016f); }
        }

        [Test]
        public void ToggleMode_CyclesChaseCloseHandlebar()
        {
            Assert.AreEqual(MopedCameraRig.CameraMode.Chase, _rig.Mode);
            _rig.ToggleMode();
            Assert.AreEqual(MopedCameraRig.CameraMode.CloseChase, _rig.Mode);
            _rig.ToggleMode();
            Assert.AreEqual(MopedCameraRig.CameraMode.Handlebar, _rig.Mode);
            _rig.ToggleMode();
            Assert.AreEqual(MopedCameraRig.CameraMode.Chase, _rig.Mode);
        }

        [Test]
        public void EveryMode_PitchIsNeverTopDown_AndCameraStaysBehindOrAtBike()
        {
            _rig.UpdateRideTelemetry(20f, 0.5f);
            for (int m = 0; m < MopedCameraRig.ModeCount; m++)
            {
                _rig.Mode = (MopedCameraRig.CameraMode)m;
                Settle(120);
                float pitch = Vector3.Angle(_camGo.transform.forward, Vector3.ProjectOnPlane(_camGo.transform.forward, Vector3.up));
                Assert.Less(pitch, 35f, "mode " + (MopedCameraRig.CameraMode)m);
                Assert.Greater(Vector3.Dot(_camGo.transform.forward, _bike.transform.forward), 0.8f);
                Assert.Less(_camGo.transform.position.y - _bike.transform.position.y, 3f);
            }
        }

        [Test]
        public void Teleport_SnapsInsteadOfSwooping()
        {
            Settle(30);
            _bike.transform.position = new Vector3(500f, 0.5f, 300f);
            _bike.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _rig.Step(0.016f);
            Assert.Less(Vector3.Distance(_camGo.transform.position, _bike.transform.position), 6f);
            Assert.Greater(Vector3.Dot(_camGo.transform.forward, _bike.transform.forward), 0.9f);
        }

        [Test]
        public void ToppledBike_KeepsHeadingAndFinitePose()
        {
            Settle(30);
            Quaternion before = _camGo.transform.rotation;
            _bike.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            Settle(60);
            Assert.IsFalse(float.IsNaN(_camGo.transform.position.x));
            Assert.Greater(Vector3.Dot(_camGo.transform.forward, before * Vector3.forward), 0.7f);
        }

        [Test]
        public void Wall_BehindBike_PullsCameraIn()
        {
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.transform.position = new Vector3(0f, 2f, -2.5f);
            _wall.transform.localScale = new Vector3(20f, 6f, 0.5f);
            Physics.SyncTransforms();
            Settle(60);
            // Wall front face is at z = -2.25; the 4.4 m default offset would otherwise sit behind it.
            Assert.Greater(_camGo.transform.position.z, -2.25f);
        }
    }
}
