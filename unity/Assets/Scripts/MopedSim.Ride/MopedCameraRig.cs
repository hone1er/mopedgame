using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Moped ride camera with three modes (Chase, CloseChase, Handlebar) cycled by <see cref="ToggleMode"/>.
    /// Presentation only. Zero GC allocations per frame (spherecast uses a preallocated hit buffer).
    /// The camera keeps a level horizon (only a small fraction of bike lean/pitch is followed), pulls in against
    /// non-bike colliders, and snaps (instead of swooping) when the bike teleports.
    /// </summary>
    public sealed class MopedCameraRig : MonoBehaviour
    {
        public enum CameraMode
        {
            Chase = 0,
            CloseChase = 1,
            Handlebar = 2
        }

        public const int ModeCount = 3;

        [SerializeField] private Transform _targetBike;
        [SerializeField] private Camera _attachedCamera;
        [SerializeField] private CameraMode _mode = CameraMode.Chase;

        [Header("Chase")]
        [SerializeField] private Vector3 _chaseOffset = new Vector3(0f, 1.75f, -4.4f);
        [SerializeField] private Vector3 _chaseLook = new Vector3(0f, 0.95f, 2.2f);
        [Header("Close chase")]
        [SerializeField] private Vector3 _closeOffset = new Vector3(0.35f, 1.3f, -2.7f);
        [SerializeField] private Vector3 _closeLook = new Vector3(0f, 1.05f, 3.0f);
        [Header("Handlebar")]
        [SerializeField] private Vector3 _handlebarEye = new Vector3(0f, 1.32f, 0.10f);
        [SerializeField] private float _handlebarPitchDeg = 2f;
        [SerializeField] private float _tuckDropMeters = 0.18f;

        [Header("Damping & feel")]
        [SerializeField] private float _yawSmooth = 6f;
        [SerializeField] private float _rotationSmooth = 14f;
        [SerializeField] private float _lookAheadPerMps = 0.08f;
        [SerializeField] private float _leanFollow = 0.15f;
        [SerializeField] private float _teleportSnapMeters = 5f;

        [Header("Speed & Aero Tuck Feel")]
        [SerializeField] private float _baseFov = 62f;
        [SerializeField] private float _maxSpeedFovBonus = 10f;
        [SerializeField] private float _tuckFovBonus = 4f;
        [SerializeField] private float _fovSmooth = 7f;

        [Header("Collision")]
        [SerializeField] private float _collisionRadius = 0.25f;
        [SerializeField] private float _pivotHeight = 1.25f;
        [SerializeField] private LayerMask _collisionMask = Physics.DefaultRaycastLayers;

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private Rigidbody _bikeBody;
        private float _pullFraction = 1f;
        private float _currentSpeedMps;
        private float _tuckBlend;
        private Quaternion _smoothYaw = Quaternion.identity;
        private Vector3 _lastBikePos;
        private bool _hasLast;
        private bool _snapRequested = true;

        public CameraMode Mode
        {
            get { return _mode; }
            set { _mode = value; _snapRequested = true; }
        }

        /// <summary>Camera driven by this rig (falls back to the Camera on the same GameObject).</summary>
        public Camera AttachedCamera
        {
            get { return _attachedCamera != null ? _attachedCamera : GetComponent<Camera>(); }
        }

        public void SetTarget(Transform bikeTransform, Camera cam)
        {
            _targetBike = bikeTransform;
            _attachedCamera = cam;
            _bikeBody = bikeTransform != null ? bikeTransform.GetComponent<Rigidbody>() : null;
            _snapRequested = true;
        }

        public void UpdateRideTelemetry(float speedMps, float tuckBlend)
        {
            _currentSpeedMps = Mathf.Max(0f, speedMps);
            _tuckBlend = Mathf.Clamp01(tuckBlend);
        }

        /// <summary>Cycles Chase -> CloseChase -> Handlebar -> Chase.</summary>
        public void ToggleMode()
        {
            _mode = (CameraMode)(((int)_mode + 1) % ModeCount);
            _snapRequested = true;
        }

        /// <summary>Forces the next <see cref="Step"/> to place the camera exactly at its target pose.</summary>
        public void SnapToTarget()
        {
            _snapRequested = true;
        }

        private void Awake()
        {
            if (_attachedCamera == null)
            {
                _attachedCamera = GetComponent<Camera>();
            }

            if (_bikeBody == null && _targetBike != null)
            {
                _bikeBody = _targetBike.GetComponent<Rigidbody>();
            }
        }

        private void LateUpdate()
        {
            Step(Time.deltaTime);
        }

        /// <summary>Advances the camera by <paramref name="dt"/> seconds (public so tests can drive it deterministically).</summary>
        public void Step(float dt)
        {
            if (_targetBike == null)
            {
                return;
            }

            Vector3 bikePos = _targetBike.position;
            bool snap = _snapRequested || !_hasLast || (bikePos - _lastBikePos).sqrMagnitude > _teleportSnapMeters * _teleportSnapMeters;
            _snapRequested = false;
            _hasLast = true;
            _lastBikePos = bikePos;

            // Heading from bike forward flattened; when the bike lies on its side keep the last heading.
            Vector3 flatForward = _targetBike.forward;
            flatForward.y = 0f;
            Quaternion targetYaw = _smoothYaw;
            if (flatForward.sqrMagnitude > 0.05f)
            {
                targetYaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            }

            _smoothYaw = snap ? targetYaw : Quaternion.Slerp(_smoothYaw, targetYaw, 1f - Mathf.Exp(-_yawSmooth * dt));

            // Subtle lean/pitch follow so the horizon stays level.
            Quaternion lean = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, _targetBike.up), _leanFollow);

            Vector3 desiredPos;
            Quaternion desiredRot;
            float rotRate = _rotationSmooth;
            if (_mode == CameraMode.Handlebar)
            {
                Vector3 eye = _handlebarEye;
                eye.y -= _tuckBlend * _tuckDropMeters;
                // Level-ish eye: follow yaw rigidly, lean only fractionally.
                desiredPos = bikePos + _smoothYaw * eye;
                desiredRot = lean * _smoothYaw * Quaternion.Euler(_handlebarPitchDeg + _tuckBlend * 3f, 0f, 0f);
                rotRate = _rotationSmooth * 1.5f;
                desiredPos = PullIn(bikePos + Vector3.up * _pivotHeight, desiredPos, snap, dt);
            }
            else
            {
                bool close = _mode == CameraMode.CloseChase;
                Vector3 offset = close ? _closeOffset : _chaseOffset;
                Vector3 look = close ? _closeLook : _chaseLook;
                offset.y -= _tuckBlend * 0.12f;
                look.z += _currentSpeedMps * _lookAheadPerMps;
                desiredPos = bikePos + _smoothYaw * offset;
                Vector3 lookTarget = bikePos + _smoothYaw * look;
                desiredRot = lean * Quaternion.LookRotation(lookTarget - desiredPos, Vector3.up);
                desiredPos = PullIn(bikePos + Vector3.up * _pivotHeight, desiredPos, snap, dt);
            }

            transform.position = desiredPos;
            transform.rotation = snap ? desiredRot : Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotRate * dt));

            if (_attachedCamera != null)
            {
                float speedRatio = Mathf.Clamp01(_currentSpeedMps / 24f);
                float targetFov = _baseFov + (speedRatio * _maxSpeedFovBonus) + (_tuckBlend * _tuckFovBonus);
                _attachedCamera.fieldOfView = snap ? targetFov : Mathf.Lerp(_attachedCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-_fovSmooth * dt));
            }
        }

        private Vector3 PullIn(Vector3 pivot, Vector3 desired, bool snap, float dt)
        {
            Vector3 delta = desired - pivot;
            float dist = delta.magnitude;
            if (dist < 0.01f)
            {
                _pullFraction = 1f;
                return desired;
            }

            Vector3 dir = delta / dist;
            int count = Physics.SphereCastNonAlloc(pivot, _collisionRadius, dir, _hits, dist, _collisionMask, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                Collider c = h.collider;
                if (c == null || h.distance <= 0f || h.distance >= best)
                {
                    continue;
                }

                if (c.transform.IsChildOf(_targetBike) || (_bikeBody != null && c.attachedRigidbody == _bikeBody))
                {
                    continue;
                }

                best = h.distance;
            }

            // Pull in instantly, ease back out so passing poles do not make the camera pop.
            float fraction = best / dist;
            if (snap || fraction < _pullFraction)
            {
                _pullFraction = fraction;
            }
            else
            {
                _pullFraction = Mathf.Lerp(_pullFraction, fraction, 1f - Mathf.Exp(-4f * dt));
            }

            return pivot + dir * (dist * _pullFraction);
        }
    }
}
