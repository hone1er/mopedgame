using System;
using MopedSim.Ride;
using MopedSim.World;
using UnityEngine;

namespace MopedSim.App
{
    /// <summary>
    /// Bridges MopedSim.World RoadHazardZone descriptors into MopedSim.Ride surface samples so neither assembly references the other.
    /// Resolution happens on the ride detector's cold path (results are cached per collider), so it never runs per FixedUpdate.
    /// </summary>
    public static class WorldSurfaceBridge
    {
        /// <summary>
        /// SurfaceHazardDetector.ExternalSurfaceResolver implementation.
        /// </summary>
        public static bool TryResolve(Collider collider, out SurfaceHazardSample sample)
        {
            sample = default(SurfaceHazardSample);
            if (collider == null)
            {
                return false;
            }

            RoadHazardZone zone = collider.GetComponentInParent<RoadHazardZone>();
            if (zone == null)
            {
                return false;
            }

            RoadSurfaceType surfaceType;
            if (!Enum.TryParse(zone.SurfaceTypeId, true, out surfaceType))
            {
                surfaceType = RoadSurfaceType.DryAsphalt;
            }

            sample = new SurfaceHazardSample(
                surfaceType,
                zone.RollingDragMultiplier,
                zone.GripMultiplier,
                zone.Roughness,
                zone.PotholeImpulse,
                zone.WorldRailAxis,
                surfaceType == RoadSurfaceType.SteelRailTrack);
            return true;
        }
    }
}
