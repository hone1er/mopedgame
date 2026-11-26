using System;
using MopedSim.UI;
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
    /// Keyboard bindings come from <see cref="GameSettingsData"/> (MopedSim.UI settings menu): key names are parsed into cached
    /// <see cref="Key"/> values once in <see cref="ApplyKeyBindings"/>, never per frame, so <see cref="SampleInputs"/> stays
    /// allocation-free. Arrow keys and Z remain fixed secondary bindings; gamepad controls are not rebindable yet.
    /// </summary>
    public sealed class InputAdapter : MonoBehaviour
    {
        private bool _hasOverride;
        private FrameControlInputs _overrideInputs;

        private Key _throttleKey = Key.W;
        private Key _steerLeftKey = Key.A;
        private Key _steerRightKey = Key.D;
        private Key _rearBrakeKey = Key.S;
        private Key _frontBrakeKey = Key.LeftShift;
        private Key _pedalAssistKey = Key.Space;
        private Key _aeroTuckKey = Key.LeftCtrl;
        private Key _chokeKey = Key.C;
        private Key _decompressorKey = Key.Q;
        private Key _interactKey = Key.E;
        private Key _toggleCameraKey = Key.V;

        public Key ThrottleKey { get { return _throttleKey; } }
        public Key SteerLeftKey { get { return _steerLeftKey; } }
        public Key SteerRightKey { get { return _steerRightKey; } }
        public Key RearBrakeKey { get { return _rearBrakeKey; } }
        public Key FrontBrakeKey { get { return _frontBrakeKey; } }
        public Key PedalAssistKey { get { return _pedalAssistKey; } }
        public Key AeroTuckKey { get { return _aeroTuckKey; } }
        public Key ChokeKey { get { return _chokeKey; } }
        public Key DecompressorKey { get { return _decompressorKey; } }
        public Key InteractKey { get { return _interactKey; } }
        public Key ToggleCameraKey { get { return _toggleCameraKey; } }

        public void SetTestOverride(in FrameControlInputs overrideInputs)
        {
            _hasOverride = true;
            _overrideInputs = overrideInputs;
        }

        public void ClearTestOverride()
        {
            _hasOverride = false;
        }

        /// <summary>
        /// Parses and caches every keyboard binding from <paramref name="settings"/> (event-driven; may allocate). Unknown or
        /// empty key names keep that action's default key. Returns the number of bindings that fell back to defaults.
        /// </summary>
        public int ApplyKeyBindings(GameSettingsData settings)
        {
            ResetDefaultKeyBindings();
            if (settings == null)
            {
                return 0;
            }

            int fallbacks = 0;
            _throttleKey = ResolveKey(settings.ThrottleKey, Key.W, ref fallbacks);
            _steerLeftKey = ResolveKey(settings.SteerLeftKey, Key.A, ref fallbacks);
            _steerRightKey = ResolveKey(settings.SteerRightKey, Key.D, ref fallbacks);
            _rearBrakeKey = ResolveKey(settings.RearBrakeKey, Key.S, ref fallbacks);
            _frontBrakeKey = ResolveKey(settings.FrontBrakeKey, Key.LeftShift, ref fallbacks);
            _pedalAssistKey = ResolveKey(settings.PedalAssistKey, Key.Space, ref fallbacks);
            _aeroTuckKey = ResolveKey(settings.AeroTuckKey, Key.LeftCtrl, ref fallbacks);
            _chokeKey = ResolveKey(settings.ChokeKey, Key.C, ref fallbacks);
            _decompressorKey = ResolveKey(settings.DecompressorKey, Key.Q, ref fallbacks);
            _interactKey = ResolveKey(settings.InteractKey, Key.E, ref fallbacks);
            _toggleCameraKey = ResolveKey(settings.ToggleCameraKey, Key.V, ref fallbacks);
            return fallbacks;
        }

        /// <summary>Restores the default keyboard layout (matches SettingsPreferencesStore defaults).</summary>
        public void ResetDefaultKeyBindings()
        {
            _throttleKey = Key.W;
            _steerLeftKey = Key.A;
            _steerRightKey = Key.D;
            _rearBrakeKey = Key.S;
            _frontBrakeKey = Key.LeftShift;
            _pedalAssistKey = Key.Space;
            _aeroTuckKey = Key.LeftCtrl;
            _chokeKey = Key.C;
            _decompressorKey = Key.Q;
            _interactKey = Key.E;
            _toggleCameraKey = Key.V;
        }

        /// <summary>
        /// Parses a settings key name ("w", "leftShift", "space", "1", "digit1", ...) into an InputSystem <see cref="Key"/>.
        /// Bare digits map to the top-row Digit keys; purely numeric enum values and Key.None are rejected.
        /// </summary>
        public static bool TryParseKeyName(string keyName, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            string trimmed = keyName.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            if (trimmed.Length == 1 && trimmed[0] >= '0' && trimmed[0] <= '9')
            {
                trimmed = "Digit" + trimmed;
            }
            else if (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '+')
            {
                // Enum.TryParse would accept raw numeric values ("42") as arbitrary Key members.
                return false;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsLetterOrDigit(trimmed[i]))
                {
                    // Enum.TryParse ORs comma-separated names ("w,a") into an unrelated Key value.
                    return false;
                }
            }

            Key parsed;
            if (!Enum.TryParse(trimmed, true, out parsed) || parsed == Key.None || !Enum.IsDefined(typeof(Key), parsed))
            {
                return false;
            }

            key = parsed;
            return true;
        }

        private static Key ResolveKey(string keyName, Key fallback, ref int fallbacks)
        {
            Key key;
            if (TryParseKeyName(keyName, out key))
            {
                return key;
            }

            fallbacks++;
            return fallback;
        }

        private static bool IsPressed(Keyboard kb, Key key)
        {
            return key != Key.None && kb[key].isPressed;
        }

        private static bool WasPressedThisFrame(Keyboard kb, Key key)
        {
            return key != Key.None && kb[key].wasPressedThisFrame;
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
                if (IsPressed(kb, _throttleKey) || kb.upArrowKey.isPressed)
                {
                    throttle = 1f;
                }

                if (IsPressed(kb, _rearBrakeKey) || kb.downArrowKey.isPressed)
                {
                    rearBrake = 1f;
                }

                if (IsPressed(kb, _frontBrakeKey))
                {
                    frontBrake = 1f;
                }

                if (IsPressed(kb, _steerLeftKey) || kb.leftArrowKey.isPressed)
                {
                    steer -= 1f;
                }

                if (IsPressed(kb, _steerRightKey) || kb.rightArrowKey.isPressed)
                {
                    steer += 1f;
                }

                aeroTuck = IsPressed(kb, _aeroTuckKey) || kb.zKey.isPressed;
                pedalAssist = IsPressed(kb, _pedalAssistKey);
                choke = IsPressed(kb, _chokeKey);
                decompressor = IsPressed(kb, _decompressorKey);
                toggleCamera = WasPressedThisFrame(kb, _toggleCameraKey);
                interact = WasPressedThisFrame(kb, _interactKey);
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
