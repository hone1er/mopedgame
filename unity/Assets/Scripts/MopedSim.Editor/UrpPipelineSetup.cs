using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MopedSim.Editor
{
    /// <summary>
    /// Idempotently activates the Universal Render Pipeline (ADR-007): creates <c>Assets/Settings/MopedSim_URP_Asset.asset</c> +
    /// <c>MopedSim_URP_Renderer.asset</c> once (mirroring URP 17's own "URP Asset (with Universal Renderer)" create path), assigns the
    /// asset to <see cref="GraphicsSettings.defaultRenderPipeline"/> and every <see cref="QualitySettings"/> level, and, only when an
    /// assignment actually changed, reimports <c>Assets/Art/Models</c> so glTFast regenerates its materials with URP shader graphs
    /// (no magenta Built-In materials). Called from SceneScaffoldBuilder and therefore from CliBuild before every player build.
    /// </summary>
    public static class UrpPipelineSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string PipelineAssetPath = SettingsFolder + "/MopedSim_URP_Asset.asset";
        public const string RendererAssetPath = SettingsFolder + "/MopedSim_URP_Renderer.asset";
        public const string ArtModelsFolder = "Assets/Art/Models";
        private const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        [MenuItem("MopedSim/Rendering/Ensure URP Pipeline Assets")]
        public static void EnsureUrpPipelineAssetsMenu()
        {
            EnsureUrpPipelineAssets();
        }

        /// <summary>
        /// Returns true when the pipeline assignment changed (and models were reimported); false when URP was already active.
        /// </summary>
        public static bool EnsureUrpPipelineAssets()
        {
            UniversalRenderPipelineAsset asset = LoadOrCreatePipelineAsset();
            if (asset == null)
            {
                Debug.LogError("[UrpPipelineSetup] Could not load or create " + PipelineAssetPath + ".");
                return false;
            }

            bool changed = false;
            if (GraphicsSettings.defaultRenderPipeline != asset)
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                changed = true;
            }

            int levels = QualitySettings.names.Length;
            changed |= AssignQualityLevelPipelines(asset);

            if (!changed)
            {
                return false;
            }

            MarkProjectSettingsDirty("ProjectSettings/GraphicsSettings.asset");
            MarkProjectSettingsDirty("ProjectSettings/QualitySettings.asset");
            AssetDatabase.SaveAssets();

            // glTFast picks material shaders from the active pipeline at import time: regenerate every model's materials.
            if (AssetDatabase.IsValidFolder(ArtModelsFolder))
            {
                AssetDatabase.ImportAsset(ArtModelsFolder,
                    ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            Debug.Log("[UrpPipelineSetup] Assigned " + PipelineAssetPath + " to GraphicsSettings and " + levels
                + " quality levels; reimported " + ArtModelsFolder + ".");
            return true;
        }

        private static UniversalRenderPipelineAsset LoadOrCreatePipelineAsset()
        {
            UniversalRenderPipelineAsset existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
            }

            UniversalRenderPipelineAsset asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, PipelineAssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
        }

        /// <summary>
        /// Sets <c>customRenderPipeline</c> on every QualitySettings level through the serialized settings asset (there is no public
        /// per-level setter, and switching the active level to use <see cref="QualitySettings.renderPipeline"/> would churn
        /// m_CurrentQuality). Returns true when any level changed.
        /// </summary>
        private static bool AssignQualityLevelPipelines(RenderPipelineAsset asset)
        {
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (objects == null || objects.Length == 0 || objects[0] == null)
            {
                Debug.LogError("[UrpPipelineSetup] ProjectSettings/QualitySettings.asset could not be loaded.");
                return false;
            }

            SerializedObject settings = new SerializedObject(objects[0]);
            SerializedProperty levels = settings.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray)
            {
                Debug.LogError("[UrpPipelineSetup] QualitySettings.asset has no m_QualitySettings array.");
                return false;
            }

            bool changed = false;
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty pipeline = levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline");
                if (pipeline != null && pipeline.objectReferenceValue != asset)
                {
                    pipeline.objectReferenceValue = asset;
                    changed = true;
                }
            }

            if (changed)
            {
                settings.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static void MarkProjectSettingsDirty(string projectSettingsPath)
        {
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath(projectSettingsPath);
            if (objects != null && objects.Length > 0 && objects[0] != null)
            {
                EditorUtility.SetDirty(objects[0]);
            }
        }
    }
}
