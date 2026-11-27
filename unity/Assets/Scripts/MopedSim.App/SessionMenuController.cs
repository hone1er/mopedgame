using System.Collections.Generic;
using System.Globalization;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.UI;
using UnityEngine;

namespace MopedSim.App
{
    /// <summary>
    /// Owns the title screen and pause menu (runtime-built MopedSim.UI views) and the Escape/Start precedence rules. Presentation
    /// and flow only: every save/session decision is dispatched back to <see cref="AppCompositionRoot"/>, which owns the Core world.
    /// Pausing sets <c>Time.timeScale = 0</c> (Core ticks take explicit dt, so determinism is untouched) and pauses audio.
    /// Event-driven; <see cref="AppCompositionRoot"/> forwards the pause press, so there is no per-frame work here.
    /// </summary>
    public sealed class SessionMenuController : MonoBehaviour
    {
        private AppCompositionRoot _app;
        private TitleMenuView _title;
        private PauseMenuView _pause;
        private bool _titleOpen;
        private bool _pauseOpen;

        public bool IsTitleOpen { get { return _titleOpen; } }
        public bool IsPauseOpen { get { return _pauseOpen; } }

        /// <summary>True while a menu owns the screen (gameplay hotkeys and ride/garage input are ignored).</summary>
        public bool IsBlockingInput { get { return _titleOpen || _pauseOpen; } }

        public TitleMenuView Title { get { return _title; } }
        public PauseMenuView Pause { get { return _pause; } }

        public void Initialize(AppCompositionRoot app)
        {
            _app = app;
        }

        // ------------------------------------------------------------------ Title

        public void ShowTitle()
        {
            if (_app == null || _titleOpen)
            {
                return;
            }

            if (_title == null)
            {
                _title = gameObject.AddComponent<TitleMenuView>();
                _title.Build(transform);
                _title.NewGameRequested += OnNewGame;
                _title.ContinueRequested += OnContinue;
                _title.SettingsRequested += OnMenuSettings;
                _title.QuitRequested += OnQuit;
            }

            _title.SetArchetypes(BuildArchetypeOptions(_app.Catalog));
            RefreshContinue();
            _titleOpen = true;
            _app.SetMenuInputBlocked(true);
            _title.Show();
        }

        public void HideTitle()
        {
            if (_title != null)
            {
                _title.Hide();
            }

            _titleOpen = false;
            _app.SetMenuInputBlocked(IsBlockingInput);
        }

        private void RefreshContinue()
        {
            SaveSummary summary;
            CatalogDatabase db = _app.Catalog;
            bool valid = _app.TryReadSaveSummary(out summary);
            string text = string.Empty;
            if (valid)
            {
                BikeArchetypeSpec spec;
                string bike = db != null && db.Archetypes.TryGetValue(summary.ArchetypeId, out spec) ? spec.DisplayName : summary.ArchetypeId;
                if (!string.IsNullOrEmpty(summary.Nickname) && !string.Equals(summary.Nickname, bike, System.StringComparison.Ordinal))
                {
                    bike = summary.Nickname + " (" + bike + ")";
                }

                text = bike + "\n$" + summary.CashUsd.ToString("N0", CultureInfo.InvariantCulture)
                    + "  |  " + summary.OdometerKm.ToString("N1", CultureInfo.InvariantCulture) + " km";
            }

            _title.SetContinueSummary(valid, text);
        }

