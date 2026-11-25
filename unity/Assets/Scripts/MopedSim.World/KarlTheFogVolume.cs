using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Trigger volume for a Karl the Fog bank. While the tracked rider rigidbody is inside, the volume registers with the
    /// active <see cref="BayAreaWeatherController"/>, which blends fog density, air density and wet grip from the deepest
    /// volume (max intensity, so overlapping banks never fight). Intensity fades in over <c>_edgeFadeMeters</c> from the
    /// collider's horizontal (x/z) edge. Presentation only: engine enrichment from denser air is computed by MopedSim.Core from
    /// RideInputState.AmbientAirDensity. Allocation-free at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class KarlTheFogVolume : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _peakIntensity = 0.9f;
        [SerializeField, Min(0f)] private float _edgeFadeMeters = 15f;

        [Tooltip("Optional explicit weather controller; zone scenes are additive, so this normally resolves to BayAreaWeatherController.Active.")]
        [SerializeField] private BayAreaWeatherController _weather;

        private Collider _collider;
        private BayAreaWeatherController _registeredWith;
        private int _riderColliderCount;

        public float PeakIntensity
        {
            get { return _peakIntensity; }
        }

        public float EdgeFadeMeters
        {
            get { return _edgeFadeMeters; }
        }

        /// <summary>
        /// True while at least one collider of the tracked rider overlaps this volume.
        /// </summary>
        public bool IsRiderInside
        {
            get { return _riderColliderCount > 0; }
        }

        /// <summary>
        /// World-space bounds of the trigger collider (falls back to a zero-size box at the transform before Awake).
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                Collider c = ResolveCollider();
                return c != null ? c.bounds : new Bounds(transform.position, Vector3.zero);
            }
        }

        public void Configure(float peakIntensity, float edgeFadeMeters)
        {
            _peakIntensity = Mathf.Clamp01(peakIntensity);
            _edgeFadeMeters = Mathf.Max(0f, edgeFadeMeters);
        }

        /// <summary>
        /// Overrides the weather controller this volume reports to (otherwise <see cref="BayAreaWeatherController.Active"/>).
        /// </summary>
        public void BindWeather(BayAreaWeatherController weather)
        {
            if (_registeredWith != null && _registeredWith != weather)
            {
                _registeredWith.UnregisterFogVolume(this);
                _registeredWith = null;
            }

            _weather = weather;
            if (IsRiderInside)
            {
                RegisterWithWeather();
            }
        }

        /// <summary>
        /// Fog intensity at a world position: 0 outside the bounds, ramping to the peak over the horizontal edge fade distance.
        /// </summary>
        public float EvaluateIntensityAt(Vector3 worldPosition)
        {
            Bounds b = WorldBounds;
            if (!b.Contains(worldPosition))
            {
                return 0f;
            }

            if (_edgeFadeMeters <= 0f)
            {
                return _peakIntensity;
            }

            // Fog banks roll in horizontally: fade on x/z depth only, so a rider near the bottom of a tall volume still gets full fog.
            Vector3 min = b.min;
            Vector3 max = b.max;
            float depth = Mathf.Min(
                Mathf.Min(worldPosition.x - min.x, max.x - worldPosition.x),
                Mathf.Min(worldPosition.z - min.z, max.z - worldPosition.z));
            return _peakIntensity * Mathf.Clamp01(depth / _edgeFadeMeters);
        }

        /// <summary>
        /// Called when a tracked-rider collider enters (also used directly by EditMode tests without physics).
        /// </summary>
        public void NotifyRiderEntered()
        {
            _riderColliderCount++;
            if (_riderColliderCount == 1)
            {
                RegisterWithWeather();
            }
        }

        /// <summary>
        /// Called when a tracked-rider collider exits; the volume unregisters once every rider collider has left.
        /// </summary>
        public void NotifyRiderExited()
        {
            if (_riderColliderCount == 0)
            {
                return;
            }

            _riderColliderCount--;
            if (_riderColliderCount == 0)
            {
                UnregisterFromWeather();
            }
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
            Collider c = ResolveCollider();
            if (c != null && !c.isTrigger)
            {
                c.isTrigger = true;
            }
        }

        private void OnDisable()
        {
            _riderColliderCount = 0;
            UnregisterFromWeather();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsTrackedRider(other))
            {
                NotifyRiderEntered();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsTrackedRider(other))
            {
                NotifyRiderExited();
            }
        }

        private bool IsTrackedRider(Collider other)
        {
            BayAreaWeatherController weather = ResolveWeather();
            return other != null && weather != null && weather.IsTrackedRider(other.attachedRigidbody);
        }

        private BayAreaWeatherController ResolveWeather()
        {
            return _weather != null ? _weather : BayAreaWeatherController.Active;
        }

        private Collider ResolveCollider()
        {
            if (_collider == null)
            {
                _collider = GetComponent<Collider>();
            }

            return _collider;
        }

        private void RegisterWithWeather()
        {
            BayAreaWeatherController weather = ResolveWeather();
            if (weather == null || weather == _registeredWith)
            {
                return;
            }

            UnregisterFromWeather();
            weather.RegisterFogVolume(this);
            _registeredWith = weather;
        }

        private void UnregisterFromWeather()
        {
            if (_registeredWith != null)
            {
                _registeredWith.UnregisterFogVolume(this);
                _registeredWith = null;
            }
        }
    }
}
