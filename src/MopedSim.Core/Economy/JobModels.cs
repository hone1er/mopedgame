#nullable enable

using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Job archetypes authored in data/catalogs/jobs.json (<c>jobType</c>).
    /// </summary>
    public enum JobType
    {
        CourierGig = 0,
        HillClimb = 1,
        SpeedTrap = 2,
        BaySwarmRally = 3
    }

    /// <summary>
    /// Lifecycle state of the single active job tracked by <see cref="JobSessionManager"/>.
    /// </summary>
    public enum JobSessionStatus
    {
        Idle = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
        Abandoned = 4
    }

    /// <summary>
    /// Why <see cref="JobSessionManager.TryAcceptJob"/> refused a job (<see cref="None"/> when accepted).
    /// </summary>
    public enum JobRejectReason
    {
        None = 0,
        InvalidJob = 1,
        NoBike = 2,
        JobAlreadyActive = 3,
        BikeHardSeized = 4,
        InsufficientCargoSlots = 5,
        InsufficientCargoKg = 6
    }

    /// <summary>
    /// Why an active job ended in <see cref="JobSessionStatus.Failed"/> (<see cref="None"/> otherwise).
    /// </summary>
    public enum JobFailReason
    {
        None = 0,
        CargoDestroyed = 1,
        TimeExpired = 2,
        BikeHardSeized = 3
    }

    /// <summary>
    /// Outcome of a checkpoint report sent by the ride / world layer.
    /// </summary>
    public enum CheckpointReportResult
    {
        NotActive = 0,
        Reached = 1,
        JobCompleted = 2,
        WrongCheckpoint = 3,
        OutOfRange = 4
    }

    /// <summary>
    /// Result of <see cref="JobSessionManager.TryAcceptJob"/>, including the capacity comparison for UI messaging.
    /// </summary>
    public readonly struct JobAcceptResult
    {
        private readonly string? _jobId;
        private readonly string? _message;

        public readonly bool Accepted;
        public readonly JobRejectReason Reason;
        public readonly int RequiredCargoSlots;
        public readonly float RequiredCargoKg;
        public readonly CargoCapacity Capacity;

        public string JobId
        {
            get { return _jobId ?? string.Empty; }
        }

        public string Message
        {
            get { return _message ?? string.Empty; }
        }

        public JobAcceptResult(bool accepted, JobRejectReason reason, string jobId, int requiredCargoSlots, float requiredCargoKg, CargoCapacity capacity, string message)
        {
            Accepted = accepted;
            Reason = reason;
            _jobId = jobId ?? string.Empty;
            RequiredCargoSlots = requiredCargoSlots;
            RequiredCargoKg = requiredCargoKg;
            Capacity = capacity;
            _message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Immutable reward record produced when a job completes. Persist via MopedSim.Data CompletedGigState
    /// (jobId, best time, completions); cash and reputation are credited once by <see cref="JobSessionManager.TryCollectReward"/>.
    /// </summary>
    public sealed class CompletedGigRecord
    {
        public string JobId { get; }
        public JobType JobType { get; }
        public float ElapsedSeconds { get; }
        public bool OnTime { get; }
        public float CargoIntegrity { get; }

        /// <summary>Rally finishing position (1 = winner); 0 for non-rally jobs.</summary>
        public int Placement { get; }

        /// <summary>Rally pack size including the player; 0 for non-rally jobs.</summary>
        public int FieldSize { get; }

        /// <summary>True when a rally was finished without any seize.</summary>
        public bool PackSurvivalBonusEarned { get; }

        public decimal BasePayoutUsd { get; }
        public decimal BonusPayoutUsd { get; }
        public decimal SurvivalBonusUsd { get; }
        public decimal TotalPayoutUsd { get; }
        public int ReputationEarned { get; }

        public CompletedGigRecord(
            string jobId,
            JobType jobType,
            float elapsedSeconds,
            bool onTime,
            float cargoIntegrity,
            int placement,
            int fieldSize,
            bool packSurvivalBonusEarned,
            decimal basePayoutUsd,
            decimal bonusPayoutUsd,
            decimal survivalBonusUsd,
            int reputationEarned)
        {
            JobId = jobId ?? string.Empty;
            JobType = jobType;
            ElapsedSeconds = elapsedSeconds;
            OnTime = onTime;
            CargoIntegrity = cargoIntegrity;
            Placement = placement;
            FieldSize = fieldSize;
            PackSurvivalBonusEarned = packSurvivalBonusEarned;
            BasePayoutUsd = basePayoutUsd;
            BonusPayoutUsd = bonusPayoutUsd;
            SurvivalBonusUsd = survivalBonusUsd;
            TotalPayoutUsd = basePayoutUsd + bonusPayoutUsd + survivalBonusUsd;
            ReputationEarned = reputationEarned;
        }
    }

    /// <summary>
    /// Read-only, allocation-free view of the active job for HUD / delivery beacon rendering.
    /// </summary>
    public readonly struct JobSessionSnapshot
    {
        private readonly string? _jobId;
        private readonly string? _nextCheckpointId;

        public readonly JobSessionStatus Status;
        public readonly JobType JobType;
        public readonly JobFailReason FailReason;
        public readonly float ElapsedSeconds;
        public readonly float TimeLimitSeconds;
        public readonly float RemainingSeconds;
        public readonly int CheckpointsReached;
        public readonly int CheckpointCount;
        public readonly float CargoIntegrity;
        public readonly float RouteProgressMeters;
        public readonly float RouteLengthMeters;
        public readonly int RallyPlacement;
        public readonly int RallyFieldSize;
        public readonly bool HasSeizedDuringJob;
        public readonly bool RewardCollected;

        public string JobId
        {
            get { return _jobId ?? string.Empty; }
        }

        /// <summary>Id of the next checkpoint to reach; empty when none remain or no job is active.</summary>
        public string NextCheckpointId
        {
            get { return _nextCheckpointId ?? string.Empty; }
        }

        public JobSessionSnapshot(
            JobSessionStatus status,
            string jobId,
            JobType jobType,
            JobFailReason failReason,
            float elapsedSeconds,
            float timeLimitSeconds,
            int checkpointsReached,
            int checkpointCount,
            string nextCheckpointId,
            float cargoIntegrity,
            float routeProgressMeters,
            float routeLengthMeters,
            int rallyPlacement,
            int rallyFieldSize,
            bool hasSeizedDuringJob,
            bool rewardCollected)
        {
            Status = status;
            _jobId = jobId ?? string.Empty;
            JobType = jobType;
            FailReason = failReason;
            ElapsedSeconds = elapsedSeconds;
            TimeLimitSeconds = timeLimitSeconds;
            RemainingSeconds = timeLimitSeconds > 0f ? Math.Max(0f, timeLimitSeconds - elapsedSeconds) : 0f;
            CheckpointsReached = checkpointsReached;
            CheckpointCount = checkpointCount;
            _nextCheckpointId = nextCheckpointId ?? string.Empty;
            CargoIntegrity = cargoIntegrity;
            RouteProgressMeters = routeProgressMeters;
            RouteLengthMeters = routeLengthMeters;
            RallyPlacement = rallyPlacement;
            RallyFieldSize = rallyFieldSize;
            HasSeizedDuringJob = hasSeizedDuringJob;
            RewardCollected = rewardCollected;
        }
    }

    /// <summary>
    /// Tunable job economy rules (injectable; defaults follow PROJECT_GUIDE.md §2.5 progression loops).
    /// </summary>
    public sealed class JobEconomyConfig
    {
        /// <summary>Jobs still in progress after TimeLimit × this multiplier fail (late deliveries before that pay base only).</summary>
        public float OvertimeFailMultiplier { get; set; } = 1.5f;

        /// <summary>Cargo integrity lost per unit of reported pothole severity (0..1).</summary>
        public float PotholeIntegrityLoss { get; set; } = 0.12f;

        /// <summary>Cargo integrity lost on a crash / dropped bike.</summary>
        public float CrashIntegrityLoss { get; set; } = 0.45f;

        /// <summary>Continuous integrity loss per second per (surface roughness × m/s) while carrying cargo.</summary>
        public float RoughRoadIntegrityLossRate { get; set; } = 0.0006f;

        /// <summary>Cargo jobs fail once integrity drops to or below this value.</summary>
        public float CargoDestroyedThreshold { get; set; } = 0.05f;

        /// <summary>Fraction of the job's reputation reward granted for late or seized completions.</summary>
        public float ReducedReputationFraction { get; set; } = 0.5f;

        /// <summary>Rally survival bonus as a fraction of the job's base payout.</summary>
        public float RallySurvivalBonusFraction { get; set; } = 0.25f;

        /// <summary>Extra distance margin (m) allowed around a checkpoint radius for position-based reporting.</summary>
        public float CheckpointRadiusSlackMeters { get; set; } = 0f;
    }

    /// <summary>
    /// Stable, process-independent string hashing (FNV-1a 32-bit) for deterministic seeding. Never use string.GetHashCode for seeds.
    /// </summary>
    public static class StableHash
    {
        public static uint Fnv1a(string? value)
        {
            uint hash = 2166136261u;
            if (value == null)
            {
                return hash;
            }
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash = unchecked(hash * 16777619u);
            }
            return hash;
        }

        public static uint Combine(uint seed, uint value)
        {
            unchecked
            {
                uint h = seed ^ (value + 0x9E3779B9u + (seed << 6) + (seed >> 2));
                return h;
            }
        }
    }
}
