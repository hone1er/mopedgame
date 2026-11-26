using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Mirrors the Core Bay Swarm rally pack (<see cref="JobSessionManager.Rally"/> / <see cref="RallyPackSimulator.Riders"/>) onto
    /// scene marker transforms. Rider progress, speed, breakdowns, and placement are owned by MopedSim.Core; this component only
    /// places each marker at the rider's route progress along the catalog checkpoint polyline (using Core's cumulative checkpoint
    /// distances) and hides DNF riders. Markers are pooled once per rally start; <see cref="LateUpdate"/> is allocation-free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RallyPackController : MonoBehaviour
    {
        [Tooltip("Optional marker template (e.g. a stylized moped prefab); a small capsule primitive is used when empty.")]
        [SerializeField] private GameObject _markerTemplate;

        [Tooltip("Parent for pooled markers (defaults to this transform).")]
        [SerializeField] private Transform _markerRoot;

        [Tooltip("Lateral spacing between riders so the pack reads as a swarm rather than a single file.")]
        [SerializeField, Min(0f)] private float _laneSpacingMeters = 1.2f;

        private readonly List<Transform> _markers = new List<Transform>(8);
        private CourierGigController _courier;
        private JobSessionManager _jobs;
        private RallyPackSimulator _mirrored;
        private int _activeMarkerCount;

        public int ActiveMarkerCount
        {
            get { return _activeMarkerCount; }
        }

        public RallyPackSimulator MirroredRally
        {
            get { return _mirrored; }
        }

        /// <summary>Marker for rider <paramref name="index"/> (matching <see cref="RallyPackSimulator.Riders"/>), or null.</summary>
        public Transform GetMarker(int index)
        {
            return index >= 0 && index < _activeMarkerCount ? _markers[index] : null;
        }

        /// <summary>Follows whatever session the courier controller is bound to (the normal App wiring).</summary>
        public void Bind(CourierGigController courier)
        {
            _courier = courier;
            _jobs = null;
            SyncMarkers();
        }

        /// <summary>Follows an explicit session manager (tests / tools).</summary>
        public void Bind(JobSessionManager jobs)
        {
            _courier = null;
            _jobs = jobs;
            SyncMarkers();
        }

        /// <summary>Re-reads the rally and places every marker. Allocation-free unless a new rally needs more pooled markers.</summary>
        public void SyncMarkers()
        {
            JobSessionManager jobs = ResolveJobs();
            RallyPackSimulator rally = jobs != null && jobs.ActiveJob != null && jobs.Status != JobSessionStatus.Abandoned ? jobs.Rally : null;
            if (!ReferenceEquals(rally, _mirrored))
            {
                _mirrored = rally;
                PrepareMarkers(rally);
            }

            if (rally == null || jobs.ActiveJob == null)
            {
                return;
            }

            List<JobCheckpointSpec> checkpoints = jobs.ActiveJob.Checkpoints;
            IReadOnlyList<float> distances = rally.CheckpointDistancesMeters;
            IReadOnlyList<RallyRiderState> riders = rally.Riders;
            for (int i = 0; i < _activeMarkerCount && i < riders.Count; i++)
            {
                RallyRiderState rider = riders[i];
                Transform marker = _markers[i];
                bool visible = !rider.IsDnf;
                if (marker.gameObject.activeSelf != visible)
                {
                    marker.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                Vector3 forward;
                Vector3 position = EvaluateRoute(checkpoints, distances, rider.ProgressMeters, out forward);
                Vector3 lateral = Vector3.Cross(Vector3.up, forward);
                float lane = ((i % 3) - 1) * _laneSpacingMeters;
                marker.SetPositionAndRotation(position + lateral * lane, forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward, Vector3.up) : marker.rotation);
            }
        }

        private void LateUpdate()
        {
            SyncMarkers();
        }

        private JobSessionManager ResolveJobs()
        {
            if (_courier != null)
            {
                return _courier.Jobs;
            }

            return _jobs;
        }

        private void PrepareMarkers(RallyPackSimulator rally)
        {
            int needed = rally != null ? rally.Riders.Count : 0;
            Transform parent = _markerRoot != null ? _markerRoot : transform;
            while (_markers.Count < needed)
            {
                GameObject go;
                if (_markerTemplate != null)
                {
                    go = Instantiate(_markerTemplate, parent);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Collider c = go.GetComponent<Collider>();
                    if (c != null)
                    {
                        // Markers are visual only: never collide with the ride moped or trip triggers.
                        Destroy(c);
                    }

                    go.transform.SetParent(parent, false);
                    go.transform.localScale = new Vector3(0.6f, 0.7f, 0.6f);
                }

                go.name = "RallyRider_" + _markers.Count.ToString("00");
                _markers.Add(go.transform);
            }

            for (int i = 0; i < _markers.Count; i++)
            {
                bool active = i < needed;
                if (_markers[i].gameObject.activeSelf != active)
                {
                    _markers[i].gameObject.SetActive(active);
                }
            }

            _activeMarkerCount = needed;
        }

        /// <summary>
        /// Piecewise-linear lookup of a route progress (meters from the first checkpoint) on the checkpoint polyline, using Core's
        /// cumulative checkpoint distances so markers agree with Core progress. Presentation interpolation only.
        /// </summary>
        public static Vector3 EvaluateRoute(List<JobCheckpointSpec> checkpoints, IReadOnlyList<float> cumulative, float progressMeters, out Vector3 forward)
        {
            forward = Vector3.forward;
            if (checkpoints == null || checkpoints.Count == 0)
            {
                return Vector3.zero;
            }

            int count = checkpoints.Count;
            if (cumulative == null || cumulative.Count < count || count == 1)
            {
                JobCheckpointSpec only = checkpoints[0];
                return new Vector3(only.X, only.Y, only.Z);
            }

            int segment = count - 2;
            for (int i = 1; i < count; i++)
            {
                if (progressMeters <= cumulative[i])
                {
                    segment = i - 1;
                    break;
                }
            }

            JobCheckpointSpec a = checkpoints[segment];
            JobCheckpointSpec b = checkpoints[segment + 1];
            Vector3 pa = new Vector3(a.X, a.Y, a.Z);
            Vector3 pb = new Vector3(b.X, b.Y, b.Z);
            float length = cumulative[segment + 1] - cumulative[segment];
            float t = length > 1e-4f ? Mathf.Clamp01((progressMeters - cumulative[segment]) / length) : 1f;
            Vector3 delta = pb - pa;
            delta.y = 0f;
            if (delta.sqrMagnitude > 1e-6f)
            {
                forward = delta.normalized;
            }

            return Vector3.Lerp(pa, pb, t);
        }
    }
}
