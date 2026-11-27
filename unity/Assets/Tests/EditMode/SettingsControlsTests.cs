using System;
using System.IO;
using MopedSim.App;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace MopedSim.Tests.EditMode
{
    /// <summary>
    /// Unit 7 coverage: Controls section rebinding (keyboard + gamepad) in SettingsPreferencesStore, SettingsMenuView
    /// capture state machine, persistence/migration, and InputAdapter honouring rebound gamepad buttons.
    /// </summary>
    public sealed class SettingsControlsTests
    {
        private GameObject _host;
        private string _tempDir;
        private Gamepad _gamepad;
        private Keyboard _keyboard;

        private InputSettings.UpdateMode _savedUpdateMode;

        [SetUp]
        public void SetUp()
        {
            _savedUpdateMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            _tempDir = Path.Combine(Path.GetTempPath(), "mopedgame_controls_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            InputSystem.settings.updateMode = _savedUpdateMode;
            if (_gamepad != null)
            {
                InputSystem.RemoveDevice(_gamepad);
                _gamepad = null;
            }

            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }

            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        // ------------------------------------------------------------ Store

        [Test]
        public void GamepadDefaults_MatchLegacyHardcodedMapping()
        {
            GameSettingsData d = SettingsPreferencesStore.CreateDefaults();
            Assert.AreEqual("RightTrigger", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Throttle));
            Assert.AreEqual("LeftTrigger", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.FrontBrake));
            Assert.AreEqual("East", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.RearBrake));
            Assert.AreEqual("South", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.PedalAssist));
            Assert.AreEqual("LeftStick", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.AeroTuck));
            Assert.AreEqual("DpadUp", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Choke));
            Assert.AreEqual("LeftShoulder", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Decompressor));
            Assert.AreEqual("West", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Interact));
            Assert.AreEqual("Select", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.ToggleCamera));
            Assert.AreEqual(string.Empty, SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.SteerLeft), "Steering is fixed to the stick.");
        }

        [Test]
        public void TrySetGamepadBinding_RejectsDuplicatesStartAndUnknown_AcceptsFreeButton()
        {
            GameSettingsData d = SettingsPreferencesStore.CreateDefaults();
            string conflict;

            Assert.IsFalse(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "south", out conflict));
            Assert.AreEqual(KeyBindingActionIds.PedalAssist, conflict);
            Assert.AreEqual("DpadUp", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Choke));

            Assert.IsFalse(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "North", out conflict), "North is the Garage/Ride toggle.");
            Assert.IsFalse(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "Start", out conflict));
            Assert.AreEqual(SettingsPreferencesStore.ReservedConflict, conflict);
            Assert.IsFalse(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "Bogus", out conflict));
            Assert.IsFalse(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.SteerLeft, "RightStick", out conflict));

            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "rightStick", out conflict));
            Assert.AreEqual("RightStick", SettingsPreferencesStore.GetGamepadBinding(d, KeyBindingActionIds.Choke));

            // Rebinding an action to its own current button is not a conflict.
            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Choke, "RightStick", out conflict));
        }

        [Test]
        public void TrySetKeyBinding_RejectsReservedSessionHotkeys()
        {
            GameSettingsData d = SettingsPreferencesStore.CreateDefaults();
            string conflict;
            Assert.IsFalse(SettingsPreferencesStore.TrySetKeyBinding(d, KeyBindingActionIds.Throttle, "F2", out conflict));
            Assert.AreEqual(SettingsPreferencesStore.ReservedConflict, conflict);
            Assert.IsFalse(SettingsPreferencesStore.TrySetKeyBinding(d, KeyBindingActionIds.Throttle, "Tab", out conflict));
            Assert.AreEqual("w", d.ThrottleKey);
        }

        [Test]
        public void GamepadBindings_PersistRoundTrip_AndResetRestoresDefaults()
        {
            GameSettingsData d = SettingsPreferencesStore.CreateDefaults();
            string conflict;
            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Throttle, "RightShoulder", out conflict));
            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(d, KeyBindingActionIds.Interact, "RightStick", out conflict));

            string path = Path.Combine(_tempDir, "settings.json");
            SettingsPreferencesStore.SaveToPath(path, d);
            GameSettingsData loaded = SettingsPreferencesStore.LoadFromPath(path);
            Assert.AreEqual("RightShoulder", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.Throttle));
            Assert.AreEqual("RightStick", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.Interact));

            GameSettingsData clone = d.Clone();
            clone.GamepadBindings[0] = "South";
            Assert.AreEqual("RightShoulder", d.GamepadBindings[0], "Clone must deep-copy the gamepad array.");

            SettingsPreferencesStore.CopyDefaultsInto(loaded);
            Assert.AreEqual("RightTrigger", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.Throttle));
            Assert.AreEqual("West", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.Interact));
        }

        [Test]
        public void LoadFromPath_OlderPrefsWithoutGamepadFields_MigrateToDefaults()
        {
            string path = Path.Combine(_tempDir, "old.json");
            File.WriteAllText(path, "{\"DiagnosticHintsEnabled\":false,\"MasterVolume\":0.5,\"ThrottleKey\":\"i\"}");
            GameSettingsData loaded = SettingsPreferencesStore.LoadFromPath(path);

            Assert.IsFalse(loaded.DiagnosticHintsEnabled);
            Assert.AreEqual("i", loaded.ThrottleKey);
            Assert.IsNotNull(loaded.GamepadBindings);
            Assert.AreEqual(GamepadBindingActionIds.All.Count, loaded.GamepadBindings.Length);
            Assert.AreEqual("RightTrigger", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.Throttle));
            Assert.AreEqual("Select", SettingsPreferencesStore.GetGamepadBinding(loaded, KeyBindingActionIds.ToggleCamera));
        }

        [Test]
        public void Sanitize_ReplacesInvalidAndDuplicateGamepadEntries()
        {
            GameSettingsData d = SettingsPreferencesStore.CreateDefaults();
            d.GamepadBindings = new string[] { "Nope", "LeftTrigger" };
            SettingsPreferencesStore.Sanitize(d);
            Assert.AreEqual(GamepadBindingActionIds.All.Count, d.GamepadBindings.Length);
            Assert.AreEqual("RightTrigger", d.GamepadBindings[0]);
            Assert.AreEqual("LeftTrigger", d.GamepadBindings[1]);

            d.GamepadBindings[2] = "LeftTrigger";
            SettingsPreferencesStore.Sanitize(d);
            Assert.AreEqual("East", d.GamepadBindings[2], "Duplicate gamepad assignments reset to defaults.");
        }

        // ------------------------------------------------------------ View

        private SettingsMenuView CreateBuiltView()
        {
            _host = new GameObject("SettingsControlsHost", typeof(RectTransform));
            SettingsMenuView view = _host.AddComponent<SettingsMenuView>();
            view.Bind(SettingsPreferencesStore.CreateDefaults(), null);
            view.BuildDefaultLayout((RectTransform)_host.transform);
            return view;
        }

        private Button FindButton(string name)
        {
            Button[] buttons = _host.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name == name)
                {
                    return buttons[i];
                }
            }

            return null;
        }

        [Test]
        public void ControlsSection_BuildsOneRowPerActionWithCurrentBindings_AndOpensViaButton()
        {
            SettingsMenuView view = CreateBuiltView();
            Assert.IsFalse(view.ControlsPanelOpen);
            for (int i = 0; i < KeyBindingActionIds.All.Count; i++)
            {
                Assert.IsNotNull(FindButton("Btn_Key_" + KeyBindingActionIds.All[i]), KeyBindingActionIds.All[i]);
                Assert.IsNotNull(FindButton("Btn_Pad_" + KeyBindingActionIds.All[i]), KeyBindingActionIds.All[i]);
            }

            Assert.AreEqual("W", FindButton("Btn_Key_throttle").GetComponentInChildren<Text>(true).text);
            Assert.AreEqual("Right Trigger", FindButton("Btn_Pad_throttle").GetComponentInChildren<Text>(true).text);
            Assert.IsFalse(FindButton("Btn_Pad_steer_left").interactable, "Steering stays on the stick.");

            FindButton("Btn_Controls").onClick.Invoke();
            Assert.IsTrue(view.ControlsPanelOpen);
            FindButton("Btn_ControlsBack").onClick.Invoke();
            Assert.IsFalse(view.ControlsPanelOpen);
        }

        [Test]
        public void Capture_StateMachine_BeginCommitCancelAndConflicts()
        {
            SettingsMenuView view = CreateBuiltView();
            int changes = 0;
            int stateEvents = 0;
            view.KeyBindingsChanged += delegate { changes++; };
            view.CaptureStateChanged += delegate { stateEvents++; };

            Assert.IsFalse(view.BeginCapture("bogus", BindingColumn.Keyboard));
            Assert.IsFalse(view.BeginCapture(KeyBindingActionIds.SteerLeft, BindingColumn.Gamepad));
            Assert.IsFalse(view.IsCapturing);
            Assert.IsFalse(view.TryCommitCapture("j"), "Commit without capture is ignored.");

            // Click the row button: enters capture mode and shows the prompt.
            view.OpenControlsPanel();
            FindButton("Btn_Key_throttle").onClick.Invoke();
            Assert.IsTrue(view.IsCapturing);
            Assert.AreEqual(KeyBindingActionIds.Throttle, view.CaptureActionId);
            Assert.AreEqual(BindingColumn.Keyboard, view.CaptureColumn);
            StringAssert.Contains("press", FindButton("Btn_Key_throttle").GetComponentInChildren<Text>(true).text);

            // Escape equivalent: cancel leaves binding unchanged.
            view.CancelCapture();
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("w", view.CurrentSettings.ThrottleKey);
            Assert.AreEqual(0, changes);

            // Conflict: rebinding throttle to 'a' (steer_left) is rejected with a visible message.
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            Assert.IsFalse(view.TryCommitCapture("A"));
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual(KeyBindingActionIds.SteerLeft, view.LastConflictAction);
            StringAssert.Contains("Steer Left", view.ControlsStatusText);
            Assert.AreEqual("w", view.CurrentSettings.ThrottleKey);

            // Reserved key rejected.
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            Assert.IsFalse(view.TryCommitCapture("F1"));
            StringAssert.Contains("reserved", view.ControlsStatusText);

            // Success.
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            Assert.IsTrue(view.TryCommitCapture("I"));
            Assert.AreEqual("I", view.CurrentSettings.ThrottleKey);
            Assert.AreEqual(1, changes);
            Assert.AreEqual("I", FindButton("Btn_Key_throttle").GetComponentInChildren<Text>(true).text);
            Assert.IsTrue(stateEvents >= 8, "Every begin/end raises CaptureStateChanged.");
        }

        [Test]
        public void Capture_GamepadColumn_ConflictRejectedAndRebindApplied_AndResetRestores()
        {
            SettingsMenuView view = CreateBuiltView();
            int changes = 0;
            view.KeyBindingsChanged += delegate { changes++; };

            view.BeginCapture(KeyBindingActionIds.Choke, BindingColumn.Gamepad);
            Assert.IsFalse(view.TryCommitCapture("South"));
            Assert.AreEqual(KeyBindingActionIds.PedalAssist, view.LastConflictAction);
            StringAssert.Contains("Pedal Assist", view.ControlsStatusText);
            Assert.AreEqual("DpadUp", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));

            view.BeginCapture(KeyBindingActionIds.Choke, BindingColumn.Gamepad);
            Assert.IsTrue(view.TryCommitCapture("RightStick"));
            Assert.AreEqual("RightStick", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));
            Assert.AreEqual(1, changes);
            Assert.AreEqual("Right Stick Click", FindButton("Btn_Pad_choke").GetComponentInChildren<Text>(true).text);

            string conflict;
            Assert.IsTrue(view.TryRebindAction(KeyBindingActionIds.Throttle, "i", out conflict));

            view.OpenControlsPanel();
            FindButton("Btn_ResetControls").onClick.Invoke();
            Assert.AreEqual("DpadUp", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));
            Assert.AreEqual("w", view.CurrentSettings.ThrottleKey);
            Assert.AreEqual(3, changes, "Reset to defaults notifies listeners.");
            Assert.AreEqual("W", FindButton("Btn_Key_throttle").GetComponentInChildren<Text>(true).text);
        }

        [Test]
        public void ResetAllToDefaults_CancelsCaptureAndRestoresGamepad()
        {
            SettingsMenuView view = CreateBuiltView();
            string conflict;
            Assert.IsTrue(view.TryRebindGamepadAction(KeyBindingActionIds.Choke, "RightStick", out conflict));
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            view.ResetAllToDefaults();
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("DpadUp", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));
        }

        // ------------------------------------------------------------ InputAdapter

        private InputAdapter CreateAdapter()
        {
            _host = new GameObject("InputAdapterHost", typeof(RectTransform));
            return _host.AddComponent<InputAdapter>();
        }

        private static void Press(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(button));
            InputSystem.Update();
        }

        [Test]
        public void InputAdapter_DefaultGamepadMapping_Unchanged()
        {
            InputAdapter input = CreateAdapter();
            _gamepad = InputSystem.AddDevice<Gamepad>();

            Press(_gamepad, GamepadButton.South);
            Assert.IsTrue(input.SampleInputs().PedalAssist);
            Press(_gamepad, GamepadButton.East);
            Assert.AreEqual(1f, input.SampleInputs().RearBrake, 1e-6f);
            Press(_gamepad, GamepadButton.DpadUp);
            Assert.IsTrue(input.SampleInputs().Choke);
        }

        [Test]
        public void InputAdapter_HonoursReboundGamepadButtons()
        {
            InputAdapter input = CreateAdapter();
            _gamepad = InputSystem.AddDevice<Gamepad>();

            GameSettingsData settings = SettingsPreferencesStore.CreateDefaults();
            string conflict;
            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(settings, KeyBindingActionIds.PedalAssist, "RightStick", out conflict));
            Assert.IsTrue(SettingsPreferencesStore.TrySetGamepadBinding(settings, KeyBindingActionIds.Throttle, "RightShoulder", out conflict));
            Assert.AreEqual(0, input.ApplyKeyBindings(settings));
            Assert.AreEqual(GamepadButton.RightStick, input.PedalAssistPadButton);

            Press(_gamepad, GamepadButton.RightStick);
            Assert.IsTrue(input.SampleInputs().PedalAssist, "Rebound button now triggers pedal assist.");

            Press(_gamepad, GamepadButton.South);
            Assert.IsFalse(input.SampleInputs().PedalAssist, "Old default button no longer triggers it.");

            Press(_gamepad, GamepadButton.RightShoulder);
            Assert.AreEqual(1f, input.SampleInputs().Throttle, 1e-6f);

            input.ResetDefaultKeyBindings();
            Assert.AreEqual(GamepadButton.South, input.PedalAssistPadButton);
            Press(_gamepad, GamepadButton.South);
            Assert.IsTrue(input.SampleInputs().PedalAssist);
        }

        [Test]
        public void InputAdapter_ApplyKeyBindings_LegacySettingsWithoutGamepadArray_UsesDefaults()
        {
            InputAdapter input = CreateAdapter();
            GameSettingsData settings = SettingsPreferencesStore.CreateDefaults();
            settings.GamepadBindings = null;
            Assert.AreEqual(0, input.ApplyKeyBindings(settings));
            Assert.AreEqual(GamepadButton.RightTrigger, input.ThrottlePadButton);
            Assert.AreEqual(GamepadButton.Select, input.ToggleCameraPadButton);
        }

        private void ReleaseAll()
        {
            if (_gamepad != null)
            {
                InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            }

            if (_keyboard != null)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            }

            InputSystem.Update();
        }

        [Test]
        public void PollBindingCapture_ArmsAfterRelease_ThenCommitsOrCancels()
        {
            SettingsMenuView view = CreateBuiltView();
            InputAdapter input = _host.AddComponent<InputAdapter>();
            _gamepad = InputSystem.AddDevice<Gamepad>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            ReleaseAll();

            // The activating button is still held when capture begins: it must not be captured, even after repeated polls.
            Press(_gamepad, GamepadButton.South);
            view.BeginCapture(KeyBindingActionIds.Choke, BindingColumn.Gamepad);
            Assert.IsTrue(input.PollBindingCapture(view));
            Assert.IsTrue(input.PollBindingCapture(view));
            Assert.IsTrue(view.IsCapturing);

            // Release arms capture; the next press (RightStick is free) commits.
            ReleaseAll();
            Assert.IsTrue(input.PollBindingCapture(view));
            Assert.IsTrue(view.IsCapturing);
            Press(_gamepad, GamepadButton.RightStick);
            Assert.IsTrue(input.PollBindingCapture(view));
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("RightStick", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));

            // A taken button is rejected with a visible message naming the owner.
            ReleaseAll();
            view.BeginCapture(KeyBindingActionIds.Choke, BindingColumn.Gamepad);
            input.PollBindingCapture(view);
            Press(_gamepad, GamepadButton.Select);
            input.PollBindingCapture(view);
            Assert.IsFalse(view.IsCapturing);
            StringAssert.Contains("Toggle Camera", view.ControlsStatusText);

            // Start cancels without binding.
            ReleaseAll();
            view.BeginCapture(KeyBindingActionIds.Choke, BindingColumn.Gamepad);
            input.PollBindingCapture(view);
            Press(_gamepad, GamepadButton.Start);
            input.PollBindingCapture(view);
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("RightStick", SettingsPreferencesStore.GetGamepadBinding(view.CurrentSettings, KeyBindingActionIds.Choke));

            // Keyboard column: a key press binds; Escape cancels; gamepad presses are ignored in the keyboard column.
            ReleaseAll();
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            input.PollBindingCapture(view);
            Press(_gamepad, GamepadButton.East);
            input.PollBindingCapture(view);
            Assert.IsTrue(view.IsCapturing);
            ReleaseAll();
            input.PollBindingCapture(view);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
            InputSystem.Update();
            input.PollBindingCapture(view);
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("K", view.CurrentSettings.ThrottleKey);

            ReleaseAll();
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            input.PollBindingCapture(view);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            input.PollBindingCapture(view);
            Assert.IsFalse(view.IsCapturing);
            Assert.AreEqual("K", view.CurrentSettings.ThrottleKey);
        }

        [Test]
        public void JobBoardHotkeys_JAndDpadLeft_AreReservedAndNotRebindable()
        {
            Assert.IsTrue(SettingsPreferencesStore.IsReservedKey("j"));
            Assert.IsTrue(SettingsPreferencesStore.IsReservedKey("J"));
            Assert.IsNull(SettingsPreferencesStore.CanonicalGamepadButton("DpadLeft"), "D-pad Left is the fixed gig board button.");
            Assert.IsNotNull(SettingsPreferencesStore.CanonicalGamepadButton("DpadRight"));

            SettingsMenuView view = CreateBuiltView();
            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            Assert.IsFalse(view.TryCommitCapture("J"));
            StringAssert.Contains("reserved", view.ControlsStatusText);
            Assert.AreEqual("w", view.CurrentSettings.ThrottleKey);
        }

        [Test]
        public void SampleInputs_WhileCapturing_ReturnsNeutralInputsAndSuppressesHotkeys()
        {
            SettingsMenuView view = CreateBuiltView();
            InputAdapter input = _host.AddComponent<InputAdapter>();
            input.AttachBindingCapture(view);
            _keyboard = InputSystem.AddDevice<Keyboard>();

            view.BeginCapture(KeyBindingActionIds.Throttle, BindingColumn.Keyboard);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.Escape));
            InputSystem.Update();
            FrameControlInputs frame = input.SampleInputs();
            Assert.AreEqual(0f, frame.Throttle);
            Assert.IsFalse(frame.ToggleSettingsPressed, "Escape cancels capture instead of closing settings.");
        }
    }
}
