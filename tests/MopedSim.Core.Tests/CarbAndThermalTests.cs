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
    }
}
