using MopedSim.Core;
using MopedSim.World;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Karl the Fog trigger volumes drive BayAreaWeatherController: registration follows rider enter/exit counts,
    /// overlapping volumes blend by max intensity regardless of exit order, and the intensity fades in horizontally.
    /// Trigger callbacks are exercised through NotifyRiderEntered/Exited so the contract is checked without physics stepping.
    /// </summary>
    public sealed class KarlTheFogVolumeTests
    {
        private GameObject _root;
        private BayAreaWeatherController _weather;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("KarlFogTestRoot");
            _weather = _root.AddComponent<BayAreaWeatherController>();
            _weather.ApplyZoneEnvironment(new ZoneEnvironmentSpec
            {
                Id = "Zone_Test",
                DisplayName = "Test",
                AmbientTemperatureC = 18f,
                AmbientAirDensity = RideInputState.StandardAirDensity,
                BaseSurfaceGrip = 1f,
            }, 0f, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void RiderEnterAndExit_RegistersVolume_AndBlendsFogAirDensityAndGrip()
        {
            KarlTheFogVolume fog = CreateVolume("Fog", Vector3.zero, new Vector3(60f, 30f, 300f), 0.9f);

            Assert.AreEqual(0, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0f, _weather.FogIntensity, 1e-5f);
            float clearDensity = _weather.CurrentAmbientAirDensity;
            float clearGrip = _weather.CurrentSurfaceGripMultiplier;

            fog.NotifyRiderEntered();
            Assert.IsTrue(fog.IsRiderInside);
            Assert.AreEqual(1, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0.9f, _weather.FogIntensity, 1e-4f, "No tracked rider: the volume's peak intensity applies.");
            Assert.Greater(_weather.CurrentAmbientAirDensity, clearDensity);
            Assert.Less(_weather.CurrentSurfaceGripMultiplier, clearGrip);

            fog.NotifyRiderExited();
            Assert.IsFalse(fog.IsRiderInside);
            Assert.AreEqual(0, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0f, _weather.FogIntensity, 1e-5f);
            Assert.AreEqual(clearDensity, _weather.CurrentAmbientAirDensity, 1e-5f);
        }

        [Test]
        public void MultipleRiderColliders_KeepVolumeActiveUntilTheLastOneLeaves()
        {
            KarlTheFogVolume fog = CreateVolume("Fog", Vector3.zero, new Vector3(40f, 20f, 40f), 0.8f);

            fog.NotifyRiderEntered(); // e.g. front wheel collider
            fog.NotifyRiderEntered(); // rear wheel collider
            fog.NotifyRiderExited();
            Assert.AreEqual(1, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0.8f, _weather.FogIntensity, 1e-4f);

            fog.NotifyRiderExited();
            fog.NotifyRiderExited(); // spurious extra exit must not go negative or re-register
            Assert.AreEqual(0, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0f, _weather.FogIntensity, 1e-5f);
        }

        [Test]
        public void OverlappingVolumes_UseMaxIntensity_IndependentOfExitOrder()
        {
            KarlTheFogVolume light = CreateVolume("LightFog", Vector3.zero, new Vector3(80f, 30f, 80f), 0.4f);
            KarlTheFogVolume dense = CreateVolume("DenseFog", Vector3.zero, new Vector3(40f, 30f, 40f), 0.95f);

            light.NotifyRiderEntered();
            dense.NotifyRiderEntered();
            Assert.AreEqual(2, _weather.ActiveFogVolumeCount);
            Assert.AreEqual(0.95f, _weather.FogIntensity, 1e-4f);

            // Leaving the dense bank first falls back to the light bank, not to clear air.
            dense.NotifyRiderExited();
            Assert.AreEqual(0.4f, _weather.FogIntensity, 1e-4f);

            dense.NotifyRiderEntered();
            light.NotifyRiderExited();
            Assert.AreEqual(0.95f, _weather.FogIntensity, 1e-4f);

            dense.NotifyRiderExited();
            Assert.AreEqual(0f, _weather.FogIntensity, 1e-5f);
        }

        [Test]
        public void TrackedRider_GetsHorizontalEdgeFade_AndOnlyTrackedBodiesCount()
        {
            KarlTheFogVolume fog = CreateVolume("Fog", new Vector3(0f, 10f, 0f), new Vector3(60f, 30f, 300f), 0.9f);
            fog.Configure(0.9f, 20f);
            Physics.SyncTransforms();

            GameObject rider = new GameObject("Rider");
            rider.transform.SetParent(_root.transform, false);
            Rigidbody riderBody = rider.AddComponent<Rigidbody>();
            riderBody.isKinematic = true;
            GameObject stranger = new GameObject("Stranger");
            stranger.transform.SetParent(_root.transform, false);
            Rigidbody strangerBody = stranger.AddComponent<Rigidbody>();

            _weather.SetTrackedRider(riderBody);
            Assert.IsTrue(_weather.IsTrackedRider(riderBody));
            Assert.IsFalse(_weather.IsTrackedRider(strangerBody));
            Assert.IsFalse(_weather.IsTrackedRider(null));

            // Near ground level in the middle of a tall bank: full intensity (vertical depth is ignored).
            Assert.AreEqual(0.9f, fog.EvaluateIntensityAt(new Vector3(0f, 0.6f, 0f)), 1e-4f);
            // 5 m inside the x edge of a 20 m fade: a quarter of the peak.
            Assert.AreEqual(0.9f * 0.25f, fog.EvaluateIntensityAt(new Vector3(25f, 0.6f, 0f)), 1e-3f);
            // Outside the bounds: none.
            Assert.AreEqual(0f, fog.EvaluateIntensityAt(new Vector3(31f, 0.6f, 0f)), 1e-5f);

            rider.transform.position = new Vector3(25f, 0.6f, 0f);
            fog.NotifyRiderEntered();
            _weather.RefreshFogVolumeIntensity();
            Assert.AreEqual(0.9f * 0.25f, _weather.FogIntensity, 1e-3f);
        }

        private KarlTheFogVolume CreateVolume(string name, Vector3 center, Vector3 size, float peak)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            KarlTheFogVolume fog = go.AddComponent<KarlTheFogVolume>();
            fog.Configure(peak, 0f);
            fog.BindWeather(_weather);
            Physics.SyncTransforms();
            return fog;
        }
    }
}
