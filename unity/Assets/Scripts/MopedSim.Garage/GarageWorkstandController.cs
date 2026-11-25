using System;
using System.Collections.Generic;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MopedSim.Garage
{
    /// <summary>
    /// Work stand station: orbit/zoom inspection camera, explode and x-ray toggles, and interactable highlighting.
    /// Which slots/fasteners are wrenchable comes exclusively from BikeSimulation.GetInteractableNodes() (Core DAG rules);
    /// the node list is refreshed only when the assembly changes, never per frame. Update/LateUpdate are allocation-free.
    /// </summary>
    public sealed class GarageWorkstandController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private BikeVisualAssembler _assembler;
        [SerializeField] private Camera _inspectionCamera;
        [SerializeField] private Transform _orbitPivot;

        [Header("Orbit / Zoom")]
        [SerializeField] private float _orbitDegreesPerPixel = 0.25f;
        [SerializeField] private float _zoomMetersPerNotch = 0.0015f;
        [SerializeField] private float _minDistance = 0.6f;
        [SerializeField] private float _maxDistance = 3.5f;
        [SerializeField] private float _minPitch = -10f;
        [SerializeField] private float _maxPitch = 75f;
        [SerializeField] private bool _readMouseInput = true;

        [Header("Explode / X-Ray")]
        [SerializeField] private float _explodeDistance = 0.18f;
        [SerializeField] private string[] _xrayHiddenSlots = new string[]
        {
            SlotIds.Exhaust,
            SlotIds.FuelTank,
            SlotIds.Seat,
            SlotIds.CargoRack,
            SlotIds.AirFilter,
            SlotIds.FrontFender
        };

        [Header("Highlight")]
        [SerializeField] private Color _removableColor = new Color(0.2f, 0.9f, 0.35f, 1f);
        [SerializeField] private Color _blockedColor = new Color(0.9f, 0.25f, 0.2f, 1f);
        [SerializeField] private Color _problemColor = new Color(1f, 0.65f, 0.1f, 1f);
        [SerializeField] private float _pickRadiusMeters = 0.06f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly List<InteractableNode> _nodes = new List<InteractableNode>(96);
        private readonly List<Renderer> _rendererScratch = new List<Renderer>(32);
        private readonly Dictionary<string, Vector3> _explodeOrigins = new Dictionary<string, Vector3>(48, StringComparer.Ordinal);
        private MaterialPropertyBlock _block;
        private GarageBikeContext _context;
        private float _yawDeg = 35f;
        private float _pitchDeg = 18f;
        private float _distance = 1.8f;
        private bool _exploded;
        private bool _xray;
        private string _selectedNodeId = string.Empty;

        /// <summary>
        /// Raised when the player picks a slot or fastener on the stand.
        /// </summary>
        public event Action<InteractableNode> NodeSelected;

        public GarageBikeContext Context
        {
            get { return _context; }
        }

        public BikeVisualAssembler Assembler
        {
            get { return _assembler; }
            set { _assembler = value; }
        }

        public IReadOnlyList<InteractableNode> Interactables
        {
            get { return _nodes; }
        }

        public bool IsExploded
        {
            get { return _exploded; }
        }

        public bool IsXRay
        {
            get { return _xray; }
        }

        public string SelectedNodeId
        {
            get { return _selectedNodeId; }
        }

        public float OrbitYawDeg
        {
            get { return _yawDeg; }
        }

        public float OrbitPitchDeg
        {
            get { return _pitchDeg; }
        }

        public float OrbitDistance
        {
            get { return _distance; }
        }

        public bool ReadMouseInput
        {
            get { return _readMouseInput; }
            set { _readMouseInput = value; }
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        public void Bind(GarageBikeContext context)
        {
            if (_context != null)
            {
                _context.AssemblyChanged -= OnAssemblyChanged;
            }

            _context = context;
            if (_context != null)
            {
                _context.AssemblyChanged += OnAssemblyChanged;
            }

            RefreshInteractables();
        }

        private void OnDestroy()
        {
            if (_context != null)
            {
                _context.AssemblyChanged -= OnAssemblyChanged;
            }
        }

        private void OnAssemblyChanged()
        {
            RefreshInteractables();
        }

        /// <summary>
        /// Re-queries Core for wrenchable nodes (allocates inside Core; call after wrench actions only).
        /// </summary>
        public void RefreshInteractables()
        {
            _nodes.Clear();
            if (_context == null)
            {
                return;
            }

            IReadOnlyList<InteractableNode> nodes = _context.Bike.GetInteractableNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                _nodes.Add(nodes[i]);
            }

            ApplyHighlights();
        }

        public bool TryGetNode(string nodeId, out InteractableNode node)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (string.Equals(_nodes[i].NodeId, nodeId, StringComparison.Ordinal))
                {
                    node = _nodes[i];
                    return true;
                }
            }

            node = default(InteractableNode);
            return false;
        }

        public bool SelectNode(string nodeId)
        {
            InteractableNode node;
            if (!TryGetNode(nodeId, out node))
            {
                return false;
            }

            _selectedNodeId = node.NodeId;
            Action<InteractableNode> handler = NodeSelected;
            if (handler != null)
            {
                handler.Invoke(node);
            }

            return true;
        }

        /// <summary>
        /// Picks the interactable whose visual anchor lies closest to the ray (within the pick radius). Zero allocations.
        /// </summary>
        public bool TryPickNode(Ray ray, out InteractableNode picked)
        {
            float bestDistance = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Transform anchor;
                if (!TryResolveAnchor(_nodes[i], out anchor))
                {
                    continue;
                }

                Vector3 toAnchor = anchor.position - ray.origin;
                float along = Vector3.Dot(toAnchor, ray.direction);
                if (along <= 0f)
                {
                    continue;
                }

                float perpendicular = (toAnchor - ray.direction * along).magnitude;
                float radius = _nodes[i].IsFastener ? _pickRadiusMeters : _pickRadiusMeters * 2.5f;
                if (perpendicular <= radius && along < bestDistance)
                {
                    bestDistance = along;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                picked = default(InteractableNode);
                return false;
            }

            picked = _nodes[bestIndex];
            return true;
        }

        /// <summary>
        /// Removes the part mounted in a slot via Core DAG preconditions (blocked results list blocking nodes).
        /// </summary>
        public WrenchActionResult TryRemovePart(string slotId)
        {
            if (_context == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, slotId, "No bike on the work stand.");
            }

            return _context.Publish(_context.Bike.TryRemovePart(slotId));
        }

        /// <summary>
        /// Installs a catalog part into a slot via Core compatibility/orientation checks.
        /// </summary>
        public WrenchActionResult TryInstallPart(string slotId, PartDefinition part)
        {
            if (_context == null || part == null)
            {
                return WrenchActionResult.Fail(WrenchActionStatus.PartNotFound, slotId, "No bike on the work stand or no part selected.");
            }

            return _context.Publish(_context.Bike.TryInstallPart(slotId, new PartInstance(part)));
        }

        public void Orbit(float deltaYawDeg, float deltaPitchDeg)
        {
            _yawDeg = Mathf.Repeat(_yawDeg + deltaYawDeg, 360f);
            _pitchDeg = Mathf.Clamp(_pitchDeg + deltaPitchDeg, _minPitch, _maxPitch);
        }

        public void Zoom(float deltaMeters)
        {
            _distance = Mathf.Clamp(_distance + deltaMeters, _minDistance, _maxDistance);
        }

        public void SetExploded(bool exploded)
        {
            if (_assembler == null || exploded == _exploded)
            {
                _exploded = exploded;
                return;
            }

            _exploded = exploded;
            Vector3 rootPos = _assembler.AssemblyRoot.position;
            IReadOnlyList<string> slots = _assembler.MountedSlotIds;
            for (int i = 0; i < slots.Count; i++)
            {
                GameObject part;
                if (!_assembler.TryGetMountedPart(slots[i], out part))
                {
                    continue;
                }

                Transform t = part.transform;
                if (exploded)
                {
                    _explodeOrigins[slots[i]] = t.localPosition;
                    Vector3 dir = t.position - rootPos;
                    dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
                    Vector3 parentSpaceDir = t.parent != null ? t.parent.InverseTransformDirection(dir) : dir;
                    t.localPosition = t.localPosition + parentSpaceDir * _explodeDistance;
                }
                else
                {
                    Vector3 origin;
                    if (_explodeOrigins.TryGetValue(slots[i], out origin))
                    {
                        t.localPosition = origin;
                    }
                }
            }

            if (!exploded)
            {
                _explodeOrigins.Clear();
            }
        }

        public void ToggleExploded()
        {
            SetExploded(!_exploded);
        }

        public void SetXRay(bool xray)
        {
            _xray = xray;
            if (_assembler == null)
            {
                return;
            }

            for (int i = 0; i < _xrayHiddenSlots.Length; i++)
            {
                GameObject part;
                if (!_assembler.TryGetMountedPart(_xrayHiddenSlots[i], out part))
                {
                    continue;
                }

                _rendererScratch.Clear();
                part.GetComponentsInChildren(true, _rendererScratch);
                for (int r = 0; r < _rendererScratch.Count; r++)
                {
                    _rendererScratch[r].enabled = !xray;
                }
            }
        }

        public void ToggleXRay()
        {
            SetXRay(!_xray);
        }

        private void Update()
        {
            if (!_readMouseInput)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                Orbit(delta.x * _orbitDegreesPerPixel, -delta.y * _orbitDegreesPerPixel);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Zoom(-scroll * _zoomMetersPerNotch);
            }

            if (mouse.leftButton.wasPressedThisFrame && _inspectionCamera != null)
            {
                InteractableNode picked;
                if (TryPickNode(_inspectionCamera.ScreenPointToRay(mouse.position.ReadValue()), out picked))
                {
                    SelectNode(picked.NodeId);
                }
            }
        }

        private void LateUpdate()
        {
            if (_inspectionCamera == null)
            {
                return;
            }

            Vector3 pivot = _orbitPivot != null
                ? _orbitPivot.position
                : (_assembler != null ? _assembler.AssemblyRoot.position : transform.position);
            Quaternion rot = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
            Transform camTf = _inspectionCamera.transform;
            camTf.position = pivot + rot * new Vector3(0f, 0f, -_distance);
            camTf.rotation = Quaternion.LookRotation(pivot - camTf.position, Vector3.up);
        }

        private bool TryResolveAnchor(InteractableNode node, out Transform anchor)
        {
            anchor = null;
            if (_assembler == null)
            {
                return false;
            }

            if (node.IsFastener)
            {
                if (_assembler.TryGetSocket(node.ParentSlotId, node.NodeId, out anchor))
                {
                    return true;
                }

                return _assembler.TryFindSocketAnywhere(node.NodeId, out anchor);
            }

            GameObject part;
            if (_assembler.TryGetMountedPart(node.NodeId, out part))
            {
                anchor = part.transform;
                return true;
            }

            return false;
        }

        private void ApplyHighlights()
        {
            if (_assembler == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                InteractableNode node = _nodes[i];
                if (node.IsFastener)
                {
                    continue;
                }

                GameObject part;
                if (!_assembler.TryGetMountedPart(node.NodeId, out part))
                {
                    continue;
                }

                Color tint = node.CanRemove ? _removableColor : _blockedColor;
                tint *= 0.35f;
                _rendererScratch.Clear();
                part.GetComponentsInChildren(true, _rendererScratch);
                for (int r = 0; r < _rendererScratch.Count; r++)
                {
                    Renderer rend = _rendererScratch[r];
                    rend.GetPropertyBlock(_block);
                    _block.SetColor(EmissionColorId, tint);
                    rend.SetPropertyBlock(_block);
                }
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                InteractableNode node = _nodes[i];
                if (!node.IsFastener || !(node.IsStripped || node.IsSeized || node.IsUnderTorqued))
                {
                    continue;
                }

                GameObject owner;
                if (!_assembler.TryGetMountedPart(node.ParentSlotId, out owner))
                {
                    continue;
                }

                _rendererScratch.Clear();
                owner.GetComponentsInChildren(true, _rendererScratch);
                for (int r = 0; r < _rendererScratch.Count; r++)
                {
                    Renderer rend = _rendererScratch[r];
                    rend.GetPropertyBlock(_block);
                    _block.SetColor(EmissionColorId, _problemColor * 0.45f);
                    rend.SetPropertyBlock(_block);
                }
            }
        }
    }
}
