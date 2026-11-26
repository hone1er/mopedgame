using UnityEngine;
using UnityEngine.InputSystem;

namespace MopedSim.App
{
    /// <summary>
    /// Minimal free-fly camera for the AssetGallery.unity showcase scene: WASD (+ Q/E down/up) to move, hold the right mouse button
    /// to look, Shift to fly faster. Reads InputSystem devices directly (the project uses the new Input System only) and performs zero
    /// GC allocations per frame. Presentation-only: no simulation state.
    /// </summary>
    public sealed class AssetGalleryFlyCamera : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 6f;
        [SerializeField] private float _fastMultiplier = 3.5f;
        [SerializeField] private float _lookSensitivity = 0.12f;

        private float _yaw;
        private float _pitch;

        public float MoveSpeed
        {
            get { return _moveSpeed; }
            set { _moveSpeed = Mathf.Max(0f, value); }
        }

        private void OnEnable()
        {
            Vector3 euler = transform.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) { move += Vector3.forward; }
            if (kb.sKey.isPressed) { move -= Vector3.forward; }
            if (kb.dKey.isPressed) { move += Vector3.right; }
            if (kb.aKey.isPressed) { move -= Vector3.right; }
            if (kb.eKey.isPressed) { move += Vector3.up; }
            if (kb.qKey.isPressed) { move -= Vector3.up; }
            if (move.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float speed = _moveSpeed * (kb.leftShiftKey.isPressed ? _fastMultiplier : 1f);
            transform.position += transform.TransformDirection(move.normalized) * (speed * dt);
        }
    }
}
