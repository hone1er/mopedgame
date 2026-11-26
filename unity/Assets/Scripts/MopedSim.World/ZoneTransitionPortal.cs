using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Boundary trigger linking two additive Bay Area zone scenes. When the tracked ride rigidbody (the one
    /// <see cref="BayAreaWeatherController.Active"/> tracks, or an explicitly assigned rider) enters the trigger, the portal asks
    /// <see cref="ZoneStreamManager.Active"/> to stream in its target zone and make it active. Target zone names are authored
    /// data (the scene builder copies them from zones.json <c>connectedZones</c>), never hard-coded here. Transitions are
    /// debounced and never re-entered while one is in flight. Presentation only; allocation-free while idle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ZoneTransitionPortal : MonoBehaviour
    {
        [Tooltip("Zone scene streamed in and made active when the rider crosses this boundary (from zones.json connectedZones).")]
        [SerializeField] private string _targetZoneScene = string.Empty;

        [Tooltip("Unload every other streamed zone after the transition completes.")]
        [SerializeField] private bool _unloadOtherZones;

        [Tooltip("Minimum seconds between two transitions fired by this portal.")]
        [SerializeField, Min(0f)] private float _debounceSeconds = 1.5f;

        [Tooltip("Optional explicit stream manager; zone scenes are additive, so this normally resolves to ZoneStreamManager.Active.")]
        [SerializeField] private ZoneStreamManager _zones;

        [Tooltip("Optional explicit rider; otherwise the rigidbody tracked by BayAreaWeatherController.Active.")]
        [SerializeField] private Rigidbody _rider;

        private float _lastTriggerTime = float.NegativeInfinity;
        private bool _inFlight;
        private int _transitionCount;

        /// <summary>Raised on the main thread after the stream manager accepted the transition (argument: target zone).</summary>
        public event Action<string> TransitionCompleted;

        public string TargetZoneScene
        {
            get { return _targetZoneScene ?? string.Empty; }
        }

        public bool UnloadOtherZones
        {
            get { return _unloadOtherZones; }
        }

        public float DebounceSeconds
        {
            get { return _debounceSeconds; }
        }

        /// <summary>True while a transition requested by this portal is streaming.</summary>
        public bool IsTransitionInFlight
        {
            get { return _inFlight; }
        }

        /// <summary>Number of transitions this portal has completed successfully.</summary>
        public int TransitionCount
        {
            get { return _transitionCount; }
        }

        public void Configure(string targetZoneScene, bool unloadOtherZones, float debounceSeconds)
        {
            _targetZoneScene = targetZoneScene ?? string.Empty;
            _unloadOtherZones = unloadOtherZones;
            _debounceSeconds = Mathf.Max(0f, debounceSeconds);
        }

        public void BindStreamManager(ZoneStreamManager zones)
        {
            _zones = zones;
        }

        public void BindRider(Rigidbody rider)
        {
            _rider = rider;
        }

        /// <summary>True when <paramref name="body"/> is the rider this portal reacts to.</summary>
        public bool IsTrackedRider(Rigidbody body)
        {
            if (body == null)
            {
                return false;
            }

            if (_rider != null)
            {
                return body == _rider;
            }

            BayAreaWeatherController weather = BayAreaWeatherController.Active;
            return weather != null && weather.IsTrackedRider(body);
        }

        /// <summary>
        /// Requests the transition (debounced). Returns a completed false task when debounced, already in flight, already in the
        /// target zone, or when no stream manager / target is available. Used by OnTriggerEnter and by tests without physics.
        /// </summary>
        public Task<bool> RequestTransitionAsync()
        {
            ZoneStreamManager zones = _zones != null ? _zones : ZoneStreamManager.Active;
            string target = _targetZoneScene;
            if (zones == null || string.IsNullOrEmpty(target) || _inFlight)
            {
                return Task.FromResult(false);
            }

            if (string.Equals(zones.ActiveZoneName, target, StringComparison.Ordinal) && zones.IsZoneLoaded(target))
            {
                return Task.FromResult(false);
            }

            float now = Time.unscaledTime;
            if (now - _lastTriggerTime < _debounceSeconds)
            {
                return Task.FromResult(false);
            }

            _lastTriggerTime = now;
            return RunTransitionAsync(zones, target, _unloadOtherZones);
        }

        private async Task<bool> RunTransitionAsync(ZoneStreamManager zones, string target, bool unloadOthers)
        {
            _inFlight = true;
            bool ok = false;
            try
            {
                ok = await zones.TransitionToZoneAsync(target, unloadOthers);
            }
            finally
            {
                // This portal's own scene may have been unloaded during the await (destroyed MonoBehaviour): only plain
                // managed fields are touched here, never the transform / GameObject.
                _inFlight = false;
            }

            if (ok)
            {
                _transitionCount++;
                Action<string> handler = TransitionCompleted;
                if (handler != null)
                {
                    handler.Invoke(target);
                }
            }

            return ok;
        }

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
            {
                c.isTrigger = true;
            }
        }

        private void Awake()
        {
            Collider c = GetComponent<Collider>();
            if (c != null && !c.isTrigger)
            {
                c.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null && IsTrackedRider(other.attachedRigidbody))
            {
                ObserveTransition(RequestTransitionAsync());
            }
        }

        private static async void ObserveTransition(Task<bool> transition)
        {
            try
            {
                await transition;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
