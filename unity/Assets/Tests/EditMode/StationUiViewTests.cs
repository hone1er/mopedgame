using System.IO;
using System.Reflection;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MopedSim.Tests.EditMode
{
    /// <summary>EditMode coverage for the garage station dock states / hints, the roadside repair panel diagnosis + tool roll, and the plug panel empty state.</summary>
    public sealed class StationUiViewTests
    {
        private GameObject _canvasGo;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("StationUiTestCanvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null)
            {
                Object.DestroyImmediate(_canvasGo);
            }
        }

        private GameObject Host(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            return go;
        }

        private static CatalogDatabase LoadCatalog()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs"));
            return CatalogLoader.LoadFromDirectory(dir);
        }

        [Test]
        public void Dock_DisabledStation_ShowsComingSoonHint_AndHintFallsBack_AllocationFreeIdle()
        {
            GameObject host = Host("Dock");
            GarageStationDockView dock = host.AddComponent<GarageStationDockView>();
            dock.BuildDefaultLayout((RectTransform)host.transform);

            Assert.IsFalse(dock.GetButton("Btn_Inventory").interactable, "Owned-parts install has no API yet: disabled by default.");
            dock.SetButtonState("Btn_Jobs", false, "Coming soon: jobs");
            Assert.IsFalse(dock.GetButton("Btn_Jobs").interactable);
            dock.SetHint("Coming soon: jobs");
            Assert.AreEqual("Coming soon: jobs", dock.DisplayedHint);
            dock.SetHint(string.Empty);
            Assert.AreEqual(GarageStationDockView.DefaultHint, dock.DisplayedHint);

            dock.SetButtonLabel("Btn_MixRatio", "PREMIX", "Mix 32:1");
            StringAssert.Contains("Mix 32:1", dock.GetButton("Btn_MixRatio").GetComponentInChildren<Text>().text);

            dock.RenderStatus(10m, 1, "#56", 50f, 0f, 0f, "x");
            dock.SetHint(string.Empty);
            TestDelegate idle = delegate
            {
                for (int i = 0; i < 64; i++)
                {
                    dock.RenderStatus(10m, 1, "#56", 50f, 0f, 0f, "x");
                    dock.SetHint(string.Empty);
                }
            };
            idle();
            Assert.That(idle, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RoadsidePanel_ShowsDiagnosis_ToolRollState_Message_AndPedalHome()
        {
            CatalogDatabase db = LoadCatalog();
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(3u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike("player_bike", bike);
            bike.InduceFault(FaultIds.FouledPlug);

            GameObject host = Host("Roadside");
            RoadsideRepairPanelView panel = host.AddComponent<RoadsideRepairPanelView>();
            panel.BuildDefaultLayout((RectTransform)host.transform);
            panel.RenderFaults(bike, world, db);

            Transform row = host.transform.Find("RoadsideRepair_Card/FaultRows/FaultRow_" + FaultIds.FouledPlug);
            Assert.IsNotNull(row, "Missing fault row.");
            Text info = row.GetComponentInChildren<Text>();
            StringAssert.Contains("MISSING", info.text, "Empty tool roll: the required wrench and spare plug are flagged missing.");
            StringAssert.Contains(db.FaultMeta[FaultIds.FouledPlug].DiagnosticHint, info.text, "Diagnosis text comes from the fault catalog.");
            Text label = row.Find("Btn_Repair").GetComponentInChildren<Text>();
            Assert.AreEqual("Missing items", label.text);

            Assert.IsTrue(world.TryPackToolRollItem(ToolIds.SparkPlugWrench));
            Assert.IsTrue(world.TryPackToolRollItem(ToolIds.SparePlug));
            panel.RenderFaults(bike, world, db);
            StringAssert.Contains("Spark Plug Wrench (packed)", info.text);
            StringAssert.Contains("Spare Plug (packed)", info.text);
            Assert.AreEqual("Repair", label.text, "One option (swap a spare plug) is fully stocked.");

            int pedal = 0;
            panel.PedalHomeRequested += delegate { pedal++; };
            host.transform.Find("RoadsideRepair_Card/Footer/Btn_PedalHome").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, pedal);

            panel.SetMessage("Tow failed: no cash");
            Assert.AreEqual("Tow failed: no cash", panel.DisplayedMessage);
            panel.SetFooterInteractable(false, true, true);
            Assert.IsFalse(host.transform.Find("RoadsideRepair_Card/Footer/Btn_BuddyTow").GetComponent<Button>().interactable);
        }

        [Test]
        public void PlugChart_EmptyState_ExplainsHowToGetAReading_ThenShowsVerdict_AndResets()
        {
            GameObject host = Host("Plug");
            PlugChopChartView chart = host.AddComponent<PlugChopChartView>();
            Text zone = AddText(host, "_zoneLabel", chart);
            Text summary = AddText(host, "_summaryLabel", chart);
            Text gap = AddText(host, "_gapLabel", chart);

            chart.ShowEmptyState();
            Assert.IsFalse(chart.HasReading);
            Assert.AreEqual(PlugChopChartView.EmptyTitle, zone.text);
            StringAssert.Contains("Plug Chop", summary.text);
            StringAssert.Contains("lean", gap.text);

            SparkPlugReading lean = new SparkPlugReading(0.6f, 0f, 0.05f, PlugColorZone.ChalkWhite, 6, true, "Running lean.");
            chart.Render(in lean);
            Assert.IsTrue(chart.HasReading);
            StringAssert.Contains("LEAN", zone.text);
            Assert.AreEqual("Running lean.", summary.text);
            Assert.AreEqual(PlugChopChartView.VerdictTint(PlugColorZone.ChalkWhite), zone.color);

            chart.ShowEmptyState();
            Assert.IsFalse(chart.HasReading);
            Assert.AreEqual(PlugChopChartView.EmptyTitle, zone.text);
        }

        private static Text AddText(GameObject host, string field, PlugChopChartView chart)
        {
            GameObject go = new GameObject(field, typeof(RectTransform));
            go.transform.SetParent(host.transform, false);
            Text t = go.AddComponent<Text>();
            typeof(PlugChopChartView).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(chart, t);
            return t;
        }
    }
}
