using MopedSim.Core;
using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Applies Bay Area zone atmosphere from catalog ZoneEnvironmentSpec data (ambient air density, base surface grip,
    /// ambient temperature) and blends in Karl the Fog volumes. Exposes the resulting AmbientAirDensity / grip multiplier
    /// that MopedSim.App feeds into RideInputState; engine enrichment/leaning effects are computed by MopedSim.Core.
    /// Only visual fog/wind presentation lives here; no per-zone constants are hardcoded.
    /// </summary>
    public sealed class BayAreaWeatherController : MonoBehaviour
    {
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
