namespace MopedSim.Core
{
    /// <summary>
    /// Simulates Cylinder Head Temperature (CHT), crankcase heat-soak, premix oil starvation, soft seize, and hard seize.
    /// </summary>
    public sealed class ThermalModel
    {
        public const float OilStarvationMinRatio = 0.008f;
        public const float SoftSeizeTempC = 230f;
        public const float HardSeizeTempC = 255f;
        public const float SoftSeizeClearTempC = 140f;

        public float ChtCelsius { get; set; } = 25f;
        public float CrankcaseTempCelsius { get; set; } = 25f;
        public float CompressionHealth { get; set; } = 1.0f;
        public float DetonationRisk { get; private set; }
        public float OilStarvationTimerSeconds { get; private set; }
        public float RingWearAccumulated { get; private set; }
        public bool IsSoftSeized { get; set; }
        public bool IsHardSeized { get; set; }

        public bool IsSeized
        {
            get { return IsSoftSeized || IsHardSeized; }
        }

        public ThermalZone CurrentZone
        {
            get
            {
                if (IsHardSeized || ChtCelsius >= HardSeizeTempC)
                {
                    return ThermalZone.HardSeize;
                }
                if (IsSoftSeized || ChtCelsius >= SoftSeizeTempC)
                {
                    return ThermalZone.SoftSeize;
                }
                if (ChtCelsius >= 206f)
                {
                    return ThermalZone.PingWarning;
                }
                if (ChtCelsius >= 186f)
                {
                    return ThermalZone.RunningHot;
                }
                if (ChtCelsius >= 120f)
                {
                    return ThermalZone.Optimal;
                }
                if (ChtCelsius >= 70f)
                {
                    return ThermalZone.Warming;
                }
                return ThermalZone.Cold;
            }
        }

        /// <summary>
        /// Converts fuel:oil ratio (e.g., 50 for 50:1, 0 for 0:1 straight gas) into oil fraction.
        /// </summary>
        public static float PremixFromRatio(float fuelPartsPerOneOil)
        {
            if (fuelPartsPerOneOil <= 0f)
            {
                return 0f;
            }
            return 1.0f / fuelPartsPerOneOil;
        }

