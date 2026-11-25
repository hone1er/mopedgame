namespace MopedSim.Core
{
    /// <summary>
    /// Simulates 2-stroke air-fuel mixture, jetting, choke, intake air leaks, four-stroking, detonation bog, and engine torque.
    /// </summary>
    public sealed class EngineSimulation
    {
        /// <summary>Ideal main jet for a stock 49cc engine on its matched 12mm venturi with a neutral (1.0 flow) pipe.</summary>
        public const float BaseIdealMainJet = 59.5f;

        /// <summary>Venturi bore (mm) matched to a stock 49cc engine; the reference size for venturi metering.</summary>
        public const float ReferenceVenturiMm = 12f;

        /// <summary>
        /// Matched venturi grows with displacement: 12mm @ 49cc, ~15mm @ 70cc, ~19mm @ 100cc.
        /// </summary>
        public const float MatchedVenturiDisplacementExponent = 0.625f;

        /// <summary>
        /// How strongly an oversized venturi demands a bigger main jet. A bore larger than the engine's matched size flows the
        /// same air at lower velocity (weaker metering signal), so it needs a larger jet for the same AFR. Undersized bores get
        /// no credit (ratio clamped at 1): a 70cc kit on the stock 12mm carb still runs lean on the stock jet and must be up-jetted.
        /// </summary>
        public const float VenturiMeteringExponent = 0.90f;

        public float Rpm { get; set; }
        public float EffectiveAfr { get; private set; } = 12.8f;
        public float CombustionTorqueNm { get; private set; }
        public float EngineBrakeTorqueNm { get; private set; }
        public float AirLeakSeverity { get; private set; }
        public float CrankSealLeakSeverity { get; set; }
        public float IdleRpmTarget { get; private set; } = 1400f;
        public float RedlineRpm { get; private set; } = 9800f;
        public bool IsFourStroking { get; private set; }
        public bool IsDetonating { get; private set; }
        public bool IsRunning { get; set; } = true;
        public bool IsStalled { get { return !IsRunning; } }

        /// <summary>
        /// Recomputes cached AirLeakSeverity immediately after wrenching, repair, or fault injection.
        /// </summary>
        public void RefreshAirLeakSeverity(float assemblyAirLeakSeverity)
        {
            AirLeakSeverity = SimMath.Clamp01(assemblyAirLeakSeverity + CrankSealLeakSeverity);
        }

        /// <summary>
        /// Main jet size that yields the ~12.8 AFR target at full main-circuit blend for the given displacement, carb venturi bore,
        /// and exhaust flow. Stock 49cc on a 12mm Bing sits near #58-60, the same 49cc on a 15mm SHA near #70, and a 70cc kit on a
        /// 15mm SHA (or the stock 12mm Bing) with a performance pipe near #72.
        /// </summary>
        public static float ComputeIdealMainJet(float displacementCc, float venturiMm, float exhaustFlowEfficiency)
        {
            float disp = SimMath.Max(displacementCc, 30f);
            float dispScale = SimMath.Pow(disp / 49f, 0.42f);
            float pipeScale = 1f + (exhaustFlowEfficiency - 1f) * 0.22f;
            float matchedVenturiMm = ReferenceVenturiMm * SimMath.Pow(disp / 49f, MatchedVenturiDisplacementExponent);
            float oversize = SimMath.Max(1f, SimMath.Clamp(venturiMm, 8f, 32f) / matchedVenturiMm);
            float venturiScale = SimMath.Pow(oversize, VenturiMeteringExponent);
            return BaseIdealMainJet * dispScale * pipeScale * venturiScale;
        }

        /// <summary>
        /// Updates air-fuel mixture, combustion torque, and RPM dynamics for one fixed timestep.
        /// </summary>
        public void Step(
            float dt,
            in RideInputState input,
            float displacementCc,
            float compressionRatio,
            float portFlowEfficiency,
            float venturiMm,
            int mainJetSize,
            int pilotJetSize,
            int needleClipPosition,
            float idleScrewTurns,
            float airFilterRestriction,
            float assemblyAirLeakSeverity,
            float exhaustFlowEfficiency,
            float exhaustTorqueMultiplier,
            float exhaustCarbonClog,
            float exhaustLeakFactor,
            float chtCelsius,
            float compressionHealth,
            float misfireFactor,
            bool hasSpark,
            bool hasFuel,
            bool isMainJetClogged,
            bool isSeized,
            bool hasCoreEngineParts,
            float drivetrainLoadTorqueNm,
            float drivetrainCoupledRpm,
            float clutchEngagement,
            bool isTransmissionDisconnected)
        {
            RefreshAirLeakSeverity(assemblyAirLeakSeverity);

            // Ideal main jet scales with displacement, pipe flow, and carb venturi bore relative to the engine's matched size:
            // 49cc/12mm stock (#58-60), 49cc/15mm SHA (#66-70), 70cc kit/15mm SHA (#70-74).
            float idealMainJet = ComputeIdealMainJet(displacementCc, venturiMm, exhaustFlowEfficiency);

            float throttle = input.Throttle;
            float ambientDensity = input.AmbientAirDensity <= 0f ? RideInputState.StandardAirDensity : input.AmbientAirDensity;

            // Air intake path: filter restriction reduces airflow; air leaks inject unmetered air downstream (strongest at idle vacuum).
            float filterAirFactor = 1f - SimMath.Clamp(airFilterRestriction, 0f, 0.75f) * 0.42f;
            float leakAirFactor = AirLeakSeverity * (0.28f + 0.55f * (1f - throttle));
            float totalAirFactor = SimMath.Max(0.25f, (filterAirFactor + leakAirFactor) * ambientDensity);

            // Fuel path: blend low-throttle pilot/idle screw circuit with mid-needle and main jet circuit.
            float pilotNorm = SimMath.Clamp(pilotJetSize / 38f, 0.5f, 1.8f);
            float idleScrewNorm = 1f - (idleScrewTurns - 1.5f) * 0.06f; // turning out leans idle slightly
            float lowFuelFactor = pilotNorm * idleScrewNorm;

            float mainRatio = SimMath.Max(mainJetSize, 30) / idealMainJet;
            float mainFuelFactor = SimMath.Pow(mainRatio, 1.22f);

            // Needle clip 1 (top/lean) .. 3 (mid) .. 5 (bottom/rich) affects mid-range throttle cleanly.
            float needleWeight = 1f - SimMath.Abs(throttle - 0.48f) * 2.0f;
            needleWeight = SimMath.Clamp01(needleWeight);
            float needleFactor = 1f + (SimMath.Clamp(needleClipPosition, 1, 5) - 3) * 0.045f * needleWeight;

            float highBlend = SimMath.Clamp01((throttle - 0.12f) / 0.55f);
            float baseFuelFactor = SimMath.Lerp(lowFuelFactor, mainFuelFactor, highBlend) * needleFactor;

            // Choke enriches cold start and causes rich bog if left on when warm.
            if (input.Choke)
            {
                baseFuelFactor *= (chtCelsius < 70f ? 1.36f : 1.48f);
            }

            // Clogged main jet starves fuel above idle unless choke provides bypass enrichment.
            if (isMainJetClogged)
            {
                float throttleStarve = throttle > 0.22f ? 0.32f : 0.72f;
                baseFuelFactor *= throttleStarve;
            }

            EffectiveAfr = SimMath.Clamp(12.8f * (totalAirFactor / SimMath.Max(baseFuelFactor, 0.15f)), 7.5f, 24.0f);

            // Air leaks raise idle RPM dramatically ("hanging high idle").
            IdleRpmTarget = 1400f + AirLeakSeverity * 2400f;
            RedlineRpm = 9400f + (exhaustFlowEfficiency - 1f) * 4500f;

            // Hard shutdown checks
            if (!hasCoreEngineParts || isSeized || compressionHealth < 0.45f)
            {
                IsRunning = false;
                IsFourStroking = false;
                IsDetonating = false;
                CombustionTorqueNm = 0f;
                EngineBrakeTorqueNm = 2.4f;
                Rpm = isSeized ? 0f : SimMath.MoveTowards(Rpm, 0f, 4500f * dt);
                return;
            }

            // Decompressor opens cylinder pressure, allowing easy pedaling or clean shutoff.
            if (input.Decompressor)
            {
                IsRunning = false;
                IsFourStroking = false;
                IsDetonating = false;
                CombustionTorqueNm = 0f;
                EngineBrakeTorqueNm = 0.25f;
                if (input.PedalAssist)
                {
                    Rpm = SimMath.MoveTowards(Rpm, 950f, 2400f * dt);
                }
                else
                {
                    Rpm = SimMath.MoveTowards(Rpm, 0f, 3200f * dt);
                }
                return;
            }

            // Pedal-start cranks stalled engine if fuel & spark are present.
            if (!IsRunning)
            {
                if (input.PedalAssist)
                {
                    Rpm = SimMath.MoveTowards(Rpm, 1100f, 2200f * dt);
                    bool coldWithoutChoke = chtCelsius < 55f && !input.Choke;
                    float startThreshold = coldWithoutChoke ? 980f : 720f;
                    if (Rpm >= startThreshold && hasSpark && hasFuel && !(isMainJetClogged && throttle > 0.25f))
                    {
                        IsRunning = true;
                        Rpm = SimMath.Max(Rpm, IdleRpmTarget);
                    }
                }
                else
                {
                    Rpm = SimMath.MoveTowards(Rpm, 0f, 3600f * dt);
                }

                if (!IsRunning)
                {
                    IsFourStroking = false;
                    IsDetonating = false;
                    CombustionTorqueNm = 0f;
                    EngineBrakeTorqueNm = 1.6f * compressionHealth;
                    return;
                }
            }

            // Stall conditions while running
            if (!hasSpark || !hasFuel || misfireFactor >= 0.92f || (isMainJetClogged && throttle > 0.25f && !input.Choke))
            {
                IsRunning = false;
                IsFourStroking = false;
                IsDetonating = false;
                CombustionTorqueNm = 0f;
                EngineBrakeTorqueNm = 1.6f * compressionHealth;
                Rpm = SimMath.MoveTowards(Rpm, 0f, 3200f * dt);
                return;
            }

            IsFourStroking = EffectiveAfr < 11.2f;
            IsDetonating = EffectiveAfr > 14.5f || chtCelsius > 206f;

            // Combustion torque model
            float dispTorqueScale = SimMath.Pow(SimMath.Max(displacementCc, 30f) / 49f, 1.08f);
            float venturiScale = 0.88f + SimMath.Clamp((venturiMm - 10f) / 10f, 0f, 1.2f) * 0.22f;
            float compScale = 0.78f + SimMath.Clamp((compressionRatio - 7.5f) / 5.0f, 0f, 1f) * 0.28f;
            float baseTorque = 5.0f * dispTorqueScale * venturiScale * compScale * portFlowEfficiency * compressionHealth;

            // AFR penalty curve: peak around 12.7 AFR
            float afrEfficiency;
            if (EffectiveAfr < 11.2f)
            {
                // Four-stroking rich blubber drops top-end torque cleanly
                afrEfficiency = SimMath.Clamp(0.62f + (EffectiveAfr - 7.5f) * 0.08f, 0.35f, 0.86f);
            }
            else if (EffectiveAfr > 14.5f)
            {
                // Lean bog & pre-ignition power drop
                afrEfficiency = SimMath.Clamp(0.95f - (EffectiveAfr - 14.5f) * 0.09f, 0.35f, 0.95f);
            }
            else
            {
                float dist = SimMath.Abs(EffectiveAfr - 12.75f);
                afrEfficiency = SimMath.Clamp(1.02f - dist * 0.04f, 0.88f, 1.02f);
            }

            // Cold engine stumble when revved hard without choke
            float coldPenalty = (chtCelsius < 70f && !input.Choke && throttle > 0.35f) ? 0.85f : 1.0f;

            // Carbon exhaust clog chokes upper RPM
            float clogPenalty = exhaustCarbonClog > 0.6f
                ? SimMath.Clamp(1f - (exhaustCarbonClog - 0.6f) * 1.4f * SimMath.Clamp01(Rpm / 6500f), 0.35f, 1f)
                : 1.0f;

            // Exhaust flange leak loses scavenging boost
            float exhaustLeakPenalty = 1f - exhaustLeakFactor * 0.26f;

            // Misfire cuts average cycle torque
            float sparkEfficiency = 1f - SimMath.Clamp01(misfireFactor) * 0.85f;

            // Soft rev limiter roll-off near RedlineRpm
            float revDrop = Rpm > RedlineRpm ? SimMath.Clamp01(1f - (Rpm - RedlineRpm) / 1800f) : 1.0f;

            float throttleCurve = 0.14f + 0.86f * SimMath.Pow(throttle, 0.85f);
            CombustionTorqueNm = SimMath.Max(
                0f,
                baseTorque
                * throttleCurve
                * exhaustTorqueMultiplier
                * afrEfficiency
                * coldPenalty
                * clogPenalty
                * exhaustLeakPenalty
                * sparkEfficiency
                * revDrop);

            // Engine braking when off throttle
            EngineBrakeTorqueNm = (1f - throttle) * (0.65f + 0.75f * (Rpm / 6000f)) * compressionHealth;

            // Crankshaft inertia & RPM integration
            float crankInertia = 0.0085f;
            float frictionTorque = 0.35f + 0.00018f * Rpm;
            float netTorque = CombustionTorqueNm - frictionTorque - drivetrainLoadTorqueNm;
            float rpmDelta = (netTorque / crankInertia) * (60f / (2f * 3.14159265f)) * dt;

            float freeRpm = SimMath.Clamp(Rpm + rpmDelta, IdleRpmTarget, RedlineRpm + 1400f);
            if (!isTransmissionDisconnected && clutchEngagement > 0.05f && drivetrainCoupledRpm > 0f)
            {
                float targetLoadedRpm = SimMath.Max(IdleRpmTarget, drivetrainCoupledRpm);
                float slipAuthority = 1f - clutchEngagement * 0.82f;
                float blended = SimMath.Lerp(targetLoadedRpm, freeRpm, slipAuthority);
                Rpm = SimMath.Clamp(SimMath.MoveTowards(Rpm, blended, 9000f * dt), IdleRpmTarget, RedlineRpm + 1200f);
            }
            else
            {
                // Free-revving (clutch open, snapped belt, or thrown chain)
                float freeTarget = SimMath.Lerp(IdleRpmTarget, RedlineRpm + 700f, SimMath.Pow(throttle, 0.72f));
                Rpm = SimMath.MoveTowards(Rpm, freeTarget, (throttle > 0.2f ? 7200f : 3800f) * dt);
            }

            if (Rpm < 650f)
            {
                IsRunning = false;
                Rpm = 0f;
            }
        }
    }
}
