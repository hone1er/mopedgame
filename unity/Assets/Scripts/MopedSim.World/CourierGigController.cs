using System;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Thin scene adapter around Core <see cref="JobSessionManager"/> (courier gigs, hill climbs, speed traps, Bay Swarm rallies).
    /// Every rule (cargo checks, clock, cargo wear, payouts, reputation) stays in MopedSim.Core: this component only forwards
    /// commands, feeds the tracked rider's world position to <see cref="JobSessionManager.ReportPosition"/>, and raises events
    /// when Core reports a change. Catalog checkpoint coordinates are metric world positions (1 unit = 1 m).
    /// <see cref="StepJob"/> is allocation-free; MopedSim.App drives it once per frame with the frame's telemetry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CourierGigController : MonoBehaviour
    {
        [Tooltip("Rider whose position is reported against the next checkpoint (the ride moped; wired by MopedSim.App).")]
        [SerializeField] private Transform _rider;

        [Tooltip("Optional beacon moved onto the next checkpoint (e.g. an sf_delivery_beacon instance).")]
        [SerializeField] private Transform _nextCheckpointBeacon;

        private SimulationWorld _world;
        private BikeSimulation _bike;
        private JobSessionManager _jobs;
        private int _lastCheckpointsReached = -1;
        private JobSessionStatus _lastStatus = JobSessionStatus.Idle;

        /// <summary>Raised after every accept attempt (accepted or refused) with Core's result.</summary>
        public event Action<JobAcceptResult> JobAcceptAttempted;

        /// <summary>Raised when Core reports a checkpoint reached (argument: Reached or JobCompleted).</summary>
        public event Action<CheckpointReportResult> CheckpointReported;

        /// <summary>Raised when the session status changes (Active / Completed / Failed / Abandoned).</summary>
        public event Action<JobSessionStatus> StatusChanged;

        /// <summary>Raised after a successful reward collection with the world's new cash balance.</summary>
        public event Action<decimal> RewardCollected;

        public JobSessionManager Jobs
        {
            get { return _jobs; }
        }

        public bool IsBound
        {
            get { return _jobs != null && _world != null && _bike != null; }
        }

        public Transform Rider
        {
            get { return _rider; }
        }

        public JobDefinition ActiveJob
        {
            get { return _jobs != null ? _jobs.ActiveJob : null; }
        }

        public JobSessionStatus Status
        {
            get { return _jobs != null ? _jobs.Status : JobSessionStatus.Idle; }
        }

        public bool HasActiveJob
        {
            get { return _jobs != null && _jobs.Status == JobSessionStatus.Active; }
        }

        /// <summary>Display name of the active (or last) job; empty when none. Returns the catalog string (no allocation).</summary>
        public string ActiveJobTitle
        {
            get
            {
                JobDefinition job = ActiveJob;
                return job != null ? job.DisplayName : string.Empty;
            }
        }

        /// <summary>
        /// Binds the session to the world (reward target) and bike (cargo checks). Passing a manager keeps an existing session;
        /// null creates a fresh <see cref="JobSessionManager"/>.
        /// </summary>
        public void Bind(SimulationWorld world, BikeSimulation bike, JobSessionManager jobs)
        {
            _world = world;
            _bike = bike;
            _jobs = jobs ?? new JobSessionManager();
            _lastCheckpointsReached = _jobs.CheckpointsReached;
            _lastStatus = _jobs.Status;
            UpdateBeacon();
        }

        public void SetRider(Transform rider)
        {
            _rider = rider;
        }

        public void SetNextCheckpointBeacon(Transform beacon)
        {
            _nextCheckpointBeacon = beacon;
            UpdateBeacon();
        }

        /// <summary>Asks Core to start <paramref name="job"/> on the bound bike (Core enforces cargo rack / seize rules).</summary>
        public JobAcceptResult AcceptJob(JobDefinition job)
        {
            JobAcceptResult result;
            if (!IsBound)
            {
                result = new JobAcceptResult(false, JobRejectReason.NoBike, job != null ? job.Id : string.Empty, 0, 0f, CargoCapacity.None,
                    "Courier dispatch is not bound to a bike.");
            }
            else
            {
                result = _jobs.TryAcceptJob(job, _bike);
            }

            Action<JobAcceptResult> handler = JobAcceptAttempted;
            if (handler != null)
            {
                handler.Invoke(result);
            }

            SyncState();
            return result;
        }

        public bool AbandonJob()
        {
            bool abandoned = _jobs != null && _jobs.Abandon();
            SyncState();
            return abandoned;
        }

        /// <summary>Credits pending payouts / reputation to the bound world exactly once (Core rule).</summary>
        public bool CollectReward()
        {
            if (!IsBound || !_jobs.TryCollectReward(_world))
            {
                return false;
            }

            Action<decimal> handler = RewardCollected;
            if (handler != null)
            {
                handler.Invoke(_world.CashUsd);
            }

            return true;
        }

        /// <summary>
        /// Advances the active job with this frame's telemetry and reports the rider position against the next checkpoint.
        /// Zero allocations when nothing changes.
        /// </summary>
        public void StepJob(float dt, in EngineTelemetrySnapshot telemetry, float surfaceRoughness)
        {
            if (_jobs == null || _jobs.Status != JobSessionStatus.Active)
            {
                return;
            }

            _jobs.Step(dt, in telemetry, surfaceRoughness);
            if (_rider != null && _jobs.Status == JobSessionStatus.Active)
            {
                Vector3 p = _rider.position;
                _jobs.ReportPosition(p.x, p.y, p.z);
            }

            SyncState();
        }

        /// <summary>Advances the job clock only (no telemetry, no position report).</summary>
        public void StepJob(float dt)
        {
            if (_jobs == null || _jobs.Status != JobSessionStatus.Active)
            {
                return;
            }

            _jobs.Step(dt);
            SyncState();
        }

        /// <summary>Reports the given world position against the next checkpoint (e.g. from a beacon trigger).</summary>
        public CheckpointReportResult ReportRiderPosition(Vector3 worldPosition)
        {
            if (_jobs == null)
            {
                return CheckpointReportResult.NotActive;
            }

            CheckpointReportResult result = _jobs.ReportPosition(worldPosition.x, worldPosition.y, worldPosition.z);
            SyncState();
            return result;
        }

        public void ReportPothole(float severity)
        {
            if (_jobs != null)
            {
                _jobs.ReportPothole(severity);
                SyncState();
            }
        }

        public void ReportCrash()
        {
            if (_jobs != null)
            {
                _jobs.ReportCrash();
                SyncState();
            }
        }

        /// <summary>Allocation-free HUD snapshot (default when unbound).</summary>
        public JobSessionSnapshot GetSnapshot()
        {
            return _jobs != null ? _jobs.GetSnapshot() : default(JobSessionSnapshot);
        }

        /// <summary>World position of the next checkpoint; false when no job is active.</summary>
        public bool TryGetNextCheckpointPosition(out Vector3 position)
        {
            JobCheckpointSpec next = _jobs != null && _jobs.Status == JobSessionStatus.Active ? _jobs.NextCheckpoint : null;
            if (next == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = new Vector3(next.X, next.Y, next.Z);
            return true;
        }

        private void SyncState()
        {
            if (_jobs == null)
            {
                return;
            }

            int reached = _jobs.CheckpointsReached;
            JobSessionStatus status = _jobs.Status;
            bool checkpointAdvanced = reached != _lastCheckpointsReached && reached > 0 && _lastStatus == JobSessionStatus.Active;
            bool statusChanged = status != _lastStatus;
            _lastCheckpointsReached = reached;
            _lastStatus = status;

            if (!checkpointAdvanced && !statusChanged)
            {
                return;
            }

            UpdateBeacon();
            if (checkpointAdvanced)
            {
                Action<CheckpointReportResult> handler = CheckpointReported;
                if (handler != null)
                {
                    handler.Invoke(status == JobSessionStatus.Completed ? CheckpointReportResult.JobCompleted : CheckpointReportResult.Reached);
                }
            }

            if (statusChanged)
            {
                Action<JobSessionStatus> statusHandler = StatusChanged;
                if (statusHandler != null)
                {
                    statusHandler.Invoke(status);
                }
            }
        }

        private void UpdateBeacon()
        {
            if (_nextCheckpointBeacon == null)
            {
                return;
            }

            Vector3 position;
            bool active = TryGetNextCheckpointPosition(out position);
            if (active)
            {
                _nextCheckpointBeacon.position = position;
            }

            if (_nextCheckpointBeacon.gameObject.activeSelf != active)
            {
                _nextCheckpointBeacon.gameObject.SetActive(active);
            }
        }
    }
}
