using System.Collections.Generic;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Editor;
using MopedSim.Garage;
using MopedSim.UI;
using MopedSim.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MopedSim.Tests.EditMode
{
    internal static class RepoCatalog
    {
        private static CatalogDatabase _db;

        public static CatalogDatabase Load()
        {
            if (_db == null)
            {
                _db = CatalogLoader.LoadFromDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs")));
            }

            return _db;
        }
    }

    /// <summary>
    /// InputAdapter parses GameSettingsData key names into cached InputSystem Keys once per apply.
    /// </summary>
    public sealed class InputAdapterKeyBindingTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void TryParseKeyName_AcceptsSettingsNames_RejectsNumericAndNone()
        {
            Key key;
            Assert.IsTrue(InputAdapter.TryParseKeyName("w", out key));
            Assert.AreEqual(Key.W, key);
            Assert.IsTrue(InputAdapter.TryParseKeyName("leftShift", out key));
            Assert.AreEqual(Key.LeftShift, key);
            Assert.IsTrue(InputAdapter.TryParseKeyName(" space ", out key));
            Assert.AreEqual(Key.Space, key);
            Assert.IsTrue(InputAdapter.TryParseKeyName("leftCtrl", out key));
            Assert.AreEqual(Key.LeftCtrl, key);
            Assert.IsTrue(InputAdapter.TryParseKeyName("1", out key));
            Assert.AreEqual(Key.Digit1, key, "A bare digit binds the top-row digit key, not enum value 1.");

            Assert.IsFalse(InputAdapter.TryParseKeyName("42", out key));
            Assert.IsFalse(InputAdapter.TryParseKeyName("none", out key));
            Assert.IsFalse(InputAdapter.TryParseKeyName(string.Empty, out key));
            Assert.IsFalse(InputAdapter.TryParseKeyName("w,a", out key));
            Assert.IsFalse(InputAdapter.TryParseKeyName("notAKey", out key));
        }

        [Test]
        public void ApplyKeyBindings_CachesOverrides_FallsBackPerAction_AndResets()
        {
            _go = new GameObject("InputUnderTest");
            InputAdapter input = _go.AddComponent<InputAdapter>();
            Assert.AreEqual(Key.W, input.ThrottleKey);

            GameSettingsData settings = SettingsPreferencesStore.CreateDefaults();
            settings.ThrottleKey = "i";
            settings.RearBrakeKey = "k";
            settings.ToggleCameraKey = "bogus";
            Assert.AreEqual(1, input.ApplyKeyBindings(settings));
            Assert.AreEqual(Key.I, input.ThrottleKey);
            Assert.AreEqual(Key.K, input.RearBrakeKey);
            Assert.AreEqual(Key.V, input.ToggleCameraKey, "Unknown key names keep the default binding.");
            Assert.AreEqual(Key.LeftShift, input.FrontBrakeKey);

            input.ResetDefaultKeyBindings();
            Assert.AreEqual(Key.W, input.ThrottleKey);
            Assert.AreEqual(Key.S, input.RearBrakeKey);

            // Overrides still win over devices.
            input.SetTestOverride(new FrameControlInputs(0.5f, 0f, 0f, 0f, false, false, false, false, false, false));
            Assert.AreEqual(0.5f, input.SampleInputs().Throttle, 1e-6f);
            input.ClearTestOverride();
        }
    }

    /// <summary>
    /// SaveLoadBridge persists reputation, the Core garage inventory, and paint booth customization without duplication.
    /// </summary>
    public sealed class SaveLoadBridgeEconomyTests
    {
        [Test]
        public void Capture_WritesReputationInventoryAndCustomization_AndRoundTripsWithoutDuplicates()
        {
            CatalogDatabase db = RepoCatalog.Load();
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(9u), db.GetZone("Zone_WestOakland"));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts));
            world.CashUsd = 500m;
            world.Reputation = 65;
            PartInstance crate;
            Assert.IsTrue(world.TryPurchasePartIntoInventory(db.GetPart("rack_milk_crate"), out crate).Succeeded);
            crate.Condition = 0.7f;
            PartInstance sprocket;
            Assert.IsTrue(world.TryPurchasePartIntoInventory(db.GetPart("sprocket_front_11t"), out sprocket).Succeeded);

            BikeCustomizationState paint = new BikeCustomizationState("#1F7A8C", 0.35f, "checker_stripe");
            // Legacy id list mirrors the world inventory plus one extra id: only the extra copy is written.
            List<string> legacy = new List<string> { "rack_milk_crate", "sprocket_front_11t", "rack_rear_wire" };
            SaveState state = SaveLoadBridge.Capture(world, AppCompositionRoot.DefaultBikeInstanceId, legacy, 9u, true, null, paint);

            Assert.AreEqual(65, state.Reputation);
            Assert.AreEqual(3, state.Inventory.Count);
            Assert.AreEqual(0.7f, FindItem(state, "rack_milk_crate").Condition, 1e-6f);
            Assert.AreEqual("#1F7A8C", state.Bikes[0].PaintHex);
            Assert.AreEqual(0.35f, state.Bikes[0].PatinaLevel, 1e-6f);
            Assert.AreEqual("checker_stripe", state.Bikes[0].DecalStyle);
            Assert.AreEqual(0, SaveMigrator.Validate(state).Count, string.Join("; ", SaveMigrator.Validate(state)));

            SaveState loaded = SaveLoadBridge.Deserialize(SaveLoadBridge.Serialize(state));
            SimulationWorld restored = new SimulationWorld(new SeededSimRandom(9u), db.GetZone("Zone_WestOakland"));
            restored.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts));
            int skipped = SaveLoadBridge.ApplyWorldState(loaded, restored, db.Parts);
            Assert.AreEqual(0, skipped);
            Assert.AreEqual(65, restored.Reputation);
            Assert.AreEqual(3, restored.Inventory.Count);
            Assert.AreEqual(0.7f, restored.Inventory.FindFirstByPartId("rack_milk_crate").Condition, 1e-6f);

            BikeCustomizationState readBack = SaveLoadBridge.ReadCustomization(loaded.Bikes[0]);
            Assert.AreEqual("#1F7A8C", readBack.PaintHex);
            Assert.AreEqual("checker_stripe", readBack.DecalStyle);

            SaveState again = SaveLoadBridge.Capture(restored, AppCompositionRoot.DefaultBikeInstanceId, null, 9u, true, loaded, null);
            Assert.AreEqual(3, again.Inventory.Count, "save -> load -> save must not grow the inventory");
            Assert.AreEqual("checker_stripe", again.Bikes[0].DecalStyle, "Without explicit customization the baseline decal is carried over.");
        }

        [Test]
        public void ApplyWorldState_WithoutCatalog_KeepsLegacyBehavior()
        {
            SaveState state = SaveMigrator.CreateNewDefault(3u);
            state.CashUsd = 77m;
            state.Reputation = 12;
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(3u));
            Assert.AreEqual(0, SaveLoadBridge.ApplyWorldState(state, world));
            Assert.AreEqual(77m, world.CashUsd);
            Assert.AreEqual(12, world.Reputation);
            Assert.AreEqual(0, world.Inventory.Count);
        }

        private static InventoryItemState FindItem(SaveState state, string partId)
        {
            for (int i = 0; i < state.Inventory.Count; i++)
            {
                if (state.Inventory[i].PartId == partId)
                {
                    return state.Inventory[i];
                }
            }

            Assert.Fail("Missing inventory item " + partId);
            return null;
        }
    }

    /// <summary>
    /// JobBoardHudView / SwapMeetView display Core state only, with dirty-checked HUD writes.
    /// </summary>
    public sealed class EconomyViewTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void JobBoardHud_RendersSnapshot_AndUnchangedFramesWriteNothing()
        {
            _go = new GameObject("JobBoardUnderTest");
            JobBoardHudView hud = _go.AddComponent<JobBoardHudView>();
            GameObject banner = new GameObject("Banner");
            banner.transform.SetParent(_go.transform, false);
            GameObject textGo = new GameObject("Clock", typeof(RectTransform));
            textGo.transform.SetParent(_go.transform, false);
            UnityEngine.UI.Text clock = textGo.AddComponent<UnityEngine.UI.Text>();
            SetField(hud, "_rewardBanner", banner);
            SetField(hud, "_clockText", clock);

            JobSessionSnapshot active = new JobSessionSnapshot(JobSessionStatus.Active, "gig", JobType.CourierGig, JobFailReason.None,
                75.4f, 195f, 1, 3, "cp_b", 0.834f, 120f, 600f, 0, 0, false, false);
            hud.Render(in active, "Mission Crankcase & Gasket Courier");
            Assert.AreEqual("1:15", clock.text);
            Assert.AreEqual(195, hud.DisplayedLimitSeconds);
            Assert.AreEqual(83, hud.DisplayedIntegrityPercent);
            Assert.AreEqual(1, hud.DisplayedCheckpointsReached);
            Assert.AreEqual(3, hud.DisplayedCheckpointCount);
            Assert.AreEqual(JobBoardHudView.StatusActive, hud.DisplayedStatus);
            Assert.IsFalse(hud.IsRewardBannerVisible);

            int writes = hud.TextWriteCount;
            hud.Render(in active, "Mission Crankcase & Gasket Courier");
            Assert.AreEqual(writes, hud.TextWriteCount, "An unchanged snapshot must not touch any Text.");

            JobSessionSnapshot done = new JobSessionSnapshot(JobSessionStatus.Completed, "rally", JobType.BaySwarmRally, JobFailReason.None,
                90f, 300f, 3, 3, string.Empty, 1f, 600f, 600f, 2, 8, false, false);
            hud.Render(in done, "East Bay Scrambler");
            Assert.IsTrue(hud.IsRewardBannerVisible);
            Assert.IsTrue(banner.activeSelf);
            Assert.AreEqual(2, hud.DisplayedPlacement);
            Assert.AreEqual(8, hud.DisplayedFieldSize);

            JobSessionSnapshot collected = new JobSessionSnapshot(JobSessionStatus.Completed, "rally", JobType.BaySwarmRally, JobFailReason.None,
                90f, 300f, 3, 3, string.Empty, 1f, 600f, 600f, 2, 8, false, true);
            hud.Render(in collected, "East Bay Scrambler");
            Assert.IsFalse(hud.IsRewardBannerVisible);

            JobSessionSnapshot failed = new JobSessionSnapshot(JobSessionStatus.Failed, "gig", JobType.CourierGig, JobFailReason.CargoDestroyed,
                10f, 195f, 1, 3, string.Empty, 0f, 0f, 600f, 0, 0, false, false);
            hud.Render(in failed, "Mission Crankcase & Gasket Courier");
            Assert.AreEqual(JobBoardHudView.StatusCargoDestroyed, hud.DisplayedStatus);
        }

        [Test]
        public void SwapMeetView_ListsMarket_InspectsViaCore_AndRaisesPurchaseIntent()
        {
            CatalogDatabase db = RepoCatalog.Load();
            SwapMeetMarket market = new SwapMeetMarket(db.PartList, 42u);
            Assert.Greater(market.Offers.Count, 0);

            _go = new GameObject("SwapMeetUnderTest");
            SwapMeetView view = _go.AddComponent<SwapMeetView>();
            string requested = null;
            view.PurchaseRequested += id => requested = id;
            view.SetMarket(market);
            Assert.AreEqual(market.Offers.Count, view.OfferCount);
            Assert.IsFalse(view.RequestPurchase(), "Nothing selected yet.");

            Assert.IsTrue(view.SelectOffer(0));
            Assert.IsTrue(view.InspectSelectedOffer());
            Assert.IsTrue(view.HasInspection);
            Assert.IsTrue(market.Offers[0].IsInspected, "Inspection is performed by the Core market.");
            Assert.IsTrue(view.RequestPurchase());
            Assert.AreEqual(market.Offers[0].OfferId, requested);

            // App owns the purchase; the view only re-reads the rotation.
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(1u));
            world.CashUsd = 10000m;
            SwapMeetPurchaseResult result = market.TryBuyOffer(requested, world);
            Assert.IsTrue(result.Succeeded, result.Message);
            view.ShowPurchaseResult(in result);
            Assert.AreEqual(0, view.SelectedIndex);
            Assert.IsTrue(view.SelectedOffer.IsSold);
            Assert.IsFalse(view.RequestPurchase(), "Sold offers cannot be requested again.");
        }

        private static void SetField(Object target, string field, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// The 18 World kit .glb modules import as renderable GameObjects, and GlbMaterialRemapper keeps glTFast emission.
    /// </summary>
    public sealed class WorldKitImportTests
    {
        private static readonly string[] WorldModels = new string[]
        {
            "BayCrossing/bc_bridge_tower", "BayCrossing/bc_causeway_deck_20m", "BayCrossing/bc_ferry_terminal_dock",
            "OaklandHills/oh_eucalyptus_cluster", "OaklandHills/oh_guardrail_10m", "OaklandHills/oh_hill_road_20m", "OaklandHills/oh_overlook_sign",
            "SFMission/sf_catenary_pole", "SFMission/sf_delivery_beacon", "SFMission/sf_hill_road_20m", "SFMission/sf_mission_storefront",
            "SFMission/sf_muni_rail_road_20m", "SFMission/sf_victorian_rowhouse",
            "WestOakland/wo_bart_overpass_pillar", "WestOakland/wo_garage_facade", "WestOakland/wo_road_pothole_20m",
            "WestOakland/wo_taco_truck", "WestOakland/wo_warehouse_brick"
        };

        [Test]
        public void AllEighteenWorldModules_ImportWithRenderersAndMaterials()
        {
            Assert.AreEqual(18, WorldModels.Length);
            for (int i = 0; i < WorldModels.Length; i++)
            {
                string path = SceneScaffoldBuilder.WorldModelsFolder + "/" + WorldModels[i] + ".glb";
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, "Not imported as a GameObject: " + path);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Assert.Greater(renderers.Length, 0, path + " has no renderers.");
                for (int r = 0; r < renderers.Length; r++)
                {
                    Material[] mats = renderers[r].sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Assert.IsNotNull(mats[m], path + " renderer " + renderers[r].name + " has a missing material.");
                    }
                }

                Assert.IsNull(model.GetComponentInChildren<Collider>(true), path + " must stay a collider-free visual module.");
            }
        }

        [Test]
        public void DeliveryBeacon_EmissiveMaterial_KeepsEmissionWhenRemappedToUrpLit()
        {
            string path = SceneScaffoldBuilder.WorldModelsFolder + "/" + SceneScaffoldBuilder.DeliveryBeaconModel + ".glb";
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Material emissive = null;
            for (int i = 0; i < assets.Length; i++)
            {
                Material mat = assets[i] as Material;
                if (mat != null && GlbMaterialRemapper.ReadEmission(mat).maxColorComponent > 0f)
                {
                    emissive = mat;
                    break;
                }
            }

            Assert.IsNotNull(emissive, "sf_delivery_beacon.glb should import at least one emissive glTF material.");
            Assert.IsTrue(GlbMaterialRemapper.IsImportedModelMaterial(emissive), "Imported glTF materials are read-only sub-assets.");
            Color sourceEmission = GlbMaterialRemapper.ReadEmission(emissive);

            Material copy = GlbMaterialRemapper.CreateUrpCopy(emissive);
            try
            {
                Assert.IsNotNull(copy, "URP Lit shader not found.");
                Assert.AreEqual("Universal Render Pipeline/Lit", copy.shader.name);
                Assert.IsTrue(copy.IsKeywordEnabled("_EMISSION"), "Emission keyword lost during remap.");
                Color remapped = copy.GetColor("_EmissionColor");
                Assert.AreEqual(sourceEmission.r, remapped.r, 1e-4f);
                Assert.AreEqual(sourceEmission.g, remapped.g, 1e-4f);
                Assert.AreEqual(sourceEmission.b, remapped.b, 1e-4f);
                Assert.AreEqual(sourceEmission, GlbMaterialRemapper.ReadEmission(emissive), "The imported source material must stay untouched.");
            }
            finally
            {
                if (copy != null)
                {
                    Object.DestroyImmediate(copy);
                }
            }
        }
    }

    /// <summary>
    /// The committed scaffold scenes contain the M6 wiring produced by SceneScaffoldBuilder.
    /// </summary>
    public sealed class GeneratedSceneContentTests
    {
        private readonly List<Scene> _opened = new List<Scene>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                if (_opened[i].IsValid() && _opened[i].isLoaded && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(_opened[i], true);
                }
            }

            _opened.Clear();
        }

        private Scene Open(string sceneName)
        {
            Scene scene = EditorSceneManager.OpenScene(SceneScaffoldBuilder.GetScenePath(sceneName), OpenSceneMode.Additive);
            _opened.Add(scene);
            return scene;
        }

        private static T FindIn<T>(Scene scene) where T : Component
        {
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

        private static List<T> FindAllIn<T>(Scene scene) where T : Component
        {
            List<T> all = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                all.AddRange(roots[i].GetComponentsInChildren<T>(true));
            }

            return all;
        }

        [Test]
        public void ZoneScenes_HaveRoadsKitPortalsMirroringCatalog_AndCheckpointBeacons()
        {
            CatalogDatabase db = RepoCatalog.Load();
            for (int z = 0; z < ZoneStreamManager.KnownZoneScenes.Length; z++)
            {
                string zone = ZoneStreamManager.KnownZoneScenes[z];
                Scene scene = Open(zone);
                Assert.Greater(FindAllIn<RoadHazardZone>(scene).Count, 0, zone + " lost its road surfaces.");

                List<MeshRenderer> renderers = FindAllIn<MeshRenderer>(scene);
                int kitRenderers = 0;
                for (int i = 0; i < renderers.Count; i++)
                {
                    Assert.IsFalse(renderers[i].gameObject.name.EndsWith("_Placeholder"), zone + " used a placeholder: " + renderers[i].name);
                    if (renderers[i].GetComponent<RoadHazardZone>() == null)
                    {
                        kitRenderers++;
                    }
                }

                Assert.Greater(kitRenderers, 0, zone + " has no World kit .glb dressing.");

                List<ZoneTransitionPortal> portals = FindAllIn<ZoneTransitionPortal>(scene);
                IReadOnlyList<string> connected = db.ZoneMeta[zone].ConnectedZones;
                Assert.AreEqual(connected.Count, portals.Count, zone + " portals != connectedZones");
                for (int i = 0; i < connected.Count; i++)
                {
                    bool found = false;
                    for (int p = 0; p < portals.Count; p++)
                    {
                        found |= portals[p].TargetZoneScene == connected[i];
                        Assert.IsTrue(portals[p].GetComponent<Collider>().isTrigger);
                    }

                    Assert.IsTrue(found, zone + " has no portal to " + connected[i]);
                }

                int checkpoints = 0;
                foreach (JobDefinition job in db.Jobs.Values)
                {
                    for (int c = 0; c < job.Checkpoints.Count; c++)
                    {
                        if (job.Checkpoints[c].ZoneId == zone)
                        {
                            checkpoints++;
                        }
                    }
                }

                GameObject beaconRoot = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i].name == SceneScaffoldBuilder.CheckpointRootName)
                    {
                        beaconRoot = roots[i];
                    }
                }

                if (checkpoints > 0)
                {
                    Assert.IsNotNull(beaconRoot, zone + " is missing courier checkpoint beacons.");
                    Assert.Greater(beaconRoot.transform.childCount, 0);
                }
            }

            Scene sf = SceneManager.GetSceneByName("Zone_SF_Mission");
            Assert.IsNotNull(FindIn<KarlTheFogVolume>(sf), "Zone_SF_Mission lost its KarlTheFogVolume.");
        }

        [Test]
        public void BootstrapGarageAndOverlay_ContainEconomyCustomizationAndSettingsComponents()
        {
            Scene bootstrap = Open("Bootstrap");
            CourierGigController courier = FindIn<CourierGigController>(bootstrap);
            Assert.IsNotNull(courier);
            Assert.IsNotNull(courier.Rider, "Courier dispatch must track the ride moped.");
            Assert.IsNotNull(FindIn<RallyPackController>(bootstrap));
            Assert.IsNotNull(FindIn<AppCompositionRoot>(bootstrap));

            Scene garage = Open("Garage");
            PaintBoothController paint = FindIn<PaintBoothController>(garage);
            Assert.IsNotNull(paint);
            Assert.IsNotNull(paint.Assembler, "Paint booth must be bound to the WorkStand BikeVisualAssembler.");
            Assert.AreSame(FindIn<BikeVisualAssembler>(garage), paint.Assembler);

            Scene overlay = Open("UI_Overlay");
            Assert.IsNotNull(FindIn<SettingsMenuView>(overlay));
            Assert.IsNotNull(FindIn<SwapMeetView>(overlay));
            Assert.IsNotNull(FindIn<JobBoardHudView>(overlay));
            Assert.IsNotNull(FindIn<RideHudView>(overlay));
        }
    }
}
