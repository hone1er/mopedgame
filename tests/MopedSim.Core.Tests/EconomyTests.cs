using System;
using System.Collections.Generic;
using System.Linq;
using MopedSim.Core;
using Xunit;

namespace MopedSim.Core.Tests
{
    public sealed class CargoAndJobSessionTests
    {
        private static readonly PartDefinition WireRack = new PartDefinition(
            "rack_test_wire", "Test Wire Rack", PartCategory.CargoRack, new[] { "Rear_Rack_Mount" }, 0.8f, 25m,
            new PartStats { CargoSlots = 1, CargoMaxKg = 8f });

        private static readonly PartDefinition Crate = new PartDefinition(
            "rack_test_crate", "Test Milk Crate", PartCategory.CargoRack, new[] { "Rear_Rack_Mount" }, 1.85f, 35m,
            new PartStats { CargoSlots = 4, CargoMaxKg = 20f });

        internal static EngineTelemetrySnapshot Telemetry(float speedMps, bool seized = false, bool hardSeized = false)
        {
            return new EngineTelemetrySnapshot(
                5000f, 10f, 0f, 14f, 140f, 2f, false, false, false, false, false, seized, 0f, 0.7f, 1,
                vehicleSpeedMps: speedMps,
                isRunning: !seized,
                isHardSeized: hardSeized,
                isSoftSeized: seized && !hardSeized);
        }

        internal static JobDefinition CourierJob(int slots = 3, float kg = 7.5f, float timeLimit = 120f)
        {
            return new JobDefinition
            {
                Id = "gig_test_courier",
                DisplayName = "Test Courier",
                JobType = JobType.CourierGig,
                ZoneId = "Zone_WestOakland",
                TargetZoneId = "Zone_WestOakland",
                RequiredCargoSlots = slots,
                RequiredCargoKg = kg,
                RewardUsd = 80m,
                BonusRewardUsd = 20m,
                ReputationReward = 10,
                TimeLimitSeconds = timeLimit,
                Checkpoints = new List<JobCheckpointSpec>
                {
                    new JobCheckpointSpec("cp_a", 1, 0f, 0f, 0f, 8f),
                    new JobCheckpointSpec("cp_b", 2, 300f, 0f, 400f, 8f, 3f),
                    new JobCheckpointSpec("cp_c", 3, 300f, 0f, 1000f, 8f, 6f),
                },
            };
        }

        private static BikeSimulation BikeWithRack(PartDefinition? rack)
        {
            BikeSimulation bike = TestFixtures.CreateStockBike();
            if (rack != null)
            {
                Assert.True(bike.TryInstallPart(SlotIds.CargoRack, new PartInstance(rack)).Succeeded);
            }
            return bike;
        }

        [Fact]
        public void Cargo_UnmountedRack_IsZero_MountedRackReadsCatalogStats()
        {
            CargoCapacity none = CargoCapacityCalculator.Compute(BikeWithRack(null));
            Assert.False(none.HasRack);
            Assert.Equal(0, none.Slots);
            Assert.Equal(0f, none.MaxKg);
            Assert.Equal(string.Empty, none.RackPartId);

            CargoCapacity crate = CargoCapacityCalculator.Compute(BikeWithRack(Crate));
            Assert.True(crate.HasRack);
            Assert.Equal(4, crate.Slots);
            Assert.Equal(20f, crate.MaxKg);
            Assert.Equal("rack_test_crate", crate.RackPartId);
            Assert.True(crate.IsSecured);
        }

