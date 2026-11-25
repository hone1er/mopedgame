using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Dynamically loads `.glb` sub-assemblies via glTFast and snaps them onto parent `SOCKET_*`
    /// transforms matching BikeAssemblyGraph slot attachments.
    /// </summary>
    public sealed class BikeVisualAssembler : MonoBehaviour
    {
        [SerializeField] private Transform _assemblyRoot;
        [SerializeField] private string _artRootRelativeToAssets = "Art";
        [SerializeField] private bool _useFallbackBoxWhenGlbMissing = true;

        private readonly Dictionary<string, GameObject> _mountedParts = new Dictionary<string, GameObject>(32);
        private readonly Dictionary<string, Transform> _socketIndex = new Dictionary<string, Transform>(64);
        private readonly Stack<Transform> _traversalStack = new Stack<Transform>(64);

        public Transform AssemblyRoot
        {
            get { return _assemblyRoot != null ? _assemblyRoot : transform; }
            set { _assemblyRoot = value; }
        }

        public int MountedPartCount
        {
            get { return _mountedParts.Count; }
        }

        /// <summary>
        /// Mounts a part `.glb` onto the specified parent socket (e.g. SOCKET_CYLINDER_BASE)
        /// or directly under AssemblyRoot when parentSocketName is null/empty.
        /// </summary>
        public async Task<GameObject> MountPartAsync(
            string slotId,
            string partId,
            string meshAssetPath,
            string parentSocketName)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                throw new ArgumentException("slotId must not be null or empty.", nameof(slotId));
            }

            UnmountPart(slotId);

            Transform parentTransform = ResolveParentTransform(parentSocketName);
            GameObject partContainer = new GameObject(slotId + "_" + (string.IsNullOrEmpty(partId) ? "part" : partId));
            partContainer.transform.SetParent(parentTransform, false);
            partContainer.transform.localPosition = Vector3.zero;
            partContainer.transform.localRotation = Quaternion.identity;
            partContainer.transform.localScale = Vector3.one;

            bool loaded = false;
            if (!string.IsNullOrEmpty(meshAssetPath))
            {
                string fullPath = ResolveArtAssetPath(meshAssetPath);
                if (File.Exists(fullPath))
                {
                    GltfImport gltfImport = new GltfImport();
                    string fileUri = new Uri(fullPath).AbsoluteUri;
                    bool loadOk = await gltfImport.Load(fileUri);
                    if (loadOk)
                    {
                        loaded = await gltfImport.InstantiateMainSceneAsync(partContainer.transform);
                    }
                }
            }

            if (!loaded && _useFallbackBoxWhenGlbMissing)
            {
                CreateFallbackVisual(partContainer, slotId);
            }

            _mountedParts[slotId] = partContainer;
            RebuildSocketIndex();
            return partContainer;
        }

        /// <summary>
        /// Synchronously removes a mounted part GameObject from the hierarchy and clears its sockets.
        /// </summary>
        public bool UnmountPart(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return false;
            }

            GameObject existing;
            if (!_mountedParts.TryGetValue(slotId, out existing))
            {
                return false;
            }

            _mountedParts.Remove(slotId);
            if (existing != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing);
                }
                else
                {
                    DestroyImmediate(existing);
                }
            }

            RebuildSocketIndex();
            return true;
        }

        public bool TryGetSocket(string socketOrBoltName, out Transform socketTransform)
        {
            if (string.IsNullOrEmpty(socketOrBoltName))
            {
                socketTransform = null;
                return false;
            }

            if (_socketIndex.TryGetValue(socketOrBoltName, out socketTransform) && socketTransform != null)
            {
                return true;
            }

            RebuildSocketIndex();
            return _socketIndex.TryGetValue(socketOrBoltName, out socketTransform) && socketTransform != null;
        }

        public bool TryGetMountedPart(string slotId, out GameObject mountedObject)
        {
            return _mountedParts.TryGetValue(slotId, out mountedObject) && mountedObject != null;
        }

        public void ClearAllMountedParts()
        {
            List<string> keys = new List<string>(_mountedParts.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                UnmountPart(keys[i]);
            }

            _socketIndex.Clear();
        }

        public string ResolveArtAssetPath(string meshAssetPath)
        {
            string normalized = meshAssetPath.Replace('\\', '/').TrimStart('/');
            return Path.GetFullPath(Path.Combine(Application.dataPath, _artRootRelativeToAssets, normalized));
        }

        public void RebuildSocketIndex()
        {
            _socketIndex.Clear();
            _traversalStack.Clear();

            Transform root = AssemblyRoot;
            if (root == null)
            {
                return;
            }

            _traversalStack.Push(root);
            while (_traversalStack.Count > 0)
            {
                Transform current = _traversalStack.Pop();
                string nodeName = current.name;
                if (nodeName.StartsWith("SOCKET_", StringComparison.Ordinal) ||
                    nodeName.StartsWith("BOLT_", StringComparison.Ordinal))
                {
                    _socketIndex[nodeName] = current;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    _traversalStack.Push(current.GetChild(i));
                }
            }
        }

        private Transform ResolveParentTransform(string parentSocketName)
        {
            if (!string.IsNullOrEmpty(parentSocketName))
            {
                Transform socketTransform;
                if (TryGetSocket(parentSocketName, out socketTransform))
                {
                    return socketTransform;
                }
            }

            return AssemblyRoot;
        }

        private static void CreateFallbackVisual(GameObject container, string slotId)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "FallbackMesh_" + slotId;
            cube.transform.SetParent(container.transform, false);
            cube.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
        }
    }
}
