using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    public sealed class SaveMigratorTests
    {
        [Fact]
        public void CreateNewDefault_WithoutCatalog_IsValidV1WithCoreDefaults()
        {
            SaveState state = SaveMigrator.CreateNewDefault(12345u);

            Assert.Empty(SaveMigrator.Validate(state));
            Assert.Equal(1, SaveState.CurrentSchemaVersion);
            Assert.Equal(SaveState.CurrentSchemaVersion, state.SchemaVersion);
            Assert.Equal(12345u, state.Seed);
            Assert.Equal(new SimulationWorld(new SeededSimRandom(1u)).CashUsd, state.CashUsd);
            Assert.Equal(SimulationWorld.BaseToolRollCapacity, state.ToolRollCapacity);
            Assert.Equal("Zone_WestOakland", state.ActiveZoneId);
            Assert.Equal(new[] { "Zone_WestOakland" }, state.UnlockedZones);

            BikeSaveState bike = Assert.Single(state.Bikes);
            Assert.Equal(state.ActiveBikeInstanceId, bike.InstanceId);
            Assert.Equal(ArchetypeIds.MaxerE50, bike.ArchetypeId);
            Assert.Empty(bike.InstalledParts);
            Assert.Empty(bike.Fasteners);

            var coreFuel = new FuelSystem();
            Assert.Equal(coreFuel.FuelLevelLiters, bike.FuelTank.FuelLiters);
            Assert.Equal(40f, bike.FuelTank.PremixRatio);
            Assert.Equal(coreFuel.PetcockMode, bike.FuelTank.PetcockMode);

            Assert.Equal(ArchetypeIds.TargaA35, SaveMigrator.CreateNewDefault(1u, ArchetypeIds.TargaA35).Bikes[0].ArchetypeId);
            Assert.Throws<ArgumentException>(() => SaveMigrator.CreateNewDefault(1u, "Maxer"));
        }

        [Theory]
        [InlineData(ArchetypeIds.MaxerE50)]
        [InlineData(ArchetypeIds.BramblePA50)]
        [InlineData(ArchetypeIds.TargaA35)]
        public void CreateNewDefault_WithCatalog_PopulatesStockBike(string archetypeId)
        {
            CatalogDatabase db = TestCatalog.Real;
            SaveState state = SaveMigrator.CreateNewDefault(7u, db, archetypeId);
            BikeArchetypeSpec spec = db.GetArchetype(archetypeId);
            ArchetypeMetadata meta = db.ArchetypeMeta[archetypeId];

            Assert.Empty(SaveMigrator.Validate(state));
            Assert.Empty(SaveMigrator.ValidateAgainstCatalog(state, db));

            BikeSaveState bike = state.Bikes[0];
            Assert.Equal(spec.DisplayName, bike.Nickname);
            Assert.Equal(spec.DefaultPartIds.Count, bike.InstalledParts.Count);
            foreach (InstalledPartSaveState installed in bike.InstalledParts)
            {
                Assert.Equal(spec.DefaultPartIds[installed.SlotId], installed.PartId);
                Assert.Equal(1f, installed.Condition);
            }
            Assert.Equal(spec.Fasteners.Select(f => f.FastenerId).ToArray(), bike.Fasteners.Select(f => f.FastenerId).ToArray());
            Assert.All(bike.Fasteners, f => Assert.Equal(spec.Fasteners.Single(s => s.FastenerId == f.FastenerId).TargetTorqueNm, f.CurrentTorqueNm));

            Assert.Equal(meta.DefaultFuelMix.FuelLiters, bike.FuelTank.FuelLiters);
            Assert.Equal(meta.DefaultFuelMix.PremixRatio, bike.FuelTank.PremixRatio);
            Assert.Equal(meta.DefaultFuelMix.RustContamination, bike.FuelTank.RustContamination);
            Assert.Equal(meta.DefaultToolRollSlots, state.ToolRollCapacity);

            string frameId = spec.DefaultPartIds["SLOT_FRAME"];
            Assert.Equal(db.PartMeta[frameId].TextStats["stockPaintHex"], bike.PaintHex);

            InstalledPartSaveState ignition = bike.InstalledParts.Single(p => p.SlotId == SlotIds.Ignition);
            float expectedGap = db.GetPart(ignition.PartId).Stats.IgnitionType == IgnitionType.Points6V ? 0.35f : 0f;
            Assert.Equal(expectedGap, ignition.PointsGapMm);

            Assert.Throws<ArgumentException>(() => SaveMigrator.CreateNewDefault(7u, db, "arch_vespa"));
        }

        [Fact]
        public void Serialize_ThenDeserialize_RoundTripsExactly()
        {
            SaveState original = BuildRichState();
            string json = SaveMigrator.Serialize(original);
            SaveState loaded = SaveMigrator.DeserializeAndMigrate(json);

            Assert.Equal(json, SaveMigrator.Serialize(loaded));
            Assert.Equal(SaveMigrator.Serialize(original, indented: false), SaveMigrator.Serialize(loaded, indented: false));

            Assert.Equal(uint.MaxValue, loaded.Seed);
            Assert.Equal(1234.56m, loaded.CashUsd);
            Assert.Equal("2026-11-25T08:30:00Z", loaded.CompletedGigs[0].LastCompletedUtc);
            Assert.Equal(PetcockMode.Reserve, loaded.Bikes[0].FuelTank.PetcockMode);
            Assert.Equal(0.42f, loaded.Bikes[0].InstalledParts[0].Wear);
            Assert.True(loaded.Bikes[0].Fasteners[0].IsSeized);
            Assert.Equal(new[] { "SparkPlugWrench", "SparePlug" }, loaded.ActiveToolRoll);
        }

        [Fact]
        public void Serialize_EmitsExactlyTheSchemaPropertyNames()
        {
            JObject schema = TestCatalog.ReadSchema("save_state.schema.json");
            JObject json = JObject.Parse(SaveMigrator.Serialize(BuildRichState()));

            AssertKeysMatchSchema(json, schema);
            JObject bikeSchema = (JObject)schema["properties"]!["bikes"]!["items"]!;
            JObject bike = (JObject)json["bikes"]![0]!;
            AssertKeysMatchSchema(bike, bikeSchema);
            AssertKeysMatchSchema((JObject)bike["fuelTank"]!, (JObject)bikeSchema["properties"]!["fuelTank"]!);
            AssertKeysMatchSchema((JObject)bike["installedParts"]![0]!, (JObject)bikeSchema["properties"]!["installedParts"]!["items"]!);
            AssertKeysMatchSchema((JObject)bike["fasteners"]![0]!, (JObject)bikeSchema["properties"]!["fasteners"]!["items"]!);
            AssertKeysMatchSchema((JObject)json["inventory"]![0]!, (JObject)schema["properties"]!["inventory"]!["items"]!);
            AssertKeysMatchSchema((JObject)json["completedGigs"]![0]!, (JObject)schema["properties"]!["completedGigs"]!["items"]!);

            Assert.Equal("Reserve", (string?)bike["fuelTank"]!["petcockMode"]);
            Assert.Equal(1, (int)json["schemaVersion"]!);
        }

        [Fact]
        public void SaveToFile_ThenLoadFromFile_PersistsAndOverwritesAtomically()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_saves_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "nested", "save_state.json");
            try
            {
                SaveState first = SaveMigrator.CreateNewDefault(99u, TestCatalog.Real);
                SaveMigrator.SaveToFile(path, first);
                Assert.True(File.Exists(path));
                Assert.Equal(SaveMigrator.Serialize(first), SaveMigrator.Serialize(SaveMigrator.LoadFromFile(path)));

                SaveState second = BuildRichState();
                SaveMigrator.SaveToFile(path, second);
                SaveState reloaded = SaveMigrator.LoadFromFile(path);
                Assert.Equal(SaveMigrator.Serialize(second), SaveMigrator.Serialize(reloaded));
                Assert.Equal(uint.MaxValue, reloaded.Seed);

                Assert.False(File.Exists(path + ".tmp"));
                Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void SaveToFile_InvalidState_ThrowsAndWritesNothing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_saves_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "save_state.json");
            SaveState bad = SaveMigrator.CreateNewDefault(1u);
            bad.Bikes[0].PaintHex = "red";
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.SaveToFile(path, bad));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void LoadFromFile_MissingFile_ThrowsFileNotFound()
        {
            string path = Path.Combine(Path.GetTempPath(), "mopedsim_missing_" + Guid.NewGuid().ToString("N") + ".json");
            Assert.Throws<FileNotFoundException>(() => SaveMigrator.LoadFromFile(path));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(99)]
        [InlineData(0)]
        [InlineData(-1)]
        public void DeserializeAndMigrate_UnsupportedSchemaVersion_Throws(int version)
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u)));
            json["schemaVersion"] = version;
            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));
            Assert.Contains("schemaVersion " + version, ex.Message);
        }

        [Fact]
        public void DeserializeAndMigrate_MissingOrNonIntegerSchemaVersion_Throws()
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u)));
            json.Remove("schemaVersion");
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));

            json["schemaVersion"] = "1";
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));

            json["schemaVersion"] = 1.5;
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{ \"schemaVersion\": 1, ")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{ \"schemaVersion\": 1 } { }")]
        public void DeserializeAndMigrate_MalformedJson_Throws(string json)
        {
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json));
        }

        [Fact]
        public void DeserializeAndMigrate_UnknownMember_Throws()
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u)));
            json["cheatMode"] = true;
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));

            JObject nested = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u)));
            nested["bikes"]![0]!["fuelTank"]!["octane"] = 91;
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(nested.ToString()));
        }

        [Theory]
        [InlineData("seed")]
        [InlineData("cashUsd")]
        [InlineData("bikes")]
        [InlineData("unlockedZones")]
        [InlineData("diagnosticHintsEnabled")]
        public void DeserializeAndMigrate_MissingRequiredMember_Throws(string member)
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u)));
            json.Remove(member);
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));
        }

        [Fact]
        public void DeserializeAndMigrate_SchemaConstraintViolations_Throw()
        {
            AssertRejected(j => j["bikes"]![0]!["fuelTank"]!["petcockMode"] = "Sideways");
            AssertRejected(j => j["bikes"]![0]!["fuelTank"]!["petcockMode"] = 1);
            AssertRejected(j => j["bikes"]![0]!["paintHex"] = "red");
            AssertRejected(j => j["bikes"]![0]!["patinaLevel"] = 1.5);
            AssertRejected(j => j["bikes"]![0]!["archetypeId"] = "Maxer E50");
            AssertRejected(j => j["activeZoneId"] = "WestOakland");
            AssertRejected(j => j["activeZoneId"] = "Zone_WestOakland\n");
            AssertRejected(j => j["activeBikeInstanceId"] = "bike_404");
            AssertRejected(j => j["toolRollCapacity"] = 13);
            AssertRejected(j => j["cashUsd"] = -1);
            AssertRejected(j => j["seed"] = -5);
            AssertRejected(j => j["seed"] = 4294967296L);
            AssertRejected(j => j["bikes"] = new JArray());
            AssertRejected(j => j["unlockedZones"] = new JArray());
            AssertRejected(j => ((JArray)j["bikes"]![0]!["activeFaultIds"]!).Add("FouledPlug"));
        }

        [Fact]
        public void DeserializeAndMigrate_WrongJsonTypes_AreRejectedNotCoerced()
        {
            AssertRejected(j => j["seed"] = "5");
            AssertRejected(j => j["reputation"] = 1.5);
            AssertRejected(j => j["toolRollCapacity"] = 6.0);
            AssertRejected(j => j["cashUsd"] = "12");
            AssertRejected(j => j["diagnosticHintsEnabled"] = "true");
            AssertRejected(j => j["diagnosticHintsEnabled"] = 1);
            AssertRejected(j => j["activeBikeInstanceId"] = 5);
            AssertRejected(j => j["bikes"]![0]!["nickname"] = 5);
            AssertRejected(j => j["bikes"]![0]!["odometerKm"] = "12.5");
            AssertRejected(j => j["bikes"]![0]!["fuelTank"] = new JArray());
            AssertRejected(j => j["bikes"]![0]!["installedParts"]![0]!["installedArrowTowardExhaust"] = "yes");
            AssertRejected(j => j["bikes"]![0]!["fasteners"]![0]!["currentTorqueNm"] = true);
            AssertRejected(j => ((JArray)j["bikes"]![0]!["activeFaultIds"]!).Add(7));
            AssertRejected(j => ((JArray)j["unlockedZones"]!).Add(new JObject()));
            AssertRejected(j => j["activeToolRoll"] = "SparkPlugWrench");
        }

        [Fact]
        public void DeserializeAndMigrate_EverySchemaLeafRejectsWrongJsonType()
        {
            // Schema-driven: proves the loader's type checks cover every leaf property in save_state.schema.json.
            JObject schema = TestCatalog.ReadSchema("save_state.schema.json");
            JObject bikeSchema = (JObject)schema["properties"]!["bikes"]!["items"]!;
            var targets = new List<KeyValuePair<JObject, Func<JObject, JObject>>>
            {
                new KeyValuePair<JObject, Func<JObject, JObject>>(schema, j => j),
                new KeyValuePair<JObject, Func<JObject, JObject>>(bikeSchema, j => (JObject)j["bikes"]![0]!),
                new KeyValuePair<JObject, Func<JObject, JObject>>((JObject)bikeSchema["properties"]!["fuelTank"]!, j => (JObject)j["bikes"]![0]!["fuelTank"]!),
                new KeyValuePair<JObject, Func<JObject, JObject>>((JObject)bikeSchema["properties"]!["installedParts"]!["items"]!, j => (JObject)j["bikes"]![0]!["installedParts"]![0]!),
                new KeyValuePair<JObject, Func<JObject, JObject>>((JObject)bikeSchema["properties"]!["fasteners"]!["items"]!, j => (JObject)j["bikes"]![0]!["fasteners"]![0]!),
                new KeyValuePair<JObject, Func<JObject, JObject>>((JObject)schema["properties"]!["inventory"]!["items"]!, j => (JObject)j["inventory"]![0]!),
                new KeyValuePair<JObject, Func<JObject, JObject>>((JObject)schema["properties"]!["completedGigs"]!["items"]!, j => (JObject)j["completedGigs"]![0]!),
            };

            int checkedLeaves = 0;
            foreach (KeyValuePair<JObject, Func<JObject, JObject>> target in targets)
            {
                foreach (JProperty prop in ((JObject)target.Key["properties"]!).Properties())
                {
                    string type = (string)prop.Value["type"]!;
                    JToken wrong;
                    switch (type)
                    {
                        case "integer": wrong = new JValue(1.5); break;
                        case "number": wrong = new JValue("1"); break;
                        case "string": wrong = new JValue(1); break;
                        case "boolean": wrong = new JValue("true"); break;
                        case "array": wrong = new JValue("x"); break;
                        case "object": wrong = new JArray(); break;
                        default: throw new InvalidOperationException("unhandled schema type " + type);
                    }

                    JObject json = JObject.Parse(SaveMigrator.Serialize(BuildRichState()));
                    target.Value(json)[prop.Name] = wrong;
                    Assert.True(Rejects(json), "save loader accepted wrong JSON type for '" + prop.Name + "' (schema type " + type + ")");
                    checkedLeaves++;
                }
            }
            Assert.True(checkedLeaves >= 45, "only " + checkedLeaves + " schema properties checked");
        }

        private static bool Rejects(JObject json)
        {
            try
            {
                SaveMigrator.DeserializeAndMigrate(json.ToString());
                return false;
            }
            catch (SaveMigrationException)
            {
                return true;
            }
        }

        [Fact]
        public void DeserializeAndMigrate_AcceptsIntegerLiteralsForNumberFields()
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(3u, TestCatalog.Real)));
            json["cashUsd"] = 300;
            json["bikes"]![0]!["odometerKm"] = 12;
            json["bikes"]![0]!["fuelTank"]!["fuelLiters"] = 2;
            SaveState state = SaveMigrator.DeserializeAndMigrate(json.ToString());
            Assert.Equal(300m, state.CashUsd);
            Assert.Equal(12.0, state.Bikes[0].OdometerKm);
            Assert.Equal(2f, state.Bikes[0].FuelTank.FuelLiters);
        }

        [Fact]
        public void ValidateAgainstCatalog_ReportsUnknownIds()
        {
            CatalogDatabase db = TestCatalog.Real;
            SaveState state = SaveMigrator.CreateNewDefault(5u, db);
            state.Bikes[0].InstalledParts.Add(new InstalledPartSaveState { SlotId = "SLOT_JETPACK", PartId = "jetpack_x" });
            state.ActiveToolRoll.Add("plug_b6hs");
            state.UnlockedZones.Add("Zone_Moon");

            IReadOnlyList<string> errors = SaveMigrator.ValidateAgainstCatalog(state, db);
            Assert.Contains(errors, e => e.Contains("SLOT_JETPACK"));
            Assert.Contains(errors, e => e.Contains("jetpack_x"));
            Assert.Contains(errors, e => e.Contains("plug_b6hs"));
            Assert.Contains(errors, e => e.Contains("Zone_Moon"));
        }

        private static void AssertRejected(Action<JObject> mutate)
        {
            JObject json = JObject.Parse(SaveMigrator.Serialize(SaveMigrator.CreateNewDefault(1u, TestCatalog.Real)));
            mutate(json);
            Assert.Throws<SaveMigrationException>(() => SaveMigrator.DeserializeAndMigrate(json.ToString()));
        }

        private static void AssertKeysMatchSchema(JObject actual, JObject schemaNode)
        {
            string[] expected = ((JObject)schemaNode["properties"]!).Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] required = ((JArray)schemaNode["required"]!).Select(t => (string)t!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] keys = actual.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, keys);
            Assert.Equal(required, keys);
        }

        private static SaveState BuildRichState()
        {
            SaveState state = SaveMigrator.CreateNewDefault(uint.MaxValue, TestCatalog.Real, ArchetypeIds.BramblePA50);
            state.CashUsd = 1234.56m;
            state.Reputation = 77;
            state.DiagnosticHintsEnabled = false;
            state.ActiveToolRoll.Add("SparkPlugWrench");
            state.ActiveToolRoll.Add("SparePlug");
            state.UnlockedZones.Add("Zone_OaklandHills");

            BikeSaveState bike = state.Bikes[0];
            bike.OdometerKm = 1532.25;
            bike.PatinaLevel = 0.3f;
            bike.FuelTank.PetcockMode = PetcockMode.Reserve;
            bike.FuelTank.FuelLiters = 0.4f;
            bike.InstalledParts[0].Wear = 0.42f;
            bike.InstalledParts[0].Condition = 0.58f;
            bike.Fasteners[0].IsSeized = true;
            bike.Fasteners[0].HasPenetratingOil = true;
            bike.ActiveFaultIds.Add(FaultIds.FouledPlug);

            state.Inventory.Add(new InventoryItemState { InstanceId = "inv_001", PartId = "jet_main_72", Quantity = 2, Condition = 1f, Wear = 0f });
            state.CompletedGigs.Add(new CompletedGigState
            {
                JobId = "gig_west_oakland_coffee",
                BestTimeSeconds = 121.5f,
                Completions = 3,
                LastCompletedUtc = "2026-11-25T08:30:00Z",
            });
            return state;
        }
    }
}
