using UnityEngine;

namespace MopedSim.Ride
{
    /// <summary>
    /// Smooth chase / first-person moped camera rig with aero-tuck drop and speed FOV kick.
    /// Zero GC allocations in LateUpdate.
    /// </summary>
    public sealed class MopedCameraRig : MonoBehaviour
    {
        public enum CameraMode
        {
            Chase = 0,
            FirstPerson = 1
        }

        [SerializeField] private Transform _targetBike;
        [SerializeField] private Camera _attachedCamera;
        [SerializeField] private CameraMode _mode = CameraMode.Chase;

        [Header("Chase Offsets")]
        [SerializeField] private Vector3 _chaseOffset = new Vector3(0f, 1.45f, -3.10f);
        [SerializeField] private Vector3 _lookAheadOffset = new Vector3(0f, 0.72f, 1.80f);
        [SerializeField] private float _positionSmooth = 10f;
        [SerializeField] private float _rotationSmooth = 12f;

        [Header("First Person Offsets")]
        [SerializeField] private Vector3 _fpHeadOffset = new Vector3(0f, 1.24f, 0.14f);
        [SerializeField] private float _fpTuckDropMeters = 0.24f;

        [Header("Speed & Aero Tuck Feel")]
        [SerializeField] private float _baseFov = 64f;
        [SerializeField] private float _maxSpeedFovBonus = 11f;
        [SerializeField] private float _tuckFovBonus = 5f;
        [SerializeField] private float _fovSmooth = 7f;

        private float _currentSpeedMps;
        private float _tuckBlend;

        public CameraMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        public void SetTarget(Transform bikeTransform, Camera cam)
        {
            _targetBike = bikeTransform;
            _attachedCamera = cam;
        }

        public void UpdateRideTelemetry(float speedMps, float tuckBlend)
        {
            _currentSpeedMps = Mathf.Max(0f, speedMps);
            _tuckBlend = Mathf.Clamp01(tuckBlend);
        }

        public void ToggleMode()
        {
            _mode = _mode == CameraMode.Chase ? CameraMode.FirstPerson : CameraMode.Chase;
        }

        private void Awake()
        {
            if (_attachedCamera == null)
            {
                _attachedCamera = GetComponent<Camera>();
            }
        }

        private void LateUpdate()
        {
            if (_targetBike == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (_mode == CameraMode.FirstPerson)
            {
                Vector3 localPos = _fpHeadOffset;
                localPos.y -= _tuckBlend * _fpTuckDropMeters;
                transform.position = _targetBike.TransformPoint(localPos);
                Quaternion desiredRot = _targetBike.rotation * Quaternion.Euler(4f + _tuckBlend * 4f, 0f, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-_rotationSmooth * 1.5f * dt));
            }
            else
            {
                Vector3 flatForward = _targetBike.forward;
                flatForward.y = 0f;
                if (flatForward.sqrMagnitude < 0.0001f)
                {
                    flatForward = Vector3.forward;
                }
                flatForward.Normalize();

                Quaternion yawRotation = Quaternion.LookRotation(flatForward, Vector3.up);
                Vector3 desiredPos = _targetBike.position + yawRotation * (_chaseOffset - new Vector3(0f, _tuckBlend * 0.14f, 0f));
                transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-_positionSmooth * dt));

                Vector3 lookTarget = _targetBike.position + yawRotation * _lookAheadOffset;
                Quaternion desiredLook = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredLook, 1f - Mathf.Exp(-_rotationSmooth * dt));
            }

            if (_attachedCamera != null)
            {
                float speedRatio = Mathf.Clamp01(_currentSpeedMps / 24f);
                float targetFov = _baseFov + (speedRatio * _maxSpeedFovBonus) + (_tuckBlend * _tuckFovBonus);
                _attachedCamera.fieldOfView = Mathf.Lerp(_attachedCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-_fovSmooth * dt));
            }
        }
    }
}
