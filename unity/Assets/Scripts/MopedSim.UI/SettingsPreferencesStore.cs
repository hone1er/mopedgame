using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MopedSim.Data;
using UnityEngine;

namespace MopedSim.UI
{
    /// <summary>
    /// Stylized quality tier presets selectable from the Settings menu.
    /// </summary>
    public enum GraphicsPreset
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Strongly typed action identifiers for keyboard control bindings stored in <see cref="GameSettingsData"/>.
    /// </summary>
    public static class KeyBindingActionIds
    {
        public const string Throttle = "throttle";
        public const string SteerLeft = "steer_left";
        public const string SteerRight = "steer_right";
        public const string RearBrake = "rear_brake";
        public const string FrontBrake = "front_brake";
        public const string PedalAssist = "pedal_assist";
        public const string AeroTuck = "aero_tuck";
        public const string Choke = "choke";
        public const string Decompressor = "decompressor";
        public const string Interact = "interact";
        public const string ToggleCamera = "toggle_camera";

        private static readonly string[] AllIds = new string[]
        {
            Throttle,
            SteerLeft,
            SteerRight,
            RearBrake,
            FrontBrake,
            PedalAssist,
            AeroTuck,
            Choke,
            Decompressor,
            Interact,
            ToggleCamera
        };

        /// <summary>All supported bindable action identifiers in canonical order.</summary>
        public static IReadOnlyList<string> All { get { return AllIds; } }
    }

    /// <summary>
    /// Actions whose gamepad button is rebindable, in canonical order (indexes <see cref="GameSettingsData.GamepadBindings"/>).
    /// Steering stays on the left stick and the settings hotkey on Start, so neither appears here.
    /// </summary>
    public static class GamepadBindingActionIds
    {
        private static readonly string[] AllIds = new string[]
        {
            KeyBindingActionIds.Throttle,
            KeyBindingActionIds.FrontBrake,
            KeyBindingActionIds.RearBrake,
            KeyBindingActionIds.PedalAssist,
            KeyBindingActionIds.AeroTuck,
            KeyBindingActionIds.Choke,
            KeyBindingActionIds.Decompressor,
            KeyBindingActionIds.Interact,
            KeyBindingActionIds.ToggleCamera
        };

        /// <summary>Gamepad-rebindable action identifiers in canonical order.</summary>
        public static IReadOnlyList<string> All { get { return AllIds; } }

