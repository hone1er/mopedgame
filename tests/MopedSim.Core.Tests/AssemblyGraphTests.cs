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

        [Fact]
        public void SwapPart_RehangsOnOwnFasteners_PreservesTorque_ButForeignOrSeizedBlockersStillBlock()
        {
            var catalog = TestFixtures.CreateCatalog();
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);

            // Plain removal of the exhaust is still blocked by its own flange/bracket fasteners.
            WrenchActionResult remove = bike.TryRemovePart(SlotIds.Exhaust);
            Assert.False(remove.Succeeded);
            Assert.Equal(WrenchActionStatus.Blocked, remove.Status);
            Assert.Contains(FastenerIds.ExhaustFlange1, remove.BlockingNodes);

            // A loose flange nut stays loose after the swap: a swap is never a free re-torque.
            Assert.True(bike.TryTorqueFastener(FastenerIds.ExhaustFlange1, 4.0f).Succeeded);
            WrenchActionResult swap = bike.TrySwapPart(SlotIds.Exhaust, new PartInstance(catalog["pipe_circuit_e50_proma"]));
            Assert.True(swap.Succeeded);
            Assert.Equal("pipe_circuit_e50_proma", bike.Assembly.GetInstalledPart(SlotIds.Exhaust)!.Definition.Id);
            Assert.Equal(4.0f, bike.Assembly.GetFastener(FastenerIds.ExhaustFlange1)!.CurrentTorqueNm);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.ExhaustFlange2)!.IsProperlyTorqued);

            // A seized owned flange nut must be dealt with first; the original pipe stays mounted.
            bike.Assembly.SetFastenerSeized(FastenerIds.ExhaustFlange2, true);
            WrenchActionResult seizedSwap = bike.TrySwapPart(SlotIds.Exhaust, new PartInstance(catalog["pipe_topend_expansion"]));
            Assert.False(seizedSwap.Succeeded);
            Assert.Equal(WrenchActionStatus.Blocked, seizedSwap.Status);
            Assert.Contains(FastenerIds.ExhaustFlange2, seizedSwap.BlockingNodes);
            Assert.Equal("pipe_circuit_e50_proma", bike.Assembly.GetInstalledPart(SlotIds.Exhaust)!.Definition.Id);

            // Foreign blockers (head nuts, mounted head/exhaust/intake) still block a cylinder swap.
            WrenchActionResult cylSwap = bike.TrySwapPart(SlotIds.Cylinder, new PartInstance(catalog["cyl_kit_70"]));
            Assert.False(cylSwap.Succeeded);
            Assert.Contains(FastenerIds.HeadNut1, cylSwap.BlockingNodes);
            Assert.Contains(SlotIds.CylinderHead, cylSwap.BlockingNodes);
            Assert.Equal("cyl_stock_50", bike.Assembly.GetInstalledPart(SlotIds.Cylinder)!.Definition.Id);

            // An incompatible replacement fails without leaving the slot empty.
            WrenchActionResult wrongPart = bike.TrySwapPart(SlotIds.AirFilter, new PartInstance(catalog["jet_72"]));
            Assert.False(wrongPart.Succeeded);
            Assert.Equal(WrenchActionStatus.IncompatiblePart, wrongPart.Status);
            Assert.Equal("filter_stock_box", bike.Assembly.GetInstalledPart(SlotIds.AirFilter)!.Definition.Id);
        }

        [Fact]
        public void InSpecFasteners_HoldTorqueUnderSustainedWotBuzz_SoTunedAfrStaysStable()
        {
            BikeSimulation bike = TestFixtures.Create70ccKittedBike("jet_72");
            var rng = new SeededSimRandom(707);
            var wot = new RideInputState(throttle: 1.0f);

            float afrAtSixtySeconds = 0f;
            for (int i = 0; i < 60 * 180; i++)
            {
                bike.Step(1f / 60f, in wot, rng);
                if (i == 60 * 60)
                {
                    afrAtSixtySeconds = bike.LatestTelemetry.EffectiveAfr;
                }
            }

            Assert.True(bike.Assembly.GetFastener(FastenerIds.Intake1)!.IsProperlyTorqued);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.Intake2)!.IsProperlyTorqued);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.CarbClamp)!.IsProperlyTorqued);
            Assert.True(bike.Assembly.GetFastener(FastenerIds.ExhaustFlange1)!.IsProperlyTorqued);
            Assert.True(bike.LatestTelemetry.AirLeakSeverity < 0.01f);
            Assert.True(System.Math.Abs(bike.LatestTelemetry.EffectiveAfr - afrAtSixtySeconds) < 0.05f);
        }
    }
}