        [Fact]
        public void TryAcceptJob_RejectsShortSlotsShortKgHardSeizeAndInvalid()
        {
            var mgr = new JobSessionManager();

            JobAcceptResult noRack = mgr.TryAcceptJob(CourierJob(), BikeWithRack(null));
            Assert.False(noRack.Accepted);
            Assert.Equal(JobRejectReason.InsufficientCargoSlots, noRack.Reason);

            JobAcceptResult wire = mgr.TryAcceptJob(CourierJob(), BikeWithRack(WireRack));
            Assert.Equal(JobRejectReason.InsufficientCargoSlots, wire.Reason);
            Assert.Equal(1, wire.Capacity.Slots);
            Assert.Equal(3, wire.RequiredCargoSlots);

            JobAcceptResult heavy = mgr.TryAcceptJob(CourierJob(slots: 1, kg: 10f), BikeWithRack(WireRack));
            Assert.Equal(JobRejectReason.InsufficientCargoKg, heavy.Reason);

            BikeSimulation seized = BikeWithRack(Crate);
            seized.InduceFault(FaultIds.HardSeize);
            Assert.Equal(JobRejectReason.BikeHardSeized, mgr.TryAcceptJob(CourierJob(), seized).Reason);

            JobDefinition noRoute = CourierJob();
            noRoute.Checkpoints.Clear();
            Assert.Equal(JobRejectReason.InvalidJob, mgr.TryAcceptJob(noRoute, BikeWithRack(Crate)).Reason);

            Assert.Equal(JobSessionStatus.Idle, mgr.Status);
            Assert.Null(mgr.ActiveJob);
        }

        [Fact]
        public void CourierGig_FullDelivery_PaysScaledCashOnceAndTracksProgress()
        {
            var mgr = new JobSessionManager();
            var world = new SimulationWorld(new SeededSimRandom(1u)) { CashUsd = 0m };
            JobDefinition job = CourierJob();

            JobAcceptResult ok = mgr.TryAcceptJob(job, BikeWithRack(Crate));
            Assert.True(ok.Accepted, ok.Message);
            Assert.Equal(JobRejectReason.JobAlreadyActive, mgr.TryAcceptJob(job, BikeWithRack(Crate)).Reason);
            Assert.Equal(1100f, mgr.RouteLengthMeters, 2);

            // Progress is capped at the next unreached checkpoint (the start).
            mgr.Step(1f, Telemetry(12f));
            Assert.Equal(0f, mgr.RouteProgressMeters);
            Assert.Equal(CheckpointReportResult.OutOfRange, mgr.ReportPosition(50f, 0f, 50f));
            Assert.Equal(CheckpointReportResult.Reached, mgr.ReportPosition(3f, 0f, 4f));
            Assert.Equal(CheckpointReportResult.WrongCheckpoint, mgr.ReportCheckpointReached("cp_c"));

            for (int i = 0; i < 60; i++)
            {
                mgr.Step(1f, Telemetry(12f), surfaceRoughness: 0f);
            }
            Assert.Equal(500f, mgr.RouteProgressMeters, 2);
            mgr.ReportPothole(0.5f);
            Assert.Equal(0.94f, mgr.CargoIntegrity, 4);

            Assert.Equal(CheckpointReportResult.Reached, mgr.ReportCheckpointReached("cp_b"));
            JobSessionSnapshot mid = mgr.GetSnapshot();
            Assert.Equal("cp_c", mid.NextCheckpointId);
            Assert.Equal(2, mid.CheckpointsReached);
            Assert.Equal(3, mid.CheckpointCount);
            Assert.Equal(JobSessionStatus.Active, mid.Status);

            Assert.Equal(CheckpointReportResult.JobCompleted, mgr.ReportPosition(300f, 0f, 995f));
            Assert.Equal(JobSessionStatus.Completed, mgr.Status);

            CompletedGigRecord rec = mgr.LastCompletedRecord!;
            Assert.True(rec.OnTime);
            Assert.Equal(75.20m, rec.BasePayoutUsd);   // 80 * 0.94
            Assert.Equal(18.80m, rec.BonusPayoutUsd);  // 20 * 0.94
            Assert.Equal(94.00m, rec.TotalPayoutUsd);
            Assert.Equal(10, rec.ReputationEarned);

            Assert.True(mgr.TryCollectReward(world));
            Assert.False(mgr.TryCollectReward(world));
            Assert.Equal(94.00m, world.CashUsd);
            Assert.Equal(10, world.Reputation);
            Assert.True(mgr.GetSnapshot().RewardCollected);
        }

