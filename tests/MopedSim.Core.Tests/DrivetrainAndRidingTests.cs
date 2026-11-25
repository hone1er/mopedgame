using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class DrivetrainAndRidingTests
    {
        [Fact]
        public void AllThreeDrivetrains_ExhibitAuthenticTransmissionBehavior()
        {
            var rng = new SeededSimRandom(1111);
            var wot = new RideInputState(throttle: 1.0f);

            // 1. Maxer E50: Single-speed wet centrifugal clutch (gear 0 at idle -> 1 under throttle)
            BikeSimulation e50 = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            e50.Thermal.ChtCelsius = 140f;
            e50.Step(1f / 60f, new RideInputState(0f), rng);
            Assert.Equal(0, e50.LatestTelemetry.CurrentGear);
            for (int i = 0; i < 900; i++)
            {
                e50.Step(1f / 60f, in wot, rng);
            }
            Assert.Equal(1, e50.LatestTelemetry.CurrentGear);
            Assert.True(e50.LatestTelemetry.VehicleSpeedMph > 15f);

            // 2. Bramble PA50: Variated belt drive shifts VariatorRatio downward as speed rises
            BikeSimulation pa50 = TestFixtures.CreateStockBike(ArchetypeIds.BramblePA50);
            pa50.Thermal.ChtCelsius = 140f;
            float initialRatio = pa50.Drivetrain.VariatorRatio;
            for (int i = 0; i < 900; i++)
            {
                pa50.Step(1f / 60f, in wot, rng);
            }
            Assert.True(pa50.Drivetrain.VariatorRatio < initialRatio);
            Assert.True(pa50.LatestTelemetry.VehicleSpeedMph > 20f);

            // Snapped belt sends RPM screaming with zero rear-wheel torque
            pa50.InduceFault(FaultIds.SnappedBelt);
            EngineTelemetrySnapshot snapped = pa50.Step(1f / 60f, in wot, rng);
            Assert.True(snapped.IsBeltSnapped);
            Assert.Equal(0f, snapped.RearWheelTorqueNm);

            // 3. Targa A35: 2-speed automatic shifts 1 -> 2
            BikeSimulation a35 = TestFixtures.CreateStockBike(ArchetypeIds.TargaA35);
            a35.Thermal.ChtCelsius = 140f;
            bool sawGear1 = false;
            bool sawGear2 = false;
            for (int i = 0; i < 900; i++)
            {
                EngineTelemetrySnapshot snap = a35.Step(1f / 60f, in wot, rng);
                if (snap.CurrentGear == 1)
                {
                    sawGear1 = true;
                }
                if (snap.CurrentGear == 2)
                {
                    sawGear2 = true;
                }
            }
            Assert.True(sawGear1);
            Assert.True(sawGear2);
        }

        [Fact]
        public void AeroTuck_IncreasesTopSpeed_ByAtLeastThreeMph()
        {
            var rngA = new SeededSimRandom(1212);
            var rngB = new SeededSimRandom(1212);

            BikeSimulation upright = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation tucked = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            upright.Thermal.ChtCelsius = 140f;
            tucked.Thermal.ChtCelsius = 140f;

            var uprightInput = new RideInputState(throttle: 1.0f, aeroTuck: false);
            var tuckInput = new RideInputState(throttle: 1.0f, aeroTuck: true);

            for (int i = 0; i < 60 * 40; i++)
            {
                upright.Step(1f / 60f, in uprightInput, rngA);
                tucked.Step(1f / 60f, in tuckInput, rngB);
            }

            float gainMph = tucked.LatestTelemetry.VehicleSpeedMph - upright.LatestTelemetry.VehicleSpeedMph;
            Assert.True(gainMph >= 3.0f);
        }

        [Fact]
        public void Decompressor_ShutsOffEngine_AndPedalAssistImprovesSteepHillClimb()
        {
            var rng = new SeededSimRandom(1313);
            BikeSimulation noPedal = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            BikeSimulation withPedal = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            noPedal.Thermal.ChtCelsius = 140f;
            withPedal.Thermal.ChtCelsius = 140f;

            // 18% grade (~0.178 rad)
            var hillNoPedal = new RideInputState(throttle: 1.0f, pedalAssist: false, roadSlopeRadians: 0.178f);
            var hillWithPedal = new RideInputState(throttle: 1.0f, pedalAssist: true, roadSlopeRadians: 0.178f);

            for (int i = 0; i < 60 * 12; i++)
            {
                noPedal.Step(1f / 60f, in hillNoPedal, rng);
                withPedal.Step(1f / 60f, in hillWithPedal, rng);
            }

            Assert.True(withPedal.LatestTelemetry.VehicleSpeedMph > noPedal.LatestTelemetry.VehicleSpeedMph + 1.5f);

            // Decompressor cleanly cuts running engine
            var decomp = new RideInputState(throttle: 0.2f, decompressor: true);
            for (int i = 0; i < 40; i++)
            {
                noPedal.Step(1f / 60f, in decomp, rng);
            }
            Assert.True(noPedal.LatestTelemetry.IsStalled);
        }

        [Fact]
        public void ExpansionChamber_DeliversPowerbandSurge_InPeakRpmWindow()
        {
            var catalog = TestFixtures.CreateCatalog();
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_72");
            Assert.True(bike.TrySwapPart(SlotIds.Exhaust, new PartInstance(catalog["pipe_topend_expansion"])).Succeeded);

            float lowRpmMult = ExhaustSystem.EvaluateTorqueCurve(
                catalog["pipe_topend_expansion"].Stats.TorqueCurve,
                4200f);
            float peakRpmMult = ExhaustSystem.EvaluateTorqueCurve(
                catalog["pipe_topend_expansion"].Stats.TorqueCurve,
                7600f);

            Assert.True(lowRpmMult < 0.90f);
            Assert.True(peakRpmMult > 1.30f);

            var rng = new SeededSimRandom(1414);
            var wot = new RideInputState(throttle: 1.0f);
            bool hitPowerband = false;
            for (int i = 0; i < 60 * 25; i++)
            {
                EngineTelemetrySnapshot snap = bike.Step(1f / 60f, in wot, rng);
                if (snap.IsPowerbandActive)
                {
                    hitPowerband = true;
                    break;
                }
            }
            Assert.True(hitPowerband);
        }
    }
}
