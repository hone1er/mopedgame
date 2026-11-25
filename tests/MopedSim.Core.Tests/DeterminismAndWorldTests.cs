using System;
using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class DeterminismAndWorldTests
    {
        [Fact]
        public void ThirtySixThousandTicks_TenMinutes_AreBitIdentical()
        {
            BikeSimulation simA = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation simB = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var rngA = new SeededSimRandom(987654u);
            var rngB = new SeededSimRandom(987654u);

            const float dt = 1f / 60f;
            EngineTelemetrySnapshot lastA = default;
            EngineTelemetrySnapshot lastB = default;

            for (int tick = 0; tick < 36000; tick++)
            {
                float throttle = (tick % 1200) < 900 ? 0.85f : 0.10f;
                bool tuck = (tick % 600) > 300;
                float roughness = (tick % 400) < 40 ? 0.65f : 0.05f;
                float slope = (tick % 2400) > 1800 ? 0.08f : 0f;

                var input = new RideInputState(
                    throttle: throttle,
                    aeroTuck: tuck,
                    roadSlopeRadians: slope,
                    surfaceRoughness: roughness,
                    ambientAirDensity: 1.02f);

                lastA = simA.Step(dt, in input, rngA);
                lastB = simB.Step(dt, in input, rngB);

                if (tick % 6000 == 5999)
                {
                    AssertBitIdentical(lastA, lastB);
                }
            }

            AssertBitIdentical(lastA, lastB);
            Assert.Equal(
                BitConverter.SingleToInt32Bits(simA.OdometerMeters),
                BitConverter.SingleToInt32Bits(simB.OdometerMeters));
        }

        [Fact]
        public void SeededSimRandom_IsDeterministic_AndValidatesBounds()
        {
            var r1 = new SeededSimRandom(42);
            var r2 = new SeededSimRandom(42);
            var r3 = new SeededSimRandom(99);

            for (int i = 0; i < 64; i++)
            {
                Assert.Equal(r1.NextUInt64(), r2.NextUInt64());
                int n = r1.NextInt(10, 20);
                int m = r2.NextInt(10, 20);
                Assert.Equal(n, m);
                Assert.InRange(n, 10, 19);
            }

            Assert.NotEqual(r1.NextUInt64(), r3.NextUInt64());
            Assert.Equal(5, r1.NextInt(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => r1.NextInt(10, 5));
        }

        [Fact]
        public void SimulationWorld_ToolRollCapacityExpandsWithMilkCrate_AndTracksTowCash()
        {
            var world = new SimulationWorld(new SeededSimRandom(7));
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            world.RegisterBike("bike_1", bike);

            Assert.Equal(6, world.ToolRollCapacity);
            for (int i = 0; i < 6; i++)
            {
                Assert.True(world.TryPackToolRollItem("tool_" + i));
            }
            Assert.False(world.TryPackToolRollItem("overflow_item"));

            var catalog = TestFixtures.CreateCatalog();
            WrenchActionResult rackRes = bike.TryInstallPart(
                SlotIds.CargoRack,
                new PartInstance(catalog["rack_milk_crate"]));
            Assert.True(rackRes.Succeeded);
            Assert.Equal(10, world.ToolRollCapacity);
            Assert.True(world.TryPackToolRollItem(ToolIds.SparePlug));

            world.SetZone(new ZoneEnvironmentSpec
            {
                Id = "Zone_SF_Mission",
                DisplayName = "SF Mission",
                AmbientTemperatureC = 14f,
                DistanceFromGarageMiles = 4.0f
            });
            Assert.Equal(14f, bike.AmbientTemperatureC);

            decimal initialCash = world.CashUsd;
            RecoveryResult tow = world.RecoverActiveBikeWithBuddyTow();
            Assert.True(tow.Succeeded);
            Assert.True(tow.CashCostUsd > 0m);
            Assert.Equal(initialCash - tow.CashCostUsd, world.CashUsd);
        }

        private static void AssertBitIdentical(EngineTelemetrySnapshot a, EngineTelemetrySnapshot b)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(a.Rpm), BitConverter.SingleToInt32Bits(b.Rpm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.RearWheelTorqueNm), BitConverter.SingleToInt32Bits(b.RearWheelTorqueNm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.EngineBrakeTorqueNm), BitConverter.SingleToInt32Bits(b.EngineBrakeTorqueNm));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.EffectiveAfr), BitConverter.SingleToInt32Bits(b.EffectiveAfr));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ChtCelsius), BitConverter.SingleToInt32Bits(b.ChtCelsius));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.FuelLitersRemaining), BitConverter.SingleToInt32Bits(b.FuelLitersRemaining));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.ExhaustLeakFactor), BitConverter.SingleToInt32Bits(b.ExhaustLeakFactor));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.HeadlightOutput), BitConverter.SingleToInt32Bits(b.HeadlightOutput));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.VehicleSpeedMps), BitConverter.SingleToInt32Bits(b.VehicleSpeedMps));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.CrankcaseTempCelsius), BitConverter.SingleToInt32Bits(b.CrankcaseTempCelsius));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.PlugColorIndex), BitConverter.SingleToInt32Bits(b.PlugColorIndex));
            Assert.Equal(a.IsReserveActive, b.IsReserveActive);
            Assert.Equal(a.IsFourStroking, b.IsFourStroking);
            Assert.Equal(a.IsDetonating, b.IsDetonating);
            Assert.Equal(a.IsPowerbandActive, b.IsPowerbandActive);
            Assert.Equal(a.IsStalled, b.IsStalled);
            Assert.Equal(a.IsSeized, b.IsSeized);
            Assert.Equal(a.CurrentGear, b.CurrentGear);
        }
    }
}
