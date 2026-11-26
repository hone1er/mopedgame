#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Top-level per-bike deterministic mechanical simulation combining assembly DAG, engine, thermal, fuel, ignition, exhaust, drivetrain, and fault subsystems.
    /// </summary>
    public sealed class BikeSimulation
    {
        private const float MpsToMph = 2.23693629f;
        private const float GravityMps2 = 9.81f;
        private const float RiderMassKg = 75f;
        private const float AirDensityKgPerM3 = 1.225f;

        /// <summary>
        /// Drag-area multiplier when the rider tucks behind stock bars (chin to bars, elbows in: ~30% less CdA).
        /// A stock pipe's torque falls off steeply at top speed, so this cut nets the specified +3-4 mph on flat straights.
        /// </summary>
        public const float AeroTuckCdAMultiplier = 0.70f;

        public BikeArchetypeSpec Archetype { get; }
        public BikeAssemblyGraph Assembly { get; }
        public EngineSimulation Engine { get; }
        public ThermalModel Thermal { get; }
        public SparkPlugState SparkPlug { get; }
        public IgnitionSystem Ignition { get; }
        public FuelSystem Fuel { get; }
        public ExhaustSystem Exhaust { get; }
        public DrivetrainModel Drivetrain { get; }
        public FaultEngine Faults { get; }

        public float AmbientTemperatureC { get; set; } = 22f;
        public float VehicleSpeedMps { get; set; }
        public float OdometerMeters { get; private set; }
        public float RiderStamina { get; set; } = 1.0f;
        public float TotalSimulatedSeconds { get; private set; }
        public EngineTelemetrySnapshot LatestTelemetry { get; private set; }

        public BikeSimulation(BikeArchetypeSpec archetype, IReadOnlyDictionary<string, PartDefinition>? catalog = null)
        {
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            Assembly = new BikeAssemblyGraph(archetype, catalog);
            Engine = new EngineSimulation();
            Thermal = new ThermalModel { ChtCelsius = AmbientTemperatureC, CrankcaseTempCelsius = AmbientTemperatureC };
            SparkPlug = new SparkPlugState();
            Ignition = new IgnitionSystem();
            Fuel = new FuelSystem();
            Exhaust = new ExhaustSystem();
            Drivetrain = new DrivetrainModel { Type = archetype.DrivetrainType };
            Faults = new FaultEngine();

            SyncSubsystemConfigFromAssembly();
            ApplyArchetypeFuelDefaults();
            Engine.Rpm = Engine.IdleRpmTarget;
            Engine.IsRunning = true;
            LatestTelemetry = BuildSnapshot(RideInputState.Default, 0f);
        }

        /// <summary>
        /// Hydrates the fresh bike's tank from the archetype's default fuel mix (fuel level, premix oil fraction, petcock, rust).
        /// </summary>
        private void ApplyArchetypeFuelDefaults()
        {
            Fuel.PetcockMode = Archetype.DefaultPetcockMode;
            Fuel.FuelLevelLiters = 0f;
            Fuel.FloatBowlLiters = 0f;
            float liters = SimMath.Clamp(Archetype.DefaultFuelLiters, 0f, Fuel.TankCapacityLiters);
            float rust = SimMath.Clamp01(Archetype.DefaultTankRustContamination);
            Refuel(liters, SimMath.Max(0f, Archetype.DefaultPremixOilFraction), rust);
        }

        /// <summary>
        /// Refreshes subsystem physical specifications from currently mounted parts in BikeAssemblyGraph.
        /// </summary>
        public void SyncSubsystemConfigFromAssembly()
        {
            PartInstance? plug = Assembly.GetInstalledPart(SlotIds.SparkPlug);
            if (plug != null)
            {
                SparkPlug.IsInstalled = true;
                SparkPlug.HeatRange = plug.Definition.Stats.HeatRange > 0 ? plug.Definition.Stats.HeatRange : 6;
                SparkPlug.ElectrodeGapMm = plug.Definition.Stats.ElectrodeGapMm > 0f ? plug.Definition.Stats.ElectrodeGapMm : 0.60f;
            }
            else
            {
                SparkPlug.IsInstalled = false;
            }

            PartInstance? ignitionPart = Assembly.GetInstalledPart(SlotIds.Ignition);
            if (ignitionPart != null)
            {
                Ignition.Type = ignitionPart.Definition.Stats.IgnitionType;
                Ignition.CondenserHeatThresholdC = ignitionPart.Definition.Stats.CondenserHeatThresholdC > 0f
                    ? ignitionPart.Definition.Stats.CondenserHeatThresholdC
                    : 95f;
                Ignition.MaxRpmRating = ignitionPart.Definition.Stats.MaxRpmRating > 0f
                    ? ignitionPart.Definition.Stats.MaxRpmRating
                    : 8500f;
            }

            PartInstance? tank = Assembly.GetInstalledPart(SlotIds.FuelTank);
            if (tank != null)
            {
                Fuel.TankCapacityLiters = tank.Definition.Stats.TankCapacityLiters > 0f ? tank.Definition.Stats.TankCapacityLiters : 3.2f;
                Fuel.ReserveThresholdLiters = tank.Definition.Stats.ReserveLiters > 0f ? tank.Definition.Stats.ReserveLiters : 0.6f;
                Fuel.RustContamination = tank.RustContamination;
            }

            if (Assembly.HasSlot(SlotIds.InlineFilter))
            {
                Fuel.HasInlineFilter = Assembly.IsSlotOccupied(SlotIds.InlineFilter);
            }

            PartInstance? drivetrainPart = Assembly.GetInstalledPart(SlotIds.DrivetrainSide);
            if (drivetrainPart != null)
            {
                PartStats d = drivetrainPart.Definition.Stats;
                Drivetrain.Type = d.DrivetrainType;
                if (d.ClutchEngageRpm > 0f)
                {
                    Drivetrain.ClutchEngageRpm = d.ClutchEngageRpm;
                }
                if (d.ClutchSlipHeatRate > 0f)
                {
                    Drivetrain.ClutchSlipHeatRate = d.ClutchSlipHeatRate;
                }
                if (d.RollerWeightGrams > 0f)
                {
                    Drivetrain.RollerWeightGrams = d.RollerWeightGrams;
                }
                if (d.ContraSpringRate > 0f)
                {
                    Drivetrain.ContraSpringRate = d.ContraSpringRate;
                }
                if (d.GearRatio1 > 0f)
                {
                    Drivetrain.GearRatio1 = d.GearRatio1;
                }
                if (d.GearRatio2 > 0f)
                {
                    Drivetrain.GearRatio2 = d.GearRatio2;
                }
                if (d.ShiftRpmThreshold > 0f)
                {
                    Drivetrain.ShiftRpmThreshold = d.ShiftRpmThreshold;
                }
            }

            PartInstance? frontSprocket = Assembly.GetInstalledPart(SlotIds.FrontSprocket);
            if (frontSprocket != null && frontSprocket.Definition.Stats.FrontSprocketTeeth > 0)
            {
                Drivetrain.FrontSprocketTeeth = frontSprocket.Definition.Stats.FrontSprocketTeeth;
            }

            PartInstance? rearSprocket = Assembly.GetInstalledPart(SlotIds.RearSprocket);
            if (rearSprocket != null && rearSprocket.Definition.Stats.RearSprocketTeeth > 0)
            {
                Drivetrain.RearSprocketTeeth = rearSprocket.Definition.Stats.RearSprocketTeeth;
            }
        }

        /// <summary>
        /// Advances deterministic simulation by dt seconds using internal 1D longitudinal load physics.
        /// </summary>
        public EngineTelemetrySnapshot Step(float dt, in RideInputState input, ISimRandom rng)
        {
            return StepInternal(dt, in input, externalWheelSpeedMps: null, rng);
        }

        /// <summary>
        /// Advances deterministic simulation by dt seconds synchronized with an external wheel speed from Unity rigidbody physics.
        /// </summary>
        public EngineTelemetrySnapshot Step(float dt, in RideInputState input, float externalWheelSpeedMps, ISimRandom rng)
        {
            return StepInternal(dt, in input, externalWheelSpeedMps: externalWheelSpeedMps, rng);
        }

        private EngineTelemetrySnapshot StepInternal(float dt, in RideInputState input, float? externalWheelSpeedMps, ISimRandom rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }
            if (dt < 0f)
            {
                dt = 0f;
            }

            SyncSubsystemConfigFromAssembly();

            float speedMps = externalWheelSpeedMps.HasValue ? SimMath.Max(0f, externalWheelSpeedMps.Value) : VehicleSpeedMps;

            PartInstance? cylinder = Assembly.GetInstalledPart(SlotIds.Cylinder);
            PartInstance? piston = Assembly.GetInstalledPart(SlotIds.Piston);
            PartInstance? head = Assembly.GetInstalledPart(SlotIds.CylinderHead);
            PartInstance? carb = Assembly.GetInstalledPart(SlotIds.Carburetor);
            PartInstance? intake = Assembly.GetInstalledPart(SlotIds.IntakeManifold);
            PartInstance? filter = Assembly.GetInstalledPart(SlotIds.AirFilter);
            PartInstance? mainJetPart = Assembly.GetInstalledPart(SlotIds.MainJet);
            PartInstance? exhaustPart = Assembly.GetInstalledPart(SlotIds.Exhaust);
            PartInstance? bars = Assembly.GetInstalledPart(SlotIds.Handlebars);

            bool hasCoreEngineParts = cylinder != null && piston != null && head != null && carb != null && intake != null;

            float displacementCc = cylinder != null && cylinder.Definition.Stats.DisplacementCc > 0f
                ? cylinder.Definition.Stats.DisplacementCc
                : 49f;
            float compressionRatio = head != null && head.Definition.Stats.CompressionRatio > 0f
                ? head.Definition.Stats.CompressionRatio
                : (cylinder != null && cylinder.Definition.Stats.CompressionRatio > 0f ? cylinder.Definition.Stats.CompressionRatio : 9.0f);
            float portFlow = cylinder != null && cylinder.Definition.Stats.PortFlowEfficiency > 0f
                ? cylinder.Definition.Stats.PortFlowEfficiency
                : 1.0f;
            float finCooling = cylinder != null && cylinder.Definition.Stats.CoolingFinEfficiency > 0f
                ? cylinder.Definition.Stats.CoolingFinEfficiency
                : 1.0f;

            float venturiMm = carb != null && carb.Definition.Stats.VenturiMm > 0f ? carb.Definition.Stats.VenturiMm : 12f;
            int mainJet = mainJetPart != null && mainJetPart.Definition.Stats.MainJetSize > 0
                ? mainJetPart.Definition.Stats.MainJetSize
                : (carb != null && carb.MainJetOverride > 0
                    ? carb.MainJetOverride
                    : (carb != null && carb.Definition.Stats.MainJetSize > 0 ? carb.Definition.Stats.MainJetSize : 60));
            int pilotJet = carb != null && carb.PilotJetOverride > 0
                ? carb.PilotJetOverride
                : (carb != null && carb.Definition.Stats.PilotJetSize > 0 ? carb.Definition.Stats.PilotJetSize : 38);
            int needleClip = carb != null && carb.NeedleClipOverride > 0
                ? carb.NeedleClipOverride
                : (carb != null && carb.Definition.Stats.NeedleClipPosition > 0 ? carb.Definition.Stats.NeedleClipPosition : 3);
            float idleTurns = carb != null && carb.IdleScrewTurnsOverride > 0f
                ? carb.IdleScrewTurnsOverride
                : (carb != null ? carb.Definition.Stats.IdleScrewTurns : 1.5f);

            float filterRestriction = filter != null ? filter.Definition.Stats.AirFilterRestriction : 0f;
            float filterMoistureSens = filter != null ? filter.Definition.Stats.MoistureSensitivity : 0.4f;
            float wetExposure = input.SurfaceGrip < 0.85f ? (0.85f - input.SurfaceGrip) * filterMoistureSens : 0f;

            float exhaustFlow = exhaustPart != null && exhaustPart.Definition.Stats.FlowEfficiency > 0f
                ? exhaustPart.Definition.Stats.FlowEfficiency
                : 1.0f;

            // 1. Assembly vibration & leak computation
            float chassisVibration = (Engine.IsRunning ? (Engine.Rpm / 7500f) * 0.45f : 0f) + input.SurfaceRoughness * (speedMps * 0.08f);
            Assembly.ApplyVibration(dt, chassisVibration, input.SurfaceRoughness);
            float assemblyAirLeak = Assembly.ComputeAirLeakSeverity();
            float assemblyExhaustLeak = Assembly.ComputeExhaustLeakSeverity();

            // 2. Fuel delivery & petcock
            bool isTankInstalled = !Assembly.HasSlot(SlotIds.FuelTank) || Assembly.IsSlotOccupied(SlotIds.FuelTank);
            Fuel.Step(dt, Engine.Rpm, input.Throttle, Engine.IsRunning, isTankInstalled, carb != null, rng);

            // 3. Ignition & spark
            bool isStatorInstalled = !Assembly.HasSlot(SlotIds.Ignition) || Assembly.IsSlotOccupied(SlotIds.Ignition);
            Ignition.Step(
                dt,
                Engine.Rpm,
                Thermal.CrankcaseTempCelsius,
                SparkPlug.FoulingLevel,
                SparkPlug.ElectrodeGapMm,
                SparkPlug.IsInstalled,
                isStatorInstalled,
                wetExposure,
                rng);

            // 4. Exhaust curve & leak
            Exhaust.Step(
                dt,
                Engine.Rpm,
                input.Throttle,
                Engine.EffectiveAfr,
                Fuel.PremixRatio,
                Engine.IsRunning,
                exhaustPart,
                assemblyExhaustLeak);

            // 5. Engine combustion & AFR
            Engine.Step(
                dt,
                in input,
                displacementCc,
                compressionRatio,
                portFlow,
                venturiMm,
                mainJet,
                pilotJet,
                needleClip,
                idleTurns,
                filterRestriction,
                assemblyAirLeak,
                exhaustFlow,
                Exhaust.TorqueMultiplier,
                Exhaust.CarbonClog,
                Exhaust.ExhaustLeakFactor,
                Thermal.ChtCelsius,
                Thermal.CompressionHealth,
                Ignition.MisfireFactor,
                Ignition.HasSpark,
                Fuel.HasFuelForCombustion,
                Fuel.IsMainJetClogged,
                Thermal.IsSeized,
                hasCoreEngineParts,
                Drivetrain.EngineLoadTorqueNm,
                Drivetrain.CoupledEngineRpm,
                Drivetrain.ClutchEngagement,
                Drivetrain.IsDriveDisconnected);

            // 6. Thermal model & seizures
            Thermal.Step(
                dt,
                AmbientTemperatureC,
                Engine.Rpm,
                input.Throttle,
                Engine.EffectiveAfr,
                Fuel.PremixRatio,
                speedMps,
                input.RoadSlopeRadians,
                displacementCc,
                finCooling,
                Engine.IsRunning);

            // Hard/soft seize stops crank cleanly
            if (Thermal.IsSeized)
            {
                Engine.IsRunning = false;
                Engine.Rpm = 0f;
            }

            // 7. Spark plug reading update
            SparkPlug.Step(
                dt,
                Engine.Rpm,
                input.Throttle,
                Engine.EffectiveAfr,
                Fuel.PremixRatio,
                Thermal.ChtCelsius,
                Engine.IsRunning);

            // 8. Drivetrain & pedal assist
            float stamina = RiderStamina;
            Drivetrain.Step(
                dt,
                Engine.Rpm,
                Engine.CombustionTorqueNm,
                Engine.EngineBrakeTorqueNm,
                input.Throttle,
                speedMps,
                Archetype.WheelRadiusM,
                input.RoadSlopeRadians,
                input.SurfaceRoughness,
                input.PedalAssist,
                ref stamina,
                rng);
            RiderStamina = stamina;

            // 9. Internal 1D longitudinal bike dynamics (for headless tests & dyno)
            if (!externalWheelSpeedMps.HasValue && dt > 0f)
            {
                float totalMass = SimMath.Max(60f, Archetype.BaseDryMassKg + Assembly.GetMountedPartsMassKg() + RiderMassKg);
                float wheelRadius = SimMath.Max(0.15f, Archetype.WheelRadiusM);

                float driveForceN = Drivetrain.RearWheelTorqueNm / wheelRadius;
                float engineBrakeN = Drivetrain.EngineBrakeWheelTorqueNm / wheelRadius;

                // Brakes (scaled slightly by surface grip)
                float grip = input.SurfaceGrip <= 0f ? 1.0f : input.SurfaceGrip;
                float brakeForceN = (input.FrontBrake * 380f + input.RearBrake * 290f) * SimMath.Clamp(grip, 0.25f, 1.2f);

                // Aerodynamic drag with rider tuck & handlebar geometry
                float barAero = bars != null ? bars.Definition.Stats.AeroDragMultiplier : 1.0f;
                float barTuckBonus = bars != null ? bars.Definition.Stats.TuckDragBonusMultiplier : 1.0f;
                float tuckMult = input.AeroTuck ? (AeroTuckCdAMultiplier / SimMath.Max(0.5f, barTuckBonus)) : 1.0f;
                float cda = Archetype.DragAreaCdA * barAero * tuckMult;
                float airDensity = (input.AmbientAirDensity <= 0f ? RideInputState.StandardAirDensity : input.AmbientAirDensity) * AirDensityKgPerM3;
                float aeroDragN = 0.5f * airDensity * cda * speedMps * speedMps;

                // Rolling drag & hill grade gravity
                float rollingDragCoeff = Archetype.RollingResistanceCoeff * (1f + input.SurfaceRoughness * 0.25f);
                float rollingDragN = speedMps > 0.05f ? totalMass * GravityMps2 * rollingDragCoeff : 0f;
                float gradeForceN = totalMass * GravityMps2 * SimMath.Sin(input.RoadSlopeRadians);

                float netForceN = driveForceN - aeroDragN - rollingDragN - engineBrakeN - brakeForceN - gradeForceN;
                float accel = netForceN / totalMass;
                speedMps = SimMath.Max(0f, speedMps + accel * dt);

                // Hard seize briefly locks rear wheel unless decompressor is pulled
                if (Thermal.IsHardSeized && !input.Decompressor)
                {
                    speedMps = SimMath.MoveTowards(speedMps, 0f, 18f * dt);
                }

                VehicleSpeedMps = speedMps;
                OdometerMeters += speedMps * dt;
            }
            else if (externalWheelSpeedMps.HasValue)
            {
                VehicleSpeedMps = speedMps;
                OdometerMeters += speedMps * dt;
            }

            // 10. Fault taxonomy evaluation
            Faults.Evaluate(SparkPlug, Fuel, Ignition, Engine, Drivetrain, Exhaust, Thermal);

            TotalSimulatedSeconds += dt;
            LatestTelemetry = BuildSnapshot(in input, chassisVibration);
            return LatestTelemetry;
        }

        public WrenchActionResult TryRemovePart(string slotId)
        {
            WrenchActionResult res = Assembly.TryRemovePart(slotId);
            if (res.Succeeded)
            {
                SyncSubsystemConfigFromAssembly();
                RefreshLeakCachesAndFaults();
            }
            return res;
        }

        public WrenchActionResult TryInstallPart(string slotId, PartInstance part)
        {
            WrenchActionResult res = Assembly.TryInstallPart(slotId, part);
            if (res.Succeeded)
            {
                if (string.Equals(slotId, SlotIds.Cylinder, StringComparison.Ordinal)
                    || string.Equals(slotId, SlotIds.Piston, StringComparison.Ordinal))
                {
                    // Installing fresh top-end restores compression
                    if (part.Condition >= 0.7f && Thermal.IsHardSeized)
                    {
                        Thermal.RebuildTopEnd();
                    }
                }
                SyncSubsystemConfigFromAssembly();
                RefreshLeakCachesAndFaults();
            }
            return res;
        }

        public WrenchActionResult TrySwapPart(string slotId, PartInstance part)
        {
            WrenchActionResult res = Assembly.TrySwapPart(slotId, part);
            if (res.Succeeded)
            {
                SyncSubsystemConfigFromAssembly();
                RefreshLeakCachesAndFaults();
            }
            return res;
        }

        public WrenchActionResult TryRemoveFastener(string fastenerId)
        {
            WrenchActionResult res = Assembly.TryRemoveFastener(fastenerId);
            RefreshLeakCachesAndFaults();
            return res;
        }

        public WrenchActionResult TryTorqueFastener(string fastenerId, float torqueNm)
        {
            WrenchActionResult res = Assembly.TryTorqueFastener(fastenerId, torqueNm);
            RefreshLeakCachesAndFaults();
            return res;
        }

        public WrenchActionResult ApplyPenetratingOil(string fastenerId)
        {
            return Assembly.ApplyPenetratingOil(fastenerId);
        }

        /// <summary>
        /// Restores a persisted fastener state verbatim (save/load only) without wrenching side effects such as stripping.
        /// </summary>
        public WrenchActionResult RestoreFastenerState(
            string fastenerId,
            bool isInstalled,
            float currentTorqueNm,
            bool isStripped,
            bool isSeized,
            bool penetratingOilApplied)
        {
            WrenchActionResult res = Assembly.RestoreFastenerState(fastenerId, isInstalled, currentTorqueNm, isStripped, isSeized, penetratingOilApplied);
            RefreshLeakCachesAndFaults();
            return res;
        }

        public WrenchActionResult RepairStrippedFastener(string fastenerId)
        {
            WrenchActionResult res = Assembly.RepairStrippedFastener(fastenerId);
            RefreshLeakCachesAndFaults();
            return res;
        }

        public IReadOnlyList<InteractableNode> GetInteractableNodes()
        {
            return Assembly.GetInteractableNodes();
        }

        public SparkPlugReading InspectSparkPlug()
        {
            return SparkPlug.Inspect();
        }

        public void SetPetcockMode(PetcockMode mode)
        {
            Fuel.PetcockMode = mode;
        }

        /// <summary>
        /// Restores the odometer from a save (meters). Negative, NaN, or infinite values are rejected (returns false, unchanged).
        /// </summary>
        public bool RestoreOdometer(float meters)
        {
            if (!(meters >= 0f) || float.IsInfinity(meters))
            {
                return false;
            }

            OdometerMeters = meters;
            return true;
        }

        /// <summary>
        /// Refuels with <paramref name="premixRatio"/> as an oil fraction (use ThermalModel.PremixFromRatio for X:1).
        /// A non-negative rust value is stored on the installed tank part, which SyncSubsystemConfigFromAssembly reads every step.
        /// </summary>
        public void Refuel(float liters, float premixRatio, float tankRustContamination = -1f)
        {
            Fuel.Refuel(liters, premixRatio, tankRustContamination);
            if (tankRustContamination >= 0f)
            {
                PartInstance? tank = Assembly.GetInstalledPart(SlotIds.FuelTank);
                if (tank != null)
                {
                    tank.RustContamination = SimMath.Clamp01(tankRustContamination);
                }
            }
        }

        public void CoolDownSeconds(float seconds)
        {
            Thermal.CoolDown(seconds, AmbientTemperatureC);
            Ignition.Step(0f, 0f, Thermal.CrankcaseTempCelsius, SparkPlug.FoulingLevel, SparkPlug.ElectrodeGapMm, SparkPlug.IsInstalled, true, 0f, new SeededSimRandom(1));
            RefreshLeakCachesAndFaults();
        }

        public IReadOnlyList<FaultState> GetActiveFaults()
        {
            return Faults.ActiveFaults;
        }

        public RoadsideRepairResult TryRoadsideRepair(string faultId, IReadOnlyCollection<string> toolRollItemIds)
        {
            RoadsideRepairResult res = Faults.TryRoadsideRepair(
                faultId,
                toolRollItemIds,
                SparkPlug,
                Fuel,
                Ignition,
                Engine,
                Drivetrain,
                Assembly,
                Thermal);

            RefreshLeakCachesAndFaults();
            LatestTelemetry = BuildSnapshot(RideInputState.Default, 0f);
            return res;
        }

        /// <summary>
        /// Induces a physical fault state deterministically (useful for scenario tests and M4 roadside testing).
        /// </summary>
        public void InduceFault(string faultId)
        {
            switch (faultId)
            {
                case FaultIds.FouledPlug:
                    SparkPlug.FoulingLevel = 0.95f;
                    break;
                case FaultIds.CloggedJet:
                    Fuel.IsMainJetClogged = true;
                    break;
                case FaultIds.CondenserHeatSoak:
                    Ignition.Type = IgnitionType.Points6V;
                    Ignition.CondenserHealth = 0.35f;
                    Thermal.CrankcaseTempCelsius = 104f;
                    break;
                case FaultIds.PointsDrift:
                    Ignition.Type = IgnitionType.Points6V;
                    Ignition.SetPointsGap(0.18f);
                    break;
                case FaultIds.AirLeak:
                    Engine.CrankSealLeakSeverity = 0.55f;
                    break;
                case FaultIds.ThrownChain:
                    Drivetrain.IsChainThrown = true;
                    break;
                case FaultIds.SnappedBelt:
                    Drivetrain.Type = DrivetrainType.VariatedBelt;
                    Drivetrain.BeltWear = 1.0f;
                    Drivetrain.IsBeltSnapped = true;
                    break;
                case FaultIds.ExhaustLoose:
                    FastenerState? f1 = Assembly.GetFastener(FastenerIds.ExhaustFlange1);
                    if (f1 != null)
                    {
                        f1.CurrentTorqueNm = 0.5f;
                    }
                    FastenerState? f2 = Assembly.GetFastener(FastenerIds.ExhaustFlange2);
                    if (f2 != null)
                    {
                        f2.CurrentTorqueNm = 0.5f;
                    }
                    break;
                case FaultIds.SoftSeize:
                    Thermal.ChtCelsius = 236f;
                    Thermal.IsSoftSeized = true;
                    Engine.IsRunning = false;
                    Engine.Rpm = 0f;
                    break;
                case FaultIds.HardSeize:
                    Thermal.ChtCelsius = 262f;
                    Thermal.IsHardSeized = true;
                    Thermal.CompressionHealth = 0.25f;
                    Engine.IsRunning = false;
                    Engine.Rpm = 0f;
                    break;
            }

            Ignition.Step(0f, Engine.Rpm, Thermal.CrankcaseTempCelsius, SparkPlug.FoulingLevel, SparkPlug.ElectrodeGapMm, SparkPlug.IsInstalled, true, 0f, new SeededSimRandom(1));
            RefreshLeakCachesAndFaults();
            LatestTelemetry = BuildSnapshot(RideInputState.Default, 0f);
        }

        private void RefreshLeakCachesAndFaults()
        {
            Engine.RefreshAirLeakSeverity(Assembly.ComputeAirLeakSeverity());
            Exhaust.RefreshExhaustLeakFactor(Assembly.IsSlotOccupied(SlotIds.Exhaust), Assembly.ComputeExhaustLeakSeverity());
            Faults.Evaluate(SparkPlug, Fuel, Ignition, Engine, Drivetrain, Exhaust, Thermal);
        }

        /// <summary>
        /// Simulates limping back to West Oakland Garage via human pedal power.
        /// </summary>
        public RecoveryResult PedalOfShame(float distanceMiles)
        {
            float safeDist = SimMath.Max(0.1f, distanceMiles);
            float staminaCost = SimMath.Clamp(safeDist * 0.22f, 0.05f, RiderStamina);
            RiderStamina = SimMath.Max(0f, RiderStamina - staminaCost);
            VehicleSpeedMps = 0f;
            Engine.IsRunning = false;
            Engine.Rpm = 0f;
            return new RecoveryResult(
                true,
                "PedalOfShame",
                safeDist,
                staminaCost,
                0m,
                "Pedaled " + safeDist.ToString("0.0") + " mi back to West Oakland Garage.");
        }

        /// <summary>
        /// Calls the Buddy Truck Tow for instant garage recovery at a cash fee scaled by distance.
        /// </summary>
        public RecoveryResult BuddyTow(float distanceMiles)
        {
            float safeDist = SimMath.Max(0.1f, distanceMiles);
            decimal fee = 25m + (decimal)Math.Round(safeDist * 15.0, 2);
            VehicleSpeedMps = 0f;
            Engine.IsRunning = false;
            Engine.Rpm = 0f;
            return new RecoveryResult(
                true,
                "BuddyTow",
                safeDist,
                0f,
                fee,
                "Buddy Truck towed moped " + safeDist.ToString("0.0") + " mi to Garage ($" + fee.ToString("0.00") + ").");
        }

        private EngineTelemetrySnapshot BuildSnapshot(in RideInputState input, float chassisVibration)
        {
            float wheelOmega = VehicleSpeedMps / SimMath.Max(0.15f, Archetype.WheelRadiusM);
            float wheelPowerWatts = Drivetrain.RearWheelTorqueNm * wheelOmega;

            return new EngineTelemetrySnapshot(
                rpm: Engine.Rpm,
                rearWheelTorqueNm: Drivetrain.RearWheelTorqueNm,
                engineBrakeTorqueNm: Engine.EngineBrakeTorqueNm,
                effectiveAfr: Engine.EffectiveAfr,
                chtCelsius: Thermal.ChtCelsius,
                fuelLitersRemaining: Fuel.FuelLevelLiters,
                isReserveActive: Fuel.IsReserveActive,
                isFourStroking: Engine.IsFourStroking,
                isDetonating: Engine.IsDetonating,
                isPowerbandActive: Exhaust.IsPowerbandActive,
                isStalled: Engine.IsStalled,
                isSeized: Thermal.IsSeized,
                exhaustLeakFactor: Exhaust.ExhaustLeakFactor,
                headlightOutput: Ignition.HeadlightOutput,
                currentGear: Drivetrain.CurrentGear,
                throttlePosition: input.Throttle,
                vehicleSpeedMps: VehicleSpeedMps,
                vehicleSpeedMph: VehicleSpeedMps * MpsToMph,
                crankcaseTempCelsius: Thermal.CrankcaseTempCelsius,
                detonationRisk: Thermal.DetonationRisk,
                airLeakSeverity: Engine.AirLeakSeverity,
                misfireFactor: Ignition.MisfireFactor,
                clutchEngagement: Drivetrain.ClutchEngagement,
                variatorRatio: Drivetrain.VariatorRatio,
                compressionHealth: Thermal.CompressionHealth,
                riderStamina: RiderStamina,
                isRunning: Engine.IsRunning,
                isHardSeized: Thermal.IsHardSeized,
                isSoftSeized: Thermal.IsSoftSeized,
                isSputtering: Fuel.IsSputtering,
                isChainThrown: Drivetrain.IsChainThrown,
                isBeltSnapped: Drivetrain.IsBeltSnapped,
                chassisVibration: chassisVibration,
                powerOutputWatts: wheelPowerWatts,
                thermalZone: Thermal.CurrentZone,
                plugColorIndex: SparkPlug.InsulatorColorIndex);
        }
    }
}
