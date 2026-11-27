using System;
using System.IO;
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
    /// <summary>
    /// EditMode coverage for Wave 4B playable UI views:
    /// GarageStationDockView, RoadsideRepairPanelView, JobDispatchModalView, JobBoardHudView.RenderWaypoint,
    /// PartsCatalogShopView, SwapMeetView, ToolRollOverlayView, and SettingsMenuView.
    /// </summary>
    public sealed class PlayableUiViewsTests
    {
        private GameObject _canvasGo;
        private RectTransform _rootRt;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("PlayableUiTestCanvas", typeof(RectTransform), typeof(Canvas));
            _rootRt = (RectTransform)_canvasGo.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_canvasGo);
                _canvasGo = null;
                _rootRt = null;
            }
        }

        [Test]
        public void GarageStationDockView_BuildDefaultLayout_AllButtonsFireEvents_AndRenderStatusDirtyChecks()
        {
            GameObject host = CreateChildPanel("DockHost");
            GarageStationDockView dock = host.AddComponent<GarageStationDockView>();
            dock.BuildDefaultLayout((RectTransform)host.transform);
            int buttonCountAfterFirst = host.GetComponentsInChildren<Button>(true).Length;
            dock.BuildDefaultLayout((RectTransform)host.transform);
            Assert.AreEqual(buttonCountAfterFirst, host.GetComponentsInChildren<Button>(true).Length, "BuildDefaultLayout must be idempotent.");
            Assert.AreEqual(11, buttonCountAfterFirst);

            int rideCount = 0;
            int shopCount = 0;
            int swapCount = 0;
            int jobsCount = 0;
            int settingsCount = 0;
            int paintCount = 0;
            int dynoCount = 0;
            int plugCount = 0;
            int refuelCount = 0;
            int jetCount = 0;
            int saveCount = 0;

            dock.RideRequested += delegate { rideCount++; };
            dock.OpenShopRequested += delegate { shopCount++; };
            dock.OpenSwapMeetRequested += delegate { swapCount++; };
            dock.OpenJobsRequested += delegate { jobsCount++; };
            dock.OpenSettingsRequested += delegate { settingsCount++; };
            dock.CyclePaintRequested += delegate { paintCount++; };
            dock.DynoPullRequested += delegate { dynoCount++; };
            dock.PlugChopRequested += delegate { plugCount++; };
            dock.RefuelRequested += delegate { refuelCount++; };
            dock.SwapJetRequested += delegate { jetCount++; };
            dock.SaveRequested += delegate { saveCount++; };

            ClickNamedButton(host, "Btn_Ride");
            ClickNamedButton(host, "Btn_Shop");
            ClickNamedButton(host, "Btn_SwapMeet");
            ClickNamedButton(host, "Btn_Jobs");
            ClickNamedButton(host, "Btn_Paint");
            ClickNamedButton(host, "Btn_DynoPull");
            ClickNamedButton(host, "Btn_PlugChop");
            ClickNamedButton(host, "Btn_Refuel");
            ClickNamedButton(host, "Btn_SwapJet");
            ClickNamedButton(host, "Btn_Save");
            ClickNamedButton(host, "Btn_Settings");

            Assert.AreEqual(1, rideCount);
            Assert.AreEqual(1, shopCount);
            Assert.AreEqual(1, swapCount);
            Assert.AreEqual(1, jobsCount);
            Assert.AreEqual(1, paintCount);
            Assert.AreEqual(1, dynoCount);
            Assert.AreEqual(1, plugCount);
            Assert.AreEqual(1, refuelCount);
            Assert.AreEqual(1, jetCount);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, settingsCount);

            dock.RenderStatus(145.50m, 30, "jet_main_68", 82.4f, 2.7f, 6750f, "Dyno ready");
            string firstStatus = dock.DisplayedStatusText;
            StringAssert.Contains("$145.50", firstStatus);
            StringAssert.Contains("Rep: 30", firstStatus);
            StringAssert.Contains("jet_main_68", firstStatus);
            StringAssert.Contains("82%", firstStatus);
            StringAssert.Contains("2.7 hp", firstStatus);
            StringAssert.Contains("6750 rpm", firstStatus);
            Assert.AreEqual("Dyno ready", dock.LastFeedback);

            // Warm steady-state branch & delegate once (Mono JIT), then assert zero GC allocations.
            string jetId = "jet_main_68";
            string feedback = "Dyno ready";
            TestDelegate steadyRender = delegate
            {
                for (int i = 0; i < 64; i++)
                {
                    dock.RenderStatus(145.50m, 30, jetId, 82.4f, 2.7f, 6750f, feedback);
                }
            };
            steadyRender();
            Assert.That(steadyRender, Is.Not.AllocatingGCMemory());
            Assert.AreSame(firstStatus, dock.DisplayedStatusText);
        }

        [Test]
        public void RoadsideRepairPanelView_RendersActiveFaults_AndButtonsFireEvents()
        {
            CatalogDatabase db = LoadCatalog();
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(19u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike("player_bike", bike);
            Assert.IsTrue(world.TryPackToolRollItem(ToolIds.SparkPlugWrench));
            Assert.IsTrue(world.TryPackToolRollItem(ToolIds.SparePlug));

            GameObject host = CreateChildPanel("RoadsideHost");
            RoadsideRepairPanelView panel = host.AddComponent<RoadsideRepairPanelView>();
            panel.BuildDefaultLayout((RectTransform)host.transform);

            panel.RenderFaults(bike, world, db);
            Assert.AreEqual(0, panel.DisplayedFaultCount);

            bike.InduceFault(FaultIds.FouledPlug);
            panel.RenderFaults(bike, world, db);
            Assert.AreEqual(1, panel.DisplayedFaultCount);
            StringAssert.Contains("1 active fault", panel.DisplayedSummaryText);

            string summaryBefore = panel.DisplayedSummaryText;
            Assert.That(delegate
            {
                for (int i = 0; i < 32; i++)
                {
                    panel.RenderFaults(bike, world, db);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreSame(summaryBefore, panel.DisplayedSummaryText);

            string repairedFault = string.Empty;
            int towCount = 0;
            int resumeCount = 0;
            panel.RepairFaultRequested += delegate(string id) { repairedFault = id; };
            panel.BuddyTowRequested += delegate { towCount++; };
            panel.ResumeRideRequested += delegate { resumeCount++; };

            Transform faultRow = FindChildByName(host.transform, "FaultRow_" + FaultIds.FouledPlug);
            Assert.IsNotNull(faultRow, "Expected generated row for fault_fouled_plug.");
            Button repairBtn = faultRow.GetComponentInChildren<Button>(true);
            Assert.IsNotNull(repairBtn);
            Assert.IsTrue(repairBtn.interactable);
            repairBtn.onClick.Invoke();
            ClickNamedButton(host, "Btn_BuddyTow");
            ClickNamedButton(host, "Btn_ResumeRide");

            Assert.AreEqual(FaultIds.FouledPlug, repairedFault);
            Assert.AreEqual(1, towCount);
            Assert.AreEqual(1, resumeCount);
        }

        [Test]
        public void JobDispatchModalView_RendersCatalogJobs_AndButtonsFireEvents()
        {
            CatalogDatabase db = LoadCatalog();
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(23u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike("player_bike", bike);

            GameObject host = CreateChildPanel("JobDispatchHost");
            JobDispatchModalView modal = host.AddComponent<JobDispatchModalView>();
            modal.BuildDefaultLayout((RectTransform)host.transform);
            modal.RenderCatalog(db, world, bike, null, JobSessionStatus.Idle, true, "Dispatch online");

            Assert.AreEqual(db.JobList.Count, modal.DisplayedJobCount);
            Assert.Greater(modal.DisplayedJobCount, 0);
            StringAssert.Contains("Dispatch online", modal.DisplayedFeedback);

            // Stock wire rack fits 1-slot coffee run, but not 2-slot Mission parts rush.
            Transform coffeeRow = FindChildByName(host.transform, "JobRow_gig_west_oakland_coffee");
            Assert.IsNotNull(coffeeRow);
            Button coffeeAccept = coffeeRow.GetComponentInChildren<Button>(true);
            Assert.IsTrue(coffeeAccept.interactable);

            Transform missionRow = FindChildByName(host.transform, "JobRow_gig_mission_parts_rush");
            Assert.IsNotNull(missionRow);
            Button missionAccept = missionRow.GetComponentInChildren<Button>(true);
            Assert.IsFalse(missionAccept.interactable, "2-slot job must show locked on stock 1-slot wire rack.");

            string acceptedJobId = string.Empty;
            int abandonCount = 0;
            int collectCount = 0;
            int closeCount = 0;
            modal.AcceptJobRequested += delegate(string id) { acceptedJobId = id; };
            modal.AbandonJobRequested += delegate { abandonCount++; };
            modal.CollectRewardRequested += delegate { collectCount++; };
            modal.CloseRequested += delegate { closeCount++; };

            coffeeAccept.onClick.Invoke();
            ClickNamedButton(host, "Btn_AbandonActive");
            ClickNamedButton(host, "Btn_CollectReward");
            ClickNamedButton(host, "Btn_Close");

            Assert.AreEqual("gig_west_oakland_coffee", acceptedJobId);
            Assert.AreEqual(1, abandonCount);
            Assert.AreEqual(1, collectCount);
            Assert.AreEqual(1, closeCount);
        }

        [Test]
        public void JobBoardHudView_RenderWaypoint_UpdatesLabelAndSteadyStateAllocatesZeroGc()
        {
            GameObject host = CreateChildPanel("JobBoardHost");
            JobBoardHudView hud = host.AddComponent<JobBoardHudView>();
            GameObject labelGo = new GameObject("WaypointLabel", typeof(RectTransform));
            labelGo.transform.SetParent(host.transform, false);
            Text waypointText = labelGo.AddComponent<Text>();
            hud.ConfigureWaypointLabel(waypointText);

            hud.RenderWaypoint("Valencia Drop", 148.3f, 72.4f);
            Assert.AreEqual("Valencia Drop", hud.DisplayedWaypointLabel);
            Assert.AreEqual(148.3f, hud.DisplayedWaypointDistanceMeters, 1e-4f);
            Assert.AreEqual(72.4f, hud.DisplayedWaypointBearingDegrees, 1e-4f);
            StringAssert.Contains("Valencia Drop", waypointText.text);
            StringAssert.Contains("148 m", waypointText.text);
            StringAssert.Contains("72°", waypointText.text);

            int writesAfterFirst = hud.TextWriteCount;
            // Warm steady-state branch & delegate once (Mono JIT), then assert zero Text writes and zero GC allocations.
            string waypointName = "Valencia Drop";
            TestDelegate steadyWaypoint = delegate
            {
                for (int i = 0; i < 200; i++)
                {
                    hud.RenderWaypoint(waypointName, 148.3f, 72.4f);
                }
            };
            steadyWaypoint();
            Assert.AreEqual(writesAfterFirst, hud.TextWriteCount, "Unchanged waypoint must perform zero Text writes.");
            Assert.That(steadyWaypoint, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(writesAfterFirst, hud.TextWriteCount, "Unchanged waypoint must perform zero Text writes.");

            // Updating distance updates label cleanly.
            hud.RenderWaypoint("Valencia Drop", 95.0f, 12.0f);
            Assert.AreEqual(writesAfterFirst + 1, hud.TextWriteCount);
            StringAssert.Contains("95 m", waypointText.text);
        }

        [Test]
        public void PartsCatalogShopView_BuildDefaultLayout_RenderCatalog_AndButtonsFireEvents()
        {
            CatalogDatabase db = LoadCatalog();
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(31u), db.GetZone("Zone_WestOakland"));
            world.CashUsd = 500m;
            world.RegisterBike("player_bike", bike);

            GameObject host = CreateChildPanel("ShopHost");
            PartsCatalogShopView shop = host.AddComponent<PartsCatalogShopView>();
            shop.BuildDefaultLayout((RectTransform)host.transform);
            shop.RenderCatalog(db, world, bike, "Parts ready");

            shop.SetFitsBikeOnly(false);
            int forSale = 0;
            for (int i = 0; i < db.PartList.Count; i++)
            {
                if (PartsCatalogShopView.TabOf(db.PartList[i]).HasValue) { forSale++; }
            }

            Assert.AreEqual(forSale, shop.VisibleCount);
            Assert.AreEqual("Parts ready", shop.DisplayedFeedback);

            string boughtPartId = string.Empty;
            string installedPartId = string.Empty;
            int closeCount = 0;
            shop.BuyPartRequested += delegate(string id) { boughtPartId = id; };
            shop.InstallPartRequested += delegate(string id) { installedPartId = id; };
            shop.CloseRequested += delegate { closeCount++; };

            PartDefinition firstPart = db.PartList[0];
            Transform firstRow = FindChildByName(host.transform, "PartRow_" + firstPart.Id);
            Assert.IsNotNull(firstRow);
            ClickNamedButton(firstRow.gameObject, "Btn_Buy");
            ClickNamedButton(firstRow.gameObject, "Btn_Install");
            ClickNamedButton(host, "Btn_Close");

            Assert.AreEqual(firstPart.Id, boughtPartId);
            Assert.AreEqual(firstPart.Id, installedPartId);
            Assert.AreEqual(1, closeCount);
        }

        [Test]
        public void SwapMeetView_BuildDefaultLayout_ButtonsFireInspectBuyRotateAndClose()
        {
            CatalogDatabase db = LoadCatalog();
            SwapMeetMarket market = new SwapMeetMarket(db.PartList, 77u);
            Assert.Greater(market.Offers.Count, 0);

            GameObject host = CreateChildPanel("SwapMeetHost");
            SwapMeetView view = host.AddComponent<SwapMeetView>();
            view.BuildDefaultLayout((RectTransform)host.transform);
            view.SetMarket(market);

            string inspectedId = string.Empty;
            string boughtId = string.Empty;
            int rotateCount = 0;
            int closeCount = 0;
            view.InspectOfferRequested += delegate(string id) { inspectedId = id; };
            view.BuyOfferRequested += delegate(string id) { boughtId = id; };
            view.RotateRequested += delegate { rotateCount++; };
            view.CloseRequested += delegate { closeCount++; };

            string firstOfferId = market.Offers[0].OfferId;
            Transform offerRow = FindChildByName(host.transform, "OfferRow_" + firstOfferId);
            Assert.IsNotNull(offerRow);
            ClickNamedButton(offerRow.gameObject, "Btn_Inspect");
            ClickNamedButton(offerRow.gameObject, "Btn_Buy");
            ClickNamedButton(host, "Btn_RotateOfferings");
            ClickNamedButton(host, "Btn_Close");

            Assert.AreEqual(firstOfferId, inspectedId);
            Assert.AreEqual(firstOfferId, boughtId);
            Assert.AreEqual(1, rotateCount);
            Assert.AreEqual(1, closeCount);
        }

        [Test]
        public void ToolRollOverlayView_BuildDefaultLayout_RenderWorld_AndToggleAndCloseFire()
        {
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(41u));
            Assert.IsTrue(world.TryPackToolRollItem(ToolIds.SparkPlugWrench));

            GameObject host = CreateChildPanel("ToolRollHost");
            ToolRollOverlayView view = host.AddComponent<ToolRollOverlayView>();
            view.BuildDefaultLayout((RectTransform)host.transform);
            view.SetAvailableItems(new string[] { ToolIds.SparkPlugWrench, ToolIds.Flathead });
            view.Render(world);

            Assert.AreEqual(1, view.PackedCount);
            Assert.AreEqual(world.ToolRollCapacity, view.Capacity);

            string toggledItem = string.Empty;
            int closeCount = 0;
            view.ToggleToolRollItemRequested += delegate(string id) { toggledItem = id; };
            view.CloseRequested += delegate { closeCount++; };

            Transform flatheadRow = FindChildByName(host.transform, "ToolRow_" + ToolIds.Flathead);
            Assert.IsNotNull(flatheadRow);
            ClickNamedButton(flatheadRow.gameObject, "Btn_Toggle");
            ClickNamedButton(host, "Btn_Close");

            Assert.AreEqual(ToolIds.Flathead, toggledItem);
            Assert.AreEqual(1, closeCount);
        }

        [Test]
        public void SettingsMenuView_BuildDefaultLayout_InteractiveControlsEmitEvents()
        {
            GameObject host = CreateChildPanel("SettingsHost");
            DiagnosticHintsPresenter presenter = host.AddComponent<DiagnosticHintsPresenter>();
            SettingsMenuView view = host.AddComponent<SettingsMenuView>();
            view.Bind(SettingsPreferencesStore.CreateDefaults(), presenter);
            view.BuildDefaultLayout((RectTransform)host.transform);

            int hintsCount = 0;
            int audioCount = 0;
            int qualityCount = 0;
            int appliedCount = 0;
            int closeCount = 0;
            float lastMaster = -1f;

            view.DiagnosticHintsToggled += delegate { hintsCount++; };
            view.AudioMixChanged += delegate(AudioMixSettings mix) { audioCount++; lastMaster = mix.MasterVolume; };
            view.GraphicsPresetChanged += delegate { qualityCount++; };
            view.SettingsApplied += delegate { appliedCount++; };
            view.CloseRequested += delegate { closeCount++; };

            Toggle toggle = host.GetComponentInChildren<Toggle>(true);
            Assert.IsNotNull(toggle);
            toggle.isOn = false;
            Assert.AreEqual(1, hintsCount);
            Assert.IsFalse(view.DiagnosticHintsEnabled);

            Transform masterSliderTf = FindChildByName(host.transform, "Slider_MasterVolume");
            Assert.IsNotNull(masterSliderTf);
            Slider masterSlider = masterSliderTf.GetComponent<Slider>();
            masterSlider.value = 0.45f;
            Assert.AreEqual(1, audioCount);
            Assert.AreEqual(0.45f, lastMaster, 1e-4f);

            ClickNamedButton(host, "Btn_CycleQuality");
            Assert.AreEqual(1, qualityCount);

            ClickNamedButton(host, "Btn_Apply");
            Assert.AreEqual(1, appliedCount);

            ClickNamedButton(host, "Btn_ResetDefaults");
            Assert.IsTrue(view.DiagnosticHintsEnabled);

            ClickNamedButton(host, "Btn_Close");
            Assert.AreEqual(1, closeCount);
        }

        private GameObject CreateChildPanel(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_rootRt, false);
            return go;
        }

        private static CatalogDatabase LoadCatalog()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs"));
            CatalogDatabase db = CatalogLoader.LoadFromDirectory(dir);
            CatalogValidationReport report = CatalogLoader.Validate(db);
            Assert.IsTrue(report.IsValid, "Catalog validation failed.");
            return db;
        }

        private static void ClickNamedButton(GameObject root, string buttonName)
        {
            Transform target = FindChildByName(root.transform, buttonName);
            Assert.IsNotNull(target, "Missing expected button '" + buttonName + "' under " + root.name);
            Button btn = target.GetComponent<Button>();
            Assert.IsNotNull(btn, "Object '" + buttonName + "' has no Button component.");
            btn.onClick.Invoke();
        }

        private static Transform FindChildByName(Transform parent, string targetName)
        {
            if (parent == null)
            {
                return null;
            }

            if (string.Equals(parent.name, targetName, StringComparison.Ordinal))
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildByName(parent.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