        [Fact]
        public void CourierGig_LateDelivery_LosesBonusAndHalfReputation()
        {
            var mgr = new JobSessionManager();
            Assert.True(mgr.TryAcceptJob(CourierJob(timeLimit: 30f), BikeWithRack(Crate)).Accepted);
            mgr.ReportCheckpointReached("cp_a");
            mgr.Step(40f);
            mgr.ReportCheckpointReached("cp_b");
            Assert.Equal(CheckpointReportResult.JobCompleted, mgr.ReportCheckpointReached("cp_c"));

            CompletedGigRecord rec = mgr.LastCompletedRecord!;
            Assert.False(rec.OnTime);
            Assert.Equal(80m, rec.BasePayoutUsd);
            Assert.Equal(0m, rec.BonusPayoutUsd);
            Assert.Equal(5, rec.ReputationEarned);
        }

        [Fact]
        public void ActiveJob_FailsOnOvertimeCargoDestructionAndHardSeize()
        {
            var overtime = new JobSessionManager();
            overtime.TryAcceptJob(CourierJob(timeLimit: 30f), BikeWithRack(Crate));
            overtime.Step(100f);
            Assert.Equal(JobSessionStatus.Active, overtime.Status); // clock waits for the start checkpoint
            Assert.Equal(0f, overtime.ElapsedSeconds);
            overtime.ReportCheckpointReached("cp_a");
            overtime.Step(46f);
            Assert.Equal(JobSessionStatus.Failed, overtime.Status);
            Assert.Equal(JobFailReason.TimeExpired, overtime.FailReason);
            Assert.Equal(CheckpointReportResult.NotActive, overtime.ReportCheckpointReached("cp_a"));

            var crash = new JobSessionManager();
            crash.TryAcceptJob(CourierJob(), BikeWithRack(Crate));
            crash.ReportCrash();
            Assert.Equal(1f, crash.CargoIntegrity); // cargo not picked up yet
            crash.ReportCheckpointReached("cp_a");
            crash.ReportCrash();
            crash.ReportCrash();
            Assert.Equal(JobSessionStatus.Active, crash.Status);
            crash.ReportCrash();
            Assert.Equal(JobSessionStatus.Failed, crash.Status);
            Assert.Equal(JobFailReason.CargoDestroyed, crash.FailReason);
            Assert.False(crash.HasUncollectedReward);

            var seize = new JobSessionManager();
            seize.TryAcceptJob(CourierJob(), BikeWithRack(Crate));
            seize.Step(0.1f, Telemetry(5f, seized: true, hardSeized: true));
            Assert.Equal(JobFailReason.BikeHardSeized, seize.FailReason);

            // After a failure a new job can be accepted.
            Assert.True(seize.TryAcceptJob(CourierJob(), BikeWithRack(Crate)).Accepted);
            Assert.True(seize.Abandon());
            Assert.Equal(JobSessionStatus.Abandoned, seize.Status);
        }
    }

    public sealed class RallyPackSimulatorTests
    {
        internal static JobDefinition RallyJob()
        {
            return new JobDefinition
            {
                Id = "rally_test_swarm",
                DisplayName = "Test Swarm",
                JobType = JobType.BaySwarmRally,
                RewardUsd = 200m,
                BonusRewardUsd = 100m,
                ReputationReward = 60,
                TimeLimitSeconds = 600f,
                Checkpoints = new List<JobCheckpointSpec>
                {
                    new JobCheckpointSpec("cp_start", 1, 0f, 0f, 0f, 10f, 0f),
                    new JobCheckpointSpec("cp_climb", 2, 0f, 0f, 1200f, 10f, 9f),
                    new JobCheckpointSpec("cp_finish", 3, 1500f, 0f, 1200f, 10f, -2f),
                },
            };
        }

        private static string Trace(uint seed, float playerSpeed, bool seizeMidway)
        {
            var sim = new RallyPackSimulator(RallyJob(), new SeededSimRandom(seed));
            float player = 0f;
            for (int tick = 0; tick < 6000 && !sim.PlayerFinished; tick++)
            {
                player += playerSpeed * 0.1f;
                sim.Step(0.1f, player, seizeMidway && tick == 1000);
            }
            return string.Join("|", sim.Riders.Select(r =>
                r.RiderId + ":" + r.ProgressMeters.ToString("R") + ":" + r.CurrentSpeedMps.ToString("R") + ":" + r.IsFinished + ":"
                + r.FinishTimeSeconds.ToString("R") + ":" + r.IsDnf))
                + "#" + sim.GetPlayerPlacement() + ":" + sim.IsPackSurvivalBonusEarned + ":" + sim.PlayerFinishTimeSeconds.ToString("R");
        }

