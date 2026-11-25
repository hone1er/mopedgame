using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Applies Bay Area zone atmosphere from catalog ZoneEnvironmentSpec data (ambient air density, base surface grip,
    /// ambient temperature) and blends in Karl the Fog volumes. Exposes the resulting AmbientAirDensity / grip multiplier
    /// that MopedSim.App feeds into RideInputState; engine enrichment/leaning effects are computed by MopedSim.Core.
    /// Only visual fog/wind presentation lives here; no per-zone constants are hardcoded.
    /// <para>
    /// <see cref="KarlTheFogVolume"/> triggers (in additive Zone_* scenes) register while the tracked rider rigidbody is
    /// inside; each frame the deepest registered volume at the rider position drives <see cref="SetKarlFogIntensity"/>.
    /// </para>
    /// </summary>
    public sealed class BayAreaWeatherController : MonoBehaviour
    {
        private const float FogIntensityEpsilon = 0.002f;

        [Header("Karl the Fog Volume Blend")]
        [SerializeField] private float _karlFogAirDensity = 1.09f;
        [SerializeField] private float _wetFogGripMultiplier = 0.76f;

        [Header("Render Fog")]
        [SerializeField] private Color _clearHorizonColor = new Color(0.84f, 0.89f, 0.96f, 1f);
        [SerializeField] private Color _karlFogColor = new Color(0.76f, 0.80f, 0.84f, 1f);
        [SerializeField] private float _maxFogDensity = 0.022f;
        [SerializeField] private bool _driveRenderSettings = true;

        private string _activeZoneId = string.Empty;
        private float _zoneAirDensity = RideInputState.StandardAirDensity;
        private float _zoneGrip = 1f;
        private float _zoneAmbientTempC = 18f;
        private float _zoneFogIntensity;
        private float _fogVolumeIntensity;
        private Vector3 _windVelocityMps;

        [Header("Karl the Fog Volume Tracking")]
        [SerializeField] private Rigidbody _trackedRider;
        private readonly List<KarlTheFogVolume> _activeFogVolumes = new List<KarlTheFogVolume>(4);

        /// <summary>
        /// The enabled weather controller (one per session); fog volumes in additive zone scenes resolve it here because
        /// cross-scene serialized references are not possible.
        /// </summary>
        public static BayAreaWeatherController Active { get; private set; }

        public Rigidbody TrackedRider
        {
            get { return _trackedRider; }
        }

        public int ActiveFogVolumeCount
        {
            get { return _activeFogVolumes.Count; }
        }

        public string ActiveZoneId
        {
            get { return _activeZoneId; }
        }

        /// <summary>
        /// Combined visual fog intensity (zone baseline or an active Karl the Fog volume, whichever is denser).
        /// </summary>
        public float FogIntensity
        {
            get { return Mathf.Max(_zoneFogIntensity, _fogVolumeIntensity); }
        }

        /// <summary>
        /// Normalized air density for RideInputState.AmbientAirDensity (zone value blended toward Karl fog inside fog volumes).
        /// </summary>
        public float CurrentAmbientAirDensity
        {
            get { return Mathf.Lerp(_zoneAirDensity, Mathf.Max(_zoneAirDensity, _karlFogAirDensity), _fogVolumeIntensity); }
        }

        /// <summary>
        /// Surface grip multiplier for the ride controller (zone base grip, reduced inside wet fog volumes).
        /// </summary>
        public float CurrentSurfaceGripMultiplier
        {
            get { return _zoneGrip * Mathf.Lerp(1f, _wetFogGripMultiplier, _fogVolumeIntensity); }
        }

        public float CurrentAmbientTemperatureC
        {
            get { return _zoneAmbientTempC; }
        }

        public Vector3 CurrentWindVelocityMps
        {
            get { return _windVelocityMps; }
        }

        /// <summary>
        /// Applies a catalog zone's environment. fogIntensity01 / wind come from zone metadata when available.
        /// </summary>
        public void ApplyZoneEnvironment(ZoneEnvironmentSpec zone, float fogIntensity01, Vector3 windVelocityMps)
        {
            if (zone == null)
            {
                return;
            }

            _activeZoneId = zone.Id ?? string.Empty;
            _zoneAirDensity = zone.AmbientAirDensity > 0f ? zone.AmbientAirDensity : RideInputState.StandardAirDensity;
            _zoneGrip = zone.BaseSurfaceGrip > 0f ? zone.BaseSurfaceGrip : 1f;
            _zoneAmbientTempC = zone.AmbientTemperatureC;
            _zoneFogIntensity = Mathf.Clamp01(fogIntensity01);
            _windVelocityMps = windVelocityMps;
            ApplyRenderSettingsFog();
        }

        /// <summary>
        /// Applies a catalog zone's environment, deriving the visual fog baseline from its air density
        /// (dense cool marine air reads as fog).
        /// </summary>
        public void ApplyZoneEnvironment(ZoneEnvironmentSpec zone)
        {
            if (zone == null)
            {
                return;
            }

            float fog = Mathf.InverseLerp(RideInputState.StandardAirDensity, _karlFogAirDensity, zone.AmbientAirDensity);
            ApplyZoneEnvironment(zone, fog, _windVelocityMps);
        }

        /// <summary>
        /// Sets the intensity of the Karl the Fog volume the rider is currently inside (0 when outside any volume).
        /// </summary>
        public void SetKarlFogIntensity(float intensity01)
        {
            _fogVolumeIntensity = Mathf.Clamp01(intensity01);
            ApplyRenderSettingsFog();
        }

        public void SetWindVelocity(Vector3 windMps)
        {
            _windVelocityMps = windMps;
        }

        /// <summary>
        /// Sets the rider rigidbody whose colliders activate Karl the Fog volumes (wired by MopedSim.App at bootstrap).
        /// </summary>
        public void SetTrackedRider(Rigidbody rider)
        {
            _trackedRider = rider;
        }

        public bool IsTrackedRider(Rigidbody body)
        {
            return body != null && body == _trackedRider;
        }

        /// <summary>
        /// Called by a <see cref="KarlTheFogVolume"/> when the tracked rider enters it. Duplicate registrations are ignored.
        /// </summary>
        public void RegisterFogVolume(KarlTheFogVolume volume)
        {
            if (volume == null || _activeFogVolumes.Contains(volume))
            {
                return;
            }

            _activeFogVolumes.Add(volume);
            RefreshFogVolumeIntensity();
        }

        /// <summary>
        /// Called by a <see cref="KarlTheFogVolume"/> when the tracked rider leaves it (or it is disabled).
        /// </summary>
        public void UnregisterFogVolume(KarlTheFogVolume volume)
        {
            if (_activeFogVolumes.Remove(volume))
            {
                RefreshFogVolumeIntensity();
            }
        }

        /// <summary>
        /// Recomputes the fog-volume blend as the maximum intensity of registered volumes at the tracked rider position
        /// (volume peak intensity when no rider is tracked). Allocation-free; called every LateUpdate.
        /// </summary>
        public void RefreshFogVolumeIntensity()
        {
            float intensity = 0f;
            for (int i = _activeFogVolumes.Count - 1; i >= 0; i--)
            {
                KarlTheFogVolume volume = _activeFogVolumes[i];
                if (volume == null)
                {
                    _activeFogVolumes.RemoveAt(i);
                    continue;
                }

                // Interpolated transform position: render fog follows what the camera sees.
                float v = _trackedRider != null ? volume.EvaluateIntensityAt(_trackedRider.transform.position) : volume.PeakIntensity;
                if (v > intensity)
                {
                    intensity = v;
                }
            }

            if (Mathf.Abs(intensity - _fogVolumeIntensity) > FogIntensityEpsilon || (intensity == 0f && _fogVolumeIntensity != 0f))
            {
                SetKarlFogIntensity(intensity);
            }
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }

            _activeFogVolumes.Clear();
        }

        private void LateUpdate()
        {
            if (_activeFogVolumes.Count > 0 || _fogVolumeIntensity > 0f)
            {
                RefreshFogVolumeIntensity();
            }
        }

        private void ApplyRenderSettingsFog()
        {
            if (!_driveRenderSettings)
            {
                return;
            }

            float fog = FogIntensity;
            RenderSettings.fog = fog > 0.01f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(_clearHorizonColor, _karlFogColor, fog);
            RenderSettings.fogDensity = fog * _maxFogDensity;
        }
    }
}
