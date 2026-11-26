#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MopedSim.Core;
using Xunit;

namespace MopedSim.Core.Tests
{
    public sealed class SoakAndBalanceTests
    {
        private const float SoakDt = 1f / 30f;
        private const int SoakDurationSeconds = 3600;
        private const int SoakStepCount = SoakDurationSeconds * 30; // 108,000 steps

        private readonly struct SoakRunSummary
        {
            public readonly EngineTelemetrySnapshot FinalTelemetry;
            public readonly float OdometerMeters;
            public readonly float TotalSimulatedSeconds;
            public readonly int RefuelCount;
            public readonly float MaxSpeedMps;
            public readonly float MaxChtCelsius;
            public readonly float MinAfr;
            public readonly float MaxAfr;
            public readonly int FogSteps;
            public readonly int TuckSteps;
            public readonly int HillClimbSteps;

            public SoakRunSummary(
                EngineTelemetrySnapshot finalTelemetry,
                float odometerMeters,
                float totalSimulatedSeconds,
                int refuelCount,
                float maxSpeedMps,
                float maxChtCelsius,
                float minAfr,
                float maxAfr,
                int fogSteps,
                int tuckSteps,
                int hillClimbSteps)
            {
                FinalTelemetry = finalTelemetry;
                OdometerMeters = odometerMeters;
                TotalSimulatedSeconds = totalSimulatedSeconds;
                RefuelCount = refuelCount;
                MaxSpeedMps = maxSpeedMps;
                MaxChtCelsius = maxChtCelsius;
                MinAfr = minAfr;
                MaxAfr = maxAfr;
                FogSteps = fogSteps;
                TuckSteps = tuckSteps;
                HillClimbSteps = hillClimbSteps;
            }
        }

        [Fact]
        public void SixtyMinuteHeadlessRidingSoak_NoNaNOrSeize_BoundedTelemetry_AndBitIdenticalDeterminism()
        {
            SoakRunSummary runA = RunSixtyMinuteSoak(20261126u);
            SoakRunSummary runB = RunSixtyMinuteSoak(20261126u);

            // Single-precision accumulation of 108,000 x (1f / 30f) drifts slightly above 3600s due to IEEE-754 float32 rounding.
            Assert.InRange(runA.TotalSimulatedSeconds, 3595.0f, 3610.0f);
            Assert.True(runA.RefuelCount >= 1, "Expected at least one refuel over 60 minutes, got " + runA.RefuelCount);
            Assert.True(runA.MaxSpeedMps > 10f, "Expected cruise speed > 10 m/s, got " + runA.MaxSpeedMps);
            Assert.True(runA.MaxChtCelsius > 110f && runA.MaxChtCelsius < ThermalModel.SoftSeizeTempC,
                "Expected warm bounded CHT, got max " + runA.MaxChtCelsius);
            Assert.InRange(runA.MinAfr, 10.5f, 14.0f);
            Assert.InRange(runA.MaxAfr, 12.0f, 16.0f);
            Assert.True(runA.OdometerMeters > 10000f, "Expected > 10 km covered in 60 minutes, got " + runA.OdometerMeters);
            Assert.True(runA.FogSteps > 0);
            Assert.True(runA.TuckSteps > 0);
            Assert.True(runA.HillClimbSteps > 0);

            Assert.Equal(runA.RefuelCount, runB.RefuelCount);
            Assert.Equal(BitConverter.SingleToInt32Bits(runA.OdometerMeters), BitConverter.SingleToInt32Bits(runB.OdometerMeters));
            Assert.Equal(BitConverter.SingleToInt32Bits(runA.TotalSimulatedSeconds), BitConverter.SingleToInt32Bits(runB.TotalSimulatedSeconds));
            AssertBitIdenticalTelemetry(in runA.FinalTelemetry, in runB.FinalTelemetry);
        }

