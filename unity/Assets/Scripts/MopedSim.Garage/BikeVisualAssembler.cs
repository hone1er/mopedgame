using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Builds the visual moped from BikeAssemblyGraph state: loads each mounted part's `.glb` via glTFast and snaps it onto
    /// the parent part's `SOCKET_*` empty (PROJECT_GUIDE.md section 5.2). Sockets are indexed per mounted slot, so identical
    /// socket names on different parts (e.g. both wheels' SOCKET_WHEEL_HUB / BOLT_AXLE_L) never collide.
    /// Contains no wrenching rules: it only mirrors Core's assembly graph. All work here is event-driven (no per-frame cost).
    /// </summary>
    public sealed class BikeVisualAssembler : MonoBehaviour
    {
        public const string SocketPrefix = "SOCKET_";
        public const string BoltPrefix = "BOLT_";

        private sealed class MountedPart
        {
            public string SlotId = string.Empty;
            public string PartId = string.Empty;
            public string ParentSlotId = string.Empty;
            public string AttachSocketName = string.Empty;
            public bool IsBoundToSocket;
            public bool UsedFallbackVisual;
            public GameObject Container;
            public GltfImport Import;
            public readonly Dictionary<string, Transform> Sockets = new Dictionary<string, Transform>(16, StringComparer.Ordinal);
        }

        [SerializeField] private Transform _assemblyRoot;
        [SerializeField] private bool _useFallbackBoxWhenGlbMissing = true;
        [SerializeField] private string _artRootOverride = string.Empty;

        private readonly Dictionary<string, MountedPart> _mounted = new Dictionary<string, MountedPart>(48, StringComparer.Ordinal);
        private readonly List<string> _mountOrder = new List<string>(48);
        private readonly Stack<Transform> _traversalStack = new Stack<Transform>(64);
        private SlotSocketBindings _bindings;
        private bool _syncRunning;
        private bool _syncDirty;
        private BikeSimulation _pendingSyncSimulation;

        public event Action<string> PartMounted;
        public event Action<string> PartUnmounted;

        public Transform AssemblyRoot
        {
            get { return _assemblyRoot != null ? _assemblyRoot : transform; }
            set { _assemblyRoot = value; }
        }

        public int MountedPartCount
        {
            get { return _mounted.Count; }
        }

        /// <summary>
        /// Slot IDs in mount order (parents before children).
        /// </summary>
        public IReadOnlyList<string> MountedSlotIds
        {
            get { return _mountOrder; }
        }

        public SlotSocketBindings Bindings
        {
            get
            {
                if (_bindings == null)
                {
                    _bindings = SlotSocketBindings.CreateDefault();
                }

                return _bindings;
            }
        }

        /// <summary>
        /// Absolute directory that catalog meshAssetPath values ("Models/Parts/...") resolve against.
        /// Defaults to Assets/Art in the Editor and StreamingAssets/Art in player builds.
        /// </summary>
        public string ArtRootOverride
        {
            get { return _artRootOverride; }
            set { _artRootOverride = value ?? string.Empty; }
        }

        public void SetSocketBindings(SlotSocketBindings bindings)
        {
            _bindings = bindings;
        }

        public bool IsSyncing
        {
            get { return _syncRunning; }
        }

        /// <summary>
        /// Mirrors the BikeSimulation assembly graph: unmounts emptied/swapped slots, then mounts installed parts parent-first.
        /// Calls made while a sync is awaiting glTF loads are coalesced into one follow-up pass (never overlapping passes).
        /// </summary>
        public async Task SyncWithSimulationAsync(BikeSimulation simulation)
        {
            if (simulation == null)
            {
                throw new ArgumentNullException(nameof(simulation));
            }

            _pendingSyncSimulation = simulation;
            if (_syncRunning)
            {
                _syncDirty = true;
                return;
            }

            _syncRunning = true;
            try
            {
                do
                {
                    _syncDirty = false;
                    await SyncPassAsync(_pendingSyncSimulation);
                }
                while (_syncDirty && this != null);
            }
            finally
            {
                _syncRunning = false;
            }
        }

        private async Task SyncPassAsync(BikeSimulation simulation)
        {
            IReadOnlyList<MountSlotNode> slots = simulation.Assembly.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                MountSlotNode node = slots[i];
                MountedPart existing;
                if (_mounted.TryGetValue(node.Spec.SlotId, out existing))
                {
                    PartInstance installed = node.InstalledPart;
                    if (installed == null || !string.Equals(installed.PartId, existing.PartId, StringComparison.Ordinal))
                    {
                        UnmountPart(node.Spec.SlotId);
                    }
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                MountSlotNode node = slots[i];
                PartInstance installed = node.InstalledPart;
                if (installed == null || _mounted.ContainsKey(node.Spec.SlotId))
                {
                    continue;
                }

                await MountPartAsync(node.Spec.SlotId, installed.Definition, node.Spec.ParentSlotId ?? string.Empty);
            }
        }

        /// <summary>
        /// Loads a catalog part's `.glb` (glTFast) and mounts it onto its bound parent socket.
        /// Falls back to a placeholder cube when the mesh is missing (data-only parts such as jets or gaskets).
        /// </summary>
        public async Task<GameObject> MountPartAsync(string slotId, PartDefinition part, string parentSlotId)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            MountedPart mounted = BeginMount(slotId, part.Id, parentSlotId);
            bool loaded = false;
            if (!string.IsNullOrEmpty(part.MeshAssetPath))
            {
                string fullPath = ResolveArtAssetPath(part.MeshAssetPath);
                if (File.Exists(fullPath))
                {
                    GltfImport gltfImport = new GltfImport();
                    bool loadOk = await gltfImport.Load(new Uri(fullPath).AbsoluteUri);
                    if (loadOk && mounted.Container != null)
                    {
                        loaded = await gltfImport.InstantiateMainSceneAsync(mounted.Container.transform);
                    }

                    if (loaded)
                    {
                        mounted.Import = gltfImport;
                    }
                    else
                    {
                        gltfImport.Dispose();
                    }
                }
            }

            if (!loaded && _useFallbackBoxWhenGlbMissing && mounted.Container != null)
            {
                CreateFallbackVisual(mounted.Container, slotId);
                mounted.UsedFallbackVisual = true;
            }

            return CompleteMount(mounted);
        }

        /// <summary>
        /// Synchronously mounts an already-instantiated visual (prefab instance, test hierarchy, or null for a fallback cube).
        /// Used by EditMode socket-binding tests and editor tooling without glTF file IO.
        /// </summary>
        public GameObject MountPartImmediate(string slotId, string partId, string parentSlotId, GameObject visualInstance)
        {
            MountedPart mounted = BeginMount(slotId, partId, parentSlotId);
            if (visualInstance != null)
            {
                visualInstance.transform.SetParent(mounted.Container.transform, false);
            }
            else if (_useFallbackBoxWhenGlbMissing)
            {
                CreateFallbackVisual(mounted.Container, slotId);
                mounted.UsedFallbackVisual = true;
            }

            return CompleteMount(mounted);
        }

        /// <summary>
        /// Removes a mounted part and, recursively, every part mounted beneath it. Disposes glTF resources.
        /// </summary>
        public bool UnmountPart(string slotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                return false;
            }

            MountedPart existing;
            if (!_mounted.TryGetValue(slotId, out existing))
            {
                return false;
            }

            List<string> children = null;
            for (int i = 0; i < _mountOrder.Count; i++)
            {
                MountedPart candidate;
                if (_mounted.TryGetValue(_mountOrder[i], out candidate)
                    && string.Equals(candidate.ParentSlotId, slotId, StringComparison.Ordinal))
                {
                    if (children == null)
                    {
                        children = new List<string>(4);
                    }

                    children.Add(candidate.SlotId);
                }
            }

            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    UnmountPart(children[i]);
                }
            }

            _mounted.Remove(slotId);
            _mountOrder.Remove(slotId);
            if (existing.Container != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing.Container);
                }
                else
                {
                    DestroyImmediate(existing.Container);
                }
            }

            if (existing.Import != null)
            {
                existing.Import.Dispose();
                existing.Import = null;
            }

            Action<string> handler = PartUnmounted;
            if (handler != null)
            {
                handler.Invoke(slotId);
            }

            return true;
        }

        public void ClearAllMountedParts()
        {
            while (_mountOrder.Count > 0)
            {
                string slotId = _mountOrder[_mountOrder.Count - 1];
                if (!UnmountPart(slotId))
                {
                    // Always drain: never spin on an id that is no longer tracked.
                    _mountOrder.RemoveAt(_mountOrder.Count - 1);
                }
            }
        }

        public bool TryGetMountedPart(string slotId, out GameObject mountedObject)
        {
            MountedPart mounted;
            if (!string.IsNullOrEmpty(slotId) && _mounted.TryGetValue(slotId, out mounted) && mounted.Container != null)
            {
                mountedObject = mounted.Container;
                return true;
            }

            mountedObject = null;
            return false;
        }

        public bool TryGetMountedPartId(string slotId, out string partId)
        {
            MountedPart mounted;
            if (!string.IsNullOrEmpty(slotId) && _mounted.TryGetValue(slotId, out mounted))
            {
                partId = mounted.PartId;
                return true;
            }

            partId = string.Empty;
            return false;
        }

        /// <summary>
        /// True when the slot's visual is parented to its bound SOCKET_* (false when it fell back to the parent root/assembly root).
        /// </summary>
        public bool IsBoundToSocket(string slotId)
        {
            MountedPart mounted;
            return !string.IsNullOrEmpty(slotId) && _mounted.TryGetValue(slotId, out mounted) && mounted.IsBoundToSocket;
        }

        public bool UsedFallbackVisual(string slotId)
        {
            MountedPart mounted;
            return !string.IsNullOrEmpty(slotId) && _mounted.TryGetValue(slotId, out mounted) && mounted.UsedFallbackVisual;
        }

        /// <summary>
        /// Looks up a SOCKET_* / BOLT_* transform belonging to the part mounted in the given slot.
        /// </summary>
        public bool TryGetSocket(string slotId, string socketOrBoltName, out Transform socketTransform)
        {
            MountedPart mounted;
            if (!string.IsNullOrEmpty(slotId)
                && !string.IsNullOrEmpty(socketOrBoltName)
                && _mounted.TryGetValue(slotId, out mounted)
                && mounted.Sockets.TryGetValue(socketOrBoltName, out socketTransform)
                && socketTransform != null)
            {
                return true;
            }

            socketTransform = null;
            return false;
        }

        /// <summary>
        /// First match of a socket/bolt name across mounted parts in mount order (use TryGetSocket when the owner slot is known).
        /// </summary>
        public bool TryFindSocketAnywhere(string socketOrBoltName, out Transform socketTransform)
        {
            if (!string.IsNullOrEmpty(socketOrBoltName))
            {
                for (int i = 0; i < _mountOrder.Count; i++)
                {
                    MountedPart mounted;
                    if (_mounted.TryGetValue(_mountOrder[i], out mounted)
                        && mounted.Sockets.TryGetValue(socketOrBoltName, out socketTransform)
                        && socketTransform != null)
                    {
                        return true;
                    }
                }
            }

            socketTransform = null;
            return false;
        }

        /// <summary>
        /// Appends every required socket/bolt name missing from the slot's mounted visual. Returns the missing count.
        /// </summary>
        public int CollectMissingSockets(string slotId, IReadOnlyList<string> requiredSockets, List<string> missingOut)
        {
            if (requiredSockets == null)
            {
                return 0;
            }

            MountedPart mounted;
            bool hasPart = !string.IsNullOrEmpty(slotId) && _mounted.TryGetValue(slotId, out mounted);
            int missing = 0;
            for (int i = 0; i < requiredSockets.Count; i++)
            {
                Transform found;
                if (!hasPart || !TryGetSocket(slotId, requiredSockets[i], out found))
                {
                    missing++;
                    if (missingOut != null)
                    {
                        missingOut.Add(requiredSockets[i]);
                    }
                }
            }

            return missing;
        }

        public string ResolveArtAssetPath(string meshAssetPath)
        {
            string normalized = (meshAssetPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            string root = !string.IsNullOrEmpty(_artRootOverride)
                ? _artRootOverride
                : (Application.isEditor
                    ? Path.Combine(Application.dataPath, "Art")
                    : Path.Combine(Application.streamingAssetsPath, "Art"));
            return Path.GetFullPath(Path.Combine(root, normalized));
        }

        private MountedPart BeginMount(string slotId, string partId, string parentSlotId)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                throw new ArgumentException("slotId must not be null or empty.", nameof(slotId));
            }

            UnmountPart(slotId);

            MountedPart mounted = new MountedPart();
            mounted.SlotId = slotId;
            mounted.PartId = partId ?? string.Empty;
            mounted.ParentSlotId = parentSlotId ?? string.Empty;

            string socketName;
            if (Bindings.TryGetAttachSocket(slotId, out socketName))
            {
                mounted.AttachSocketName = socketName;
            }

            Transform parentTransform = ResolveAttachTransform(mounted, out mounted.IsBoundToSocket);
            GameObject container = new GameObject(slotId + "__" + (string.IsNullOrEmpty(partId) ? "part" : partId));
            container.transform.SetParent(parentTransform, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;
            mounted.Container = container;
            return mounted;
        }

        private GameObject CompleteMount(MountedPart mounted)
        {
            // A concurrent mount of the same slot may have completed while this one awaited glTF IO: replace it cleanly.
            if (_mounted.ContainsKey(mounted.SlotId))
            {
                UnmountPart(mounted.SlotId);
            }

            if (mounted.Container != null)
            {
                IndexSockets(mounted);
            }

            _mounted[mounted.SlotId] = mounted;
            _mountOrder.Remove(mounted.SlotId);
            _mountOrder.Add(mounted.SlotId);

            Action<string> handler = PartMounted;
            if (handler != null)
            {
                handler.Invoke(mounted.SlotId);
            }

            return mounted.Container;
        }

        private Transform ResolveAttachTransform(MountedPart mounted, out bool boundToSocket)
        {
            boundToSocket = false;
            Transform socket;
            MountedPart parent;
            bool hasParent = mounted.ParentSlotId.Length > 0 && _mounted.TryGetValue(mounted.ParentSlotId, out parent);

            if (mounted.AttachSocketName.Length > 0)
            {
                if (hasParent && TryGetSocket(mounted.ParentSlotId, mounted.AttachSocketName, out socket))
                {
                    boundToSocket = true;
                    return socket;
                }

                // Some archetypes hang a part off a grandparent socket (e.g. intake on crankcase vs. cylinder).
                if (TryFindSocketAnywhere(mounted.AttachSocketName, out socket))
                {
                    boundToSocket = true;
                    return socket;
                }
            }

            GameObject parentObject;
            if (hasParent && TryGetMountedPart(mounted.ParentSlotId, out parentObject))
            {
                return parentObject.transform;
            }

            return AssemblyRoot;
        }

        private void IndexSockets(MountedPart mounted)
        {
            mounted.Sockets.Clear();
            _traversalStack.Clear();
            _traversalStack.Push(mounted.Container.transform);
            while (_traversalStack.Count > 0)
            {
                Transform current = _traversalStack.Pop();
                string nodeName = current.name;
                if ((nodeName.StartsWith(SocketPrefix, StringComparison.Ordinal) || nodeName.StartsWith(BoltPrefix, StringComparison.Ordinal))
                    && !mounted.Sockets.ContainsKey(nodeName))
                {
                    mounted.Sockets[nodeName] = current;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    _traversalStack.Push(current.GetChild(i));
                }
            }
        }

        private static void CreateFallbackVisual(GameObject container, string slotId)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "FallbackMesh_" + slotId;
            cube.transform.SetParent(container.transform, false);
            cube.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
            Collider col = cube.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(col);
                }
                else
                {
                    DestroyImmediate(col);
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _mountOrder.Count; i++)
            {
                MountedPart mounted;
                if (_mounted.TryGetValue(_mountOrder[i], out mounted) && mounted.Import != null)
                {
                    mounted.Import.Dispose();
                    mounted.Import = null;
                }
            }
        }
    }
}
