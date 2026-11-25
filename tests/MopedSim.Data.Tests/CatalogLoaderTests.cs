using System.Collections.Generic;
using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    public sealed class CatalogLoaderTests
    {
        [Fact]
        public void LoadFromDirectory_LoadsAllFiveRealCatalogs_WithExpectedCounts()
        {
            CatalogDatabase db = TestCatalog.Real;

            Assert.Equal(1, db.SchemaVersion);
            Assert.Equal(((JArray)TestCatalog.ReadCatalog("parts.json")["parts"]!).Count, db.Parts.Count);
            Assert.Equal(((JArray)TestCatalog.ReadCatalog("archetypes.json")["archetypes"]!).Count, db.Archetypes.Count);
            Assert.Equal(((JArray)TestCatalog.ReadCatalog("faults.json")["faults"]!).Count, db.Faults.Count);
            Assert.Equal(((JArray)TestCatalog.ReadCatalog("zones.json")["zones"]!).Count, db.Zones.Count);
            Assert.Equal(((JArray)TestCatalog.ReadCatalog("jobs.json")["jobs"]!).Count, db.Jobs.Count);

            Assert.Equal(124, db.Parts.Count);
            Assert.Equal(3, db.Archetypes.Count);
            Assert.Equal(10, db.Faults.Count);
            Assert.Equal(4, db.Zones.Count);
            Assert.Equal(9, db.Jobs.Count);

            Assert.Equal(db.Parts.Count, db.PartMeta.Count);
            Assert.Equal(db.Archetypes.Count, db.ArchetypeMeta.Count);
            Assert.Equal(db.Zones.Count, db.ZoneMeta.Count);
            Assert.Equal(db.Jobs.Count, db.JobMeta.Count);
            Assert.Equal(db.Faults.Count, db.FaultMeta.Count);
        }

        [Fact]
        public void Validate_RealCatalogs_IsValid()
        {
            CatalogValidationReport report = CatalogLoader.Validate(TestCatalog.Real);
            Assert.True(report.IsValid, report.ToString());
            Assert.Empty(report.Errors);
        }

        [Fact]
        public void LoadAndValidateFromDirectory_RealCatalogs_Succeeds()
        {
            CatalogDatabase db = CatalogLoader.LoadAndValidateFromDirectory(TestCatalog.CatalogsDir);
            Assert.Equal(3, db.Archetypes.Count);
        }

        [Fact]
        public void Lookups_ReturnCatalogEntries_AndRejectUnknownIds()
        {
            CatalogDatabase db = TestCatalog.Real;

            Assert.Equal("Maxer E50", db.GetArchetype(ArchetypeIds.MaxerE50).DisplayName);
            Assert.Equal(FaultType.FouledPlug, db.GetFault(FaultIds.FouledPlug).FaultType);
            Assert.Equal("pipe_circuit_e50_proma", db.GetPart("pipe_circuit_e50_proma").Id);
            Assert.Equal("Zone_WestOakland", db.GetZone("Zone_WestOakland").Id);
            Assert.Equal("gig_west_oakland_coffee", db.GetJob("gig_west_oakland_coffee").Id);

            Assert.True(db.TryGetPart("plug_b7hs", out var plug));
            Assert.NotNull(plug);
            Assert.False(db.TryGetPart("no_such_part", out var missing));
            Assert.Null(missing);

            Assert.Throws<KeyNotFoundException>(() => db.GetPart("no_such_part"));
            Assert.Throws<KeyNotFoundException>(() => db.GetArchetype("arch_nope"));
            Assert.Throws<KeyNotFoundException>(() => db.GetFault("fault_nope"));
            Assert.Throws<KeyNotFoundException>(() => db.GetZone("Zone_Nope"));
            Assert.Throws<KeyNotFoundException>(() => db.GetJob("gig_nope"));
        }

        [Fact]
        public void GetPartsByCategory_CoreEnumAndCatalogString_ReturnCatalogOrder()
        {
            CatalogDatabase db = TestCatalog.Real;

            IReadOnlyList<PartDefinition> jets = db.GetPartsByCategory(PartCategory.MainJet);
            Assert.Equal(19, jets.Count);
            Assert.Equal("jet_main_52", jets[0].Id);
            Assert.Equal("jet_main_92", jets[jets.Count - 1].Id);

            Assert.Equal(new[] { "sprocket_front_11t", "sprocket_front_13t", "sprocket_front_16t" },
                db.GetPartsByCategory(PartCategory.SprocketFront).Select(p => p.Id).ToArray());
            Assert.Equal(new[] { "sprocket_rear_45t", "sprocket_rear_38t" },
                db.GetPartsByCategory(PartCategory.SprocketRear).Select(p => p.Id).ToArray());
            Assert.Equal(new[] { "brake_drum_stock", "brake_drum_ebc", "brake_disc_front_kit" },
                db.GetPartsByCategory("Brake").Select(p => p.Id).ToArray());
            Assert.Equal(20, db.GetPartsByCategory(PartCategory.ToolRollItem).Count);
        }

        [Fact]
        public void GetJobsForZone_ReturnsJobsStartingOrEndingInZone()
        {
            CatalogDatabase db = TestCatalog.Real;
            JArray rawJobs = (JArray)TestCatalog.ReadCatalog("jobs.json")["jobs"]!;

            foreach (string zoneId in db.Zones.Keys)
            {
                string[] expected = rawJobs
                    .Where(j => (string?)j["startZoneId"] == zoneId || (string?)j["targetZoneId"] == zoneId)
                    .Select(j => (string)j["id"]!)
                    .ToArray();
                Assert.Equal(expected, db.GetJobsForZone(zoneId).Select(j => j.Id).ToArray());
            }
            Assert.Empty(db.GetJobsForZone("Zone_Nowhere"));
        }

        [Fact]
        public void PartStats_AreMappedFromCatalogKeys()
        {
            CatalogDatabase db = TestCatalog.Real;

            PartStats jet = db.GetPart("jet_main_68").Stats;
            Assert.Equal(68, jet.MainJetSize);
            Assert.Equal(PartCategory.PilotJet, db.GetPart("pilot_jet_35").Category);
            Assert.Equal(35, db.GetPart("pilot_jet_35").Stats.PilotJetSize);

            PartStats carb = db.GetPart("carb_sha_15").Stats;
            Assert.Equal(15f, carb.VenturiMm);
            Assert.Equal(68, carb.MainJetSize);
            Assert.Equal(35, carb.PilotJetSize);

            PartStats cyl = db.GetPart("cyl_kit_70").Stats;
            Assert.Equal(68.4f, cyl.DisplacementCc);
            Assert.Equal(45f, cyl.BoreMm);
            Assert.Equal(1.18f, cyl.CoolingFinEfficiency);
            Assert.Equal(11.4f, db.GetPart("head_highcomp_70").Stats.CompressionRatio);

            PartStats pipe = db.GetPart("pipe_circuit_e50_proma").Stats;
            Assert.Equal(1.18f, pipe.FlowEfficiency);
            Assert.Equal(4800f, pipe.PeakRpmStart);
            Assert.Equal(7900f, pipe.PeakRpmEnd);
            Assert.Equal(92f, pipe.NoiseDb);
            Assert.Equal(new[] { 2500f, 5500f, 7800f, 9500f }, pipe.TorqueCurve.Select(p => p.Rpm).ToArray());
            Assert.Equal(new[] { 0.85f, 1.24f, 1.16f, 0.70f }, pipe.TorqueCurve.Select(p => p.Multiplier).ToArray());

            Assert.Equal(IgnitionType.Points6V, db.GetPart("stator_points_6v").Stats.IgnitionType);
            Assert.Equal(95f, db.GetPart("stator_points_6v").Stats.CondenserHeatThresholdC);
            Assert.Equal(0.35f, db.GetPart("stator_points_6v").Stats.InitialPointsGapMm);
            Assert.Equal(IgnitionType.Cdi12V, db.GetPart("stator_cdi_12v").Stats.IgnitionType);
            Assert.Equal(11500f, db.GetPart("stator_cdi_12v").Stats.MaxRpmRating);

            Assert.Equal(7, db.GetPart("plug_b7hs").Stats.HeatRange);
            Assert.Equal(3.8f, db.GetPart("tank_stock").Stats.TankCapacityLiters);
            Assert.Equal(0.6f, db.GetPart("tank_stock").Stats.ReserveLiters);
            Assert.Equal(0.18f, db.GetPart("filter_stock_box").Stats.AirFilterRestriction);

            PartDefinition clutch = db.GetPart("clutch_e50_stock");
            Assert.Equal(PartCategory.Clutch, clutch.Category);
            Assert.Equal(DrivetrainType.SingleSpeedCentrifugal, clutch.Stats.DrivetrainType);
            Assert.Equal(2900f, clutch.Stats.ClutchEngageRpm);
            Assert.Equal(1.85f, clutch.Stats.GearRatio1);
            Assert.Equal(PartCategory.Variator, db.GetPart("variator_pa50_stock").Category);
            Assert.Equal(DrivetrainType.VariatedBelt, db.GetPart("variator_pa50_stock").Stats.DrivetrainType);
            PartDefinition gearbox = db.GetPart("gearbox_a35_2spd");
            Assert.Equal(PartCategory.Gearbox, gearbox.Category);
            Assert.Equal(DrivetrainType.TwoSpeedAuto, gearbox.Stats.DrivetrainType);
            Assert.Equal(2.45f, gearbox.Stats.GearRatio1);
            Assert.Equal(1.38f, gearbox.Stats.GearRatio2);
            Assert.Equal(5100f, gearbox.Stats.ShiftRpmThreshold);

            Assert.Equal(16, db.GetPart("sprocket_front_16t").Stats.FrontSprocketTeeth);
            Assert.Equal(38, db.GetPart("sprocket_rear_38t").Stats.RearSprocketTeeth);
            Assert.Equal(0.275f, db.GetPart("wheel_16_spoke").Stats.WheelRadiusM);
            Assert.Equal(1, db.GetPart("rack_rear_wire").Stats.CargoSlots);

            PartDefinition wrench = db.GetPart("SparkPlugWrench");
            Assert.Equal(PartCategory.ToolRollItem, wrench.Category);
            Assert.True(wrench.Stats.IsTool);
            Assert.False(wrench.Stats.IsSpare);
            Assert.Equal("SparkPlugWrench", wrench.Stats.ToolCapabilityTag);
            Assert.True(db.GetPart("SparePlug").Stats.IsSpare);
        }

        [Fact]
        public void PartMetadata_KeepsCatalogOnlyFields()
        {
            CatalogDatabase db = TestCatalog.Real;
            PartMetadata frame = db.PartMeta["frame_maxer_e50"];

            Assert.Equal("Frame", frame.CatalogCategory);
            Assert.True(frame.Has3dMesh);
            Assert.Equal("Models/Parts/Frames/frame_maxer_e50.glb", frame.MeshAssetPath);
            Assert.Equal("Major", frame.AssetClass);
            Assert.Contains("SOCKET_FORK_STEERER", frame.RequiredSockets);
            Assert.Equal("#C42828", frame.TextStats["stockPaintHex"]);
            Assert.Equal(1.15, frame.NumericStats["wheelbaseM"], 6);
            Assert.False(string.IsNullOrEmpty(frame.Description));

            Assert.True(db.PartMeta["carb_sha_15"].FlagStats["hasChoke"]);
            Assert.Equal(2, db.PartMeta["TorqueWrench"].ToolRollSlots);
            Assert.Equal("Brake", db.PartMeta["brake_drum_ebc"].CatalogCategory);
        }

        [Fact]
        public void ArchetypeSpecs_MapSlotsFastenersAndDefaults_Verbatim()
        {
            CatalogDatabase db = TestCatalog.Real;
            JArray rawArchetypes = (JArray)TestCatalog.ReadCatalog("archetypes.json")["archetypes"]!;

            foreach (JObject raw in rawArchetypes.Cast<JObject>())
            {
                BikeArchetypeSpec spec = db.GetArchetype((string)raw["id"]!);
                JArray rawSlots = (JArray)raw["mountSlots"]!;
                JArray rawFasteners = (JArray)raw["fasteners"]!;

                Assert.Equal(rawSlots.Select(s => (string)s["slotId"]!).ToArray(), spec.MountSlots.Select(s => s.SlotId).ToArray());
                Assert.Equal(rawFasteners.Select(f => (string)f["fastenerId"]!).ToArray(), spec.Fasteners.Select(f => f.FastenerId).ToArray());
                Assert.Equal((float)raw["wheelbaseMeters"]!, spec.WheelbaseM);

                foreach (JObject rawSlot in rawSlots.Cast<JObject>())
                {
                    MountSlotSpec slot = spec.MountSlots.Single(s => s.SlotId == (string)rawSlot["slotId"]!);
                    Assert.Equal((string?)rawSlot["parentSlotId"], slot.ParentSlotId);
                    Assert.Equal(rawSlot["acceptedInterfaces"]!.Select(t => (string)t!).ToArray(), slot.RequiredInterfaces);
                    Assert.Equal(rawSlot["blockedBy"]!.Select(t => (string)t!).ToArray(), slot.BlockedBySlotIds);

                    string? defaultPart = (string?)rawSlot["defaultPartId"];
                    if (defaultPart == null)
                    {
                        Assert.False(spec.DefaultPartIds.ContainsKey(slot.SlotId));
                    }
                    else
                    {
                        Assert.Equal(defaultPart, spec.DefaultPartIds[slot.SlotId]);
                    }

                    foreach (string fastenerId in rawSlot["fastenerIds"]!.Select(t => (string)t!))
                    {
                        Assert.Equal(slot.SlotId, spec.Fasteners.Single(f => f.FastenerId == fastenerId).OwnerSlotId);
                    }
                }

                foreach (JObject rawFastener in rawFasteners.Cast<JObject>())
                {
                    FastenerSpec f = spec.Fasteners.Single(x => x.FastenerId == (string)rawFastener["fastenerId"]!);
                    Assert.Equal((float)rawFastener["targetTorqueNm"]!, f.TargetTorqueNm);
                    Assert.Equal((float)rawFastener["toleranceNm"]!, f.ToleranceNm);
                    Assert.Equal((float)rawFastener["stripThresholdNm"]!, f.StripThresholdNm);
                    Assert.Equal((float)rawFastener["vibrationLoosenRate"]!, f.VibrationLoosenRate);
                    Assert.Equal((bool)rawFastener["defaultSeized"]!, f.DefaultSeized);
                }
            }

            Assert.Equal(DrivetrainType.SingleSpeedCentrifugal, db.GetArchetype(ArchetypeIds.MaxerE50).DrivetrainType);
            Assert.Equal(DrivetrainType.VariatedBelt, db.GetArchetype(ArchetypeIds.BramblePA50).DrivetrainType);
            Assert.Equal(DrivetrainType.TwoSpeedAuto, db.GetArchetype(ArchetypeIds.TargaA35).DrivetrainType);
            Assert.Equal(0.275f, db.GetArchetype(ArchetypeIds.MaxerE50).WheelRadiusM);
        }

        [Fact]
        public void CatalogSlotAndFastenerIds_StayVerbatim_AndCoveredByCoreContract()
        {
            CatalogDatabase db = TestCatalog.Real;

            foreach (BikeArchetypeSpec spec in db.ArchetypeList)
            {
                var slotIds = new HashSet<string>(spec.MountSlots.Select(s => s.SlotId));
                var fastenerIds = new HashSet<string>(spec.Fasteners.Select(f => f.FastenerId));

                // Catalog / Blender naming is kept verbatim and Core's constants use the same canonical strings.
                Assert.Contains(SlotIds.Frame, slotIds);
                Assert.Equal("SLOT_FRAME", SlotIds.Frame);
                Assert.Contains(FastenerIds.RearAxleL, fastenerIds);
                Assert.Contains(FastenerIds.RearAxleR, fastenerIds);

                foreach (KeyValuePair<string, PartCategory> required in CoreIdContract.RequiredSlots)
                {
                    Assert.Equal(required.Value, spec.MountSlots.Single(s => s.SlotId == required.Key).AcceptedCategory);
                }
                Assert.Equal(CoreIdContract.DrivetrainSideCategory(spec.DrivetrainType),
                    spec.MountSlots.Single(s => s.SlotId == SlotIds.DrivetrainSide).AcceptedCategory);
                foreach (string fastenerId in CoreIdContract.RequiredFastenerIds)
                {
                    Assert.Contains(fastenerId, fastenerIds);
                }
                if (spec.DrivetrainType != DrivetrainType.VariatedBelt)
                {
                    Assert.Contains(SlotIds.FrontSprocket, slotIds);
                    Assert.Contains(SlotIds.RearSprocket, slotIds);
                }
            }

            Assert.Equal("SLOT_VARIATOR_BELT", SlotIds.VariatorBelt);
            Assert.Contains(SlotIds.VariatorBelt, db.GetArchetype(ArchetypeIds.BramblePA50).MountSlots.Select(s => s.SlotId));
            Assert.Equal(PartCategory.DriveBelt,
                db.GetArchetype(ArchetypeIds.BramblePA50).MountSlots.Single(s => s.SlotId == SlotIds.VariatorBelt).AcceptedCategory);
        }

        [Fact]
        public void CoreSlotAndFastenerConstants_AreAllLiveCatalogIds_AndEveryCatalogSlotHasAConstant()
        {
            CatalogDatabase db = TestCatalog.Real;
            var catalogSlots = new HashSet<string>(db.ArchetypeList.SelectMany(a => a.MountSlots).Select(s => s.SlotId));
            var catalogFasteners = new HashSet<string>(db.ArchetypeList.SelectMany(a => a.Fasteners).Select(f => f.FastenerId));
            string[] coreSlots = ConstStrings(typeof(SlotIds));
            string[] coreFasteners = ConstStrings(typeof(FastenerIds));

            // No dead Core constants: every SlotIds/FastenerIds value is mounted/declared by at least one catalog archetype.
            Assert.Equal(System.Array.Empty<string>(), coreSlots.Where(id => !catalogSlots.Contains(id)).ToArray());
            Assert.Equal(System.Array.Empty<string>(), coreFasteners.Where(id => !catalogFasteners.Contains(id)).ToArray());

            // Every catalog slot has a Core constant, so Unity bindings and Core code never need raw slot strings.
            Assert.Equal(System.Array.Empty<string>(), catalogSlots.Where(id => !coreSlots.Contains(id)).OrderBy(x => x).ToArray());
            Assert.Equal(coreSlots.Length, coreSlots.Distinct().Count());
            Assert.Equal(coreFasteners.Length, coreFasteners.Distinct().Count());
        }

        [Fact]
        public void EveryCatalogCategory_MapsToADedicatedCorePartCategory()
        {
            CatalogDatabase db = TestCatalog.Real;
            var expected = new Dictionary<string, PartCategory>
            {
                { "FuelLine", PartCategory.FuelLine },
                { "VariatorRoller", PartCategory.VariatorRoller },
                { "Fender", PartCategory.Fender },
                { "Brake", PartCategory.Brake },
                { "Cable", PartCategory.Cable },
                { "PremixOil", PartCategory.PremixOil },
                { "GarageStation", PartCategory.GarageStation },
            };

            foreach (KeyValuePair<string, PartCategory> pair in expected)
            {
                IReadOnlyList<PartDefinition> parts = db.GetPartsByCategory(pair.Key);
                Assert.True(parts.Count > 0, "catalog has no '" + pair.Key + "' parts");
                Assert.All(parts, p => Assert.Equal(pair.Value, p.Category));
                Assert.Equal(parts.Select(p => p.Id).OrderBy(x => x), db.GetPartsByCategory(pair.Value).Select(p => p.Id).OrderBy(x => x));
            }

            // Nothing in the real catalog falls back to the generic Consumable bucket any more.
            Assert.Empty(db.GetPartsByCategory(PartCategory.Consumable));
        }

        private static string[] ConstStrings(System.Type type)
        {
            return type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToArray();
        }

        [Fact]
        public void ArchetypeDryMass_IsResidualAfterStockParts_SoAssembledMassIsNotDoubleCounted()
        {
            CatalogDatabase db = TestCatalog.Real;
            JObject rawParts = TestCatalog.ReadCatalog("parts.json");

            foreach (BikeArchetypeSpec spec in db.ArchetypeList)
            {
                ArchetypeMetadata meta = db.ArchetypeMeta[spec.Id];
                JObject rawArch = TestCatalog.FindById(TestCatalog.ReadCatalog("archetypes.json"), "archetypes", spec.Id);
                Assert.Equal((float)rawArch["baseDryMassKg"]!, meta.CatalogDryMassKg);

                float expectedStock = 0f;
                foreach (JObject slot in ((JArray)rawArch["mountSlots"]!).Cast<JObject>())
                {
                    string? partId = (string?)slot["defaultPartId"];
                    if (partId != null)
                    {
                        expectedStock += (float)TestCatalog.FindById(rawParts, "parts", partId)["massKg"]!;
                    }
                }
                Assert.Equal(expectedStock, meta.StockPartsMassKg, 3);

                float expectedResidual = System.Math.Max(CatalogLoader.MinResidualHardwareMassKg, meta.CatalogDryMassKg - meta.StockPartsMassKg);
                Assert.Equal(expectedResidual, spec.BaseDryMassKg, 4);

                // Stock parts sum slightly below the authored whole-bike mass, so the residual is real frame hardware/wiring
                // (2-6 kg) and the MinResidualHardwareMassKg floor never inflates the bike.
                float residual = meta.CatalogDryMassKg - meta.StockPartsMassKg;
                Assert.True(residual > CatalogLoader.MinResidualHardwareMassKg,
                    spec.Id + ": stock parts " + meta.StockPartsMassKg + " kg leave only " + residual + " kg residual (floor engaged)");
                Assert.InRange(residual, 2.0f, 6.0f);
                Assert.Equal(residual, spec.BaseDryMassKg, 3);

                var sim = new BikeSimulation(spec, db.Parts);
                float assembledDryMassKg = spec.BaseDryMassKg + sim.Assembly.GetMountedPartsMassKg();
                Assert.Equal(meta.CatalogDryMassKg, assembledDryMassKg, 2);

                // Realistic vintage 50cc moped dry mass (Puch Maxi / Hobbit / Tomos class: ~46-54 kg).
                Assert.InRange(assembledDryMassKg, 46.5f, 54f);
                Assert.InRange(meta.CatalogDryMassKg, 46.5f, 52.5f);
            }
        }

        [Fact]
        public void Metadata_ZonesJobsFaultsAndArchetypes_AreMapped()
        {
            CatalogDatabase db = TestCatalog.Real;

            ZoneEnvironmentSpec mission = db.GetZone("Zone_SF_Mission");
            ZoneMetadata missionMeta = db.ZoneMeta["Zone_SF_Mission"];
            Assert.Equal("Zone_SF_Mission", missionMeta.SceneName);
            Assert.True(mission.AmbientAirDensity > 0f);
            Assert.NotEmpty(missionMeta.PrimarySurfaces);
            Assert.NotEmpty(missionMeta.ConnectedZones);
            Assert.True(missionMeta.TowFeeUsd > 0m);

            ZoneMetadata westOakland = db.ZoneMeta["Zone_WestOakland"];
            Assert.Equal(25m, westOakland.TowFeeUsd);
            Assert.False(westOakland.HasMuniRails);
            Assert.Equal("lm_west_oakland_garage", westOakland.Landmarks[0].Id);
            Assert.Equal(180f, westOakland.Landmarks[1].Position.X);

            JobDefinition coffee = db.GetJob("gig_west_oakland_coffee");
            JobMetadata coffeeMeta = db.JobMeta[coffee.Id];
            Assert.Equal("Zone_WestOakland", coffee.ZoneId);
            Assert.Equal(1, coffee.RequiredCargoSlots);
            Assert.Equal(35m, coffee.RewardUsd);
            Assert.Equal(150f, coffee.TimeLimitSeconds);
            Assert.Equal(1, coffeeMeta.RequiredCargoSlots);
            Assert.Equal(2.5f, coffeeMeta.RequiredCargoKg);
            Assert.Equal(new[] { "rack_rear_wire" }, coffeeMeta.RecommendedPartIds);
            Assert.Equal(new[] { 1, 2 }, coffeeMeta.Checkpoints.Select(c => c.Order).ToArray());

            FaultDefinition plug = db.GetFault(FaultIds.FouledPlug);
            Assert.True(plug.IsFieldRepairable);
            Assert.Equal(new[] { "SparkPlugWrench", "WireBrush" }, plug.RequiredTools);
            Assert.Equal(new[] { "SparePlug" }, plug.RequiredSpares);
            FaultMetadata plugMeta = db.FaultMeta[FaultIds.FouledPlug];
            Assert.Equal(2, plugMeta.RepairOptions.Count);
            Assert.Equal(11.2, plugMeta.NumericTriggerRules["afrBelow"], 6);
            Assert.False(db.GetFault(FaultIds.HardSeize).IsFieldRepairable);
            Assert.True(db.FaultMeta[FaultIds.CondenserHeatSoak].FlagTriggerRules["requiresPointsIgnition"]);

            ArchetypeMetadata maxer = db.ArchetypeMeta[ArchetypeIds.MaxerE50];
            Assert.Equal("SingleSpeedCentrifugal", maxer.DrivetrainArchitecture);
            Assert.Equal(6, maxer.DefaultToolRollSlots);
            Assert.Equal(3.2f, maxer.DefaultFuelMix.FuelLiters);
            Assert.Equal(PetcockMode.On, maxer.DefaultFuelMix.PetcockMode);
            Assert.Equal("SOCKET_FORK_STEERER", maxer.GetSlot("SLOT_FORK_ASSEMBLY")!.AttachSocket);
            Assert.True(maxer.GetSlot(SlotIds.Piston)!.RequiresOrientationCheck);
            Assert.True(maxer.GetSlot(SlotIds.MainJet)!.RequiresCarbBowlOpen);
            Assert.Equal("Wrench15mm", maxer.GetFastener(FastenerIds.StemNut)!.RequiredToolId);
        }
    }
}
