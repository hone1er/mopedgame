using System;
using MopedSim.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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

        /// <summary>F1 / Tab: toggle between the Garage workstand and riding.</summary>
        public readonly bool ToggleGarageRidePressed;

        /// <summary>F2: open or close the settings menu.</summary>
        public readonly bool ToggleSettingsPressed;

        /// <summary>F3: open or close the parts catalog shop.</summary>
        public readonly bool ToggleShopPressed;

        /// <summary>F4: cycle the paint booth's curated swatches on the bike.</summary>
        public readonly bool CyclePaintPressed;

        /// <summary>Escape / gamepad Start: close the top-most panel, otherwise toggle the pause menu.</summary>
        public readonly bool PausePressed;
        /// <summary>R / gamepad D-pad Down (both reserved session hotkeys): stand a fallen bike back up ("pick up bike").</summary>
        public readonly bool PickUpBikePressed;

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
            bool interactPressed,
            bool toggleGarageRidePressed = false,
            bool toggleSettingsPressed = false,
            bool toggleShopPressed = false,
            bool cyclePaintPressed = false,
            bool pickUpBikePressed = false,
            bool pausePressed = false)
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
            ToggleGarageRidePressed = toggleGarageRidePressed;
            ToggleSettingsPressed = toggleSettingsPressed;
            ToggleShopPressed = toggleShopPressed;
            CyclePaintPressed = cyclePaintPressed;
            PausePressed = pausePressed;
            PickUpBikePressed = pickUpBikePressed;
        }
    }

    /// <summary>
    /// Samples Unity InputSystem keyboard/gamepad devices or programmatic overrides for automated tests.
    /// Keyboard bindings come from <see cref="GameSettingsData"/> (MopedSim.UI settings menu): key names are parsed into cached
    /// <see cref="Key"/> values once in <see cref="ApplyKeyBindings"/>, never per frame, so <see cref="SampleInputs"/> stays
    /// allocation-free. Arrow keys and Z remain fixed secondary bindings. Gamepad buttons come from <see cref="GameSettingsData.GamepadBindings"/>
    /// (cached <see cref="GamepadButton"/> values; steering stays on the left stick, Start stays the settings hotkey).
    /// While the Settings Controls section is capturing a rebind, <see cref="SampleInputs"/> consumes the next key/button press and returns neutral inputs.
    /// Session hotkeys are fixed (not rebindable): F1/Tab Garage-Ride toggle, F2 settings, Escape/Start pause, F3 shop, F4 paint swatch.
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

        private GamepadButton _padThrottle = GamepadButton.RightTrigger;
        private GamepadButton _padFrontBrake = GamepadButton.LeftTrigger;
        private GamepadButton _padRearBrake = GamepadButton.East;
        private GamepadButton _padPedalAssist = GamepadButton.South;
        private GamepadButton _padAeroTuck = GamepadButton.LeftStick;
        private GamepadButton _padChoke = GamepadButton.DpadUp;
        private GamepadButton _padDecompressor = GamepadButton.LeftShoulder;
        private GamepadButton _padInteract = GamepadButton.West;
        private GamepadButton _padToggleCamera = GamepadButton.Select;

        private SettingsMenuView _captureView;
        private int _armedCaptureSerial = -1;

        public GamepadButton ThrottlePadButton { get { return _padThrottle; } }
        public GamepadButton FrontBrakePadButton { get { return _padFrontBrake; } }
        public GamepadButton RearBrakePadButton { get { return _padRearBrake; } }
        public GamepadButton PedalAssistPadButton { get { return _padPedalAssist; } }
        public GamepadButton AeroTuckPadButton { get { return _padAeroTuck; } }
        public GamepadButton ChokePadButton { get { return _padChoke; } }
        public GamepadButton DecompressorPadButton { get { return _padDecompressor; } }
        public GamepadButton InteractPadButton { get { return _padInteract; } }
        public GamepadButton ToggleCameraPadButton { get { return _padToggleCamera; } }

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
            ApplyGamepadBindings(settings, ref fallbacks);
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
            ResetDefaultGamepadBindings();
        }

        /// <summary>Restores the default gamepad layout (matches SettingsPreferencesStore defaults).</summary>
        public void ResetDefaultGamepadBindings()
        {
            int unused = 0;
            ApplyGamepadBindings(SettingsPreferencesStore.CreateDefaults(), ref unused);
        }

        /// <summary>Parses a gamepad button name ("RightTrigger", "east", ...) into a <see cref="GamepadButton"/>; Start is not assignable.</summary>
        public static bool TryParseGamepadButton(string name, out GamepadButton button)
        {
            button = GamepadButton.South;
            string canonical = SettingsPreferencesStore.CanonicalGamepadButton(name);
            if (canonical == null)
            {
                return false;
            }

            GamepadButton parsed;
            if (!Enum.TryParse(canonical, false, out parsed))
            {
                return false;
            }

            button = parsed;
            return true;
        }

        private void ApplyGamepadBindings(GameSettingsData settings, ref int fallbacks)
        {
            _padThrottle = ResolvePad(settings, KeyBindingActionIds.Throttle, GamepadButton.RightTrigger, ref fallbacks);
            _padFrontBrake = ResolvePad(settings, KeyBindingActionIds.FrontBrake, GamepadButton.LeftTrigger, ref fallbacks);
            _padRearBrake = ResolvePad(settings, KeyBindingActionIds.RearBrake, GamepadButton.East, ref fallbacks);
            _padPedalAssist = ResolvePad(settings, KeyBindingActionIds.PedalAssist, GamepadButton.South, ref fallbacks);
            _padAeroTuck = ResolvePad(settings, KeyBindingActionIds.AeroTuck, GamepadButton.LeftStick, ref fallbacks);
            _padChoke = ResolvePad(settings, KeyBindingActionIds.Choke, GamepadButton.DpadUp, ref fallbacks);
            _padDecompressor = ResolvePad(settings, KeyBindingActionIds.Decompressor, GamepadButton.LeftShoulder, ref fallbacks);
            _padInteract = ResolvePad(settings, KeyBindingActionIds.Interact, GamepadButton.West, ref fallbacks);
            _padToggleCamera = ResolvePad(settings, KeyBindingActionIds.ToggleCamera, GamepadButton.Select, ref fallbacks);
        }

        private static GamepadButton ResolvePad(GameSettingsData settings, string actionId, GamepadButton fallback, ref int fallbacks)
        {
            GamepadButton button;
            if (TryParseGamepadButton(SettingsPreferencesStore.GetGamepadBinding(settings, actionId), out button))
            {
                return button;
            }

            fallbacks++;
            return fallback;
        }

        /// <summary>Connects the Settings menu so its Controls section can capture the next key/gamepad press during <see cref="SampleInputs"/>.</summary>
        public void AttachBindingCapture(SettingsMenuView view)
        {
            _captureView = view;
        }

        /// <summary>
        /// While <paramref name="view"/> is capturing, consumes the next key (Escape cancels) or gamepad button (Start cancels).
        /// Capture is "armed" only after one poll sees every key/button released, so the Enter/Space/A press that activated the
        /// row (and its release) can never become the new binding. Returns true when capture is active (the caller must then
        /// ignore all other inputs this frame). Allocation-free unless a binding is committed.
        /// </summary>
        public bool PollBindingCapture(SettingsMenuView view)
        {
            if (view == null || !view.IsCapturing)
            {
                return false;
            }

            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
            bool armed = _armedCaptureSerial == view.CaptureSerial;
            bool escape = kb != null && kb.escapeKey.isPressed;
            bool start = gp != null && gp.startButton.isPressed;

            Key pressedKey = Key.None;
            if (kb != null && view.CaptureColumn == BindingColumn.Keyboard)
            {
                var keys = kb.allKeys;
                for (int i = 0; i < keys.Count; i++)
                {
                    var control = keys[i];
                    if (control.isPressed && control.keyCode != Key.None)
                    {
                        pressedKey = control.keyCode;
                        break;
                    }
                }
            }

            bool padPressed = false;
            GamepadButton pressedButton = GamepadButton.South;
            if (gp != null)
            {
                for (int i = 0; i < CapturableButtons.Length; i++)
                {
                    if (gp[CapturableButtons[i]].isPressed)
                    {
                        padPressed = true;
                        pressedButton = CapturableButtons[i];
                        break;
                    }
                }
            }

            bool anyDown = escape || start || padPressed || pressedKey != Key.None || AnyKeyboardKeyDown(kb);
            if (!armed)
            {
                if (!anyDown)
                {
                    _armedCaptureSerial = view.CaptureSerial;
                }

                return true;
            }

            if (escape || start)
            {
                view.CancelCapture();
            }
            else if (view.CaptureColumn == BindingColumn.Keyboard && pressedKey != Key.None)
            {
                view.TryCommitCapture(pressedKey.ToString());
            }
            else if (view.CaptureColumn == BindingColumn.Gamepad && padPressed)
            {
                view.TryCommitCapture(pressedButton.ToString());
            }

            return true;
        }

        private static bool AnyKeyboardKeyDown(Keyboard kb)
        {
            if (kb == null)
            {
                return false;
            }

            var keys = kb.allKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly GamepadButton[] CapturableButtons = new GamepadButton[]
        {
            GamepadButton.South, GamepadButton.West, GamepadButton.East,
            GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadRight,
            GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftStick, GamepadButton.RightStick,
            GamepadButton.Select, GamepadButton.LeftTrigger, GamepadButton.RightTrigger
        };

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

            if (_captureView != null && PollBindingCapture(_captureView))
            {
                return default(FrameControlInputs);
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
            bool toggleGarageRide = false;
            bool toggleSettings = false;
            bool toggleShop = false;
            bool cyclePaint = false;
            bool pause = false;
            bool pickUpBike = false;

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
                toggleGarageRide = kb.f1Key.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                toggleSettings = kb.f2Key.wasPressedThisFrame;
                pause = kb.escapeKey.wasPressedThisFrame;
                toggleShop = kb.f3Key.wasPressedThisFrame;
                cyclePaint = kb.f4Key.wasPressedThisFrame;
                pickUpBike = kb.rKey.wasPressedThisFrame;
            }

            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                throttle = Mathf.Max(throttle, gp[_padThrottle].ReadValue());
                frontBrake = Mathf.Max(frontBrake, gp[_padFrontBrake].ReadValue());
                rearBrake = Mathf.Max(rearBrake, gp[_padRearBrake].ReadValue());
                float stickX = gp.leftStick.x.ReadValue();
                if (Mathf.Abs(stickX) > Mathf.Abs(steer))
                {
                    steer = stickX;
                }

                aeroTuck = aeroTuck || gp[_padAeroTuck].isPressed;
                pedalAssist = pedalAssist || gp[_padPedalAssist].isPressed;
                choke = choke || gp[_padChoke].isPressed;
                decompressor = decompressor || gp[_padDecompressor].isPressed;
                toggleCamera = toggleCamera || gp[_padToggleCamera].wasPressedThisFrame;
                interact = interact || gp[_padInteract].wasPressedThisFrame;
                toggleGarageRide = toggleGarageRide || gp.buttonNorth.wasPressedThisFrame;
                pause = pause || gp.startButton.wasPressedThisFrame;
                pickUpBike = pickUpBike || gp.dpad.down.wasPressedThisFrame;
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
                interact,
                toggleGarageRide,
                toggleSettings,
                toggleShop,
                cyclePaint,
                pickUpBike,
                pause);
        }
    }
}