        [Fact]
        public void SameSeedAndInputs_AreBitIdentical_DifferentSeedsDiverge()
        {
            Assert.Equal(Trace(4242u, 11f, false), Trace(4242u, 11f, false));
            Assert.NotEqual(Trace(4242u, 11f, false), Trace(4243u, 11f, false));
        }

        [Fact]
        public void PackTracksRidersPlacementAndSurvival()
        {
            var sim = new RallyPackSimulator(RallyJob(), new SeededSimRandom(7u));
            Assert.Equal(8, sim.FieldSize);
            Assert.Equal(7, sim.Riders.Count);
            Assert.Equal(new[] { 0f, 1200f, 2700f }, sim.CheckpointDistancesMeters.ToArray());
            Assert.All(sim.Riders, r => Assert.InRange(r.CruiseSpeedMps, 9f, 12.5f));
            Assert.Equal(7, sim.Riders.Select(r => r.RiderId).Distinct().Count());

            // A slow player (5 m/s) falls off the back and places last among finishers.
            float player = 0f;
            for (int tick = 0; tick < 2000; tick++)
            {
                player += 5f * 0.5f;
                sim.Step(0.5f, player, false);
            }
            Assert.True(sim.PlayerFinished);
            int dnf = sim.Riders.Count(r => r.IsDnf);
            Assert.Equal(sim.FieldSize - dnf, sim.GetPlayerPlacement());
            Assert.True(sim.SecondsOffTheBack > 0f);
            Assert.True(sim.MaxGapBehindPackMeters > sim.Config.FallOffBackGapMeters);
            Assert.True(sim.IsPackSurvivalBonusEarned);
            Assert.All(sim.Riders.Where(r => !r.IsDnf), r => Assert.True(r.IsFinished && r.CheckpointsPassed == 3));

            // A fast player wins; seizing forfeits the survival bonus.
            var fast = new RallyPackSimulator(RallyJob(), new SeededSimRandom(7u));
            float p2 = 0f;
            for (int tick = 0; tick < 400 && !fast.PlayerFinished; tick++)
            {
                p2 += 20f * 0.5f;
                fast.Step(0.5f, p2, tick == 10);
            }
            Assert.Equal(1, fast.GetPlayerPlacement());
            Assert.True(fast.PlayerEverSeized);
            Assert.False(fast.IsPackSurvivalBonusEarned);
        }

        [Fact]
        public void RallyJob_ThroughSessionManager_RecordsPlacementAndSurvivalBonus()
        {
            var mgr = new JobSessionManager();
            JobAcceptResult ok = mgr.TryAcceptJob(RallyJob(), TestFixtures.CreateStockBike(), new SeededSimRandom(99u));
            Assert.True(ok.Accepted, ok.Message);
            Assert.NotNull(mgr.Rally);
            Assert.Equal(8, mgr.GetSnapshot().RallyFieldSize);

            EngineTelemetrySnapshot fast = CargoAndJobSessionTests.Telemetry(16f);

            // Riding to the start line: the pack and clock hold.
            for (int i = 0; i < 60; i++)
            {
                mgr.Step(0.5f, fast);
            }
            Assert.False(mgr.IsRouteStarted);
            Assert.Equal(0f, mgr.ElapsedSeconds);
            Assert.Equal(0f, mgr.Rally!.ElapsedSeconds);
            Assert.All(mgr.Rally.Riders, r => Assert.Equal(0f, r.ProgressMeters));

            mgr.ReportCheckpointReached("cp_start");
            Assert.True(mgr.IsRouteStarted);
            while (mgr.RouteProgressMeters < 1200f)
            {
                mgr.Step(0.5f, fast);
            }
            Assert.Equal(CheckpointReportResult.Reached, mgr.ReportCheckpointReached("cp_climb"));
            while (mgr.RouteProgressMeters < 2700f)
            {
                mgr.Step(0.5f, fast);
            }
            Assert.False(mgr.Rally.PlayerFinished);
            Assert.Equal(mgr.ElapsedSeconds, mgr.Rally.ElapsedSeconds);
            Assert.Equal(CheckpointReportResult.JobCompleted, mgr.ReportCheckpointReached("cp_finish"));

            CompletedGigRecord rec = mgr.LastCompletedRecord!;
            Assert.Equal(JobType.BaySwarmRally, rec.JobType);
            Assert.Equal(8, rec.FieldSize);
            Assert.InRange(rec.Placement, 1, 8);
            Assert.Equal(mgr.Rally.PlayerFinalPlacement, rec.Placement);
            Assert.True(rec.PackSurvivalBonusEarned);
            Assert.Equal(50m, rec.SurvivalBonusUsd);
            Assert.Equal(200m, rec.BasePayoutUsd);
            Assert.Equal(Math.Round(100m * (8 - rec.Placement) / 7m, 2, MidpointRounding.AwayFromZero), rec.BonusPayoutUsd);
            Assert.Equal(60, rec.ReputationEarned);
        }