        /// <summary>Index of <paramref name="canonicalActionId"/> in <see cref="All"/>, or -1 when not gamepad-bindable.</summary>
        public static int IndexOf(string canonicalActionId)
        {
            for (int i = 0; i < AllIds.Length; i++)
            {
                if (string.Equals(AllIds[i], canonicalActionId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// Serializable user preferences document persisted as JSON via <see cref="JsonUtility"/>.
    /// Key names match <c>UnityEngine.InputSystem.Keyboard</c> control paths (e.g. "w", "leftShift", "space").
    /// </summary>
    [Serializable]
    public sealed class GameSettingsData
    {
        public bool DiagnosticHintsEnabled = SettingsPreferencesStore.DefaultDiagnosticHintsEnabled;

        public float MasterVolume = SettingsPreferencesStore.DefaultMasterVolume;
        public float EngineVolume = SettingsPreferencesStore.DefaultEngineVolume;
        public float FoleyVolume = SettingsPreferencesStore.DefaultFoleyVolume;
        public float AmbienceVolume = SettingsPreferencesStore.DefaultAmbienceVolume;

        public GraphicsPreset GraphicsPreset = SettingsPreferencesStore.DefaultGraphicsPreset;
        public int TargetFrameRate = SettingsPreferencesStore.DefaultTargetFrameRate;
        public float FogQualityScale = SettingsPreferencesStore.DefaultFogQualityScale;

        public string ThrottleKey = SettingsPreferencesStore.DefaultThrottleKey;
        public string SteerLeftKey = SettingsPreferencesStore.DefaultSteerLeftKey;
        public string SteerRightKey = SettingsPreferencesStore.DefaultSteerRightKey;
        public string RearBrakeKey = SettingsPreferencesStore.DefaultRearBrakeKey;
        public string FrontBrakeKey = SettingsPreferencesStore.DefaultFrontBrakeKey;
        public string PedalAssistKey = SettingsPreferencesStore.DefaultPedalAssistKey;
        public string AeroTuckKey = SettingsPreferencesStore.DefaultAeroTuckKey;
        public string ChokeKey = SettingsPreferencesStore.DefaultChokeKey;
        public string DecompressorKey = SettingsPreferencesStore.DefaultDecompressorKey;
        public string InteractKey = SettingsPreferencesStore.DefaultInteractKey;
        public string ToggleCameraKey = SettingsPreferencesStore.DefaultToggleCameraKey;

        /// <summary>
        /// Gamepad button names (<c>UnityEngine.InputSystem.LowLevel.GamepadButton</c> members such as "RightTrigger" or "East"),
        /// indexed by <see cref="GamepadBindingActionIds.All"/>. Older preference files lack this field and load the defaults.
        /// </summary>
        public string[] GamepadBindings = SettingsPreferencesStore.CreateDefaultGamepadBindings();

        /// <summary>Copies all settings fields from <paramref name="source"/> into this instance without allocating.</summary>
        public void CopyFrom(GameSettingsData source)
        {
            if (source == null)
            {
                return;
            }

            DiagnosticHintsEnabled = source.DiagnosticHintsEnabled;
            MasterVolume = source.MasterVolume;
            EngineVolume = source.EngineVolume;
            FoleyVolume = source.FoleyVolume;
            AmbienceVolume = source.AmbienceVolume;
            GraphicsPreset = source.GraphicsPreset;
            TargetFrameRate = source.TargetFrameRate;
            FogQualityScale = source.FogQualityScale;
            ThrottleKey = source.ThrottleKey;
            SteerLeftKey = source.SteerLeftKey;
            SteerRightKey = source.SteerRightKey;
            RearBrakeKey = source.RearBrakeKey;
            FrontBrakeKey = source.FrontBrakeKey;
            PedalAssistKey = source.PedalAssistKey;
            AeroTuckKey = source.AeroTuckKey;
            ChokeKey = source.ChokeKey;
            DecompressorKey = source.DecompressorKey;
            InteractKey = source.InteractKey;
            ToggleCameraKey = source.ToggleCameraKey;
            SettingsPreferencesStore.CopyGamepadBindings(source.GamepadBindings, this);
        }

        /// <summary>Creates a deep copy of this settings document.</summary>
        public GameSettingsData Clone()
        {
            GameSettingsData copy = new GameSettingsData();
            copy.CopyFrom(this);
            return copy;
        }
    }

    /// <summary>
    /// Manages default creation, sanitization, JSON persistence, conflict-checked key rebinding,
    /// and <see cref="SaveState"/> synchronization for <see cref="GameSettingsData"/>.
    /// </summary>
    public static class SettingsPreferencesStore
    {
        public const bool DefaultDiagnosticHintsEnabled = true;

        public const float DefaultMasterVolume = 1f;
        public const float DefaultEngineVolume = 0.85f;
        public const float DefaultFoleyVolume = 0.85f;
        public const float DefaultAmbienceVolume = 0.75f;

        public const GraphicsPreset DefaultGraphicsPreset = GraphicsPreset.Medium;
        public const int MinTargetFrameRate = 60;
        public const int MaxTargetFrameRate = 120;
        public const int DefaultTargetFrameRate = 60;

        public const float MinFogQualityScale = 0.25f;
        public const float MaxFogQualityScale = 1.5f;
        public const float DefaultFogQualityScale = 1f;

        public const string DefaultThrottleKey = "w";
        public const string DefaultSteerLeftKey = "a";
        public const string DefaultSteerRightKey = "d";
        public const string DefaultRearBrakeKey = "s";
        public const string DefaultFrontBrakeKey = "leftShift";
        public const string DefaultPedalAssistKey = "space";
        public const string DefaultAeroTuckKey = "leftCtrl";
        public const string DefaultChokeKey = "c";
        public const string DefaultDecompressorKey = "q";
        public const string DefaultInteractKey = "e";
        public const string DefaultToggleCameraKey = "v";

        /// <summary>Conflict marker returned when a key or button is reserved for a fixed session hotkey or is unknown.</summary>
        public const string ReservedConflict = "reserved";

        private static readonly string[] DefaultGamepadButtons = new string[]
        {
            "RightTrigger", // throttle
            "LeftTrigger",  // front brake
            "East",         // rear brake
            "South",        // pedal assist
            "LeftStick",    // aero tuck
            "DpadUp",       // choke
            "LeftShoulder", // decompressor
            "West",         // interact
            "Select"        // toggle camera
        };

        // Start (settings hotkey / capture cancel) and North (Garage-Ride toggle) are fixed session buttons, so they are excluded.
        private static readonly string[] AssignableGamepadButtons = new string[]
        {
            "South", "West", "East",
            "DpadUp", "DpadDown", "DpadLeft", "DpadRight",
            "LeftShoulder", "RightShoulder", "LeftStick", "RightStick",
            "Select", "LeftTrigger", "RightTrigger"
        };

        private static readonly string[] ReservedKeyNames = new string[] { "escape", "tab", "f1", "f2", "f3", "f4" };

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Creates a fresh <see cref="GameSettingsData"/> populated with default values.</summary>
        public static GameSettingsData CreateDefaults()
        {
            return new GameSettingsData();
        }

        /// <summary>Resets all fields on <paramref name="target"/> to default values in place without allocating.</summary>
        public static void CopyDefaultsInto(GameSettingsData target)
        {
            if (target == null)
            {
                return;
            }

            target.DiagnosticHintsEnabled = DefaultDiagnosticHintsEnabled;
            target.MasterVolume = DefaultMasterVolume;
            target.EngineVolume = DefaultEngineVolume;
            target.FoleyVolume = DefaultFoleyVolume;
            target.AmbienceVolume = DefaultAmbienceVolume;
            target.GraphicsPreset = DefaultGraphicsPreset;
            target.TargetFrameRate = DefaultTargetFrameRate;
            target.FogQualityScale = DefaultFogQualityScale;
            ResetKeyBindings(target);
            ResetGamepadBindings(target);
        }

        /// <summary>
        /// Clamps volumes, target frame rate, and fog scale into valid ranges, validates the graphics preset,
        /// and restores any missing or blank keybinding names to their defaults.
        /// </summary>
        public static GameSettingsData Sanitize(GameSettingsData data)
        {
            if (data == null)
            {
                return CreateDefaults();
            }

            data.MasterVolume = ClampFloat(data.MasterVolume, 0f, 1f, DefaultMasterVolume);
            data.EngineVolume = ClampFloat(data.EngineVolume, 0f, 1f, DefaultEngineVolume);
            data.FoleyVolume = ClampFloat(data.FoleyVolume, 0f, 1f, DefaultFoleyVolume);
            data.AmbienceVolume = ClampFloat(data.AmbienceVolume, 0f, 1f, DefaultAmbienceVolume);

            if (data.GraphicsPreset < GraphicsPreset.Low || data.GraphicsPreset > GraphicsPreset.High)
            {
                data.GraphicsPreset = DefaultGraphicsPreset;
            }

            data.TargetFrameRate = Mathf.Clamp(data.TargetFrameRate, MinTargetFrameRate, MaxTargetFrameRate);
            data.FogQualityScale = ClampFloat(data.FogQualityScale, MinFogQualityScale, MaxFogQualityScale, DefaultFogQualityScale);

            data.ThrottleKey = SanitizeKey(data.ThrottleKey, DefaultThrottleKey);
            data.SteerLeftKey = SanitizeKey(data.SteerLeftKey, DefaultSteerLeftKey);
            data.SteerRightKey = SanitizeKey(data.SteerRightKey, DefaultSteerRightKey);
            data.RearBrakeKey = SanitizeKey(data.RearBrakeKey, DefaultRearBrakeKey);
            data.FrontBrakeKey = SanitizeKey(data.FrontBrakeKey, DefaultFrontBrakeKey);
            data.PedalAssistKey = SanitizeKey(data.PedalAssistKey, DefaultPedalAssistKey);
            data.AeroTuckKey = SanitizeKey(data.AeroTuckKey, DefaultAeroTuckKey);
            data.ChokeKey = SanitizeKey(data.ChokeKey, DefaultChokeKey);
            data.DecompressorKey = SanitizeKey(data.DecompressorKey, DefaultDecompressorKey);
            data.InteractKey = SanitizeKey(data.InteractKey, DefaultInteractKey);
            data.ToggleCameraKey = SanitizeKey(data.ToggleCameraKey, DefaultToggleCameraKey);
            SanitizeGamepadBindings(data);

            return data;
        }

        /// <summary>
        /// Loads settings JSON from <paramref name="path"/> and sanitizes it.
        /// Returns defaults cleanly if the file is missing, empty, unreadable, or contains corrupt JSON.
        /// </summary>
        public static GameSettingsData LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return CreateDefaults();
            }

            try
            {
                if (!File.Exists(path))
                {
                    return CreateDefaults();
                }

                string json = File.ReadAllText(path, Utf8NoBom);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return CreateDefaults();
                }

                string trimmed = json.Trim();
                if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
                {
                    return CreateDefaults();
                }

                GameSettingsData loaded = JsonUtility.FromJson<GameSettingsData>(trimmed);
                return Sanitize(loaded);
            }
            catch (Exception)
            {
                return CreateDefaults();
            }
        }

        /// <summary>
        /// Sanitizes and saves <paramref name="data"/> as formatted JSON to <paramref name="path"/>.
        /// </summary>
        public static void SaveToPath(string path, GameSettingsData data)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Settings file path must not be null or whitespace.", nameof(path));
            }

            GameSettingsData sanitized = Sanitize(data != null ? data.Clone() : CreateDefaults());
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(sanitized, true);
            File.WriteAllText(path, json, Utf8NoBom);
        }

