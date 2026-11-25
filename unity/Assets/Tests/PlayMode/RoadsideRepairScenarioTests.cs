using System.Collections;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Garage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M4 exit scenarios (PROJECT_GUIDE.md section 7) through AppCompositionRoot + GameSessionStateMachine:
    /// fouled plug roadside repair with/without the Tool Roll, Buddy Tow / Pedal of Shame recovery, and hard seize
    /// persisting the scored cylinder back into the Garage (including a save/reload roundtrip via MopedSim.Data).
    /// </summary>
    public sealed class RoadsideRepairScenarioTests
    {
        private const float Dt = 0.02f;

        private GameObject _appGo;
        private GameObject _garageGo;
        private AppCompositionRoot _app;
        private GameSessionStateMachine _session;
        private string _savePath;

        private AppCompositionRoot BuildApp(uint seed)
        {
            _appGo = new GameObject("AppRoot_M4");
            _session = _appGo.AddComponent<GameSessionStateMachine>();
            _app = _appGo.AddComponent<AppCompositionRoot>();
            _app.BootstrapOnStart = false;
            _app.AssignCoreComponents(null, _session, null, null);

            SimulationWorld world = new SimulationWorld(new SeededSimRandom(seed));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50));
            _app.BootstrapWithWorld(world, null);
            return _app;
        }

        [TearDown]
        public void TearDown()
        {
            if (_appGo != null)
            {
                Object.Destroy(_appGo);
            }

            if (_garageGo != null)
            {
                Object.Destroy(_garageGo);
            }

            if (!string.IsNullOrEmpty(_savePath) && File.Exists(_savePath))
            {
                File.Delete(_savePath);
            }
        }

        private EngineTelemetrySnapshot Ride(float seconds, in RideInputState input)
        {
            EngineTelemetrySnapshot t = _app.ActiveBike.LatestTelemetry;
            int steps = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                t = _app.World.Step(Dt, in input);
            }

            return t;
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        [UnityTest]
        public IEnumerator FouledPlug_WithWrenchAndSparePlug_RepairsRoadside_AndRideResumes()
        {
            BuildApp(11u);
            Assert.IsTrue(_app.TryPackToolRollItem(ToolIds.SparkPlugWrench));
            Assert.IsTrue(_app.TryPackToolRollItem(ToolIds.SparePlug));
            Assert.IsTrue(_app.TryStartRide());
            yield return null;

            RideInputState cruise = new RideInputState(0.8f);
            Ride(10f, in cruise);
            _app.ActiveBike.InduceFault(FaultIds.FouledPlug);
            EngineTelemetrySnapshot fouled = Ride(6f, in cruise);
            Assert.IsTrue(fouled.IsStalled, "A fouled plug must stall the engine.");

            Assert.IsTrue(_app.PullOverForRoadsideRepair());
            Assert.AreEqual(GameSessionState.RoadsideRepair, _session.CurrentState);

            RoadsideRepairResult repair = _app.AttemptRoadsideRepair(FaultIds.FouledPlug);
            Assert.IsTrue(repair.Succeeded, repair.Message);
            Assert.IsTrue(repair.ConsumedSpare);
            Assert.IsFalse(Contains(_app.World.ActiveToolRoll, ToolIds.SparePlug), "Spare plug is consumed from the Tool Roll.");
            Assert.IsTrue(Contains(_app.World.ActiveToolRoll, ToolIds.SparkPlugWrench), "Wrench stays in the Tool Roll.");
            Assert.AreEqual(GameSessionState.Ride, _session.CurrentState, "Ride resumes once no faults remain.");

            RideInputState pedalStart = new RideInputState(0.6f, pedalAssist: true);
            Ride(6f, in pedalStart);
            EngineTelemetrySnapshot resumed = Ride(6f, in cruise);
            Assert.IsTrue(resumed.IsRunning, "Pedal-start should refire the fresh plug.");
            Assert.Greater(resumed.VehicleSpeedMph, 10f);
            Assert.AreEqual(0, _app.ActiveBike.GetActiveFaults().Count);
        }

        [UnityTest]
        public IEnumerator FouledPlug_WithoutTools_BlocksRepair_TowAndPedalOfShameRecoverToGarage()
        {
            BuildApp(11u);
            Assert.IsTrue(_app.TryStartRide());
            yield return null;

            RideInputState cruise = new RideInputState(0.8f);
            Ride(10f, in cruise);
            _app.ActiveBike.InduceFault(FaultIds.FouledPlug);
            Assert.IsTrue(Ride(6f, in cruise).IsStalled);
            Assert.IsTrue(_app.PullOverForRoadsideRepair());

            RoadsideRepairResult repair = _app.AttemptRoadsideRepair(FaultIds.FouledPlug);
            Assert.IsFalse(repair.Succeeded);
            Assert.IsTrue(Contains(repair.MissingToolsOrSpares, ToolIds.SparkPlugWrench));
            Assert.AreEqual(GameSessionState.RoadsideRepair, _session.CurrentState, "Blocked repair leaves the rider stranded.");

            decimal cashBefore = _app.World.CashUsd;
            RecoveryResult tow = _app.RecoverWithBuddyTow();
            Assert.IsTrue(tow.Succeeded, tow.Message);
            Assert.Greater(tow.CashCostUsd, 0m);
            Assert.AreEqual(cashBefore - tow.CashCostUsd, _app.World.CashUsd);
            Assert.AreEqual(GameSessionState.Garage, _session.CurrentState);

            // Second outing: no cash spent this time — pedal home instead.
            Assert.IsTrue(_app.TryStartRide());
            Assert.IsTrue(_app.PullOverForRoadsideRepair());
            float staminaBefore = _app.ActiveBike.RiderStamina;
            RecoveryResult pedal = _app.RecoverWithPedalOfShame();
            Assert.IsTrue(pedal.Succeeded);
            Assert.Less(_app.ActiveBike.RiderStamina, staminaBefore, "Pedal of Shame spends rider stamina (Core).");
            Assert.AreEqual(GameSessionState.Garage, _session.CurrentState);
            Assert.IsTrue(_app.ActiveBike.GetActiveFaults().Count > 0, "Fault persists until fixed in the garage.");
        }

        [UnityTest]
        public IEnumerator HardSeize_BlocksRoadsideFix_TowsHome_AndScoredCylinderPersistsIntoGarage()
        {
            BuildApp(12u);
            string[] fullRoll = new[] { ToolIds.SparkPlugWrench, ToolIds.SparePlug, ToolIds.Flathead, ToolIds.Wrench10mm, ToolIds.PenetratingOil, ToolIds.WireBrush };
            for (int i = 0; i < fullRoll.Length; i++)
            {
                Assert.IsTrue(_app.TryPackToolRollItem(fullRoll[i]));
            }

            Assert.IsTrue(_app.TryStartRide());
            yield return null;

            RideInputState cruise = new RideInputState(0.8f);
            Ride(10f, in cruise);
            _app.ActiveBike.InduceFault(FaultIds.HardSeize);
            Assert.IsTrue(_app.ActiveBike.LatestTelemetry.IsHardSeized);
            Assert.IsTrue(_app.PullOverForRoadsideRepair());

            RoadsideRepairResult repair = _app.AttemptRoadsideRepair(FaultIds.HardSeize);
            Assert.IsFalse(repair.Succeeded);
            Assert.IsTrue(repair.RequiresGarageRebuild, "Hard seize is garage-only even with a full Tool Roll.");

            RecoveryResult tow = _app.RecoverWithBuddyTow();
            Assert.IsTrue(tow.Succeeded, tow.Message);
            Assert.AreEqual(GameSessionState.Garage, _session.CurrentState);

            // Garage work stand sees the same scored top end.
            _garageGo = new GameObject("GarageWorkstand_M4");
            GarageWorkstandController workstand = _garageGo.AddComponent<GarageWorkstandController>();
            workstand.ReadMouseInput = false;
            workstand.Bind(_app.GarageContext);
            InteractableNode cylinder;
            Assert.IsTrue(workstand.TryGetNode(SlotIds.Cylinder, out cylinder));
            Assert.IsTrue(cylinder.IsInstalled);
            Assert.IsTrue(_app.ActiveBike.LatestTelemetry.IsHardSeized);
            Assert.Less(_app.ActiveBike.LatestTelemetry.CompressionHealth, 0.5f, "Scored cylinder keeps low compression.");

            // Persist across save/reload through MopedSim.Data's versioned SaveMigrator.
            _savePath = Path.Combine(Application.temporaryCachePath, "m4_hard_seize_save.json");
            _app.SaveToPath(_savePath);
            Object.Destroy(_appGo);
            yield return null;

            BuildApp(12u);
            Assert.IsFalse(_app.ActiveBike.LatestTelemetry.IsHardSeized, "Fresh session starts healthy before loading.");
            int rejected = _app.LoadFromPath(_savePath);
            Assert.AreEqual(0, rejected);
            Assert.IsTrue(_app.ActiveBike.LatestTelemetry.IsHardSeized, "Reloaded garage bike is still hard-seized.");
            Assert.Less(_app.ActiveBike.LatestTelemetry.CompressionHealth, 0.5f);
            Assert.AreEqual(fullRoll.Length, _app.World.ActiveToolRoll.Count);
        }
    }
}
