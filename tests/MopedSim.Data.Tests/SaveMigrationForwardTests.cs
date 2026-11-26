#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    public sealed class SaveMigrationForwardTests
    {
        public static readonly TheoryData<string> AllArchetypes = new TheoryData<string>
        {
            ArchetypeIds.MaxerE50,
            ArchetypeIds.BramblePA50,
            ArchetypeIds.TargaA35,
        };

        [Theory]
        [InlineData("")]
        [InlineData("   \t\r\n  ")]
        [InlineData("{}")]
        [InlineData("{ \"schemaVersion\": 1,")]
        [InlineData("not valid json")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("{ \"schemaVersion\": 1 } { \"extra\": true }")]
        [InlineData("{ \"schemaVersion\": 1, \"seed\": NaN }")]
        public void DeserializeAndMigrate_RejectsMalformedEmptyAndCorruptJson(string rawJson)
        {
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(rawJson));
        }

        [Fact]
        public void DeserializeAndMigrate_RejectsNullJson()
        {
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(null!));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(99)]
        [InlineData(int.MaxValue)]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-99)]
        public void DeserializeAndMigrate_RejectsUnsupportedSchemaVersions(int version)
        {
            JObject doc = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(101u, TestCatalog.Real)));
            doc["schemaVersion"] = version;

            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(doc.ToString()));
            Assert.Contains("schemaVersion " + version, ex.Message);
        }

        [Theory]
        [InlineData("red")]
        [InlineData("")]
        [InlineData("#FFF")]
        [InlineData("C42828")]
        [InlineData("#12345G")]
        [InlineData("#1234567")]
        [InlineData("#123456\n")]
        public void DeserializeAndMigrate_RejectsInvalidPaintHex(string badPaintHex)
        {
            JObject doc = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(102u, TestCatalog.Real)));
            doc["bikes"]![0]!["paintHex"] = badPaintHex;

            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(doc.ToString()));
        }

        [Theory]
        [InlineData(-0.001)]
        [InlineData(-1.0)]
        [InlineData(1.001)]
        [InlineData(2.5)]
        public void DeserializeAndMigrate_RejectsOutOfRangePatinaLevel(double badPatina)
        {
            JObject doc = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(103u, TestCatalog.Real)));
            doc["bikes"]![0]!["patinaLevel"] = badPatina;

            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(doc.ToString()));
        }

        [Fact]
        public void Validate_RejectsNonFinitePatinaAndOverLengthDecal_AndAcceptsExactDecalBoundary()
        {
            SaveState nanPatina = SaveMigrator.CreateNewDefault(104u);
            nanPatina.Bikes[0].PatinaLevel = float.NaN;
            Assert.Contains(SaveMigrator.Validate(nanPatina), e => e.Contains("patinaLevel"));

            SaveState infPatina = SaveMigrator.CreateNewDefault(104u);
            infPatina.Bikes[0].PatinaLevel = float.PositiveInfinity;
            Assert.Contains(SaveMigrator.Validate(infPatina), e => e.Contains("patinaLevel"));

            // Over-length decal (> 64 chars) rejected on both deserialize and validate.
            foreach (int badLen in new[] { BikeCustomizationState.MaxDecalStyleLength + 1, 128 })
            {
                JObject badDoc = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(105u, TestCatalog.Real)));
                badDoc["bikes"]![0]!["decalStyle"] = new string('x', badLen);
                Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(badDoc.ToString()));
            }

            // Exact 64-char boundary succeeds and round-trips cleanly.
            string maxDecal = new string('d', BikeCustomizationState.MaxDecalStyleLength);
            SaveState boundary = SaveMigrator.CreateNewDefault(106u, TestCatalog.Real);
            boundary.Bikes[0].DecalStyle = maxDecal;
            Assert.Empty(SaveMigrator.Validate(boundary));
            SaveState loaded = SaveMigrator.DeserializeAndMigrate(SaveMigrator.Serialize(boundary));
            Assert.Equal(maxDecal, loaded.Bikes[0].DecalStyle);
        }

        [Theory]
        [MemberData(nameof(AllArchetypes))]
        public void LegacyV1Fixture_MissingOptionalPatinaAndDecal_LoadsCleanlyWithDefaults(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            string legacyJson = @"{
  ""schemaVersion"": 1,
  ""seed"": 4242,
  ""cashUsd"": 315.5,
  ""reputation"": 15,
  ""activeBikeInstanceId"": ""bike_legacy_1"",
  ""activeZoneId"": ""Zone_WestOakland"",
  ""toolRollCapacity"": 6,
  ""bikes"": [
    {
      ""instanceId"": ""bike_legacy_1"",
      ""archetypeId"": """ + archetypeId + @""",
      ""nickname"": ""Legacy Rider"",
      ""odometerKm"": 48.25,
      ""paintHex"": ""#2A6F97"",
      ""fuelTank"": {
        ""fuelLiters"": 2.4,
        ""premixRatio"": 50.0,
        ""petcockMode"": ""On"",
        ""rustContamination"": 0.05
      },
      ""installedParts"": [],
      ""fasteners"": [],
      ""activeFaultIds"": []
    }
  ],
  ""inventory"": [],
  ""activeToolRoll"": [ ""SparkPlugWrench"", ""SparePlug"" ],
  ""completedGigs"": [],
  ""unlockedZones"": [ ""Zone_WestOakland"" ],
  ""diagnosticHintsEnabled"": true
}";

            SaveState loaded = SaveMigrator.DeserializeAndMigrate(legacyJson);
            Assert.Empty(SaveMigrator.Validate(loaded));
            Assert.Empty(SaveMigrator.ValidateAgainstCatalog(loaded, db));

            BikeSaveState bike = Assert.Single(loaded.Bikes);
            Assert.Equal(archetypeId, bike.ArchetypeId);
            Assert.Equal("#2A6F97", bike.PaintHex);
            Assert.Equal(0f, bike.PatinaLevel);
            Assert.Null(bike.DecalStyle);

            BikeCustomizationState custom = bike.ToCustomizationState();
            Assert.Equal("#2A6F97", custom.PaintHex);
            Assert.Equal(0f, custom.PatinaLevel);
            Assert.Null(custom.DecalStyle);

            // Re-saving and reloading stays deterministic and schema-valid.
            string resaved = SaveMigrator.Serialize(loaded);
            SaveState reloaded = SaveMigrator.DeserializeAndMigrate(resaved);
            Assert.Equal(resaved, SaveMigrator.Serialize(reloaded));
            Assert.Equal(0f, reloaded.Bikes[0].PatinaLevel);
            Assert.Null(reloaded.Bikes[0].DecalStyle);
        }

        [Theory]
        [MemberData(nameof(AllArchetypes))]
        public void FullSaveLoadRoundTrip_AllArchetypes_PreservesAllStateAndSimDeterminism(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            SaveState original = SaveMigrator.CreateNewDefault(9001u, db, archetypeId);
            original.CashUsd = 842.75m;
            original.Reputation = 135;
            original.DiagnosticHintsEnabled = false;
            original.UnlockedZones.Add("Zone_OaklandHills");
            original.UnlockedZones.Add("Zone_BayCrossing");
            original.ActiveZoneId = "Zone_OaklandHills";
            original.ToolRollCapacity = 8;

            original.ActiveToolRoll.Add("SparkPlugWrench");
            original.ActiveToolRoll.Add("SparePlug");
            original.ActiveToolRoll.Add("Wrench10mm");
            original.ActiveToolRoll.Add("Flathead");
            original.ActiveToolRoll.Add("MasterLink");
            original.ActiveToolRoll.Add("PenetratingOil");

            original.Inventory.Add(new InventoryItemState
            {
                InstanceId = "inv_jet_74",
                PartId = "jet_main_74",
                Quantity = 3,
                Condition = 0.95f,
                Wear = 0.05f,
            });
            original.Inventory.Add(new InventoryItemState
            {
                InstanceId = "inv_crate_1",
                PartId = "rack_milk_crate",
                Quantity = 1,
                Condition = 0.82f,
                Wear = 0.18f,
            });

            original.CompletedGigs.Add(new CompletedGigState
            {
                JobId = "gig_west_oakland_coffee",
                BestTimeSeconds = 88.4f,
                Completions = 2,
                LastCompletedUtc = "2026-11-26T08:00:00Z",
            });

            BikeSaveState bike = original.Bikes[0];
            bike.Nickname = "Bay Area " + archetypeId;
            bike.OdometerKm = 312.85;
            bike.ApplyCustomization(new BikeCustomizationState("#E07A5F", 0.54f, "oakland_stripe_01"));
            bike.FuelTank.FuelLiters = 0.55f;
            bike.FuelTank.PremixRatio = 40f;
            bike.FuelTank.PetcockMode = PetcockMode.Reserve;
            bike.FuelTank.RustContamination = 0.14f;

            // Mutate installed parts state (wear, fouling, carbon clog, points gap).
            InstalledPartSaveState plugPart = bike.InstalledParts.Single(p => p.SlotId == SlotIds.SparkPlug);
            plugPart.FoulingLevel = 0.32f;
            plugPart.InsulatorColorIndex = 0.48f;
            InstalledPartSaveState exhaustPart = bike.InstalledParts.Single(p => p.SlotId == SlotIds.Exhaust);
            exhaustPart.CarbonClog = 0.21f;
            exhaustPart.Condition = 0.79f;
            exhaustPart.Wear = 0.21f;

            // Mutate fasteners across all states: stripped, seized+oiled, under-torqued, removed (0 Nm).
            FastenerSaveState headNut1 = bike.Fasteners.Single(f => f.FastenerId == FastenerIds.HeadNut1);
            headNut1.CurrentTorqueNm = 20.5f;
            headNut1.IsStripped = true;

            FastenerSaveState exhaustFlange1 = bike.Fasteners.Single(f => f.FastenerId == FastenerIds.ExhaustFlange1);
            exhaustFlange1.CurrentTorqueNm = 4.2f;
            exhaustFlange1.IsSeized = true;
            exhaustFlange1.HasPenetratingOil = true;

            FastenerSaveState carbClamp = bike.Fasteners.Single(f => f.FastenerId == FastenerIds.CarbClamp);
            carbClamp.CurrentTorqueNm = 0f;

            // Active faults.
            bike.ActiveFaultIds.Add(FaultIds.FouledPlug);
            bike.ActiveFaultIds.Add(FaultIds.ExhaustLoose);
            bike.ActiveFaultIds.Add(FaultIds.AirLeak);

            Assert.Empty(SaveMigrator.Validate(original));
            Assert.Empty(SaveMigrator.ValidateAgainstCatalog(original, db));

            // Round-trip through JSON and atomic disk persistence.
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_fwd_" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(dir, archetypeId + "_save.json");
            SaveState loaded;
            try
            {
                SaveMigrator.SaveToFile(filePath, original);
                loaded = SaveMigrator.LoadFromFile(filePath);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }

            Assert.Empty(SaveMigrator.Validate(loaded));
            Assert.Empty(SaveMigrator.ValidateAgainstCatalog(loaded, db));
            Assert.Equal(SaveMigrator.Serialize(original), SaveMigrator.Serialize(loaded));
            Assert.Equal(SaveMigrator.Serialize(original, indented: false), SaveMigrator.Serialize(loaded, indented: false));

            // Explicit field-by-field preservation assertions.
            Assert.Equal(original.Seed, loaded.Seed);
            Assert.Equal(original.CashUsd, loaded.CashUsd);
            Assert.Equal(original.Reputation, loaded.Reputation);
            Assert.Equal(original.ToolRollCapacity, loaded.ToolRollCapacity);
            Assert.Equal(original.ActiveToolRoll, loaded.ActiveToolRoll);
            Assert.Equal(original.UnlockedZones, loaded.UnlockedZones);
            Assert.Equal(original.Inventory.Count, loaded.Inventory.Count);
            for (int i = 0; i < original.Inventory.Count; i++)
            {
                Assert.Equal(original.Inventory[i].InstanceId, loaded.Inventory[i].InstanceId);
                Assert.Equal(original.Inventory[i].PartId, loaded.Inventory[i].PartId);
                Assert.Equal(original.Inventory[i].Quantity, loaded.Inventory[i].Quantity);
                Assert.Equal(original.Inventory[i].Condition, loaded.Inventory[i].Condition);
                Assert.Equal(original.Inventory[i].Wear, loaded.Inventory[i].Wear);
            }

            BikeSaveState loadedBike = Assert.Single(loaded.Bikes);
            Assert.Equal("#E07A5F", loadedBike.PaintHex);
            Assert.Equal(0.54f, loadedBike.PatinaLevel);
            Assert.Equal("oakland_stripe_01", loadedBike.DecalStyle);
            Assert.Equal(0.55f, loadedBike.FuelTank.FuelLiters);
            Assert.Equal(40f, loadedBike.FuelTank.PremixRatio);
            Assert.Equal(PetcockMode.Reserve, loadedBike.FuelTank.PetcockMode);
            Assert.Equal(0.14f, loadedBike.FuelTank.RustContamination);
            Assert.Equal(original.Bikes[0].ActiveFaultIds, loadedBike.ActiveFaultIds);

            FastenerSaveState loadedHeadNut1 = loadedBike.Fasteners.Single(f => f.FastenerId == FastenerIds.HeadNut1);
            Assert.True(loadedHeadNut1.IsStripped);
            Assert.Equal(20.5f, loadedHeadNut1.CurrentTorqueNm);

            FastenerSaveState loadedFlange1 = loadedBike.Fasteners.Single(f => f.FastenerId == FastenerIds.ExhaustFlange1);
            Assert.True(loadedFlange1.IsSeized);
            Assert.True(loadedFlange1.HasPenetratingOil);
            Assert.Equal(4.2f, loadedFlange1.CurrentTorqueNm);

            FastenerSaveState loadedCarbClamp = loadedBike.Fasteners.Single(f => f.FastenerId == FastenerIds.CarbClamp);
            Assert.Equal(0f, loadedCarbClamp.CurrentTorqueNm);

            // Hydrate two BikeSimulations from original vs loaded save and verify deterministic simulation equivalence.
            BikeSimulation simA = HydrateBikeFromSave(db, original.Bikes[0]);
            BikeSimulation simB = HydrateBikeFromSave(db, loadedBike);

            Assert.Equal(simA.Assembly.ComputeAirLeakSeverity(), simB.Assembly.ComputeAirLeakSeverity(), 6);
            Assert.Equal(simA.Assembly.ComputeExhaustLeakSeverity(), simB.Assembly.ComputeExhaustLeakSeverity(), 6);

            var rngA = new SeededSimRandom(original.Seed);
            var rngB = new SeededSimRandom(loaded.Seed);
            var ride = new RideInputState(throttle: 0.70f, surfaceRoughness: 0.05f);
            EngineTelemetrySnapshot snapA = default;
            EngineTelemetrySnapshot snapB = default;
            for (int tick = 0; tick < 180; tick++)
            {
                snapA = simA.Step(1f / 30f, in ride, rngA);
                snapB = simB.Step(1f / 30f, in ride, rngB);
            }

            Assert.Equal(BitConverter.SingleToInt32Bits(snapA.Rpm), BitConverter.SingleToInt32Bits(snapB.Rpm));
            Assert.Equal(BitConverter.SingleToInt32Bits(snapA.ChtCelsius), BitConverter.SingleToInt32Bits(snapB.ChtCelsius));
            Assert.Equal(BitConverter.SingleToInt32Bits(snapA.EffectiveAfr), BitConverter.SingleToInt32Bits(snapB.EffectiveAfr));
            Assert.Equal(BitConverter.SingleToInt32Bits(snapA.VehicleSpeedMps), BitConverter.SingleToInt32Bits(snapB.VehicleSpeedMps));
            Assert.Equal(BitConverter.SingleToInt32Bits(snapA.FuelLitersRemaining), BitConverter.SingleToInt32Bits(snapB.FuelLitersRemaining));
        }

        [Fact]
        public void RealCatalogM7Progression_MaxerE50_MilkCrateCourierGigsTo70ccExpansionPipe_BeatsStockSpeedWithPowerband()
        {
            CatalogDatabase db = CatalogLoader.LoadFromDirectory(TestCatalog.CatalogsDir);
            BikeArchetypeSpec archetype = db.GetArchetype(ArchetypeIds.MaxerE50);

            // 1. Measure baseline stock Maxer E50 peak flat WOT speed.
            var stockBike = new BikeSimulation(archetype, db.Parts);
            (float stockPeakSpeedMps, _) = MeasureFlatWotRun(stockBike, 555u, seconds: 45f);
            Assert.True(stockPeakSpeedMps > 8f, "Stock Maxer E50 should exceed 8 m/s; got " + stockPeakSpeedMps);

            // 2. Start career from default SimulationWorld cash ($250) on arch_maxer_e50.
            var world = new SimulationWorld(new SeededSimRandom(20261126u), db.GetZone("Zone_WestOakland"));
            var bike = new BikeSimulation(archetype, db.Parts);
            world.RegisterBike("bike_001", bike);
            var jobs = new JobSessionManager();

            Assert.Equal(SaveMigrator.DefaultStartingCashUsd, world.CashUsd);

            // 3. Buy rack_milk_crate and install via Core assembly commands.
            PartDefinition milkCrate = db.GetPart("rack_milk_crate");
            Assert.True(world.TryPurchasePartIntoInventory(milkCrate, out PartInstance? crateInstance).Succeeded);
            foreach (FastenerSpec bolt in archetype.Fasteners.Where(f => f.OwnerSlotId == SlotIds.CargoRack))
            {
                Assert.True(bike.TryRemoveFastener(bolt.FastenerId).Succeeded, bolt.FastenerId);
            }
            Assert.True(bike.TryRemovePart(SlotIds.CargoRack).Succeeded);
            world.Inventory.Remove(crateInstance!);
            Assert.True(bike.TryInstallPart(SlotIds.CargoRack, crateInstance!).Succeeded);
            foreach (FastenerSpec bolt in archetype.Fasteners.Where(f => f.OwnerSlotId == SlotIds.CargoRack))
            {
                Assert.True(bike.TryTorqueFastener(bolt.FastenerId, bolt.TargetTorqueNm).Succeeded, bolt.FastenerId);
            }
            Assert.Equal(4, CargoCapacityCalculator.Compute(bike).Slots);

            // 4. Complete real catalog courier gigs without soft-lock to fund the 70cc + expansion pipe build.
            PartDefinition cyl70 = db.GetPart("cyl_kit_70");
            PartDefinition piston70 = db.GetPart("piston_70");
            PartDefinition head70 = db.GetPart("head_highcomp_70");
            PartDefinition pipeExpansion = db.GetPart("pipe_circuit_e50_proma");
            PartDefinition mainJet72 = db.GetPart("jet_main_72");
            decimal targetCost = cyl70.PriceUsd + piston70.PriceUsd + head70.PriceUsd + pipeExpansion.PriceUsd + mainJet72.PriceUsd;

            string[] gigRotation = new[]
            {
                "gig_west_oakland_coffee",
                "gig_oakland_burrito_crate",
                "gig_ferry_parcel_dash",
                "gig_mission_parts_rush",
            };

            int gigsCompleted = 0;
            for (int i = 0; i < gigRotation.Length && world.CashUsd < targetCost; i++)
            {
                bike.CoolDownSeconds(180f);
                if (bike.Fuel.FuelLevelLiters < 1.2f)
                {
                    bike.Refuel(2.0f, ThermalModel.PremixFromRatio(50f), 0f);
                }

                JobDefinition gig = db.GetJob(gigRotation[i]);
                JobAcceptResult accepted = jobs.TryAcceptJob(gig, bike);
                Assert.True(accepted.Accepted, gig.Id + ": " + accepted.Message);

                JobCheckpointSpec startCp = gig.Checkpoints[0];
                Assert.Equal(CheckpointReportResult.Reached, jobs.ReportPosition(startCp.X, startCp.Y, startCp.Z));

                float[] cumulative = RallyPackSimulator.ComputeCumulativeDistances(gig.Checkpoints);
                var input = new RideInputState(throttle: 0.90f, aeroTuck: true, surfaceGrip: 1f, surfaceRoughness: 0.04f);
                const float dt = 1f / 30f;
                int ticks = 0;
                while (jobs.Status == JobSessionStatus.Active && ticks < 30 * 400)
                {
                    EngineTelemetrySnapshot t = world.Step(dt, in input);
                    Assert.False(t.IsHardSeized, gig.Id + ": unexpected hard seize");
                    jobs.Step(dt, in t, input.SurfaceRoughness);

                    JobCheckpointSpec? next = jobs.NextCheckpoint;
                    int reachedIdx = jobs.CheckpointsReached;
                    if (next != null && jobs.RouteProgressMeters >= cumulative[reachedIdx] - 0.01f)
                    {
                        jobs.ReportCheckpointReached(next.Id);
                    }
                    ticks++;
                }

                Assert.Equal(JobSessionStatus.Completed, jobs.Status);
                Assert.True(jobs.TryCollectReward(world));
                gigsCompleted++;
            }

            Assert.True(gigsCompleted >= 2, "Expected at least 2 courier gigs completed, got " + gigsCompleted);
            Assert.True(world.CashUsd >= targetCost, "Expected >= $" + targetCost + " after courier gigs, had $" + world.CashUsd);
            Assert.True(world.Reputation >= 35, "Expected >= 35 reputation after courier gigs, had " + world.Reputation);

            // 5. Purchase 70cc cylinder kit, 70cc piston, high-comp head, expansion pipe, and matched #72 main jet.
            Assert.True(world.TryPurchasePartIntoInventory(cyl70, out PartInstance? cylInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(piston70, out PartInstance? pistonInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(head70, out PartInstance? headInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(pipeExpansion, out PartInstance? pipeInst).Succeeded);
            Assert.True(world.TryPurchasePartIntoInventory(mainJet72, out PartInstance? jetInst).Succeeded);

            world.Inventory.Remove(cylInst!);
            world.Inventory.Remove(pistonInst!);
            world.Inventory.Remove(headInst!);
            world.Inventory.Remove(pipeInst!);
            world.Inventory.Remove(jetInst!);

            // 6. Install all upgrades through real Core assembly commands respecting catalog DAG blockers & fasteners.
            bike.CoolDownSeconds(600f);
            bike.VehicleSpeedMps = 0f;

            // Remove spark plug boot, spark plug, head, and head gasket.
            PartInstance plugBoot = bike.Assembly.GetInstalledPart(SlotIds.PlugBoot)!;
            PartInstance sparkPlug = bike.Assembly.GetInstalledPart(SlotIds.SparkPlug)!;
            PartInstance headGasket = bike.Assembly.GetInstalledPart(SlotIds.HeadGasket)!;

            Assert.True(bike.TryRemovePart(SlotIds.PlugBoot).Succeeded);
            Assert.True(bike.TryRemoveFastener(FastenerIds.SparkPlugThread).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.SparkPlug).Succeeded);
            RemoveOwnerFasteners(bike, archetype, SlotIds.CylinderHead);
            Assert.True(bike.TryRemovePart(SlotIds.CylinderHead).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.HeadGasket).Succeeded);

            // Remove exhaust and exhaust gasket.
            PartInstance exhaustGasket = bike.Assembly.GetInstalledPart(SlotIds.ExhaustGasket)!;
            RemoveOwnerFasteners(bike, archetype, SlotIds.Exhaust);
            Assert.True(bike.TryRemovePart(SlotIds.Exhaust).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.ExhaustGasket).Succeeded);

            // Remove air filter, cables/lines blocking carb, carb, intake manifold, and intake gasket.
            PartInstance airFilter = bike.Assembly.GetInstalledPart(SlotIds.AirFilter)!;
            PartInstance throttleCable = bike.Assembly.GetInstalledPart(SlotIds.ThrottleCable)!;
            PartInstance fuelLine = bike.Assembly.GetInstalledPart(SlotIds.FuelLine)!;
            PartInstance mainJetStock = bike.Assembly.GetInstalledPart(SlotIds.MainJet)!;
            PartInstance pilotJet = bike.Assembly.GetInstalledPart(SlotIds.PilotJet)!;
            PartInstance carb = bike.Assembly.GetInstalledPart(SlotIds.Carburetor)!;
            PartInstance intake = bike.Assembly.GetInstalledPart(SlotIds.IntakeManifold)!;
            PartInstance intakeGasket = bike.Assembly.GetInstalledPart(SlotIds.IntakeGasket)!;

            RemoveOwnerFasteners(bike, archetype, SlotIds.AirFilter);
            Assert.True(bike.TryRemovePart(SlotIds.AirFilter).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.ThrottleCable).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.FuelLine).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.MainJet).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.PilotJet).Succeeded);
            RemoveOwnerFasteners(bike, archetype, SlotIds.Carburetor);
            Assert.True(bike.TryRemovePart(SlotIds.Carburetor).Succeeded);
            RemoveOwnerFasteners(bike, archetype, SlotIds.IntakeManifold);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeManifold).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.IntakeGasket).Succeeded);

            // Remove cylinder studs, cylinder, and stock piston; mount 70cc piston & cylinder.
            RemoveOwnerFasteners(bike, archetype, SlotIds.Cylinder);
            Assert.True(bike.TryRemovePart(SlotIds.Cylinder).Succeeded);
            Assert.True(bike.TryRemovePart(SlotIds.Piston).Succeeded);

            Assert.True(bike.TryInstallPart(SlotIds.Piston, pistonInst!).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Cylinder, cylInst!).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.Cylinder);

            // Reassemble head, spark plug, boot.
            Assert.True(bike.TryInstallPart(SlotIds.HeadGasket, headGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.CylinderHead, headInst!).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.CylinderHead);
            Assert.True(bike.TryInstallPart(SlotIds.SparkPlug, sparkPlug).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.SparkPlug);
            Assert.True(bike.TryInstallPart(SlotIds.PlugBoot, plugBoot).Succeeded);

            // Mount expansion pipe.
            Assert.True(bike.TryInstallPart(SlotIds.ExhaustGasket, exhaustGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.Exhaust, pipeInst!).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.Exhaust);

            // Reassemble intake, carb, jets (up-jetting via TrySwapPart), cables, fuel line, and filter.
            Assert.True(bike.TryInstallPart(SlotIds.IntakeGasket, intakeGasket).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.IntakeManifold, intake).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.IntakeManifold);
            Assert.True(bike.TryInstallPart(SlotIds.Carburetor, carb).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.Carburetor);
            Assert.True(bike.TryInstallPart(SlotIds.PilotJet, pilotJet).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.MainJet, mainJetStock).Succeeded);
            Assert.True(bike.TrySwapPart(SlotIds.MainJet, jetInst!).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.FuelLine, fuelLine).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.ThrottleCable, throttleCable).Succeeded);
            Assert.True(bike.TryInstallPart(SlotIds.AirFilter, airFilter).Succeeded);
            TorqueOwnerFasteners(bike, archetype, SlotIds.AirFilter);

            Assert.Equal(0f, bike.Assembly.ComputeAirLeakSeverity(), 5);
            Assert.Equal(0f, bike.Assembly.ComputeExhaustLeakSeverity(), 5);
            Assert.Equal("cyl_kit_70", bike.Assembly.GetInstalledPart(SlotIds.Cylinder)!.PartId);
            Assert.Equal("pipe_circuit_e50_proma", bike.Assembly.GetInstalledPart(SlotIds.Exhaust)!.PartId);
            Assert.Equal("jet_main_72", bike.Assembly.GetInstalledPart(SlotIds.MainJet)!.PartId);

            // 7. Verify measurably higher peak speed & powerband activation than stock.
            (float upgradedPeakSpeedMps, bool powerbandActivated) = MeasureFlatWotRun(bike, 555u, seconds: 45f);
            Assert.True(upgradedPeakSpeedMps > stockPeakSpeedMps + 2.5f,
                "Expected upgraded 70cc Maxer E50 (" + upgradedPeakSpeedMps.ToString("0.00") + " m/s) to significantly beat stock ("
                + stockPeakSpeedMps.ToString("0.00") + " m/s).");
            Assert.True(powerbandActivated, "Expected expansion pipe powerband to activate on upgraded 70cc WOT run.");
        }

        private static void RemoveOwnerFasteners(BikeSimulation bike, BikeArchetypeSpec archetype, string ownerSlotId)
        {
            foreach (FastenerSpec f in archetype.Fasteners.Where(x => string.Equals(x.OwnerSlotId, ownerSlotId, StringComparison.Ordinal)))
            {
                WrenchActionResult res = bike.TryRemoveFastener(f.FastenerId);
                Assert.True(res.Succeeded, ownerSlotId + " / " + f.FastenerId + ": " + res.Message);
            }
        }

        private static void TorqueOwnerFasteners(BikeSimulation bike, BikeArchetypeSpec archetype, string ownerSlotId)
        {
            foreach (FastenerSpec f in archetype.Fasteners.Where(x => string.Equals(x.OwnerSlotId, ownerSlotId, StringComparison.Ordinal)))
            {
                WrenchActionResult res = bike.TryTorqueFastener(f.FastenerId, f.TargetTorqueNm);
                Assert.True(res.Succeeded, ownerSlotId + " / " + f.FastenerId + ": " + res.Message);
            }
        }

        private static BikeSimulation HydrateBikeFromSave(CatalogDatabase db, BikeSaveState saved)
        {
            var sim = new BikeSimulation(db.GetArchetype(saved.ArchetypeId), db.Parts);
            sim.SetPetcockMode(saved.FuelTank.PetcockMode);
            sim.Fuel.FuelLevelLiters = 0f;
            float premixFraction = saved.FuelTank.PremixRatio > 0f ? ThermalModel.PremixFromRatio(saved.FuelTank.PremixRatio) : 0f;
            sim.Refuel(saved.FuelTank.FuelLiters, premixFraction, saved.FuelTank.RustContamination);

            foreach (FastenerSaveState fs in saved.Fasteners)
            {
                bool installed = fs.IsStripped || fs.CurrentTorqueNm > 0f;
                WrenchActionResult r = sim.RestoreFastenerState(
                    fs.FastenerId,
                    installed,
                    fs.CurrentTorqueNm,
                    fs.IsStripped,
                    fs.IsSeized,
                    fs.HasPenetratingOil);
                Assert.True(r.Succeeded, fs.FastenerId + ": " + r.Message);
            }
            return sim;
        }

        private static (float PeakSpeedMps, bool PowerbandActivated) MeasureFlatWotRun(BikeSimulation bike, uint seed, float seconds)
        {
            const float dt = 1f / 30f;
            var rng = new SeededSimRandom(seed);
            var wot = new RideInputState(throttle: 1.0f, aeroTuck: true, roadSlopeRadians: 0f, surfaceGrip: 1.0f, surfaceRoughness: 0f);
            float peakSpeed = 0f;
            bool powerband = false;
            int steps = (int)(seconds / dt);

            for (int i = 0; i < steps; i++)
            {
                EngineTelemetrySnapshot t = bike.Step(dt, in wot, rng);
                Assert.False(float.IsNaN(t.VehicleSpeedMps) || float.IsInfinity(t.VehicleSpeedMps));
                Assert.False(t.IsHardSeized, "Unexpected hard seize during WOT run at tick " + i);
                if (t.VehicleSpeedMps > peakSpeed)
                {
                    peakSpeed = t.VehicleSpeedMps;
                }
                if (t.IsPowerbandActive)
                {
                    powerband = true;
                }
            }

            return (peakSpeed, powerband);
        }
    }
}