        /// <summary>
        /// Attempts to bind <paramref name="actionId"/> to <paramref name="keyName"/>.
        /// Rejects duplicate bindings across actions (case-insensitive) and reports the conflicting canonical action ID.
        /// </summary>
        public static bool TrySetKeyBinding(GameSettingsData data, string actionId, string keyName, out string conflictAction)
        {
            conflictAction = string.Empty;
            if (data == null)
            {
                return false;
            }

            string canonical = NormalizeActionId(actionId);
            if (canonical == null || string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            string trimmedKey = keyName.Trim();
            if (IsReservedKey(trimmedKey))
            {
                conflictAction = ReservedConflict;
                return false;
            }

            IReadOnlyList<string> allActions = KeyBindingActionIds.All;
            for (int i = 0; i < allActions.Count; i++)
            {
                string otherAction = allActions[i];
                if (string.Equals(otherAction, canonical, StringComparison.Ordinal))
                {
                    continue;
                }

                string existingKey = GetKeyBindingRaw(data, otherAction);
                if (!string.IsNullOrEmpty(existingKey)
                    && string.Equals(existingKey, trimmedKey, StringComparison.OrdinalIgnoreCase))
                {
                    conflictAction = otherAction;
                    return false;
                }
            }

            SetKeyBindingRaw(data, canonical, trimmedKey);
            return true;
        }

        /// <summary>Gets the key binding string for <paramref name="actionId"/>, or empty string if unknown.</summary>
        public static string GetKeyBinding(GameSettingsData data, string actionId)
        {
            if (data == null)
            {
                return string.Empty;
            }

            string canonical = NormalizeActionId(actionId);
            if (canonical == null)
            {
                return string.Empty;
            }

            return GetKeyBindingRaw(data, canonical) ?? string.Empty;
        }

        /// <summary>Restores all 11 keyboard bindings on <paramref name="data"/> to their default values.</summary>
        public static void ResetKeyBindings(GameSettingsData data)
        {
            if (data == null)
            {
                return;
            }

            data.ThrottleKey = DefaultThrottleKey;
            data.SteerLeftKey = DefaultSteerLeftKey;
            data.SteerRightKey = DefaultSteerRightKey;
            data.RearBrakeKey = DefaultRearBrakeKey;
            data.FrontBrakeKey = DefaultFrontBrakeKey;
            data.PedalAssistKey = DefaultPedalAssistKey;
            data.AeroTuckKey = DefaultAeroTuckKey;
            data.ChokeKey = DefaultChokeKey;
            data.DecompressorKey = DefaultDecompressorKey;
            data.InteractKey = DefaultInteractKey;
            data.ToggleCameraKey = DefaultToggleCameraKey;
        }

        /// <summary>Returns true when <paramref name="keyName"/> is one of the fixed session hotkeys.</summary>
        public static bool IsReservedKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            string trimmed = keyName.Trim();
            for (int i = 0; i < ReservedKeyNames.Length; i++)
            {
                if (EqualsIgnoreCase(ReservedKeyNames[i], trimmed))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Creates a fresh array of default gamepad button names (parallel to <see cref="GamepadBindingActionIds.All"/>).</summary>
        public static string[] CreateDefaultGamepadBindings()
        {
            return (string[])DefaultGamepadButtons.Clone();
        }

        /// <summary>Default gamepad button name for <paramref name="actionId"/>, or empty when the action is not gamepad-bindable.</summary>
        public static string GetDefaultGamepadBinding(string actionId)
        {
            int index = GamepadBindingActionIds.IndexOf(NormalizeActionId(actionId));
            return index >= 0 ? DefaultGamepadButtons[index] : string.Empty;
        }

        /// <summary>Resolves <paramref name="buttonName"/> to its canonical assignable gamepad button name (case-insensitive), or null.</summary>
        public static string CanonicalGamepadButton(string buttonName)
        {
            if (string.IsNullOrWhiteSpace(buttonName))
            {
                return null;
            }

            string trimmed = buttonName.Trim();
            for (int i = 0; i < AssignableGamepadButtons.Length; i++)
            {
                if (EqualsIgnoreCase(AssignableGamepadButtons[i], trimmed))
                {
                    return AssignableGamepadButtons[i];
                }
            }

            return null;
        }

        /// <summary>Gets the gamepad button name bound to <paramref name="actionId"/>, or empty if it is not gamepad-bindable.</summary>
        public static string GetGamepadBinding(GameSettingsData data, string actionId)
        {
            if (data == null)
            {
                return string.Empty;
            }

            int index = GamepadBindingActionIds.IndexOf(NormalizeActionId(actionId));
            if (index < 0)
            {
                return string.Empty;
            }

            if (data.GamepadBindings == null || index >= data.GamepadBindings.Length || string.IsNullOrEmpty(data.GamepadBindings[index]))
            {
                return DefaultGamepadButtons[index];
            }

            return data.GamepadBindings[index];
        }

        /// <summary>
        /// Attempts to bind <paramref name="actionId"/> to the gamepad button <paramref name="buttonName"/>.
        /// Rejects unknown/reserved buttons (<see cref="ReservedConflict"/>) and buttons already used by another action
        /// (<paramref name="conflictAction"/> is that action's canonical ID).
        /// </summary>
        public static bool TrySetGamepadBinding(GameSettingsData data, string actionId, string buttonName, out string conflictAction)
        {
            conflictAction = string.Empty;
            if (data == null)
            {
                return false;
            }

            int index = GamepadBindingActionIds.IndexOf(NormalizeActionId(actionId));
            if (index < 0)
            {
                return false;
            }

            string canonicalButton = CanonicalGamepadButton(buttonName);
            if (canonicalButton == null)
            {
                conflictAction = ReservedConflict;
                return false;
            }

            EnsureGamepadArray(data);
            for (int i = 0; i < data.GamepadBindings.Length; i++)
            {
                if (i != index && EqualsIgnoreCase(data.GamepadBindings[i], canonicalButton))
                {
                    conflictAction = GamepadBindingActionIds.All[i];
                    return false;
                }
            }

            data.GamepadBindings[index] = canonicalButton;
            return true;
        }

        /// <summary>Restores all gamepad bindings on <paramref name="data"/> to their defaults in place.</summary>
        public static void ResetGamepadBindings(GameSettingsData data)
        {
            if (data == null)
            {
                return;
            }

            EnsureGamepadArray(data);
            for (int i = 0; i < DefaultGamepadButtons.Length; i++)
            {
                data.GamepadBindings[i] = DefaultGamepadButtons[i];
            }
        }

        internal static void CopyGamepadBindings(string[] source, GameSettingsData target)
        {
            if (ReferenceEquals(source, target.GamepadBindings))
            {
                return;
            }

            EnsureGamepadArray(target);
            for (int i = 0; i < DefaultGamepadButtons.Length; i++)
            {
                target.GamepadBindings[i] = source != null && i < source.Length && !string.IsNullOrEmpty(source[i])
                    ? source[i]
                    : DefaultGamepadButtons[i];
            }
        }

        private static void EnsureGamepadArray(GameSettingsData data)
        {
            if (data.GamepadBindings == null || data.GamepadBindings.Length != DefaultGamepadButtons.Length)
            {
                string[] old = data.GamepadBindings;
                data.GamepadBindings = new string[DefaultGamepadButtons.Length];
                for (int i = 0; i < data.GamepadBindings.Length; i++)
                {
                    data.GamepadBindings[i] = old != null && i < old.Length ? old[i] : null;
                }
            }
        }

        // Migration: preference files written before gamepad rebinding (or with invalid/duplicate entries) fall back to defaults.
        private static void SanitizeGamepadBindings(GameSettingsData data)
        {
            EnsureGamepadArray(data);
            bool duplicate = false;
            for (int i = 0; i < data.GamepadBindings.Length; i++)
            {
                string canonical = CanonicalGamepadButton(data.GamepadBindings[i]);
                data.GamepadBindings[i] = canonical ?? DefaultGamepadButtons[i];
                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(data.GamepadBindings[j], data.GamepadBindings[i], StringComparison.Ordinal))
                    {
                        duplicate = true;
                    }
                }
            }

            if (duplicate)
            {
                ResetGamepadBindings(data);
            }
        }