        /// <summary>Formats the catalog archetypes for the picker (data comes from archetypes.json via MopedSim.Data; no sim math).</summary>
        public static List<TitleArchetypeOption> BuildArchetypeOptions(CatalogDatabase db)
        {
            List<TitleArchetypeOption> list = new List<TitleArchetypeOption>(4);
            if (db == null)
            {
                return list;
            }

            IReadOnlyList<BikeArchetypeSpec> specs = db.ArchetypeList;
            for (int i = 0; i < specs.Count; i++)
            {
                BikeArchetypeSpec spec = specs[i];
                ArchetypeMetadata meta;
                db.ArchetypeMeta.TryGetValue(spec.Id, out meta);
                TitleArchetypeOption o = new TitleArchetypeOption();
                o.Id = spec.Id;
                o.DisplayName = spec.DisplayName;
                if (meta != null)
                {
                    o.Description = meta.Description;
                    o.PriceText = "$" + meta.BasePriceUsd.ToString("N0", CultureInfo.InvariantCulture);
                    o.StatsText = "Dry mass " + meta.CatalogDryMassKg.ToString("0.#", CultureInfo.InvariantCulture) + " kg\n"
                        + "Tank " + meta.FuelCapacityLiters.ToString("0.#", CultureInfo.InvariantCulture) + " L\n"
                        + "Wheelbase " + spec.WheelbaseM.ToString("0.00", CultureInfo.InvariantCulture) + " m\n"
                        + "Tool roll " + meta.DefaultToolRollSlots.ToString(CultureInfo.InvariantCulture) + " slots\n"
                        + meta.DrivetrainArchitecture;
                }

                list.Add(o);
            }

            return list;
        }

        private void OnNewGame(string archetypeId)
        {
            _app.StartNewGame(archetypeId);
        }

        private void OnContinue()
        {
            _app.ContinueGame();
        }

        // ------------------------------------------------------------------ Pause

        /// <summary>
        /// Escape / gamepad Start. Closes the top-most panel first (Settings, then Shop / Swap Meet, then the title's archetype
        /// picker, then the pause menu); otherwise opens the pause menu from the Garage or Ride.
        /// </summary>
        public void HandlePausePressed()
        {
            if (_app == null)
            {
                return;
            }

            if (_app.IsSettingsMenuOpen)
            {
                _app.CloseSettings();
                return;
            }

            if (_app.CloseTransientPanels())
            {
                return;
            }

            if (_titleOpen)
            {
                _title.HandleBack();
                return;
            }

            if (_pauseOpen)
            {
                Resume();
                return;
            }

            if (_app.Session != null && (_app.Session.CurrentState == GameSessionState.Garage || _app.Session.CurrentState == GameSessionState.Ride))
            {
                Pause();
            }
        }

        public void Pause()
        {
            if (_pauseOpen || _titleOpen || _app == null)
            {
                return;
            }

            if (_pause == null)
            {
                _pause = gameObject.AddComponent<PauseMenuView>();
                _pause.Build(transform);
                _pause.ResumeRequested += Resume;
                _pause.SettingsRequested += OnMenuSettings;
                _pause.SaveRequested += OnSave;
                _pause.SaveAndQuitToTitleRequested += OnSaveAndQuitToTitle;
                _pause.QuitRequested += OnQuit;
            }

            _pauseOpen = true;
            _app.SetMenuInputBlocked(true);
            _pause.Show();
        }

        public void Resume()
        {
            if (!_pauseOpen)
            {
                return;
            }

            _pauseOpen = false;
            _pause.Hide();
            _app.SetMenuInputBlocked(IsBlockingInput);
        }

        private void OnSave()
        {
            bool ok = _app.SaveGame();
            _pause.SetStatus(ok ? "Game saved." : "Save failed (see log).");
        }

        private void OnSaveAndQuitToTitle()
        {
            if (!_app.SaveGame())
            {
                _pause.SetStatus("Save failed (see log).");
                return;
            }

            _pauseOpen = false;
            _app.SetMenuInputBlocked(false);
            _app.ReturnToTitle();
        }

        private void OnQuit()
        {
            _app.QuitApplication();
        }

        // ------------------------------------------------------------------ Settings layering

        private void OnMenuSettings()
        {
            _app.OpenSettings();
        }

        /// <summary>Settings panel opened: hide the menu cards (backdrop stays) so the panel is the only focus target.</summary>
        public void OnSettingsOpened()
        {
            if (_titleOpen && _title != null)
            {
                _title.SetCardsVisible(false);
            }

            if (_pauseOpen && _pause != null)
            {
                _pause.SetCardVisible(false);
            }
        }

        /// <summary>Settings panel closed: bring the owning menu's cards (and focus) back.</summary>
        public void OnSettingsClosed()
        {
            if (_titleOpen && _title != null)
            {
                _title.SetCardsVisible(true);
            }

            if (_pauseOpen && _pause != null)
            {
                _pause.SetCardVisible(true);
            }
        }
    }
}
