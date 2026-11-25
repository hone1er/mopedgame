#nullable enable

using System;

namespace MopedSim.Data
{
    /// <summary>
    /// Thrown when a catalog file is missing, is malformed JSON, or violates the structural contract of data/schemas/*.json.
    /// Cross-reference problems are reported by <see cref="CatalogLoader.Validate"/> instead.
    /// </summary>
    public sealed class CatalogLoadException : Exception
    {
        public string CatalogSource { get; }

        public CatalogLoadException(string source, string message)
            : base(source + ": " + message)
        {
            CatalogSource = source ?? string.Empty;
        }

        public CatalogLoadException(string source, string message, Exception innerException)
            : base(source + ": " + message, innerException)
        {
            CatalogSource = source ?? string.Empty;
        }
    }

    /// <summary>
    /// Thrown when a save file cannot be parsed, has an unsupported schemaVersion, or violates data/schemas/save_state.schema.json.
    /// </summary>
    public sealed class SaveMigrationException : Exception
    {
        public SaveMigrationException(string message)
            : base(message)
        {
        }

        public SaveMigrationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
