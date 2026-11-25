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

        /// <summary>
        /// Folder under Assets/StreamingAssets that players load catalogs from (AppCompositionRoot.ResolveCatalogDirectory).
        /// Generated before each build from the repo's data/catalogs and git-ignored; data/catalogs stays the source of truth.
        /// </summary>
        public const string StagedCatalogFolder = "Catalogs";

        [MenuItem("MopedSim/Build/macOS arm64 (Dev)")]
        public static void BuildMacOSDev()
        {
            Debug.Log("[CliBuild] Starting macOS arm64 Development build...");

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

            ConfigureMacOSArm64Architecture();

            string[] scenes = CollectEnabledBuildScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CliBuild] No enabled scenes found in EditorBuildSettings.");
                ExitIfBatchMode(1);
                return;
            }

            string outputFullPath = Path.GetFullPath(MacOSOutputPath);
            string outputDirectory = Path.GetDirectoryName(outputFullPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            BuildPlayerOptions options = new BuildPlayerOptions();
            options.scenes = scenes;
            options.locationPathName = MacOSOutputPath;
            options.target = BuildTarget.StandaloneOSX;
            options.targetGroup = BuildTargetGroup.Standalone;
            options.options = BuildOptions.Development;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[CliBuild] macOS arm64 Development build succeeded (" + summary.totalSize + " bytes) at: " + MacOSOutputPath);
                ExitIfBatchMode(0);
                return;
            }

            Debug.LogError("[CliBuild] macOS arm64 build failed with result: " + summary.result + " (errors: " + summary.totalErrors + ")");
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
