using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>
    /// Optional "Mechanic Diagnostic Hints" presenter. Picks one hint from static tables per frame
    /// without allocating; the label is only written when the selected hint changes.
    /// </summary>
    public sealed class DiagnosticHintsPresenter : MonoBehaviour
    {
        /// <summary>Hint id reported for the lean-ping telemetry hint.</summary>
        public const string TelemetryPingId = "telemetry_ping";
        /// <summary>Hint id reported for the four-stroking telemetry hint.</summary>
        public const string TelemetryFourStrokeId = "telemetry_four_stroke";
        /// <summary>Hint id reported for the running-hot telemetry hint.</summary>
        public const string TelemetryHotId = "telemetry_hot";
        /// <summary>Hint id reported for the reserve telemetry hint.</summary>
        public const string TelemetryReserveId = "telemetry_reserve";

        private const string HintHardSeize = "Hard seize: scored piston. Tow or pedal home — garage top-end rebuild needed.";
        private const string HintSoftSeize = "Engine seized hot — pull the decompressor, let it cool, then jet richer.";
        private const string HintThrownChain = "Chain's off: use a spare master link or re-seat it with the needle-nose.";
        private const string HintSnappedBelt = "Belt snapped: swap in a spare drive belt.";
        private const string HintFouledPlug = "Sputtering & dead plug: pull it with the plug wrench — swap a spare or wire-brush it.";
        private const string HintCloggedJet = "Starving for fuel: pull the main jet and blow it out with carb cleaner.";
        private const string HintCondenser = "Heat-soaked condenser: let it cool, or swap a spare condenser.";
        private const string HintPoints = "Points drifted: re-gap them with the feeler gauge.";
        private const string HintAirLeak = "Air leak: re-seat the intake and tighten the manifold bolts.";
        private const string HintExhaustLoose = "Exhaust rattling loose: tighten the flange nuts or fit a spare flange nut.";
        private const string HintPing = "Lean ping! Back off throttle and richen the main jet.";
        private const string HintFourStroke = "Four-stroking blubber: running rich — try a smaller main jet.";
        private const string HintHot = "Running hot — ease off the throttle and let airflow cool the cylinder.";
        private const string HintReserve = "On reserve — find premix soon.";

        // Priority order: most severe first.
        private static readonly string[] PriorityIds =
        {
            FaultIds.HardSeize,
            FaultIds.SoftSeize,
            FaultIds.ThrownChain,
            FaultIds.SnappedBelt,
            FaultIds.FouledPlug,
            FaultIds.CloggedJet,
            FaultIds.CondenserHeatSoak,
            FaultIds.PointsDrift,
            FaultIds.AirLeak,
            FaultIds.ExhaustLoose
        };

        [SerializeField] private Text _hintLabel;

        private bool _hintsEnabled = true;
        private string _currentHint = string.Empty;
        private string _currentHintId = string.Empty;
        private bool _applied;

        /// <summary>Whether hints are shown. When false the hint text is cleared.</summary>
        public bool HintsEnabled
        {
            get { return _hintsEnabled; }
            set
            {
                _hintsEnabled = value;
                if (!value) { Apply(string.Empty, string.Empty); }
            }
        }

        /// <summary>Currently displayed hint (empty when none).</summary>
        public string CurrentHint { get { return _currentHint; } }

        /// <summary>Fault id or telemetry_* id of the current hint (empty when none).</summary>
        public string CurrentHintId { get { return _currentHintId; } }

        /// <summary>Maps a fault id to its hint, or empty string.</summary>
        public static string GetFaultHint(string faultId)
        {
            switch (faultId)
            {
                case FaultIds.HardSeize: return HintHardSeize;
                case FaultIds.SoftSeize: return HintSoftSeize;
                case FaultIds.ThrownChain: return HintThrownChain;
                case FaultIds.SnappedBelt: return HintSnappedBelt;
                case FaultIds.FouledPlug: return HintFouledPlug;
                case FaultIds.CloggedJet: return HintCloggedJet;
                case FaultIds.CondenserHeatSoak: return HintCondenser;
                case FaultIds.PointsDrift: return HintPoints;
                case FaultIds.AirLeak: return HintAirLeak;
                case FaultIds.ExhaustLoose: return HintExhaustLoose;
                default: return string.Empty;
            }
        }

        /// <summary>Selects and displays one hint. Zero allocations.</summary>
        public void Present(in EngineTelemetrySnapshot t, IReadOnlyList<FaultState> activeFaults)
        {
            if (!_hintsEnabled)
            {
                Apply(string.Empty, string.Empty);
                return;
            }

            if (activeFaults != null && activeFaults.Count > 0)
            {
                for (int p = 0; p < PriorityIds.Length; p++)
                {
                    string priorityId = PriorityIds[p];
                    for (int i = 0; i < activeFaults.Count; i++)
                    {
                        FaultState fault = activeFaults[i];
                        if (fault != null && string.Equals(fault.FaultId, priorityId, System.StringComparison.Ordinal))
                        {
                            Apply(GetFaultHint(priorityId), priorityId);
                            return;
                        }
                    }
                }
            }

            if (t.IsDetonating || t.ThermalZone == ThermalZone.PingWarning)
            {
                Apply(HintPing, TelemetryPingId);
            }
            else if (t.IsFourStroking)
            {
                Apply(HintFourStroke, TelemetryFourStrokeId);
            }
            else if (t.ThermalZone == ThermalZone.RunningHot)
            {
                Apply(HintHot, TelemetryHotId);
            }
            else if (t.IsReserveActive)
            {
                Apply(HintReserve, TelemetryReserveId);
            }
            else
            {
                Apply(string.Empty, string.Empty);
            }
        }

        private void Apply(string hint, string id)
        {
            if (_applied && ReferenceEquals(hint, _currentHint))
            {
                return;
            }
            _applied = true;
            _currentHint = hint;
            _currentHintId = id;
            if (_hintLabel != null)
            {
                _hintLabel.text = hint;
            }
        }
    }
}