        /// <summary>Synchronizes save-persisted preferences (such as <see cref="SaveState.DiagnosticHintsEnabled"/>) onto <paramref name="saveState"/>.</summary>
        public static void ApplyToSaveState(GameSettingsData data, SaveState saveState)
        {
            if (saveState == null)
            {
                return;
            }

            saveState.DiagnosticHintsEnabled = data != null ? data.DiagnosticHintsEnabled : DefaultDiagnosticHintsEnabled;
        }

        private static float ClampFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value))
            {
                return fallback;
            }

            if (float.IsNegativeInfinity(value))
            {
                return min;
            }

            if (float.IsPositiveInfinity(value))
            {
                return max;
            }

            return Mathf.Clamp(value, min, max);
        }

        private static string SanitizeKey(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim();
            return IsReservedKey(trimmed) ? fallback : trimmed;
        }

        private static string NormalizeActionId(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            string trimmed = actionId.Trim();
            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.Throttle) || EqualsIgnoreCase(trimmed, "ThrottleKey"))
            {
                return KeyBindingActionIds.Throttle;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.SteerLeft) || EqualsIgnoreCase(trimmed, "SteerLeft") || EqualsIgnoreCase(trimmed, "SteerLeftKey"))
            {
                return KeyBindingActionIds.SteerLeft;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.SteerRight) || EqualsIgnoreCase(trimmed, "SteerRight") || EqualsIgnoreCase(trimmed, "SteerRightKey"))
            {
                return KeyBindingActionIds.SteerRight;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.RearBrake) || EqualsIgnoreCase(trimmed, "RearBrake") || EqualsIgnoreCase(trimmed, "RearBrakeKey"))
            {
                return KeyBindingActionIds.RearBrake;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.FrontBrake) || EqualsIgnoreCase(trimmed, "FrontBrake") || EqualsIgnoreCase(trimmed, "FrontBrakeKey"))
            {
                return KeyBindingActionIds.FrontBrake;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.PedalAssist) || EqualsIgnoreCase(trimmed, "PedalAssist") || EqualsIgnoreCase(trimmed, "PedalAssistKey"))
            {
                return KeyBindingActionIds.PedalAssist;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.AeroTuck) || EqualsIgnoreCase(trimmed, "AeroTuck") || EqualsIgnoreCase(trimmed, "AeroTuckKey"))
            {
                return KeyBindingActionIds.AeroTuck;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.Choke) || EqualsIgnoreCase(trimmed, "ChokeKey"))
            {
                return KeyBindingActionIds.Choke;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.Decompressor) || EqualsIgnoreCase(trimmed, "DecompressorKey"))
            {
                return KeyBindingActionIds.Decompressor;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.Interact) || EqualsIgnoreCase(trimmed, "InteractKey"))
            {
                return KeyBindingActionIds.Interact;
            }

            if (EqualsIgnoreCase(trimmed, KeyBindingActionIds.ToggleCamera) || EqualsIgnoreCase(trimmed, "ToggleCamera") || EqualsIgnoreCase(trimmed, "ToggleCameraKey"))
            {
                return KeyBindingActionIds.ToggleCamera;
            }

            return null;
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetKeyBindingRaw(GameSettingsData data, string canonicalActionId)
        {
            switch (canonicalActionId)
            {
                case KeyBindingActionIds.Throttle: return data.ThrottleKey;
                case KeyBindingActionIds.SteerLeft: return data.SteerLeftKey;
                case KeyBindingActionIds.SteerRight: return data.SteerRightKey;
                case KeyBindingActionIds.RearBrake: return data.RearBrakeKey;
                case KeyBindingActionIds.FrontBrake: return data.FrontBrakeKey;
                case KeyBindingActionIds.PedalAssist: return data.PedalAssistKey;
                case KeyBindingActionIds.AeroTuck: return data.AeroTuckKey;
                case KeyBindingActionIds.Choke: return data.ChokeKey;
                case KeyBindingActionIds.Decompressor: return data.DecompressorKey;
                case KeyBindingActionIds.Interact: return data.InteractKey;
                case KeyBindingActionIds.ToggleCamera: return data.ToggleCameraKey;
                default: return string.Empty;
            }
        }

        private static void SetKeyBindingRaw(GameSettingsData data, string canonicalActionId, string keyName)
        {
            switch (canonicalActionId)
            {
                case KeyBindingActionIds.Throttle: data.ThrottleKey = keyName; break;
                case KeyBindingActionIds.SteerLeft: data.SteerLeftKey = keyName; break;
                case KeyBindingActionIds.SteerRight: data.SteerRightKey = keyName; break;
                case KeyBindingActionIds.RearBrake: data.RearBrakeKey = keyName; break;
                case KeyBindingActionIds.FrontBrake: data.FrontBrakeKey = keyName; break;
                case KeyBindingActionIds.PedalAssist: data.PedalAssistKey = keyName; break;
                case KeyBindingActionIds.AeroTuck: data.AeroTuckKey = keyName; break;
                case KeyBindingActionIds.Choke: data.ChokeKey = keyName; break;
                case KeyBindingActionIds.Decompressor: data.DecompressorKey = keyName; break;
                case KeyBindingActionIds.Interact: data.InteractKey = keyName; break;
                case KeyBindingActionIds.ToggleCamera: data.ToggleCameraKey = keyName; break;
            }
        }
    }
}
