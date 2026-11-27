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
    /// <summary>On-disk v1 save fixtures + the forward-migration loop via <see cref="SaveMigrator.ApplyMigrationSteps"/>.</summary>
    public sealed class SaveMigrationFixtureTests
    {
        private static string FixturePath(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        }

        public static readonly TheoryData<string, uint, double, string, int> ArchetypeFixtures = new TheoryData<string, uint, double, string, int>
        {
            { ArchetypeIds.MaxerE50, 1000u, 123.5, "#2E6F9E", 17 },
            { ArchetypeIds.BramblePA50, 1001u, 223.5, "#3F7D3A", 18 },
            { ArchetypeIds.TargaA35, 1002u, 323.5, "#E0B030", 19 },
        };

        [Theory]
        [MemberData(nameof(ArchetypeFixtures))]
        public void V1Fixture_LoadsAndPreservesKeyFields(string archetypeId, uint seed, double odometer, string paint, int reputation)
        {
            SaveState state = SaveMigrator.LoadFromFile(FixturePath("save_v1_" + archetypeId + ".json"));

            Assert.Equal(SaveMigrator.CurrentSchemaVersion, state.SchemaVersion);
            Assert.Equal(seed, state.Seed);
            Assert.Equal(reputation, state.Reputation);
            BikeSaveState bike = Assert.Single(state.Bikes);
            Assert.Equal(archetypeId, bike.ArchetypeId);
            Assert.Equal(odometer, bike.OdometerKm);
            Assert.Equal(paint, bike.PaintHex);
            Assert.True(bike.PatinaLevel > 0f);
            Assert.NotEmpty(bike.InstalledParts);
            Assert.Contains(bike.InstalledParts, p => p.SlotId == "SLOT_FRAME");

            InventoryItemState item = Assert.Single(state.Inventory);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(bike.InstalledParts[0].PartId, item.PartId);
            CompletedGigState gig = Assert.Single(state.CompletedGigs);
            Assert.Equal("gig_mission_parts_rush", gig.JobId);
            Assert.Equal("2026-01-01T00:00:00Z", gig.LastCompletedUtc);

            Assert.Empty(SaveMigrator.ValidateAgainstCatalog(state, TestCatalog.Real));
        }

        [Theory]
        [MemberData(nameof(ArchetypeFixtures))]
        public void V1Fixture_ReserializesByteStableThroughMigrator(string archetypeId, uint seed, double odometer, string paint, int reputation)
        {
            Assert.True(seed >= 1000u && odometer > 0 && paint.Length == 7 && reputation > 0);
            string raw = File.ReadAllText(FixturePath("save_v1_" + archetypeId + ".json"));
            SaveState state = SaveMigrator.DeserializeAndMigrate(raw);
            Assert.Equal(raw.TrimEnd(), SaveMigrator.Serialize(state).TrimEnd());
        }

        [Fact]
        public void LegacyFixture_MissingOptionalFields_DefaultsThem()
        {
            SaveState state = SaveMigrator.LoadFromFile(FixturePath("save_v1_legacy_minimal.json"));
            BikeSaveState bike = state.Bikes[0];
            Assert.Equal(12.5, bike.OdometerKm);
            Assert.Equal(3, state.Reputation);
            Assert.Equal(0f, bike.PatinaLevel);
            Assert.Null(bike.DecalStyle);
            Assert.Empty(state.Inventory);
            Assert.Empty(bike.InstalledParts);
            Assert.Equal("#C8553D", bike.ToCustomizationState().PaintHex);
        }

        // ---- migration loop (synthetic step tables) ----

        private static Func<JObject, JObject> Tag(string name)
        {
            return doc =>
            {
                JArray trail = doc["trail"] as JArray ?? new JArray();
                trail.Add(name + ":" + (int)doc["schemaVersion"]!);
                doc["trail"] = trail;
                return doc;
            };
        }

        private static string[] Trail(JObject doc)
        {
            return doc["trail"]!.Select(t => (string)t!).ToArray();
        }

        [Fact]
        public void ApplyMigrationSteps_RunsChainInOrder_AndStampsVersionAfterEachStep()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>>
            {
                { 0, Tag("a") }, { 1, Tag("b") }, { 2, Tag("c") },
            };
            JObject result = SaveMigrator.ApplyMigrationSteps(new JObject { ["schemaVersion"] = 0 }, 0, 3, steps);

            Assert.Equal(3, (int)result["schemaVersion"]!);
            Assert.Equal(new[] { "a:0", "b:1", "c:2" }, Trail(result));
        }

        [Fact]
        public void ApplyMigrationSteps_StartsMidChain_AndNoOpWhenAlreadyAtTarget()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>> { { 1, Tag("b") }, { 2, Tag("c") } };
            JObject mid = SaveMigrator.ApplyMigrationSteps(new JObject { ["schemaVersion"] = 1 }, 1, 3, steps);
            Assert.Equal(new[] { "b:1", "c:2" }, Trail(mid));

            var same = new JObject { ["schemaVersion"] = 3 };
            Assert.Same(same, SaveMigrator.ApplyMigrationSteps(same, 3, 3, steps));
        }

        [Fact]
        public void ApplyMigrationSteps_RejectsGapInChain()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>> { { 0, Tag("a") }, { 2, Tag("c") } };
            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(
                () => SaveMigrator.ApplyMigrationSteps(new JObject { ["schemaVersion"] = 0 }, 0, 3, steps));
            Assert.Contains("No migration path from save schemaVersion 1", ex.Message);
        }

        [Fact]
        public void ApplyMigrationSteps_RejectsFutureVersion()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>>();
            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(
                () => SaveMigrator.ApplyMigrationSteps(new JObject(), 5, 2, steps));
            Assert.Contains("newer", ex.Message);
        }

        [Fact]
        public void ApplyMigrationSteps_ThrowingStep_SurfacesClearErrorWithInnerException()
        {
            var boom = new InvalidOperationException("kaboom");
            var steps = new Dictionary<int, Func<JObject, JObject>>
            {
                { 0, Tag("a") },
                { 1, _ => throw boom },
            };
            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(
                () => SaveMigrator.ApplyMigrationSteps(new JObject { ["schemaVersion"] = 0 }, 0, 2, steps));
            Assert.Contains("v1 -> v2", ex.Message);
            Assert.Contains("kaboom", ex.Message);
            Assert.Same(boom, ex.InnerException);
        }

        [Fact]
        public void ApplyMigrationSteps_NullReturningStep_IsRejected()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>> { { 0, _ => null! } };
            SaveMigrationException ex = Assert.Throws<SaveMigrationException>(
                () => SaveMigrator.ApplyMigrationSteps(new JObject(), 0, 1, steps));
            Assert.Contains("returned null", ex.Message);
        }
    }
}
