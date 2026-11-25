using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class CarbAndThermalTests
    {
        [Fact]
        public void SeventyCcKit_LeanJet54_OverheatsDetonatesChalkWhite_AndSoftSeizes_ThenCoolsDown()
        {
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_54");
            var rng = new SeededSimRandom(101);
            var wot = new RideInputState(throttle: 1.0f);

            float peakCht = 0f;
            bool sawDetonation = false;
            bool sawSoftSeize = false;

            for (int i = 0; i < 60 * 120; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in wot, rng);
                if (snap.ChtCelsius > peakCht)
                {
                    peakCht = snap.ChtCelsius;
                }
                if (snap.IsDetonating)
                {
                    sawDetonation = true;
                }
                if (snap.IsSoftSeized)
                {
                    sawSoftSeize = true;
                    break;
                }
            }

            SparkPlugReading plug = bike.InspectSparkPlug();
            Assert.True(peakCht > 220f);
            Assert.True(sawDetonation);
            Assert.True(sawSoftSeize);
            Assert.False(bike.Thermal.IsHardSeized);
            Assert.Equal(PlugColorZone.ChalkWhite, plug.ColorZone);

            // Curb cool-down clears soft seize and lets engine restart with pedals
            bike.CoolDownSeconds(90f);
            Assert.False(bike.Thermal.IsSeized);

            var pedalStart = new RideInputState(throttle: 0.35f, pedalAssist: true);
            for (int i = 0; i < 90; i++)
            {
                bike.Step(1f / 60f, in pedalStart, rng);
            }
            Assert.True(bike.LatestTelemetry.IsRunning);
        }

        [Fact]
        public void SeventyCcKit_OptimalJet72_StaysBelow185C_WithChocolateBrownPlug()
        {
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_72");
            var rng = new SeededSimRandom(202);
            var wot = new RideInputState(throttle: 1.0f);

            float peakCht = 0f;
            bool anySeize = false;
            bool anyDetonation = false;

            for (int i = 0; i < 60 * 180; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in wot, rng);
                if (snap.ChtCelsius > peakCht)
                {
                    peakCht = snap.ChtCelsius;
                }
                if (snap.IsSeized)
                {
                    anySeize = true;
                }
                if (snap.IsDetonating && snap.ChtCelsius > 190f)
                {
                    anyDetonation = true;
                }
            }

            SparkPlugReading plug = bike.InspectSparkPlug();
            Assert.InRange(bike.LatestTelemetry.EffectiveAfr, 11.8f, 13.6f);
            Assert.True(peakCht < 185f);
            Assert.True(peakCht > 120f);
            Assert.False(anySeize);
            Assert.False(anyDetonation);
            Assert.Equal(PlugColorZone.ChocolateBrown, plug.ColorZone);
        }

        [Fact]
        public void SeventyCcKit_RichJet88_InducesFourStroking_AndWetBlackPlug()
        {
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_88");
            var rng = new SeededSimRandom(303);
            var wot = new RideInputState(throttle: 1.0f);

            for (int i = 0; i < 60 * 30; i++)
            {
                bike.Step(1f / 60f, in wot, rng);
            }

            SparkPlugReading plug = bike.InspectSparkPlug();
            Assert.True(bike.LatestTelemetry.EffectiveAfr < 11.2f);
            Assert.True(bike.LatestTelemetry.IsFourStroking);
            Assert.Equal(PlugColorZone.WetBlack, plug.ColorZone);
        }

        [Fact]
        public void StraightGas_ZeroToOnePremix_TriggersHardSeize_AndCannotRestartAfterCooling()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.Fuel.FuelLevelLiters = 0f;
            bike.Refuel(2.5f, ThermalModel.PremixFromRatio(0f));

            var rng = new SeededSimRandom(404);
            var cruise = new RideInputState(throttle: 0.90f);

            for (int i = 0; i < 60 * 60; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in cruise, rng);
                if (snap.IsHardSeized)
                {
                    break;
                }
            }

            Assert.True(bike.Thermal.IsHardSeized);
            Assert.True(bike.LatestTelemetry.IsSeized);
            Assert.True(bike.Thermal.CompressionHealth < 0.45f);
            Assert.True(bike.Faults.HasFault(FaultIds.HardSeize));

            // Cooling down does NOT rescue a hard-seized engine
            bike.CoolDownSeconds(300f);
            var pedal = new RideInputState(throttle: 0.5f, pedalAssist: true);
            for (int i = 0; i < 120; i++)
            {
                bike.Step(1f / 60f, in pedal, rng);
            }
            Assert.False(bike.LatestTelemetry.IsRunning);
            Assert.True(bike.LatestTelemetry.IsStalled);
        }

        [Fact]
        public void AmbientAirDensity_AndDirtyAirFilter_ModulateEffectiveAfr()
        {
            BikeSimulation bikeFog = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation bikeHill = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var rngA = new SeededSimRandom(505);
            var rngB = new SeededSimRandom(505);

            var fogInput = new RideInputState(throttle: 0.8f, ambientAirDensity: 1.12f);
            var hillInput = new RideInputState(throttle: 0.8f, ambientAirDensity: 0.88f);

            for (int i = 0; i < 120; i++)
            {
                bikeFog.Step(1f / 60f, in fogInput, rngA);
                bikeHill.Step(1f / 60f, in hillInput, rngB);
            }

            // Denser cold fog air produces leaner AFR than thin high-elevation air
            Assert.True(bikeFog.LatestTelemetry.EffectiveAfr > bikeHill.LatestTelemetry.EffectiveAfr);

            // Installing clogged air filter enriches mixture
            var catalog = TestFixtures.CreateCatalog();
            var clogged = new PartDefinition(
                "filter_dirty",
                "Clogged Dirty Filter",
                PartCategory.AirFilter,
                new[] { "Carb_Inlet" },
                0.2f,
                5m,
                new PartStats { AirFilterRestriction = 0.55f });
            Assert.True(bikeFog.TrySwapPart(SlotIds.AirFilter, new PartInstance(clogged)).Succeeded);
            Assert.True(catalog.ContainsKey("filter_stock_box"));

            float afrBefore = bikeFog.LatestTelemetry.EffectiveAfr;
            for (int i = 0; i < 30; i++)
            {
                bikeFog.Step(1f / 60f, in fogInput, rngA);
            }
            Assert.True(bikeFog.LatestTelemetry.EffectiveAfr < afrBefore);
        }
    
        [Fact]
        public void IdealMainJet_ScalesWithVenturiRelativeToDisplacement()
        {
            // 49cc on its matched 12mm venturi with a neutral pipe is the unscaled reference.
            Assert.Equal(EngineSimulation.BaseIdealMainJet, EngineSimulation.ComputeIdealMainJet(49f, 12f, 1.0f));

            float stock12 = EngineSimulation.ComputeIdealMainJet(49f, 12f, 0.92f);
            float stock15 = EngineSimulation.ComputeIdealMainJet(49f, 15f, 0.92f);
            float stock19 = EngineSimulation.ComputeIdealMainJet(49f, 19f, 0.92f);
            Assert.InRange(stock12, 57f, 60f);
            Assert.InRange(stock15, 66f, 73f); // 15mm SHA on a stock 49cc wants roughly #66-#70
            Assert.True(stock19 > stock15 && stock15 > stock12);

            // A 70cc kit's matched venturi is ~15mm, so a 15mm SHA with a performance pipe still wants ~#72.
            float kit15 = EngineSimulation.ComputeIdealMainJet(70f, 15f, 1.18f);
            Assert.InRange(kit15, 70.5f, 73.5f);

            // At a fixed 15mm venturi the bigger engine pulls a stronger signal, so it needs proportionally less jet per cc.
            Assert.True(kit15 / stock15 < System.MathF.Pow(70f / 49f, 0.42f));

            // Undersized venturis get no credit: a 70cc kit on the stock 12mm carb wants the same big jet as on its matched 15mm.
            float kit12 = EngineSimulation.ComputeIdealMainJet(70f, 12f, 1.18f);
            Assert.Equal(kit12, EngineSimulation.ComputeIdealMainJet(70f, 10f, 1.18f));
            Assert.InRange(kit12 / kit15, 0.999f, 1.0f);
        }

        [Fact]
        public void SeventyCcKit_OnStock12mmCarb_StockJetRunsLean_UntilUpJetted()
        {
            // Bolting a kit + pipe onto the stock 12mm Bing without re-jetting must still be the classic lean-seize trap.
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_60");
            var catalog = TestFixtures.CreateCatalog();
            Assert.True(bike.TrySwapPart(SlotIds.Carburetor, new PartInstance(catalog["carb_bing_12"])).Succeeded);

            var rng = new SeededSimRandom(616);
            var wot = new RideInputState(throttle: 1.0f);
            for (int i = 0; i < 120; i++)
            {
                bike.Step(1f / 60f, in wot, rng);
            }
            Assert.True(bike.LatestTelemetry.EffectiveAfr > 14.5f, "stock #60 on a kitted 12mm should be lean, got " + bike.LatestTelemetry.EffectiveAfr);
            Assert.True(bike.LatestTelemetry.IsDetonating);

            Assert.True(bike.TrySwapPart(SlotIds.MainJet, new PartInstance(catalog["jet_72"])).Succeeded);
            for (int i = 0; i < 120; i++)
            {
                bike.Step(1f / 60f, in wot, rng);
            }
            Assert.InRange(bike.LatestTelemetry.EffectiveAfr, 11.8f, 13.6f);
        }

        [Fact]
        public void StockFortyNineCc_On15mmCarb_WithVenturiSizedJet_RunsCleanForSixtySeconds()
        {
            // Same 49cc fixture bike swapped to a 15mm SHA with a #72 jet: the larger venturi's weaker signal needs the bigger
            // jet, so it runs near-optimal instead of fouling rich as it did when the ideal jet ignored venturi size.
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var catalog = TestFixtures.CreateCatalog();
            Assert.True(bike.TrySwapPart(SlotIds.Carburetor, new PartInstance(catalog["carb_sha_15"])).Succeeded);
            Assert.True(bike.TrySwapPart(SlotIds.MainJet, new PartInstance(catalog["jet_72"])).Succeeded);

            var rng = new SeededSimRandom(515);
            var cruise = new RideInputState(throttle: 0.6f);
            for (int i = 0; i < 60 * 60; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in cruise, rng);
                Assert.True(snap.IsRunning, "stalled at tick " + i);
                Assert.False(snap.IsFourStroking);
            }

            Assert.InRange(bike.LatestTelemetry.EffectiveAfr, 11.8f, 13.6f);
            Assert.True(bike.SparkPlug.FoulingLevel < 0.10f);
        }
}
}
