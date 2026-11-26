using System.Collections;
using System.Collections.Generic;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Garage;
using MopedSim.Ride;
using MopedSim.UI;
using MopedSim.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MopedSim.Tests.PlayMode
{
    /// <summary>
    /// M7 Verifiable Outcome #2 end-to-end on the real generated scenes and real data/catalogs: Bootstrap.unity boots exactly as
    /// the player does (additive Garage + UI_Overlay, catalog bootstrap, Zone_WestOakland stream-in), then the session is driven
    /// through the InputAdapter hotkey path: Garage (F4 paint swatch + patina/decal, F2 settings, F3 shop, pack the tool roll, buy a
    /// part) -> F1 Ride (the assembled .glb bike rides on RideMoped; accelerate, build odometer + CHT) -> fouled-plug breakdown ->
    /// roadside repair -> resume -> F1 back to the Garage -> SaveToPath -> LoadFromPath into a fresh session, asserting full
    /// persistence, zero unexpected console errors, and bounded frame times.
    /// </summary>
    public sealed class FullCycleReleaseCandidateTests
    {
        private const int MaxBootFrames = 1800;
        private const int MaxRideFrames = 5000000; // Safety net only: game time (MaxPhaseGameSeconds) bounds each phase.
        private const float MaxPhaseGameSeconds = 30f;
        private const string WorkingZone = "Zone_WestOakland";

        private static readonly string[] RuntimeScenes =
        {
            "Bootstrap", "Garage", "UI_Overlay", "Zone_WestOakland", "Zone_OaklandHills", "Zone_BayCrossing", "Zone_SF_Mission"
        };

        private SimulationMode _previousMode;
        private int _previousTargetFrameRate;
        private GameObject _freshGo;
        private string _savePath;

        [SetUp]
        public void SetUp()
        {
            // The real RideMoped auto-steps in FixedUpdate, exactly like the player.
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            // Batch-mode frames are otherwise uncapped (~50 us); pace like a 60 Hz player so frame-time stats mean something.
            _previousTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_freshGo != null)
            {
                Object.Destroy(_freshGo);
            }

            for (int i = RuntimeScenes.Length - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneByName(RuntimeScenes[i]);
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
            Application.targetFrameRate = _previousTargetFrameRate;
        }

        [UnityTest]
        public IEnumerator Garage_Ride_Breakdown_RoadsideRepair_Save_Reload_OnRealScenesAndCatalogs()
        {
            // ---------------------------------------------------------------- Boot the real game (additively, keeping the runner).
            AsyncOperation boot = SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Additive);
            while (!boot.isDone)
            {
                yield return null;
            }

            AppCompositionRoot app = FindInScene<AppCompositionRoot>("Bootstrap");
            Assert.IsNotNull(app, "Bootstrap.unity must contain the AppCompositionRoot.");
            int frames = 0;
            while ((!app.IsBootstrapped || app.Zones == null || !app.Zones.IsZoneLoaded(WorkingZone)) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(app.IsBootstrapped, "App did not bootstrap from the real catalogs.");
            Assert.IsNotNull(app.Catalog);
            Assert.IsTrue(app.Zones.IsZoneLoaded(WorkingZone), WorkingZone + " did not stream in after bootstrap.");

            BikeVisualAssembler assembler = FindInScene<BikeVisualAssembler>("Garage");
            Assert.IsNotNull(assembler);
            frames = 0;
            while ((assembler.IsSyncing || assembler.MountedPartCount == 0) && frames < MaxBootFrames)
            {
                yield return null;
                frames++;
            }

            Assert.Greater(assembler.MountedPartCount, 0, "The workstand bike never assembled its .glb parts.");
            Assert.AreEqual(GameSessionState.Garage, app.Session.CurrentState);

            SimulationWorld world = app.World;
            BikeSimulation bike = app.ActiveBike;
            InputAdapter input = app.Input;
            Assert.IsNotNull(input);
            input.SetTestOverride(default(FrameControlInputs));

            MopedBikeController ride = FindInScene<MopedBikeController>("Bootstrap");
            Rigidbody rideBody = ride.GetComponent<Rigidbody>();
            Camera rideCamera = FindInScene<MopedCameraRig>("Bootstrap").AttachedCamera;
            GarageWorkstandController workstand = FindInScene<GarageWorkstandController>("Garage");
            Camera inspectionCamera = workstand.InspectionCamera;
            RideHudView rideHud = FindInScene<RideHudView>("UI_Overlay");
            GarageWrenchHudView wrenchHud = FindInScene<GarageWrenchHudView>("UI_Overlay");
            ToolRollOverlayView toolRoll = FindInScene<ToolRollOverlayView>("UI_Overlay");
            PlugChopChartView plugChart = FindInScene<PlugChopChartView>("UI_Overlay");
            JobBoardHudView jobBoard = FindInScene<JobBoardHudView>("UI_Overlay");

            // ---------------------------------------------------------------- Garage: dressed, isolated, real bike visual.
            Transform visual = app.BikeVisualRoot;
            Assert.IsNotNull(visual, "The assembled bike visual must be a dedicated, movable hierarchy.");
            Transform visualHome = visual.parent;
            Vector3 visualHomePosition = visual.localPosition;
            Assert.IsFalse(app.IsBikeVisualOnRideMoped);
            Assert.IsTrue(visual.IsChildOf(workstand.transform), "In the Garage the bike sits on the workstand.");
            AssertRealGlbVisual(visual);
            Assert.Greater(Vector3.Distance(workstand.transform.position, Vector3.zero), 400f, "Garage must be isolated from the zones.");
            Assert.IsTrue(rideBody.isKinematic, "RideMoped is held kinematic while in the Garage.");
            Assert.IsTrue(inspectionCamera.enabled);
            Assert.IsFalse(rideCamera.enabled);
            AssertHud(rideHud, false, wrenchHud, true, toolRoll, true, plugChart, true, jobBoard, false, "Garage");

            // F4: cycle the curated paint swatch live on the bike, then add patina + a decal.
            string hexBefore = app.PaintBooth.CurrentPaintHex;
            yield return PressHotkey(input, false, false, false, true);
            string paintHex = app.PaintBooth.CurrentPaintHex;
            Assert.AreNotEqual(hexBefore, paintHex, "F4 must apply the next paint swatch.");
            Assert.IsTrue(app.PaintBooth.SetPaint(paintHex, 0.35f, app.PaintBooth.MetallicFlake));
            Assert.IsTrue(app.SetDecalStyle("checker_stripe"));
            Assert.AreEqual(paintHex.ToUpperInvariant(), app.Customization.PaintHex.ToUpperInvariant());
            Assert.AreEqual(0.35f, app.Customization.PatinaLevel, 1e-4f);
            AssertPaintOnRenderers(visual, app.PaintBooth.EffectiveColor);

            // F2 settings + F3 shop open and close through the hotkeys.
            yield return PressHotkey(input, false, true, false, false);
            Assert.IsTrue(app.IsSettingsMenuOpen, "F2 must open the settings menu.");
            yield return PressHotkey(input, false, true, false, false);
            Assert.IsFalse(app.IsSettingsMenuOpen, "F2 must close the settings menu.");
            yield return PressHotkey(input, false, false, true, false);
            Assert.IsTrue(app.IsShopOpen, "F3 must open the parts shop.");
            yield return PressHotkey(input, false, false, true, false);
            Assert.IsFalse(app.IsShopOpen, "F3 must close the parts shop.");

            // Pack the tool roll and buy a spare jet for the shelf.
            Assert.IsTrue(app.TryPackToolRollItem(ToolIds.SparkPlugWrench));
            Assert.IsTrue(app.TryPackToolRollItem(ToolIds.SparePlug));
            decimal cashBeforePurchase = world.CashUsd;
            PartDefinition jet = app.Catalog.GetPart("jet_main_70");
            Assert.IsTrue(app.TryPurchasePart(jet));
            Assert.AreEqual(cashBeforePurchase - jet.PriceUsd, world.CashUsd);

            // ---------------------------------------------------------------- F1: ride the bike you just wrenched & painted.
            yield return PressHotkey(input, true, false, false, false);
            Assert.AreEqual(GameSessionState.Ride, app.Session.CurrentState, "F1 must start the ride.");
            Assert.IsTrue(app.IsBikeVisualOnRideMoped, "The assembled .glb bike must ride on RideMoped.");
            Assert.AreSame(ride.transform, visual.parent);
            Assert.IsFalse(rideBody.isKinematic);
            Assert.IsTrue(rideCamera.enabled);
            Assert.IsFalse(inspectionCamera.enabled);
            AssertHud(rideHud, true, wrenchHud, false, toolRoll, false, plugChart, false, jobBoard, true, "Ride");
            AssertPaintOnRenderers(visual, app.PaintBooth.EffectiveColor);

            float odometerStart = bike.OdometerMeters;
            float chtStart = bike.LatestTelemetry.ChtCelsius;
            float worstFrame = 0f;
            float frameSum = 0f;
            int frameCount = 0;
            bool everRunning = false;
            input.SetTestOverride(new FrameControlInputs(1f, 0f, 0f, 0f, false, true, false, false, false, false));
            frames = 0;
            float phaseStart = Time.time;
            while ((bike.OdometerMeters < odometerStart + 25f || ride.SpeedMps < 4f) && frames < MaxRideFrames && Time.time - phaseStart < MaxPhaseGameSeconds)
            {
                yield return null;
                everRunning |= bike.LatestTelemetry.IsRunning;
                if (frames > 30)
                {
                    worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    frameSum += Time.unscaledDeltaTime;
                    frameCount++;
                }

                frames++;
            }

            Assert.Greater(bike.OdometerMeters, odometerStart + 25f, "Odometer did not build while riding. " + RideDiagnostics(app, ride, rideBody, phaseStart, frames));
            Assert.Greater(ride.SpeedMps, 4f, "The moped did not accelerate on the real West Oakland road.");
            Assert.IsTrue(everRunning, "The engine never fired.");
            Assert.Greater(bike.LatestTelemetry.ChtCelsius, chtStart, "CHT must build while riding.");
            Assert.AreEqual(GameSessionState.Ride, app.Session.CurrentState);

            // ---------------------------------------------------------------- Breakdown: fouled plug stalls the engine.
            bike.InduceFault(FaultIds.FouledPlug);
            input.SetTestOverride(new FrameControlInputs(0.8f, 0f, 0f, 0f, false, false, false, false, false, false));
            frames = 0;
            phaseStart = Time.time;
            while (!bike.LatestTelemetry.IsStalled && frames < MaxRideFrames && Time.time - phaseStart < MaxPhaseGameSeconds)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(bike.LatestTelemetry.IsStalled, "A fouled plug must stall the engine on the road.");

            // ---------------------------------------------------------------- Roadside repair with the packed tool roll.
            input.SetTestOverride(default(FrameControlInputs));
            Assert.IsTrue(app.PullOverForRoadsideRepair());
            Assert.AreEqual(GameSessionState.RoadsideRepair, app.Session.CurrentState);
            Assert.IsTrue(app.IsBikeVisualOnRideMoped, "The bike stays on the road during a roadside repair.");
            AssertHud(rideHud, false, wrenchHud, true, toolRoll, true, plugChart, false, jobBoard, true, "RoadsideRepair");
            RoadsideRepairResult repair = app.AttemptRoadsideRepair(FaultIds.FouledPlug);
            Assert.IsTrue(repair.Succeeded, repair.Message);
            Assert.AreEqual(GameSessionState.Ride, app.Session.CurrentState, "Ride resumes once no faults remain.");
            Assert.AreEqual(0, bike.GetActiveFaults().Count);

            input.SetTestOverride(new FrameControlInputs(1f, 0f, 0f, 0f, false, true, false, false, false, false));
            frames = 0;
            phaseStart = Time.time;
            while (!(bike.LatestTelemetry.IsRunning && ride.SpeedMps > 2f) && frames < MaxRideFrames && Time.time - phaseStart < MaxPhaseGameSeconds)
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(bike.LatestTelemetry.IsRunning, "Pedal-start must refire the fresh plug.");
            Assert.Greater(ride.SpeedMps, 2f);

            // ---------------------------------------------------------------- F1 back to the Garage: visual returns home.
            input.SetTestOverride(default(FrameControlInputs));
            yield return PressHotkey(input, true, false, false, false);
            Assert.AreEqual(GameSessionState.Garage, app.Session.CurrentState, "F1 must return to the Garage.");
            Assert.IsFalse(app.IsBikeVisualOnRideMoped);
            Assert.AreSame(visualHome, visual.parent);
            Assert.Less(Vector3.Distance(visualHomePosition, visual.localPosition), 1e-4f, "The workstand pose must be restored.");
            Assert.IsTrue(rideBody.isKinematic);
            Assert.IsTrue(inspectionCamera.enabled);
            Assert.IsFalse(rideCamera.enabled);

            // ---------------------------------------------------------------- Save.
            SaveState expected = app.CaptureSave();
            Assert.AreEqual(1, expected.Bikes.Count);
            BikeSaveState expectedBike = expected.Bikes[0];
            Assert.Greater(expectedBike.OdometerKm, 0.02);
            Assert.AreEqual(0, expectedBike.ActiveFaultIds.Count);
            _savePath = Path.Combine(Application.temporaryCachePath, "m7_full_cycle_rc.json");
            app.SaveToPath(_savePath);

            // ---------------------------------------------------------------- Reload into a fresh session.
            CatalogDatabase db = app.Catalog;
            SimulationWorld freshWorld = new SimulationWorld(new SeededSimRandom(99u), db.GetZone(WorkingZone));
            freshWorld.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts));
            _freshGo = new GameObject("FreshReleaseCandidateSession");
            GameSessionStateMachine freshSession = _freshGo.AddComponent<GameSessionStateMachine>();
            AppCompositionRoot fresh = _freshGo.AddComponent<AppCompositionRoot>();
            fresh.BootstrapOnStart = false;
            fresh.AssignCoreComponents(null, freshSession, null, null);
            fresh.BootstrapWithWorld(freshWorld, db.PartList, db);
            Assert.AreNotEqual(expected.CashUsd, freshWorld.CashUsd, "The fresh session must start from defaults before loading.");

            Assert.AreEqual(0, fresh.LoadFromPath(_savePath), "Core rejected saved entries.");
            SaveState actual = fresh.CaptureSave();
            BikeSaveState actualBike = actual.Bikes[0];
            Assert.AreEqual(expected.CashUsd, freshWorld.CashUsd);
            Assert.AreEqual(expected.Reputation, freshWorld.Reputation);
            Assert.AreEqual(expectedBike.PaintHex, actualBike.PaintHex);
            Assert.AreEqual(expectedBike.PatinaLevel, actualBike.PatinaLevel, 1e-4f);
            Assert.AreEqual("checker_stripe", actualBike.DecalStyle);
            Assert.AreEqual("checker_stripe", fresh.Customization.DecalStyle);
            Assert.AreEqual(expectedBike.FuelTank.FuelLiters, actualBike.FuelTank.FuelLiters, 1e-3f);
            Assert.AreEqual(expectedBike.OdometerKm, actualBike.OdometerKm, 1e-4);
            Assert.AreEqual(Describe(expectedBike.InstalledParts), Describe(actualBike.InstalledParts), "Installed parts must persist.");
            CollectionAssert.AreEqual(expected.ActiveToolRoll, actual.ActiveToolRoll, "Tool roll must persist.");
            CollectionAssert.Contains(actual.ActiveToolRoll, ToolIds.SparkPlugWrench);
            Assert.AreEqual(1, freshWorld.Inventory.CountOf("jet_main_70"), "The spare jet must persist on the shelf.");
            Assert.AreEqual(expected.Inventory.Count, actual.Inventory.Count);
            Assert.AreEqual(0, fresh.ActiveBike.GetActiveFaults().Count, "The repaired bike reloads with zero faults.");
            Assert.AreEqual(0, actualBike.ActiveFaultIds.Count);

            // ---------------------------------------------------------------- Stability.
            Assert.Greater(frameCount, 0);
            float meanFrame = frameSum / frameCount;
            Assert.Less(worstFrame, 0.5f, "Worst ride frame took " + worstFrame + " s.");
            Assert.Less(meanFrame, 1f / 20f, "Mean ride frame took " + meanFrame + " s.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ Helpers

        private static string RideDiagnostics(AppCompositionRoot app, MopedBikeController ride, Rigidbody body, float phaseStart, int frames)
        {
            EngineTelemetrySnapshot t = app.ActiveBike.LatestTelemetry;
            return "[gameSeconds=" + (Time.time - phaseStart) + " frames=" + frames + " speed=" + ride.SpeedMps + " grounded=" + ride.IsGrounded
                + " simEnabled=" + ride.SimulationEnabled + " kinematic=" + body.isKinematic + " pos=" + body.position + " vel=" + body.linearVelocity
                + " rpm=" + t.Rpm + " running=" + t.IsRunning + " stalled=" + t.IsStalled + " gear=" + t.CurrentGear + " throttle=" + t.ThrottlePosition
                + " clutch=" + t.ClutchEngagement + " wheelTorque=" + t.RearWheelTorqueNm + " stamina=" + t.RiderStamina + " vehMps=" + t.VehicleSpeedMps
                + " faults=" + app.ActiveBike.GetActiveFaults().Count + " fixedDt=" + Time.fixedDeltaTime + " simMode=" + Physics.simulationMode + "]";
        }

        private static IEnumerator PressHotkey(InputAdapter input, bool garageRide, bool settings, bool shop, bool paint)
        {
            input.SetTestOverride(new FrameControlInputs(0f, 0f, 0f, 0f, false, false, false, false, false, false, garageRide, settings, shop, paint));
            yield return null; // AppCompositionRoot.Update consumes the press exactly once this frame.
            input.SetTestOverride(default(FrameControlInputs));
            yield return null;
        }

        private static void AssertHud(
            Component rideHud, bool rideVisible,
            Component wrenchHud, bool wrenchVisible,
            Component toolRoll, bool toolRollVisible,
            Component plugChart, bool plugChartVisible,
            Component jobBoard, bool jobBoardVisible,
            string state)
        {
            Assert.AreEqual(rideVisible, rideHud.gameObject.activeSelf, state + ": RideHud visibility");
            Assert.AreEqual(wrenchVisible, wrenchHud.gameObject.activeSelf, state + ": WrenchHud visibility");
            Assert.AreEqual(toolRollVisible, toolRoll.gameObject.activeSelf, state + ": ToolRoll visibility");
            Assert.AreEqual(plugChartVisible, plugChart.gameObject.activeSelf, state + ": PlugChopChart visibility");
            Assert.AreEqual(jobBoardVisible, jobBoard.gameObject.activeSelf, state + ": JobBoardHud visibility");
        }

        private static void AssertRealGlbVisual(Transform visual)
        {
            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            Assert.Greater(renderers.Length, 0, "The assembled bike has no rendered .glb meshes.");
            for (int i = 0; i < renderers.Length; i++)
            {
                Assert.IsFalse(renderers[i].name.StartsWith("FallbackMesh_"), "Fallback cube drawn for " + renderers[i].name);
                Material material = renderers[i].sharedMaterial;
                Assert.IsNotNull(material, renderers[i].name + " has no material.");
                Assert.IsTrue(material.shader.isSupported, renderers[i].name + " uses an unsupported (magenta) shader " + material.shader.name);
            }
        }

        private static void AssertPaintOnRenderers(Transform visual, Color expected)
        {
            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            int checkedCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                Color painted = block.GetColor(PaintBoothController.GltfBaseColorFactorPropertyId);
                Assert.AreEqual(expected.r, painted.r, 1e-3f, renderers[i].name + " paint (r)");
                Assert.AreEqual(expected.g, painted.g, 1e-3f, renderers[i].name + " paint (g)");
                Assert.AreEqual(expected.b, painted.b, 1e-3f, renderers[i].name + " paint (b)");
                checkedCount++;
            }

            Assert.Greater(checkedCount, 0);
        }

        private static string Describe(List<InstalledPartSaveState> parts)
        {
            List<string> entries = new List<string>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                entries.Add(parts[i].SlotId + "=" + parts[i].PartId);
            }

            entries.Sort(System.StringComparer.Ordinal);
            return string.Join(";", entries);
        }

        private static T FindInScene<T>(string sceneName) where T : Component
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, sceneName + " is not loaded.");
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
    }
}
