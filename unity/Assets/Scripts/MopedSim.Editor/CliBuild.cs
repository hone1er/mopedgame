using System;
using System.Collections.Generic;
using System.IO;
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

        [MenuItem("MopedSim/Build/macOS arm64 (Dev)")]
        public static void BuildMacOSDev()
        {
            Debug.Log("[CliBuild] Starting macOS arm64 Development build...");

            // Ensure modular additive scenes exist before collecting build scene list.
            SceneScaffoldBuilder.BuildAllScenes();

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
