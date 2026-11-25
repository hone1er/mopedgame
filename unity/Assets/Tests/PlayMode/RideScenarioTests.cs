using System.Collections;
using MopedSim.Core;
using MopedSim.Ride;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M2 exit scenarios (PROJECT_GUIDE.md section 7): aero tuck terminal speed, 18% hill pedal-assist + CHT rise, and
    /// shallow-angle Muni rail lateral slip, run on the real MopedBikeController rigid body with scripted physics.
    /// </summary>
    public sealed class RideScenarioTests
    {
        private SimulationMode _previousMode;
        private GameObject[] _spawned = new GameObject[0];

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Length; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.Destroy(_spawned[i]);
                }
            }

            _spawned = new GameObject[0];
            Physics.simulationMode = _previousMode;
        }

        private void Track(params GameObject[] objects)
        {
            _spawned = objects;
        }

        [UnityTest]
        public IEnumerator AeroTuck_RaisesFlatTerminalSpeed_ByAtLeastThreeMph()
        {
            GameObject ground = PlayModeRig.CreateGround("Ground_FlatTestStraight", new Vector3(0f, -0.5f, 1900f), Quaternion.identity, new Vector3(40f, 1f, 4000f));
            BikeSimulation uprightSim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation tuckSim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            uprightSim.Thermal.ChtCelsius = 140f;
            tuckSim.Thermal.ChtCelsius = 140f;

            MopedBikeController upright = PlayModeRig.CreateRideRig("Upright", uprightSim, 1212u, new Vector3(-6f, 0f, 0f), Quaternion.identity);
            MopedBikeController tucked = PlayModeRig.CreateRideRig("Tucked", tuckSim, 1212u, new Vector3(6f, 0f, 0f), Quaternion.identity);
            Track(ground, upright.gameObject, tucked.gameObject);
            yield return null;

            upright.SetControls(1f, 0f, 0f, 0f, false, false, false, false);
            tucked.SetControls(1f, 0f, 0f, 0f, true, false, false, false);

            // 90 s at WOT reaches terminal speed for both postures (headless model converges by ~60 s).
            for (int i = 0; i < 90 * 60; i++)
            {
                PlayModeRig.Step(upright, tucked);
            }

            float gainMph = tucked.SpeedMph - upright.SpeedMph;
            Assert.Greater(upright.SpeedMph, 20f, "Upright stock E50 should cruise well above 20 mph on the flat.");
            Assert.GreaterOrEqual(gainMph, 3f, "Aero tuck gain was " + gainMph.ToString("0.00") + " mph.");
            Assert.AreEqual(tucked.SpeedMps, tuckSim.LatestTelemetry.VehicleSpeedMps, 0.25f, "Core should track the rigid-body wheel speed.");
        }

        [UnityTest]
        public IEnumerator EighteenPercentHill_PedalAssistClimbsFaster_AndChtRises()
        {
            float slopeDeg = Mathf.Atan(0.18f) * Mathf.Rad2Deg;
            Quaternion slope = Quaternion.Euler(-slopeDeg, 0f, 0f);
            Vector3 up = slope * Vector3.up;
            GameObject ramp = PlayModeRig.CreateGround("Ground_TwinPeaksGrade18", -up * 0.5f, slope, new Vector3(40f, 1f, 4000f));

            BikeSimulation noPedalSim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation pedalSim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            noPedalSim.Thermal.ChtCelsius = 140f;
            pedalSim.Thermal.ChtCelsius = 140f;

            MopedBikeController noPedal = PlayModeRig.CreateRideRig("HillNoPedal", noPedalSim, 1313u, slope * new Vector3(-6f, 0f, 0f), slope);
            MopedBikeController pedal = PlayModeRig.CreateRideRig("HillPedal", pedalSim, 1313u, slope * new Vector3(6f, 0f, 0f), slope);
            Track(ramp, noPedal.gameObject, pedal.gameObject);
            yield return null;

            noPedal.SetControls(1f, 0f, 0f, 0f, false, false, false, false);
            pedal.SetControls(1f, 0f, 0f, 0f, false, true, false, false);
            for (int i = 0; i < 12 * 60; i++)
            {
                PlayModeRig.Step(noPedal, pedal);
            }

            float noPedalMph = noPedal.ForwardSpeedMps * PlayModeRig.MpsToMph;
            float pedalMph = pedal.ForwardSpeedMps * PlayModeRig.MpsToMph;
            Assert.Greater(pedalMph, noPedalMph + 1.5f, "Pedal-assist " + pedalMph.ToString("0.0") + " mph vs " + noPedalMph.ToString("0.0") + " mph.");
            Assert.Greater(pedalMph, 3f, "Pedal-assisted bike should make forward progress up the 18% grade.");
            Assert.Greater(noPedalSim.LatestTelemetry.ChtCelsius, 145f, "Lugging WOT up 18% must heat the cylinder head.");
            Assert.Greater(pedalSim.LatestTelemetry.RiderStamina, 0f);
            Assert.Less(pedalSim.LatestTelemetry.RiderStamina, 1f, "Pedal-assist must spend rider stamina (owned by Core).");
        }

        [UnityTest]
        public IEnumerator MuniRail_ShallowCrossing_InducesLateralSlip_PerpendicularDoesNot()
        {
            float shallow = 0f;
            float shallowLateralVel = 0f;
            float perpendicular = 0f;
            float perpendicularLateralVel = 0f;
            float shallowAngle = 0f;

            for (int pass = 0; pass < 2; pass++)
            {
                float railYawDeg = pass == 0 ? 10f : 90f;
                GameObject rail = PlayModeRig.CreateGround("Ground_MuniSteelRailTrack_" + railYawDeg, new Vector3(0f, -0.5f, 100f), Quaternion.identity, new Vector3(40f, 1f, 400f));
                RideSurfaceMarker marker = rail.AddComponent<RideSurfaceMarker>();
                marker.SurfaceType = RoadSurfaceType.SteelRailTrack;
                marker.LocalRailAxis = Quaternion.Euler(0f, railYawDeg, 0f) * Vector3.forward;

                BikeSimulation sim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
                MopedBikeController bike = PlayModeRig.CreateRideRig("RailBike_" + railYawDeg, sim, 77u, Vector3.zero, Quaternion.identity);
                Track(rail, bike.gameObject);
                yield return null;

                bike.Body.linearVelocity = new Vector3(0f, 0f, 8f);
                bike.SetControls(0.5f, 0f, 0f, 0f, false, false, false, false);

                float peakLateral = 0f;
                for (int i = 0; i < 45; i++)
                {
                    PlayModeRig.Step(bike, null);
                    peakLateral = Mathf.Max(peakLateral, Mathf.Abs(bike.LastRailLateralAccel));
                }

                float lateralVel = Mathf.Abs(Vector3.Dot(bike.Body.linearVelocity, bike.transform.right));
                if (pass == 0)
                {
                    shallow = peakLateral;
                    shallowLateralVel = lateralVel;
                    shallowAngle = bike.LastRailCrossingAngleDeg;
                }
                else
                {
                    perpendicular = peakLateral;
                    perpendicularLateralVel = lateralVel;
                }

                Object.Destroy(bike.gameObject);
                Object.Destroy(rail);
                _spawned = new GameObject[0];
                yield return null;
            }

            Assert.Less(shallowAngle, SurfaceHazardDetector.ShallowRailAngleThresholdDeg);
            Assert.Greater(shallow, 1f, "10 degree Muni rail crossing should throw a strong lateral slip impulse.");
            Assert.Greater(shallowLateralVel, 0.25f, "Shallow crossing should push the bike sideways.");
            Assert.AreEqual(0f, perpendicular, 0.0001f, "90 degree crossing must not induce rail slip.");
            Assert.Less(perpendicularLateralVel, 0.05f);
            Assert.Greater(shallowLateralVel, perpendicularLateralVel + 0.2f);
        }
    }
}
