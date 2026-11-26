#nullable enable

using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Paint &amp; aesthetic booth state of one bike: frame paint (#RRGGBB), weathered patina 0..1, and an optional decal/style tag.
    /// Persisted by MopedSim.Data as BikeSaveState paintHex / patinaLevel / decalStyle.
    /// </summary>
    public sealed class BikeCustomizationState
    {
        public const string DefaultPaintHex = "#C42828";
        public const int MaxDecalStyleLength = 64;

        public string PaintHex { get; private set; } = DefaultPaintHex;

        /// <summary>Weathered patina 0 (fresh paint) .. 1 (fully sun-baked).</summary>
        public float PatinaLevel { get; private set; }

        /// <summary>Optional decal / style tag (e.g. "checker_stripe"); null when none.</summary>
        public string? DecalStyle { get; private set; }

        public BikeCustomizationState()
        {
        }

        /// <summary>
        /// Throws <see cref="ArgumentException"/> for an invalid paint hex or decal; patina is clamped to 0..1.
        /// </summary>
        public BikeCustomizationState(string paintHex, float patinaLevel = 0f, string? decalStyle = null)
        {
            if (!TrySetPaintHex(paintHex))
            {
                throw new ArgumentException("Paint must be #RRGGBB, got '" + (paintHex ?? "<null>") + "'.", nameof(paintHex));
            }
            SetPatinaLevel(patinaLevel);
            if (!TrySetDecalStyle(decalStyle))
            {
                throw new ArgumentException("Decal style must be at most " + MaxDecalStyleLength + " characters.", nameof(decalStyle));
            }
        }

        public static bool IsValidPaintHex(string? hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#')
            {
                return false;
            }
            for (int i = 1; i < 7; i++)
            {
                char c = hex[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsValidDecalStyle(string? style)
        {
            return style == null || style.Length <= MaxDecalStyleLength;
        }

        /// <summary>Sets paint without touching patina. Returns false (unchanged) for an invalid hex.</summary>
        public bool TrySetPaintHex(string? hex)
        {
            if (!IsValidPaintHex(hex))
            {
                return false;
            }
            PaintHex = hex!;
            return true;
        }

        /// <summary>Fresh rattle-can respray: sets paint and strips patina back to 0. Returns false for an invalid hex.</summary>
        public bool TryRepaint(string? hex)
        {
            if (!TrySetPaintHex(hex))
            {
                return false;
            }
            PatinaLevel = 0f;
            return true;
        }

        /// <summary>Clamps to 0..1 (NaN becomes 0).</summary>
        public void SetPatinaLevel(float level)
        {
            PatinaLevel = float.IsNaN(level) ? 0f : SimMath.Clamp01(level);
        }

        /// <summary>Deterministic weathering: patina += ratePerSecond × dt, clamped to 0..1.</summary>
        public void Weather(float dt, float ratePerSecond)
        {
            if (dt > 0f && ratePerSecond > 0f)
            {
                SetPatinaLevel(PatinaLevel + ratePerSecond * dt);
            }
        }

        /// <summary>Null or empty clears the decal. Returns false (unchanged) when longer than <see cref="MaxDecalStyleLength"/>.</summary>
        public bool TrySetDecalStyle(string? style)
        {
            if (!IsValidDecalStyle(style))
            {
                return false;
            }
            DecalStyle = style == null || style.Length == 0 ? null : style;
            return true;
        }

        public BikeCustomizationState Clone()
        {
            var copy = new BikeCustomizationState();
            copy.PaintHex = PaintHex;
            copy.PatinaLevel = PatinaLevel;
            copy.DecalStyle = DecalStyle;
            return copy;
        }
    }
}
