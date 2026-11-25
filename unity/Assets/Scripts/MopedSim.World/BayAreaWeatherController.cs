using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// Controls Karl the Fog coastal mist volumes, ambient air density (feeding BikeSimulation),
    /// wet surface grip modifiers, and Bay Crossing headwinds/crosswinds.
    /// </summary>
    public sealed class BayAreaWeatherController : MonoBehaviour
    {
        [Header("Atmospheric Baseline")]
        [SerializeField] private float _dryAirDensity = 1.00f;
        [SerializeField] private float _karlFogAirDensity = 1.09f;
        [SerializeField] private float _wetFogGripMultiplier = 0.78f;
        [SerializeField] private Color _clearHorizonColor = new Color(0.84f, 0.89f, 0.96f, 1f);
        [SerializeField] private Color _karlFogColor = new Color(0.76f, 0.80f, 0.84f, 1f);
        [SerializeField] private float _maxFogDensity = 0.022f;

        private float _fogIntensity;
        private Vector3 _windVelocityMps;
        private string _activeZoneId = "Zone_WestOakland";

        public float FogIntensity
        {
            get { return _fogIntensity; }
        }

        public float CurrentAmbientAirDensity
        {
            get { return Mathf.Lerp(_dryAirDensity, _karlFogAirDensity, _fogIntensity); }
        }

        public float CurrentSurfaceGripMultiplier
        {
            get { return Mathf.Lerp(1.0f, _wetFogGripMultiplier, _fogIntensity); }
        }

        public Vector3 CurrentWindVelocityMps
        {
            get { return _windVelocityMps; }
        }

        public string ActiveZoneId
        {
            get { return _activeZoneId; }
        }

        /// <summary>
        /// Applies weather presets for the specified Bay Area zone ID or Karl the Fog volume override.
        /// </summary>
        public void ApplyZoneWeather(string zoneId, float fogOverride = -1f)
        {
            _activeZoneId = string.IsNullOrEmpty(zoneId) ? "Zone_WestOakland" : zoneId;

            if (fogOverride >= 0f)
            {
                _fogIntensity = Mathf.Clamp01(fogOverride);
            }
            else if (string.Equals(_activeZoneId, "Zone_SF_Mission", System.StringComparison.OrdinalIgnoreCase))
            {
                _fogIntensity = 0.85f;
                _windVelocityMps = new Vector3(-2.5f, 0f, -1.8f);
            }
            else if (string.Equals(_activeZoneId, "Zone_BayCrossing", System.StringComparison.OrdinalIgnoreCase))
            {
                _fogIntensity = 0.45f;
                _windVelocityMps = new Vector3(-7.5f, 0f, -4.5f);
            }
            else if (string.Equals(_activeZoneId, "Zone_OaklandHills", System.StringComparison.OrdinalIgnoreCase))
            {
                _fogIntensity = 0.15f;
                _windVelocityMps = new Vector3(-1.2f, 0f, -0.5f);
            }
            else
            {
                _fogIntensity = 0.05f;
                _windVelocityMps = Vector3.zero;
            }

            ApplyRenderSettingsFog();
        }

        public void SetKarlFogIntensity(float intensity01)
        {
            _fogIntensity = Mathf.Clamp01(intensity01);
            ApplyRenderSettingsFog();
        }

        public void SetWindVelocity(Vector3 windMps)
        {
            _windVelocityMps = windMps;
        }

        private void ApplyRenderSettingsFog()
        {
            RenderSettings.fog = _fogIntensity > 0.01f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(_clearHorizonColor, _karlFogColor, _fogIntensity);
            RenderSettings.fogDensity = _fogIntensity * _maxFogDensity;
        }
    }
}
