using UnityEngine;

namespace MopedSim.UI
{
    /// <summary>One row of the presentation-layer graphics preset table.</summary>
    public readonly struct GraphicsPresetEntry
    {
        public readonly GraphicsPreset Preset;
        /// <summary>Name of the Unity quality level (ProjectSettings/QualitySettings) this preset selects.</summary>
        public readonly string QualityLevelName;
        public readonly int TargetFrameRate;
        public readonly float FogQualityScale;

        public GraphicsPresetEntry(GraphicsPreset preset, string qualityLevelName, int targetFrameRate, float fogQualityScale)
        {
            Preset = preset;
            QualityLevelName = qualityLevelName;
            TargetFrameRate = targetFrameRate;
            FogQualityScale = fogQualityScale;
        }
    }

    /// <summary>
    /// Maps <see cref="GraphicsPreset"/> to concrete presentation values (quality level, frame cap, fog quality) and applies
    /// them. Presentation only; no simulation values live here.
    /// </summary>
    public static class GraphicsPresetTable
    {
        private static readonly GraphicsPresetEntry[] Entries =
        {
            new GraphicsPresetEntry(GraphicsPreset.Low, "Low", 30, 0.5f),
            new GraphicsPresetEntry(GraphicsPreset.Medium, "Medium", 60, 1f),
            new GraphicsPresetEntry(GraphicsPreset.High, "High", 60, 1.25f),
            new GraphicsPresetEntry(GraphicsPreset.Ultra, "Ultra", 120, 1.5f),
        };

        public static int Count
        {
            get { return Entries.Length; }
        }

        public static GraphicsPresetEntry GetByIndex(int index)
        {
            return Entries[index];
        }

        /// <summary>Entry for <paramref name="preset"/>; falls back to Medium for undefined values.</summary>
        public static GraphicsPresetEntry Get(GraphicsPreset preset)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Preset == preset)
                {
                    return Entries[i];
                }
            }

            return Entries[1];
        }

        /// <summary>Next preset in cycle order Low, Medium, High, Ultra, Low.</summary>
        public static GraphicsPreset Next(GraphicsPreset preset)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Preset == preset)
                {
                    return Entries[(i + 1) % Entries.Length].Preset;
                }
            }

            return Entries[1].Preset;
        }

        /// <summary>Index of the quality level with this name (case-insensitive), or -1.</summary>
        public static int FindQualityLevel(string name)
        {
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Selects the preset's quality level (when present) and applies the frame cap. Returns the applied level index,
        /// or -1 when no level matched (frame rate is still applied).
        /// </summary>
        /// <summary>
        /// Editor-only opt-in for tests that assert quality-level switching and restore the original state afterwards.
        /// Without it, Apply in the Editor only sets the frame cap so ProjectSettings are never rewritten.
        /// </summary>
        public static bool AllowEditorQualityChanges { get; set; }

        public static int Apply(GraphicsQualitySettings graphics)
        {
            int level = FindQualityLevel(Get(graphics.Preset).QualityLevelName);
#if UNITY_EDITOR
            // Quality level / vSync changes made in the Editor persist into ProjectSettings/QualitySettings.asset.
            // Only the frame cap is applied there unless a test opts in (and restores state); players apply everything.
            if (!AllowEditorQualityChanges)
            {
                Application.targetFrameRate = graphics.TargetFrameRate;
                return level;
            }
#endif
            if (level >= 0 && QualitySettings.GetQualityLevel() != level)
            {
                QualitySettings.SetQualityLevel(level, true);
            }

            // Unity ignores targetFrameRate while vSync is on; set it after the level switch (levels carry their own vSync).
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = graphics.TargetFrameRate;

            return level;
        }
    }
}
