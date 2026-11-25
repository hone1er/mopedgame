#nullable enable

using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Simulates Single-Speed Centrifugal (Maxer E50), Variated Belt (Bramble PA50), and 2-Speed Auto (Targa A35) drivetrains, final drive, and pedals.
    /// </summary>
    public sealed class DrivetrainModel
    {
        // RPM span over which each clutch goes from first bite to full lock-up.
        private const float SingleSpeedEngageSpanRpm = 900f;
        private const float VariatorEngageSpanRpm = 700f;
        private const float TwoSpeedEngageSpanRpm = 750f;

        public DrivetrainType Type { get; set; } = DrivetrainType.SingleSpeedCentrifugal;
        public float PrimaryReduction { get; set; } = 3.2f;
        public float ClutchEngageRpm { get; set; } = 2400f;
        public float ClutchSlipHeatRate { get; set; } = 1.0f;
        public float ClutchHeat { get; private set; }

        /// <summary>
        /// RPM the engine pulls above full clutch lock-up at wide-open throttle while the clutch slips during launch (stall speed).
        /// </summary>
        public float ClutchStallRpmRise { get; set; } = 1100f;

        // Variator (Bramble PA50)
        public float RollerWeightGrams { get; set; } = 6.5f;
        public float ContraSpringRate { get; set; } = 1.0f;
        public float VariatorRatio { get; private set; } = 1.95f;
        public float BeltWear { get; set; }
        public float BeltOilContamination { get; set; }
        public bool IsBeltSnapped { get; set; }

        // 2-Speed Auto (Targa A35)
        public float GearRatio1 { get; set; } = 2.25f;
        public float GearRatio2 { get; set; } = 1.18f;
        public float ShiftRpmThreshold { get; set; } = 4800f;
        public float TransmissionOilLevel { get; set; } = 1.0f;
        public float ClutchCorkGlaze { get; set; }

        // Final drive (sprockets & chain)
        public int FrontSprocketTeeth { get; set; } = 13;
        public int RearSprocketTeeth { get; set; } = 45;
        public float ChainSlackMm { get; set; } = 20f;
        public bool IsChainThrown { get; set; }

        // Outputs
        public int CurrentGear { get; private set; }
        public float ClutchEngagement { get; private set; }
        public float EffectiveTotalRatio { get; private set; }
        public float RearWheelTorqueNm { get; private set; }
        public float EngineBrakeWheelTorqueNm { get; private set; }
        public float CoupledEngineRpm { get; private set; }
        public float EngineLoadTorqueNm { get; private set; }
        public float PedalWheelTorqueNm { get; private set; }
        public float ClutchStallRpm { get; private set; }

        public bool IsDriveDisconnected
        {
            get
            {
                if (IsChainThrown)
                {
                    return true;
                }
                if (Type == DrivetrainType.VariatedBelt && IsBeltSnapped)
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Steps transmission, clutch/variator/2-speed shifting, chain/belt integrity, and pedal propulsion.
        /// </summary>
        public void Step(
            float dt,
            float engineRpm,
            float combustionTorqueNm,
            float engineBrakeTorqueNm,
            float throttle,
            float wheelSpeedMps,
            float wheelRadiusM,
            float roadSlopeRadians,
            float surfaceRoughness,
            bool pedalAssist,
            ref float riderStamina,
            ISimRandom rng)
        {
            float safeRadius = SimMath.Max(0.15f, wheelRadiusM);
            float wheelRpm = (wheelSpeedMps / (2f * 3.14159265f * safeRadius)) * 60f;
            float sprocketRatio = (float)SimMath.Max(20, RearSprocketTeeth) / SimMath.Max(8, FrontSprocketTeeth);

            // Chain tension efficiency & thrown chain hazard
            float chainEfficiency = 0.94f;
            if (ChainSlackMm < 8f)
            {
                chainEfficiency -= (8f - ChainSlackMm) * 0.025f;
            }

            if (dt > 0f && !IsChainThrown && ChainSlackMm > 36f && surfaceRoughness > 0.45f)
            {
                double throwChance = (ChainSlackMm - 36f) * surfaceRoughness * 0.015 * dt;
                if (rng.NextDouble() < throwChance)
                {
                    IsChainThrown = true;
                }
            }

            // Pedal assist torque (human leg power ~130-180W scaled by stamina, minimum limp-home pedal always available)
            if (pedalAssist)
            {
                float staminaFactor = 0.45f + 0.55f * SimMath.Clamp01(riderStamina);
                float speedCadenceDrop = SimMath.Clamp01(1f - wheelSpeedMps / 11f);
                PedalWheelTorqueNm = (18f + 28f * staminaFactor) * (0.45f + 0.55f * speedCadenceDrop);
                if (dt > 0f)
                {
                    riderStamina = SimMath.Clamp01(riderStamina - 0.025f * dt);
                }
            }
            else
            {
                PedalWheelTorqueNm = 0f;
                if (dt > 0f)
                {
                    riderStamina = SimMath.Clamp01(riderStamina + 0.018f * dt);
                }
            }

            // If final drive is severed (thrown chain or snapped belt), zero engine power reaches the wheel.
            if (IsDriveDisconnected)
            {
                CurrentGear = 0;
                ClutchEngagement = 0f;
                RearWheelTorqueNm = PedalWheelTorqueNm;
                EngineBrakeWheelTorqueNm = 0f;
                CoupledEngineRpm = 0f;
                EngineLoadTorqueNm = 0f;
                return;
            }

            float transmissionRatio;
            float driveEfficiency = chainEfficiency;

            if (Type == DrivetrainType.SingleSpeedCentrifugal)
            {
                // Maxer E50: single wet centrifugal clutch
                float engageThreshold = SimMath.Max(1600f, ClutchEngageRpm);
                float rawEngage = SimMath.Clamp01((engineRpm - engageThreshold) / SingleSpeedEngageSpanRpm);
                float heatSlipLoss = SimMath.Clamp01(ClutchHeat * 0.25f);
                ClutchEngagement = throttle < 0.04f && engineRpm < engageThreshold + 150f
                    ? 0f
                    : rawEngage * (1f - heatSlipLoss);

                CurrentGear = ClutchEngagement > 0.08f ? 1 : 0;
                float primary = PrimaryReduction * 1.25f;
                transmissionRatio = primary;
                EffectiveTotalRatio = transmissionRatio * sprocketRatio;
                driveEfficiency *= 0.88f;

                float wheelCoupled = wheelRpm * EffectiveTotalRatio;
                if (dt > 0f && ClutchEngagement > 0.05f && engineRpm > wheelCoupled + 400f)
                {
                    float slipRatio = SimMath.Clamp01((engineRpm - wheelCoupled) / 3500f);
                    ClutchHeat = SimMath.Clamp01(ClutchHeat + slipRatio * ClutchSlipHeatRate * 0.08f * dt);
                }
                else if (dt > 0f)
                {
                    ClutchHeat = SimMath.Clamp01(ClutchHeat - 0.05f * dt);
                }

                // Centrifugal clutch lets engine rev up to engagement stall speed when launching from 0 mph
                CoupledEngineRpm = ComputeCoupledEngineRpm(wheelCoupled, engageThreshold, SingleSpeedEngageSpanRpm, throttle);
            }
            else if (Type == DrivetrainType.VariatedBelt)
            {
                // Bramble PA50: dual-pulley variated belt drive
                float engageThreshold = SimMath.Max(1800f, ClutchEngageRpm);
                ClutchEngagement = SimMath.Clamp01((engineRpm - engageThreshold) / VariatorEngageSpanRpm);

                // Belt wear & oil contamination
                if (dt > 0f && engineRpm > 2000f && ClutchEngagement > 0.1f)
                {
                    BeltWear = SimMath.Clamp01(BeltWear + (0.000018f + 0.000025f * throttle) * dt);
                    if (BeltWear >= 1.0f)
                    {
                        IsBeltSnapped = true;
                    }
                }

                float beltGrip = 1f - SimMath.Clamp01(BeltOilContamination * 0.55f + BeltWear * 0.22f);
                ClutchEngagement *= beltGrip;
                CurrentGear = ClutchEngagement > 0.08f ? 1 : 0;

                // Variator shifts toward high (smaller ratio) with higher RPM & heavier rollers, resisted by contra spring & hill slope
                float rollerBias = SimMath.Clamp(RollerWeightGrams / 6.5f, 0.55f, 1.55f);
                float springResistance = SimMath.Max(0.5f, ContraSpringRate) + SimMath.Clamp(roadSlopeRadians * 2.2f, 0f, 0.65f);
                float shiftProgress = SimMath.Clamp01(((engineRpm * rollerBias) - 3400f * springResistance) / 4200f);
                float targetVariator = SimMath.Lerp(1.98f, 1.02f, shiftProgress);

                if (dt > 0f)
                {
                    VariatorRatio = SimMath.MoveTowards(VariatorRatio, targetVariator, 2.2f * dt);
                }
                else
                {
                    VariatorRatio = targetVariator;
                }

                transmissionRatio = VariatorRatio * PrimaryReduction;
                EffectiveTotalRatio = transmissionRatio * sprocketRatio;
                driveEfficiency *= 0.85f * beltGrip;

                float wheelCoupled = wheelRpm * EffectiveTotalRatio;
                CoupledEngineRpm = ComputeCoupledEngineRpm(wheelCoupled, engageThreshold, VariatorEngageSpanRpm, throttle);
            }
            else
            {
                // Targa A35: 2-speed automatic gearbox (1st -> 2nd sprag clutch)
                float engageThreshold = SimMath.Max(1700f, ClutchEngageRpm);
                float corkGrip = 1f - SimMath.Clamp01(ClutchCorkGlaze * 0.40f);
                ClutchEngagement = SimMath.Clamp01((engineRpm - engageThreshold) / TwoSpeedEngageSpanRpm) * corkGrip;

                if (dt > 0f && engineRpm > 2000f && TransmissionOilLevel < 0.35f)
                {
                    ClutchCorkGlaze = SimMath.Clamp01(ClutchCorkGlaze + (0.35f - TransmissionOilLevel) * 0.04f * dt);
                }

                if (ClutchEngagement <= 0.08f)
                {
                    CurrentGear = 0;
                }
                else
                {
                    float slopeShiftDelay = SimMath.Clamp(roadSlopeRadians * 4500f, 0f, 1300f);
                    float upshiftRpm = ShiftRpmThreshold + slopeShiftDelay;
                    float downshiftRpm = upshiftRpm - 1200f;

                    float g2Ratio = GearRatio2 * PrimaryReduction * 0.88f * sprocketRatio;

                    if (CurrentGear <= 1)
                    {
                        CurrentGear = 1;
                        if (engineRpm >= upshiftRpm && wheelRpm * g2Ratio >= engageThreshold + 300f)
                        {
                            CurrentGear = 2;
                        }
                    }
                    else if (CurrentGear == 2 && (engineRpm <= downshiftRpm || wheelRpm * g2Ratio < engageThreshold))
                    {
                        CurrentGear = 1;
                    }
                }

                float activeGear = CurrentGear == 2 ? GearRatio2 : GearRatio1;
                transmissionRatio = activeGear * PrimaryReduction * 0.88f;
                EffectiveTotalRatio = transmissionRatio * sprocketRatio;
                driveEfficiency *= 0.90f * corkGrip;

                float wheelCoupled = wheelRpm * EffectiveTotalRatio;
                CoupledEngineRpm = ComputeCoupledEngineRpm(wheelCoupled, engageThreshold, TwoSpeedEngageSpanRpm, throttle);
            }

            float driveTorque = combustionTorqueNm * EffectiveTotalRatio * ClutchEngagement * driveEfficiency;
            RearWheelTorqueNm = SimMath.Max(0f, driveTorque) + PedalWheelTorqueNm;
            EngineBrakeWheelTorqueNm = engineBrakeTorqueNm * EffectiveTotalRatio * ClutchEngagement * 0.75f;
            EngineLoadTorqueNm = ClutchEngagement * SimMath.Max(0f, combustionTorqueNm * 0.35f + SimMath.Max(0f, roadSlopeRadians) * 4.5f);
        }

        /// <summary>
        /// Resolves the engine speed the slipping/locked clutch holds the crank at. While the wheel side is still below the
        /// throttle-dependent stall speed, a slipping centrifugal clutch holds the engine at that stall speed (strong launch
        /// torque instead of free-revving into the limiter); once road speed catches up, the crank is locked to the wheel.
        /// At closed throttle the stall speed collapses to the engagement threshold so the clutch releases back to idle.
        /// </summary>
        private float ComputeCoupledEngineRpm(float wheelCoupledRpm, float engageThreshold, float engageSpanRpm, float throttle)
        {
            float throttleDemand = SimMath.Pow(SimMath.Clamp01(throttle), 0.75f);
            ClutchStallRpm = engageThreshold + (engageSpanRpm + SimMath.Max(0f, ClutchStallRpmRise)) * throttleDemand;

            if (ClutchEngagement > 0.05f)
            {
                return SimMath.Max(wheelCoupledRpm, ClutchStallRpm);
            }
            return wheelCoupledRpm >= engageThreshold ? wheelCoupledRpm : 0f;
        }

        /// <summary>
        /// Installs a fresh drive belt and clears snapped belt state.
        /// </summary>
        public void ReplaceBelt()
        {
            BeltWear = 0f;
            BeltOilContamination = 0f;
            IsBeltSnapped = false;
        }

        /// <summary>
        /// Re-installs drive chain with master link and sets proper tension slack.
        /// </summary>
        public void RepairChain(float targetSlackMm = 20f)
        {
            IsChainThrown = false;
            ChainSlackMm = SimMath.Clamp(targetSlackMm, 12f, 26f);
        }
    }
}
