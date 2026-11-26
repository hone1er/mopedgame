using System;
using System.IO;
using MopedSim.Data;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// M7 EditMode coverage for SettingsPreferencesStore and SettingsMenuView:
    /// defaults, clamping/sanitization, JSON persistence roundtrip, corrupt fallback,
    /// keybinding conflict rejection, SaveState synchronization, and view event notifications.
    /// </summary>
    public sealed class SettingsMenuTests
    {
        private GameObject _root;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mopedgame_settings_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }

            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void CreateDefaults_PopulatesExpectedAudioGraphicsAndKeyBindings()
        {
            GameSettingsData defaults = SettingsPreferencesStore.CreateDefaults();

            Assert.IsTrue(defaults.DiagnosticHintsEnabled);
            Assert.AreEqual(1f, defaults.MasterVolume, 1e-5f);
            Assert.AreEqual(0.85f, defaults.EngineVolume, 1e-5f);
            Assert.AreEqual(0.85f, defaults.FoleyVolume, 1e-5f);
            Assert.AreEqual(0.75f, defaults.AmbienceVolume, 1e-5f);
            Assert.AreEqual(GraphicsPreset.Medium, defaults.GraphicsPreset);
            Assert.AreEqual(60, defaults.TargetFrameRate);
            Assert.AreEqual(1f, defaults.FogQualityScale, 1e-5f);

            Assert.AreEqual("w", defaults.ThrottleKey);
            Assert.AreEqual("a", defaults.SteerLeftKey);
            Assert.AreEqual("d", defaults.SteerRightKey);
            Assert.AreEqual("s", defaults.RearBrakeKey);
            Assert.AreEqual("leftShift", defaults.FrontBrakeKey);
            Assert.AreEqual("space", defaults.PedalAssistKey);
            Assert.AreEqual("leftCtrl", defaults.AeroTuckKey);
            Assert.AreEqual("c", defaults.ChokeKey);
            Assert.AreEqual("q", defaults.DecompressorKey);
            Assert.AreEqual("e", defaults.InteractKey);
            Assert.AreEqual("v", defaults.ToggleCameraKey);
        }

        [Test]
        public void Sanitize_ClampsVolumesFrameRateFogScaleAndRestoresEmptyKeys()
        {
            GameSettingsData dirty = new GameSettingsData
            {
                DiagnosticHintsEnabled = false,
                MasterVolume = 2.5f,
                EngineVolume = -0.4f,
                FoleyVolume = float.NaN,
                AmbienceVolume = float.PositiveInfinity,
                GraphicsPreset = (GraphicsPreset)99,
                TargetFrameRate = 240,
                FogQualityScale = 0.05f,
                ThrottleKey = "",
                SteerLeftKey = "   ",
                SteerRightKey = null,
                InteractKey = "  f  "
            };

            GameSettingsData sanitized = SettingsPreferencesStore.Sanitize(dirty);

            Assert.AreSame(dirty, sanitized);
            Assert.IsFalse(sanitized.DiagnosticHintsEnabled);
            Assert.AreEqual(1f, sanitized.MasterVolume, 1e-5f);
            Assert.AreEqual(0f, sanitized.EngineVolume, 1e-5f);
            Assert.AreEqual(SettingsPreferencesStore.DefaultFoleyVolume, sanitized.FoleyVolume, 1e-5f);
            Assert.AreEqual(1f, sanitized.AmbienceVolume, 1e-5f);
            Assert.AreEqual(GraphicsPreset.Medium, sanitized.GraphicsPreset);
            Assert.AreEqual(SettingsPreferencesStore.MaxTargetFrameRate, sanitized.TargetFrameRate);
            Assert.AreEqual(SettingsPreferencesStore.MinFogQualityScale, sanitized.FogQualityScale, 1e-5f);
            Assert.AreEqual(SettingsPreferencesStore.DefaultThrottleKey, sanitized.ThrottleKey);
            Assert.AreEqual(SettingsPreferencesStore.DefaultSteerLeftKey, sanitized.SteerLeftKey);
            Assert.AreEqual(SettingsPreferencesStore.DefaultSteerRightKey, sanitized.SteerRightKey);
            Assert.AreEqual("f", sanitized.InteractKey);

            dirty.TargetFrameRate = 15;
            dirty.FogQualityScale = 5f;
            SettingsPreferencesStore.Sanitize(dirty);
            Assert.AreEqual(SettingsPreferencesStore.MinTargetFrameRate, dirty.TargetFrameRate);
            Assert.AreEqual(SettingsPreferencesStore.MaxFogQualityScale, dirty.FogQualityScale, 1e-5f);
        }

        [Test]
        public void SaveAndLoad_RoundTripsCustomizedSettingsViaJsonUtility()
        {
            string path = Path.Combine(_tempDir, "nested", "settings.json");
            GameSettingsData original = SettingsPreferencesStore.CreateDefaults();
            original.DiagnosticHintsEnabled = false;
            original.MasterVolume = 0.65f;
            original.EngineVolume = 0.9f;
            original.FoleyVolume = 0.4f;
            original.AmbienceVolume = 0.3f;
            original.GraphicsPreset = GraphicsPreset.High;
            original.TargetFrameRate = 120;
            original.FogQualityScale = 1.35f;

            string conflict;
            Assert.IsTrue(SettingsPreferencesStore.TrySetKeyBinding(original, KeyBindingActionIds.Throttle, "upArrow", out conflict));
            Assert.IsTrue(SettingsPreferencesStore.TrySetKeyBinding(original, KeyBindingActionIds.ToggleCamera, "tab", out conflict));

            SettingsPreferencesStore.SaveToPath(path, original);
            Assert.IsTrue(File.Exists(path));

            GameSettingsData reloaded = SettingsPreferencesStore.LoadFromPath(path);

            Assert.IsFalse(reloaded.DiagnosticHintsEnabled);
            Assert.AreEqual(0.65f, reloaded.MasterVolume, 1e-5f);
            Assert.AreEqual(0.9f, reloaded.EngineVolume, 1e-5f);
            Assert.AreEqual(0.4f, reloaded.FoleyVolume, 1e-5f);
            Assert.AreEqual(0.3f, reloaded.AmbienceVolume, 1e-5f);
            Assert.AreEqual(GraphicsPreset.High, reloaded.GraphicsPreset);
            Assert.AreEqual(120, reloaded.TargetFrameRate);
            Assert.AreEqual(1.35f, reloaded.FogQualityScale, 1e-5f);
            Assert.AreEqual("upArrow", reloaded.ThrottleKey);
            Assert.AreEqual("tab", reloaded.ToggleCameraKey);
            Assert.AreEqual("a", reloaded.SteerLeftKey);
        }

        [Test]
        public void LoadFromPath_MissingOrCorruptJson_FallsBackToDefaults()
        {
            string missingPath = Path.Combine(_tempDir, "does_not_exist.json");
            GameSettingsData fromMissing = SettingsPreferencesStore.LoadFromPath(missingPath);
            Assert.IsTrue(fromMissing.DiagnosticHintsEnabled);
            Assert.AreEqual(GraphicsPreset.Medium, fromMissing.GraphicsPreset);
            Assert.AreEqual("w", fromMissing.ThrottleKey);

            string corruptPath = Path.Combine(_tempDir, "corrupt.json");
            File.WriteAllText(corruptPath, "{ not valid json !!!");
            GameSettingsData fromCorrupt = SettingsPreferencesStore.LoadFromPath(corruptPath);
            Assert.IsTrue(fromCorrupt.DiagnosticHintsEnabled);
            Assert.AreEqual(SettingsPreferencesStore.DefaultEngineVolume, fromCorrupt.EngineVolume, 1e-5f);
            Assert.AreEqual(60, fromCorrupt.TargetFrameRate);
            Assert.AreEqual("space", fromCorrupt.PedalAssistKey);

            string emptyPath = Path.Combine(_tempDir, "empty.json");
            File.WriteAllText(emptyPath, "   ");
            GameSettingsData fromEmpty = SettingsPreferencesStore.LoadFromPath(emptyPath);
            Assert.AreEqual(SettingsPreferencesStore.DefaultMasterVolume, fromEmpty.MasterVolume, 1e-5f);

            string arrayPath = Path.Combine(_tempDir, "array.json");
            File.WriteAllText(arrayPath, "[1, 2, 3]");
            GameSettingsData fromArray = SettingsPreferencesStore.LoadFromPath(arrayPath);
            Assert.AreEqual(GraphicsPreset.Medium, fromArray.GraphicsPreset);
        }

        [Test]
        public void TrySetKeyBinding_RejectsDuplicatesCaseInsensitively_AndResetsCleanly()
        {
            GameSettingsData data = SettingsPreferencesStore.CreateDefaults();
            string conflict;

            bool duplicate = SettingsPreferencesStore.TrySetKeyBinding(data, KeyBindingActionIds.Throttle, "A", out conflict);
            Assert.IsFalse(duplicate);
            Assert.AreEqual(KeyBindingActionIds.SteerLeft, conflict);
            Assert.AreEqual("w", data.ThrottleKey);

            bool sameKey = SettingsPreferencesStore.TrySetKeyBinding(data, KeyBindingActionIds.Throttle, "w", out conflict);
            Assert.IsTrue(sameKey);
            Assert.AreEqual(string.Empty, conflict);

            bool rebound = SettingsPreferencesStore.TrySetKeyBinding(data, KeyBindingActionIds.Throttle, "upArrow", out conflict);
            Assert.IsTrue(rebound);
            Assert.AreEqual(string.Empty, conflict);
            Assert.AreEqual("upArrow", data.ThrottleKey);

            bool invalidAction = SettingsPreferencesStore.TrySetKeyBinding(data, "nonexistent_action", "k", out conflict);
            Assert.IsFalse(invalidAction);
            Assert.AreEqual(string.Empty, conflict);

            bool emptyKey = SettingsPreferencesStore.TrySetKeyBinding(data, KeyBindingActionIds.Choke, "   ", out conflict);
            Assert.IsFalse(emptyKey);
            Assert.AreEqual("c", data.ChokeKey);

            SettingsPreferencesStore.ResetKeyBindings(data);
            Assert.AreEqual("w", data.ThrottleKey);
            Assert.AreEqual("c", data.ChokeKey);
        }

        [Test]
        public void ApplyToSaveState_SyncsDiagnosticHintsEnabled()
        {
            SaveState save = SaveMigrator.CreateNewDefault(7u);
            GameSettingsData settings = SettingsPreferencesStore.CreateDefaults();

            settings.DiagnosticHintsEnabled = false;
            SettingsPreferencesStore.ApplyToSaveState(settings, save);
            Assert.IsFalse(save.DiagnosticHintsEnabled);

            settings.DiagnosticHintsEnabled = true;
            SettingsPreferencesStore.ApplyToSaveState(settings, save);
            Assert.IsTrue(save.DiagnosticHintsEnabled);

            Assert.DoesNotThrow(() => SettingsPreferencesStore.ApplyToSaveState(settings, null));
        }

        [Test]
        public void SettingsMenuView_UpdatesPresenterAndEmitsTypedEvents()
        {
            _root = new GameObject("SettingsMenuTestRoot");
            DiagnosticHintsPresenter presenter = _root.AddComponent<DiagnosticHintsPresenter>();
            SettingsMenuView view = _root.AddComponent<SettingsMenuView>();

            int hintsEvents = 0;
            bool lastHintsValue = true;
            int audioEvents = 0;
            AudioMixSettings lastAudio = default(AudioMixSettings);
            int graphicsEvents = 0;
            GraphicsQualitySettings lastGraphics = default(GraphicsQualitySettings);
            int keyEvents = 0;
            int appliedEvents = 0;
            GameSettingsData lastApplied = null;

            view.DiagnosticHintsToggled += enabled => { hintsEvents++; lastHintsValue = enabled; };
            view.AudioMixChanged += mix => { audioEvents++; lastAudio = mix; };
            view.GraphicsPresetChanged += gfx => { graphicsEvents++; lastGraphics = gfx; };
            view.KeyBindingsChanged += _ => { keyEvents++; };
            view.SettingsApplied += applied => { appliedEvents++; lastApplied = applied; };

            GameSettingsData initial = SettingsPreferencesStore.CreateDefaults();
            initial.DiagnosticHintsEnabled = false;
            view.Bind(initial, presenter);
            Assert.IsFalse(presenter.HintsEnabled, "Bind should sync initial DiagnosticHintsEnabled onto the presenter.");

            view.SetDiagnosticHints(true);
            Assert.AreEqual(1, hintsEvents);
            Assert.IsTrue(lastHintsValue);
            Assert.IsTrue(presenter.HintsEnabled);

            view.SetAudioMix(1.4f, -0.5f, 0.6f, 0.25f);
            Assert.AreEqual(1, audioEvents);
            Assert.AreEqual(1f, lastAudio.MasterVolume, 1e-5f);
            Assert.AreEqual(0f, lastAudio.EngineVolume, 1e-5f);
            Assert.AreEqual(0.6f, lastAudio.FoleyVolume, 1e-5f);
            Assert.AreEqual(0.25f, lastAudio.AmbienceVolume, 1e-5f);

            view.SetGraphicsPreset(GraphicsPreset.High, 120, 2.5f);
            Assert.AreEqual(1, graphicsEvents);
            Assert.AreEqual(GraphicsPreset.High, lastGraphics.Preset);
            Assert.AreEqual(120, lastGraphics.TargetFrameRate);
            Assert.AreEqual(SettingsPreferencesStore.MaxFogQualityScale, lastGraphics.FogQualityScale, 1e-5f);

            string conflict;
            Assert.IsFalse(view.TryRebindAction(KeyBindingActionIds.Throttle, "s", out conflict));
            Assert.AreEqual(KeyBindingActionIds.RearBrake, conflict);
            Assert.AreEqual(KeyBindingActionIds.RearBrake, view.LastConflictAction);
            Assert.AreEqual(0, keyEvents, "Rejected duplicate rebind must not emit KeyBindingsChanged.");

            Assert.IsTrue(view.TryRebindAction(KeyBindingActionIds.Throttle, "upArrow", out conflict));
            Assert.AreEqual(string.Empty, conflict);
            Assert.AreEqual(string.Empty, view.LastConflictAction);
            Assert.AreEqual(1, keyEvents);
            Assert.AreEqual("upArrow", view.CurrentSettings.ThrottleKey);

            view.Apply();
            Assert.AreEqual(1, appliedEvents);
            Assert.AreSame(view.CurrentSettings, lastApplied);
            Assert.AreEqual("upArrow", lastApplied.ThrottleKey);

            view.ResetAllToDefaults();
            Assert.AreEqual(2, hintsEvents);
            Assert.AreEqual(2, audioEvents);
            Assert.AreEqual(2, graphicsEvents);
            Assert.AreEqual(2, keyEvents);
            Assert.IsTrue(view.CurrentSettings.DiagnosticHintsEnabled);
            Assert.IsTrue(presenter.HintsEnabled);
            Assert.AreEqual(GraphicsPreset.Medium, view.CurrentSettings.GraphicsPreset);
            Assert.AreEqual(60, view.CurrentSettings.TargetFrameRate);
            Assert.AreEqual("w", view.CurrentSettings.ThrottleKey);
        }
    }
}
