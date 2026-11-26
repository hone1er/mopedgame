using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Ride;
using MopedSim.UI;
using MopedSim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M6 exit outcomes (PROJECT_GUIDE.md section 7) against the real generated Zone_* scenes and the real data/catalogs:
    /// 1. ZoneTransitionPortal triggers stream WestOakland -> OaklandHills -> BayCrossing -> SF_Mission with zero console errors
    ///    and a bounded per-frame time.
    /// 2. Courier economy: cargo gigs are refused on the stock wire rack, the milk crate is bought and mounted, the 2-crate
    ///    gig_mission_parts_rush is delivered in Zone_SF_Mission, cash is collected once, and the earnings buy + install an upgrade
    ///    in the Garage (plus a save/reload roundtrip of reputation, inventory, and customization).
    /// 3. Entering Zone_SF_Mission's KarlTheFogVolume raises AmbientAirDensity / lowers SurfaceGrip in BayAreaWeatherController,
    ///    AppCompositionRoot pushes both into the riding MopedBikeController, and Core telemetry reacts (leaner AFR).
    /// </summary>
    public sealed class BayAreaWorldAndEconomyTests
    {
        private const string BurritoGigId = "gig_oakland_burrito_crate";
        private const string MissionGigId = "gig_mission_parts_rush";
        private const string RallyJobId = "rally_east_bay_scrambler";
        private const int MaxStreamFrames = 600;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private SimulationMode _previousMode;
        private string _savePath;

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.Destroy(_spawned[i]);
                }
            }

            _spawned.Clear();
            yield return null;

            for (int i = 0; i < ZoneStreamManager.KnownZoneScenes.Length; i++)
            {
                Scene scene = SceneManager.GetSceneByName(ZoneStreamManager.KnownZoneScenes[i]);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                    while (op != null && !op.isDone)
                    {
                        yield return null;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_savePath) && File.Exists(_savePath))
            {
                File.Delete(_savePath);
            }

            Physics.simulationMode = _previousMode;
        }

        // ------------------------------------------------------------------ Outcome 1: zone streaming through portals

        [UnityTest]
        public IEnumerator ZoneStreaming_PortalsChainWestOaklandToSfMission_WithRealSceneContent()
        {
            CatalogDatabase db = LoadCatalog();
            GameObject appGo = Spawn("AppRoot_ZoneStreaming");
            ZoneStreamManager zones = appGo.AddComponent<ZoneStreamManager>();
            BayAreaWeatherController weather = appGo.AddComponent<BayAreaWeatherController>();
            Assert.AreSame(zones, ZoneStreamManager.Active);
            Assert.AreSame(weather, BayAreaWeatherController.Active);

            Rigidbody rider = CreateKinematicRider("PortalRider", new Vector3(0f, 50f, -500f));
            weather.SetTrackedRider(rider);

            float worstFrame = 0f;
            Task<bool> first = zones.TransitionToZoneAsync("Zone_WestOakland", false);
            yield return WaitForTask(first, f => worstFrame = Mathf.Max(worstFrame, f));
            Assert.IsTrue(first.Result, "Zone_WestOakland failed to stream.");
            AssertRealZoneContent("Zone_WestOakland", db);

            string[] chain = { "Zone_WestOakland", "Zone_OaklandHills", "Zone_BayCrossing", "Zone_SF_Mission" };
            for (int i = 1; i < chain.Length; i++)
            {
                string from = chain[i - 1];
                string to = chain[i];
                ZoneTransitionPortal portal = FindPortal(from, to);
                Assert.IsNotNull(portal, "No ZoneTransitionPortal " + from + " -> " + to + " in the generated scene.");
                int transitionsBefore = portal.TransitionCount;

                // Ride the tracked rigidbody into the boundary trigger; physics raises OnTriggerEnter on the portal.
                MoveRider(rider, portal.transform.position);
                Physics.Simulate(PlayModeRig.Dt);

                int frames = 0;
                while ((zones.ActiveZoneName != to || !zones.IsZoneLoaded(to)) && frames < MaxStreamFrames)
                {
                    yield return null;
                    worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    frames++;
                }

                Assert.AreEqual(to, zones.ActiveZoneName, "Portal " + from + " -> " + to + " did not activate the target zone.");
                Assert.AreEqual(transitionsBefore + 1, portal.TransitionCount);
                Assert.IsFalse(portal.IsTransitionInFlight);
                AssertRealZoneContent(to, db);

                // Re-entering immediately is debounced / ignored because the target is already active.
                Task<bool> again = portal.RequestTransitionAsync();
                Assert.IsTrue(again.IsCompleted);
                Assert.IsFalse(again.Result);
                MoveRider(rider, new Vector3(0f, 50f, -500f));
                Physics.Simulate(PlayModeRig.Dt);
            }

            for (int i = 0; i < chain.Length; i++)
            {
                Assert.IsTrue(zones.IsZoneLoaded(chain[i]), chain[i] + " should still be streamed (portals keep neighbours loaded).");
            }

            Assert.Less(worstFrame, 2.0f, "Worst streaming frame took " + worstFrame + " s.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ Outcome 2: courier gig -> cash -> garage upgrade

        [UnityTest]
        public IEnumerator CourierLoop_RackRequired_DeliverInSfMission_CollectCash_BuyAndInstallUpgrade()
        {
            CatalogDatabase db = LoadCatalog();
            BikeArchetypeSpec archetype = db.GetArchetype(ArchetypeIds.MaxerE50);
            BikeSimulation bike = new BikeSimulation(archetype, db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(2026u), db.GetZone("Zone_WestOakland"));
            world.CashUsd = 35m;
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, bike);

            GameObject appGo = Spawn("AppRoot_Courier");
            GameSessionStateMachine session = appGo.AddComponent<GameSessionStateMachine>();
            ZoneStreamManager zones = appGo.AddComponent<ZoneStreamManager>();
            BayAreaWeatherController weather = appGo.AddComponent<BayAreaWeatherController>();
            CourierGigController courier = appGo.AddComponent<CourierGigController>();
            JobBoardHudView jobBoard = appGo.AddComponent<JobBoardHudView>();
            AppCompositionRoot app = appGo.AddComponent<AppCompositionRoot>();
            app.BootstrapOnStart = false;

            Rigidbody rider = CreateKinematicRider("CourierRider", new Vector3(0f, 1f, 0f));
            courier.SetRider(rider.transform);
            weather.SetTrackedRider(rider);
            app.AssignCoreComponents(null, session, null, null);
            app.AssignWorldComponents(zones, weather, null, null);
            app.AssignEconomyComponents(null, courier, null, null, jobBoard, null);
            app.BootstrapWithWorld(world, db.PartList, db);
            yield return null;

            // 1. Stock wire rack (1 slot): both cargo gigs are refused by Core.
            JobAcceptResult burritoRefused = app.AcceptJob(BurritoGigId);
            Assert.IsFalse(burritoRefused.Accepted);
            Assert.AreEqual(JobRejectReason.InsufficientCargoSlots, burritoRefused.Reason);
            JobAcceptResult missionRefused = app.AcceptJob(MissionGigId);
            Assert.IsFalse(missionRefused.Accepted);
            Assert.AreEqual(JobRejectReason.InsufficientCargoSlots, missionRefused.Reason);
            Assert.AreEqual(1, missionRefused.Capacity.Slots);
            Assert.AreEqual(2, missionRefused.RequiredCargoSlots);
            Assert.AreEqual(JobSessionStatus.Idle, courier.Status);

            // 2. Buy the milk crate with the last $35 (into the Core garage inventory) and mount it with Core wrench commands.
            Assert.IsTrue(app.TryPurchasePart(db.GetPart("rack_milk_crate")));
            Assert.AreEqual(0m, world.CashUsd);
            Assert.AreEqual(1, world.Inventory.CountOf("rack_milk_crate"));
            Assert.IsFalse(app.TryPurchasePart(db.GetPart("sprocket_front_11t")), "No cash left before the gig pays out.");
            SwapRackForMilkCrate(bike, archetype, world);
            app.GarageContext.NotifyAssemblyChanged();
            CargoCapacity crate = CargoCapacityCalculator.Compute(bike);
            Assert.AreEqual(4, crate.Slots);
            Assert.IsTrue(crate.IsSecured);

            // 3. Stream into the Mission and ride the real 2-crate gig through its catalog checkpoints.
            Task<bool> toMission = zones.TransitionToZoneAsync("Zone_SF_Mission", false);
            yield return WaitForTask(toMission, null);
            Assert.IsTrue(toMission.Result);
            Assert.AreEqual("Zone_SF_Mission", world.CurrentZone.Id, "App applies the catalog zone environment on zone change.");

            Assert.IsTrue(app.TryStartRide());
            JobAcceptResult accepted = app.AcceptJob(MissionGigId);
            Assert.IsTrue(accepted.Accepted, accepted.Message);
            JobDefinition gig = db.GetJob(MissionGigId);
            for (int i = 0; i < gig.Checkpoints.Count; i++)
            {
                Assert.AreEqual("Zone_SF_Mission", gig.Checkpoints[i].ZoneId);
                JobCheckpointSpec cp = gig.Checkpoints[i];
                MoveRider(rider, new Vector3(cp.X, cp.Y, cp.Z));
                int frames = 0;
                while (courier.Jobs.CheckpointsReached <= i && frames < 60)
                {
                    // AppCompositionRoot.Update steps the job and reports the rider position each frame.
                    yield return null;
                    frames++;
                }

                Assert.AreEqual(i + 1, courier.Jobs.CheckpointsReached, "Checkpoint " + cp.Id + " not reached.");
            }

            Assert.AreEqual(JobSessionStatus.Completed, courier.Status);
            CompletedGigRecord record = courier.Jobs.LastCompletedRecord;
            Assert.IsNotNull(record);
            Assert.AreEqual(MissionGigId, record.JobId);
            Assert.IsTrue(record.OnTime, "Elapsed " + record.ElapsedSeconds);
            yield return null;
            Assert.IsTrue(jobBoard.IsRewardBannerVisible);
            Assert.AreEqual(3, jobBoard.DisplayedCheckpointsReached);
            Assert.AreEqual(100, jobBoard.DisplayedIntegrityPercent);
            Assert.AreEqual(gig.DisplayName, jobBoard.DisplayedTitle);

            // 4. Collect exactly once.
            Assert.IsTrue(app.CollectJobReward());
            Assert.IsFalse(app.CollectJobReward());
            Assert.AreEqual(record.TotalPayoutUsd, world.CashUsd);
            Assert.AreEqual(gig.ReputationReward, world.Reputation);
            yield return null;
            Assert.IsFalse(jobBoard.IsRewardBannerVisible);

            // 5. Back in the Garage, the earnings buy and install the 11T front sprocket upgrade.
            Assert.IsTrue(app.ReturnToGarage());
            PartDefinition sprocket = db.GetPart("sprocket_front_11t");
            Assert.IsTrue(app.TryPurchasePart(sprocket));
            Assert.AreEqual(record.TotalPayoutUsd - sprocket.PriceUsd, world.CashUsd);
            InstallFrontSprocket(bike, archetype, world, sprocket.Id);
            app.GarageContext.NotifyAssemblyChanged();
            Assert.AreEqual(11, bike.Drivetrain.FrontSprocketTeeth);

            // 6. Save / reload: reputation, cash, decal survive and the inventory does not duplicate across save -> load -> save.
            Assert.IsTrue(app.SetDecalStyle("checker_stripe"));
            Assert.IsTrue(app.TryPurchasePart(db.GetPart("rack_rear_wire")), "Spare wire rack for the parts shelf.");
            Assert.AreEqual(1, world.Inventory.Count, "Mounted parts leave the inventory; the spare stays on the shelf.");
            Assert.AreEqual(1, app.Inventory.Count);
            decimal cashBeforeSave = world.CashUsd;
            _savePath = Path.Combine(Application.temporaryCachePath, "m6_courier_roundtrip.json");
            app.SaveToPath(_savePath);
            SaveState saved = SaveLoadBridge.LoadFromPath(_savePath);
            Assert.AreEqual(gig.ReputationReward, saved.Reputation);
            Assert.AreEqual(1, saved.Inventory.Count);
            Assert.AreEqual("rack_rear_wire", saved.Inventory[0].PartId);
            Assert.AreEqual("checker_stripe", saved.Bikes[0].DecalStyle);

            world.Reputation = 0;
            world.CashUsd = 0m;
            world.Inventory.Clear();
            Assert.AreEqual(0, app.LoadFromPath(_savePath));
            Assert.AreEqual(gig.ReputationReward, world.Reputation);
            Assert.AreEqual(cashBeforeSave, world.CashUsd);
            Assert.AreEqual(1, world.Inventory.CountOf("rack_rear_wire"));
            Assert.AreEqual("checker_stripe", app.Customization.DecalStyle);
            Assert.AreEqual(11, bike.Drivetrain.FrontSprocketTeeth);
            SaveState resaved = app.CaptureSave();
            Assert.AreEqual(1, resaved.Inventory.Count, "Inventory must not grow across save -> load -> save.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ Outcome 3: Karl the Fog -> ride environment

        [UnityTest]
        public IEnumerator KarlTheFog_EnteringSfMissionVolume_RaisesAirDensityLowersGrip_InRidingBike()
        {
            CatalogDatabase db = LoadCatalog();
            BikeArchetypeSpec archetype = db.GetArchetype(ArchetypeIds.MaxerE50);
            BikeSimulation bike = new BikeSimulation(archetype, db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(11u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, bike);

            GameObject appGo = Spawn("AppRoot_Fog");
            GameSessionStateMachine session = appGo.AddComponent<GameSessionStateMachine>();
            ZoneStreamManager zones = appGo.AddComponent<ZoneStreamManager>();
            BayAreaWeatherController weather = appGo.AddComponent<BayAreaWeatherController>();
            InputAdapter input = appGo.AddComponent<InputAdapter>();
            AppCompositionRoot app = appGo.AddComponent<AppCompositionRoot>();
            app.BootstrapOnStart = false;

            Task<bool> toMission = zones.TransitionToZoneAsync("Zone_SF_Mission", false);
            yield return WaitForTask(toMission, null);
            Assert.IsTrue(toMission.Result);
            KarlTheFogVolume fog = FindInScene<KarlTheFogVolume>("Zone_SF_Mission");
            Assert.IsNotNull(fog, "Zone_SF_Mission must contain a KarlTheFogVolume.");

            // Both rigs ride Valencia_Corridor (dry asphalt) clear of the Muni rail strip at x = 0.
            Vector3 fogSurface = new Vector3(-4f, 0.1f, fog.WorldBounds.center.z);
            Vector3 controlSurface = new Vector3(4f, 0.1f, fog.WorldBounds.center.z);
            MopedBikeController ride = PlayModeRig.CreateRideRig("FogRideBike", bike, 7u, fogSurface + new Vector3(0f, 0f, -1000f), Quaternion.identity);
            _spawned.Add(ride.gameObject);
            AddTriggerProbe(ride);

            app.AssignCoreComponents(input, session, ride, null);
            app.AssignWorldComponents(zones, weather, ride.Aero, null);
            app.BootstrapWithWorld(world, db.PartList, db);
            ride.Body.interpolation = RigidbodyInterpolation.None;
            Assert.AreSame(ride.Body, weather.TrackedRider);

            // Re-apply the active zone through App (as ZoneStreamManager.ActiveZoneChanged would after bootstrap).
            Task<bool> reapply = zones.TransitionToZoneAsync("Zone_SF_Mission", false);
            yield return WaitForTask(reapply, null);
            ZoneEnvironmentSpec sf = db.GetZone("Zone_SF_Mission");
            Assert.AreEqual("Zone_SF_Mission", world.CurrentZone.Id);
            Assert.AreEqual(sf.AmbientAirDensity, weather.CurrentAmbientAirDensity, 1e-4f);
            float clearDensity = weather.CurrentAmbientAirDensity;
            float clearGrip = weather.CurrentSurfaceGripMultiplier;
            Assert.AreEqual(0, weather.ActiveFogVolumeCount);

            input.SetTestOverride(new FrameControlInputs(0.9f, 0f, 0f, 0f, false, false, false, false, false, false));
            Assert.IsTrue(app.TryStartRide());
            yield return null;
            Assert.AreEqual(clearDensity, app.LastAppliedAmbientAirDensity, 1e-4f);

            // Enter the fog bank: teleport onto the fogged corridor and let physics raise OnTriggerEnter on the volume.
            MoveRigTo(ride, fogSurface);
            Physics.Simulate(PlayModeRig.Dt);
            Assert.IsTrue(fog.IsRiderInside, "The tracked ride rigidbody did not trip the KarlTheFogVolume trigger.");
            Assert.AreEqual(1, weather.ActiveFogVolumeCount);
            yield return null;

            float fogDensity = weather.CurrentAmbientAirDensity;
            float fogGrip = weather.CurrentSurfaceGripMultiplier;
            Assert.Greater(fogDensity, clearDensity, "Karl the Fog must raise AmbientAirDensity.");
            Assert.Less(fogGrip, clearGrip, "Karl the Fog must lower SurfaceGrip.");
            Assert.Greater(weather.FogIntensity, 0.5f);
            Assert.AreEqual(fogDensity, app.LastAppliedAmbientAirDensity, 1e-5f);
            Assert.AreEqual(fogGrip, app.LastAppliedSurfaceGripMultiplier, 1e-5f);
            Assert.AreEqual(fogDensity, ReadPrivateFloat(ride, "_ambientAirDensity"), 1e-5f, "Ride controller did not receive the fog air density.");
            Assert.AreEqual(fogGrip, ReadPrivateFloat(ride, "_weatherGripMultiplier"), 1e-5f, "Ride controller did not receive the fog grip.");

            // Core telemetry reacts: same grip, clear-air control bike vs. fogged bike. Denser air meters more air per jet
            // (EngineSimulation totalAirFactor scales with AmbientAirDensity), so the fogged bike runs leaner.
            BikeSimulation controlSim = new BikeSimulation(archetype, db.Parts);
            MopedBikeController control = PlayModeRig.CreateRideRig("ClearControlBike", controlSim, 7u, controlSurface, Quaternion.identity);
            _spawned.Add(control.gameObject);
            control.SetEnvironment(clearDensity, fogGrip);
            control.SetControls(0.9f, 0f, 0f, 0f, false, false, false, false);
            ride.SetControls(0.9f, 0f, 0f, 0f, false, false, false, false);
            for (int i = 0; i < 240; i++)
            {
                PlayModeRig.Step(ride, control);
            }

            float fogAfr = ride.LatestTelemetry.EffectiveAfr;
            float clearAfr = control.LatestTelemetry.EffectiveAfr;
            Assert.Greater(ride.LatestTelemetry.Rpm, 0f);
            Assert.Greater(fogAfr, clearAfr + 0.01f, "Fogged (denser) air should lean the mixture: fog AFR " + fogAfr + " vs clear " + clearAfr);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ Per-frame economy fan-out stays allocation-free

        /// <summary>
        /// The per-frame App fan-out for jobs (CourierGigController.StepJob, JobSessionSnapshot + JobBoardHudView.Render) and the
        /// rally marker mirror (RallyPackController.SyncMarkers) allocate zero GC memory once warmed. Rally pack stepping itself is
        /// warmed outside the measured blocks: Core RallyPackSimulator.UpdatePackGap calls Array.Sort, which allocates under Unity's
        /// Mono runtime (Core-lane handoff recorded in docs/ROADMAP_STATUS.md).
        /// </summary>
        [UnityTest]
        public IEnumerator CourierStep_HudRender_AndRallyMirror_PerFrame_AllocateNoGcMemory()
        {
            CatalogDatabase db = LoadCatalog();
            BikeSimulation bike = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(5u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, bike);
            EngineTelemetrySnapshot telemetry = bike.LatestTelemetry;
            GameObject riderGo = Spawn("AllocRider");

            // A. Courier gig session (stock wire rack carries the 1-slot coffee run).
            GameObject gigGo = Spawn("CourierAllocationRig");
            CourierGigController courier = gigGo.AddComponent<CourierGigController>();
            JobBoardHudView hud = gigGo.AddComponent<JobBoardHudView>();
            courier.Bind(world, bike, null);
            courier.SetRider(riderGo.transform);
            JobDefinition coffee = db.GetJob("gig_west_oakland_coffee");
            JobAcceptResult gigAccepted = courier.AcceptJob(coffee);
            Assert.IsTrue(gigAccepted.Accepted, gigAccepted.Message);
            JobCheckpointSpec pickup = coffee.Checkpoints[0];
            Assert.AreEqual(CheckpointReportResult.Reached, courier.ReportRiderPosition(new Vector3(pickup.X, pickup.Y, pickup.Z)));
            riderGo.transform.position = new Vector3(pickup.X, pickup.Y + 500f, pickup.Z);

            // B. Bay Swarm rally session mirrored onto pooled markers.
            GameObject rallyGo = Spawn("RallyAllocationRig");
            CourierGigController rallyDispatch = rallyGo.AddComponent<CourierGigController>();
            RallyPackController rally = rallyGo.AddComponent<RallyPackController>();
            JobBoardHudView rallyHud = rallyGo.AddComponent<JobBoardHudView>();
            rallyDispatch.Bind(world, bike, null);
            rally.Bind(rallyDispatch);
            JobDefinition rallyJob = db.GetJob(RallyJobId);
            JobAcceptResult rallyAccepted = rallyDispatch.AcceptJob(rallyJob);
            Assert.IsTrue(rallyAccepted.Accepted, rallyAccepted.Message);
            JobCheckpointSpec start = rallyJob.Checkpoints[0];
            Assert.AreEqual(CheckpointReportResult.Reached, rallyDispatch.ReportRiderPosition(new Vector3(start.X, start.Y, start.Z)));
            yield return null;
            Assert.Greater(rally.ActiveMarkerCount, 0, "Rally pack markers were not pooled.");

            string gigTitle = courier.ActiveJobTitle;
            string rallyTitle = rallyDispatch.ActiveJobTitle;
            for (int i = 0; i < 240; i++)
            {
                courier.StepJob(PlayModeRig.Dt, in telemetry, 0.05f);
                JobSessionSnapshot warm = courier.GetSnapshot();
                hud.Render(in warm, gigTitle);
                rallyDispatch.StepJob(PlayModeRig.Dt);
                JobSessionSnapshot warmRally = rallyDispatch.GetSnapshot();
                rallyHud.Render(in warmRally, rallyTitle);
                rally.SyncMarkers();
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    courier.StepJob(PlayModeRig.Dt, in telemetry, 0.05f);
                    JobSessionSnapshot s = courier.GetSnapshot();
                    hud.Render(in s, courier.ActiveJobTitle);
                }
            }, Is.Not.AllocatingGCMemory(), "Courier StepJob + snapshot + JobBoardHudView.Render");

            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    JobSessionSnapshot s = rallyDispatch.GetSnapshot();
                    rallyHud.Render(in s, rallyDispatch.ActiveJobTitle);
                    rally.SyncMarkers();
                }
            }, Is.Not.AllocatingGCMemory(), "Rally snapshot + HUD + RallyPackController.SyncMarkers");

            // C. AppCompositionRoot.Update itself (input sample, weather -> ride env, HUD + hints fan-out, courier step, job board).
            SimulationWorld appWorld = new SimulationWorld(new SeededSimRandom(6u), db.GetZone("Zone_WestOakland"));
            appWorld.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts));
            GameObject appGo = Spawn("AppAllocationRig");
            GameSessionStateMachine session = appGo.AddComponent<GameSessionStateMachine>();
            InputAdapter input = appGo.AddComponent<InputAdapter>();
            BayAreaWeatherController weather = appGo.AddComponent<BayAreaWeatherController>();
            CourierGigController appCourier = appGo.AddComponent<CourierGigController>();
            JobBoardHudView appBoard = appGo.AddComponent<JobBoardHudView>();
            RideHudView rideHud = appGo.AddComponent<RideHudView>();
            DiagnosticHintsPresenter hints = appGo.AddComponent<DiagnosticHintsPresenter>();
            AppCompositionRoot app = appGo.AddComponent<AppCompositionRoot>();
            app.BootstrapOnStart = false;
            appCourier.SetRider(riderGo.transform);
            app.AssignCoreComponents(input, session, null, null);
            app.AssignWorldComponents(null, weather, null, null);
            app.AssignPresentationComponents(rideHud, null, null, null, null, hints, null);
            app.AssignEconomyComponents(null, appCourier, null, null, appBoard, null);
            app.BootstrapWithWorld(appWorld, db.PartList, db);
            input.SetTestOverride(new FrameControlInputs(0.4f, 0f, 0f, 0f, false, false, false, false, false, false));
            Assert.IsTrue(app.TryStartRide());
            Assert.IsTrue(app.AcceptJob("gig_west_oakland_coffee").Accepted);
            Assert.AreEqual(CheckpointReportResult.Reached, appCourier.ReportRiderPosition(new Vector3(pickup.X, pickup.Y, pickup.Z)));
            MethodInfo updateMethod = typeof(AppCompositionRoot).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updateMethod);
            Action appUpdate = (Action)Delegate.CreateDelegate(typeof(Action), app, updateMethod);
            for (int i = 0; i < 120; i++)
            {
                appUpdate();
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    appUpdate();
                }
            }, Is.Not.AllocatingGCMemory(), "AppCompositionRoot.Update with an active courier gig");
            Assert.AreEqual(JobSessionStatus.Active, appCourier.Status);
            Assert.IsTrue(appBoard.IsPanelVisible);

            Assert.AreEqual(JobSessionStatus.Active, courier.Status);
            Assert.Greater(courier.Jobs.ElapsedSeconds, 0f);
            Assert.Greater(hud.DisplayedClockSeconds, 0);
            Assert.AreEqual(JobSessionStatus.Active, rallyDispatch.Status);
            Assert.Greater(rallyDispatch.Jobs.Rally.ElapsedSeconds, 0f);
            Assert.Greater(rallyHud.DisplayedFieldSize, 1);
            Transform lead = rally.GetMarker(0);
            Assert.IsNotNull(lead);
            Assert.Greater(Vector3.Distance(lead.position, new Vector3(start.X, start.Y, start.Z)), 1f, "Rally markers should advance along the route.");
        }

        // ------------------------------------------------------------------ Helpers

        private GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
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

        private Rigidbody CreateKinematicRider(string name, Vector3 position)
        {
            GameObject go = Spawn(name);
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            SphereCollider c = go.AddComponent<SphereCollider>();
            c.radius = 0.5f;
            return rb;
        }

        private static void MoveRider(Rigidbody rider, Vector3 position)
        {
            rider.transform.position = position;
            rider.position = position;
            Physics.SyncTransforms();
        }

        private static void MoveRigTo(MopedBikeController ride, Vector3 surfacePoint)
        {
            Vector3 p = surfacePoint + Vector3.up * PlayModeRig.RideHeightM;
            ride.transform.position = p;
            ride.Body.position = p;
            ride.Body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static void AddTriggerProbe(MopedBikeController ride)
        {
            // PlayModeRig rigs are collider-free (raycast suspension). Fog volumes need a collider on the tracked rigidbody: a
            // trigger sphere above the wheel mounts never touches the road or the suspension raycasts (which ignore triggers).
            GameObject probe = new GameObject("RiderTriggerProbe");
            probe.transform.SetParent(ride.transform, false);
            probe.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            SphereCollider sphere = probe.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.35f;
            ride.Body.centerOfMass = Vector3.zero;
        }

        private static float ReadPrivateFloat(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, "Missing field " + field);
            return (float)info.GetValue(target);
        }

        private static IEnumerator WaitForTask(Task<bool> task, Action<float> onFrame)
        {
            int frames = 0;
            while (!task.IsCompleted && frames < MaxStreamFrames)
            {
                yield return null;
                if (onFrame != null)
                {
                    onFrame(Time.unscaledDeltaTime);
                }

                frames++;
            }

            Assert.IsTrue(task.IsCompleted, "Zone streaming task did not complete within " + MaxStreamFrames + " frames.");
            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        private static void AssertRealZoneContent(string zoneScene, CatalogDatabase db)
        {
            Scene scene = SceneManager.GetSceneByName(zoneScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, zoneScene + " is not loaded.");
            GameObject root = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == zoneScene + "_Root")
                {
                    root = roots[i];
                }
            }

            Assert.IsNotNull(root, zoneScene + " streamed without its generated root (empty fallback scene?).");
            Assert.Greater(root.GetComponentsInChildren<RoadHazardZone>(true).Length, 0, zoneScene + " has no road surfaces.");
            Transform kit = root.transform.Find("WorldKit");
            Assert.IsNotNull(kit, zoneScene + " has no WorldKit dressing.");
            Assert.Greater(kit.GetComponentsInChildren<MeshRenderer>(true).Length, 0, zoneScene + " WorldKit has no rendered .glb modules.");

            ZoneMetadata meta = db.ZoneMeta[zoneScene];
            ZoneTransitionPortal[] portals = root.GetComponentsInChildren<ZoneTransitionPortal>(true);
            Assert.AreEqual(meta.ConnectedZones.Count, portals.Length, zoneScene + " portals must mirror zones.json connectedZones.");
        }

        private static ZoneTransitionPortal FindPortal(string fromScene, string toScene)
        {
            Scene scene = SceneManager.GetSceneByName(fromScene);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ZoneTransitionPortal[] portals = roots[i].GetComponentsInChildren<ZoneTransitionPortal>(true);
                for (int p = 0; p < portals.Length; p++)
                {
                    if (portals[p].TargetZoneScene == toScene)
                    {
                        return portals[p];
                    }
                }
            }

            return null;
        }

        private static T FindInScene<T>(string sceneName) where T : Component
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
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

        private static void SwapRackForMilkCrate(BikeSimulation bike, BikeArchetypeSpec archetype, SimulationWorld world)
        {
            List<FastenerSpec> rackBolts = FastenersOf(archetype, SlotIds.CargoRack);
            Assert.AreEqual(2, rackBolts.Count);
            for (int i = 0; i < rackBolts.Count; i++)
            {
                Assert.IsTrue(bike.TryRemoveFastener(rackBolts[i].FastenerId).Succeeded, rackBolts[i].FastenerId);
            }

            Assert.IsTrue(bike.TryRemovePart(SlotIds.CargoRack).Succeeded);
            PartInstance crate = world.Inventory.TakeFirstByPartId("rack_milk_crate");
            Assert.IsNotNull(crate);
            Assert.IsTrue(bike.TryInstallPart(SlotIds.CargoRack, crate).Succeeded);
            for (int i = 0; i < rackBolts.Count; i++)
            {
                Assert.IsTrue(bike.TryTorqueFastener(rackBolts[i].FastenerId, rackBolts[i].TargetTorqueNm).Succeeded, rackBolts[i].FastenerId);
            }
        }

        private static void InstallFrontSprocket(BikeSimulation bike, BikeArchetypeSpec archetype, SimulationWorld world, string sprocketPartId)
        {
            bike.CoolDownSeconds(600f);
            List<FastenerSpec> bolts = FastenersOf(archetype, SlotIds.FrontSprocket);
            for (int i = 0; i < bolts.Count; i++)
            {
                Assert.IsTrue(bike.TryRemoveFastener(bolts[i].FastenerId).Succeeded, bolts[i].FastenerId);
            }

            PartInstance chain = bike.Assembly.GetInstalledPart(SlotIds.Chain);
            Assert.IsTrue(bike.TryRemovePart(SlotIds.Chain).Succeeded);
            WrenchActionResult removeStock = bike.TryRemovePart(SlotIds.FrontSprocket);
            Assert.IsTrue(removeStock.Succeeded, removeStock.Message);
            PartInstance upgrade = world.Inventory.TakeFirstByPartId(sprocketPartId);
            Assert.IsNotNull(upgrade);
            Assert.IsTrue(bike.TryInstallPart(SlotIds.FrontSprocket, upgrade).Succeeded);
            Assert.IsTrue(bike.TryInstallPart(SlotIds.Chain, chain).Succeeded);
        }

        private static List<FastenerSpec> FastenersOf(BikeArchetypeSpec archetype, string slotId)
        {
            List<FastenerSpec> result = new List<FastenerSpec>();
            for (int i = 0; i < archetype.Fasteners.Count; i++)
            {
                if (archetype.Fasteners[i].OwnerSlotId == slotId)
                {
                    result.Add(archetype.Fasteners[i]);
                }
            }

            return result;
        }
    }
}
