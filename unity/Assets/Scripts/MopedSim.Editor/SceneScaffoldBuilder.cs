using System;
using System.Collections.Generic;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Audio;
using MopedSim.Garage;
using MopedSim.Ride;
using MopedSim.UI;
using MopedSim.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MopedSim.Editor
{
    /// <summary>
    /// Deterministically (re)builds the modular additive scenes (AGENTS.md Golden Rule 4 / ADR-004) from code so no scene is
    /// hand-wired: Bootstrap, Garage, UI_Overlay, and the four Bay Area Zone_* blockouts. Every object, transform, and reference
    /// is created in a fixed order with fixed values. Unity assigns fresh local fileIDs on every save, so a rebuild rewrites the
    /// committed .unity files even when nothing changed: CLI builds call <see cref="EnsureScenesAndBuildSettings"/> (scaffolds only
    /// missing scenes); run the "Rebuild All Scaffold Scenes" menu item and commit the result after changing a populator.
    /// <para>
    /// M6 dressing: each Zone_* scene keeps its blockout road cubes (names, colliders, RoadHazardZone surfaces) as the physics
    /// surface and adds the validated World kit .glb modules (Assets/Art/Models/World/**) as collider-free visuals.
    /// ZoneTransitionPortal triggers are generated from zones.json <c>connectedZones</c>, and courier checkpoint beacons are placed
    /// at the catalog checkpoint coordinates (absolute metric positions) of every job checkpoint in that zone. When a model or the
    /// catalog is missing (stripped checkout), the builder falls back to empty named placeholders instead of failing.
    /// </para>
    /// <para>
    /// M7: every build first runs <see cref="UrpPipelineSetup.EnsureUrpPipelineAssets"/> (URP active, glTFast materials regenerated).
    /// Garage.unity sits at <see cref="GarageOrigin"/>, far from every Zone_* road, dressed with the four Blender garage station
    /// .glb models; UI_Overlay carries the controls cheat-sheet banner; and <see cref="BuildAssetGalleryScene"/> writes the
    /// non-runtime AssetGallery.unity showcase of every .glb under Assets/Art/Models (not part of <see cref="SceneNames"/>).
    /// </para>
    /// </summary>
    public static class SceneScaffoldBuilder
    {
        public const string ScenesFolder = "Assets/Scenes";
        public const string WorldModelsFolder = "Assets/Art/Models/World";
        public const string DeliveryBeaconModel = "SFMission/sf_delivery_beacon";
        public const string CheckpointRootName = "Courier_Checkpoints";
        public const string PortalNamePrefix = "Portal_To_";
        public const string GarageModelsFolder = "Assets/Art/Models/Garage";
        public const string ArtModelsFolder = "Assets/Art/Models";
        public const string AssetGallerySceneName = "AssetGallery";
        public const string GalleryRootName = "AssetGallery_Root";
        public const string ControlsBannerName = "ControlsBanner";
        public const string ControlsBannerText =
            "[F1/Tab] Ride/Garage | [F2] Settings | [F3] Shop | [F4] Paint | [WASD] Ride | [Space] Pedal | [Ctrl] Tuck | [V] Cam";

        /// <summary>Garage.unity world origin: isolated from all four Bay Area zones (West Oakland's roads start at the origin).</summary>
        public static readonly Vector3 GarageOrigin = new Vector3(-500f, 0f, -500f);

        /// <summary>RideMoped spawn on West Oakland's Maritime straight (road top y = 0.1; wheel mounts settle ~0.42 m above it).</summary>
        public static readonly Vector3 RideMopedSpawn = new Vector3(0f, 0.25f, 12f);

        /// <summary>Minimum edge-to-edge spacing between the two Oakland-end BayCrossing portals (never both triggered at once).</summary>
        public const float MinOaklandPortalSpacingMeters = 8f;

        /// <summary>Gallery category labels in display order (folder under Assets/Art/Models -> label).</summary>
        public static readonly string[] GalleryCategories = new string[] { "Bike Parts", "Garage Stations", "Bay Area World Kit", "Test" };

        private static CatalogDatabase _catalog;

        public static readonly string[] SceneNames = new string[]
        {
            "Bootstrap",
            "Garage",
            "UI_Overlay",
            "Zone_WestOakland",
            "Zone_OaklandHills",
            "Zone_BayCrossing",
            "Zone_SF_Mission"
        };

        public static string GetScenePath(string sceneName)
        {
            return ScenesFolder + "/" + sceneName + ".unity";
        }

        [MenuItem("MopedSim/Scenes/Rebuild All Scaffold Scenes")]
        public static void BuildAllScenes()
        {
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            UrpPipelineSetup.EnsureUrpPipelineAssets();
            _catalog = TryLoadCatalog();
            BuildScene("Bootstrap", PopulateBootstrap);
            BuildScene("Garage", PopulateGarage);
            BuildScene("UI_Overlay", PopulateUiOverlay);
            BuildScene("Zone_WestOakland", PopulateWestOakland);
            BuildScene("Zone_OaklandHills", PopulateOaklandHills);
            BuildScene("Zone_BayCrossing", PopulateBayCrossing);
            BuildScene("Zone_SF_Mission", PopulateSfMission);

            WriteBuildSettingsScenes();
            _catalog = null;
            Debug.Log("[SceneScaffoldBuilder] Rebuilt " + SceneNames.Length + " scaffold scenes and EditorBuildSettings.");
        }

        /// <summary>
        /// Loads data/catalogs (repo root next to the Unity project); null (logged) when unavailable.
        /// </summary>
        public static CatalogDatabase TryLoadCatalog()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs"));
            try
            {
                return CatalogLoader.LoadFromDirectory(dir);
            }
            catch (Exception ex)
            {
                Debug.Log("[SceneScaffoldBuilder] Catalog unavailable at '" + dir + "' (" + ex.Message + "); portals and courier beacons skipped.");
                return null;
            }
        }

        /// <summary>
        /// Non-destructive variant for CLI builds: rebuilds the scaffold only when a scene is missing, then makes sure
        /// EditorBuildSettings lists every scaffold scene, leaving committed scenes byte-identical otherwise.
        /// </summary>
        public static void EnsureScenesAndBuildSettings()
        {
            UrpPipelineSetup.EnsureUrpPipelineAssets();
            if (!AllScenesExist())
            {
                BuildAllScenes();
                return;
            }

            WriteBuildSettingsScenes();
            Debug.Log("[SceneScaffoldBuilder] All " + SceneNames.Length + " scaffold scenes present; EditorBuildSettings verified.");
        }

        private static void WriteBuildSettingsScenes()
        {
            List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>(SceneNames.Length);
            for (int i = 0; i < SceneNames.Length; i++)
            {
                buildScenes.Add(new EditorBuildSettingsScene(GetScenePath(SceneNames[i]), true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        private delegate void ScenePopulator(Scene scene);

        private static void BuildScene(string sceneName, ScenePopulator populate)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            populate(scene);
            string path = GetScenePath(sceneName);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                Debug.LogError("[SceneScaffoldBuilder] Failed to save " + path);
            }
        }

        // ------------------------------------------------------------------ Bootstrap

        private static void PopulateBootstrap(Scene scene)
        {
            GameObject sun = new GameObject("Sun_GoldenHour");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.93f, 0.82f, 1f);
            sun.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

            GameObject appRoot = new GameObject("AppRoot");
            InputAdapter input = appRoot.AddComponent<InputAdapter>();
            GameSessionStateMachine session = appRoot.AddComponent<GameSessionStateMachine>();
            ZoneStreamManager zones = appRoot.AddComponent<ZoneStreamManager>();
            BayAreaWeatherController weather = appRoot.AddComponent<BayAreaWeatherController>();
            AppCompositionRoot root = appRoot.AddComponent<AppCompositionRoot>();

            GameObject moped = new GameObject("RideMoped");
            moped.transform.position = RideMopedSpawn;
            Rigidbody body = moped.AddComponent<Rigidbody>();
            body.mass = 127f;
            // Held kinematic until AppCompositionRoot enters Ride (zones stream in asynchronously after bootstrap).
            body.isKinematic = true;
            BoxCollider chassis = moped.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0f, 0.45f, 0f);
            chassis.size = new Vector3(0.45f, 0.7f, 1.6f);
            MopedBikeController ride = moped.AddComponent<MopedBikeController>();
            AeroTuckController aero = moped.AddComponent<AeroTuckController>();
            BrakeFadeController brakes = moped.AddComponent<BrakeFadeController>();
            PedalInputHandler pedal = moped.AddComponent<PedalInputHandler>();
            Transform frontMount = CreateChild(moped.transform, "FrontWheelMount", new Vector3(0f, 0.3f, 0.58f));
            Transform rearMount = CreateChild(moped.transform, "RearWheelMount", new Vector3(0f, 0.3f, -0.58f));
            ride.SetMounts(frontMount, rearMount);
            ride.SetAero(aero);
            ride.SetBrakes(brakes);
            ride.SetPedalHandler(pedal);

            GameObject audioGo = new GameObject("TwoStrokeAudio");
            audioGo.transform.SetParent(moped.transform, false);
            audioGo.AddComponent<AudioSource>();
            TwoStrokeAudioDriver audioDriver = audioGo.AddComponent<TwoStrokeAudioDriver>();

            GameObject camGo = new GameObject("RideCamera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 1.6f, -3.2f);
            MopedCameraRig rig = camGo.AddComponent<MopedCameraRig>();
            rig.SetTarget(moped.transform, cam);

            // Job sessions span several additive zones (e.g. West Oakland pickup -> Oakland Hills drop), so the single courier
            // dispatch and rally pack mirror live on the persistent Bootstrap AppRoot rather than in any Zone_* scene.
            CourierGigController courier = appRoot.AddComponent<CourierGigController>();
            courier.SetRider(moped.transform);
            GameObject beacon = PlaceWorldModel(null, DeliveryBeaconModel, Vector3.zero, 0f);
            beacon.name = "NextCheckpointBeacon";
            beacon.SetActive(false);
            SetObjectReference(courier, "_nextCheckpointBeacon", beacon.transform);

            GameObject pack = new GameObject("RallyPack");
            RallyPackController rally = pack.AddComponent<RallyPackController>();

            root.AssignCoreComponents(input, session, ride, null);
            root.AssignWorldComponents(zones, weather, aero, rig);
            root.AssignPresentationComponents(null, null, null, null, null, null, audioDriver);
            root.AssignEconomyComponents(null, courier, rally, null, null, null);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ Garage

        private static void PopulateGarage(Scene scene)
        {
            GameObject light = new GameObject("GarageWorkLight");
            Light l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.intensity = 2.2f;
            light.transform.position = GarageOrigin + new Vector3(0f, 3.2f, 0f);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "GarageFloor";
            floor.transform.position = GarageOrigin;
            floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f);

            GameObject garageRoot = new GameObject("GarageRoot");
            garageRoot.transform.position = GarageOrigin;

            GameObject stand = new GameObject("WorkStand");
            stand.transform.SetParent(garageRoot.transform, false);
            GameObject bikeRoot = new GameObject("WorkStandBikeRoot");
            bikeRoot.transform.SetParent(stand.transform, false);
            bikeRoot.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            BikeVisualAssembler assembler = stand.AddComponent<BikeVisualAssembler>();
            assembler.AssemblyRoot = bikeRoot.transform;

            GameObject camGo = new GameObject("InspectionCamera");
            camGo.transform.SetParent(garageRoot.transform, false);
            camGo.transform.localPosition = new Vector3(2.4f, 1.6f, -2.8f);
            camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(-2.4f, -1.05f, 2.8f));
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            GarageWorkstandController workstand = stand.AddComponent<GarageWorkstandController>();
            workstand.Assembler = assembler;
            SetObjectReference(workstand, "_inspectionCamera", cam);
            SetObjectReference(workstand, "_orbitPivot", bikeRoot.transform);

            PaintBoothController paintBooth = stand.AddComponent<PaintBoothController>();
            SetObjectReference(paintBooth, "_assembler", assembler);
            SetObjectReference(paintBooth, "_rendererRoot", bikeRoot.transform);

            GameObject stations = new GameObject("Stations");
            stations.transform.SetParent(garageRoot.transform, false);
            stations.AddComponent<FastenerWrenchInteractor>();
            stations.AddComponent<CarbWorkbenchController>();
            stations.AddComponent<PlugChopInspector>();
            stations.AddComponent<PremixStationController>();
            stations.AddComponent<CenterStandDynoController>();

            // Real Blender garage station models (collider-free visuals); empty placeholders when a model is missing.
            Transform models = CreateChild(garageRoot.transform, "StationModels", Vector3.zero);
            PlaceModel(models, GarageModelsFolder, "garage_workstand", Vector3.zero, 0f);
            PlaceModel(models, GarageModelsFolder, "garage_workbench", new Vector3(-2.6f, 0f, 1.8f), 90f);
            PlaceModel(models, GarageModelsFolder, "garage_premix_can", new Vector3(-2.6f, 0.88f, 1.3f), 20f);
            PlaceModel(models, GarageModelsFolder, "garage_dyno_roller", new Vector3(2.4f, 0f, 0.6f), 0f);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ UI overlay

        private static void PopulateUiOverlay(Scene scene)
        {
            GameObject canvasGo = new GameObject("HUDCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            RideHudView hud = CreatePanel(canvasGo.transform, "RideHud", new Vector2(0f, 0f), new Vector2(40f, 40f)).AddComponent<RideHudView>();
            SetObjectReference(hud, "_speedText", CreateText(hud.transform, "Speed", new Vector2(0f, 0f), 48));
            SetObjectReference(hud, "_rpmText", CreateText(hud.transform, "Rpm", new Vector2(0f, 60f), 28));
            SetObjectReference(hud, "_chtText", CreateText(hud.transform, "Cht", new Vector2(0f, 100f), 28));
            SetObjectReference(hud, "_afrText", CreateText(hud.transform, "Afr", new Vector2(0f, 140f), 28));
            SetObjectReference(hud, "_fuelText", CreateText(hud.transform, "Fuel", new Vector2(0f, 180f), 24));
            SetObjectReference(hud, "_gearText", CreateText(hud.transform, "Gear", new Vector2(220f, 0f), 36));
            SetObjectReference(hud, "_staminaText", CreateText(hud.transform, "Stamina", new Vector2(220f, 60f), 24));
            SetObjectReference(hud, "_statusText", CreateText(hud.transform, "Status", new Vector2(0f, 230f), 30));

            DiagnosticHintsPresenter hints = CreatePanel(canvasGo.transform, "DiagnosticHints", new Vector2(0.5f, 1f), new Vector2(-400f, -80f)).AddComponent<DiagnosticHintsPresenter>();
            SetObjectReference(hints, "_hintLabel", CreateText(hints.transform, "Hint", Vector2.zero, 24));

            GarageWrenchHudView wrenchHud = CreatePanel(canvasGo.transform, "WrenchHud", new Vector2(1f, 0f), new Vector2(-460f, 40f)).AddComponent<GarageWrenchHudView>();
            SetObjectReference(wrenchHud, "_fastenerLabel", CreateText(wrenchHud.transform, "Fastener", new Vector2(0f, 120f), 22));
            SetObjectReference(wrenchHud, "_torqueText", CreateText(wrenchHud.transform, "Torque", new Vector2(0f, 80f), 28));
            SetObjectReference(wrenchHud, "_zoneLabel", CreateText(wrenchHud.transform, "Zone", new Vector2(0f, 40f), 24));
            SetObjectReference(wrenchHud, "_resultLabel", CreateText(wrenchHud.transform, "Result", new Vector2(0f, 0f), 18));
            SetObjectReference(wrenchHud, "_torqueFill", CreateFill(wrenchHud.transform, "TorqueFill", new Vector2(0f, 160f)));

            PlugChopChartView plugChart = CreatePanel(canvasGo.transform, "PlugChopChart", new Vector2(1f, 1f), new Vector2(-460f, -260f)).AddComponent<PlugChopChartView>();
            SetObjectReference(plugChart, "_zoneLabel", CreateText(plugChart.transform, "ZoneLabel", new Vector2(0f, 60f), 24));
            SetObjectReference(plugChart, "_summaryLabel", CreateText(plugChart.transform, "Summary", new Vector2(0f, 20f), 18));
            SetObjectReference(plugChart, "_gapLabel", CreateText(plugChart.transform, "Gap", new Vector2(0f, -20f), 18));
            SetObjectReference(plugChart, "_insulatorPreview", CreateFill(plugChart.transform, "InsulatorPreview", new Vector2(0f, 100f)));

            ToolRollOverlayView toolRoll = CreatePanel(canvasGo.transform, "ToolRoll", new Vector2(0f, 1f), new Vector2(40f, -300f)).AddComponent<ToolRollOverlayView>();
            SetObjectReference(toolRoll, "_slotsLabel", CreateText(toolRoll.transform, "Slots", new Vector2(0f, 200f), 24));
            SetObjectReference(toolRoll, "_itemsList", CreateText(toolRoll.transform, "Items", new Vector2(0f, 0f), 18));

            PartsCatalogShopView shop = CreatePanel(canvasGo.transform, "PartsShop", new Vector2(0.5f, 0.5f), new Vector2(-300f, -200f)).AddComponent<PartsCatalogShopView>();
            SetObjectReference(shop, "_cashLabel", CreateText(shop.transform, "Cash", new Vector2(0f, 360f), 26));
            shop.gameObject.SetActive(false);

            JobBoardHudView jobBoard = CreatePanel(canvasGo.transform, "JobBoardHud", new Vector2(1f, 1f), new Vector2(-460f, -560f)).AddComponent<JobBoardHudView>();
            GameObject jobContent = new GameObject("Content", typeof(RectTransform));
            jobContent.transform.SetParent(jobBoard.transform, false);
            SetObjectReference(jobBoard, "_panelRoot", jobContent);
            SetObjectReference(jobBoard, "_titleText", CreateText(jobContent.transform, "Title", new Vector2(0f, 220f), 24));
            SetObjectReference(jobBoard, "_clockText", CreateText(jobContent.transform, "Clock", new Vector2(0f, 180f), 28));
            SetObjectReference(jobBoard, "_limitText", CreateText(jobContent.transform, "Limit", new Vector2(160f, 180f), 20));
            SetObjectReference(jobBoard, "_integrityText", CreateText(jobContent.transform, "CargoIntegrity", new Vector2(0f, 140f), 22));
            SetObjectReference(jobBoard, "_integrityFill", CreateFill(jobContent.transform, "CargoIntegrityFill", new Vector2(0f, 120f)));
            SetObjectReference(jobBoard, "_checkpointsReachedText", CreateText(jobContent.transform, "CheckpointsReached", new Vector2(0f, 80f), 22));
            SetObjectReference(jobBoard, "_checkpointCountText", CreateText(jobContent.transform, "CheckpointCount", new Vector2(80f, 80f), 22));
            SetObjectReference(jobBoard, "_placementText", CreateText(jobContent.transform, "RallyPlacement", new Vector2(0f, 40f), 22));
            SetObjectReference(jobBoard, "_fieldSizeText", CreateText(jobContent.transform, "RallyFieldSize", new Vector2(80f, 40f), 22));
            SetObjectReference(jobBoard, "_statusText", CreateText(jobContent.transform, "Status", new Vector2(0f, 0f), 22));
            Text bannerText = CreateText(jobContent.transform, "RewardBanner", new Vector2(0f, -40f), 26);
            bannerText.text = JobBoardHudView.RewardBannerText;
            bannerText.gameObject.SetActive(false);
            SetObjectReference(jobBoard, "_rewardBanner", bannerText.gameObject);
            jobContent.SetActive(false);

            SwapMeetView swapMeet = CreatePanel(canvasGo.transform, "SwapMeet", new Vector2(0.5f, 0.5f), new Vector2(-300f, -200f)).AddComponent<SwapMeetView>();
            SetObjectReference(swapMeet, "_offersLabel", CreateText(swapMeet.transform, "Offers", new Vector2(0f, 120f), 18));
            SetObjectReference(swapMeet, "_selectionLabel", CreateText(swapMeet.transform, "Selection", new Vector2(0f, 80f), 20));
            SetObjectReference(swapMeet, "_inspectionLabel", CreateText(swapMeet.transform, "Inspection", new Vector2(0f, 40f), 18));
            SetObjectReference(swapMeet, "_resultLabel", CreateText(swapMeet.transform, "Result", new Vector2(0f, 0f), 18));
            swapMeet.gameObject.SetActive(false);

            SettingsMenuView settings = CreatePanel(canvasGo.transform, "SettingsMenu", new Vector2(0.5f, 0.5f), new Vector2(-210f, -130f)).AddComponent<SettingsMenuView>();
            SetObjectReference(settings, "_graphicsPresetLabel", CreateText(settings.transform, "GraphicsPreset", new Vector2(0f, 120f), 22));
            SetObjectReference(settings, "_conflictStatusLabel", CreateText(settings.transform, "BindingConflict", new Vector2(0f, 80f), 18));
            settings.gameObject.SetActive(false);

            // Compact controls cheat sheet pinned to the top-center of the screen.
            GameObject banner = new GameObject(ControlsBannerName, typeof(RectTransform));
            banner.transform.SetParent(canvasGo.transform, false);
            RectTransform bannerRt = (RectTransform)banner.transform;
            bannerRt.anchorMin = new Vector2(0.5f, 1f);
            bannerRt.anchorMax = new Vector2(0.5f, 1f);
            bannerRt.pivot = new Vector2(0.5f, 1f);
            bannerRt.anchoredPosition = new Vector2(0f, -12f);
            bannerRt.sizeDelta = new Vector2(1500f, 40f);
            Image bannerBack = banner.AddComponent<Image>();
            bannerBack.color = new Color(0f, 0f, 0f, 0.55f);
            bannerBack.raycastTarget = false;
            Text cheatSheet = CreateText(banner.transform, "Text", Vector2.zero, 22);
            RectTransform textRt = (RectTransform)cheatSheet.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            cheatSheet.alignment = TextAnchor.MiddleCenter;
            cheatSheet.raycastTarget = false;
            cheatSheet.text = ControlsBannerText;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ Asset gallery (showcase, not a runtime scene)

        /// <summary>
        /// Writes Assets/Scenes/AssetGallery.unity: every .glb under Assets/Art/Models instantiated in a labelled grid grouped by
        /// category (Bike Parts, Garage Stations, Bay Area World Kit, Test) with a WASD + right-drag free-fly camera. Not added to
        /// EditorBuildSettings and not part of <see cref="SceneNames"/>.
        /// </summary>
        [MenuItem("MopedSim/Scenes/Build Asset Gallery Scene")]
        public static void BuildAssetGalleryScene()
        {
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            UrpPipelineSetup.EnsureUrpPipelineAssets();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int placed = PopulateAssetGallery();
            string path = GetScenePath(AssetGallerySceneName);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                Debug.LogError("[SceneScaffoldBuilder] Failed to save " + path);
                return;
            }

            Debug.Log("[SceneScaffoldBuilder] Built " + path + " with " + placed + " .glb models.");
        }

        /// <summary>Sorted asset paths of every .glb under Assets/Art/Models.</summary>
        public static List<string> FindAllModelPaths()
        {
            List<string> paths = new List<string>(80);
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { ArtModelsFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>Gallery category label for a model asset path (index into <see cref="GalleryCategories"/>).</summary>
        public static int GalleryCategoryIndex(string assetPath)
        {
            string rel = assetPath.Substring(Math.Min(assetPath.Length, ArtModelsFolder.Length + 1));
            if (rel.StartsWith("Parts/", StringComparison.Ordinal)) { return 0; }
            if (rel.StartsWith("Garage/", StringComparison.Ordinal)) { return 1; }
            if (rel.StartsWith("World/", StringComparison.Ordinal)) { return 2; }
            return 3;
        }

        private static int PopulateAssetGallery()
        {
            GameObject sun = new GameObject("Gallery_Sun");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.88f, 1f);
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            GameObject root = new GameObject(GalleryRootName);
            List<string> paths = FindAllModelPaths();
            // Row width wraps per category so small bike parts sit close together and 20 m world modules get room.
            float[] rowWidths = { 9f, 12f, 110f, 4f };
            float[] gaps = { 0.6f, 1.2f, 6f, 1f };
            float[] headerSizes = { 0.09f, 0.12f, 0.5f, 0.09f };
            float[] labelSizes = { 0.018f, 0.04f, 0.2f, 0.03f };
            float bandZ = 0f;
            int placed = 0;
            for (int c = 0; c < GalleryCategories.Length; c++)
            {
                List<string> members = new List<string>();
                for (int i = 0; i < paths.Count; i++)
                {
                    if (GalleryCategoryIndex(paths[i]) == c)
                    {
                        members.Add(paths[i]);
                    }
                }

                if (members.Count == 0)
                {
                    continue;
                }

                Transform band = CreateChild(root.transform, "Category_" + GalleryCategories[c].Replace(" ", string.Empty), new Vector3(0f, 0f, bandZ));
                CreateLabel(band, "Header", GalleryCategories[c] + " (" + members.Count + ")", new Vector3(0f, 0.02f, c == 2 ? -4f : -0.9f), headerSizes[c], 90f);

                float cursorX = 0f;
                float rowZ = 0f;
                float rowDepth = 0f;
                for (int i = 0; i < members.Count; i++)
                {
                    string assetPath = members[i];
                    string modelName = Path.GetFileNameWithoutExtension(assetPath);
                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    GameObject item = asset != null ? PrefabUtility.InstantiatePrefab(asset) as GameObject : null;
                    if (item == null)
                    {
                        item = new GameObject(modelName + "_Placeholder");
                    }

                    item.name = modelName;
                    item.transform.SetParent(band, false);
                    item.transform.localPosition = Vector3.zero;
                    item.transform.localRotation = Quaternion.identity;
                    Bounds b = ComputeLocalBounds(item.transform, band);
                    float width = Mathf.Max(0.3f, b.size.x);
                    float depth = Mathf.Max(0.3f, b.size.z);
                    if (cursorX > 0f && cursorX + width > rowWidths[c])
                    {
                        rowZ += rowDepth + gaps[c] + 0.4f;
                        cursorX = 0f;
                        rowDepth = 0f;
                    }

                    item.transform.localPosition = new Vector3(cursorX - b.min.x, -b.min.y, rowZ - b.min.z);
                    CreateLabel(band, "Label_" + modelName, modelName, new Vector3(cursorX, 0.01f, rowZ - (c == 2 ? 0.6f : 0.12f)), labelSizes[c], 90f);
                    cursorX += width + gaps[c];
                    rowDepth = Mathf.Max(rowDepth, depth);
                    placed++;
                }

                bandZ += rowZ + rowDepth + (c == 2 ? 12f : 4f);
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Gallery_Floor";
            floor.transform.position = new Vector3(55f, -0.01f, bandZ * 0.5f);
            floor.transform.localScale = new Vector3(16f, 1f, Mathf.Max(4f, bandZ / 10f + 2f));

            GameObject camGo = new GameObject("GalleryCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<AssetGalleryFlyCamera>();
            camGo.transform.position = new Vector3(4.5f, 3.2f, -5.5f);
            camGo.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            return placed;
        }

        private static Bounds ComputeLocalBounds(Transform item, Transform space)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.3f);
            }

            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                world.Encapsulate(renderers[i].bounds);
            }

            Vector3 min = space.InverseTransformPoint(world.min);
            Vector3 max = space.InverseTransformPoint(world.max);
            Bounds local = new Bounds((min + max) * 0.5f, Vector3.zero);
            local.Encapsulate(min);
            local.Encapsulate(max);
            return local;
        }

        private static void CreateLabel(Transform parent, string name, string text, Vector3 localPosition, float characterSize, float tiltDeg)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(tiltDeg, 0f, 0f);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = characterSize;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.UpperLeft;
            mesh.color = new Color(0.95f, 0.95f, 0.9f, 1f);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font != null)
            {
                renderer.sharedMaterial = font.material;
            }
        }

        // ------------------------------------------------------------------ Bay Area zone blockouts

        private static void PopulateWestOakland(Scene scene)
        {
            GameObject root = new GameObject("Zone_WestOakland_Root");
            CreateRoad(root.transform, "Maritime_PlugChopStraight", new Vector3(0f, 0f, 200f), new Vector3(12f, 0.2f, 400f), 0f, "DryAsphalt");
            CreateRoad(root.transform, "Frontage_PotholePatch", new Vector3(0f, 0.01f, 60f), new Vector3(10f, 0.2f, 20f), 0f, "PatchedPotholeAsphalt");
            // Spur road so the Oakland Hills portal (x 23..29, z 290..310) is reachable on pavement from the main road.
            CreateRoad(root.transform, "OaklandHills_Spur", new Vector3(18f, 0f, 300f), new Vector3(24f, 0.2f, 20f), 0f, "DryAsphalt");
            CreateRoad(root.transform, "Frontage_GravelShoulder", new Vector3(9f, 0f, 200f), new Vector3(6f, 0.2f, 400f), 0f, "GravelShoulder");
            CreateMarker(root.transform, "LM_WestOaklandGarage", new Vector3(0f, 0f, 0f));
            CreateMarker(root.transform, "LM_PartsAlleySwapMeet", new Vector3(180f, 0f, 120f));

            Transform kit = CreateChild(root.transform, "WorldKit", Vector3.zero);
            for (int k = 0; k < 10; k++)
            {
                PlaceWorldModel(kit, "WestOakland/wo_road_pothole_20m", new Vector3(0f, RoadKitLift, 10f + 20f * k), 0f);
            }

            PlaceWorldModel(kit, "WestOakland/wo_garage_facade", new Vector3(-16f, 0f, -4f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_warehouse_brick", new Vector3(-26f, 0f, 60f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_warehouse_brick", new Vector3(-26f, 0f, 130f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_warehouse_brick", new Vector3(-26f, 0f, 200f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_bart_overpass_pillar", new Vector3(30f, 0f, 240f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_bart_overpass_pillar", new Vector3(30f, 0f, 260f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_bart_overpass_pillar", new Vector3(30f, 0f, 280f), 90f);
            PlaceWorldModel(kit, "WestOakland/wo_taco_truck", new Vector3(172f, 0f, 116f), -30f);

            CreatePortals(root.transform, "Zone_WestOakland",
                new PortalAnchor("Zone_OaklandHills", new Vector3(26f, 2f, 300f), new Vector3(6f, 6f, 20f)),
                new PortalAnchor("Zone_BayCrossing", new Vector3(0f, 2f, 396f), new Vector3(24f, 6f, 4f)));
            CreateCheckpointBeacons(scene, "Zone_WestOakland");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateOaklandHills(Scene scene)
        {
            GameObject root = new GameObject("Zone_OaklandHills_Root");
            root.transform.position = new Vector3(1000f, 0f, 0f);
            CreateRoad(root.transform, "LakeMerritt_Flat", new Vector3(0f, 0f, 50f), new Vector3(10f, 0.2f, 100f), 0f, "DryAsphalt");
            CreateRoad(root.transform, "TunnelRd_Grade10", new Vector3(0f, 5f, 150f), new Vector3(10f, 0.2f, 101f), Mathf.Atan(0.10f) * Mathf.Rad2Deg, "DryAsphalt");
            CreateRoad(root.transform, "GrizzlyPeak_Grade18", new Vector3(0f, 18f, 250f), new Vector3(10f, 0.2f, 102f), Mathf.Atan(0.18f) * Mathf.Rad2Deg, "DryAsphalt");

            Transform kit = CreateChild(root.transform, "WorldKit", Vector3.zero);
            PlaceWorldModel(kit, "OaklandHills/oh_hill_road_20m", new Vector3(18f, 0f, 20f), 0f);
            PlaceWorldModel(kit, "OaklandHills/oh_hill_road_20m", new Vector3(18f, 0f, 60f), 0f);
            float tunnelPitch = Mathf.Atan(0.10f) * Mathf.Rad2Deg;
            for (int k = 0; k < 10; k++)
            {
                float z = 105f + 10f * k;
                GameObject rail = PlaceWorldModel(kit, "OaklandHills/oh_guardrail_10m", new Vector3(5.6f, 5.1f + (z - 150f) * 0.10f, z), 0f);
                rail.transform.localRotation = Quaternion.Euler(-tunnelPitch, 0f, 0f);
            }

            PlaceWorldModel(kit, "OaklandHills/oh_eucalyptus_cluster", new Vector3(-18f, 0f, 60f), 0f);
            PlaceWorldModel(kit, "OaklandHills/oh_eucalyptus_cluster", new Vector3(-20f, 5f, 140f), 120f);
            PlaceWorldModel(kit, "OaklandHills/oh_eucalyptus_cluster", new Vector3(16f, 12f, 225f), 240f);
            PlaceWorldModel(kit, "OaklandHills/oh_overlook_sign", new Vector3(8f, 27f, 296f), -90f);

            CreatePortals(root.transform, "Zone_OaklandHills",
                new PortalAnchor("Zone_WestOakland", new Vector3(0f, 2f, 3f), new Vector3(14f, 6f, 4f)),
                new PortalAnchor("Zone_BayCrossing", new Vector3(0f, 29f, 299f), new Vector3(14f, 6f, 4f)));
            CreateCheckpointBeacons(scene, "Zone_OaklandHills");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateBayCrossing(Scene scene)
        {
            GameObject root = new GameObject("Zone_BayCrossing_Root");
            root.transform.position = new Vector3(0f, 0f, 1000f);
            CreateRoad(root.transform, "BaySpan_Deck", new Vector3(0f, 20f, 600f), new Vector3(8f, 0.3f, 1200f), 0f, "DryAsphalt");
            CreateMarker(root.transform, "LM_FerryTerminal", new Vector3(0f, 20f, 1220f));

            Transform kit = CreateChild(root.transform, "WorldKit", Vector3.zero);
            for (int k = 0; k < 60; k++)
            {
                PlaceWorldModel(kit, "BayCrossing/bc_causeway_deck_20m", new Vector3(0f, 20.15f + RoadKitLift, 10f + 20f * k), 0f);
            }

            // Tower SOCKET_DECK_LEVEL sits 8.8 m above the tower origin: drop the towers so the deck socket meets the span deck.
            PlaceWorldModel(kit, "BayCrossing/bc_bridge_tower", new Vector3(0f, 20.15f - 8.8f, 300f), 0f);
            PlaceWorldModel(kit, "BayCrossing/bc_bridge_tower", new Vector3(0f, 20.15f - 8.8f, 900f), 0f);
            PlaceWorldModel(kit, "BayCrossing/bc_ferry_terminal_dock", new Vector3(14f, 19.5f, 1210f), -90f);

            // The span is the only road: the Oakland approaches split into lanes (left lane West Oakland, right lane Oakland Hills)
            // AND are staggered along the deck (Oakland Hills exit first, MinOaklandPortalSpacingMeters before the West Oakland end),
            // so a rider straddling the centerline can never be inside both triggers at once.
            CreatePortals(root.transform, "Zone_BayCrossing",
                new PortalAnchor("Zone_WestOakland", new Vector3(-2f, 22f, 4f), new Vector3(3.8f, 6f, 4f)),
                new PortalAnchor("Zone_OaklandHills", new Vector3(2f, 22f, 4f + 4f + MinOaklandPortalSpacingMeters), new Vector3(3.8f, 6f, 4f)),
                new PortalAnchor("Zone_SF_Mission", new Vector3(0f, 22f, 1196f), new Vector3(8f, 6f, 4f)));
            CreateCheckpointBeacons(scene, "Zone_BayCrossing");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateSfMission(Scene scene)
        {
            GameObject root = new GameObject("Zone_SF_Mission_Root");
            root.transform.position = new Vector3(0f, 0f, 2500f);
            CreateRoad(root.transform, "Valencia_Corridor", new Vector3(0f, 0f, 150f), new Vector3(14f, 0.2f, 300f), 0f, "DryAsphalt");
            GameObject rail = CreateRoad(root.transform, "Muni_SteelRailTrack", new Vector3(0f, 0.02f, 150f), new Vector3(1.6f, 0.2f, 300f), 0f, "SteelRailTrack");
            rail.GetComponent<RoadHazardZone>().Configure("SteelRailTrack", 0.95f, 0.38f, 0.25f, 0.2f, Vector3.forward);
            // Centre y = 11 so the surface climbs 0 -> 22 over local z 300..400 (catalog formula y = (z - 2800) * 0.22), meeting Valencia flush at z 300.
            CreateRoad(root.transform, "TwinPeaks_Grade22", new Vector3(0f, 11f, 350f), new Vector3(10f, 0.2f, 102f), Mathf.Atan(0.22f) * Mathf.Rad2Deg, "DryAsphalt");

            GameObject fog = new GameObject("KarlTheFog_Volume");
            fog.transform.SetParent(root.transform, false);
            fog.transform.localPosition = new Vector3(0f, 10f, 150f);
            BoxCollider fogBox = fog.AddComponent<BoxCollider>();
            fogBox.isTrigger = true;
            fogBox.size = new Vector3(60f, 30f, 300f);
            fog.AddComponent<RoadHazardZone>().ConfigureKarlFogVolume(0.9f);
            fog.AddComponent<KarlTheFogVolume>().Configure(0.9f, 20f);

            Transform kit = CreateChild(root.transform, "WorldKit", Vector3.zero);
            for (int k = 0; k < 15; k++)
            {
                PlaceWorldModel(kit, "SFMission/sf_muni_rail_road_20m", new Vector3(0f, RoadKitLift, 10f + 20f * k), 0f);
            }

            for (int k = 0; k < 7; k++)
            {
                PlaceWorldModel(kit, "SFMission/sf_catenary_pole", new Vector3(5f, 0f, 20f + 40f * k), 0f);
            }

            PlaceWorldModel(kit, "SFMission/sf_victorian_rowhouse", new Vector3(-24f, 0f, 40f), 90f);
            PlaceWorldModel(kit, "SFMission/sf_victorian_rowhouse", new Vector3(-24f, 0f, 70f), 90f);
            PlaceWorldModel(kit, "SFMission/sf_victorian_rowhouse", new Vector3(-24f, 0f, 100f), 90f);
            PlaceWorldModel(kit, "SFMission/sf_victorian_rowhouse", new Vector3(-24f, 0f, 130f), 90f);
            PlaceWorldModel(kit, "SFMission/sf_mission_storefront", new Vector3(24f, 0f, 60f), -90f);
            PlaceWorldModel(kit, "SFMission/sf_mission_storefront", new Vector3(24f, 0f, 110f), -90f);
            PlaceWorldModel(kit, "SFMission/sf_hill_road_20m", new Vector3(16f, 1.8f, 330f), 0f);
            PlaceWorldModel(kit, "SFMission/sf_hill_road_20m", new Vector3(16f, 5.4f, 350f), 0f);

            CreatePortals(root.transform, "Zone_SF_Mission",
                new PortalAnchor("Zone_BayCrossing", new Vector3(0f, 2f, 3f), new Vector3(14f, 6f, 4f)));
            CreateCheckpointBeacons(scene, "Zone_SF_Mission");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ World kit, portals & courier beacons

        /// <summary>Road kit tiles sit just above the blockout road cube tops (0.1 m) so the visual surface covers the collider.</summary>
        private const float RoadKitLift = 0.12f;

        private readonly struct PortalAnchor
        {
            public readonly string TargetZone;
            public readonly Vector3 LocalPosition;
            public readonly Vector3 Size;

            public PortalAnchor(string targetZone, Vector3 localPosition, Vector3 size)
            {
                TargetZone = targetZone;
                LocalPosition = localPosition;
                Size = size;
            }
        }

        /// <summary>
        /// Instantiates a World kit model (<c>Assets/Art/Models/World/{relativePath}.glb</c>) as a prefab instance under
        /// <paramref name="parent"/> (scene root when null); falls back to an empty named placeholder when the model is missing.
        /// </summary>
        private static GameObject PlaceWorldModel(Transform parent, string relativePath, Vector3 localPosition, float yawDeg)
        {
            return PlaceModel(parent, WorldModelsFolder, relativePath, localPosition, yawDeg);
        }

        /// <summary>
        /// Instantiates <c>{folder}/{relativePath}.glb</c> as a prefab instance (empty named placeholder when the model is missing).
        /// </summary>
        private static GameObject PlaceModel(Transform parent, string folder, string relativePath, Vector3 localPosition, float yawDeg)
        {
            string assetPath = folder + "/" + relativePath + ".glb";
            string modelName = Path.GetFileNameWithoutExtension(relativePath);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            GameObject instance = null;
            if (asset != null)
            {
                instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
                if (instance == null)
                {
                    instance = UnityEngine.Object.Instantiate(asset);
                }
            }

            if (instance == null)
            {
                instance = new GameObject(modelName + "_Placeholder");
            }
            else
            {
                instance.name = modelName;
            }

            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            return instance;
        }

        /// <summary>
        /// One ZoneTransitionPortal per zones.json connectedZones entry of <paramref name="zoneId"/>; anchors give the boundary
        /// placement, and a connected zone without an anchor gets a portal 50 m from the zone root toward the target zone.
        /// </summary>
        private static void CreatePortals(Transform zoneRoot, string zoneId, params PortalAnchor[] anchors)
        {
            ZoneMetadata meta;
            if (_catalog == null || !_catalog.ZoneMeta.TryGetValue(zoneId, out meta) || meta == null)
            {
                return;
            }

            Transform portals = CreateChild(zoneRoot, "ZonePortals", Vector3.zero);
            for (int i = 0; i < meta.ConnectedZones.Count; i++)
            {
                string target = meta.ConnectedZones[i];
                ZoneMetadata targetMeta;
                string targetScene = _catalog.ZoneMeta.TryGetValue(target, out targetMeta) && targetMeta != null && !string.IsNullOrEmpty(targetMeta.SceneName)
                    ? targetMeta.SceneName
                    : target;

                Vector3 position = new Vector3(0f, 2f, 50f);
                Vector3 size = new Vector3(14f, 6f, 4f);
                bool anchored = false;
                for (int a = 0; a < anchors.Length; a++)
                {
                    if (string.Equals(anchors[a].TargetZone, target, StringComparison.Ordinal))
                    {
                        position = anchors[a].LocalPosition;
                        size = anchors[a].Size;
                        anchored = true;
                        break;
                    }
                }

                if (!anchored)
                {
                    Debug.Log("[SceneScaffoldBuilder] No portal anchor for " + zoneId + " -> " + target + "; using default placement.");
                }

                GameObject go = new GameObject(PortalNamePrefix + targetScene);
                go.transform.SetParent(portals, false);
                go.transform.localPosition = position;
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;
                ZoneTransitionPortal portal = go.AddComponent<ZoneTransitionPortal>();
                portal.Configure(targetScene, false, 1.5f);
            }
        }

        /// <summary>
        /// Places a delivery beacon at every catalog job checkpoint located in <paramref name="zoneId"/> (absolute metric catalog
        /// coordinates, deduplicated by checkpoint id) under a world-origin <see cref="CheckpointRootName"/> object.
        /// </summary>
        private static void CreateCheckpointBeacons(Scene scene, string zoneId)
        {
            if (_catalog == null)
            {
                return;
            }

            List<string> jobIds = new List<string>(_catalog.Jobs.Keys);
            jobIds.Sort(StringComparer.Ordinal);
            HashSet<string> placed = new HashSet<string>(StringComparer.Ordinal);
            Transform root = null;
            for (int j = 0; j < jobIds.Count; j++)
            {
                JobDefinition job = _catalog.Jobs[jobIds[j]];
                for (int c = 0; c < job.Checkpoints.Count; c++)
                {
                    JobCheckpointSpec cp = job.Checkpoints[c];
                    if (!string.Equals(cp.ZoneId, zoneId, StringComparison.Ordinal) || !placed.Add(cp.Id))
                    {
                        continue;
                    }

                    if (root == null)
                    {
                        root = new GameObject(CheckpointRootName).transform;
                    }

                    GameObject beacon = PlaceWorldModel(root, DeliveryBeaconModel, new Vector3(cp.X, cp.Y, cp.Z), 0f);
                    beacon.name = "Beacon_" + cp.Id;
                }
            }
        }

        // ------------------------------------------------------------------ Helpers

        private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static GameObject CreateRoad(Transform parent, string name, Vector3 localPosition, Vector3 size, float pitchUpDeg, string surfaceTypeId)
        {
            GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = name;
            road.transform.SetParent(parent, false);
            road.transform.localPosition = localPosition;
            road.transform.localRotation = Quaternion.Euler(-pitchUpDeg, 0f, 0f);
            road.transform.localScale = size;
            road.isStatic = true;
            RoadHazardZone hazard = road.AddComponent<RoadHazardZone>();
            SurfaceHazardDefaults(hazard, surfaceTypeId);
            return road;
        }

        private static void SurfaceHazardDefaults(RoadHazardZone hazard, string surfaceTypeId)
        {
            // Mirrors the PROJECT_GUIDE.md section 2.4 surface matrix (rolling drag, grip, roughness, pothole impulse).
            switch (surfaceTypeId)
            {
                case "PatchedPotholeAsphalt":
                    hazard.Configure(surfaceTypeId, 1.08f, 0.90f, 0.65f, 1.25f, Vector3.forward);
                    break;
                case "WetAsphalt":
                    hazard.Configure(surfaceTypeId, 1.10f, 0.76f, 0.10f, 0f, Vector3.forward);
                    break;
                case "SteelRailTrack":
                    hazard.Configure(surfaceTypeId, 0.95f, 0.38f, 0.25f, 0.20f, Vector3.forward);
                    break;
                case "GravelShoulder":
                    hazard.Configure(surfaceTypeId, 1.35f, 0.62f, 0.50f, 0.35f, Vector3.forward);
                    break;
                default:
                    hazard.Configure("DryAsphalt", 1.00f, 1.00f, 0.05f, 0f, Vector3.forward);
                    break;
            }
        }

        private static void CreateMarker(Transform parent, string name, Vector3 localPosition)
        {
            CreateChild(parent, name, localPosition);
        }

        private static GameObject CreatePanel(Transform canvas, string name, Vector2 anchor, Vector2 anchoredPosition)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(canvas, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(420f, 260f);
            return panel;
        }

        private static Text CreateText(Transform parent, string name, Vector2 anchoredPosition, int fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(400f, fontSize + 12f);
            Text text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.text = string.Empty;
            return text;
        }

        private static Image CreateFill(Transform parent, string name, Vector2 anchoredPosition)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = new Vector2(360f, 24f);
            Image image = go.AddComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = 0f;
            return image;
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogError("[SceneScaffoldBuilder] Missing serialized field '" + propertyName + "' on " + target.GetType().Name);
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// True when every scaffold scene exists on disk (used by CliBuild and EditMode checks).
        /// </summary>
        public static bool AllScenesExist()
        {
            for (int i = 0; i < SceneNames.Length; i++)
            {
                if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), GetScenePath(SceneNames[i]))))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
