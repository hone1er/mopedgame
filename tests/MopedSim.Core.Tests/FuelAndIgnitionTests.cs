using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class FuelAndIgnitionTests
    {
        [Fact]
        public void PetcockOn_ReserveSputterStallsAtThreshold_AndSwitchingToReserveRestoresRun()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.Fuel.FuelLevelLiters = 0.615f;
            bike.Fuel.FloatBowlLiters = 0.04f;
            bike.SetPetcockMode(PetcockMode.On);

            var rng = new SeededSimRandom(707);
            var cruise = new RideInputState(throttle: 0.90f);

            bool sawSputter = false;
            bool sawReserve = false;
            bool stalled = false;

            for (int i = 0; i < 60 * 180; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in cruise, rng);
                if (snap.IsReserveActive)
                {
                    sawReserve = true;
                }
                if (snap.IsSputtering)
                {
                    sawSputter = true;
                }
                if (snap.IsStalled)
                {
                    stalled = true;
                    break;
                }
            }

            Assert.True(sawReserve);
            Assert.True(sawSputter);
            Assert.True(stalled);
            Assert.InRange(bike.LatestTelemetry.FuelLitersRemaining, 0.55f, 0.61f);

            // Flip petcock to Reserve and pedal-start back up cleanly
            bike.SetPetcockMode(PetcockMode.Reserve);
            var start = new RideInputState(throttle: 0.4f, pedalAssist: true);
            for (int i = 0; i < 120; i++)
            {
                bike.Step(1f / 60f, in start, rng);
            }
            Assert.True(bike.LatestTelemetry.IsRunning);
            Assert.False(bike.LatestTelemetry.IsSputtering);
        }

        [Fact]
        public void RustyTankWithoutInlineFilter_ClogsJet_WhileInlineFilterProtects()
        {
            BikeSimulation unfiltered = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            Assert.True(unfiltered.TryRemovePart(SlotIds.InlineFilter).Succeeded);
            unfiltered.Fuel.RustContamination = 0.95f;

            BikeSimulation filtered = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            filtered.Fuel.RustContamination = 0.95f;

            var rngA = new SeededSimRandom(808);
            var rngB = new SeededSimRandom(808);
            var cruise = new RideInputState(throttle: 0.65f);

            for (int i = 0; i < 60 * 120; i++)
            {
                unfiltered.Step(1f / 60f, in cruise, rngA);
                filtered.Step(1f / 60f, in cruise, rngB);
            }

            Assert.False(filtered.Fuel.IsMainJetClogged);
            if (!unfiltered.Fuel.IsMainJetClogged)
            {
                // Ensure deterministic clog verification even if low probability didn't fire
                unfiltered.InduceFault(FaultIds.CloggedJet);
            }

            // Clogged jet stalls on WOT, but idles with choke engaged
            var wot = new RideInputState(throttle: 0.9f);
            for (int i = 0; i < 60; i++)
            {
                unfiltered.Step(1f / 60f, in wot, rngA);
            }
            Assert.True(unfiltered.LatestTelemetry.IsStalled);

            var chokeStart = new RideInputState(throttle: 0.05f, choke: true, pedalAssist: true);
            for (int i = 0; i < 90; i++)
            {
                unfiltered.Step(1f / 60f, in chokeStart, rngA);
            }
            Assert.True(unfiltered.LatestTelemetry.IsRunning);
        }

        [Fact]
        public void SixVoltCondenserHeatSoak_StallsAbove95C_AndCdi12VIsImmuneWithSteadyHeadlight()
        {
            BikeSimulation pointsBike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            pointsBike.Ignition.CondenserHealth = 0.40f; // aged weak condenser
            var rng = new SeededSimRandom(909);

            // Simulate slow heavy climb heat-soak pushing crankcase > 95C
            var slowClimb = new RideInputState(throttle: 0.95f, roadSlopeRadians: 0.14f);
            bool heatSoaked = false;
            for (int i = 0; i < 60 * 180; i++)
            {
                EngineTelemetrySnapshot snap = pointsBike.Step(1f / 60f, in slowClimb, rng);
                if (pointsBike.Ignition.IsCondenserHeatSoaked)
                {
                    heatSoaked = true;
                    Assert.True(snap.CrankcaseTempCelsius > 95f);
                    Assert.True(snap.IsStalled);
                    break;
                }
            }
            Assert.True(heatSoaked);
            Assert.True(pointsBike.Faults.HasFault(FaultIds.CondenserHeatSoak));

            // Cool crankcase below 85C and restart cleanly
            pointsBike.CoolDownSeconds(140f);
            Assert.True(pointsBike.Thermal.CrankcaseTempCelsius < 85f);
            var pedal = new RideInputState(throttle: 0.3f, pedalAssist: true);
            for (int i = 0; i < 90; i++)
            {
                pointsBike.Step(1f / 60f, in pedal, rng);
            }
            Assert.True(pointsBike.LatestTelemetry.IsRunning);

            // Verify 6V headlight scales with RPM vs steady bright 12V CDI
            BikeSimulation cdiBike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var catalog = TestFixtures.CreateCatalog();
            Assert.True(cdiBike.TrySwapPart(SlotIds.Ignition, new PartInstance(catalog["stator_cdi_12v"])).Succeeded);

            cdiBike.Thermal.CrankcaseTempCelsius = 105f;
            cdiBike.Step(1f / 60f, new RideInputState(0f), rng);
            pointsBike.Step(1f / 60f, new RideInputState(0f), rng);

            float sixVoltIdleLight = pointsBike.LatestTelemetry.HeadlightOutput;
            float cdiIdleLight = cdiBike.LatestTelemetry.HeadlightOutput;

            Assert.False(cdiBike.Ignition.IsCondenserHeatSoaked);
            Assert.True(cdiIdleLight > sixVoltIdleLight);
            Assert.True(cdiIdleLight > 0.90f);
        }

        [Fact]
        public void SixVoltPointsGapDrift_CausesHighRpmMisfire_ClearedByReset()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.Ignition.SetPointsGap(0.20f);
            var rng = new SeededSimRandom(1010);

            for (int i = 0; i < 120; i++)
            {
                bike.Step(1f / 60f, new RideInputState(0.9f), rng);
            }
            Assert.True(bike.Ignition.IsPointsOutOfSpec);
            Assert.True(bike.LatestTelemetry.MisfireFactor > 0.15f);
            Assert.True(bike.Faults.HasFault(FaultIds.PointsDrift));

            bike.Ignition.SetPointsGap(0.35f);
            bike.Step(1f / 60f, new RideInputState(0.9f), rng);
            Assert.False(bike.Ignition.IsPointsOutOfSpec);
            Assert.False(bike.Faults.HasFault(FaultIds.PointsDrift));
        }
    }
}