        [Fact]
        public void RallyJob_DefaultRandom_IsSeededFromStableJobIdHash()
        {
            Assert.Equal(StableHash.Fnv1a("rally_test_swarm"), StableHash.Fnv1a("rally_test_swarm"));
            Assert.Equal(2166136261u, StableHash.Fnv1a(string.Empty));

            var a = new JobSessionManager();
            var b = new JobSessionManager();
            a.TryAcceptJob(RallyJob(), TestFixtures.CreateStockBike());
            b.TryAcceptJob(RallyJob(), TestFixtures.CreateStockBike());
            Assert.Equal(a.Rally!.Riders.Select(r => r.CruiseSpeedMps), b.Rally!.Riders.Select(r => r.CruiseSpeedMps));
        }
    }

    public sealed class SwapMeetMarketTests
    {
        private static List<PartDefinition> Catalog()
        {
            return new List<PartDefinition>
            {
                new PartDefinition("tank_test", "Test Tank", PartCategory.FuelTank, new[] { "Frame_Tank" }, 2.2f, 70m),
                new PartDefinition("stator_test_points", "Test Points Stator", PartCategory.Ignition, new[] { "Moped_Stator" }, 0.9f, 50m,
                    new PartStats { IgnitionType = IgnitionType.Points6V }),
                new PartDefinition("stator_test_cdi", "Test CDI", PartCategory.Ignition, new[] { "Moped_Stator" }, 0.9f, 160m,
                    new PartStats { IgnitionType = IgnitionType.Cdi12V }),
                new PartDefinition("piston_test", "Test Piston", PartCategory.Piston, new[] { "Moped_Piston_50" }, 0.3f, 45m),
                new PartDefinition("belt_test", "Test Belt", PartCategory.DriveBelt, new[] { "Belt" }, 0.3f, 30m),
                new PartDefinition("pipe_test", "Test Pipe", PartCategory.Exhaust, new[] { "Flange" }, 2.4f, 135m),
                new PartDefinition("bars_test", "Test Bars", PartCategory.Handlebars, new[] { "Clamp" }, 1.0f, 55m),
                new PartDefinition("wrench_test", "Test Wrench", PartCategory.ToolRollItem, new[] { "Tool" }, 0.2f, 12m,
                    new PartStats { IsTool = true }),
                new PartDefinition("free_test", "Free Sticker", PartCategory.Headlight, new[] { "x" }, 0.01f, 0m),
            };
        }

        private static string Describe(SwapMeetMarket m)
        {
            return string.Join("|", m.Offers.Select(o => o.OfferId + ":" + o.AskingPriceUsd + ":" + o.Condition.ToString("R")));
        }