        /// <summary>
        /// Steps CHT, crankcase temperature, oil starvation friction, and seizure state transitions.
        /// </summary>
        public void Step(
            float dt,
            float ambientTempC,
            float rpm,
            float throttle,
            float effectiveAfr,
            float premixRatio,
            float vehicleSpeedMps,
            float roadSlopeRadians,
            float displacementCc,
            float finCoolingEfficiency,
            bool isRunning)
        {
            if (dt <= 0f)
            {
                return;
            }

            float airflowCooling = SimMath.Max(0.4f, finCoolingEfficiency) * (1f + SimMath.Max(0f, vehicleSpeedMps) * 0.025f);

            if (!isRunning || IsSeized || rpm < 500f)
            {
                // Curb cool-down toward ambient
                float coolTau = 38f / SimMath.Max(0.6f, finCoolingEfficiency);
                float coolAlpha = 1f - SimMath.Exp(-dt / coolTau);
                ChtCelsius = SimMath.Lerp(ChtCelsius, ambientTempC, coolAlpha);
                CrankcaseTempCelsius = SimMath.Lerp(CrankcaseTempCelsius, ambientTempC + (ChtCelsius - ambientTempC) * 0.42f, 1f - SimMath.Exp(-dt / 45f));
                DetonationRisk = SimMath.MoveTowards(DetonationRisk, 0f, 0.8f * dt);
                return;
            }

            // Thermal equilibrium calculation
            float loadFactor = (0.28f + 0.72f * throttle) * SimMath.Clamp(0.65f + (rpm / 7500f) * 0.45f, 0.55f, 1.35f);
            float dispBoost = 1f + SimMath.Clamp((displacementCc - 49f) / 40f, 0f, 0.6f) * 0.14f;
            float gradeHeat = SimMath.Clamp(roadSlopeRadians * 180f, 0f, 45f);

            // Base heat keeps dialed bikes safely in 120-185C Optimal range
            float baseTarget = ambientTempC + (122f * loadFactor * dispBoost + gradeHeat) / (0.85f + (airflowCooling - 1f) * 0.35f);

            // Lean mixture dramatically spikes combustion heat
            float leanHeatBoost = 0f;
            if (effectiveAfr > 13.5f)
            {
                leanHeatBoost = (effectiveAfr - 13.5f) * 26.5f * (0.45f + 0.55f * throttle);
            }
            else if (effectiveAfr < 11.8f)
            {
                leanHeatBoost = -(11.8f - effectiveAfr) * 5.0f;
            }

            // Premix oil starvation (e.g., 0:1 straight gas) creates severe piston-wall friction heat
            bool isOilStarved = premixRatio < OilStarvationMinRatio;
            float starvationHeatBoost = 0f;
            if (isOilStarved && rpm > 900f)
            {
                starvationHeatBoost = 105f * SimMath.Clamp(rpm / 6000f, 0.4f, 1.4f);
                OilStarvationTimerSeconds += dt * (0.65f + 0.75f * SimMath.Clamp01(rpm / 7000f));
            }
            else
            {
                OilStarvationTimerSeconds = SimMath.Max(0f, OilStarvationTimerSeconds - dt * 0.25f);
            }

            float targetCht = baseTarget + leanHeatBoost + starvationHeatBoost;
            float tau = targetCht > ChtCelsius ? 25f : 34f;
            float alpha = 1f - SimMath.Exp(-dt / tau);
            ChtCelsius = SimMath.Lerp(ChtCelsius, targetCht, alpha);

            // Crankcase temp trails CHT (~0.56 * CHT + 0.44 * ambient, rising faster under stop-and-go low airflow)
            float slowTrafficHeatSoak = vehicleSpeedMps < 3.5f ? 16f : 0f;
            float targetCase = ambientTempC + (ChtCelsius - ambientTempC) * 0.56f + slowTrafficHeatSoak;
            CrankcaseTempCelsius = SimMath.Lerp(CrankcaseTempCelsius, targetCase, 1f - SimMath.Exp(-dt / 42f));

            // Detonation risk & accelerated ring wear in PingWarning (>206C) or lean (>14.5 AFR)
            bool inPingConditions = ChtCelsius > 206f || effectiveAfr > 14.5f;
            if (inPingConditions)
            {
                float targetRisk = SimMath.Clamp01(
                    SimMath.Max((ChtCelsius - 200f) / 35f, (effectiveAfr - 14.3f) / 3.2f));
                DetonationRisk = SimMath.MoveTowards(DetonationRisk, SimMath.Max(0.35f, targetRisk), 0.35f * dt);
                RingWearAccumulated += dt * 0.00025f * 5.0f;
            }
            else
            {
                DetonationRisk = SimMath.MoveTowards(DetonationRisk, 0f, 0.25f * dt);
                RingWearAccumulated += dt * 0.00025f;
            }

            // Hard seize check: CHT >= 255C or sustained oil starvation
            if (ChtCelsius >= HardSeizeTempC || OilStarvationTimerSeconds >= 14.0f)
            {
                IsHardSeized = true;
                IsSoftSeized = false;
                CompressionHealth = SimMath.Min(CompressionHealth, 0.25f);
                return;
            }

            // Soft seize check: CHT >= 230C (4-corner heat seize)
            if (ChtCelsius >= SoftSeizeTempC)
            {
                IsSoftSeized = true;
                CompressionHealth = SimMath.Max(0.55f, CompressionHealth - 0.08f);
            }
        }

        /// <summary>
        /// Advances passive curb cool-down for a given number of seconds.
        /// </summary>
        public void CoolDown(float seconds, float ambientTempC = 25f)
        {
            if (seconds <= 0f)
            {
                return;
            }
            float chtAlpha = 1f - SimMath.Exp(-seconds / 38f);
            ChtCelsius = SimMath.Lerp(ChtCelsius, ambientTempC, chtAlpha);
            float caseAlpha = 1f - SimMath.Exp(-seconds / 45f);
            CrankcaseTempCelsius = SimMath.Lerp(CrankcaseTempCelsius, ambientTempC + (ChtCelsius - ambientTempC) * 0.42f, caseAlpha);
            DetonationRisk = 0f;

            if (IsSoftSeized && !IsHardSeized && ChtCelsius <= SoftSeizeClearTempC)
            {
                IsSoftSeized = false;
            }
        }

        /// <summary>
        /// Attempts to clear a soft seize once CHT has dropped below the cool-down threshold.
        /// </summary>
        public bool TryClearSoftSeize()
        {
            if (IsHardSeized)
            {
                return false;
            }
            if (!IsSoftSeized)
            {
                return true;
            }
            if (ChtCelsius <= SoftSeizeClearTempC)
            {
                IsSoftSeized = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Restores top-end compression and clears seizure flags after a Garage cylinder/piston rebuild.
        /// </summary>
        public void RebuildTopEnd()
        {
            IsHardSeized = false;
            IsSoftSeized = false;
            OilStarvationTimerSeconds = 0f;
            CompressionHealth = 1.0f;
            DetonationRisk = 0f;
        }
    }
}
