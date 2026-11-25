using System.Collections;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Garage;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M3 exit scenarios (PROJECT_GUIDE.md section 7) driven through the real garage station components:
    /// DAG-blocked cylinder removal, over-torque strip + thread repair, under-torque leaks on the center-stand dyno,
    /// and a #56 -> #68 main jet swap changing AFR, RPM, and the plug chop reading.
    /// </summary>
    public sealed class GarageWrenchScenarioTests
    {
        private GameObject _garage;
        private GarageWorkstandController _workstand;
        private FastenerWrenchInteractor _wrench;
        private CarbWorkbenchController _carb;
        private PlugChopInspector _plug;
        private CenterStandDynoController _dyno;
        private GarageWrenchHudView _wrenchHud;
        private PlugChopChartView _plugChart;

        private GarageBikeContext BuildGarage(BikeSimulation bike, uint seed)
        {
            _garage = new GameObject("GarageUnderTest");
            _workstand = _garage.AddComponent<GarageWorkstandController>();
            _workstand.ReadMouseInput = false;
            _wrench = _garage.AddComponent<FastenerWrenchInteractor>();
            _wrench.ReadMouseInput = false;
            _carb = _garage.AddComponent<CarbWorkbenchController>();
            _plug = _garage.AddComponent<PlugChopInspector>();
            _dyno = _garage.AddComponent<CenterStandDynoController>();
            _dyno.AutoStepInFixedUpdate = false;
            _wrenchHud = _garage.AddComponent<GarageWrenchHudView>();
            _plugChart = _garage.AddComponent<PlugChopChartView>();

            GarageBikeContext context = new GarageBikeContext(bike, new SeededSimRandom(seed));
            _workstand.Bind(context);
            _wrench.Bind(context);
            _carb.Bind(context);
            _plug.Bind(context);
            _dyno.Bind(context);
            _wrench.ActiveFastenerChanged += _wrenchHud.ShowFastener;
            _wrench.WrenchResult += r => _wrenchHud.ShowResult(in r);
            _plug.PlugInspected += r => _plugChart.Render(in r);
            return context;
        }

        [TearDown]
        public void TearDown()
        {
            if (_garage != null)
            {
                Object.Destroy(_garage);
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
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
        public IEnumerator Cylinder_IsBlockedUntilHeadNutsExhaustAndIntakeRemoved_ThenPistonInspectable()
        {
            BuildGarage(MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50), 5u);
            yield return null;

            InteractableNode cylinderNode;
            Assert.IsTrue(_workstand.TryGetNode(SlotIds.Cylinder, out cylinderNode));
            Assert.IsFalse(cylinderNode.CanRemove, "Workstand must not highlight the cylinder as removable on a built engine.");

            WrenchActionResult blocked = _workstand.TryRemovePart(SlotIds.Cylinder);
            Assert.IsFalse(blocked.Succeeded);
            Assert.AreEqual(WrenchActionStatus.Blocked, blocked.Status);
            Assert.IsTrue(Contains(blocked.BlockingNodes, FastenerIds.HeadNut1));
            Assert.IsTrue(Contains(blocked.BlockingNodes, SlotIds.CylinderHead));
            Assert.IsTrue(Contains(blocked.BlockingNodes, SlotIds.Exhaust));
            Assert.IsTrue(Contains(blocked.BlockingNodes, SlotIds.IntakeManifold));

            string[] fasteners = new[]
            {
                FastenerIds.HeadNut1, FastenerIds.HeadNut2, FastenerIds.HeadNut3, FastenerIds.HeadNut4,
                FastenerIds.ExhaustFlange1, FastenerIds.ExhaustFlange2, FastenerIds.ExhaustFrameBracket,
                FastenerIds.Intake1, FastenerIds.Intake2
            };
            for (int i = 0; i < fasteners.Length; i++)
            {
                Assert.IsTrue(_wrench.SelectFastener(fasteners[i]), fasteners[i]);
                Assert.IsTrue(_wrench.Loosen().Succeeded, "Loosen " + fasteners[i]);
            }

            Assert.IsFalse(_workstand.TryRemovePart(SlotIds.Cylinder).Succeeded, "Head/exhaust/intake are still mounted.");
            Assert.IsTrue(_workstand.TryRemovePart(SlotIds.CylinderHead).Succeeded);
            Assert.IsTrue(_workstand.TryRemovePart(SlotIds.Exhaust).Succeeded);
            Assert.IsTrue(_workstand.TryRemovePart(SlotIds.IntakeManifold).Succeeded);

            Assert.IsTrue(_workstand.TryGetNode(SlotIds.Cylinder, out cylinderNode));
            Assert.IsTrue(cylinderNode.CanRemove, "Interactables refresh after each wrench action.");
            Assert.IsTrue(_workstand.TryRemovePart(SlotIds.Cylinder).Succeeded);

            InteractableNode pistonNode;
            Assert.IsTrue(_workstand.TryGetNode(SlotIds.Piston, out pistonNode));
            Assert.IsTrue(pistonNode.IsInstalled);
            Assert.IsTrue(pistonNode.CanRemove, "Pulled cylinder exposes the piston for inspection/removal.");
        }

        [UnityTest]
        public IEnumerator HeadNut_OverTorqueStrips_RepairThenInSpec_UnderTorqueLeaksOnDyno()
        {
            BikeSimulation bike = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BuildGarage(bike, 6u);
            yield return null;

            Assert.IsTrue(_wrench.SelectFastener(FastenerIds.HeadNut1));
            FastenerState nut = _wrench.ActiveFastener;
            Assert.AreEqual(FastenerIds.HeadNut1, _wrenchHud.ActiveFastenerId);

            _wrench.BeginTorque();
            float elapsed = 0f;
            while (_wrench.PendingTorqueNm <= nut.StripThresholdNm + 1f && elapsed < 30f)
            {
                _wrench.TickTorque(0.02f);
                _wrenchHud.SetAppliedTorque(_wrench.PendingTorqueNm);
                elapsed += 0.02f;
            }

            Assert.AreEqual(TorqueGaugeZone.Strip, _wrenchHud.CurrentZone);
            WrenchActionResult stripped = _wrench.ReleaseAndCommit();
            Assert.AreEqual(WrenchActionStatus.FastenerStripped, stripped.Status);
            Assert.IsTrue(nut.IsStripped, "Exceeding StripThresholdNm must strip the stud.");
            Assert.IsTrue(_wrenchHud.LastResultStripped);

            InteractableNode node;
            Assert.IsTrue(_workstand.TryGetNode(FastenerIds.HeadNut1, out node));
            Assert.IsTrue(node.IsStripped, "Workstand highlights the stripped stud for repair.");

            Assert.IsTrue(_wrench.RepairStrippedThreads().Succeeded);
            Assert.IsFalse(nut.IsStripped);
            Assert.IsTrue(_wrench.ApplyTorque(nut.TargetTorqueNm).Succeeded);
            Assert.IsTrue(nut.IsProperlyTorqued);

            // Under-torque comparison on the center-stand dyno.
            BikeSimulation tight = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            GarageBikeContext tightContext = new GarageBikeContext(tight, new SeededSimRandom(8u));
            CenterStandDynoController tightDyno = _garage.AddComponent<CenterStandDynoController>();
            tightDyno.AutoStepInFixedUpdate = false;
            tightDyno.Bind(tightContext);
            EngineTelemetrySnapshot tightRun = tightDyno.RunFor(10f, 1f, 0.02f);

            BikeSimulation loose = MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            GarageBikeContext looseContext = new GarageBikeContext(loose, new SeededSimRandom(8u));
            _wrench.Bind(looseContext);
            _dyno.Bind(looseContext);
            Assert.IsTrue(_wrench.SelectFastener(FastenerIds.HeadNut1));
            Assert.IsTrue(_wrench.ApplyTorque(2f).Succeeded);
            Assert.IsTrue(_wrench.SelectFastener(FastenerIds.HeadNut2));
            Assert.IsTrue(_wrench.ApplyTorque(2f).Succeeded);
            Assert.IsTrue(_wrench.SelectFastener(FastenerIds.ExhaustFlange1));
            Assert.IsTrue(_wrench.ApplyTorque(1f).Succeeded);
            EngineTelemetrySnapshot looseRun = _dyno.RunFor(10f, 1f, 0.02f);

            Assert.Greater(looseRun.AirLeakSeverity, tightRun.AirLeakSeverity + 0.1f, "Under-torqued head nuts must leak air.");
            Assert.Greater(looseRun.ExhaustLeakFactor, tightRun.ExhaustLeakFactor + 0.1f, "Loose flange must leak exhaust.");
        }

        [UnityTest]
        public IEnumerator MainJet56To68_OnDyno_UpdatesAfrRpmAndPlugChop()
        {
            BikeSimulation bike = MopedScenarioFixtures.Create70ccKittedBike("jet_56");
            BuildGarage(bike, 9u);
            List<PartDefinition> parts = new List<PartDefinition>(MopedScenarioFixtures.CreateCatalog().Values);
            _carb.SetJetCatalog(parts);
            yield return null;

            Assert.AreEqual(56, _carb.CurrentMainJetSize);
            EngineTelemetrySnapshot lean = _dyno.RunFor(20f, 1f, 0.02f);
            SparkPlugReading leanPlug = _plug.Inspect();
            Assert.AreEqual(leanPlug.ColorZone, _plugChart.DisplayedZone);

            WrenchActionResult swap = _carb.SwapMainJet(68);
            Assert.IsTrue(swap.Succeeded, swap.Message);
            Assert.AreEqual(68, _carb.CurrentMainJetSize);

            EngineTelemetrySnapshot rich = _dyno.RunFor(20f, 1f, 0.02f);
            SparkPlugReading richPlug = _plug.Inspect();

            Assert.IsFalse(lean.IsSeized);
            Assert.IsFalse(rich.IsSeized);
            Assert.Less(rich.EffectiveAfr, lean.EffectiveAfr - 1.0f, "#68 must richen AFR (lean " + lean.EffectiveAfr + " -> " + rich.EffectiveAfr + ").");
            Assert.AreNotEqual(lean.Rpm, rich.Rpm, "Jetting change must move dyno RPM.");
            Assert.Greater(richPlug.InsulatorColorIndex, leanPlug.InsulatorColorIndex, "Plug chop should darken from lean toward brown.");
            Assert.AreNotEqual(leanPlug.ColorZone, richPlug.ColorZone);
            Assert.AreEqual(richPlug.ColorZone, _plugChart.DisplayedZone);
            Assert.Greater(_dyno.PeakRpm, 0f);
            Assert.Greater(_dyno.PeakWheelSpeedMps, 0f);
        }
    }
}