        [Fact]
        public void Rotation_IsDeterministicPerSeedAndIndex_AndSkipsToolsAndFreeParts()
        {
            var a = new SwapMeetMarket(Catalog(), 2026u, new SwapMeetConfig { OffersPerRotation = 4 });
            var b = new SwapMeetMarket(Catalog(), 2026u, new SwapMeetConfig { OffersPerRotation = 4 });
            Assert.Equal(4, a.Offers.Count);
            Assert.Equal(Describe(a), Describe(b));

            string r0 = Describe(a);
            a.AdvanceRotation();
            Assert.Equal(1, a.RotationIndex);
            b.Rotate(1);
            Assert.Equal(Describe(a), Describe(b));
            a.Rotate(0);
            Assert.Equal(r0, Describe(a));

            var all = new SwapMeetMarket(Catalog(), 1u, new SwapMeetConfig { OffersPerRotation = 50 });
            Assert.Equal(7, all.Offers.Count);
            Assert.DoesNotContain(all.Offers, o => o.PartId == "wrench_test" || o.PartId == "free_test");
            Assert.All(all.Offers, o =>
            {
                Assert.True(o.AskingPriceUsd < o.ListPriceUsd);
                Assert.True(o.AskingPriceUsd >= o.ListPriceUsd * 0.35m - 0.01m);
                Assert.InRange(o.Condition, 0.6f, 0.92f);
            });
        }

        [Fact]
        public void HiddenDefect_IsRevealedByInspection_AndAppliedOnPurchase()
        {
            var market = new SwapMeetMarket(Catalog(), 77u, new SwapMeetConfig { OffersPerRotation = 50, DefectChance = 1f });
            var world = new SimulationWorld(new SeededSimRandom(1u)) { CashUsd = 30m };

            SwapMeetOffer tank = market.Offers.Single(o => o.PartId == "tank_test");
            Assert.False(tank.IsInspected);
            Assert.Equal(SwapMeetDefect.None, tank.RevealedDefect);

            SwapMeetInspection inspection = market.InspectOffer(tank.OfferId);
            Assert.True(inspection.Succeeded);
            Assert.Equal(SwapMeetDefect.RustyTank, inspection.Defect);
            Assert.Equal(SwapMeetDefect.RustyTank, tank.RevealedDefect);
            Assert.Contains("rust", inspection.Description);

            Assert.Equal(SwapMeetDefect.WeakCondenser, market.InspectOffer(market.Offers.Single(o => o.PartId == "stator_test_points").OfferId).Defect);
            Assert.Equal(SwapMeetDefect.None, market.InspectOffer(market.Offers.Single(o => o.PartId == "stator_test_cdi").OfferId).Defect);
            Assert.Equal(SwapMeetDefect.WornBelt, market.InspectOffer(market.Offers.Single(o => o.PartId == "belt_test").OfferId).Defect);
            Assert.Equal(SwapMeetDefect.StrippedThread, market.InspectOffer(market.Offers.Single(o => o.PartId == "pipe_test").OfferId).Defect);
            Assert.Equal(SwapMeetDefect.None, market.InspectOffer(market.Offers.Single(o => o.PartId == "bars_test").OfferId).Defect);

            // Too poor first, then pay up.
            if (world.CashUsd >= tank.AskingPriceUsd)
            {
                world.CashUsd = tank.AskingPriceUsd - 0.01m;
            }
            SwapMeetPurchaseResult broke = market.TryBuyOffer(tank.OfferId, world);
            Assert.False(broke.Succeeded);
            Assert.Equal(0, world.Inventory.Count);

            world.CashUsd = 100m;
            SwapMeetPurchaseResult bought = market.TryBuyOffer(tank.OfferId, world);
            Assert.True(bought.Succeeded, bought.Message);
            Assert.True(bought.DefectWasRevealedBeforePurchase);
            Assert.Equal(SwapMeetDefect.RustyTank, bought.Defect);
            Assert.Equal(100m - tank.AskingPriceUsd, world.CashUsd);
            Assert.Equal(world.CashUsd, bought.RemainingCashUsd);
            Assert.Equal(1, world.Inventory.CountOf("tank_test"));
            PartInstance owned = world.Inventory.FindFirstByPartId("tank_test")!;
            Assert.Same(bought.Part, owned);
            Assert.InRange(owned.RustContamination, 0.45f, 0.9f);
            Assert.True(tank.IsSold);
            Assert.False(market.TryBuyOffer(tank.OfferId, world).Succeeded);

            // Worn rings push the ring end-gap out of the piston's spec.
            SwapMeetOffer piston = market.Offers.Single(o => o.PartId == "piston_test");
            SwapMeetPurchaseResult blind = market.TryBuyOffer(piston.OfferId, world);
            Assert.True(blind.Succeeded);
            Assert.False(blind.DefectWasRevealedBeforePurchase);
            Assert.Equal(SwapMeetDefect.WornRings, blind.Defect);
            Assert.True(blind.Part!.RingGapMm > blind.Part.Definition.Stats.OptimalRingGapMaxMm);
        }

