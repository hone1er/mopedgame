using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>Which key-hint set the controls banner shows.</summary>
    public enum ControlsBannerMode
    {
        Garage = 0,
        Ride = 1,
        Roadside = 2
    }

    /// <summary>
    /// Top-of-screen key-hint banner whose text follows the current mode. Only hints for real, fixed or default bindings
    /// (see InputAdapter) are listed. Text is assigned only on mode change.
    /// </summary>
    public sealed class ControlsBannerView : MonoBehaviour
    {
        public const string GarageText =
            "[LMB] Select part / hold to torque | [RMB drag] Orbit | [Scroll] Zoom | [F3] Shop | [F4] Paint | [F1/Tab] Ride | [F2/Esc] Settings";

        public const string RideText =
            "[W] Throttle | [A/D] Steer | [S] Rear brake | [Shift] Front brake | [Space] Pedal | [Ctrl] Tuck | [C] Choke | [Q] Decompressor | [V] Cam | [F1/Tab] Garage | [F2/Esc] Settings";

        public const string RoadsideText =
            "[LMB] Select part / hold to torque | [E] Interact | [F1/Tab] Back to ride | [F2/Esc] Settings";

        [SerializeField] private Text _label;

        private ControlsBannerMode _mode = (ControlsBannerMode)(-1);

        /// <summary>Currently displayed mode.</summary>
        public ControlsBannerMode Mode { get { return _mode; } }

        /// <summary>Text shown for a mode.</summary>
        public static string TextFor(ControlsBannerMode mode)
        {
            switch (mode)
            {
                case ControlsBannerMode.Ride: return RideText;
                case ControlsBannerMode.Roadside: return RoadsideText;
                default: return GarageText;
            }
        }

        /// <summary>Switches the hint set (no-op when unchanged).</summary>
        public void SetMode(ControlsBannerMode mode)
        {
            if (mode == _mode)
            {
                return;
            }

            _mode = mode;
            if (_label != null)
            {
                _label.text = TextFor(mode);
            }
        }
    }
}
