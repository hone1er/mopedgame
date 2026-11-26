#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MopedSim.Core;

namespace MopedSim.Data
{
    /// <summary>
    /// Immutable, fully-mapped view of the five data/catalogs/*.json files.
    /// <see cref="Parts"/> can be passed directly to <c>new BikeSimulation(archetype, db.Parts)</c>.
    /// Catalog IDs (slot, fastener, part, zone, job) are kept verbatim from the JSON.
    /// </summary>
    public sealed class CatalogDatabase
    {
        private readonly Dictionary<string, PartDefinition> _parts;
        private readonly Dictionary<string, BikeArchetypeSpec> _archetypes;
        private readonly Dictionary<string, FaultDefinition> _faults;
        private readonly Dictionary<string, ZoneEnvironmentSpec> _zones;
        private readonly Dictionary<string, JobDefinition> _jobs;
        private readonly Dictionary<string, PartMetadata> _partMeta;
        private readonly Dictionary<string, ArchetypeMetadata> _archetypeMeta;
        private readonly Dictionary<string, FaultMetadata> _faultMeta;
        private readonly Dictionary<string, ZoneMetadata> _zoneMeta;
        private readonly Dictionary<string, JobMetadata> _jobMeta;

        private readonly List<PartDefinition> _partList;
        private readonly List<BikeArchetypeSpec> _archetypeList;
        private readonly List<FaultDefinition> _faultList;
        private readonly List<ZoneEnvironmentSpec> _zoneList;
        private readonly List<JobDefinition> _jobList;

        /// <summary>Catalog schema version shared by all five catalog files.</summary>
        public int SchemaVersion { get; }

        public IReadOnlyDictionary<string, PartDefinition> Parts
        {
            get { return _parts; }
        }

        public IReadOnlyDictionary<string, BikeArchetypeSpec> Archetypes
        {
            get { return _archetypes; }
        }

        public IReadOnlyDictionary<string, FaultDefinition> Faults
        {
            get { return _faults; }
        }

        public IReadOnlyDictionary<string, ZoneEnvironmentSpec> Zones
        {
            get { return _zones; }
        }

        public IReadOnlyDictionary<string, JobDefinition> Jobs
        {
            get { return _jobs; }
        }

        public IReadOnlyDictionary<string, PartMetadata> PartMeta
        {
            get { return _partMeta; }
        }

        public IReadOnlyDictionary<string, ArchetypeMetadata> ArchetypeMeta
        {
            get { return _archetypeMeta; }
        }

        public IReadOnlyDictionary<string, FaultMetadata> FaultMeta
        {
            get { return _faultMeta; }
        }

        public IReadOnlyDictionary<string, ZoneMetadata> ZoneMeta
        {
            get { return _zoneMeta; }
        }

        public IReadOnlyDictionary<string, JobMetadata> JobMeta
        {
            get { return _jobMeta; }
        }

        /// <summary>Parts in catalog file order (deterministic iteration for shops / UI).</summary>
        public IReadOnlyList<PartDefinition> PartList
        {
            get { return _partList; }
        }

        /// <summary>Archetypes in catalog file order.</summary>
        public IReadOnlyList<BikeArchetypeSpec> ArchetypeList
        {
            get { return _archetypeList; }
        }

        /// <summary>Faults in catalog file order.</summary>
        public IReadOnlyList<FaultDefinition> FaultList
        {
            get { return _faultList; }
        }

        /// <summary>Zones in catalog file order.</summary>
        public IReadOnlyList<ZoneEnvironmentSpec> ZoneList
        {
            get { return _zoneList; }
        }

        /// <summary>Jobs in catalog file order.</summary>
        public IReadOnlyList<JobDefinition> JobList
        {
            get { return _jobList; }
        }

