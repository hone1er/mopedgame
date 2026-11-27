using System.Collections;
using MopedSim.App;
using MopedSim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// A human can fix a roadside breakdown purely through the UI: the "engine dead" prompt appears, Interact pulls over, the panel
    /// lists the diagnosis / tool-roll state, a refused tow does not strand the player, and the Repair button resumes the ride.
    /// </summary>
    public sealed class StationUiScenarioTests
    {
        private const float Dt = 0.02f;

        private GameObject _appGo;
        private AppCompositionRoot _app;
        private InputAdapter _input;

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null)
            {
                Object.Destroy(_appGo);
            }
        }

        private void BuildApp(uint seed)
        {
            _appGo = new GameObject("AppRoot_StationUi");
            GameSessionStateMachine session = _appGo.AddComponent<GameSessionStateMachine>();
            _input = _appGo.AddComponent<InputAdapter>();
            _app = _appGo.AddComponent<AppCompositionRoot>();
            _app.BootstrapOnStart = false;
            _app.AssignCoreComponents(_input, session, null, null);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(seed));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50));
            _app.BootstrapWithWorld(world, null);
        }

        private void StepWorld(float seconds, in RideInputState input)
        {
            int steps = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                _app.World.Step(Dt, in input);
            }
        }

        private static Button FindButton(GameObject root, string name)
        {
            Button[] all = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) { return all[i]; }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator Breakdown_PromptThenInteract_OpensPanel_RefusedTowKeepsPlayerAtRoadside_RepairButtonResumesRide()
        {
            BuildApp(31u);
            _input.SetTestOverride(default(FrameControlInputs));
            yield return null;
            Assert.IsTrue(_app.StationUi.IsDockVisible, "Garage shows the station dock.");
            Assert.IsFalse(_app.StationUi.IsRoadsidePanelVisible);

            Assert.IsTrue(_app.TryStartRide());
            yield return null;
            Assert.IsFalse(_app.StationUi.IsDockVisible);

            RideInputState cruise = new RideInputState(0.8f);
            StepWorld(10f, in cruise);
            _app.ActiveBike.InduceFault(FaultIds.FouledPlug);
            StepWorld(6f, in cruise);
            Assert.IsTrue(_app.ActiveBike.LatestTelemetry.IsStalled);
            yield return null;
            yield return null;
            Assert.IsTrue(_app.StationUi.IsBreakdownPromptVisible, "A stalled bike with an active fault shows the pull-over prompt.");
            StringAssert.Contains("press [E]", _app.StationUi.BreakdownPromptText);

            _input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, true));
            yield return null;
            _input.SetTestOverride(default(FrameControlInputs));
            yield return null;
            Assert.AreEqual(GameSessionState.RoadsideRepair, _app.Session.CurrentState);
            Assert.IsTrue(_app.StationUi.IsRoadsidePanelVisible);
            Assert.IsFalse(_app.StationUi.IsBreakdownPromptVisible);
            Assert.AreEqual(1, _app.StationUi.RoadsidePanel.DisplayedFaultCount);

            // No cash: the tow is refused, the player stays at the roadside with a message instead of being stranded in Tow.
            _app.World.CashUsd = 0m;
            _app.StationUi.RoadsidePanel.TriggerBuddyTow();
            Assert.AreEqual(GameSessionState.RoadsideRepair, _app.Session.CurrentState, "A refused tow must not strand the player.");
            Assert.IsTrue(_app.StationUi.IsRoadsidePanelVisible);
            StringAssert.Contains("Tow failed", _app.StationUi.RoadsidePanel.DisplayedMessage);

            // Repair without tools: Core refuses and the panel says why.
            Button repair = FindButton(_app.StationUi.RoadsidePanel.gameObject, "Btn_Repair");
            Assert.IsNotNull(repair);
            repair.onClick.Invoke();
            StringAssert.Contains("Repair failed", _app.StationUi.RoadsidePanel.DisplayedMessage);
            Assert.AreEqual(GameSessionState.RoadsideRepair, _app.Session.CurrentState);

            Assert.IsTrue(_app.TryPackToolRollItem(ToolIds.SparkPlugWrench));
            Assert.IsTrue(_app.TryPackToolRollItem(ToolIds.SparePlug));
            yield return null;
            repair.onClick.Invoke();
            Assert.AreEqual(GameSessionState.Ride, _app.Session.CurrentState, "A successful repair resumes the ride.");
            Assert.AreEqual(0, _app.ActiveBike.GetActiveFaults().Count);
            yield return null;
            Assert.IsFalse(_app.StationUi.IsRoadsidePanelVisible);
        }

        [UnityTest]
        public IEnumerator Escape_AtRoadside_ResumesRide_AndPedalHomeReturnsToGarage()
        {
            BuildApp(32u);
            Assert.IsTrue(_app.TryStartRide());
            yield return null;
            Assert.IsTrue(_app.PullOverForRoadsideRepair());
            yield return null;
            Assert.IsTrue(_app.StationUi.IsRoadsidePanelVisible);

            Assert.IsTrue(_app.CloseTransientPanels(), "Esc closes the roadside panel before opening the pause menu.");
            Assert.AreEqual(GameSessionState.Ride, _app.Session.CurrentState);

            Assert.IsTrue(_app.PullOverForRoadsideRepair());
            yield return null;
            _app.StationUi.RoadsidePanel.TriggerPedalHome();
            Assert.AreEqual(GameSessionState.Garage, _app.Session.CurrentState);
            yield return null;
            Assert.IsTrue(_app.StationUi.IsDockVisible);
            Assert.IsFalse(_app.StationUi.IsRoadsidePanelVisible);
        }
    }
}
