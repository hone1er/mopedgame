using UnityEngine;

namespace MopedSim.World
{
    /// <summary>
    /// World-space surface/hazard zone descriptor placed on road segments, Muni rail tracks,
    /// pothole patches, gravel shoulders, or Karl the Fog trigger volumes.
    /// Does not reference MopedSim.Ride directly; bridged via MopedSim.App.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoadHazardZone : MonoBehaviour
    {
        [SerializeField] private string _surfaceTypeId = "DryAsphalt";
        [SerializeField] private float _rollingDragMultiplier = 1.0f;
        [SerializeField] private float _gripMultiplier = 1.0f;
        [SerializeField] private float _roughness = 0.05f;
        [SerializeField] private float _potholeImpulse;
        [SerializeField] private Vector3 _localRailAxis = Vector3.forward;
        [SerializeField] private bool _isKarlFogVolume;
        [SerializeField] private float _fogVolumeIntensity = 0.9f;

        public string SurfaceTypeId
        {
            get { return _surfaceTypeId; }
            set { _surfaceTypeId = value; }
        }

        public float RollingDragMultiplier
        {
            get { return _rollingDragMultiplier; }
        }

        public float GripMultiplier
        {
            get { return _gripMultiplier; }
        }

        public float Roughness
        {
            get { return _roughness; }
        }

        public float PotholeImpulse
        {
            get { return _potholeImpulse; }
        }

        public bool IsKarlFogVolume
        {
            get { return _isKarlFogVolume; }
        }

        public float FogVolumeIntensity
        {
            get { return _fogVolumeIntensity; }
        }

        public Vector3 WorldRailAxis
        {
            get
            {
                Vector3 axis = _localRailAxis.sqrMagnitude > 0.0001f ? _localRailAxis.normalized : Vector3.forward;
                return transform.TransformDirection(axis);
            }
        }

        public void Configure(
            string surfaceTypeId,
            float rollingDragMultiplier,
            float gripMultiplier,
            float roughness,
            float potholeImpulse,
            Vector3 localRailAxis)
        {
            _surfaceTypeId = surfaceTypeId;
            _rollingDragMultiplier = rollingDragMultiplier;
            _gripMultiplier = gripMultiplier;
            _roughness = roughness;
            _potholeImpulse = potholeImpulse;
            _localRailAxis = localRailAxis;
        }

        public void ConfigureKarlFogVolume(float fogIntensity)
        {
            _isKarlFogVolume = true;
            _fogVolumeIntensity = Mathf.Clamp01(fogIntensity);
            _surfaceTypeId = "WetAsphalt";
            _rollingDragMultiplier = 1.10f;
            _gripMultiplier = 0.76f;
            _roughness = 0.10f;
        }
    }
}
