using System.Collections;
using MopedSim.Core;
using MopedSim.Ride;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M2 exit check #4: the per-FixedUpdate ride bridge (wheel raycasts + BikeSimulation.Step + rigid-body forces) and the
    /// per-frame HUD / diagnostic-hint fan-out allocate zero GC memory once warmed up.
    /// </summary>
    public sealed class TelemetryBridgeAllocationTests
    {
        private SimulationMode _previousMode;
        private GameObject _ground;
        private GameObject _hudGo;
        private MopedBikeController _ride;

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ride != null)
            {
                Object.Destroy(_ride.gameObject);
            }

            if (_ground != null)
            {
                Object.Destroy(_ground);
            }

            if (_hudGo != null)
            {
                Object.Destroy(_hudGo);
            }

            Physics.simulationMode = _previousMode;
        }

        [UnityTest]
        public IEnumerator RideBridgeAndHud_PerTick_AllocateNoGcMemory()
        {
            _ground = PlayModeRig.CreateGround("Ground_AllocationTrack", new Vector3(0f, -0.5f, 500f), Quaternion.identity, new Vector3(40f, 1f, 1200f));
            BikeSimulation sim = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            _ride = PlayModeRig.CreateRideRig("AllocBike", sim, 3u, Vector3.zero, Quaternion.identity);
            _hudGo = new GameObject("HudUnderTest");
            RideHudView hud = _hudGo.AddComponent<RideHudView>();
            DiagnosticHintsPresenter hints = _hudGo.AddComponent<DiagnosticHintsPresenter>();
            yield return null;

            _ride.SetControls(0.8f, 0f, 0f, 0.1f, false, false, false, false);
            for (int i = 0; i < 180; i++)
            {
                PlayModeRig.Step(_ride, null);
                EngineTelemetrySnapshot warm = _ride.LatestTelemetry;
                hud.Render(in warm);
                hints.Present(in warm, sim.GetActiveFaults());
            }

            MopedBikeController ride = _ride;
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    ride.SimulateStep(PlayModeRig.Dt);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    EngineTelemetrySnapshot t = ride.LatestTelemetry;
                    hud.Render(in t);
                    hints.Present(in t, sim.GetActiveFaults());
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.Greater(_ride.LatestTelemetry.Rpm, 0f);
            Assert.Greater(hud.RenderCount, 0);
        }
    }
}
