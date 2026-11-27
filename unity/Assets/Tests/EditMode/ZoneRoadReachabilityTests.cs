using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Editor;
using MopedSim.World;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Every catalog gig checkpoint and every zone portal in the generated Zone_*.unity scenes must sit on drivable road
    /// geometry (a downward raycast from 3 m above hits a non-trigger collider within 1.5 m of the checkpoint height).
    /// </summary>
    public sealed class ZoneRoadReachabilityTests
    {
        private const float RayLift = 3f;
        private const float MaxHeightError = 1.5f;

        private readonly List<Scene> _opened = new List<Scene>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                if (_opened[i].IsValid() && _opened[i].isLoaded && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(_opened[i], true);
                }
            }

            _opened.Clear();
        }

        private Scene Open(string sceneName)
        {
            Scene scene = EditorSceneManager.OpenScene(SceneScaffoldBuilder.GetScenePath(sceneName), OpenSceneMode.Additive);
            _opened.Add(scene);
            Physics.SyncTransforms();
            return scene;
        }

        private static bool HitsRoad(Vector3 point, out float hitY)
        {
            Vector3 origin = point + Vector3.up * RayLift;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, RayLift + MaxHeightError + 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                hitY = hit.point.y;
                return true;
            }

            hitY = float.NaN;
            return false;
        }

        [Test]
        public void EveryCatalogCheckpoint_SitsOnRoadWithinTolerance()
        {
            CatalogDatabase db = RepoCatalog.Load();
            int exercised = 0;
            for (int z = 0; z < ZoneStreamManager.KnownZoneScenes.Length; z++)
            {
                string zone = ZoneStreamManager.KnownZoneScenes[z];
                Scene opened = Open(zone);
                foreach (JobDefinition job in db.Jobs.Values)
                {
                    for (int c = 0; c < job.Checkpoints.Count; c++)
                    {
                        JobCheckpointSpec cp = job.Checkpoints[c];
                        if (cp.ZoneId != zone)
                        {
                            continue;
                        }

                        exercised++;
                        float hitY;
                        Assert.IsTrue(HitsRoad(new Vector3(cp.X, cp.Y, cp.Z), out hitY),
                            job.Id + "/" + cp.Id + " at (" + cp.X + "," + cp.Y + "," + cp.Z + ") has no road/ground below it in " + zone);
                        Assert.LessOrEqual(Mathf.Abs(hitY - cp.Y), MaxHeightError,
                            job.Id + "/" + cp.Id + " expects y=" + cp.Y + " but the road surface is at y=" + hitY + " in " + zone);
                    }
                }

                CloseOpened(opened);
            }

            Assert.Greater(exercised, 0, "No checkpoints were exercised.");
        }

        [Test]
        public void EveryPortal_AndArrivalPose_IsOnRoad()
        {
            for (int z = 0; z < ZoneStreamManager.KnownZoneScenes.Length; z++)
            {
                string zone = ZoneStreamManager.KnownZoneScenes[z];
                Scene scene = Open(zone);
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    ZoneTransitionPortal[] portals = roots[r].GetComponentsInChildren<ZoneTransitionPortal>(true);
                    for (int p = 0; p < portals.Length; p++)
                    {
                        Vector3 pos = portals[p].transform.position;
                        float hitY;
                        Assert.IsTrue(HitsRoad(pos, out hitY), zone + " portal to " + portals[p].TargetZoneScene + " at " + pos + " is not over a road.");
                        Assert.IsTrue(portals[p].HasArrivalPose, zone + " portal to " + portals[p].TargetZoneScene + " has no arrival pose.");
                    }
                }

                CloseOpened(scene);
            }
        }

        [Test]
        public void EveryPortalArrival_LandsOnTargetZoneRoad_OutsideAllPortalTriggers()
        {
            // Arrival poses point into the TARGET zone, so every zone must be open at once.
            List<ZoneTransitionPortal> portals = new List<ZoneTransitionPortal>();
            for (int z = 0; z < ZoneStreamManager.KnownZoneScenes.Length; z++)
            {
                Scene scene = Open(ZoneStreamManager.KnownZoneScenes[z]);
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    portals.AddRange(roots[r].GetComponentsInChildren<ZoneTransitionPortal>(true));
                }
            }

            Physics.SyncTransforms();
            Assert.Greater(portals.Count, 0);
            for (int p = 0; p < portals.Count; p++)
            {
                ZoneTransitionPortal portal = portals[p];
                Assert.IsTrue(portal.HasArrivalPose, portal.name + " has no arrival pose.");
                Vector3 arrival = portal.ArrivalWorldPosition;
                RaycastHit hit;
                Assert.IsTrue(
                    Physics.Raycast(arrival + Vector3.up * 6f, Vector3.down, out hit, 40f, ~0, QueryTriggerInteraction.Ignore),
                    portal.name + " arrival " + arrival + " has no ground below it.");
                Assert.Less(Mathf.Abs(hit.point.y - (arrival.y - 2f)), 6f, portal.name + " arrival " + arrival + " is far from the road surface (hit y " + hit.point.y + ").");

                for (int q = 0; q < portals.Count; q++)
                {
                    BoxCollider trigger = portals[q].GetComponent<BoxCollider>();
                    if (trigger == null)
                    {
                        continue;
                    }

                    Vector3 local = trigger.transform.InverseTransformPoint(new Vector3(arrival.x, hit.point.y + 0.6f, arrival.z)) - trigger.center;
                    bool inside = Mathf.Abs(local.x) <= trigger.size.x * 0.5f && Mathf.Abs(local.y) <= trigger.size.y * 0.5f && Mathf.Abs(local.z) <= trigger.size.z * 0.5f;
                    Assert.IsFalse(inside, portal.name + " arrival " + arrival + " sits inside " + portals[q].name + "'s trigger (would bounce straight back).");
                }
            }
        }

        private void CloseOpened(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            _opened.Remove(scene);
        }
    }
}
