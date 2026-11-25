using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>
    /// Per-frame ride HUD. Displays telemetry values only; all strings come from precomputed tables
    /// and Text is only assigned when the displayed bucket changes (zero GC per frame).
    /// </summary>
    public sealed class RideHudView : MonoBehaviour
    {
        private const int MaxMph = 99;
        private const int MaxRpmBucket = 150;   // x100
        private const int MaxCht = 400;
        private const int MinAfrTenths = 50;
        private const int MaxAfrTenths = 250;
        private const int MaxFuelTenths = 100;
        private const int MaxStamina = 100;

        private const string StatusHardSeize = "HARD SEIZE";
        private const string StatusSoftSeize = "SOFT SEIZE";
        private const string StatusStalled = "STALLED";
        private const string StatusPinging = "PINGING — LEAN!";
        private const string StatusFourStroke = "FOUR-STROKING (RICH)";
        private const string StatusReserve = "RESERVE";
        private const string StatusPowerband = "ON THE PIPE";

        private static readonly string[] MphStrings;
        private static readonly string[] RpmStrings;
        private static readonly string[] ChtStrings;
        private static readonly string[] AfrStrings;
        private static readonly string[] FuelStrings;
        private static readonly string[] GearStrings;
        private static readonly string[] StaminaStrings;

        static RideHudView()
        {
            MphStrings = new string[MaxMph + 1];
            for (int i = 0; i <= MaxMph; i++) { MphStrings[i] = i.ToString(); }

            RpmStrings = new string[MaxRpmBucket + 1];
            for (int i = 0; i <= MaxRpmBucket; i++) { RpmStrings[i] = (i * 100).ToString(); }

            ChtStrings = new string[MaxCht + 1];
            for (int i = 0; i <= MaxCht; i++) { ChtStrings[i] = i.ToString() + "°C"; }

            AfrStrings = new string[MaxAfrTenths - MinAfrTenths + 1];
            for (int i = MinAfrTenths; i <= MaxAfrTenths; i++)
            {
                AfrStrings[i - MinAfrTenths] = (i / 10).ToString() + "." + (i % 10).ToString();
            }

            FuelStrings = new string[MaxFuelTenths + 1];
            for (int i = 0; i <= MaxFuelTenths; i++)
            {
                FuelStrings[i] = (i / 10).ToString() + "." + (i % 10).ToString() + " L";
            }

            GearStrings = new string[] { "N", "1", "2" };

            StaminaStrings = new string[MaxStamina + 1];
            for (int i = 0; i <= MaxStamina; i++) { StaminaStrings[i] = i.ToString() + "%"; }
        }

        [Header("Readouts")]
        [SerializeField] private Text _speedText;
        [SerializeField] private Text _rpmText;
        [SerializeField] private Text _chtText;
        [SerializeField] private Text _afrText;
        [SerializeField] private Text _fuelText;
        [SerializeField] private Text _gearText;
        [SerializeField] private Text _staminaText;
        [SerializeField] private Text _statusText;

        [Header("Fills")]
        [SerializeField] private Image _chtFill;
        [SerializeField] private Image _staminaFill;

        private int _mph = -1;
        private int _rpmBucket = -1;
        private int _cht = -1;
        private int _afr = -1;
        private int _fuel = -1;
        private int _gear = -1;
        private int _stamina = -1;
        private string _status;
        private int _renderCount;

        /// <summary>Last displayed whole mph (0..99).</summary>
        public int DisplayedMph { get { return _mph < 0 ? 0 : _mph; } }

        /// <summary>Last displayed status/warning line (empty when none).</summary>
        public string DisplayedStatus { get { return _status ?? string.Empty; } }

        /// <summary>Number of Render calls received.</summary>
        public int RenderCount { get { return _renderCount; } }

        /// <summary>Renders a telemetry snapshot. Zero allocations.</summary>
        public void Render(in EngineTelemetrySnapshot t)
        {
            _renderCount++;

            int mph = Mathf.Clamp(Mathf.RoundToInt(t.VehicleSpeedMph), 0, MaxMph);
            if (mph != _mph)
            {
                _mph = mph;
                SetText(_speedText, MphStrings[mph]);
            }

            int rpmBucket = Mathf.Clamp(Mathf.RoundToInt(t.Rpm / 100f), 0, MaxRpmBucket);
            if (rpmBucket != _rpmBucket)
            {
                _rpmBucket = rpmBucket;
                SetText(_rpmText, RpmStrings[rpmBucket]);
            }

            int cht = Mathf.Clamp(Mathf.RoundToInt(t.ChtCelsius), 0, MaxCht);
            if (cht != _cht)
            {
                _cht = cht;
                SetText(_chtText, ChtStrings[cht]);
            }
            if (_chtFill != null)
            {
                _chtFill.fillAmount = Mathf.InverseLerp(20f, 260f, t.ChtCelsius);
            }

            int afr = Mathf.Clamp(Mathf.RoundToInt(t.EffectiveAfr * 10f), MinAfrTenths, MaxAfrTenths);
            if (afr != _afr)
            {
                _afr = afr;
                SetText(_afrText, AfrStrings[afr - MinAfrTenths]);
            }

            int fuel = Mathf.Clamp(Mathf.RoundToInt(t.FuelLitersRemaining * 10f), 0, MaxFuelTenths);
            if (fuel != _fuel)
            {
                _fuel = fuel;
                SetText(_fuelText, FuelStrings[fuel]);
            }

            int gear = Mathf.Clamp(t.CurrentGear, 0, 2);
            if (gear != _gear)
            {
                _gear = gear;
                SetText(_gearText, GearStrings[gear]);
            }

            int stamina = Mathf.Clamp(Mathf.RoundToInt(t.RiderStamina * 100f), 0, MaxStamina);
            if (stamina != _stamina)
            {
                _stamina = stamina;
                SetText(_staminaText, StaminaStrings[stamina]);
            }
            if (_staminaFill != null)
            {
                _staminaFill.fillAmount = Mathf.Clamp01(t.RiderStamina);
            }

            string status = SelectStatus(in t);
            if (!ReferenceEquals(status, _status))
            {
                _status = status;
                SetText(_statusText, status);
            }
        }

        private static string SelectStatus(in EngineTelemetrySnapshot t)
        {
            if (t.IsHardSeized) { return StatusHardSeize; }
            if (t.IsSoftSeized) { return StatusSoftSeize; }
            if (t.IsStalled) { return StatusStalled; }
            if (t.IsDetonating) { return StatusPinging; }
            if (t.IsFourStroking) { return StatusFourStroke; }
            if (t.IsReserveActive) { return StatusReserve; }
            if (t.IsPowerbandActive) { return StatusPowerband; }
            return string.Empty;
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
