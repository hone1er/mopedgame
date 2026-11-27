using System;
using System.Collections.Generic;
using System.IO;
using MopedSim.Data;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MopedSim.Editor
{
    /// <summary>
    /// Headless CLI build entrypoints for Gate 5 verification (AGENTS.md section 4).
    /// </summary>
    public static class CliBuild
    {
        private const string MacOSOutputPath = "Builds/MacOS/arm64/MopedGame.app";
        private const string MacOSReleaseOutputPath = "Builds/MacOS/arm64-release/MopedGame.app";

        /// <summary>
        /// Folder under Assets/StreamingAssets that players load catalogs from (AppCompositionRoot.ResolveCatalogDirectory).
        /// Generated before each build from the repo's data/catalogs and git-ignored; data/catalogs stays the source of truth.
        /// </summary>
        public const string StagedCatalogFolder = "Catalogs";

        /// <summary>
        /// Folder under Assets/StreamingAssets mirroring Assets/Art for runtime glTFast loads (BikeVisualAssembler.ResolveArtAssetPath
        /// reads StreamingAssets/Art in players). Only bike part models (Models/Parts, the catalog meshAssetPath root) are staged;
        /// generated before each build and git-ignored.
        /// </summary>
        public const string StagedArtFolder = "Art";
        public const string PartModelsRelativeFolder = "Models/Parts";

        [MenuItem("MopedSim/Build/macOS arm64 (Dev)")]
        public static void BuildMacOSDev()
        {
            BuildMacOSArm64("Development", MacOSOutputPath, BuildOptions.Development);
        }

        /// <summary>
        /// Non-development (release) macOS arm64 player for profiling and release-candidate smoke runs.
        /// Same scenes and StreamingAssets staging as BuildMacOSDev; no profiler/debug connection.
        /// </summary>
        [MenuItem("MopedSim/Build/macOS arm64 (Release)")]
        public static void BuildMacOSRelease()
        {
            BuildMacOSArm64("Release", MacOSReleaseOutputPath, BuildOptions.None);
        }

        private static void BuildMacOSArm64(string label, string outputPath, BuildOptions buildOptions)
        {
            Debug.Log("[CliBuild] Starting macOS arm64 " + label + " build...");

            // Ensure modular additive scenes exist before collecting build scene list (without rewriting committed scenes).
            SceneScaffoldBuilder.EnsureScenesAndBuildSettings();

            // Players cannot reach the repo's data/catalogs, so stage the JSON catalogs into StreamingAssets first.
            int stagedCatalogs;
            try
            {
                stagedCatalogs = StageCatalogsIntoStreamingAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("[CliBuild] Catalog staging failed: " + ex.Message);
                ExitIfBatchMode(1);
                return;
            }

            Debug.Log("[CliBuild] Staged " + stagedCatalogs + " catalog JSON files into StreamingAssets/" + StagedCatalogFolder + ".");

            // The assembled moped is loaded at runtime with glTFast from StreamingAssets/Art (no fallback cubes in the player).
            int stagedModels;
            try
            {
                stagedModels = StagePartModelsIntoStreamingAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("[CliBuild] Part model staging failed: " + ex.Message);
                ExitIfBatchMode(1);
                return;
            }

            Debug.Log("[CliBuild] Staged " + stagedModels + " part .glb files into StreamingAssets/" + StagedArtFolder + "/" + PartModelsRelativeFolder + ".");

            ConfigureMacOSArm64Architecture();

            string[] scenes = CollectEnabledBuildScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CliBuild] No enabled scenes found in EditorBuildSettings.");
                ExitIfBatchMode(1);
                return;
            }

            string outputFullPath = Path.GetFullPath(outputPath);
            string outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildPlayerOptions options = new BuildPlayerOptions();
            options.scenes = scenes;
            options.locationPathName = outputPath;
            options.target = BuildTarget.StandaloneOSX;
            options.targetGroup = BuildTargetGroup.Standalone;
            options.options = buildOptions;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[CliBuild] macOS arm64 " + label + " build succeeded (" + summary.totalSize + " bytes) at: " + outputPath);
                ExitIfBatchMode(0);
                return;
            }

            Debug.LogError("[CliBuild] macOS arm64 " + label + " build failed with result: " + summary.result + " (errors: " + summary.totalErrors + ")");
            ExitIfBatchMode(1);
        }

        /// <summary>
        /// Repo-level data/catalogs directory (sibling of the Unity project folder).
        /// </summary>
        public static string SourceCatalogDirectory
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs")); }
        }

        /// <summary>
        /// Absolute path of Assets/StreamingAssets/Catalogs.
        /// </summary>
        public static string StagedCatalogDirectory
        {
            get { return Path.Combine(Application.streamingAssetsPath, StagedCatalogFolder); }
        }

        /// <summary>
        /// Copies data/catalogs/*.json into Assets/StreamingAssets/Catalogs (replacing any stale staged JSON) and refreshes
        /// the AssetDatabase so the player build includes them. Returns the number of files staged; throws if none exist.
        /// </summary>
        [MenuItem("MopedSim/Build/Stage Catalogs Into StreamingAssets")]
        public static void StageCatalogsMenu()
        {
            Debug.Log("[CliBuild] Staged " + StageCatalogsIntoStreamingAssets() + " catalog JSON files into StreamingAssets/" + StagedCatalogFolder + ".");
        }

        public static int StageCatalogsIntoStreamingAssets()
        {
            string source = SourceCatalogDirectory;
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("Catalog source directory not found: " + source);
            }

            string[] catalogFiles = Directory.GetFiles(source, "*.json", SearchOption.TopDirectoryOnly);
            if (catalogFiles.Length == 0)
            {
                throw new FileNotFoundException("No catalog *.json files found in " + source);
            }

            Array.Sort(catalogFiles, StringComparer.Ordinal);
            string destination = StagedCatalogDirectory;
            Directory.CreateDirectory(destination);

            string[] stale = Directory.GetFiles(destination, "*.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < stale.Length; i++)
            {
                File.Delete(stale[i]);
                string staleMeta = stale[i] + ".meta";
                if (File.Exists(staleMeta))
                {
                    File.Delete(staleMeta);
                }
            }

            for (int i = 0; i < catalogFiles.Length; i++)
            {
                string target = Path.Combine(destination, Path.GetFileName(catalogFiles[i]));
                File.Copy(catalogFiles[i], target, true);
            }

            // Fail the build early if the staged copy would not boot (same load + validation AppCompositionRoot runs).
            CatalogValidationReport report = CatalogLoader.Validate(CatalogLoader.LoadFromDirectory(destination));
            if (!report.IsValid)
            {
                throw new InvalidOperationException(
                    "Staged catalogs failed validation (" + report.Errors.Count + " errors); first: " + report.Errors[0]);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return catalogFiles.Length;
        }

        /// <summary>
        /// Mirrors Assets/Art/Models/Parts/**/*.glb into Assets/StreamingAssets/Art/Models/Parts (stale staged models removed first).
        /// Returns the number of models staged; throws when the source folder holds none.
        /// </summary>
        public static int StagePartModelsIntoStreamingAssets()
        {
            string source = Path.Combine(Application.dataPath, "Art", PartModelsRelativeFolder);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("Part model source directory not found: " + source);
            }

            string[] models = Directory.GetFiles(source, "*.glb", SearchOption.AllDirectories);
            if (models.Length == 0)
            {
                throw new FileNotFoundException("No part .glb files found under " + source);
            }

            Array.Sort(models, StringComparer.Ordinal);
            string destination = Path.Combine(Application.streamingAssetsPath, StagedArtFolder, PartModelsRelativeFolder);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            for (int i = 0; i < models.Length; i++)
            {
                string relative = models[i].Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(models[i], target, true);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return models.Length;
        }

        private static void ConfigureMacOSArm64Architecture()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            EditorUserBuildSettings.SetPlatformSettings("OSXUniversal", "Architecture", "arm64");

            // Also configure UnityEditor.OSXStandalone.UserBuildSettings.architecture via reflection
            // so this assembly compiles cleanly even when OSX build extensions are inspected generically.
            Type osxUserBuildSettings = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            Type osArchType = Type.GetType("UnityEditor.Build.OSArchitecture, UnityEditor.CoreModule");
            if (osxUserBuildSettings != null && osArchType != null)
            {
                var archProp = osxUserBuildSettings.GetProperty("architecture");
                if (archProp != null && archProp.CanWrite)
                {
                    object arm64Val = Enum.Parse(osArchType, "ARM64");
                    archProp.SetValue(null, arm64Val);
                }
            }
        }

        private static string[] CollectEnabledBuildScenes()
        {
            List<string> enabled = new List<string>(8);
            EditorBuildSettingsScene[] configured = EditorBuildSettings.scenes;
            for (int i = 0; i < configured.Length; i++)
            {
                if (configured[i].enabled && !string.IsNullOrEmpty(configured[i].path))
                {
                    enabled.Add(configured[i].path);
                }
            }

            return enabled.ToArray();
        }

        private static void ExitIfBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
