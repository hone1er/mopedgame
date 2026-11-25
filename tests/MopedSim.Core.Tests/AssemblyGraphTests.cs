using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class AssemblyGraphTests
    {
        [Fact]
        public void CylinderRemoval_BlockedUntilHeadNutsHeadExhaustAndIntakeRemoved()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);

            WrenchActionResult blocked = bike.TryRemovePart(SlotIds.Cylinder);
            Assert.False(blocked.Succeeded);
            Assert.Equal(WrenchActionStatus.Blocked, blocked.Status);
            Assert.Contains(FastenerIds.HeadNut1, blocked.BlockingNodes);
            Assert.Contains(SlotIds.CylinderHead, blocked.BlockingNodes);
            Assert.Contains(SlotIds.Exhaust, blocked.BlockingNodes);
            Assert.Contains(SlotIds.IntakeManifold, blocked.BlockingNodes);

            // Unbolt and remove exhaust
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFlange1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFlange2).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFrameBracket).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Exhaust).Succeeded);

            // Unbolt and remove intake
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake2).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeManifold).Succeeded);

            // Still blocked by head & head nuts
            WrenchActionResult stillBlocked = bike.TryRemovePart(SlotIds.Cylinder);
            Assert.False(stillBlocked.Succeeded);

            // Unbolt head nuts and pull head
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut2).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut3).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut4).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.CylinderHead).Succeeded);

            // Cylinder now pulls cleanly
            Assert.True(bike.TryRemovePart(SlotIds.Cylinder).Succeeded);
        }

        [Fact]
        public void PistonInstall_RejectsBackwardsArrow_AndOutOfSpecRingGap()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            var catalog = TestFixtures.CreateCatalog();

            // Strip top end to access piston
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFlange1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFlange2).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFrameBracket).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Exhaust).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.Intake2).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeManifold).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut1).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut2).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut3).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.HeadNut4).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.CylinderHead).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Cylinder).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Piston).Succeeded);

            var backwardsPiston = new PartInstance(catalog["piston_50"])
            {
                ArrowTowardExhaust = false,
                RingGapMm = 0.25f
            };
            WrenchActionResult orientationErr = bike.TryInstallPart(SlotIds.Piston, backwardsPiston);
            Assert.False(orientationErr.Succeeded);
            Assert.Equal(WrenchActionStatus.InvalidOrientation, orientationErr.Status);

            var tightRingPiston = new PartInstance(catalog["piston_50"])
            {
                ArrowTowardExhaust = true,
                RingGapMm = 0.04f
            };
            WrenchActionResult gapErr = bike.TryInstallPart(SlotIds.Piston, tightRingPiston);
            Assert.False(gapErr.Succeeded);
            Assert.Equal(WrenchActionStatus.InvalidSpec, gapErr.Status);

            var validPiston = new PartInstance(catalog["piston_50"])
            {
                ArrowTowardExhaust = true,
                RingGapMm = 0.25f
            };
            Assert.True(bike.TryInstallPart(SlotIds.Piston, validPiston).Succeeded);
        }

        [Fact]
        public void FastenerOverTorque_StripsThread_AndUnderTorque_CausesAirLeakAndVibrationLoosening()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);

            // Over-torquing head nut past StripThresholdNm (18 Nm) strips it
            WrenchActionResult stripRes = bike.TryTorqueFastener(FastenerIds.HeadNut1, 22f);
            Assert.False(stripRes.Succeeded);
            Assert.Equal(WrenchActionStatus.FastenerStripped, stripRes.Status);
            FastenerState? headNut = bike.Assembly.GetFastener(FastenerIds.HeadNut1);
            Assert.NotNull(headNut);
            Assert.True(headNut!.IsStripped);

            // Tap/helicoil repair restores fastener
            Assert.True(bike.RepairStrippedFastener(FastenerIds.HeadNut1).Succeeded);
            Assert.False(headNut.IsStripped);
            Assert.True(bike.TryTorqueFastener(FastenerIds.HeadNut1, 12f).Succeeded);

            // Under-torquing intake bolts & carb clamp induces air leak + higher AFR + higher idle
            Assert.True(bike.TryTorqueFastener(FastenerIds.Intake1, 1.0f).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.Intake2, 1.0f).Succeeded);
            Assert.True(bike.TryTorqueFastener(FastenerIds.CarbClamp, 0.5f).Succeeded);

            float initialIntakeTorque = bike.Assembly.GetFastener(FastenerIds.Intake1)!.CurrentTorqueNm;
            var rng = new SeededSimRandom(606);
            var roughRide = new RideInputState(throttle: 0.4f, surfaceRoughness: 1.0f);

            for (int i = 0; i < 600; i++)
            {
                bike.Step(1f / 60f, in roughRide, rng);
            }

            Assert.True(bike.LatestTelemetry.AirLeakSeverity > 0.25f);
            Assert.True(bike.Engine.IdleRpmTarget > 1800f);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.Intake1)!.CurrentTorqueNm < initialIntakeTorque);
        }

        [Fact]
        public void SeizedExhaustNut_SnapsWithoutPenetratingOil_AndUnseizesWithOil()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.Assembly.SetFastenerSeized(FastenerIds.ExhaustFlange1, true);
            bike.Assembly.SetFastenerSeized(FastenerIds.ExhaustFlange2, true);

            // Forcing seized nut without penetrating oil snaps/strips it
            WrenchActionResult forced = bike.TryRemoveFastener(FastenerIds.ExhaustFlange1);
            Assert.False(forced.Succeeded);
            Assert.Equal(WrenchActionStatus.FastenerSeized, forced.Status);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.ExhaustFlange1)!.IsStripped);

            // Applying penetrating oil first allows clean removal
            Assert.True(bike.ApplyPenetratingOil(FastenerIds.ExhaustFlange2).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.ExhaustFlange2).Succeeded);
            Assert.False(bike.Assembly.GetFastener(FastenerIds.ExhaustFlange2)!.IsInstalled);

            // Interactable nodes expose UI status accurately
            var nodes = bike.GetInteractableNodes();
            Assert.NotEmpty(nodes);
        }
    }
}
