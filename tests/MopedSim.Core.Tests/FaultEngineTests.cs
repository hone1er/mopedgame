using System;
using Xunit;
using MopedSim.Core;

namespace MopedSim.Core.Tests
{
    public sealed class FaultEngineTests
    {
        [Theory]
        [InlineData(FaultIds.FouledPlug, new string[] { ToolIds.SparkPlugWrench, ToolIds.SparePlug })]
        [InlineData(FaultIds.CloggedJet, new string[] { ToolIds.Flathead })]
        [InlineData(FaultIds.CondenserHeatSoak, new string[] { ToolIds.FlywheelPuller, ToolIds.SpareCondenser })]
        [InlineData(FaultIds.PointsDrift, new string[] { ToolIds.Flathead, ToolIds.FeelerGauge })]
        [InlineData(FaultIds.ThrownChain, new string[] { ToolIds.SpareMasterLink, ToolIds.NeedleNose, ToolIds.Wrench15mm })]
        [InlineData(FaultIds.SnappedBelt, new string[] { ToolIds.SpareBelt, ToolIds.VariatorStrap })]
        [InlineData(FaultIds.ExhaustLoose, new string[] { ToolIds.Wrench10mm })]
        public void RoadsideRepair_RequiresExactTools_AndFixesFieldFaults(string faultId, string[] requiredTools)
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.InduceFault(faultId);
            Assert.True(bike.Faults.HasFault(faultId));

            // Empty tool roll fails cleanly with missing tool list
            RoadsideRepairResult emptyAttempt = bike.TryRoadsideRepair(faultId, Array.Empty<string>());
            Assert.False(emptyAttempt.Succeeded);
            Assert.NotEmpty(emptyAttempt.MissingToolsOrSpares);

            // Full required tool roll repairs fault cleanly
            RoadsideRepairResult repaired = bike.TryRoadsideRepair(faultId, requiredTools);
            Assert.True(repaired.Succeeded);
            Assert.False(bike.Faults.HasFault(faultId));
        }

        [Fact]
        public void RoadsideRepair_AcceptsCatalogPascalCaseToolIds_AndConsumesSpareInWorld()
        {
            var world = new SimulationWorld(new SeededSimRandom(1515));
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.BramblePA50);
            world.RegisterBike("pa50", bike);

            Assert.True(world.TryPackToolRollItem("SparkPlugWrench"));
            Assert.True(world.TryPackToolRollItem("SparePlug"));
            Assert.True(world.TryPackToolRollItem("SpareBelt"));
            Assert.True(world.TryPackToolRollItem("VariatorStrapTool"));

            bike.InduceFault(FaultIds.FouledPlug);
            RoadsideRepairResult plugFix = world.TryRoadsideRepairActiveBike(FaultIds.FouledPlug);
            Assert.True(plugFix.Succeeded);
            Assert.True(plugFix.ConsumedSpare);
            Assert.DoesNotContain("SparePlug", world.ActiveToolRoll);

            bike.InduceFault(FaultIds.SnappedBelt);
            RoadsideRepairResult beltFix = world.TryRoadsideRepairActiveBike(FaultIds.SnappedBelt);
            Assert.True(beltFix.Succeeded);
            Assert.DoesNotContain("SpareBelt", world.ActiveToolRoll);
        }

        [Fact]
        public void AirLeak_TightensClampForIntakeLeak_AndFlagsCrankSealForGarage()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            Assert.True(bike.TryTorqueFastener(FastenerIds.CarbClamp, 0.0f).Succeeded);
            bike.Step(1f / 60f, RideInputState.Default, new SeededSimRandom(1));
            Assert.True(bike.Faults.HasFault(FaultIds.AirLeak));

            RoadsideRepairResult fixedClamp = bike.TryRoadsideRepair(FaultIds.AirLeak, new[] { ToolIds.Wrench10mm });
            Assert.True(fixedClamp.Succeeded);
            Assert.False(bike.Faults.HasFault(FaultIds.AirLeak));

            // Crank seal leak cannot be fixed permanently roadside
            bike.InduceFault(FaultIds.AirLeak);
            RoadsideRepairResult sealCheck = bike.TryRoadsideRepair(FaultIds.AirLeak, new[] { ToolIds.Wrench10mm });
            Assert.False(sealCheck.Succeeded);
            Assert.True(sealCheck.RequiresGarageRebuild);
        }

        [Fact]
        public void SoftSeize_AndHardSeize_RespectFieldRepairAndRecoveryRules()
        {
            BikeSimulation bike = TestFixtures.CreateStockBike(ArchetypeIds.MaxerE50);
            bike.InduceFault(FaultIds.SoftSeize);

            // Still too hot -> blocked until cooled
            RoadsideRepairResult tooHot = bike.TryRoadsideRepair(FaultIds.SoftSeize, Array.Empty<string>());
            Assert.False(tooHot.Succeeded);

            bike.CoolDownSeconds(120f);
            Assert.False(bike.Faults.HasFault(FaultIds.SoftSeize));

            // Hard seize is NEVER field-repairable -> Pedal of Shame or Buddy Tow
            bike.InduceFault(FaultIds.HardSeize);
            RoadsideRepairResult hardFail = bike.TryRoadsideRepair(FaultIds.HardSeize, new[] { ToolIds.SparkPlugWrench, ToolIds.SparePlug });
            Assert.False(hardFail.Succeeded);
            Assert.True(hardFail.RequiresGarageRebuild);

            RecoveryResult pedal = bike.PedalOfShame(2.0f);
            Assert.True(pedal.Succeeded);
            Assert.Equal(0m, pedal.CashCostUsd);
            Assert.True(pedal.StaminaUsed > 0f);

            RecoveryResult tow = bike.BuddyTow(3.0f);
            Assert.True(tow.Succeeded);
            Assert.True(tow.CashCostUsd > 0m);
        }
    }
}
