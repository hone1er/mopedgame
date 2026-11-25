using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    /// <summary>
    /// Proves the real catalog archetypes drive MopedSim.Core end to end. Core lookups are null-safe, so these tests check
    /// occupancy, leak-free assembly, and subsystem sync against the raw JSON instead of merely "no exception".
    /// </summary>
    public sealed class CatalogSimulationTests
    {
        public static readonly TheoryData<string> RealArchetypes = new TheoryData<string>
        {
            ArchetypeIds.MaxerE50,
            ArchetypeIds.BramblePA50,
            ArchetypeIds.TargaA35,
        };

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void BikeSimulation_FromCatalog_MountsEveryStockPartAndFastener(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            BikeArchetypeSpec spec = db.GetArchetype(archetypeId);
            var sim = new BikeSimulation(spec, db.Parts);
            JObject rawArch = TestCatalog.FindById(TestCatalog.ReadCatalog("archetypes.json"), "archetypes", archetypeId);

            foreach (JObject rawSlot in ((JArray)rawArch["mountSlots"]!).Cast<JObject>())
            {
                string slotId = (string)rawSlot["slotId"]!;
                string? defaultPart = (string?)rawSlot["defaultPartId"];
                PartInstance? installed = sim.Assembly.GetInstalledPart(slotId);
                if (defaultPart == null)
                {
                    Assert.Null(installed);
                    continue;
                }
                Assert.True(installed != null, archetypeId + ": " + slotId + " should hold stock part " + defaultPart);
                Assert.Equal(defaultPart, installed!.PartId);

                foreach (string fastenerId in rawSlot["fastenerIds"]!.Select(t => (string)t!))
                {
                    FastenerState? f = sim.Assembly.GetFastener(fastenerId);
                    Assert.True(f != null, archetypeId + ": missing fastener " + fastenerId);
                    Assert.True(f!.IsInstalled, archetypeId + ": " + fastenerId + " not installed");
                    Assert.True(f.IsProperlyTorqued, archetypeId + ": " + fastenerId + " not at spec");
                    Assert.Equal(slotId, f.OwnerSlotId);
                }
            }

            Assert.Equal(spec.MountSlots.Count, sim.Assembly.Slots.Count);
            Assert.Equal(spec.Fasteners.Count, sim.Assembly.Fasteners.Count);
            Assert.Equal(0f, sim.Assembly.ComputeAirLeakSeverity());
            Assert.Equal(0f, sim.Assembly.ComputeExhaustLeakSeverity());
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void BikeSimulation_FromCatalog_SyncsSubsystemsToStockPartStats(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            BikeArchetypeSpec spec = db.GetArchetype(archetypeId);
            var sim = new BikeSimulation(spec, db.Parts);
            JObject rawParts = TestCatalog.ReadCatalog("parts.json");
            JObject rawArch = TestCatalog.FindById(TestCatalog.ReadCatalog("archetypes.json"), "archetypes", archetypeId);

            JObject StockStats(string slotId)
            {
                string partId = (string)TestCatalog.FindSlot(rawArch, slotId)["defaultPartId"]!;
                return (JObject)TestCatalog.FindById(rawParts, "parts", partId)["stats"]!;
            }

            Assert.Equal(spec.DrivetrainType, sim.Drivetrain.Type);
            Assert.Equal((float)StockStats(SlotIds.DrivetrainSide)["clutchEngageRpm"]!, sim.Drivetrain.ClutchEngageRpm);

            JObject ignition = StockStats(SlotIds.Ignition);
            Assert.Equal((bool)ignition["isCdi"]! ? IgnitionType.Cdi12V : IgnitionType.Points6V, sim.Ignition.Type);
            Assert.Equal((float)ignition["maxCleanRpm"]!, sim.Ignition.MaxRpmRating);

            Assert.Equal((float)StockStats(SlotIds.FuelTank)["capacityLiters"]!, sim.Fuel.TankCapacityLiters);
            Assert.Equal((float)StockStats(SlotIds.FuelTank)["reserveThresholdLiters"]!, sim.Fuel.ReserveThresholdLiters);
            Assert.Equal((int)StockStats(SlotIds.SparkPlug)["heatRange"]!, sim.SparkPlug.HeatRange);
            Assert.True(sim.SparkPlug.IsInstalled);

            if (spec.DrivetrainType != DrivetrainType.VariatedBelt)
            {
                Assert.Equal((int)StockStats(SlotIds.FrontSprocket)["teethCount"]!, sim.Drivetrain.FrontSprocketTeeth);
                Assert.Equal((int)StockStats(SlotIds.RearSprocket)["teethCount"]!, sim.Drivetrain.RearSprocketTeeth);
            }

            Assert.Equal(archetypeId == ArchetypeIds.TargaA35 ? IgnitionType.Cdi12V : IgnitionType.Points6V, sim.Ignition.Type);
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void BikeSimulation_FromCatalog_RunsSixtySecondsAtSixtyPercentThrottle_WithoutFoulingOrStalling(string archetypeId)
        {
            // Stock catalog jetting (12mm Bing #56, 15mm SHA #68 / #66) must sit near optimal AFR for its venturi: the bike keeps
            // running the full minute, never four-strokes, keeps a clean chocolate-brown plug, and actually rides.
            CatalogDatabase db = TestCatalog.Real;
            var sim = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);
            var rng = new SeededSimRandom(20261125u);
            var input = new RideInputState(throttle: 0.6f);

            float maxRpm = 0f;
            float maxSpeed = 0f;
            float minAfr = float.MaxValue;
            float maxAfr = float.MinValue;
            for (int i = 0; i < 3600; i++)
            {
                EngineTelemetrySnapshot t = sim.Step(1f / 60f, in input, rng);
                Assert.True(IsFinite(t.Rpm) && IsFinite(t.ChtCelsius) && IsFinite(t.VehicleSpeedMps) && IsFinite(t.EffectiveAfr)
                    && IsFinite(t.RearWheelTorqueNm) && IsFinite(t.FuelLitersRemaining),
                    archetypeId + ": non-finite telemetry at tick " + i);
                Assert.True(t.IsRunning && !t.IsStalled, archetypeId + ": stalled at " + (i / 60f).ToString("0.0") + " s (AFR " + t.EffectiveAfr + ")");
                Assert.False(t.IsFourStroking, archetypeId + ": four-stroking rich at tick " + i + " (AFR " + t.EffectiveAfr + ")");
                Assert.False(t.IsSeized, archetypeId + ": seized at tick " + i);
                maxRpm = System.Math.Max(maxRpm, t.Rpm);
                maxSpeed = System.Math.Max(maxSpeed, t.VehicleSpeedMps);
                minAfr = System.Math.Min(minAfr, t.EffectiveAfr);
                maxAfr = System.Math.Max(maxAfr, t.EffectiveAfr);
            }

            Assert.Equal(60f, sim.TotalSimulatedSeconds, 2);
            Assert.True(minAfr >= 12.4f && maxAfr <= 13.5f, archetypeId + ": AFR ranged " + minAfr + ".." + maxAfr + " (want ~12.5-13.5)");
            Assert.True(sim.SparkPlug.FoulingLevel < 0.10f, archetypeId + ": plug fouling " + sim.SparkPlug.FoulingLevel);
            Assert.Equal(PlugColorZone.ChocolateBrown, sim.InspectSparkPlug().ColorZone);
            Assert.InRange(sim.LatestTelemetry.ChtCelsius, 70f, 185f);
            Assert.True(maxRpm > sim.Drivetrain.ClutchEngageRpm, archetypeId + ": engine never reached clutch engagement (max " + maxRpm + " rpm)");
            Assert.True(maxSpeed > 8f, archetypeId + ": stock bike never exceeded 8 m/s (max " + maxSpeed + ")");
            Assert.True(sim.OdometerMeters > 400f, archetypeId + ": odometer only " + sim.OdometerMeters + " m");
        }

        [Theory]
        [InlineData(ArchetypeIds.MaxerE50, 12f)]
        [InlineData(ArchetypeIds.BramblePA50, 15f)]
        [InlineData(ArchetypeIds.TargaA35, 15f)]
        public void StockCatalogMainJet_SitsNearCoreIdealForItsVenturi(string archetypeId, float expectedVenturiMm)
        {
            CatalogDatabase db = TestCatalog.Real;
            var sim = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);
            PartStats carb = sim.Assembly.GetInstalledPart(SlotIds.Carburetor)!.Definition.Stats;
            int jet = sim.Assembly.GetInstalledPart(SlotIds.MainJet)!.Definition.Stats.MainJetSize;
            float displacement = sim.Assembly.GetInstalledPart(SlotIds.Cylinder)!.Definition.Stats.DisplacementCc;
            float pipeFlow = sim.Assembly.GetInstalledPart(SlotIds.Exhaust)!.Definition.Stats.FlowEfficiency;

            Assert.Equal(expectedVenturiMm, carb.VenturiMm);
            float ideal = EngineSimulation.ComputeIdealMainJet(displacement, carb.VenturiMm, pipeFlow);
            Assert.InRange(jet / ideal, 0.90f, 1.02f);
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void BikeSimulation_FromCatalog_IsDeterministic(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            var simA = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);
            var simB = new BikeSimulation(CatalogLoader.LoadFromDirectory(TestCatalog.CatalogsDir).GetArchetype(archetypeId),
                CatalogLoader.LoadFromDirectory(TestCatalog.CatalogsDir).Parts);
            var rngA = new SeededSimRandom(42u);
            var rngB = new SeededSimRandom(42u);

            EngineTelemetrySnapshot a = default;
            EngineTelemetrySnapshot b = default;
            for (int i = 0; i < 1800; i++)
            {
                var input = new RideInputState(throttle: (i % 600) < 450 ? 0.9f : 0.1f, surfaceRoughness: (i % 200) < 20 ? 0.6f : 0.05f);
                a = simA.Step(1f / 60f, in input, rngA);
                b = simB.Step(1f / 60f, in input, rngB);
            }

            Assert.Equal(a.Rpm, b.Rpm);
            Assert.Equal(a.ChtCelsius, b.ChtCelsius);
            Assert.Equal(a.VehicleSpeedMps, b.VehicleSpeedMps);
            Assert.Equal(a.EffectiveAfr, b.EffectiveAfr);
            Assert.Equal(a.FuelLitersRemaining, b.FuelLitersRemaining);
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void Wrenching_BrakeAndCableSlots_UseDedicatedCoreCategories(string archetypeId)
        {
            // "Brake", "Cable" and "Fender" map to their own PartCategory values, so Core's category check keeps them apart.
            CatalogDatabase db = TestCatalog.Real;
            var sim = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);

            WrenchActionResult blocked = sim.TryRemovePart("SLOT_REAR_BRAKE");
            Assert.False(blocked.Succeeded);
            Assert.Contains("SLOT_REAR_BRAKE_CABLE", blocked.BlockingNodes);

            Assert.True(sim.TryRemovePart("SLOT_REAR_BRAKE_CABLE").Succeeded);
            Assert.True(sim.TryRemovePart("SLOT_REAR_BRAKE").Succeeded);

            Assert.Equal(PartCategory.Brake, sim.Assembly.GetSlot("SLOT_REAR_BRAKE")!.Spec.AcceptedCategory);
            Assert.Equal(PartCategory.Cable, sim.Assembly.GetSlot("SLOT_REAR_BRAKE_CABLE")!.Spec.AcceptedCategory);

            // A cable or fender must not fit a brake slot; a brake upgrade must.
            Assert.Equal(WrenchActionStatus.IncompatiblePart,
                sim.TryInstallPart("SLOT_REAR_BRAKE", new PartInstance(db.GetPart("cable_brake_rear"))).Status);
            Assert.Equal(WrenchActionStatus.IncompatiblePart,
                sim.TryInstallPart("SLOT_REAR_BRAKE", new PartInstance(db.GetPart("fender_front_steel"))).Status);
            Assert.True(sim.TryInstallPart("SLOT_REAR_BRAKE", new PartInstance(db.GetPart("brake_drum_ebc"))).Succeeded);

            Assert.Equal(WrenchActionStatus.IncompatiblePart,
                sim.TryInstallPart("SLOT_REAR_BRAKE_CABLE", new PartInstance(db.GetPart("cable_throttle"))).Status);
            Assert.True(sim.TryInstallPart("SLOT_REAR_BRAKE_CABLE", new PartInstance(db.GetPart("cable_brake_rear"))).Succeeded);
            Assert.Equal("brake_drum_ebc", sim.Assembly.GetInstalledPart("SLOT_REAR_BRAKE")!.PartId);
        }

        [Theory]
        [InlineData(ArchetypeIds.MaxerE50)]
        [InlineData(ArchetypeIds.TargaA35)]
        public void Wrenching_RearSprocketSwap_ReachesCoreDrivetrain(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            var sim = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);
            Assert.Equal(45, sim.Drivetrain.RearSprocketTeeth);

            WrenchActionResult blocked = sim.TryRemovePart(SlotIds.RearSprocket);
            Assert.False(blocked.Succeeded);
            Assert.Contains(SlotIds.Chain, blocked.BlockingNodes);

            foreach (FastenerSpec f in db.GetArchetype(archetypeId).Fasteners.Where(x => x.OwnerSlotId == SlotIds.RearSprocket))
            {
                Assert.True(sim.TryRemoveFastener(f.FastenerId).Succeeded, f.FastenerId);
            }
            Assert.True(sim.TryRemovePart(SlotIds.Chain).Succeeded);
            Assert.True(sim.TryRemovePart(SlotIds.RearSprocket).Succeeded);

            // Front sprocket maps to SprocketFront and is rejected by the SprocketRear slot.
            Assert.Equal(WrenchActionStatus.IncompatiblePart,
                sim.TryInstallPart(SlotIds.RearSprocket, new PartInstance(db.GetPart("sprocket_front_16t"))).Status);
            Assert.True(sim.TryInstallPart(SlotIds.RearSprocket, new PartInstance(db.GetPart("sprocket_rear_38t"))).Succeeded);
            Assert.True(sim.TryInstallPart(SlotIds.Chain, new PartInstance(db.GetPart("chain_415_standard"))).Succeeded);

            Assert.Equal(38, sim.Drivetrain.RearSprocketTeeth);
        }

        [Fact]
        public void Wrenching_SparkPlugSwap_UpdatesHeatRange()
        {
            CatalogDatabase db = TestCatalog.Real;
            var sim = new BikeSimulation(db.GetArchetype(ArchetypeIds.MaxerE50), db.Parts);
            Assert.Equal(6, sim.SparkPlug.HeatRange);

            Assert.True(sim.TryRemovePart("SLOT_PLUG_BOOT").Succeeded);
            Assert.True(sim.TryRemoveFastener(FastenerIds.SparkPlugThread).Succeeded);
            Assert.True(sim.TryRemovePart(SlotIds.SparkPlug).Succeeded);
            Assert.True(sim.TryInstallPart(SlotIds.SparkPlug, new PartInstance(db.GetPart("plug_b8hs"))).Succeeded);

            Assert.Equal(8, sim.SparkPlug.HeatRange);
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void BikeSimulation_FromCatalog_HydratesDefaultFuelMix(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            FuelMixSpec mix = db.ArchetypeMeta[archetypeId].DefaultFuelMix;
            JObject rawMix = (JObject)TestCatalog.FindById(TestCatalog.ReadCatalog("archetypes.json"), "archetypes", archetypeId)["defaultFuelMix"]!;
            var sim = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);

            // Catalog premix is authored X:1 (50 = 50:1); Core stores the oil fraction (0.02).
            Assert.Equal((float)rawMix["premixRatio"]!, mix.PremixRatio);
            Assert.Equal(1f / (float)rawMix["premixRatio"]!, mix.PremixOilFraction, 6);
            Assert.Equal(mix.PremixOilFraction, sim.Fuel.PremixRatio, 6);
            Assert.Equal((float)rawMix["fuelLiters"]!, sim.Fuel.FuelLevelLiters, 4);
            Assert.Equal(mix.PetcockMode, sim.Fuel.PetcockMode);
            Assert.Equal(sim.Fuel.FloatBowlCapacityLiters, sim.Fuel.FloatBowlLiters);

            var idle = new RideInputState(throttle: 0.1f);
            sim.Step(1f / 60f, in idle, new SeededSimRandom(1u));
            Assert.Equal((float)rawMix["rustContamination"]!, sim.Fuel.RustContamination, 5);
        }

        [Fact]
        public void SimulationWorld_TryPurchasePart_SpendsCatalogPriceThroughCore()
        {
            CatalogDatabase db = TestCatalog.Real;
            var world = new SimulationWorld(new SeededSimRandom(3u));
            decimal start = world.CashUsd;
            PartDefinition jet = db.GetPart("jet_main_68");

            PurchaseResult bought = world.TryPurchasePart(jet);
            Assert.True(bought.Succeeded, bought.Message);
            Assert.Equal(start - jet.PriceUsd, world.CashUsd);

            PartDefinition priciest = db.PartList.OrderByDescending(p => p.PriceUsd).First();
            Assert.True(priciest.PriceUsd > world.CashUsd, "catalog needs a part pricier than starting cash for this test");
            decimal before = world.CashUsd;
            Assert.False(world.TryPurchasePart(priciest).Succeeded);
            Assert.Equal(before, world.CashUsd);
        }

        [Theory]
        [MemberData(nameof(RealArchetypes))]
        public void SaveRoundTrip_StrippedSeizedAndRemovedFasteners_RestoreVerbatimOnFreshBike(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            var original = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);

            // Strip a head nut by over-torque, seize + oil an exhaust flange, and pull the carb clamp.
            FastenerState headNut = original.Assembly.GetFastener(FastenerIds.HeadNut1)!;
            Assert.Equal(WrenchActionStatus.FastenerStripped,
                original.TryTorqueFastener(FastenerIds.HeadNut1, headNut.StripThresholdNm + 2f).Status);
            original.Assembly.SetFastenerSeized(FastenerIds.ExhaustFlange1, true);
            Assert.True(original.ApplyPenetratingOil(FastenerIds.ExhaustFlange1).Succeeded);
            Assert.True(original.TryRemoveFastener(FastenerIds.CarbClamp).Succeeded);

            // Same projection SaveLoadBridge (MopedSim.App) writes into BikeSaveState.Fasteners, through the real serializer.
            SaveState state = SaveMigrator.CreateNewDefault(9u, db, archetypeId);
            BikeSaveState bike = state.Bikes[0];
            bike.Fasteners.Clear();
            foreach (FastenerState f in original.Assembly.Fasteners)
            {
                bike.Fasteners.Add(new FastenerSaveState
                {
                    FastenerId = f.FastenerId,
                    CurrentTorqueNm = f.CurrentTorqueNm,
                    IsStripped = f.IsStripped,
                    IsSeized = f.IsSeized,
                    HasPenetratingOil = f.PenetratingOilApplied,
                });
            }
            SaveState loaded = SaveMigrator.DeserializeAndMigrate(SaveMigrator.Serialize(state, false));

            var restored = new BikeSimulation(db.GetArchetype(archetypeId), db.Parts);
            foreach (FastenerSaveState fs in loaded.Bikes[0].Fasteners)
            {
                bool installed = fs.IsStripped || fs.CurrentTorqueNm > 0f;
                WrenchActionResult r = restored.RestoreFastenerState(
                    fs.FastenerId, installed, fs.CurrentTorqueNm, fs.IsStripped, fs.IsSeized, fs.HasPenetratingOil);
                Assert.True(r.Succeeded, fs.FastenerId + ": " + r.Message);
            }

            foreach (FastenerState expected in original.Assembly.Fasteners)
            {
                FastenerState actual = restored.Assembly.GetFastener(expected.FastenerId)!;
                Assert.True(expected.IsInstalled == actual.IsInstalled, expected.FastenerId + " installed");
                Assert.True(expected.IsStripped == actual.IsStripped, expected.FastenerId + " stripped");
                Assert.True(expected.IsSeized == actual.IsSeized, expected.FastenerId + " seized");
                Assert.True(expected.PenetratingOilApplied == actual.PenetratingOilApplied, expected.FastenerId + " oil");
                Assert.Equal(expected.CurrentTorqueNm, actual.CurrentTorqueNm, 4);
            }
            Assert.Equal(original.Assembly.ComputeAirLeakSeverity(), restored.Assembly.ComputeAirLeakSeverity(), 5);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
