using System;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Immutable value snapshot emitted by <see cref="SettingsMenuView.AudioMixChanged"/>. Zero heap allocations.
    /// </summary>
    public readonly struct AudioMixSettings
    {
        public readonly float MasterVolume;
        public readonly float EngineVolume;
        public readonly float FoleyVolume;
        public readonly float AmbienceVolume;

        public AudioMixSettings(float masterVolume, float engineVolume, float foleyVolume, float ambienceVolume)
        {
            MasterVolume = masterVolume;
            EngineVolume = engineVolume;
            FoleyVolume = foleyVolume;
            AmbienceVolume = ambienceVolume;
        }
    }

    /// <summary>
    /// Immutable value snapshot emitted by <see cref="SettingsMenuView.GraphicsPresetChanged"/>. Zero heap allocations.
    /// </summary>
    public readonly struct GraphicsQualitySettings
    {
        public readonly GraphicsPreset Preset;
        public readonly int TargetFrameRate;
        public readonly float FogQualityScale;

        public GraphicsQualitySettings(GraphicsPreset preset, int targetFrameRate, float fogQualityScale)
        {
            Preset = preset;
            TargetFrameRate = targetFrameRate;
            FogQualityScale = fogQualityScale;
        }
    }

    /// <summary>
    /// Event-driven Settings menu view managing Mechanic Diagnostic Hints, audio mix, graphics presets,
    /// and keyboard rebinding. Performs zero per-frame allocations (no Update loop).
    /// </summary>
    public sealed class SettingsMenuView : MonoBehaviour
    {
        private const string PresetLowLabel = "Low";
        private const string PresetMediumLabel = "Medium";
        private const string PresetHighLabel = "High";

        [Header("UI Controls")]
        [SerializeField] private Toggle _diagnosticHintsToggle;
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _engineVolumeSlider;
        [SerializeField] private Slider _foleyVolumeSlider;
        [SerializeField] private Slider _ambienceVolumeSlider;
        [SerializeField] private Text _graphicsPresetLabel;
        [SerializeField] private Text _conflictStatusLabel;

        private GameSettingsData _current = SettingsPreferencesStore.CreateDefaults();
        private DiagnosticHintsPresenter _hintsPresenter;
        private string _lastConflictAction = string.Empty;

        /// <summary>Raised when <see cref="Apply"/> commits the sanitized settings snapshot.</summary>
        public event Action<GameSettingsData> SettingsApplied;

        /// <summary>Raised when Mechanic Diagnostic Hints are toggled.</summary>
        public event Action<bool> DiagnosticHintsToggled;

        /// <summary>Raised when audio mix levels change.</summary>
        public event Action<AudioMixSettings> AudioMixChanged;

        /// <summary>Raised when graphics preset, target frame rate, or fog quality scale changes.</summary>
        public event Action<GraphicsQualitySettings> GraphicsPresetChanged;

        /// <summary>Raised when one or more keyboard bindings change.</summary>
        public event Action<GameSettingsData> KeyBindingsChanged;

        /// <summary>Current sanitized settings data held by this view.</summary>
        public GameSettingsData CurrentSettings
        {
            get
            {
                EnsureInitialized();
                return _current;
            }
        }

        /// <summary>Canonical action ID that conflicted on the most recent failed rebind (empty when none).</summary>
        public string LastConflictAction
        {
            get { return _lastConflictAction; }
        }

        /// <summary>Convenience readout of the current diagnostic hints flag.</summary>
        public bool DiagnosticHintsEnabled
        {
            get
            {
                EnsureInitialized();
                return _current.DiagnosticHintsEnabled;
            }
        }

        /// <summary>
        /// Binds the settings model and optional <see cref="DiagnosticHintsPresenter"/>, sanitizing values
        /// and synchronizing the presenter and UI controls.
        /// </summary>
        public void Bind(GameSettingsData settings, DiagnosticHintsPresenter hintsPresenter)
        {
            _current = settings != null
                ? SettingsPreferencesStore.Sanitize(settings)
                : SettingsPreferencesStore.CreateDefaults();
            _hintsPresenter = hintsPresenter;
            _lastConflictAction = string.Empty;

            SyncHintsPresenter();
            RefreshUiControls();
        }

        /// <summary>Toggles Mechanic Diagnostic Hints, updates the bound presenter, and emits <see cref="DiagnosticHintsToggled"/>.</summary>
        public void SetDiagnosticHints(bool enabled)
        {
            EnsureInitialized();
            _current.DiagnosticHintsEnabled = enabled;
            SyncHintsPresenter();
            if (_diagnosticHintsToggle != null)
            {
                _diagnosticHintsToggle.SetIsOnWithoutNotify(enabled);
            }

            Action<bool> handler = DiagnosticHintsToggled;
            if (handler != null)
            {
                handler(enabled);
            }
        }

        /// <summary>Updates and clamps audio mix sliders, then emits <see cref="AudioMixChanged"/>.</summary>
        public void SetAudioMix(float master, float engine, float foley, float ambience)
        {
            EnsureInitialized();
            _current.MasterVolume = master;
            _current.EngineVolume = engine;
            _current.FoleyVolume = foley;
            _current.AmbienceVolume = ambience;
            SettingsPreferencesStore.Sanitize(_current);

            RefreshAudioSliders();
            EmitAudioMixChanged();
        }

        /// <summary>Updates and clamps graphics preset, target frame rate, and fog quality scale, then emits <see cref="GraphicsPresetChanged"/>.</summary>
        public void SetGraphicsPreset(GraphicsPreset preset, int targetFps, float fogScale)
        {
            EnsureInitialized();
            _current.GraphicsPreset = preset;
            _current.TargetFrameRate = targetFps;
            _current.FogQualityScale = fogScale;
            SettingsPreferencesStore.Sanitize(_current);

            RefreshGraphicsLabel();
            EmitGraphicsPresetChanged();
        }

        /// <summary>
        /// Attempts to rebind <paramref name="actionId"/> to <paramref name="newKey"/>.
        /// Rejects duplicate keys with <paramref name="conflict"/> set to the conflicting action ID; emits <see cref="KeyBindingsChanged"/> only on success.
        /// </summary>
        public bool TryRebindAction(string actionId, string newKey, out string conflict)
        {
            EnsureInitialized();
            bool rebound = SettingsPreferencesStore.TrySetKeyBinding(_current, actionId, newKey, out conflict);
            if (!rebound)
            {
                _lastConflictAction = conflict ?? string.Empty;
                SetConflictText(_lastConflictAction);
                return false;
            }

            _lastConflictAction = string.Empty;
            SetConflictText(string.Empty);
            EmitKeyBindingsChanged();
            return true;
        }

        /// <summary>
        /// Resets all settings to defaults in place, syncs the bound presenter and UI controls,
        /// and notifies category listeners.
        /// </summary>
        public void ResetAllToDefaults()
        {
            EnsureInitialized();
            SettingsPreferencesStore.CopyDefaultsInto(_current);
            _lastConflictAction = string.Empty;

            SyncHintsPresenter();
            RefreshUiControls();

            Action<bool> hintsHandler = DiagnosticHintsToggled;
            if (hintsHandler != null)
            {
                hintsHandler(_current.DiagnosticHintsEnabled);
            }

            EmitAudioMixChanged();
            EmitGraphicsPresetChanged();
            EmitKeyBindingsChanged();
        }

        /// <summary>Sanitizes current settings, syncs the bound presenter, and emits <see cref="SettingsApplied"/>.</summary>
        public void Apply()
        {
            EnsureInitialized();
            SettingsPreferencesStore.Sanitize(_current);
            SyncHintsPresenter();
            RefreshUiControls();

            Action<GameSettingsData> handler = SettingsApplied;
            if (handler != null)
            {
                handler(_current);
            }
        }

        private void EnsureInitialized()
        {
            if (_current == null)
            {
                _current = SettingsPreferencesStore.CreateDefaults();
            }
        }

        private void SyncHintsPresenter()
        {
            if (_hintsPresenter != null)
            {
                _hintsPresenter.HintsEnabled = _current.DiagnosticHintsEnabled;
            }
        }

        private void RefreshUiControls()
        {
            if (_diagnosticHintsToggle != null)
            {
                _diagnosticHintsToggle.SetIsOnWithoutNotify(_current.DiagnosticHintsEnabled);
            }

            RefreshAudioSliders();
            RefreshGraphicsLabel();
            SetConflictText(_lastConflictAction);
        }

        private void RefreshAudioSliders()
        {
            if (_masterVolumeSlider != null) { _masterVolumeSlider.SetValueWithoutNotify(_current.MasterVolume); }
            if (_engineVolumeSlider != null) { _engineVolumeSlider.SetValueWithoutNotify(_current.EngineVolume); }
            if (_foleyVolumeSlider != null) { _foleyVolumeSlider.SetValueWithoutNotify(_current.FoleyVolume); }
            if (_ambienceVolumeSlider != null) { _ambienceVolumeSlider.SetValueWithoutNotify(_current.AmbienceVolume); }
        }

        private void RefreshGraphicsLabel()
        {
            if (_graphicsPresetLabel != null)
            {
                _graphicsPresetLabel.text = GetPresetLabel(_current.GraphicsPreset);
            }
        }

        private void SetConflictText(string conflictAction)
        {
            if (_conflictStatusLabel != null)
            {
                _conflictStatusLabel.text = conflictAction ?? string.Empty;
            }
        }

        private void EmitAudioMixChanged()
        {
            Action<AudioMixSettings> handler = AudioMixChanged;
            if (handler != null)
            {
                handler(new AudioMixSettings(
                    _current.MasterVolume,
                    _current.EngineVolume,
                    _current.FoleyVolume,
                    _current.AmbienceVolume));
            }
        }

        private void EmitGraphicsPresetChanged()
        {
            Action<GraphicsQualitySettings> handler = GraphicsPresetChanged;
            if (handler != null)
            {
                handler(new GraphicsQualitySettings(
                    _current.GraphicsPreset,
                    _current.TargetFrameRate,
                    _current.FogQualityScale));
            }
        }

        private void EmitKeyBindingsChanged()
        {
            Action<GameSettingsData> handler = KeyBindingsChanged;
            if (handler != null)
            {
                handler(_current);
            }
        }

        private static string GetPresetLabel(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.Low: return PresetLowLabel;
                case GraphicsPreset.High: return PresetHighLabel;
                default: return PresetMediumLabel;
            }
        }
    }
}