        internal CatalogDatabase(
            int schemaVersion,
            List<PartDefinition> parts,
            List<PartMetadata> partMeta,
            List<BikeArchetypeSpec> archetypes,
            List<ArchetypeMetadata> archetypeMeta,
            List<FaultDefinition> faults,
            List<FaultMetadata> faultMeta,
            List<ZoneEnvironmentSpec> zones,
            List<ZoneMetadata> zoneMeta,
            List<JobDefinition> jobs,
            List<JobMetadata> jobMeta)
        {
            SchemaVersion = schemaVersion;
            _partList = parts;
            _archetypeList = archetypes;
            _faultList = faults;
            _zoneList = zones;
            _jobList = jobs;

            _parts = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Count; i++)
            {
                _parts[parts[i].Id] = parts[i];
            }
            _archetypes = new Dictionary<string, BikeArchetypeSpec>(StringComparer.Ordinal);
            for (int i = 0; i < archetypes.Count; i++)
            {
                _archetypes[archetypes[i].Id] = archetypes[i];
            }
            _faults = new Dictionary<string, FaultDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < faults.Count; i++)
            {
                _faults[faults[i].Id] = faults[i];
            }
            _zones = new Dictionary<string, ZoneEnvironmentSpec>(StringComparer.Ordinal);
            for (int i = 0; i < zones.Count; i++)
            {
                _zones[zones[i].Id] = zones[i];
            }
            _jobs = new Dictionary<string, JobDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < jobs.Count; i++)
            {
                _jobs[jobs[i].Id] = jobs[i];
            }

            _partMeta = new Dictionary<string, PartMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < partMeta.Count; i++)
            {
                _partMeta[partMeta[i].PartId] = partMeta[i];
            }
            _archetypeMeta = new Dictionary<string, ArchetypeMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < archetypeMeta.Count; i++)
            {
                _archetypeMeta[archetypeMeta[i].ArchetypeId] = archetypeMeta[i];
            }
            _faultMeta = new Dictionary<string, FaultMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < faultMeta.Count; i++)
            {
                _faultMeta[faultMeta[i].FaultId] = faultMeta[i];
            }
            _zoneMeta = new Dictionary<string, ZoneMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < zoneMeta.Count; i++)
            {
                _zoneMeta[zoneMeta[i].ZoneId] = zoneMeta[i];
            }
            _jobMeta = new Dictionary<string, JobMetadata>(StringComparer.Ordinal);
            for (int i = 0; i < jobMeta.Count; i++)
            {
                _jobMeta[jobMeta[i].JobId] = jobMeta[i];
            }
        }

        public PartDefinition GetPart(string partId)
        {
            return Require(_parts, partId, "part");
        }

        public bool TryGetPart(string partId, [NotNullWhen(true)] out PartDefinition? part)
        {
            if (partId != null && _parts.TryGetValue(partId, out var found))
            {
                part = found;
                return true;
            }
            part = null;
            return false;
        }

        /// <summary>Parts whose mapped Core <see cref="PartCategory"/> matches, in catalog order.</summary>
        public IReadOnlyList<PartDefinition> GetPartsByCategory(PartCategory category)
        {
            var result = new List<PartDefinition>();
            for (int i = 0; i < _partList.Count; i++)
            {
                if (_partList[i].Category == category)
                {
                    result.Add(_partList[i]);
                }
            }
            return result;
        }

        /// <summary>Parts whose exact parts.json category string matches (e.g. "Brake", "Sprocket"), in catalog order.</summary>
        public IReadOnlyList<PartDefinition> GetPartsByCategory(string catalogCategory)
        {
            var result = new List<PartDefinition>();
            for (int i = 0; i < _partList.Count; i++)
            {
                if (_partMeta.TryGetValue(_partList[i].Id, out var meta)
                    && string.Equals(meta.CatalogCategory, catalogCategory, StringComparison.Ordinal))
                {
                    result.Add(_partList[i]);
                }
            }
            return result;
        }

        public BikeArchetypeSpec GetArchetype(string archetypeId)
        {
            return Require(_archetypes, archetypeId, "archetype");
        }

        public FaultDefinition GetFault(string faultId)
        {
            return Require(_faults, faultId, "fault");
        }

        public ZoneEnvironmentSpec GetZone(string zoneId)
        {
            return Require(_zones, zoneId, "zone");
        }

        public JobDefinition GetJob(string jobId)
        {
            return Require(_jobs, jobId, "job");
        }

        /// <summary>Jobs that start or finish in the given zone, in catalog order.</summary>
        public IReadOnlyList<JobDefinition> GetJobsForZone(string zoneId)
        {
            var result = new List<JobDefinition>();
            for (int i = 0; i < _jobList.Count; i++)
            {
                JobDefinition job = _jobList[i];
                if (_jobMeta.TryGetValue(job.Id, out var meta))
                {
                    if (string.Equals(meta.StartZoneId, zoneId, StringComparison.Ordinal)
                        || string.Equals(meta.TargetZoneId, zoneId, StringComparison.Ordinal))
                    {
                        result.Add(job);
                    }
                }
                else if (string.Equals(job.ZoneId, zoneId, StringComparison.Ordinal))
                {
                    result.Add(job);
                }
            }
            return result;
        }

        /// <summary>Jobs of one <see cref="JobType"/> (e.g. every Bay Swarm rally), in catalog order.</summary>
        public IReadOnlyList<JobDefinition> GetJobsOfType(JobType jobType)
        {
            var result = new List<JobDefinition>();
            for (int i = 0; i < _jobList.Count; i++)
            {
                if (_jobList[i].JobType == jobType)
                {
                    result.Add(_jobList[i]);
                }
            }
            return result;
        }

        private static T Require<T>(Dictionary<string, T> map, string id, string kind)
        {
            if (id != null && map.TryGetValue(id, out var value))
            {
                return value;
            }
            throw new KeyNotFoundException("Unknown " + kind + " id '" + (id ?? "<null>") + "'.");
        }
    }

    /// <summary>
    /// Result of <see cref="CatalogLoader.Validate"/>: cross-reference and Core-contract errors.
    /// </summary>
    public sealed class CatalogValidationReport
    {
        private readonly List<string> _errors;

        public bool IsValid
        {
            get { return _errors.Count == 0; }
        }

        public IReadOnlyList<string> Errors
        {
            get { return _errors; }
        }

        internal CatalogValidationReport(List<string> errors)
        {
            _errors = errors;
        }

        public override string ToString()
        {
            return IsValid
                ? "Catalog valid."
                : "Catalog invalid (" + _errors.Count + " errors):\n  " + string.Join("\n  ", _errors);
        }
    }
}
