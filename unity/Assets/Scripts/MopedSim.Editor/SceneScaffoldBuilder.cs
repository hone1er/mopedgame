using System.Collections.Generic;
using System.IO;
using MopedSim.App;
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
    /// is created in a fixed order with fixed values, so rebuilding produces identical scenes. Also writes EditorBuildSettings.
    /// </summary>
    public static class SceneScaffoldBuilder
    {
        public const string ScenesFolder = "Assets/Scenes";

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

            BuildScene("Bootstrap", PopulateBootstrap);
            BuildScene("Garage", PopulateGarage);
            BuildScene("UI_Overlay", PopulateUiOverlay);
            BuildScene("Zone_WestOakland", PopulateWestOakland);
            BuildScene("Zone_OaklandHills", PopulateOaklandHills);
            BuildScene("Zone_BayCrossing", PopulateBayCrossing);
            BuildScene("Zone_SF_Mission", PopulateSfMission);

            List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>(SceneNames.Length);
            for (int i = 0; i < SceneNames.Length; i++)
            {
                buildScenes.Add(new EditorBuildSettingsScene(GetScenePath(SceneNames[i]), true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneScaffoldBuilder] Rebuilt " + SceneNames.Length + " scaffold scenes and EditorBuildSettings.");
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
            moped.transform.position = new Vector3(0f, 0.6f, 0f);
            Rigidbody body = moped.AddComponent<Rigidbody>();
            body.mass = 127f;
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

            root.AssignCoreComponents(input, session, ride, null);
            root.AssignWorldComponents(zones, weather, aero, rig);
            root.AssignPresentationComponents(null, null, null, null, null, null, audioDriver);
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
            light.transform.position = new Vector3(0f, 3.2f, 0f);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "GarageFloor";
            floor.transform.position = new Vector3(0f, 0f, 0f);
            floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f);

            GameObject garageRoot = new GameObject("GarageRoot");
            garageRoot.transform.position = new Vector3(0f, 0f, 30f);

            GameObject stand = new GameObject("WorkStand");
            stand.transform.SetParent(garageRoot.transform, false);
            GameObject bikeRoot = new GameObject("WorkStandBikeRoot");
            bikeRoot.transform.SetParent(stand.transform, false);
            bikeRoot.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            BikeVisualAssembler assembler = stand.AddComponent<BikeVisualAssembler>();
            assembler.AssemblyRoot = bikeRoot.transform;

            GameObject camGo = new GameObject("InspectionCamera");
            camGo.transform.SetParent(garageRoot.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            GarageWorkstandController workstand = stand.AddComponent<GarageWorkstandController>();
            workstand.Assembler = assembler;
            SetObjectReference(workstand, "_inspectionCamera", cam);
            SetObjectReference(workstand, "_orbitPivot", bikeRoot.transform);

            GameObject stations = new GameObject("Stations");
            stations.transform.SetParent(garageRoot.transform, false);
            stations.AddComponent<FastenerWrenchInteractor>();
            stations.AddComponent<CarbWorkbenchController>();
            stations.AddComponent<PlugChopInspector>();
            stations.AddComponent<PremixStationController>();
            stations.AddComponent<CenterStandDynoController>();
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
            EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ Bay Area zone blockouts

        private static void PopulateWestOakland(Scene scene)
        {
            GameObject root = new GameObject("Zone_WestOakland_Root");
            CreateRoad(root.transform, "Maritime_PlugChopStraight", new Vector3(0f, 0f, 200f), new Vector3(12f, 0.2f, 400f), 0f, "DryAsphalt");
            CreateRoad(root.transform, "Frontage_PotholePatch", new Vector3(0f, 0.01f, 60f), new Vector3(10f, 0.2f, 20f), 0f, "PatchedPotholeAsphalt");
            CreateRoad(root.transform, "Frontage_GravelShoulder", new Vector3(9f, 0f, 200f), new Vector3(6f, 0.2f, 400f), 0f, "GravelShoulder");
            CreateMarker(root.transform, "LM_WestOaklandGarage", new Vector3(0f, 0f, 0f));
            CreateMarker(root.transform, "LM_PartsAlleySwapMeet", new Vector3(180f, 0f, 120f));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateOaklandHills(Scene scene)
        {
            GameObject root = new GameObject("Zone_OaklandHills_Root");
            root.transform.position = new Vector3(1000f, 0f, 0f);
            CreateRoad(root.transform, "LakeMerritt_Flat", new Vector3(0f, 0f, 50f), new Vector3(10f, 0.2f, 100f), 0f, "DryAsphalt");
            CreateRoad(root.transform, "TunnelRd_Grade10", new Vector3(0f, 5f, 150f), new Vector3(10f, 0.2f, 101f), Mathf.Atan(0.10f) * Mathf.Rad2Deg, "DryAsphalt");
            CreateRoad(root.transform, "GrizzlyPeak_Grade18", new Vector3(0f, 18f, 250f), new Vector3(10f, 0.2f, 102f), Mathf.Atan(0.18f) * Mathf.Rad2Deg, "DryAsphalt");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateBayCrossing(Scene scene)
        {
            GameObject root = new GameObject("Zone_BayCrossing_Root");
            root.transform.position = new Vector3(0f, 0f, 1000f);
            CreateRoad(root.transform, "BaySpan_Deck", new Vector3(0f, 20f, 600f), new Vector3(8f, 0.3f, 1200f), 0f, "DryAsphalt");
            CreateMarker(root.transform, "LM_FerryTerminal", new Vector3(0f, 20f, 1220f));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void PopulateSfMission(Scene scene)
        {
            GameObject root = new GameObject("Zone_SF_Mission_Root");
            root.transform.position = new Vector3(0f, 0f, 2500f);
            CreateRoad(root.transform, "Valencia_Corridor", new Vector3(0f, 0f, 150f), new Vector3(14f, 0.2f, 300f), 0f, "DryAsphalt");
            GameObject rail = CreateRoad(root.transform, "Muni_SteelRailTrack", new Vector3(0f, 0.02f, 150f), new Vector3(1.6f, 0.2f, 300f), 0f, "SteelRailTrack");
            rail.GetComponent<RoadHazardZone>().Configure("SteelRailTrack", 0.95f, 0.38f, 0.25f, 0.2f, Vector3.forward);
            CreateRoad(root.transform, "TwinPeaks_Grade22", new Vector3(0f, 22f, 350f), new Vector3(10f, 0.2f, 102f), Mathf.Atan(0.22f) * Mathf.Rad2Deg, "DryAsphalt");

            GameObject fog = new GameObject("KarlTheFog_Volume");
            fog.transform.SetParent(root.transform, false);
            fog.transform.localPosition = new Vector3(0f, 10f, 150f);
            BoxCollider fogBox = fog.AddComponent<BoxCollider>();
            fogBox.isTrigger = true;
            fogBox.size = new Vector3(60f, 30f, 300f);
            fog.AddComponent<RoadHazardZone>().ConfigureKarlFogVolume(0.9f);
            fog.AddComponent<KarlTheFogVolume>().Configure(0.9f, 20f);
            EditorSceneManager.MarkSceneDirty(scene);
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

        private static void SetObjectReference(Object target, string propertyName, Object value)
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
