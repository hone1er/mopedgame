using System;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Runtime-built pause menu: Resume, Settings, Save, Save &amp; Quit to Title, Quit over a dimmed backdrop. Event-driven only;
    /// keyboard/gamepad navigation uses the scene EventSystem.
    /// </summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        private RectTransform _backdrop;
        private RectTransform _card;
        private Button _resumeButton;
        private Text _statusLabel;

        public event Action ResumeRequested;
        public event Action SettingsRequested;
        public event Action SaveRequested;
        public event Action SaveAndQuitToTitleRequested;
        public event Action QuitRequested;

        public bool IsBuilt { get { return _backdrop != null; } }
        public bool IsOpen { get { return _backdrop != null && _backdrop.gameObject.activeSelf; } }
        public string StatusText { get { return _statusLabel != null ? _statusLabel.text : string.Empty; } }

        public void Build(Transform parent)
        {
            if (_backdrop != null)
            {
                return;
            }

            Font font = MenuWidgets.LoadFont();
            _backdrop = MenuWidgets.CreateOverlayCanvas(parent, "PauseMenu_Canvas", 190, new Color(0f, 0f, 0f, 0.62f));
            _card = MenuWidgets.CreateCard(_backdrop, "PauseCard", new Vector2(480f, 520f), new Color(0.08f, 0.09f, 0.12f, 0.97f), 22, 12f);
            MenuWidgets.CreateLabel(_card, "Title", "PAUSED", 40, TextAnchor.MiddleCenter, new Color(0.95f, 0.62f, 0.18f, 1f), 56f, font);
            _resumeButton = MenuWidgets.CreateButton(_card, "Btn_Resume", "Resume", new Color(0.20f, 0.52f, 0.28f, 1f), 26, 58f, font, delegate { Fire(ResumeRequested); }, out Text _);
            MenuWidgets.CreateButton(_card, "Btn_Settings", "Settings", new Color(0.26f, 0.27f, 0.32f, 1f), 26, 58f, font, delegate { Fire(SettingsRequested); }, out Text _);
            MenuWidgets.CreateButton(_card, "Btn_Save", "Save", new Color(0.22f, 0.40f, 0.62f, 1f), 26, 58f, font, delegate { Fire(SaveRequested); }, out Text _);
            MenuWidgets.CreateButton(_card, "Btn_SaveQuitTitle", "Save & Quit to Title", new Color(0.22f, 0.40f, 0.62f, 1f), 26, 58f, font, delegate { Fire(SaveAndQuitToTitleRequested); }, out Text _);
            MenuWidgets.CreateButton(_card, "Btn_Quit", "Quit", new Color(0.50f, 0.28f, 0.20f, 1f), 26, 58f, font, delegate { Fire(QuitRequested); }, out Text _);
            _statusLabel = MenuWidgets.CreateLabel(_card, "Status", string.Empty, 18, TextAnchor.MiddleCenter, new Color(0.70f, 0.90f, 1f, 1f), 28f, font);
            _backdrop.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (_backdrop == null)
            {
                return;
            }

            _backdrop.gameObject.SetActive(true);
            SetStatus(string.Empty);
            SetCardVisible(true);
        }

        public void Hide()
        {
            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>Hides/shows the card while the backdrop stays (Settings is layered on top).</summary>
        public void SetCardVisible(bool visible)
        {
            if (_card == null)
            {
                return;
            }

            _card.gameObject.SetActive(visible);
            if (visible)
            {
                MenuWidgets.Focus(_resumeButton);
            }
        }

        public void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text ?? string.Empty;
            }
        }

        private static void Fire(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }
    }
}
