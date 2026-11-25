using System;
using System.Collections.Generic;
using System.IO;
using MopedSim.Core;
using MopedSim.Data;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Smoke checks that the .asmdef graph matches PROJECT_GUIDE.md section 4.2 and that MopedSim.Data's CatalogLoader is
    /// reachable from Unity and produces catalogs that drive a MopedSim.Core BikeSimulation.
    /// </summary>
    public sealed class AssemblyAndCatalogWiringTests
    {
        [Serializable]
        private sealed class AsmdefFile
        {
            public string name = string.Empty;
            public string[] references = new string[0];
            public string[] includePlatforms = new string[0];
            public bool noEngineReferences;
        }

        private static string RepoRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")); }
        }

        private static AsmdefFile ReadAsmdef(string relativePath)
        {
            string path = Path.Combine(RepoRoot, relativePath);
            Assert.IsTrue(File.Exists(path), "Missing asmdef: " + path);
            return JsonUtility.FromJson<AsmdefFile>(File.ReadAllText(path));
        }

        private static AsmdefFile ReadRuntimeAsmdef(string assemblyName)
        {
            return ReadAsmdef(Path.Combine("unity", "Assets", "Scripts", assemblyName, assemblyName + ".asmdef"));
        }

        private static void AssertNoReference(AsmdefFile asmdef, params string[] forbidden)
        {
            for (int i = 0; i < forbidden.Length; i++)
            {
                CollectionAssert.DoesNotContain(asmdef.references, forbidden[i], asmdef.name + " must not reference " + forbidden[i]);
            }
        }

        [Test]
        public void PureCSharpPackages_DeclareNoEngineReferences()
        {
            AsmdefFile core = ReadAsmdef(Path.Combine("src", "MopedSim.Core", "MopedSim.Core.asmdef"));
            AsmdefFile data = ReadAsmdef(Path.Combine("src", "MopedSim.Data", "MopedSim.Data.asmdef"));
            Assert.IsTrue(core.noEngineReferences);
            Assert.IsTrue(data.noEngineReferences);
            Assert.AreEqual(0, core.references.Length);
            CollectionAssert.IsSubsetOf(data.references, new[] { "MopedSim.Core", "Newtonsoft.Json" });
        }

        [Test]
        public void RuntimeAssemblies_FollowDependencyMatrix()
        {
            AsmdefFile ride = ReadRuntimeAsmdef("MopedSim.Ride");
            AssertNoReference(ride, "MopedSim.Data", "MopedSim.Garage", "MopedSim.World", "MopedSim.UI", "MopedSim.Audio", "MopedSim.App", "MopedSim.Editor");

            AsmdefFile garage = ReadRuntimeAsmdef("MopedSim.Garage");
            AssertNoReference(garage, "MopedSim.Ride", "MopedSim.World", "MopedSim.UI", "MopedSim.Audio", "MopedSim.App", "MopedSim.Editor");
            CollectionAssert.Contains(garage.references, "glTFast");

            AsmdefFile world = ReadRuntimeAsmdef("MopedSim.World");
            AssertNoReference(world, "MopedSim.Garage", "MopedSim.UI", "MopedSim.Ride", "MopedSim.Audio", "MopedSim.App", "MopedSim.Editor");

            AsmdefFile audio = ReadRuntimeAsmdef("MopedSim.Audio");
            AssertNoReference(audio, "MopedSim.Data", "MopedSim.Garage", "MopedSim.World", "MopedSim.Ride", "MopedSim.UI", "MopedSim.App", "MopedSim.Editor");

            AsmdefFile ui = ReadRuntimeAsmdef("MopedSim.UI");
            AssertNoReference(ui, "MopedSim.Ride", "MopedSim.World", "MopedSim.Garage", "MopedSim.Audio", "MopedSim.App", "MopedSim.Editor");

            AsmdefFile app = ReadRuntimeAsmdef("MopedSim.App");
            AssertNoReference(app, "MopedSim.Editor");
            CollectionAssert.IsSubsetOf(
                new[] { "MopedSim.Core", "MopedSim.Data", "MopedSim.Ride", "MopedSim.Garage", "MopedSim.World", "MopedSim.UI", "MopedSim.Audio" },
                app.references);

            string[] runtime = new[] { "MopedSim.Ride", "MopedSim.Garage", "MopedSim.World", "MopedSim.Audio", "MopedSim.UI", "MopedSim.App" };
            for (int i = 0; i < runtime.Length; i++)
            {
                AsmdefFile asm = ReadRuntimeAsmdef(runtime[i]);
                CollectionAssert.DoesNotContain(asm.includePlatforms, "Editor", runtime[i] + " must ship in player builds.");
            }

            AsmdefFile editor = ReadRuntimeAsmdef("MopedSim.Editor");
            CollectionAssert.AreEqual(new[] { "Editor" }, editor.includePlatforms);
        }

        [Test]
        public void RuntimeAssemblies_AreLoadedInTheEditorDomain()
        {
            string[] expected = new[]
            {
                "MopedSim.Core", "MopedSim.Data", "MopedSim.Ride", "MopedSim.Garage", "MopedSim.World",
                "MopedSim.UI", "MopedSim.Audio", "MopedSim.App", "MopedSim.Editor"
            };
            HashSet<string> loaded = new HashSet<string>(StringComparer.Ordinal);
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                loaded.Add(assemblies[i].GetName().Name);
            }

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsTrue(loaded.Contains(expected[i]), expected[i] + " failed to compile/load in Unity 6.");
            }
        }

        [Test]
        public void CatalogLoader_LoadsRepoCatalogs_AndDrivesCoreSimulation()
        {
            string catalogsDir = Path.Combine(RepoRoot, "data", "catalogs");
            CatalogDatabase db = CatalogLoader.LoadFromDirectory(catalogsDir);
            CatalogValidationReport report = CatalogLoader.Validate(db);
            Assert.IsTrue(report.IsValid, "Catalog validation errors:\n" + string.Join("\n", report.Errors));

            Assert.Greater(db.Parts.Count, 0);
            Assert.IsTrue(db.Archetypes.ContainsKey(ArchetypeIds.MaxerE50));
            Assert.IsTrue(db.Archetypes.ContainsKey(ArchetypeIds.BramblePA50));
            Assert.IsTrue(db.Archetypes.ContainsKey(ArchetypeIds.TargaA35));

            string[] archetypes = new[] { ArchetypeIds.MaxerE50, ArchetypeIds.BramblePA50, ArchetypeIds.TargaA35 };
            for (int a = 0; a < archetypes.Length; a++)
            {
                BikeSimulation bike = new BikeSimulation(db.GetArchetype(archetypes[a]), db.Parts);
                SeededSimRandom rng = new SeededSimRandom(7u);
                RideInputState input = new RideInputState(0.7f);
                for (int i = 0; i < 300; i++)
                {
                    bike.Step(1f / 60f, in input, rng);
                }

                Assert.IsTrue(bike.LatestTelemetry.IsRunning, archetypes[a] + " should run on catalog defaults.");
                Assert.Greater(bike.LatestTelemetry.VehicleSpeedMps, 0.5f, archetypes[a] + " should move under throttle.");
                Assert.IsTrue(bike.Assembly.IsSlotOccupied(SlotIds.Cylinder), archetypes[a] + " should mount a cylinder.");
            }
        }
    }
}
