using System;
using System.Collections.Generic;
using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    public sealed class EconomyCatalogTests
    {
        private const string BurritoGigId = "gig_oakland_burrito_crate";

        [Fact]
        public void Catalog_MapsRackCargoStats()
        {
            CatalogDatabase db = TestCatalog.Real;
            Assert.Equal(1, db.GetPart("rack_rear_wire").Stats.CargoSlots);
            Assert.Equal(8f, db.GetPart("rack_rear_wire").Stats.CargoMaxKg);
            Assert.Equal(4, db.GetPart("rack_milk_crate").Stats.CargoSlots);
            Assert.Equal(20f, db.GetPart("rack_milk_crate").Stats.CargoMaxKg);

            // Stock bikes come with the wire rack mounted in SLOT_CARGO_RACK.
            foreach (BikeArchetypeSpec archetype in db.ArchetypeList)
            {
                CargoCapacity cap = CargoCapacityCalculator.Compute(new BikeSimulation(archetype, db.Parts));
                Assert.Equal("rack_rear_wire", cap.RackPartId);
                Assert.Equal(1, cap.Slots);
                Assert.Equal(8f, cap.MaxKg);
                Assert.True(cap.IsSecured);
            }
        }

        [Fact]
        public void Catalog_JobDefinitionsCarryTypeRewardsCargoAndOrderedCheckpoints()
        {
            CatalogDatabase db = TestCatalog.Real;
            JobDefinition gig = db.GetJob(BurritoGigId);
            Assert.Equal(JobType.CourierGig, gig.JobType);
            Assert.Equal("Zone_WestOakland", gig.ZoneId);
            Assert.Equal("Zone_OaklandHills", gig.TargetZoneId);
            Assert.Equal(3, gig.RequiredCargoSlots);
            Assert.Equal(7.5f, gig.RequiredCargoKg);
            Assert.Equal(75m, gig.RewardUsd);
            Assert.Equal(30m, gig.BonusRewardUsd);
            Assert.Equal(25, gig.ReputationReward);
            Assert.Equal(240f, gig.TimeLimitSeconds);
            Assert.Equal(new[] { "cp_burrito_pickup", "cp_lake_merritt_turn", "cp_tunnel_crew_drop" }, gig.Checkpoints.Select(c => c.Id));
            Assert.Equal(860f, gig.Checkpoints[2].X);
            Assert.Equal(12f, gig.Checkpoints[2].GradePercent);
            Assert.Equal("Zone_OaklandHills", gig.Checkpoints[2].ZoneId);

            JobMetadata meta = db.JobMeta[BurritoGigId];
            Assert.Equal("CourierGig", meta.JobType);
            Assert.Equal(JobType.CourierGig, meta.ParsedJobType);
            Assert.Equal(gig.DisplayName, meta.DisplayName);
            Assert.Equal(240f, meta.TimeLimitSeconds);
            Assert.Equal(75m, meta.BasePayoutUsd);

            Assert.Equal(new[] { "rally_east_bay_scrambler", "rally_bay_swarm_sunday" }, db.GetJobsOfType(JobType.BaySwarmRally).Select(j => j.Id));
            Assert.Equal(2, db.GetJobsOfType(JobType.HillClimb).Count);
            Assert.Single(db.GetJobsOfType(JobType.SpeedTrap));
            foreach (JobDefinition job in db.JobList)
            {
                Assert.NotEmpty(job.Checkpoints);
                Assert.Equal(job.Checkpoints.Select(c => c.Order).OrderBy(o => o), job.Checkpoints.Select(c => c.Order));
                Assert.Equal(db.JobMeta[job.Id].ParsedJobType, job.JobType);
            }
        }

        [Fact]
        public void Catalog_ZoneMetadataExposesDisplayNameLandmarksAndLinks()
        {
            CatalogDatabase db = TestCatalog.Real;
            ZoneMetadata wo = db.ZoneMeta["Zone_WestOakland"];
            Assert.Equal(db.GetZone("Zone_WestOakland").DisplayName, wo.DisplayName);
            Assert.Equal("lm_parts_alley_swap", wo.GetLandmarksOfType("Shop").Single().Id);
            Assert.True(wo.IsConnectedTo("Zone_OaklandHills"));
            Assert.False(wo.IsConnectedTo("Zone_Moon"));
            Assert.All(db.ZoneMeta.Values, z => Assert.False(string.IsNullOrEmpty(z.DisplayName)));
        }

        [Fact]
        public void SwapMeet_OverRealCatalog_DiscountsEveryOfferAndReachesEveryDefect()
        {
            CatalogDatabase db = TestCatalog.Real;
            var market = new SwapMeetMarket(db.PartList, 2026u);
            var seen = new HashSet<SwapMeetDefect>();
            for (int rotation = 0; rotation < 60; rotation++)
            {
                market.Rotate(rotation);
                Assert.Equal(market.Config.OffersPerRotation, market.Offers.Count);
                Assert.Equal(market.Offers.Count, market.Offers.Select(o => o.OfferId).Distinct().Count());
                foreach (SwapMeetOffer offer in market.Offers)
                {
                    Assert.True(offer.AskingPriceUsd > 0m && offer.AskingPriceUsd < offer.ListPriceUsd, offer.OfferId);
                    Assert.NotEqual(PartCategory.ToolRollItem, offer.Part.Category);
                    seen.Add(market.InspectOffer(offer.OfferId).Defect);
                }
            }
            foreach (SwapMeetDefect defect in Enum.GetValues(typeof(SwapMeetDefect)))
            {
                Assert.Contains(defect, seen);
            }

            var again = new SwapMeetMarket(db.PartList, 2026u);
            again.Rotate(59);
            Assert.Equal(market.Offers.Select(o => o.OfferId + o.AskingPriceUsd), again.Offers.Select(o => o.OfferId + o.AskingPriceUsd));
        }

        [Fact]
        public void Catalog_UnknownJobType_FailsToLoad()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation(CatalogLoader.JobsFileName,
                root => TestCatalog.FindById(root, "jobs", BurritoGigId)["jobType"] = "Teleport"));
        }

        [Fact]
        public void EndToEnd_RefusedWithoutMilkCrate_MountCrate_DeliverRealGig_EarnCash_BuyAndInstallUpgrade()
        {
            CatalogDatabase db = TestCatalog.Real;
            BikeArchetypeSpec archetype = db.GetArchetype(ArchetypeIds.MaxerE50);
            var bike = new BikeSimulation(archetype, db.Parts);
            var world = new SimulationWorld(new SeededSimRandom(2026u), db.GetZone("Zone_WestOakland")) { CashUsd = 35m };
            world.RegisterBike("bike_001", bike);
            var jobs = new JobSessionManager();
            JobDefinition gig = db.GetJob(BurritoGigId);
            PartDefinition sprocket = db.GetPart("sprocket_front_11t");

            // 1. Stock wire rack: refused on cargo slots.
            JobAcceptResult refused = jobs.TryAcceptJob(gig, bike);
            Assert.False(refused.Accepted);
            Assert.Equal(JobRejectReason.InsufficientCargoSlots, refused.Reason);
            Assert.Equal(1, refused.Capacity.Slots);
            Assert.Equal("rack_rear_wire", refused.Capacity.RackPartId);

            // 2. Buy the milk crate with the last $35 and mount it.
            Assert.True(world.TryPurchasePartIntoInventory(db.GetPart("rack_milk_crate"), out PartInstance? crate).Succeeded);
            Assert.Equal(0m, world.CashUsd);
            Assert.False(world.TryPurchasePart(sprocket).Succeeded);

            List<FastenerSpec> rackBolts = archetype.Fasteners.Where(f => f.OwnerSlotId == SlotIds.CargoRack).ToList();
            Assert.Equal(2, rackBolts.Count);
            foreach (FastenerSpec f in rackBolts)
            {
                Assert.True(bike.TryRemoveFastener(f.FastenerId).Succeeded, f.FastenerId);
            }
            Assert.False(CargoCapacityCalculator.Compute(bike).IsSecured);
            Assert.True(bike.TryRemovePart(SlotIds.CargoRack).Succeeded);
            Assert.Equal(0, CargoCapacityCalculator.Compute(bike).Slots);
            world.Inventory.Remove(crate!);
            Assert.True(bike.TryInstallPart(SlotIds.CargoRack, crate!).Succeeded);
            foreach (FastenerSpec f in rackBolts)
            {
                Assert.True(bike.TryTorqueFastener(f.FastenerId, f.TargetTorqueNm).Succeeded, f.FastenerId);
            }
            CargoCapacity crateCap = CargoCapacityCalculator.Compute(bike);
            Assert.Equal(4, crateCap.Slots);
            Assert.Equal(20f, crateCap.MaxKg);
            Assert.True(crateCap.IsSecured);

            // 3. Accept and ride the real route with Core telemetry.
            JobAcceptResult accepted = jobs.TryAcceptJob(gig, bike);
            Assert.True(accepted.Accepted, accepted.Message);
            Assert.Equal(CheckpointReportResult.Reached, jobs.ReportPosition(gig.Checkpoints[0].X, gig.Checkpoints[0].Y, gig.Checkpoints[0].Z));

            var input = new RideInputState(throttle: 0.85f, surfaceGrip: 1f, surfaceRoughness: 0.05f, ambientAirDensity: 1f);
            const float dt = 1f / 30f;
            int ticks = 0;
            while (jobs.Status == JobSessionStatus.Active && ticks < 30 * 600)
            {
                EngineTelemetrySnapshot t = world.Step(dt, in input);
                jobs.Step(dt, in t, input.SurfaceRoughness);
                JobCheckpointSpec? next = jobs.NextCheckpoint;
                int reachedIdx = jobs.CheckpointsReached;
                if (next != null && jobs.RouteProgressMeters >= RallyPackSimulator.ComputeCumulativeDistances(gig.Checkpoints)[reachedIdx] - 0.01f)
                {
                    jobs.ReportCheckpointReached(next.Id);
                }
                ticks++;
            }
            Assert.Equal(JobSessionStatus.Completed, jobs.Status);
            CompletedGigRecord rec = jobs.LastCompletedRecord!;
            Assert.Equal(BurritoGigId, rec.JobId);
            Assert.True(rec.OnTime, "elapsed " + rec.ElapsedSeconds);
            Assert.InRange(rec.CargoIntegrity, 0.5f, 1f);
            Assert.True(rec.TotalPayoutUsd > sprocket.PriceUsd);

            // 4. Collect exactly once, then the earned cash buys and installs an aftermarket upgrade.
            Assert.True(jobs.TryCollectReward(world));
            Assert.False(jobs.TryCollectReward(world));
            Assert.Equal(rec.TotalPayoutUsd, world.CashUsd);
            Assert.Equal(25, world.Reputation);

            PurchaseResult bought = world.TryPurchasePartIntoInventory(sprocket, out PartInstance? newSprocket);
            Assert.True(bought.Succeeded, bought.Message);
            Assert.Equal(rec.TotalPayoutUsd - sprocket.PriceUsd, world.CashUsd);

            bike.CoolDownSeconds(600f);
            foreach (FastenerSpec f in archetype.Fasteners.Where(x => x.OwnerSlotId == SlotIds.FrontSprocket))
            {
                Assert.True(bike.TryRemoveFastener(f.FastenerId).Succeeded, f.FastenerId);
            }
            PartInstance chain = bike.Assembly.GetInstalledPart(SlotIds.Chain)!;
            Assert.True(bike.TryRemovePart(SlotIds.Chain).Succeeded);
            WrenchActionResult removeStock = bike.TryRemovePart(SlotIds.FrontSprocket);
            Assert.True(removeStock.Succeeded, removeStock.Message + " " + string.Join(",", removeStock.BlockingNodes));
            world.Inventory.Remove(newSprocket!);
            Assert.True(bike.TryInstallPart(SlotIds.FrontSprocket, newSprocket!).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Chain, chain).Succeeded);
            Assert.Equal(11, bike.Drivetrain.FrontSprocketTeeth);
        }
    }

    public sealed class CustomizationSaveTests
    {
        private const string V1SaveWithoutPatina = @"{
  ""schemaVersion"": 1,
  ""seed"": 12345,
  ""cashUsd"": 250.0,
  ""reputation"": 0,
  ""activeBikeInstanceId"": ""bike_001"",
  ""activeZoneId"": ""Zone_WestOakland"",
  ""toolRollCapacity"": 6,
  ""bikes"": [
    {
      ""instanceId"": ""bike_001"",
      ""archetypeId"": ""arch_maxer_e50"",
      ""nickname"": ""Starter Moped"",
      ""odometerKm"": 12.5,
      ""paintHex"": ""#C8553D"",
      ""fuelTank"": { ""fuelLiters"": 2.5, ""premixRatio"": 50.0, ""petcockMode"": ""On"", ""rustContamination"": 0.0 },
      ""installedParts"": [],
      ""fasteners"": [],
      ""activeFaultIds"": []
    }
  ],
  ""inventory"": [],
  ""activeToolRoll"": [],
  ""completedGigs"": [],
  ""unlockedZones"": [ ""Zone_WestOakland"" ],
  ""diagnosticHintsEnabled"": true
}";

        private static string V1SaveWithPatina
        {
            get { return V1SaveWithoutPatina.Replace(@"""paintHex"": ""#C8553D"",", @"""paintHex"": ""#C8553D"", ""patinaLevel"": 0.42,"); }
        }

        [Fact]
        public void LegacyV1Save_WithoutPatinaLevel_LoadsWithZeroPatinaAndNoDecal()
        {
            SaveState state = SaveMigrator.DeserializeAndMigrate(V1SaveWithoutPatina);
            BikeSaveState bike = state.Bikes[0];
            Assert.Equal(0f, bike.PatinaLevel);
            Assert.Null(bike.DecalStyle);
            Assert.Equal("#C8553D", bike.PaintHex);

            BikeCustomizationState c = bike.ToCustomizationState();
            Assert.Equal("#C8553D", c.PaintHex);
            Assert.Equal(0f, c.PatinaLevel);
            Assert.Null(c.DecalStyle);

            // Re-saving writes patinaLevel explicitly but still omits the absent decal.
            JObject resaved = JObject.Parse(SaveMigrator.Serialize(state));
            Assert.Equal(0.0, (double)resaved["bikes"]![0]!["patinaLevel"]!);
            Assert.Null(resaved["bikes"]![0]!["decalStyle"]);
        }

        [Fact]
        public void LegacyV1Save_WithPatinaLevel_LoadsIt()
        {
            SaveState state = SaveMigrator.DeserializeAndMigrate(V1SaveWithPatina);
            Assert.Equal(0.42f, state.Bikes[0].PatinaLevel);
            Assert.Equal(0.42f, state.Bikes[0].ToCustomizationState().PatinaLevel);
        }

        [Fact]
        public void Customization_PaintPatinaDecal_RoundTripsThroughSaveJson()
        {
            SaveState state = SaveMigrator.CreateNewDefault(7u, TestCatalog.Real);
            var custom = new BikeCustomizationState("#3A7D44", 0.65f, "checker_stripe");
            state.Bikes[0].ApplyCustomization(custom);

            string json = SaveMigrator.Serialize(state);
            SaveState loaded = SaveMigrator.DeserializeAndMigrate(json);
            BikeCustomizationState back = loaded.Bikes[0].ToCustomizationState();
            Assert.Equal("#3A7D44", back.PaintHex);
            Assert.Equal(0.65f, back.PatinaLevel);
            Assert.Equal("checker_stripe", back.DecalStyle);
            Assert.Equal(json, SaveMigrator.Serialize(loaded));

            // Clearing the decal removes the member again.
            custom.TrySetDecalStyle(null);
            loaded.Bikes[0].ApplyCustomization(custom);
            Assert.Null(JObject.Parse(SaveMigrator.Serialize(loaded))["bikes"]![0]!["decalStyle"]);
        }

        [Fact]
        public void Customization_InvalidSaveValues_AreRejected()
        {
            AssertRejected(j => j["bikes"]![0]!["patinaLevel"] = -0.1);
            AssertRejected(j => j["bikes"]![0]!["patinaLevel"] = JValue.CreateNull());
            AssertRejected(j => j["bikes"]![0]!["decalStyle"] = new string('x', 65));
            AssertRejected(j => j["bikes"]![0]!["decalStyle"] = JValue.CreateNull());
            AssertRejected(j => j["bikes"]![0]!["decalStyle"] = 5);

            SaveState state = SaveMigrator.CreateNewDefault(1u);
            state.Bikes[0].DecalStyle = new string('y', 65);
            Assert.Contains(SaveMigrator.Validate(state), e => e.Contains("decalStyle"));
        }

        [Fact]
        public void SaveSchema_PatinaAndDecalAreOptionalBikeProperties()
        {
            JObject bikeSchema = (JObject)TestCatalog.ReadSchema("save_state.schema.json")["properties"]!["bikes"]!["items"]!;
            string[] required = ((JArray)bikeSchema["required"]!).Select(t => (string)t!).ToArray();
            Assert.DoesNotContain("patinaLevel", required);
            Assert.DoesNotContain("decalStyle", required);
            Assert.Contains("paintHex", required);
            Assert.Equal("number", (string?)bikeSchema["properties"]!["patinaLevel"]!["type"]);
            Assert.Equal(BikeCustomizationState.MaxDecalStyleLength, (int)bikeSchema["properties"]!["decalStyle"]!["maxLength"]!);
        }

        private static void AssertRejected(Action<JObject> mutate)
        {
            JObject json = JObject.Parse(V1SaveWithPatina);
            mutate(json);
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));
        }
    }
}
