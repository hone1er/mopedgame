using System;
using UnityEditor;
using UnityEngine;

namespace MopedSim.Editor
{
    /// <summary>
    /// Remaps imported glTFast / glTF PBR materials onto stylized URP Lit shaders
    /// while preserving base color, metallic, smoothness, and emissive properties.
    /// glTFast shader-graph materials expose glTF-named properties (<c>baseColorFactor</c>, <c>baseColorTexture</c>,
    /// <c>metallicFactor</c>, <c>roughnessFactor</c>, <c>emissiveFactor</c>, <c>emissiveTexture</c>); those are read first so the
    /// emissive World kit pieces (e.g. <c>sf_delivery_beacon.glb</c>) keep their glow. Materials imported as sub-assets of a
    /// <c>.glb</c> are read-only importer output (and already URP shader graphs), so the bulk menu skips them; remap a copy via
    /// <see cref="CreateUrpCopy"/> instead.
    /// </summary>
    public static class GlbMaterialRemapper
    {
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string UrpSimpleLitShaderName = "Universal Render Pipeline/Simple Lit";
        private const string ArtModelsAssetFolder = "Assets/Art/Models";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int RoughnessFactorId = Shader.PropertyToID("roughnessFactor");
        private static readonly int MetallicFactorId = Shader.PropertyToID("metallicFactor");
        private static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int GltfBaseColorTextureId = Shader.PropertyToID("baseColorTexture");
        private static readonly int GltfEmissiveFactorId = Shader.PropertyToID("emissiveFactor");
        private static readonly int GltfEmissiveTextureId = Shader.PropertyToID("emissiveTexture");

        [MenuItem("MopedSim/Assets/Remap GLB Materials to URP")]
        public static void RemapAllArtModelMaterials()
        {
            if (!AssetDatabase.IsValidFolder(ArtModelsAssetFolder))
            {
                Debug.LogWarning("[GlbMaterialRemapper] Folder not found: " + ArtModelsAssetFolder);
                return;
            }

            Shader urpLit = ResolveUrpShader();
            if (urpLit == null)
            {
                Debug.LogError("[GlbMaterialRemapper] URP Lit shader could not be resolved.");
                return;
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { ArtModelsAssetFolder });
            int updatedCount = 0;
            int skippedImported = 0;

            for (int i = 0; i < materialGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    continue;
                }

                if (IsImportedModelMaterial(mat))
                {
                    skippedImported++;
                    continue;
                }

                if (RemapMaterial(mat, urpLit))
                {
                    EditorUtility.SetDirty(mat);
                    updatedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[GlbMaterialRemapper] Remapped " + updatedCount + " material(s) to " + urpLit.name + "; skipped "
                + skippedImported + " read-only glTF importer material(s).");
        }

        /// <summary>
        /// Remaps all renderer materials on an instantiated hierarchy in-place onto URP Lit.
        /// </summary>
        public static int RemapHierarchyMaterials(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            Shader urpLit = ResolveUrpShader();
            if (urpLit == null)
            {
                return 0;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            int remapped = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = renderers[i].sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                {
                    if (mats[j] != null && RemapMaterial(mats[j], urpLit))
                    {
                        remapped++;
                    }
                }
            }

            return remapped;
        }

        /// <summary>
        /// True for materials that are sub-assets of an imported .glb / .gltf model (read-only importer output).
        /// </summary>
        public static bool IsImportedModelMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(material);
            return !string.IsNullOrEmpty(path)
                && (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns a new URP Lit material converted from <paramref name="source"/> (source untouched), or null when URP is missing.
        /// </summary>
        public static Material CreateUrpCopy(Material source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Shader urpLit = ResolveUrpShader();
            if (urpLit == null)
            {
                return null;
            }

            Material copy = new Material(source);
            copy.name = source.name + "_URP";
            RemapMaterial(copy, urpLit);
            return copy;
        }

        /// <summary>
        /// Reads the emissive color a glTF or Standard/URP material renders with (black when not emissive).
        /// </summary>
        public static Color ReadEmission(Material material)
        {
            if (material == null)
            {
                return Color.black;
            }

            if (material.HasProperty(GltfEmissiveFactorId))
            {
                return material.GetColor(GltfEmissiveFactorId);
            }

            if (material.HasProperty(EmissionColorId) && material.IsKeywordEnabled("_EMISSION"))
            {
                return material.GetColor(EmissionColorId);
            }

            return Color.black;
        }

        /// <summary>
        /// Converts a single glTF/Standard material to URP Lit if needed.
        /// </summary>
        public static bool RemapMaterial(Material material, Shader urpLitShader)
        {
            if (material == null || urpLitShader == null)
            {
                return false;
            }

            if (material.shader == urpLitShader)
            {
                return false;
            }

            Color baseColor = Color.white;
            if (material.HasProperty(BaseColorFactorId))
            {
                baseColor = material.GetColor(BaseColorFactorId);
            }
            else if (material.HasProperty(BaseColorId))
            {
                baseColor = material.GetColor(BaseColorId);
            }
            else if (material.HasProperty(ColorId))
            {
                baseColor = material.GetColor(ColorId);
            }

            Texture baseTex = null;
            if (material.HasProperty(GltfBaseColorTextureId))
            {
                baseTex = material.GetTexture(GltfBaseColorTextureId);
            }
            else if (material.HasProperty(BaseMapId))
            {
                baseTex = material.GetTexture(BaseMapId);
            }
            else if (material.HasProperty(MainTexId))
            {
                baseTex = material.GetTexture(MainTexId);
            }

            float metallic = 0f;
            if (material.HasProperty(MetallicFactorId))
            {
                metallic = material.GetFloat(MetallicFactorId);
            }
            else if (material.HasProperty(MetallicId))
            {
                metallic = material.GetFloat(MetallicId);
            }

            float smoothness = 0.5f;
            if (material.HasProperty(RoughnessFactorId))
            {
                smoothness = Mathf.Clamp01(1f - material.GetFloat(RoughnessFactorId));
            }
            else if (material.HasProperty(SmoothnessId))
            {
                smoothness = material.GetFloat(SmoothnessId);
            }
            else if (material.HasProperty(GlossinessId))
            {
                smoothness = material.GetFloat(GlossinessId);
            }

            Color emissionColor = ReadEmission(material);
            Texture emissionTex = null;
            if (material.HasProperty(GltfEmissiveTextureId))
            {
                emissionTex = material.GetTexture(GltfEmissiveTextureId);
            }
            else if (material.HasProperty(EmissionMapId))
            {
                emissionTex = material.GetTexture(EmissionMapId);
            }

            bool hadEmission = emissionColor.maxColorComponent > 0f || emissionTex != null;

            material.shader = urpLitShader;
            material.SetColor(BaseColorId, baseColor);
            if (baseTex != null)
            {
                material.SetTexture(BaseMapId, baseTex);
            }

            material.SetFloat(MetallicId, Mathf.Clamp01(metallic));
            material.SetFloat(SmoothnessId, Mathf.Clamp01(smoothness));

            if (hadEmission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, emissionColor);
                if (emissionTex != null)
                {
                    material.SetTexture(EmissionMapId, emissionTex);
                }

                material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            return true;
        }

        private static Shader ResolveUrpShader()
        {
            Shader shader = Shader.Find(UrpLitShaderName);
            if (shader != null)
            {
                return shader;
            }

            return Shader.Find(UrpSimpleLitShaderName);
        }
    }
}