        [Fact]
        public void PossibleDefects_FollowPartCategory()
        {
            List<PartDefinition> parts = Catalog();
            Assert.Equal(new[] { SwapMeetDefect.RustyTank }, SwapMeetMarket.GetPossibleDefects(parts[0]));
            Assert.Empty(SwapMeetMarket.GetPossibleDefects(parts[2]));
            Assert.Empty(SwapMeetMarket.GetPossibleDefects(parts[6]));
        }
    }

    public sealed class CustomizationAndInventoryTests
    {
        [Fact]
        public void Customization_ValidatesPaintClampsPatinaAndBoundsDecal()
        {
            var c = new BikeCustomizationState();
            Assert.Equal(BikeCustomizationState.DefaultPaintHex, c.PaintHex);
            Assert.Equal(0f, c.PatinaLevel);
            Assert.Null(c.DecalStyle);

            Assert.False(c.TrySetPaintHex("red"));
            Assert.False(c.TrySetPaintHex("#12345G"));
            Assert.True(c.TrySetPaintHex("#3a7d44"));
            Assert.Equal("#3a7d44", c.PaintHex);

            c.SetPatinaLevel(1.7f);
            Assert.Equal(1f, c.PatinaLevel);
            c.SetPatinaLevel(float.NaN);
            Assert.Equal(0f, c.PatinaLevel);
            c.Weather(10f, 0.02f);
            Assert.Equal(0.2f, c.PatinaLevel, 5);
            Assert.True(c.TryRepaint("#FFFFFF"));
            Assert.Equal(0f, c.PatinaLevel);

            Assert.True(c.TrySetDecalStyle("checker_stripe"));
            Assert.False(c.TrySetDecalStyle(new string('x', 65)));
            Assert.Equal("checker_stripe", c.DecalStyle);
            Assert.True(c.TrySetDecalStyle(string.Empty));
            Assert.Null(c.DecalStyle);

            Assert.Throws<ArgumentException>(() => new BikeCustomizationState("blue"));
            var copy = new BikeCustomizationState("#112233", 0.4f, "flames").Clone();
            Assert.Equal("#112233", copy.PaintHex);
            Assert.Equal(0.4f, copy.PatinaLevel);
            Assert.Equal("flames", copy.DecalStyle);
        }

        [Fact]
        public void World_PurchaseIntoInventory_AndInventoryIdsAreUnique()
        {
            var world = new SimulationWorld(new SeededSimRandom(1u)) { CashUsd = 50m };
            var bars = new PartDefinition("bars_x", "Bars", PartCategory.Handlebars, new[] { "Clamp" }, 1f, 30m);
            PurchaseResult ok = world.TryPurchasePartIntoInventory(bars, out PartInstance? first);
            Assert.True(ok.Succeeded);
            Assert.NotNull(first);
            Assert.Equal(20m, world.CashUsd);
            PurchaseResult broke = world.TryPurchasePartIntoInventory(bars, out PartInstance? none);
            Assert.False(broke.Succeeded);
            Assert.Null(none);

            world.Inventory.Add(new PartInstance(bars, first!.InstanceId));
            Assert.Equal(2, world.Inventory.CountOf("bars_x"));
            Assert.Equal(2, world.Inventory.Items.Select(p => p.InstanceId).Distinct().Count());
            Assert.Same(first, world.Inventory.TakeFirstByPartId("bars_x"));
            Assert.Equal(1, world.Inventory.Count);
        }
    }
}
