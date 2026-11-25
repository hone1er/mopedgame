using System;
using System.Collections.Generic;
using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Zero-allocation (per FixedUpdate) detector for Bay Area road surfaces,
    /// pothole impulses, and shallow-angle (<20 deg) SF Muni steel rail track slip.
    /// </summary>
    public sealed class SurfaceHazardDetector
    {
        public delegate bool ExternalSurfaceResolver(Collider collider, out SurfaceHazardSample sample);

        public const float ShallowRailAngleThresholdDeg = 20f;

        private readonly Dictionary<int, SurfaceHazardSample> _colliderCache = new Dictionary<int, SurfaceHazardSample>(64);
        private ExternalSurfaceResolver _externalResolver;

        public void SetExternalResolver(ExternalSurfaceResolver resolver)
        {
            _externalResolver = resolver;
            _colliderCache.Clear();
        }

        public void ClearCache()
        {
            _colliderCache.Clear();
        }

        /// <summary>
        /// Resolves the surface hazard for a wheel hit collider without allocating on repeated hits.
        /// </summary>
        public SurfaceHazardSample ResolveColliderSurface(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return SurfaceHazardSample.FromSurfaceType(RoadSurfaceType.DryAsphalt, Vector3.forward, false);
            }

            int instanceId = hitCollider.GetInstanceID();
            SurfaceHazardSample cached;
            if (_colliderCache.TryGetValue(instanceId, out cached))
            {
                return cached;
            }

            SurfaceHazardSample resolved = ResolveColliderColdPath(hitCollider);
            _colliderCache[instanceId] = resolved;
            return resolved;
        }

        /// <summary>
        /// Computes lateral slip acceleration (m/s^2) when crossing a SteelRailTrack at a shallow angle (<20 deg).
        /// Crossing perpendicular (90 deg) returns zero lateral slip impulse.
        /// </summary>
        public static float ComputeRailLateralSlipAccel(
            Vector3 bikeForwardWorld,
            Vector3 railAxisWorld,
            float forwardSpeedMps,
            float railGripMultiplier,
            out float crossingAngleDeg)
        {
            Vector3 bikeFlat = new Vector3(bikeForwardWorld.x, 0f, bikeForwardWorld.z);
            Vector3 railFlat = new Vector3(railAxisWorld.x, 0f, railAxisWorld.z);

            if (bikeFlat.sqrMagnitude < 0.0001f || railFlat.sqrMagnitude < 0.0001f)
            {
                crossingAngleDeg = 90f;
                return 0f;
            }

            bikeFlat.Normalize();
            railFlat.Normalize();

            float absDot = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(bikeFlat, railFlat)));
            crossingAngleDeg = Mathf.Acos(absDot) * Mathf.Rad2Deg;

            if (crossingAngleDeg >= ShallowRailAngleThresholdDeg || forwardSpeedMps < 1.5f)
            {
                return 0f;
            }

            // Shallow crossing severity scales from 0 at 20 deg up to 1.0 when nearly parallel.
            float shallowness = 1f - (crossingAngleDeg / ShallowRailAngleThresholdDeg);
            Vector3 railRight = new Vector3(-railFlat.z, 0f, railFlat.x);
            float sideSign = Vector3.Dot(bikeFlat, railRight) >= 0f ? 1f : -1f;
            float speedFactor = Mathf.Clamp01(forwardSpeedMps / 12f);
            float gripLoss = Mathf.Clamp01(1f - railGripMultiplier);

            return sideSign * shallowness * speedFactor * (4.5f + 6.5f * gripLoss);
        }

        private SurfaceHazardSample ResolveColliderColdPath(Collider hitCollider)
        {
            if (_externalResolver != null)
            {
                SurfaceHazardSample externalSample;
                if (_externalResolver(hitCollider, out externalSample))
                {
                    return externalSample;
                }
            }

            RideSurfaceMarker marker = hitCollider.GetComponent<RideSurfaceMarker>();
            if (marker != null)
            {
                return marker.CreateSample();
            }

            PhysicsMaterial physMat = hitCollider.sharedMaterial;
            if (physMat != null)
            {
                RoadSurfaceType fromMat;
                if (TryParseSurfaceName(physMat.name, out fromMat))
                {
                    return SurfaceHazardSample.FromSurfaceType(
                        fromMat,
                        hitCollider.transform.forward,
                        fromMat == RoadSurfaceType.SteelRailTrack);
                }
            }

            RoadSurfaceType fromName;
            if (TryParseSurfaceName(hitCollider.name, out fromName))
            {
                return SurfaceHazardSample.FromSurfaceType(
                    fromName,
                    hitCollider.transform.forward,
                    fromName == RoadSurfaceType.SteelRailTrack);
            }

            return SurfaceHazardSample.FromSurfaceType(RoadSurfaceType.DryAsphalt, Vector3.forward, false);
        }

        private static bool TryParseSurfaceName(string candidate, out RoadSurfaceType surfaceType)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                surfaceType = RoadSurfaceType.DryAsphalt;
                return false;
            }

            if (candidate.IndexOf("SteelRail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidate.IndexOf("MuniRail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidate.IndexOf("RailTrack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                surfaceType = RoadSurfaceType.SteelRailTrack;
                return true;
            }

            if (candidate.IndexOf("Pothole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidate.IndexOf("Patched", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                surfaceType = RoadSurfaceType.PatchedPotholeAsphalt;
                return true;
            }

            if (candidate.IndexOf("WetAsphalt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidate.IndexOf("Wet", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                surfaceType = RoadSurfaceType.WetAsphalt;
                return true;
            }

            if (candidate.IndexOf("Gravel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                candidate.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                surfaceType = RoadSurfaceType.GravelShoulder;
                return true;
            }

            surfaceType = RoadSurfaceType.DryAsphalt;
            return false;
        }
    }
}