        [Fact]
        public void CoreEconomyProgression_MaxerE50_MilkCrateCourierGigsTo70ccExpansionPipe_ImprovesPeakSpeedAndPowerband()
        {
            Dictionary<string, PartDefinition> catalog = TestFixtures.CreateCatalog();
            BikeArchetypeSpec archetype = TestFixtures.CreateArchetype(ArchetypeIds.MaxerE50);

            // Baseline stock peak speed on level asphalt.
            var stockBike = new BikeSimulation(archetype, catalog);
            (float stockPeakSpeedMps, _) = MeasureFlatWotRun(stockBike, 333u, seconds: 45f);
            Assert.True(stockPeakSpeedMps > 8f, "Stock bike should accelerate past 8 m/s; got " + stockPeakSpeedMps);

            // Player progression from default SimulationWorld starting cash ($250).
            var world = new SimulationWorld(new SeededSimRandom(444u));
            var bike = new BikeSimulation(archetype, catalog);
            world.RegisterBike("bike_001", bike);
            var jobs = new JobSessionManager();

            decimal initialCash = world.CashUsd;
            Assert.Equal(250m, initialCash);

            // Buy and mount milk crate rack to unlock 3+ slot courier gigs.
            PartDefinition milkCrate = catalog["rack_milk_crate"];
            Assert.True(world.TryPurchasePartIntoInventory(milkCrate, out PartInstance? cratePart).Succeeded);
            Assert.NotNull(cratePart);
            world.Inventory.Remove(cratePart!);
            Assert.True(bike.TryInstallPart(SlotIds.CargoRack, cratePart!).Succeeded);
            Assert.Equal(4, CargoCapacityCalculator.Compute(bike).Slots);

            JobDefinition courierGig = new JobDefinition
            {
                Id = "gig_core_crate_run",
                DisplayName = "West Oakland Crate Run",
                JobType = JobType.CourierGig,
                ZoneId = "Zone_WestOakland",
                TargetZoneId = "Zone_OaklandHills",
                RequiredCargoSlots = 3,
                RequiredCargoKg = 0f,
                RewardUsd = 95m,
                BonusRewardUsd = 30m,
                ReputationReward = 25,
                TimeLimitSeconds = 240f,
                Checkpoints = new List<JobCheckpointSpec>
                {
                    new JobCheckpointSpec("cp_start", 1, 0f, 0f, 0f, 10f, 0f),
                    new JobCheckpointSpec("cp_mid", 2, 0f, 0f, 500f, 10f, 2f),
                    new JobCheckpointSpec("cp_end", 3, 0f, 0f, 1100f, 10f, 5f),
                },
            };

            PartDefinition cyl70 = catalog["cyl_kit_70"];
            PartDefinition piston70 = catalog["piston_70"];
            PartDefinition head70 = catalog["head_highcomp_70"];
            PartDefinition expansionPipe = catalog["pipe_circuit_e50_proma"];
            PartDefinition mainJet72 = catalog["jet_72"];
            decimal upgradeTotalCost = cyl70.PriceUsd + piston70.PriceUsd + head70.PriceUsd + expansionPipe.PriceUsd + mainJet72.PriceUsd;

            // Complete courier gigs until the player can afford the full 70cc + expansion pipe + main jet package.
            int completedGigs = 0;
            while (world.CashUsd < upgradeTotalCost && completedGigs < 10)
            {
                bike.CoolDownSeconds(180f);
                if (bike.Fuel.FuelLevelLiters < 1.2f)
                {
                    bike.Refuel(2.0f, ThermalModel.PremixFromRatio(50f), 0f);
                }

                JobAcceptResult accepted = jobs.TryAcceptJob(courierGig, bike);
                Assert.True(accepted.Accepted, accepted.Message);
                Assert.Equal(CheckpointReportResult.Reached, jobs.ReportCheckpointReached("cp_start"));

                float[] cumulative = RallyPackSimulator.ComputeCumulativeDistances(courierGig.Checkpoints);
                var rideInput = new RideInputState(throttle: 0.90f, surfaceGrip: 1f, surfaceRoughness: 0.03f);
                int maxTicks = 30 * 300;
                int tick = 0;
                while (jobs.Status == JobSessionStatus.Active && tick < maxTicks)
                {
                    EngineTelemetrySnapshot t = world.Step(SoakDt, in rideInput);
                    AssertAllNumericFieldsFinite(in t);
                    jobs.Step(SoakDt, in t, rideInput.SurfaceRoughness);

                    JobCheckpointSpec? next = jobs.NextCheckpoint;
                    int reached = jobs.CheckpointsReached;
                    if (next != null && jobs.RouteProgressMeters >= cumulative[reached] - 0.01f)
                    {
                        jobs.ReportCheckpointReached(next.Id);
                    }
                    tick++;
                }

                Assert.Equal(JobSessionStatus.Completed, jobs.Status);
                Assert.True(jobs.TryCollectReward(world));
                completedGigs++;
            }

            Assert.True(completedGigs >= 1 && completedGigs < 10, "Completed gigs count unexpected: " + completedGigs);
            Assert.True(world.CashUsd >= upgradeTotalCost, "Expected enough cash for 70cc kit; had $" + world.CashUsd);
            Assert.True(world.Reputation >= 25, "Expected positive reputation; had " + world.Reputation);

            // Purchase all upgrade parts through SimulationWorld.
            Assert.True(world.TryPurchasePartIntoInventory(cyl70, out PartInstance? cylInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(piston70, out PartInstance? pistonInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(head70, out PartInstance? headInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(expansionPipe, out PartInstance? pipeInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(mainJet72, out PartInstance? jetInst).Succeeded);

            world.Inventory.Remove(cylInst!);
            world.Inventory.Remove(pistonInst!);
            world.Inventory.Remove(headInst!);
            world.Inventory.Remove(pipeInst!);
            world.Inventory.Remove(jetInst!);

            // Disassemble top-end, exhaust, and intake using Core assembly commands respecting DAG blockers.
            bike.CoolDownSeconds(600f);
            bike.VehicleSpeedMps = 0f;

            Assert.True(bike.TryRemoveFastener(FastenerIds.SparkPlugThread).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.SparkPlug).Succeeded);
            foreach (string headNut in new[] { FastenerIds.HeadNut1, FastenerIds.HeadNut2, FastenerIds.HeadNut3, FastenerIds.HeadNut4 })
            {
                Assert.True(bike.TryRemoveFastener(headNut).Succeeded, headNut);
            }
            Assert.True(bike.TryRemovePart(SlotIds.CylinderHead).Succeeded);
            PartInstance headGasket = bike.Assembly.GetInstalledPart(SlotIds.HeadGasket)!;
            Assert.True(bike.TryRemovePart(SlotIds.HeadGasket).Succeeded);

            foreach (string exhBolt in new[] { FastenerIds.ExhaustFlange1, FastenerIds.ExhaustFlange2, FastenerIds.ExhaustFrameBracket })
            {
                Assert.True(bike.TryRemoveFastener(exhBolt).Succeeded, exhBolt);
            }
            Assert.True(bike.TryRemovePart(SlotIds.Exhaust).Succeeded);
            PartInstance exhaustGasket = bike.Assembly.GetInstalledPart(SlotIds.ExhaustGasket)!;
            Assert.True(bike.TryRemovePart(SlotIds.ExhaustGasket).Succeeded);

            PartInstance airFilter = bike.Assembly.GetInstalledPart(SlotIds.AirFilter)!;
            PartInstance mainJetStock = bike.Assembly.GetInstalledPart(SlotIds.MainJet)!;
            PartInstance carb = bike.Assembly.GetInstalledPart(SlotIds.Carburetor)!;
            PartInstance intake = bike.Assembly.GetInstalledPart(SlotIds.IntakeManifold)!;
            PartInstance intakeGasket = bike.Assembly.GetInstalledPart(SlotIds.IntakeGasket)!;

            Assert.True(bike.TryRemovePart(SlotIds.AirFilter).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.MainJet).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.CarbClamp).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Carburetor).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake2).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeManifold).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeGasket).Succeeded);

            // Swap cylinder & piston.
            Assert.True(bike.TryRemovePart(SlotIds.Cylinder).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Piston).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Piston, pistonInst!).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Cylinder, cylInst!).Succeeded);

            // Reassemble head, spark plug, exhaust, intake, carb, and up-jet via TrySwapPart.
            Assert.True(bike.TryInstallPart(SlotIds.HeadGasket, headGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.CylinderHead, headInst!).Succeeded);
            foreach (string headNut in new[] { FastenerIds.HeadNut1, FastenerIds.HeadNut2, FastenerIds.HeadNut3, FastenerIds.HeadNut4 })
            {
                FastenerState f = bike.Assembly.GetFastener(headNut)!;
                Assert.True(bike.TryTorqueFastener(headNut, f.TargetTorqueNm).Succeeded, headNut);
            }
            Assert.True(bike.TryInstallPart(SlotIds.SparkPlug, new PartInstance(catalog["plug_b8hs"])).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.SparkPlugThread, bike.Assembly.GetFastener(FastenerIds.SparkPlugThread)!.TargetTorqueNm).Succeeded);

            Assert.True(bike.TryInstallPart(SlotIds.ExhaustGasket, exhaustGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Exhaust, pipeInst!).Succeeded);
            foreach (string exhBolt in new[] { FastenerIds.ExhaustFlange1, FastenerIds.ExhaustFlange2, FastenerIds.ExhaustFrameBracket })
            {
                FastenerState f = bike.Assembly.GetFastener(exhBolt)!;
                Assert.True(bike.TryTorqueFastener(exhBolt, f.TargetTorqueNm).Succeeded, exhBolt);
            }

            Assert.True(bike.TryInstallPart(SlotIds.IntakeGasket, intakeGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.IntakeManifold, intake).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.Intake1, bike.Assembly.GetFastener(FastenerIds.Intake1)!.TargetTorqueNm).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.Intake2, bike.Assembly.GetFastener(FastenerIds.Intake2)!.TargetTorqueNm).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Carburetor, carb).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.CarbClamp, bike.Assembly.GetFastener(FastenerIds.CarbClamp)!.TargetTorqueNm).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.MainJet, mainJetStock).Succeeded);
            Assert.True(bike.TrySwapPart(SlotIds.MainJet, jetInst!).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.AirFilter, airFilter).Succeeded);

            Assert.Equal(0f, bike.Assembly.ComputeAirLeakSeverity(), 5);
            Assert.Equal(0f, bike.Assembly.ComputeExhaustLeakSeverity(), 5);

            // Upgraded 70cc + expansion pipe speed run must exceed stock peak speed and hit powerband.
            (float upgradedPeakSpeedMps, bool powerbandActivated) = MeasureFlatWotRun(bike, 333u, seconds: 45f);
            Assert.True(upgradedPeakSpeedMps > stockPeakSpeedMps + 2.0f,
                "Expected upgraded 70cc speed (" + upgradedPeakSpeedMps.ToString("0.00") + " m/s) to significantly beat stock ("
                + stockPeakSpeedMps.ToString("0.00") + " m/s).");
            Assert.True(powerbandActivated, "Expected expansion pipe powerband to activate on upgraded 70cc WOT run.");
        }

        [Fact]
        public void SwapMeetMarket_OneHundredTwentyRotations_DeterministicDiscountedValidDefectsAndZeroExceptions()
        {
            List<PartDefinition> parts = TestFixtures.CreateCatalog().Values.ToList();
            var config = new SwapMeetConfig { OffersPerRotation = 6 };

            var marketA = new SwapMeetMarket(parts, 20261126u, config);
            var marketB = new SwapMeetMarket(parts, 20261126u, config);
            var marketDiff = new SwapMeetMarket(parts, 20261127u, config);
            var buyerWorld = new SimulationWorld(new SeededSimRandom(77u)) { CashUsd = 100000m };

            string rotation0Snapshot = SnapshotOffers(marketA);
            int differingRotations = 0;
            int totalInspected = 0;
            int totalPurchased = 0;

            for (int r = 0; r < 120; r++)
            {
                if (r > 0)
                {
                    marketA.AdvanceRotation();
                    marketB.Rotate(r);
                    marketDiff.Rotate(r);
                }

                Assert.Equal(r, marketA.RotationIndex);
                Assert.Equal(r, marketB.RotationIndex);
                Assert.Equal(config.OffersPerRotation, marketA.Offers.Count);
                Assert.Equal(SnapshotOffers(marketA), SnapshotOffers(marketB));

                if (!string.Equals(SnapshotOffers(marketA), SnapshotOffers(marketDiff), StringComparison.Ordinal))
                {
                    differingRotations++;
                }

                var offerIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SwapMeetOffer offer in marketA.Offers)
                {
                    Assert.True(offerIds.Add(offer.OfferId), "Duplicate offerId " + offer.OfferId + " at rotation " + r);
                    Assert.True(offer.AskingPriceUsd > 0m, "Non-positive asking price at rotation " + r);
                    Assert.True(offer.AskingPriceUsd < offer.ListPriceUsd,
                        "Asking price " + offer.AskingPriceUsd + " must be strictly below list price " + offer.ListPriceUsd);
                    Assert.InRange(offer.Condition, 0f, 1f);
                    Assert.False(offer.IsInspected);
                    Assert.Equal(SwapMeetDefect.None, offer.RevealedDefect);

                    SwapMeetInspection inspection = marketA.InspectOffer(offer.OfferId);
                    Assert.True(inspection.Succeeded, "InspectOffer failed for " + offer.OfferId);
                    Assert.Equal(offer.OfferId, inspection.OfferId);
                    Assert.True(Enum.IsDefined(typeof(SwapMeetDefect), inspection.Defect));
                    Assert.False(string.IsNullOrWhiteSpace(inspection.Description));
                    Assert.True(offer.IsInspected);
                    Assert.Equal(inspection.Defect, offer.RevealedDefect);

                    if (inspection.Defect != SwapMeetDefect.None)
                    {
                        IReadOnlyList<SwapMeetDefect> allowed = SwapMeetMarket.GetPossibleDefects(offer.Part);
                        Assert.Contains(inspection.Defect, allowed);
                    }
                    totalInspected++;
                }

                // Periodically exercise purchase path and verify no exceptions or double-buy.
                if (r % 12 == 0 && marketA.Offers.Count > 0)
                {
                    SwapMeetOffer target = marketA.Offers[0];
                    decimal before = buyerWorld.CashUsd;
                    SwapMeetPurchaseResult purchase = marketA.TryBuyOffer(target.OfferId, buyerWorld);
                    Assert.True(purchase.Succeeded, purchase.Message);
                    Assert.NotNull(purchase.Part);
                    Assert.Equal(before - target.AskingPriceUsd, buyerWorld.CashUsd);
                    Assert.True(target.IsSold);
                    Assert.False(marketA.TryBuyOffer(target.OfferId, buyerWorld).Succeeded);
                    totalPurchased++;
                }
            }

            Assert.Equal(120 * config.OffersPerRotation, totalInspected);
            Assert.Equal(10, totalPurchased);
            Assert.True(differingRotations > 100, "Different seeds should produce distinct swap-meet rotations.");

            // Rotating back to index 0 reproduces the original rotation identically.
            marketA.Rotate(0);
            Assert.Equal(rotation0Snapshot, SnapshotOffers(marketA));
        }

        private static SoakRunSummary RunSixtyMinuteSoak(uint seed)
        {
            Dictionary<string, PartDefinition> catalog = TestFixtures.CreateCatalog();
            BikeArchetypeSpec archetype = TestFixtures.CreateArchetype(ArchetypeIds.MaxerE50);
            var bike = new BikeSimulation(archetype, catalog);

            // Well-tuned endurance setup: 12V CDI ignition (avoids 6V points rubbing-block wearout over 60 minutes)
            // and clean 50:1 premix fuel.
            Assert.True(bike.TrySwapPart(SlotIds.Ignition, new PartInstance(catalog["stator_cdi_12v"])).Succeeded);
            bike.Fuel.FuelLevelLiters = 1.6f; // Forces refuel(s) within the 60-minute duty cycle.
            bike.Fuel.PremixRatio = ThermalModel.PremixFromRatio(50f);

            var rng = new SeededSimRandom(seed);
            int refuelCount = 0;
            float maxSpeed = 0f;
            float maxCht = float.MinValue;
            float minAfr = float.MaxValue;
            float maxAfr = float.MinValue;
            int fogSteps = 0;
            int tuckSteps = 0;
            int hillClimbSteps = 0;

            EngineTelemetrySnapshot last = default;
            const int cycleSteps = 600 * 30; // 10-minute repeating duty cycle (6 cycles = 60 min)

            for (int step = 0; step < SoakStepCount; step++)
            {
                int cycleStep = step % cycleSteps;
                float cycleSeconds = cycleStep * SoakDt;

                RideInputState input;
                if (cycleSeconds < 30f)
                {
                    // Idle warmup
                    input = new RideInputState(throttle: 0.06f, rearBrake: 0.25f, surfaceRoughness: 0.0f, ambientAirDensity: 1.0f);
                }
                else if (cycleSeconds < 180f)
                {
                    // WOT flat straightaway
                    input = new RideInputState(throttle: 1.0f, aeroTuck: false, roadSlopeRadians: 0f, surfaceGrip: 1.0f, surfaceRoughness: 0.04f, ambientAirDensity: 1.0f);
                }
                else if (cycleSeconds < 270f)
                {
                    // Aero tuck flat run
                    tuckSteps++;
                    input = new RideInputState(throttle: 1.0f, aeroTuck: true, roadSlopeRadians: 0f, surfaceGrip: 1.0f, surfaceRoughness: 0.03f, ambientAirDensity: 1.0f);
                }
                else if (cycleSeconds < 350f)
                {
                    // 10-15% hill climb (atan(0.10) ~ 0.100 rad, atan(0.15) ~ 0.149 rad)
                    hillClimbSteps++;
                    float gradeRad = cycleSeconds < 310f ? 0.10f : 0.148f;
                    bool needPedal = bike.VehicleSpeedMps < 4.5f;
                    input = new RideInputState(
                        throttle: 0.92f,
                        aeroTuck: false,
                        pedalAssist: needPedal,
                        roadSlopeRadians: gradeRad,
                        surfaceGrip: 0.95f,
                        surfaceRoughness: 0.06f,
                        ambientAirDensity: 1.0f);
                }
                else if (cycleSeconds < 440f)
                {
                    // Cooldown glide / gentle descent
                    input = new RideInputState(
                        throttle: 0.10f,
                        rearBrake: 0.12f,
                        roadSlopeRadians: -0.04f,
                        surfaceGrip: 1.0f,
                        surfaceRoughness: 0.02f,
                        ambientAirDensity: 1.0f);
                }
                else if (cycleSeconds < 570f)
                {
                    // Karl the Fog elevated AmbientAirDensity coastal run
                    fogSteps++;
                    input = new RideInputState(
                        throttle: 0.80f,
                        aeroTuck: true,
                        roadSlopeRadians: 0.015f,
                        surfaceGrip: 0.88f,
                        surfaceRoughness: 0.04f,
                        ambientAirDensity: 1.08f);
                }
                else
                {
                    // Pit stop idle + fastener torque maintenance check at start of pit window
                    if (cycleStep == 570 * 30)
                    {
                        foreach (FastenerState f in bike.Assembly.Fasteners)
                        {
                            if (f.IsInstalled && !f.IsStripped && f.CurrentTorqueNm < f.TargetTorqueNm - f.ToleranceNm * 0.5f)
                            {
                                Assert.True(bike.TryTorqueFastener(f.FastenerId, f.TargetTorqueNm).Succeeded, f.FastenerId);
                            }
                        }
                    }
                    input = new RideInputState(throttle: 0.05f, rearBrake: 0.5f, surfaceRoughness: 0f, ambientAirDensity: 1.0f);
                }

                last = bike.Step(SoakDt, in input, rng);

                AssertAllNumericFieldsFinite(in last);
                Assert.False(last.IsHardSeized, "Unexpected hard seize at step " + step + " (CHT=" + last.ChtCelsius + ")");
                Assert.False(last.IsSeized, "Unexpected seize at step " + step + " (CHT=" + last.ChtCelsius + ")");
                Assert.True(last.Rpm >= 0f && last.Rpm <= 13000f, "RPM out of bounds at step " + step + ": " + last.Rpm);
                Assert.True(last.ChtCelsius >= 15f && last.ChtCelsius < ThermalModel.SoftSeizeTempC,
                    "CHT out of bounds at step " + step + ": " + last.ChtCelsius);
                Assert.True(last.EffectiveAfr >= 7.5f && last.EffectiveAfr <= 18.0f,
                    "AFR out of bounds at step " + step + ": " + last.EffectiveAfr);
                Assert.True(last.VehicleSpeedMps >= 0f && last.VehicleSpeedMps <= 40f,
                    "Speed out of bounds at step " + step + ": " + last.VehicleSpeedMps);
                Assert.True(last.FuelLitersRemaining >= 0f && last.FuelLitersRemaining <= bike.Fuel.TankCapacityLiters + 0.001f,
                    "Fuel out of bounds at step " + step + ": " + last.FuelLitersRemaining);

                if (last.VehicleSpeedMps > maxSpeed) maxSpeed = last.VehicleSpeedMps;
                if (last.ChtCelsius > maxCht) maxCht = last.ChtCelsius;
                if (last.EffectiveAfr < minAfr) minAfr = last.EffectiveAfr;
                if (last.EffectiveAfr > maxAfr) maxAfr = last.EffectiveAfr;

                if (last.FuelLitersRemaining <= 0.85f)
                {
                    bike.Refuel(2.2f, ThermalModel.PremixFromRatio(50f), 0f);
                    refuelCount++;
                }
            }

            return new SoakRunSummary(
                last,
                bike.OdometerMeters,
                bike.TotalSimulatedSeconds,
                refuelCount,
                maxSpeed,
                maxCht,
                minAfr,
                maxAfr,
                fogSteps,
                tuckSteps,
                hillClimbSteps);
        }

        private static (float PeakSpeedMps, bool PowerbandActivated) MeasureFlatWotRun(BikeSimulation bike, uint seed, float seconds)
        {
            var rng = new SeededSimRandom(seed);
            var wot = new RideInputState(throttle: 1.0f, aeroTuck: true, roadSlopeRadians: 0f, surfaceGrip: 1.0f, surfaceRoughness: 0f);
            float peakSpeed = 0f;
            bool powerband = false;
            int steps = (int)(seconds / SoakDt);

            for (int i = 0; i < steps; i++)
            {
                EngineTelemetrySnapshot t = bike.Step(SoakDt, in wot, rng);
                AssertAllNumericFieldsFinite(in t);
                Assert.False(t.IsHardSeized, "Unexpected hard seize during WOT run at tick " + i);
                if (t.VehicleSpeedMps > peakSpeed)
                {
                    peakSpeed = t.VehicleSpeedMps;
                }
                if (t.IsPowerbandActive)
                {
                    powerband = true;
                }
            }

            return (peakSpeed, powerband);
        }

        private static string SnapshotOffers(SwapMeetMarket market)
        {
            return string.Join(
                "|",
                market.Offers.Select(o =>
                    o.OfferId + ":" + o.PartId + ":" + o.AskingPriceUsd + ":" + o.ListPriceUsd + ":" + BitConverter.SingleToInt32Bits(o.Condition)));
        }

        private static void AssertAllNumericFieldsFinite(in EngineTelemetrySnapshot t)
        {
            AssertFinite(t.Rpm, nameof(t.Rpm));
            AssertFinite(t.RearWheelTorqueNm, nameof(t.RearWheelTorqueNm));
            AssertFinite(t.EngineBrakeTorqueNm, nameof(t.EngineBrakeTorqueNm));
            AssertFinite(t.EffectiveAfr, nameof(t.EffectiveAfr));
            AssertFinite(t.ChtCelsius, nameof(t.ChtCelsius));
            AssertFinite(t.FuelLitersRemaining, nameof(t.FuelLitersRemaining));
            AssertFinite(t.ExhaustLeakFactor, nameof(t.ExhaustLeakFactor));
            AssertFinite(t.HeadlightOutput, nameof(t.HeadlightOutput));
            AssertFinite(t.ThrottlePosition, nameof(t.ThrottlePosition));
            AssertFinite(t.VehicleSpeedMps, nameof(t.VehicleSpeedMps));
            AssertFinite(t.VehicleSpeedMph, nameof(t.VehicleSpeedMph));
            AssertFinite(t.CrankcaseTempCelsius, nameof(t.CrankcaseTempCelsius));
            AssertFinite(t.DetonationRisk, nameof(t.DetonationRisk));
            AssertFinite(t.AirLeakSeverity, nameof(t.AirLeakSeverity));
            AssertFinite(t.MisfireFactor, nameof(t.MisfireFactor));
            AssertFinite(t.ClutchEngagement, nameof(t.ClutchEngagement));
            AssertFinite(t.VariatorRatio, nameof(t.VariatorRatio));
            AssertFinite(t.CompressionHealth, nameof(t.CompressionHealth));
            AssertFinite(t.RiderStamina, nameof(t.RiderStamina));
            AssertFinite(t.ChassisVibration, nameof(t.ChassisVibration));
            AssertFinite(t.PowerOutputWatts, nameof(t.PowerOutputWatts));
            AssertFinite(t.PlugColorIndex, nameof(t.PlugColorIndex));
        }

        private static void AssertFinite(float value, string fieldName)
        {
            Assert.False(float.IsNaN(value) || float.IsInfinity(value), fieldName + " was non-finite: " + value);
        }

        private static void AssertBitIdenticalTelemetry(in EngineTelemetrySnapshot a, in EngineTelemetrySnapshot b)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a.Rpm), BitConverter.SingleToInt32Bits(b.Rpm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.RearWheelTorqueNm), BitConverter.SingleToInt32Bits(b.RearWheelTorqueNm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.EngineBrakeTorqueNm), BitConverter.SingleToInt32Bits(b.EngineBrakeTorqueNm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.EffectiveAfr), BitConverter.SingleToInt32Bits(b.EffectiveAfr));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ChtCelsius), BitConverter.SingleToInt32Bits(b.ChtCelsius));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.FuelLitersRemaining), BitConverter.SingleToInt32Bits(b.FuelLitersRemaining));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ExhaustLeakFactor), BitConverter.SingleToInt32Bits(b.ExhaustLeakFactor));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.HeadlightOutput), BitConverter.SingleToInt32Bits(b.HeadlightOutput));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ThrottlePosition), BitConverter.SingleToInt32Bits(b.ThrottlePosition));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.VehicleSpeedMps), BitConverter.SingleToInt32Bits(b.VehicleSpeedMps));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.VehicleSpeedMph), BitConverter.SingleToInt32Bits(b.VehicleSpeedMph));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.CrankcaseTempCelsius), BitConverter.SingleToInt32Bits(b.CrankcaseTempCelsius));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.DetonationRisk), BitConverter.SingleToInt32Bits(b.DetonationRisk));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.AirLeakSeverity), BitConverter.SingleToInt32Bits(b.AirLeakSeverity));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.MisfireFactor), BitConverter.SingleToInt32Bits(b.MisfireFactor));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ClutchEngagement), BitConverter.SingleToInt32Bits(b.ClutchEngagement));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.VariatorRatio), BitConverter.SingleToInt32Bits(b.VariatorRatio));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.CompressionHealth), BitConverter.SingleToInt32Bits(b.CompressionHealth));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.RiderStamina), BitConverter.SingleToInt32Bits(b.RiderStamina));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ChassisVibration), BitConverter.SingleToInt32Bits(b.ChassisVibration));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.PowerOutputWatts), BitConverter.SingleToInt32Bits(b.PowerOutputWatts));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.PlugColorIndex), BitConverter.SingleToInt32Bits(b.PlugColorIndex));

            Assert.Equal(a.IsReserveActive, b.IsReserveActive);
            Assert.Equal(a.IsFourStroking, b.IsFourStroking);
            Assert.Equal(a.IsDetonating, b.IsDetonating);
            Assert.Equal(a.IsPowerbandActive, b.IsPowerbandActive);
            Assert.Equal(a.IsStalled, b.IsStalled);
            Assert.Equal(a.IsSeized, b.IsSeized);
            Assert.Equal(a.CurrentGear, b.CurrentGear);
            Assert.Equal(a.IsRunning, b.IsRunning);
            Assert.Equal(a.IsHardSeized, b.IsHardSeized);
            Assert.Equal(a.IsSoftSeized, b.IsSoftSeized);
            Assert.Equal(a.IsSputtering, b.IsSputtering);
            Assert.Equal(a.IsChainThrown, b.IsChainThrown);
            Assert.Equal(a.IsBeltSnapped, b.IsBeltSnapped);
            Assert.Equal(a.ThermalZone, b.ThermalZone);
        }
    }
}
