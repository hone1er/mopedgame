using System.Collections.Generic;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.Tests.PlayMode;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    /// <summary>W2-3: title screen (Continue / New Game archetype picker), pause menu, Escape precedence, title gating.</summary>
    public sealed class TitlePauseMenuTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private string _tempSave;

        [SetUp]
        public void SetUp()
        {
            _tempSave = Path.Combine(Path.GetTempPath(), "moped_titletest_" + System.Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            AppCompositionRoot.SkipTitleScreen = false;
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
            if (File.Exists(_tempSave))
            {
                File.Delete(_tempSave);
            }
        }

        private static CatalogDatabase LoadCatalog()
        {
            return CatalogLoader.LoadFromDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs")));
        }

        private GameObject Spawn(string name, params System.Type[] components)
        {
            GameObject go = new GameObject(name, components);
            _spawned.Add(go);
            return go;
        }

        private AppCompositionRoot CreateRoot(out SettingsMenuView settings)
        {
            GameObject rootGo = Spawn("AppRoot_Title");
            GameSessionStateMachine session = rootGo.AddComponent<GameSessionStateMachine>();
            AppCompositionRoot app = rootGo.AddComponent<AppCompositionRoot>();
            app.BootstrapOnStart = false;
            app.AssignCoreComponents(null, session, null, null);
            GameObject settingsGo = Spawn("Settings", typeof(RectTransform));
            settings = settingsGo.AddComponent<SettingsMenuView>();
            settingsGo.SetActive(false);
            app.AssignEconomyComponents(null, null, null, null, null, settings);
            SimulationWorld world = new SimulationWorld(new SeededSimRandom(7u));
            world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, MopedScenarioFixtures.CreateStockBike(ArchetypeIds.MaxerE50));
            app.BootstrapWithWorld(world, null);
            app.SetSaveFileName(_tempSave);
            return app;
        }

        [Test]
        public void TitleView_ContinueDisabledWithoutSave_EnabledWithSummary()
        {
            TitleMenuView view = Spawn("Title").AddComponent<TitleMenuView>();
            view.Build(view.transform);
            view.Show();
            Assert.IsTrue(view.IsOpen);
            Assert.IsFalse(view.ContinueEnabled, "Continue must be disabled until a valid save exists.");

            bool continued = false;
            view.ContinueRequested += delegate { continued = true; };
            view.PressContinue();
            Assert.IsFalse(continued);

            view.SetContinueSummary(true, "Maxer E50\n$650  |  12.0 km");
            Assert.IsTrue(view.ContinueEnabled);
            Assert.That(view.ContinueSummaryText, Does.Contain("Maxer E50"));
            view.PressContinue();
            Assert.IsTrue(continued);
        }

        [Test]
        public void TitleView_NewGameListsCatalogArchetypes_AndPicksOne()
        {
            List<TitleArchetypeOption> options = SessionMenuController.BuildArchetypeOptions(LoadCatalog());
            Assert.AreEqual(3, options.Count, "The catalog ships three archetypes.");
            for (int i = 0; i < options.Count; i++)
            {
                Assert.IsNotEmpty(options[i].DisplayName);
                Assert.IsNotEmpty(options[i].Description);
                Assert.IsTrue(options[i].PriceText.StartsWith("$"));
                Assert.IsNotEmpty(options[i].StatsText);
            }

            TitleMenuView view = Spawn("Title").AddComponent<TitleMenuView>();
            view.Build(view.transform);
            view.SetArchetypes(options);
            view.Show();
            Assert.AreEqual(3, view.ArchetypeCount);
            Assert.IsFalse(view.IsPickerOpen);

            view.ShowPicker();
            Assert.IsTrue(view.IsPickerOpen);
            Assert.IsTrue(view.HandleBack(), "Back closes the picker first.");
            Assert.IsFalse(view.IsPickerOpen);
            Assert.IsFalse(view.HandleBack());

            string chosen = null;
            view.NewGameRequested += delegate(string id) { chosen = id; };
            view.ChooseArchetype(ArchetypeIds.TargaA35);
            Assert.AreEqual(ArchetypeIds.TargaA35, chosen);
        }

        [Test]
        public void SaveSummary_RejectsMissingAndGarbageFiles_AcceptsRealSave()
        {
            SaveSummary summary;
            Assert.IsFalse(SaveLoadBridge.TryReadSummary(_tempSave, null, out summary), "Missing file.");
            File.WriteAllText(_tempSave, "{ not json");
            Assert.IsFalse(SaveLoadBridge.TryReadSummary(_tempSave, null, out summary), "Garbage file.");
            File.Delete(_tempSave);

            SettingsMenuView settings;
            AppCompositionRoot app = CreateRoot(out settings);
            app.World.CashUsd = 321m;
            Assert.IsTrue(app.SaveGame());
            Assert.IsTrue(app.TryReadSaveSummary(out summary));
            Assert.AreEqual(ArchetypeIds.MaxerE50, summary.ArchetypeId);
            Assert.AreEqual(321m, summary.CashUsd);

            Dictionary<string, BikeArchetypeSpec> other = new Dictionary<string, BikeArchetypeSpec>();
            Assert.IsFalse(SaveLoadBridge.TryReadSummary(_tempSave, other, out summary), "Unknown archetype is not a valid save.");
        }

        [Test]
        public void Pause_FreezesAndResumesTimeScale_AndBlocksInput()
        {
            SettingsMenuView settings;
            AppCompositionRoot app = CreateRoot(out settings);
            SessionMenuController menus = app.EnsureMenus();
            Assert.AreEqual(1f, Time.timeScale);

            menus.HandlePausePressed();
            Assert.IsTrue(menus.IsPauseOpen);
            Assert.IsTrue(app.IsPaused);
            Assert.IsTrue(app.IsMenuOpen);
            Assert.AreEqual(0f, Time.timeScale);

            menus.HandlePausePressed();
            Assert.IsFalse(menus.IsPauseOpen);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(app.IsMenuOpen);
        }

        [Test]
        public void Escape_ClosesTopmostPanelFirst_ThenTogglesPause()
        {
            SettingsMenuView settings;
            AppCompositionRoot app = CreateRoot(out settings);
            SessionMenuController menus = app.EnsureMenus();

            // Settings opened from the pause menu: Esc closes Settings only, the game stays paused.
            menus.HandlePausePressed();
            Assert.IsTrue(menus.IsPauseOpen);
            app.OpenSettings();
            Assert.IsTrue(app.IsSettingsMenuOpen);
            menus.HandlePausePressed();
            Assert.IsFalse(app.IsSettingsMenuOpen, "Esc closes the top-most panel (Settings).");
            Assert.IsTrue(menus.IsPauseOpen, "...without also resuming.");
            Assert.AreEqual(0f, Time.timeScale);
            menus.HandlePausePressed();
            Assert.IsFalse(menus.IsPauseOpen);
            Assert.AreEqual(1f, Time.timeScale);

            // Settings opened in-game (F2): Esc closes it instead of pausing.
            app.ToggleSettingsMenu();
            Assert.IsTrue(app.IsSettingsMenuOpen);
            menus.HandlePausePressed();
            Assert.IsFalse(app.IsSettingsMenuOpen);
            Assert.IsFalse(menus.IsPauseOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void Title_EscapeClosesPickerAndNeverPauses()
        {
            SettingsMenuView settings;
            AppCompositionRoot app = CreateRoot(out settings);
            SessionMenuController menus = app.EnsureMenus();
            menus.ShowTitle();
            Assert.IsTrue(menus.IsTitleOpen);
            Assert.AreEqual(1f, Time.timeScale, "Title must not freeze the sim (zones stream in behind it).");

            menus.TitleView.ShowPicker();
            menus.HandlePausePressed();
            Assert.IsFalse(menus.TitleView.IsPickerOpen);
            menus.HandlePausePressed();
            Assert.IsTrue(menus.IsTitleOpen);
            Assert.IsFalse(menus.IsPauseOpen);

        }

        [Test]
        public void ShouldShowTitle_HonorsFlagsAndOverrides()
        {
            Assert.IsTrue(AppCompositionRoot.ShouldShowTitle(new string[0]));
            Assert.IsFalse(AppCompositionRoot.ShouldShowTitle(new[] { "-skipTitle" }));
            Assert.IsFalse(AppCompositionRoot.ShouldShowTitle(new[] { "-runTests" }));
            Assert.IsFalse(AppCompositionRoot.ShouldShowTitle(new[] { "-autoplayCapture", "out" }));
            Assert.IsTrue(AppCompositionRoot.ShouldShowTitle(new[] { "-autoplayCapture", "out", "-autoplayTitle" }));
            AppCompositionRoot.SkipTitleScreen = true;
            Assert.IsFalse(AppCompositionRoot.ShouldShowTitle(new string[0]));
        }

        [Test]
        public void PauseView_ButtonsRaiseEvents()
        {
            PauseMenuView view = Spawn("Pause").AddComponent<PauseMenuView>();
            view.Build(view.transform);
            Assert.IsFalse(view.IsOpen);
            view.Show();
            Assert.IsTrue(view.IsOpen);
            view.SetStatus("Game saved.");
            Assert.AreEqual("Game saved.", view.StatusText);
            view.Hide();
            Assert.IsFalse(view.IsOpen);
        }
    }
}
