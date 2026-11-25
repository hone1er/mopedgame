using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>Display-only classification of a pending wrench pull against the active fastener spec.</summary>
    public enum TorqueGaugeZone
    {
        Idle = 0,
        Under = 1,
        InSpec = 2,
        Over = 3,
        Strip = 4
    }

    /// <summary>
    /// Hold-to-torque wrench bar. Previews the pending pull; the actual outcome comes from Core via ShowResult.
    /// </summary>
    public sealed class GarageWrenchHudView : MonoBehaviour
    {
        private const int MaxTenths = 600;
        private const string LabelIdle = "";
        private const string LabelUnder = "UNDER-TORQUED";
        private const string LabelInSpec = "IN SPEC";
        private const string LabelOver = "OVER SPEC";
        private const string LabelStrip = "STRIPPING!";

        private static readonly string[] TorqueStrings;

        static GarageWrenchHudView()
        {
            TorqueStrings = new string[MaxTenths + 1];
            for (int i = 0; i <= MaxTenths; i++)
            {
                TorqueStrings[i] = (i / 10).ToString() + "." + (i % 10).ToString() + " Nm";
            }
        }

        [SerializeField] private Image _torqueFill;
        [SerializeField] private Text _fastenerLabel;
        [SerializeField] private Text _torqueText;
        [SerializeField] private Text _zoneLabel;
        [SerializeField] private Text _resultLabel;
        [SerializeField] private Color _idleColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        [SerializeField] private Color _underColor = new Color(0.95f, 0.8f, 0.2f, 1f);
        [SerializeField] private Color _inSpecColor = new Color(0.25f, 0.85f, 0.35f, 1f);
        [SerializeField] private Color _overColor = new Color(0.95f, 0.5f, 0.15f, 1f);
        [SerializeField] private Color _stripColor = new Color(0.9f, 0.15f, 0.15f, 1f);

        private string _activeFastenerId = string.Empty;
        private float _target;
        private float _tolerance;
        private float _strip;
        private bool _active;
        private TorqueGaugeZone _zone = TorqueGaugeZone.Idle;
        private bool _zoneApplied;
        private int _lastTenths = -1;

        /// <summary>True when the last ShowResult reported a stripped fastener.</summary>
        public bool LastResultStripped { get; private set; }

        /// <summary>Current preview zone.</summary>
        public TorqueGaugeZone CurrentZone { get { return _zone; } }

        /// <summary>Id of the fastener currently shown (empty when hidden).</summary>
        public string ActiveFastenerId { get { return _activeFastenerId; } }

        /// <summary>Begins showing a fastener's spec.</summary>
        public void ShowFastener(FastenerState fastener)
        {
            if (fastener == null) { Hide(); return; }
            _active = true;
            _activeFastenerId = fastener.FastenerId ?? string.Empty;
            _target = fastener.TargetTorqueNm;
            _tolerance = fastener.ToleranceNm;
            _strip = fastener.StripThresholdNm;
            LastResultStripped = false;
            _lastTenths = -1;
            _zoneApplied = false;
            if (_fastenerLabel != null)
            {
                _fastenerLabel.text = _activeFastenerId + " (target " + _target.ToString("0.0") + " Nm)";
            }
            if (_resultLabel != null) { _resultLabel.text = string.Empty; }
            SetAppliedTorque(0f);
        }

        /// <summary>Clears the wrench bar.</summary>
        public void Hide()
        {
            _active = false;
            _activeFastenerId = string.Empty;
            _target = 0f;
            _tolerance = 0f;
            _strip = 0f;
            _lastTenths = -1;
            _zoneApplied = false;
            if (_fastenerLabel != null) { _fastenerLabel.text = string.Empty; }
            SetAppliedTorque(0f);
        }

        /// <summary>Per-frame preview of the pending pull torque. Zero allocations.</summary>
        public void SetAppliedTorque(float torqueNm)
        {
            if (_torqueFill != null)
            {
                float denom = _strip * 1.1f;
                _torqueFill.fillAmount = denom > 0.0001f ? Mathf.Clamp01(torqueNm / denom) : 0f;
            }

            int tenths = Mathf.Clamp(Mathf.RoundToInt(torqueNm * 10f), 0, MaxTenths);
            if (tenths != _lastTenths)
            {
                _lastTenths = tenths;
                if (_torqueText != null) { _torqueText.text = TorqueStrings[tenths]; }
            }

            TorqueGaugeZone zone = Classify(torqueNm);
            if (!_zoneApplied || zone != _zone)
            {
                _zone = zone;
                _zoneApplied = true;
                if (_zoneLabel != null) { _zoneLabel.text = ZoneLabel(zone); }
                if (_torqueFill != null) { _torqueFill.color = ZoneColor(zone); }
            }
        }

        /// <summary>Shows the Core-decided wrench outcome.</summary>
        public void ShowResult(in WrenchActionResult result)
        {
            if (_resultLabel != null) { _resultLabel.text = result.Message; }
            LastResultStripped = result.Status == WrenchActionStatus.FastenerStripped;
        }

        private TorqueGaugeZone Classify(float torqueNm)
        {
            if (!_active || torqueNm <= 0.0001f) { return TorqueGaugeZone.Idle; }
            if (torqueNm > _strip) { return TorqueGaugeZone.Strip; }
            if (torqueNm > _target + _tolerance) { return TorqueGaugeZone.Over; }
            if (torqueNm < _target - _tolerance) { return TorqueGaugeZone.Under; }
            return TorqueGaugeZone.InSpec;
        }

        private static string ZoneLabel(TorqueGaugeZone zone)
        {
            switch (zone)
            {
                case TorqueGaugeZone.Under: return LabelUnder;
                case TorqueGaugeZone.InSpec: return LabelInSpec;
                case TorqueGaugeZone.Over: return LabelOver;
                case TorqueGaugeZone.Strip: return LabelStrip;
                default: return LabelIdle;
            }
        }

        private Color ZoneColor(TorqueGaugeZone zone)
        {
            switch (zone)
            {
                case TorqueGaugeZone.Under: return _underColor;
                case TorqueGaugeZone.InSpec: return _inSpecColor;
                case TorqueGaugeZone.Over: return _overColor;
                case TorqueGaugeZone.Strip: return _stripColor;
                default: return _idleColor;
            }
        }
    }
}
