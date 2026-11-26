#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Runs one courier gig, hill climb, speed trap, or Bay Swarm rally at a time: acceptance gating (cargo capacity,
    /// hard seize), elapsed time, ordered checkpoint progression, cargo integrity, rally pack placement, and payout.
    /// The job clock, overtime check, cargo wear, and rally pack start when the first checkpoint (start line / pickup) is
    /// reached, so the player may accept a job anywhere and ride to the start.
    /// Deterministic: advanced only by explicit dt; rally randomness comes from an injected <see cref="ISimRandom"/>.
    /// </summary>
    public sealed class JobSessionManager
    {
        private readonly List<CompletedGigRecord> _completed = new List<CompletedGigRecord>();
        private readonly List<CompletedGigRecord> _uncollected = new List<CompletedGigRecord>();
        private float[] _cumulative = Array.Empty<float>();

        public JobEconomyConfig Config { get; }
        public RallyPackConfig RallyConfig { get; }

        public JobDefinition? ActiveJob { get; private set; }
        public JobSessionStatus Status { get; private set; } = JobSessionStatus.Idle;
        public JobFailReason FailReason { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public int CheckpointsReached { get; private set; }
        public float CargoIntegrity { get; private set; } = 1f;
        public float RouteProgressMeters { get; private set; }
        public bool HasSeizedDuringJob { get; private set; }

        /// <summary>AI pack of the active (or last) Bay Swarm rally; null for other job types.</summary>
        public RallyPackSimulator? Rally { get; private set; }

        /// <summary>Record of the most recently completed job (null until one completes).</summary>
        public CompletedGigRecord? LastCompletedRecord { get; private set; }

        /// <summary>All jobs completed through this manager, in completion order.</summary>
        public IReadOnlyList<CompletedGigRecord> CompletedRecords
        {
            get { return _completed; }
        }

        /// <summary>True while a completed job's cash / reputation has not been credited yet.</summary>
        public bool HasUncollectedReward
        {
            get { return _uncollected.Count > 0; }
        }

        public float RouteLengthMeters
        {
            get { return _cumulative.Length > 0 ? _cumulative[_cumulative.Length - 1] : 0f; }
        }

        /// <summary>True once the first checkpoint has been reached and the job clock is running.</summary>
        public bool IsRouteStarted
        {
            get { return Status == JobSessionStatus.Active && CheckpointsReached > 0; }
        }

        public bool IsCargoJob
        {
            get { return ActiveJob != null && IsCargoJobDefinition(ActiveJob); }
        }

        public JobSessionManager(JobEconomyConfig? config = null, RallyPackConfig? rallyConfig = null)
        {
            Config = config ?? new JobEconomyConfig();
            RallyConfig = rallyConfig ?? new RallyPackConfig();
        }

        /// <summary>
        /// Starts <paramref name="job"/> on <paramref name="bike"/>. Rejects when another job is active, the bike is hard-seized,
        /// or the mounted SLOT_CARGO_RACK (<see cref="CargoCapacityCalculator"/>) cannot hold RequiredCargoSlots / RequiredCargoKg.
        /// <paramref name="rallyRandom"/> seeds the AI pack for rallies; when null a generator seeded from a stable hash of the job id is used.
        /// </summary>
        public JobAcceptResult TryAcceptJob(JobDefinition job, BikeSimulation bike, ISimRandom? rallyRandom = null)
        {
            if (job == null || job.Id.Length == 0 || job.Checkpoints == null || job.Checkpoints.Count == 0)
            {
                return new JobAcceptResult(false, JobRejectReason.InvalidJob, job != null ? job.Id : string.Empty, 0, 0f, CargoCapacity.None,
                    "Job is missing an id or route checkpoints.");
            }
            if (bike == null)
            {
                return new JobAcceptResult(false, JobRejectReason.NoBike, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, CargoCapacity.None,
                    "No bike selected for " + job.DisplayName + ".");
            }

            CargoCapacity capacity = CargoCapacityCalculator.Compute(bike);
            if (Status == JobSessionStatus.Active)
            {
                return new JobAcceptResult(false, JobRejectReason.JobAlreadyActive, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, capacity,
                    "Finish or abandon " + (ActiveJob != null ? ActiveJob.DisplayName : "the current job") + " first.");
            }
            if (bike.Thermal.IsHardSeized)
            {
                return new JobAcceptResult(false, JobRejectReason.BikeHardSeized, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, capacity,
                    "Engine is hard-seized: rebuild the top end before taking " + job.DisplayName + ".");
            }

            JobRejectReason cargo = CargoCapacityCalculator.CheckRequirement(capacity, job.RequiredCargoSlots, job.RequiredCargoKg);
            if (cargo == JobRejectReason.InsufficientCargoSlots)
            {
                return new JobAcceptResult(false, cargo, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, capacity,
                    job.DisplayName + " needs " + job.RequiredCargoSlots + " cargo slot(s); mounted rack has " + capacity.Slots + ".");
            }
            if (cargo == JobRejectReason.InsufficientCargoKg)
            {
                return new JobAcceptResult(false, cargo, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, capacity,
                    job.DisplayName + " needs " + job.RequiredCargoKg.ToString("0.0") + " kg of cargo capacity; mounted rack holds "
                    + capacity.MaxKg.ToString("0.0") + " kg.");
            }

            ActiveJob = job;
            Status = JobSessionStatus.Active;
            FailReason = JobFailReason.None;
            ElapsedSeconds = 0f;
            CheckpointsReached = 0;
            CargoIntegrity = 1f;
            RouteProgressMeters = 0f;
            HasSeizedDuringJob = false;
            _cumulative = RallyPackSimulator.ComputeCumulativeDistances(job.Checkpoints);
            Rally = job.JobType == JobType.BaySwarmRally
                ? new RallyPackSimulator(job.Checkpoints, rallyRandom ?? new SeededSimRandom(StableHash.Fnv1a(job.Id)), RallyConfig)
                : null;

            return new JobAcceptResult(true, JobRejectReason.None, job.Id, job.RequiredCargoSlots, job.RequiredCargoKg, capacity,
                "Accepted " + job.DisplayName + ".");
        }

        /// <summary>
        /// Advances the job clock by dt without ride telemetry (no route progress, cargo wear, or seize tracking).
        /// </summary>
        public void Step(float dt)
        {
            StepInternal(dt, 0f, 0f, false, false);
        }

        /// <summary>
        /// Advances the active job by dt using the latest ride telemetry: seize tracking (a hard seize fails the job) and, once the
        /// first checkpoint is reached, the clock, route progress from vehicle speed (capped at the next unreached checkpoint),
        /// rough-road cargo wear, rally pack, and overtime failure.
        /// </summary>
        public void Step(float dt, in EngineTelemetrySnapshot telemetry, float surfaceRoughness = 0f)
        {
            StepInternal(dt, telemetry.VehicleSpeedMps, surfaceRoughness, telemetry.IsSeized, telemetry.IsHardSeized);
        }

        private void StepInternal(float dt, float speedMps, float surfaceRoughness, bool isSeized, bool isHardSeized)
        {
            if (Status != JobSessionStatus.Active || ActiveJob == null)
            {
                return;
            }
            if (!(dt > 0f))
            {
                dt = 0f;
            }

            if (isSeized)
            {
                HasSeizedDuringJob = true;
            }
            if (isHardSeized)
            {
                Fail(JobFailReason.BikeHardSeized);
                return;
            }
            if (CheckpointsReached == 0)
            {
                // Riding to the start line / pickup: the clock, cargo, and rally pack wait.
                return;
            }

            ElapsedSeconds += dt;
            float speed = speedMps > 0f ? speedMps : 0f;

            if (CheckpointsReached < _cumulative.Length)
            {
                float cap = _cumulative[CheckpointsReached];
                RouteProgressMeters = SimMath.Min(cap, SimMath.Max(RouteProgressMeters, RouteProgressMeters + speed * dt));
            }

            if (Rally != null)
            {
                // The player finishes only via the final checkpoint report, never by speed integration alone.
                Rally.Step(dt, SimMath.Min(RouteProgressMeters, Rally.RouteLengthMeters - 0.01f), isSeized);
            }

            if (IsCargoJobDefinition(ActiveJob) && surfaceRoughness > 0f && speed > 0f)
            {
                DegradeCargo(SimMath.Max(0f, Config.RoughRoadIntegrityLossRate) * SimMath.Clamp01(surfaceRoughness) * speed * dt);
                if (Status != JobSessionStatus.Active)
                {
                    return;
                }
            }

            float limit = ActiveJob.TimeLimitSeconds;
            if (limit > 0f && ElapsedSeconds > limit * SimMath.Max(1f, Config.OvertimeFailMultiplier))
            {
                Fail(JobFailReason.TimeExpired);
            }
        }

        /// <summary>
        /// Trigger-volume report: <paramref name="checkpointId"/> must be the next unreached checkpoint.
        /// </summary>
        public CheckpointReportResult ReportCheckpointReached(string checkpointId)
        {
            if (Status != JobSessionStatus.Active || ActiveJob == null)
            {
                return CheckpointReportResult.NotActive;
            }
            JobCheckpointSpec? next = NextCheckpoint;
            if (next == null || !string.Equals(next.Id, checkpointId, StringComparison.Ordinal))
            {
                return CheckpointReportResult.WrongCheckpoint;
            }
            return AdvanceCheckpoint();
        }

        /// <summary>
        /// Position report (metric world coordinates): reaches the next checkpoint when within its radius
        /// (+ <see cref="JobEconomyConfig.CheckpointRadiusSlackMeters"/>).
        /// </summary>
        public CheckpointReportResult ReportPosition(float x, float y, float z)
        {
            if (Status != JobSessionStatus.Active || ActiveJob == null)
            {
                return CheckpointReportResult.NotActive;
            }
            JobCheckpointSpec? next = NextCheckpoint;
            if (next == null)
            {
                return CheckpointReportResult.WrongCheckpoint;
            }
            double dx = x - next.X;
            double dy = y - next.Y;
            double dz = z - next.Z;
            double radius = SimMath.Max(0f, next.RadiusMeters) + SimMath.Max(0f, Config.CheckpointRadiusSlackMeters);
            if (dx * dx + dy * dy + dz * dz > radius * radius)
            {
                return CheckpointReportResult.OutOfRange;
            }
            return AdvanceCheckpoint();
        }

        /// <summary>
        /// Pothole hit with severity 0..1 (cargo jobs only, after the pickup checkpoint).
        /// </summary>
        public void ReportPothole(float severity)
        {
            if (IsRouteStarted && ActiveJob != null && IsCargoJobDefinition(ActiveJob))
            {
                DegradeCargo(SimMath.Clamp01(severity) * SimMath.Max(0f, Config.PotholeIntegrityLoss));
            }
        }

        /// <summary>
        /// Crash / dropped bike (cargo jobs only, after the pickup checkpoint).
        /// </summary>
        public void ReportCrash()
        {
            if (IsRouteStarted && ActiveJob != null && IsCargoJobDefinition(ActiveJob))
            {
                DegradeCargo(SimMath.Max(0f, Config.CrashIntegrityLoss));
            }
        }

        /// <summary>
        /// Abandons the active job (no payout). Returns false when no job is active.
        /// </summary>
        public bool Abandon()
        {
            if (Status != JobSessionStatus.Active)
            {
                return false;
            }
            Status = JobSessionStatus.Abandoned;
            return true;
        }

        /// <summary>
        /// Credits every completed-but-uncollected payout and reputation to <paramref name="world"/> exactly once.
        /// Returns false when nothing was pending.
        /// </summary>
        public bool TryCollectReward(SimulationWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (_uncollected.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < _uncollected.Count; i++)
            {
                world.ApplyJobReward(_uncollected[i]);
            }
            _uncollected.Clear();
            return true;
        }

        public JobCheckpointSpec? NextCheckpoint
        {
            get
            {
                if (ActiveJob == null || CheckpointsReached >= ActiveJob.Checkpoints.Count)
                {
                    return null;
                }
                return ActiveJob.Checkpoints[CheckpointsReached];
            }
        }

        public JobSessionSnapshot GetSnapshot()
        {
            JobDefinition? job = ActiveJob;
            JobCheckpointSpec? next = Status == JobSessionStatus.Active ? NextCheckpoint : null;
            return new JobSessionSnapshot(
                Status,
                job != null ? job.Id : string.Empty,
                job != null ? job.JobType : JobType.CourierGig,
                FailReason,
                ElapsedSeconds,
                job != null ? job.TimeLimitSeconds : 0f,
                CheckpointsReached,
                job != null ? job.Checkpoints.Count : 0,
                next != null ? next.Id : string.Empty,
                CargoIntegrity,
                RouteProgressMeters,
                RouteLengthMeters,
                Rally != null ? Rally.GetPlayerPlacement() : 0,
                Rally != null ? Rally.FieldSize : 0,
                HasSeizedDuringJob,
                Status == JobSessionStatus.Completed && _uncollected.Count == 0);
        }

        private CheckpointReportResult AdvanceCheckpoint()
        {
            JobDefinition job = ActiveJob!;
            CheckpointsReached++;
            int idx = CheckpointsReached - 1;
            if (idx < _cumulative.Length)
            {
                RouteProgressMeters = SimMath.Max(RouteProgressMeters, _cumulative[idx]);
            }
            if (CheckpointsReached < job.Checkpoints.Count)
            {
                return CheckpointReportResult.Reached;
            }
            Complete(job);
            return CheckpointReportResult.JobCompleted;
        }

        private void Complete(JobDefinition job)
        {
            bool onTime = job.TimeLimitSeconds <= 0f || ElapsedSeconds <= job.TimeLimitSeconds;
            bool cargoJob = IsCargoJobDefinition(job);
            decimal integrityFactor = cargoJob ? (decimal)SimMath.Clamp01(CargoIntegrity) : 1m;

            int placement = 0;
            int fieldSize = 0;
            bool survival = false;
            decimal bonusFactor = onTime ? integrityFactor : 0m;
            if (Rally != null)
            {
                Rally.FinishPlayer();
                placement = Rally.PlayerFinalPlacement;
                fieldSize = Rally.FieldSize;
                survival = Rally.IsPackSurvivalBonusEarned;
                decimal placementFactor = fieldSize > 1 ? (decimal)(fieldSize - placement) / (fieldSize - 1) : 1m;
                bonusFactor = onTime ? placementFactor : 0m;
            }

            decimal basePay = RoundUsd(job.RewardUsd * integrityFactor);
            decimal bonusPay = RoundUsd(job.BonusRewardUsd * bonusFactor);
            decimal survivalPay = survival ? RoundUsd(job.RewardUsd * (decimal)SimMath.Max(0f, Config.RallySurvivalBonusFraction)) : 0m;

            int reputation = job.ReputationReward;
            if (!onTime || HasSeizedDuringJob)
            {
                reputation = (int)Math.Round(job.ReputationReward * SimMath.Clamp01(Config.ReducedReputationFraction), MidpointRounding.AwayFromZero);
            }

            var record = new CompletedGigRecord(
                job.Id,
                job.JobType,
                ElapsedSeconds,
                onTime,
                cargoJob ? SimMath.Clamp01(CargoIntegrity) : 1f,
                placement,
                fieldSize,
                survival,
                basePay,
                bonusPay,
                survivalPay,
                reputation);

            Status = JobSessionStatus.Completed;
            LastCompletedRecord = record;
            _completed.Add(record);
            _uncollected.Add(record);
        }

        private void DegradeCargo(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            CargoIntegrity = SimMath.Clamp01(CargoIntegrity - amount);
            if (CargoIntegrity <= Config.CargoDestroyedThreshold)
            {
                Fail(JobFailReason.CargoDestroyed);
            }
        }

        private void Fail(JobFailReason reason)
        {
            Status = JobSessionStatus.Failed;
            FailReason = reason;
        }

        private static bool IsCargoJobDefinition(JobDefinition job)
        {
            return job.RequiredCargoSlots > 0 || job.RequiredCargoKg > 0f;
        }

        private static decimal RoundUsd(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
