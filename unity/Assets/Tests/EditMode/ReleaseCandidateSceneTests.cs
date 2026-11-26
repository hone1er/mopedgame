using System.Collections.Generic;
using MopedSim.App;
using MopedSim.Editor;
using MopedSim.Garage;
using MopedSim.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// M7 release-candidate scene and rendering contract: URP is the active pipeline with zero unsupported (magenta) .glb materials,
    /// Garage.unity is isolated from every zone and dressed with the four station models, UI_Overlay carries the controls banner,
    /// the two Oakland-end BayCrossing portals never overlap a rider at once, and AssetGallery.unity shows every .glb model.
    /// </summary>
    public sealed class ReleaseCandidateSceneTests
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

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        [Test]
        public void Urp_IsActivePipeline_ForGraphicsAndEveryQualityLevel_AndNoGlbMaterialIsMagenta()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.IsNotNull(pipeline, "GraphicsSettings.defaultRenderPipeline is unset: the project is running Built-In.");
            Assert.AreEqual("UniversalRenderPipelineAsset", pipeline.GetType().Name);
            Assert.AreEqual(UrpPipelineSetup.PipelineAssetPath, AssetDatabase.GetAssetPath(pipeline));
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                Assert.AreSame(pipeline, QualitySettings.GetRenderPipelineAssetAt(i), "Quality level " + QualitySettings.names[i]);
            }

            List<string> models = SceneScaffoldBuilder.FindAllModelPaths();
            Assert.AreEqual(69, models.Count, "Expected the full 69-model .glb set under Assets/Art/Models.");
            int materials = 0;
            for (int m = 0; m < models.Count; m++)
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(models[m]);
                for (int i = 0; i < subAssets.Length; i++)
                {
                    Material material = subAssets[i] as Material;
                    if (material == null)
                    {
                        continue;
                    }

                    materials++;
                    Assert.IsNotNull(material.shader, models[m] + " / " + material.name + " has no shader.");
                    Assert.IsTrue(material.shader.isSupported, models[m] + " / " + material.name + " uses unsupported shader " + material.shader.name);
                    Assert.IsFalse(material.shader.name.StartsWith("glTF/"), models[m] + " / " + material.name
                        + " still uses the Built-In glTFast shader " + material.shader.name);
                    Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name, models[m] + " / " + material.name);
                }
            }

            Assert.Greater(materials, 0);
        }

        [Test]
        public void Garage_IsIsolatedFromZones_AndDressedWithStationModels()
        {
            Scene garage = Open("Garage");
            GameObject garageRoot = FindRoot(garage, "GarageRoot");
            GameObject floor = FindRoot(garage, "GarageFloor");
            Assert.IsNotNull(garageRoot);
            Assert.IsNotNull(floor);
            Assert.AreEqual(SceneScaffoldBuilder.GarageOrigin, garageRoot.transform.position);
            Assert.AreEqual(SceneScaffoldBuilder.GarageOrigin, floor.transform.position);
            BikeVisualAssembler assembler = FindAllIn<BikeVisualAssembler>(garage)[0];
            Assert.AreNotSame(assembler.transform, assembler.AssemblyRoot, "The bike visual root must be a dedicated child (Ride <-> Garage).");

            for (int z = 0; z < ZoneStreamManager.KnownZoneScenes.Length; z++)
            {
                Scene zone = Open(ZoneStreamManager.KnownZoneScenes[z]);
                List<RoadHazardZone> roads = FindAllIn<RoadHazardZone>(zone);
                for (int r = 0; r < roads.Count; r++)
                {
                    Renderer c = roads[r].GetComponent<Renderer>();
                    if (c == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(c.bounds.ClosestPoint(garageRoot.transform.position), garageRoot.transform.position);
                    Assert.Greater(distance, 200f, "Garage overlaps " + roads[r].name + " in " + zone.name);
                }
            }

            Transform stations = garageRoot.transform.Find("StationModels");
            Assert.IsNotNull(stations, "Garage.unity lost its StationModels dressing.");
            string[] expected = { "garage_workstand", "garage_workbench", "garage_premix_can", "garage_dyno_roller" };
            for (int i = 0; i < expected.Length; i++)
            {
                Transform model = stations.Find(expected[i]);
                Assert.IsNotNull(model, "Missing garage station model " + expected[i]);
                Assert.Greater(model.GetComponentsInChildren<MeshRenderer>(true).Length, 0, expected[i] + " is a placeholder, not the .glb.");
                Assert.AreEqual(0, model.GetComponentsInChildren<Collider>(true).Length, expected[i] + " must be collider-free.");
            }
        }

        [Test]
        public void Bootstrap_RideMoped_SpawnsOnTheRoad_HeldKinematic()
        {
            Scene bootstrap = Open("Bootstrap");
            GameObject moped = FindRoot(bootstrap, "RideMoped");
            Assert.IsNotNull(moped);
            Assert.AreEqual(SceneScaffoldBuilder.RideMopedSpawn, moped.transform.position);
            Assert.IsTrue(moped.GetComponent<Rigidbody>().isKinematic, "RideMoped waits kinematic until the session enters Ride.");
        }

        [Test]
        public void UiOverlay_ShowsControlsCheatSheetBanner()
        {
            Scene overlay = Open("UI_Overlay");
            Text banner = null;
            List<Text> texts = FindAllIn<Text>(overlay);
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i].transform.parent != null && texts[i].transform.parent.name == SceneScaffoldBuilder.ControlsBannerName)
                {
                    banner = texts[i];
                }
            }

            Assert.IsNotNull(banner, "UI_Overlay is missing the controls banner.");
            Assert.AreEqual(SceneScaffoldBuilder.ControlsBannerText, banner.text);
            StringAssert.Contains("[F1/Tab] Ride/Garage", banner.text);
            StringAssert.Contains("[F4] Paint", banner.text);
            Assert.IsTrue(banner.gameObject.activeInHierarchy);
        }

        [Test]
        public void BayCrossing_OaklandEndPortals_AreAtLeastEightMetersApart()
        {
            Scene bay = Open("Zone_BayCrossing");
            List<ZoneTransitionPortal> portals = FindAllIn<ZoneTransitionPortal>(bay);
            Bounds westOakland = default(Bounds);
            Bounds oaklandHills = default(Bounds);
            int found = 0;
            for (int i = 0; i < portals.Count; i++)
            {
                BoxCollider box = portals[i].GetComponent<BoxCollider>();
                Bounds b = new Bounds(box.transform.TransformPoint(box.center), Vector3.Scale(box.size, box.transform.lossyScale));
                if (portals[i].TargetZoneScene == "Zone_WestOakland")
                {
                    westOakland = b;
                    found++;
                }
                else if (portals[i].TargetZoneScene == "Zone_OaklandHills")
                {
                    oaklandHills = b;
                    found++;
                }
            }

            Assert.AreEqual(2, found, "Both Oakland-end portals must exist.");
            Assert.IsFalse(westOakland.Intersects(oaklandHills));
            float gapZ = Mathf.Max(oaklandHills.min.z - westOakland.max.z, westOakland.min.z - oaklandHills.max.z);
            float gapX = Mathf.Max(oaklandHills.min.x - westOakland.max.x, westOakland.min.x - oaklandHills.max.x);
            float gap = Mathf.Max(gapX, gapZ);
            Assert.GreaterOrEqual(gap, SceneScaffoldBuilder.MinOaklandPortalSpacingMeters - 1e-3f,
                "Oakland-end portals are only " + gap + " m apart; a centered rider could trigger both.");
        }

        [Test]
        public void AssetGallery_ShowsEveryGlbModel_GroupedByCategory_WithFlyCamera()
        {
            Assert.IsFalse(System.Array.IndexOf(SceneScaffoldBuilder.SceneNames, SceneScaffoldBuilder.AssetGallerySceneName) >= 0,
                "AssetGallery is a showcase scene, not a runtime scene.");
            Scene gallery = Open(SceneScaffoldBuilder.AssetGallerySceneName);
            GameObject root = FindRoot(gallery, SceneScaffoldBuilder.GalleryRootName);
            Assert.IsNotNull(root);
            Assert.AreEqual(SceneScaffoldBuilder.GalleryCategories.Length, root.transform.childCount, "One band per category.");

            List<string> models = SceneScaffoldBuilder.FindAllModelPaths();
            int[] expectedPerCategory = new int[SceneScaffoldBuilder.GalleryCategories.Length];
            for (int i = 0; i < models.Count; i++)
            {
                expectedPerCategory[SceneScaffoldBuilder.GalleryCategoryIndex(models[i])]++;
            }

            int total = 0;
            for (int c = 0; c < root.transform.childCount; c++)
            {
                Transform band = root.transform.GetChild(c);
                int items = 0;
                for (int i = 0; i < band.childCount; i++)
                {
                    Transform child = band.GetChild(i);
                    if (child.GetComponent<TextMesh>() == null)
                    {
                        items++;
                        Assert.IsFalse(child.name.EndsWith("_Placeholder"), "Gallery placeholder: " + child.name);
                        Assert.IsNotNull(PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject), child.name + " is not a .glb prefab instance.");
                    }
                }

                Assert.AreEqual(expectedPerCategory[c], items, band.name);
                total += items;
            }

            Assert.AreEqual(69, total);
            Assert.AreEqual(1, FindAllIn<AssetGalleryFlyCamera>(gallery).Count);
            Assert.AreEqual(1, FindAllIn<Camera>(gallery).Count);
        }
    }
}
