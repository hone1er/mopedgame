using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace MopedSim.Data.Tests
{
    /// <summary>
    /// Locates the real repository catalogs/schemas and offers JSON mutation helpers for error-path tests.
    /// </summary>
    internal static class TestCatalog
    {
        private static readonly Lazy<string> RepoRootLazy = new Lazy<string>(FindRepoRoot);
        private static readonly Lazy<CatalogDatabase> RealDbLazy = new Lazy<CatalogDatabase>(() => CatalogLoader.LoadFromDirectory(CatalogsDir));

        public static string RepoRoot
        {
            get { return RepoRootLazy.Value; }
        }

        public static string CatalogsDir
        {
            get { return Path.Combine(RepoRoot, "data", "catalogs"); }
        }

        public static string SchemasDir
        {
            get { return Path.Combine(RepoRoot, "data", "schemas"); }
        }

        /// <summary>Shared, read-only database of the real catalogs. Do not mutate.</summary>
        public static CatalogDatabase Real
        {
            get { return RealDbLazy.Value; }
        }

        public static JObject ReadCatalog(string fileName)
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(CatalogsDir, fileName)));
        }

        public static JObject ReadSchema(string fileName)
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(SchemasDir, fileName)));
        }

        public static string RawCatalog(string fileName)
        {
            return File.ReadAllText(Path.Combine(CatalogsDir, fileName));
        }

        /// <summary>
        /// Loads the real catalogs with one file replaced by <paramref name="replacementJson"/>.
        /// </summary>
        public static CatalogDatabase LoadWithReplacement(string fileName, string replacementJson)
        {
            return CatalogLoader.LoadFromJson(
                fileName == CatalogLoader.PartsFileName ? replacementJson : RawCatalog(CatalogLoader.PartsFileName),
                fileName == CatalogLoader.ArchetypesFileName ? replacementJson : RawCatalog(CatalogLoader.ArchetypesFileName),
                fileName == CatalogLoader.FaultsFileName ? replacementJson : RawCatalog(CatalogLoader.FaultsFileName),
                fileName == CatalogLoader.ZonesFileName ? replacementJson : RawCatalog(CatalogLoader.ZonesFileName),
                fileName == CatalogLoader.JobsFileName ? replacementJson : RawCatalog(CatalogLoader.JobsFileName));
        }

        /// <summary>
        /// Loads the real catalogs after applying <paramref name="mutate"/> to the parsed JSON of one file.
        /// </summary>
        public static CatalogDatabase LoadWithMutation(string fileName, Action<JObject> mutate)
        {
            JObject root = ReadCatalog(fileName);
            mutate(root);
            return LoadWithReplacement(fileName, root.ToString());
        }

        /// <summary>
        /// Loads the real catalogs after mutating several files (each file may appear once).
        /// </summary>
        public static CatalogDatabase LoadWithMutations(params (string FileName, Action<JObject> Mutate)[] mutations)
        {
            var raw = new System.Collections.Generic.Dictionary<string, string>();
            foreach (string name in new[]
                     {
                         CatalogLoader.PartsFileName, CatalogLoader.ArchetypesFileName, CatalogLoader.FaultsFileName,
                         CatalogLoader.ZonesFileName, CatalogLoader.JobsFileName,
                     })
            {
                raw[name] = RawCatalog(name);
            }
            foreach ((string fileName, Action<JObject> mutate) in mutations)
            {
                JObject root = JObject.Parse(raw[fileName]);
                mutate(root);
                raw[fileName] = root.ToString();
            }
            return CatalogLoader.LoadFromJson(
                raw[CatalogLoader.PartsFileName],
                raw[CatalogLoader.ArchetypesFileName],
                raw[CatalogLoader.FaultsFileName],
                raw[CatalogLoader.ZonesFileName],
                raw[CatalogLoader.JobsFileName]);
        }

        public static JObject FindById(JObject root, string arrayKey, string id)
        {
            foreach (JToken item in (JArray)root[arrayKey]!)
            {
                if ((string?)item["id"] == id)
                {
                    return (JObject)item;
                }
            }
            throw new InvalidOperationException(arrayKey + " has no entry '" + id + "'.");
        }

        public static JObject FindSlot(JObject archetype, string slotId)
        {
            foreach (JToken slot in (JArray)archetype["mountSlots"]!)
            {
                if ((string?)slot["slotId"] == slotId)
                {
                    return (JObject)slot;
                }
            }
            throw new InvalidOperationException("archetype has no slot '" + slotId + "'.");
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "data", "catalogs", "parts.json"))
                    && Directory.Exists(Path.Combine(dir, "src", "MopedSim.Data")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate repository root (data/catalogs/parts.json) from " + AppContext.BaseDirectory);
        }
    }
}
