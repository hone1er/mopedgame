using System;
using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Bay Area road surface classifications from PROJECT_GUIDE.md section 2.4.
    /// </summary>
    public enum RoadSurfaceType
    {
        DryAsphalt = 0,
        PatchedPotholeAsphalt = 1,
        WetAsphalt = 2,
        SteelRailTrack = 3,
        GravelShoulder = 4
    }

    /// <summary>
    /// Immutable value snapshot describing road surface physics and vibration properties.
    /// </summary>
    public readonly struct SurfaceHazardSample
    {
        public readonly RoadSurfaceType SurfaceType;
        public readonly float RollingDragMultiplier;
        public readonly float GripMultiplier;
        public readonly float Roughness;
        public readonly float PotholeImpulse;
        public readonly Vector3 RailAxisWorld;
        public readonly bool HasRailAxis;

        public SurfaceHazardSample(
            RoadSurfaceType surfaceType,
            float rollingDragMultiplier,
            float gripMultiplier,
            float roughness,
            float potholeImpulse,
            Vector3 railAxisWorld,
            bool hasRailAxis)
        {
            SurfaceType = surfaceType;
            RollingDragMultiplier = rollingDragMultiplier;
            GripMultiplier = gripMultiplier;
            Roughness = roughness;
            PotholeImpulse = potholeImpulse;
            RailAxisWorld = railAxisWorld;
            HasRailAxis = hasRailAxis;
        }

        public static SurfaceHazardSample FromSurfaceType(RoadSurfaceType surfaceType, Vector3 railAxisWorld, bool hasRailAxis)
        {
            switch (surfaceType)
            {
                case RoadSurfaceType.PatchedPotholeAsphalt:
                    return new SurfaceHazardSample(surfaceType, 1.08f, 0.90f, 0.65f, 1.25f, Vector3.forward, false);
                case RoadSurfaceType.WetAsphalt:
                    return new SurfaceHazardSample(surfaceType, 1.10f, 0.76f, 0.10f, 0f, Vector3.forward, false);
                case RoadSurfaceType.SteelRailTrack:
                    return new SurfaceHazardSample(surfaceType, 0.95f, 0.38f, 0.25f, 0.20f, railAxisWorld, hasRailAxis);
                case RoadSurfaceType.GravelShoulder:
                    return new SurfaceHazardSample(surfaceType, 1.35f, 0.62f, 0.50f, 0.35f, Vector3.forward, false);
                case RoadSurfaceType.DryAsphalt:
                default:
                    return new SurfaceHazardSample(RoadSurfaceType.DryAsphalt, 1.00f, 1.00f, 0.05f, 0f, Vector3.forward, false);
            }
        }
    }

    /// <summary>
    /// Optional component placed on road/track colliders to explicitly declare a surface hazard.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RideSurfaceMarker : MonoBehaviour
    {
        [SerializeField] private RoadSurfaceType _surfaceType = RoadSurfaceType.DryAsphalt;
        [SerializeField] private Vector3 _localRailAxis = Vector3.forward;
        [SerializeField] private float _potholeImpulseOverride = -1f;

        public RoadSurfaceType SurfaceType
        {
            get { return _surfaceType; }
            set { _surfaceType = value; }
        }

        public Vector3 LocalRailAxis
        {
            get { return _localRailAxis; }
            set { _localRailAxis = value; }
        }

        public SurfaceHazardSample CreateSample()
        {
            Vector3 worldRail = transform.TransformDirection(_localRailAxis.sqrMagnitude > 0.0001f ? _localRailAxis.normalized : Vector3.forward);
            SurfaceHazardSample baseSample = SurfaceHazardSample.FromSurfaceType(_surfaceType, worldRail, _surfaceType == RoadSurfaceType.SteelRailTrack);
            if (_potholeImpulseOverride >= 0f)
            {
                return new SurfaceHazardSample(
                    baseSample.SurfaceType,
                    baseSample.RollingDragMultiplier,
                    baseSample.GripMultiplier,
                    baseSample.Roughness,
                    _potholeImpulseOverride,
                    baseSample.RailAxisWorld,
                    baseSample.HasRailAxis);
            }

            return baseSample;
        }
    }
}
