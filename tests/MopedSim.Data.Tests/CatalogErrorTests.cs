using System;
using System.IO;
using System.Linq;
using MopedSim.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MopedSim.Data.Tests
{
    public sealed class CatalogErrorTests
    {
        // ---------------------------------------------------------------- load-time (structural) failures

        [Fact]
        public void LoadFromDirectory_MissingDirectory_Throws()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_missing_" + Guid.NewGuid().ToString("N"));
            Assert.Throws<CatalogLoadException>(() => CatalogLoader.LoadFromDirectory(dir));
        }

        [Fact]
        public void LoadFromDirectory_MissingFile_ThrowsNamingFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_catalogs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string name in new[] { "parts.json", "archetypes.json", "faults.json", "zones.json" })
                {
                    File.Copy(Path.Combine(TestCatalog.CatalogsDir, name), Path.Combine(dir, name));
                }
                CatalogLoadException ex = Assert.Throws<CatalogLoadException>(() => CatalogLoader.LoadFromDirectory(dir));
                Assert.Contains("jobs.json", ex.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Theory]
        [InlineData("parts.json")]
        [InlineData("archetypes.json")]
        [InlineData("faults.json")]
        [InlineData("zones.json")]
        [InlineData("jobs.json")]
        public void LoadFromJson_MalformedJson_ThrowsNamingFile(string fileName)
        {
            CatalogLoadException ex = Assert.Throws<CatalogLoadException>(
                () => TestCatalog.LoadWithReplacement(fileName, "{ \"schemaVersion\": 1, \"items\": [ "));
            Assert.Contains(fileName, ex.Message);
        }

        [Fact]
        public void LoadFromJson_RootArrayInsteadOfObject_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithReplacement("zones.json", "[]"));
        }

        [Fact]
        public void LoadFromJson_TrailingContent_Throws()
        {
            string json = TestCatalog.RawCatalog("zones.json") + " {}";
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithReplacement("zones.json", json));
        }

        [Fact]
        public void LoadFromJson_MissingRootArray_Throws()
        {
            CatalogLoadException ex = Assert.Throws<CatalogLoadException>(
                () => TestCatalog.LoadWithReplacement("parts.json", "{ \"schemaVersion\": 1 }"));
            Assert.Contains("parts", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnsupportedSchemaVersion_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("faults.json", root => root["schemaVersion"] = 2));
        }

        [Fact]
        public void LoadFromJson_DuplicatePartId_Throws()
        {
            CatalogLoadException ex = Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("parts.json", root =>
            {
                JArray parts = (JArray)root["parts"]!;
                parts.Add(parts[0].DeepClone());
            }));
            Assert.Contains("duplicate id 'frame_maxer_e50'", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownPartCategory_Throws()
        {
            CatalogLoadException ex = Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("parts.json",
                root => TestCatalog.FindById(root, "parts", "plug_b6hs")["category"] = "Turbocharger"));
            Assert.Contains("Turbocharger", ex.Message);
        }

        [Fact]
        public void LoadFromJson_WrongJsonType_ThrowsWithPath()
        {
            CatalogLoadException ex = Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("parts.json",
                root => TestCatalog.FindById(root, "parts", "jet_main_68")["massKg"] = "heavy"));
            Assert.Contains("jet_main_68", ex.Message);
            Assert.Contains("massKg", ex.Message);
        }

        [Fact]
        public void LoadFromJson_UnknownPartDrivetrainType_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("parts.json",
                root => TestCatalog.FindById(root, "parts", "clutch_e50_stock")["stats"]!["drivetrainType"] = "Hydrostatic"));
        }

        [Fact]
        public void LoadFromJson_SprocketWithoutPosition_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("parts.json",
                root => ((JObject)TestCatalog.FindById(root, "parts", "sprocket_rear_45t")["stats"]!).Remove("position")));
        }

        [Fact]
        public void LoadFromJson_UnknownArchetypeDrivetrain_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("archetypes.json",
                root => TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50)["drivetrainArchitecture"] = "1"));
        }

        [Fact]
        public void LoadFromJson_UnknownPetcockMode_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("archetypes.json",
                root => TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50)["defaultFuelMix"]!["petcockMode"] = "Sideways"));
        }

        [Fact]
        public void LoadFromJson_FaultIdWithoutCoreFaultType_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("faults.json",
                root => TestCatalog.FindById(root, "faults", FaultIds.AirLeak)["id"] = "fault_gremlins"));
        }

        [Fact]
        public void LoadFromJson_DuplicateSlotId_Throws()
        {
            Assert.Throws<CatalogLoadException>(() => TestCatalog.LoadWithMutation("archetypes.json", root =>
            {
                JArray slots = (JArray)TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50)["mountSlots"]!;
                slots.Add(slots[1].DeepClone());
            }));
        }

        // ---------------------------------------------------------------- Validate (cross-reference) failures

        [Fact]
        public void Validate_UnknownDefaultPart_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50), SlotIds.Exhaust)["defaultPartId"] = "pipe_unobtainium");
            AssertInvalid(db, "pipe_unobtainium");
        }

        [Fact]
        public void Validate_UnknownParentSlot_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.TargaA35), SlotIds.Seat)["parentSlotId"] = "SLOT_SIDECAR");
            AssertInvalid(db, "SLOT_SIDECAR");
        }

        [Fact]
        public void Validate_ChildDeclaredBeforeParent_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
            {
                JArray slots = (JArray)TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50)["mountSlots"]!;
                JToken fork = slots[1];
                fork.Remove();
                slots.Add(fork);
            });
            AssertInvalid(db, "declared before its child");
        }

        [Fact]
        public void Validate_UnknownBlockedBySlot_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                ((JArray)TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.BramblePA50), SlotIds.Cylinder)["blockedBy"]!).Add("SLOT_GHOST"));
            AssertInvalid(db, "SLOT_GHOST");
        }

        [Fact]
        public void Validate_UnownedFastener_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                ((JArray)TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50), SlotIds.Exhaust)["fastenerIds"]!)
                    .Where(t => (string?)t == FastenerIds.ExhaustFrameBracket).ToList().ForEach(t => t.Remove()));
            AssertInvalid(db, FastenerIds.ExhaustFrameBracket);
        }

        [Fact]
        public void Validate_SlotReferencesUndeclaredFastener_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                ((JArray)TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50), SlotIds.Seat)["fastenerIds"]!).Add("BOLT_PHANTOM"));
            AssertInvalid(db, "BOLT_PHANTOM");
        }

        [Fact]
        public void Validate_BadTorqueWindow_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
            {
                JArray fasteners = (JArray)TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50)["fasteners"]!;
                JObject stem = (JObject)fasteners.First(f => (string?)f["fastenerId"] == FastenerIds.StemNut);
                stem["stripThresholdNm"] = 10.0;
            });
            AssertInvalid(db, FastenerIds.StemNut);
        }

        [Fact]
        public void Validate_MissingCoreRequiredSlot_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
            {
                JObject maxer = TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50);
                JArray slots = (JArray)maxer["mountSlots"]!;
                slots.Where(s => (string?)s["slotId"] == SlotIds.MainJet).ToList().ForEach(s => s.Remove());
            });
            AssertInvalid(db, "missing slot '" + SlotIds.MainJet + "' required by MopedSim.Core");
        }

        [Fact]
        public void Validate_MissingCoreRequiredFastener_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
            {
                JObject targa = TestCatalog.FindById(root, "archetypes", ArchetypeIds.TargaA35);
                ((JArray)targa["fasteners"]!).Where(f => (string?)f["fastenerId"] == FastenerIds.CarbClamp).ToList().ForEach(f => f.Remove());
                ((JArray)TestCatalog.FindSlot(targa, SlotIds.Carburetor)["fastenerIds"]!).Where(t => (string?)t == FastenerIds.CarbClamp).ToList().ForEach(t => t.Remove());
            });
            AssertInvalid(db, "missing fastener '" + FastenerIds.CarbClamp + "' required by MopedSim.Core");
        }

        [Fact]
        public void Validate_DefaultPartInWrongCategorySlot_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                TestCatalog.FindSlot(TestCatalog.FindById(root, "archetypes", ArchetypeIds.MaxerE50), "SLOT_REAR_BRAKE")["defaultPartId"] = "fender_front_steel");
            AssertInvalid(db, "fender_front_steel");
        }

        [Fact]
        public void Validate_CategoryAliasing_ConsumableCollision_ReportsError()
        {
            // A cable that advertised the wheel-brake interface would pass Core's category+interface check in a brake slot.
            CatalogDatabase db = TestCatalog.LoadWithMutation("parts.json", root =>
                ((JArray)TestCatalog.FindById(root, "parts", "cable_throttle")["compatibleInterfaces"]!).Add("Universal_WheelBrake"));
            AssertInvalid(db, "cable_throttle");
        }

        [Fact]
        public void Validate_DrivetrainMismatch_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("archetypes.json", root =>
                TestCatalog.FindById(root, "archetypes", ArchetypeIds.TargaA35)["drivetrainArchitecture"] = "SingleSpeedCentrifugal");
            CatalogValidationReport report = CatalogLoader.Validate(db);
            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Contains("gearbox_a35_2spd") && e.Contains("drivetrainArchitecture"));
        }

        [Fact]
        public void Validate_FaultUnknownTool_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("faults.json", root =>
                ((JArray)TestCatalog.FindById(root, "faults", FaultIds.FouledPlug)["repairOptions"]![0]!["requiredTools"]!).Add("SonicScrewdriver"));
            AssertInvalid(db, "SonicScrewdriver");
        }

        [Fact]
        public void Validate_FaultToolThatIsNotToolRollItem_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("faults.json", root =>
                ((JArray)TestCatalog.FindById(root, "faults", FaultIds.FouledPlug)["repairOptions"]![0]!["requiredTools"]!).Add("plug_b6hs"));
            AssertInvalid(db, "plug_b6hs");
        }

        [Fact]
        public void Validate_JobUnknownZone_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("jobs.json", root =>
                TestCatalog.FindById(root, "jobs", "gig_west_oakland_coffee")["targetZoneId"] = "Zone_Atlantis");
            AssertInvalid(db, "Zone_Atlantis");
        }

        [Fact]
        public void Validate_JobUnknownRecommendedPart_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("jobs.json", root =>
                ((JArray)TestCatalog.FindById(root, "jobs", "gig_west_oakland_coffee")["recommendedPartIds"]!).Add("rack_hover"));
            AssertInvalid(db, "rack_hover");
        }

        [Fact]
        public void Validate_ZoneUnknownConnection_ReportsError()
        {
            CatalogDatabase db = TestCatalog.LoadWithMutation("zones.json", root =>
                ((JArray)TestCatalog.FindById(root, "zones", "Zone_WestOakland")["connectedZones"]!).Add("Zone_Moon"));
            AssertInvalid(db, "Zone_Moon");
        }

        [Fact]
        public void LoadAndValidateFromDirectory_InvalidCatalog_Throws()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mopedsim_catalogs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string name in new[] { "parts.json", "archetypes.json", "faults.json", "zones.json" })
                {
                    File.Copy(Path.Combine(TestCatalog.CatalogsDir, name), Path.Combine(dir, name));
                }
                JObject jobs = TestCatalog.ReadCatalog("jobs.json");
                TestCatalog.FindById(jobs, "jobs", "gig_west_oakland_coffee")["startZoneId"] = "Zone_Atlantis";
                File.WriteAllText(Path.Combine(dir, "jobs.json"), jobs.ToString());

                CatalogLoadException ex = Assert.Throws<CatalogLoadException>(() => CatalogLoader.LoadAndValidateFromDirectory(dir));
                Assert.Contains("Zone_Atlantis", ex.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void AssertInvalid(CatalogDatabase db, string expectedFragment)
        {
            CatalogValidationReport report = CatalogLoader.Validate(db);
            Assert.False(report.IsValid, "expected validation errors mentioning '" + expectedFragment + "'");
            Assert.True(report.Errors.Any(e => e.Contains(expectedFragment)), "no error mentions '" + expectedFragment + "':\n" + report);
        }
    }
}
