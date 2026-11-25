using UnityEngine;
using UnityEngine.InputSystem;

namespace MopedSim.App
{
    /// <summary>
    /// Normalized riding and interaction inputs sampled from Unity InputSystem without per-frame GC allocations.
    /// </summary>
    public readonly struct FrameControlInputs
    {
        public readonly float Throttle;
        public readonly float FrontBrake;
        public readonly float RearBrake;
        public readonly float Steer;
        public readonly bool AeroTuck;
        public readonly bool PedalAssist;
        public readonly bool Choke;
        public readonly bool Decompressor;
        public readonly bool ToggleCameraPressed;
        public readonly bool InteractPressed;

        public FrameControlInputs(
            float throttle,
            float frontBrake,
            float rearBrake,
            float steer,
            bool aeroTuck,
            bool pedalAssist,
            bool choke,
            bool decompressor,
            bool toggleCameraPressed,
            bool interactPressed)
        {
            Throttle = throttle;
            FrontBrake = frontBrake;
            RearBrake = rearBrake;
            Steer = steer;
            AeroTuck = aeroTuck;
            PedalAssist = pedalAssist;
            Choke = choke;
            Decompressor = decompressor;
            ToggleCameraPressed = toggleCameraPressed;
            InteractPressed = interactPressed;
        }
    }

    /// <summary>
    /// Samples Unity InputSystem keyboard/gamepad devices or programmatic overrides for automated tests.
    /// </summary>
    public sealed class InputAdapter : MonoBehaviour
    {
        private bool _hasOverride;
        private FrameControlInputs _overrideInputs;

        public void SetTestOverride(in FrameControlInputs overrideInputs)
        {
            _hasOverride = true;
            _overrideInputs = overrideInputs;
        }

        public void ClearTestOverride()
        {
            _hasOverride = false;
        }

        public FrameControlInputs SampleInputs()
        {
            if (_hasOverride)
            {
                return _overrideInputs;
            }

            float throttle = 0f;
            float frontBrake = 0f;
            float rearBrake = 0f;
            float steer = 0f;
            bool aeroTuck = false;
            bool pedalAssist = false;
            bool choke = false;
            bool decompressor = false;
            bool toggleCamera = false;
            bool interact = false;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
                {
                    throttle = 1f;
                }

                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
                {
                    rearBrake = 1f;
                }

                if (kb.leftShiftKey.isPressed)
                {
                    frontBrake = 1f;
                }

                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
                {
                    steer -= 1f;
                }

                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
                {
                    steer += 1f;
                }

                aeroTuck = kb.leftCtrlKey.isPressed || kb.zKey.isPressed;
                pedalAssist = kb.spaceKey.isPressed;
                choke = kb.cKey.isPressed;
                decompressor = kb.qKey.isPressed;
                toggleCamera = kb.vKey.wasPressedThisFrame;
                interact = kb.eKey.wasPressedThisFrame;
            }

            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                throttle = Mathf.Max(throttle, gp.rightTrigger.ReadValue());
                frontBrake = Mathf.Max(frontBrake, gp.leftTrigger.ReadValue());
                rearBrake = Mathf.Max(rearBrake, gp.buttonEast.isPressed ? 1f : 0f);
                float stickX = gp.leftStick.x.ReadValue();
                if (Mathf.Abs(stickX) > Mathf.Abs(steer))
                {
                    steer = stickX;
                }

                aeroTuck = aeroTuck || gp.leftStickButton.isPressed;
                pedalAssist = pedalAssist || gp.buttonSouth.isPressed;
                choke = choke || gp.dpad.up.isPressed;
                decompressor = decompressor || gp.leftShoulder.isPressed;
                toggleCamera = toggleCamera || gp.selectButton.wasPressedThisFrame;
                interact = interact || gp.buttonWest.wasPressedThisFrame;
            }

            return new FrameControlInputs(
                Mathf.Clamp01(throttle),
                Mathf.Clamp01(frontBrake),
                Mathf.Clamp01(rearBrake),
                Mathf.Clamp(steer, -1f, 1f),
                aeroTuck,
                pedalAssist,
                choke,
                decompressor,
                toggleCamera,
                interact);
        }
    }
}
