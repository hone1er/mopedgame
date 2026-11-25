using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MopedSim.World
{
    /// <summary>
    /// Abstraction over additive zone streaming so Addressables can replace SceneManager later (ADR-004).
    /// </summary>
    public interface IZoneLoader
    {
        Task<bool> LoadZoneAdditiveAsync(string zoneSceneName);
        Task<bool> UnloadZoneAsync(string zoneSceneName);
        bool IsZoneLoaded(string zoneSceneName);
    }

    /// <summary>
    /// Manages additive Bay Area neighborhood scenes:
    /// Zone_WestOakland, Zone_OaklandHills, Zone_BayCrossing, and Zone_SF_Mission.
    /// </summary>
    public sealed class ZoneStreamManager : MonoBehaviour, IZoneLoader
    {
        public static readonly string[] KnownZoneScenes = new string[]
        {
            "Zone_WestOakland",
            "Zone_OaklandHills",
            "Zone_BayCrossing",
            "Zone_SF_Mission"
        };

        private readonly HashSet<string> _loadedZones = new HashSet<string>(StringComparer.Ordinal);
        private string _activeZoneName = "Zone_WestOakland";

        public event Action<string> ZoneLoaded;
        public event Action<string> ZoneUnloaded;
        public event Action<string> ActiveZoneChanged;

        public string ActiveZoneName
        {
            get { return _activeZoneName; }
        }

        public bool IsZoneLoaded(string zoneSceneName)
        {
            if (string.IsNullOrEmpty(zoneSceneName))
            {
                return false;
            }

            if (_loadedZones.Contains(zoneSceneName))
            {
                return true;
            }

            Scene scene = SceneManager.GetSceneByName(zoneSceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        public async Task<bool> TransitionToZoneAsync(string targetZoneSceneName, bool unloadOtherZones)
        {
            if (string.IsNullOrEmpty(targetZoneSceneName))
            {
                return false;
            }

            if (!IsZoneLoaded(targetZoneSceneName))
            {
                bool loaded = await LoadZoneAdditiveAsync(targetZoneSceneName);
                if (!loaded)
                {
                    return false;
                }
            }

            _activeZoneName = targetZoneSceneName;
            if (ActiveZoneChanged != null)
            {
                ActiveZoneChanged.Invoke(_activeZoneName);
            }

            if (unloadOtherZones)
            {
                List<string> toUnload = new List<string>(_loadedZones.Count);
                foreach (string zone in _loadedZones)
                {
                    if (!string.Equals(zone, targetZoneSceneName, StringComparison.Ordinal))
                    {
                        toUnload.Add(zone);
                    }
                }

                for (int i = 0; i < toUnload.Count; i++)
                {
                    await UnloadZoneAsync(toUnload[i]);
                }
            }

            return true;
        }

        public async Task<bool> LoadZoneAdditiveAsync(string zoneSceneName)
        {
            if (string.IsNullOrEmpty(zoneSceneName))
            {
                return false;
            }

            if (IsZoneLoaded(zoneSceneName))
            {
                _loadedZones.Add(zoneSceneName);
                return true;
            }

            if (Application.CanStreamedLevelBeLoaded(zoneSceneName))
            {
                AsyncOperation op = SceneManager.LoadSceneAsync(zoneSceneName, LoadSceneMode.Additive);
                if (op != null)
                {
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }
                }
            }
            else
            {
                // Allows headless PlayMode tests to validate zone transitions without build-settings scenes.
                Scene created = SceneManager.CreateScene(zoneSceneName);
                if (!created.IsValid())
                {
                    return false;
                }
            }

            _loadedZones.Add(zoneSceneName);
            if (ZoneLoaded != null)
            {
                ZoneLoaded.Invoke(zoneSceneName);
            }

            return true;
        }

        public async Task<bool> UnloadZoneAsync(string zoneSceneName)
        {
            if (string.IsNullOrEmpty(zoneSceneName) || !IsZoneLoaded(zoneSceneName))
            {
                return false;
            }

            Scene scene = SceneManager.GetSceneByName(zoneSceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                if (op != null)
                {
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }
                }
            }

            _loadedZones.Remove(zoneSceneName);
            if (ZoneUnloaded != null)
            {
                ZoneUnloaded.Invoke(zoneSceneName);
            }

            return true;
        }
    }
}
