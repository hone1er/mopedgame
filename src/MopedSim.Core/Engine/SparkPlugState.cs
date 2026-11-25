namespace MopedSim.Core
{
    /// <summary>
    /// Tracks spark plug electrode gap, fouling buildup, and ceramic insulator chop color (ChalkWhite -> ChocolateBrown -> WetBlack).
    /// </summary>
    public sealed class SparkPlugState
    {
        public float ElectrodeGapMm { get; set; } = 0.60f;
        public float FoulingLevel { get; set; }
        public float InsulatorColorIndex { get; set; } = 0.50f;
        public int HeatRange { get; set; } = 6;
        public bool IsInstalled { get; set; } = true;

        public PlugColorZone ColorZone
        {
            get
            {
                if (InsulatorColorIndex < 0.22f)
                {
                    return PlugColorZone.ChalkWhite;
                }
                if (InsulatorColorIndex < 0.38f)
                {
                    return PlugColorZone.TanLean;
                }
                if (InsulatorColorIndex <= 0.62f)
                {
                    return PlugColorZone.ChocolateBrown;
                }
                if (InsulatorColorIndex <= 0.78f)
                {
                    return PlugColorZone.DarkRich;
                }
                return PlugColorZone.WetBlack;
            }
        }

        /// <summary>
        /// Updates ceramic insulator color and soot fouling while installed and running; freezes cleanly when removed.
        /// </summary>
        public void Step(
            float dt,
            float rpm,
            float throttle,
            float effectiveAfr,
            float premixRatio,
            float chtCelsius,
            bool isRunning)
        {
            if (dt <= 0f || !IsInstalled || !isRunning || rpm < 900f)
            {
                return;
            }

            // Target color index: 0.0 ChalkWhite (lean) .. 0.5 ChocolateBrown (12.8 AFR) .. 1.0 WetBlack (rich)
            float targetColor;
            if (effectiveAfr <= 12.8f)
            {
                float t = SimMath.Clamp01((12.8f - effectiveAfr) / 2.6f);
                targetColor = SimMath.Lerp(0.50f, 0.96f, t);
            }
            else
            {
                float t = SimMath.Clamp01((effectiveAfr - 12.8f) / 3.2f);
                targetColor = SimMath.Lerp(0.50f, 0.08f, t);
            }

            // Heat range weighting (B6HS hotter -> slightly whiter; B8HS colder -> slightly darker on low load)
            float heatOffset = (HeatRange - 7) * 0.02f;
            targetColor = SimMath.Clamp01(targetColor + heatOffset);

            // Plug chop primarily integrates high-throttle (>80% TPS) AFR cleanly
            float throttleRate = throttle >= 0.80f ? 0.025f : (throttle >= 0.35f ? 0.008f : 0.002f);
            InsulatorColorIndex = SimMath.MoveTowards(InsulatorColorIndex, targetColor, throttleRate * dt);

            // Fouling builds when running rich (<11.2 AFR), heavy oil (>30:1), or prolonged cold idling
            float foulRate = 0f;
            if (effectiveAfr < 11.2f)
            {
                foulRate += (11.2f - effectiveAfr) * 0.022f;
            }
            if (premixRatio > 0.033f)
            {
                foulRate += (premixRatio - 0.033f) * 1.2f;
            }
            if (chtCelsius < 65f && throttle < 0.25f)
            {
                foulRate += 0.008f;
            }

            if (foulRate > 0f)
            {
                FoulingLevel = SimMath.Clamp01(FoulingLevel + foulRate * dt);
            }
            else if (chtCelsius > 130f && effectiveAfr >= 12.0f && effectiveAfr <= 13.8f && throttle > 0.65f)
            {
                // Self-cleaning burn at optimal operating temperature
                FoulingLevel = SimMath.Clamp01(FoulingLevel - 0.006f * dt);
            }
        }

        /// <summary>
        /// Returns an immutable diagnostic reading of the spark plug.
        /// </summary>
        public SparkPlugReading Inspect()
        {
            PlugColorZone zone = ColorZone;
            string summary;
            switch (zone)
            {
                case PlugColorZone.ChalkWhite:
                    summary = "Dangerously lean (ChalkWhite) — up-jet main jet or check for air leaks.";
                    break;
                case PlugColorZone.TanLean:
                    summary = "Slightly lean (TanLean) — consider +2 main jet size for hills.";
                    break;
                case PlugColorZone.ChocolateBrown:
                    summary = "Dialed carburation (ChocolateBrown) — optimal 2-stroke plug chop.";
                    break;
                case PlugColorZone.DarkRich:
                    summary = "Slightly rich (DarkRich) — safe break-in or step down 1-2 jet sizes.";
                    break;
                default:
                    summary = "Rich / oil-fouled (WetBlack) — down-jet carb or check premix/choke.";
                    break;
            }

            if (FoulingLevel >= 0.85f)
            {
                summary += " Electrode fouled — clean with wire brush or replace plug.";
            }

            return new SparkPlugReading(
                ElectrodeGapMm,
                FoulingLevel,
                InsulatorColorIndex,
                zone,
                HeatRange,
                IsInstalled,
                summary);
        }

        /// <summary>
        /// Cleans soot fouling and resets electrode gap during roadside or workbench service.
        /// </summary>
        public void CleanPlug(float gapMm = 0.60f)
        {
            FoulingLevel = 0f;
            ElectrodeGapMm = SimMath.Clamp(gapMm, 0.40f, 1.00f);
        }

        /// <summary>
        /// Installs a fresh spark plug.
        /// </summary>
        public void ReplacePlug(int heatRange = 6, float gapMm = 0.60f)
        {
            HeatRange = heatRange;
            ElectrodeGapMm = SimMath.Clamp(gapMm, 0.40f, 1.00f);
            FoulingLevel = 0f;
            InsulatorColorIndex = 0.50f;
            IsInstalled = true;
        }
    }
}
