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
        private const int MaxVariatorHundredths = 600;

        /// <summary>RPM at the full end of the tach bar (display scale only).</summary>
        public const float TachMaxRpm = 8000f;
        /// <summary>RPM where the tach bar turns red and the redline tick is drawn (display cue only).</summary>
        public const float RedlineRpm = 7000f;

        private const string MixtureLean = "LEAN";
        private const string MixtureRich = "RICH";
        private const string MixtureOk = "OK";

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
        private static readonly string[] ThrottleStrings;
        private static readonly string[] VariatorStrings;

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
                AfrStrings[i - MinAfrTenths] = (i / 10).ToString() + "." + (i % 10).ToString() + " : 1";
            }

            FuelStrings = new string[MaxFuelTenths + 1];
            for (int i = 0; i <= MaxFuelTenths; i++)
            {
                FuelStrings[i] = (i / 10).ToString() + "." + (i % 10).ToString() + " L";
            }

            GearStrings = new string[] { "GEAR N", "GEAR 1", "GEAR 2" };

            ThrottleStrings = new string[101];
            for (int i = 0; i <= 100; i++) { ThrottleStrings[i] = i.ToString() + "%"; }

            VariatorStrings = new string[MaxVariatorHundredths + 1];
            for (int i = 0; i <= MaxVariatorHundredths; i++)
            {
                VariatorStrings[i] = "CVT " + (i / 100).ToString() + "." + ((i / 10) % 10).ToString() + (i % 10).ToString() + " : 1";
            }

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
        [SerializeField] private Text _throttleText;
        [SerializeField] private Text _variatorText;
        [SerializeField] private Text _mixtureText;

        [Header("Fills")]
        [SerializeField] private Image _rpmFill;
        [SerializeField] private Image _throttleFill;
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
        private int _throttle = -1;
        private int _variator = -1;
        private int _tachZone = -1;
        private int _chtZone = -1;
        private int _fuelZone = -1;
        private int _mixture = -1;

        private void Awake()
        {
            HudStyle.PrepareFill(_chtFill);
            HudStyle.PrepareFill(_staminaFill);
            HudStyle.PrepareFill(_rpmFill);
            HudStyle.PrepareFill(_throttleFill);
        }

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
            if (_rpmFill != null)
            {
                _rpmFill.fillAmount = Mathf.Clamp01(t.Rpm / TachMaxRpm);
                int tachZone = t.Rpm >= RedlineRpm ? 2 : (t.IsPowerbandActive ? 1 : 0);
                if (tachZone != _tachZone)
                {
                    _tachZone = tachZone;
                    HudStyle.SetColor(_rpmFill, tachZone == 2 ? HudStyle.Bad : (tachZone == 1 ? HudStyle.Good : HudStyle.Cool));
                }
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
            int chtZone = (int)t.ThermalZone;
            if (chtZone != _chtZone)
            {
                _chtZone = chtZone;
                Color c = ChtColor(t.ThermalZone);
                HudStyle.SetColor(_chtFill, c);
                if (_chtText != null) { HudStyle.SetColor(_chtText, c); }
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
            int fuelZone = t.IsReserveActive ? 1 : 0;
            if (fuelZone != _fuelZone)
            {
                _fuelZone = fuelZone;
                if (_fuelText != null) { HudStyle.SetColor(_fuelText, fuelZone == 1 ? HudStyle.Warn : HudStyle.TextPrimary); }
            }

            int mixture = t.IsDetonating ? 1 : (t.IsFourStroking ? 2 : 0);
            if (mixture != _mixture)
            {
                _mixture = mixture;
                SetText(_mixtureText, mixture == 1 ? MixtureLean : (mixture == 2 ? MixtureRich : MixtureOk));
                if (_mixtureText != null) { HudStyle.SetColor(_mixtureText, mixture == 0 ? HudStyle.Good : HudStyle.Warn); }
            }

            int throttle = Mathf.Clamp(Mathf.RoundToInt(t.ThrottlePosition * 100f), 0, 100);
            if (throttle != _throttle)
            {
                _throttle = throttle;
                SetText(_throttleText, ThrottleStrings[throttle]);
            }
            if (_throttleFill != null) { _throttleFill.fillAmount = Mathf.Clamp01(t.ThrottlePosition); }

            int variator = Mathf.Clamp(Mathf.RoundToInt(t.VariatorRatio * 100f), 0, MaxVariatorHundredths);
            if (variator != _variator)
            {
                _variator = variator;
                SetText(_variatorText, VariatorStrings[variator]);
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

        private static Color ChtColor(ThermalZone zone)
        {
            switch (zone)
            {
                case ThermalZone.Cold: return HudStyle.Cool;
                case ThermalZone.Warming: return HudStyle.Cool;
                case ThermalZone.Optimal: return HudStyle.Good;
                case ThermalZone.RunningHot: return HudStyle.Warn;
                default: return HudStyle.Bad;
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
