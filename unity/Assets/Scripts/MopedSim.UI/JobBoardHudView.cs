using System;
using MopedSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.UI
{
    /// <summary>
    /// Active job HUD: title, clock vs. time limit, cargo integrity, checkpoint progress, rally placement / field size, and the
    /// uncollected-reward banner. Displays a Core <see cref="JobSessionSnapshot"/> only. Every number comes from precomputed string
    /// tables and Text is assigned only when its displayed bucket changes, so unchanged frames allocate zero bytes.
    /// </summary>
    public sealed class JobBoardHudView : MonoBehaviour
    {
        public const int MaxClockSeconds = 3599;
        public const int MaxCount = 32;

        public const string RewardBannerText = "DELIVERED — COLLECT YOUR PAY";
        public const string StatusIdle = "";
        public const string StatusActive = "ON THE CLOCK";
        public const string StatusStaging = "HEAD TO THE PICKUP";
        public const string StatusCompleted = "COMPLETE";
        public const string StatusCargoDestroyed = "FAILED — CARGO DESTROYED";
        public const string StatusTimeExpired = "FAILED — OUT OF TIME";
        public const string StatusSeized = "FAILED — ENGINE SEIZED";
        public const string StatusFailed = "FAILED";
        public const string StatusAbandoned = "ABANDONED";

        private static readonly string[] ClockStrings;
        private static readonly string[] PercentStrings;
        private static readonly string[] CountStrings;

        static JobBoardHudView()
        {
            ClockStrings = new string[MaxClockSeconds + 1];
            for (int i = 0; i <= MaxClockSeconds; i++)
            {
                int m = i / 60;
                int s = i % 60;
                ClockStrings[i] = m.ToString() + ":" + (s < 10 ? "0" : string.Empty) + s.ToString();
            }

            PercentStrings = new string[101];
            for (int i = 0; i <= 100; i++)
            {
                PercentStrings[i] = i.ToString() + "%";
            }

            CountStrings = new string[MaxCount + 1];
            for (int i = 0; i <= MaxCount; i++)
            {
                CountStrings[i] = i.ToString();
            }
        }

        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _clockText;
        [SerializeField] private Text _limitText;
        [SerializeField] private Text _integrityText;
        [SerializeField] private Text _checkpointsReachedText;
        [SerializeField] private Text _checkpointCountText;
        [SerializeField] private Text _placementText;
        [SerializeField] private Text _fieldSizeText;
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _waypointLabel;
        [SerializeField] private GameObject _rewardBanner;
        [SerializeField] private Image _integrityFill;

        private void Awake()
        {
            HudStyle.PrepareFill(_integrityFill);
        }

        private readonly char[] _waypointBuffer = new char[128];
        private string _title;
        private string _status;
        private int _clock = -1;
        private int _limit = -1;
        private int _integrity = -1;
        private int _reached = -1;
        private int _count = -1;
        private int _placement = -1;
        private int _fieldSize = -1;
        private int _visible = -1;
        private int _banner = -1;
        private int _renderCount;
        private int _textWriteCount;
        private string _waypointCheckpointLabel;
        private float _waypointDistanceMeters;
        private float _waypointBearingDegrees;
        private int _waypointDistanceRounded = int.MinValue;
        private int _waypointBearingRounded = int.MinValue;
        private string _waypointFormatted = string.Empty;

        public string DisplayedTitle { get { return _title ?? string.Empty; } }
        public string DisplayedStatus { get { return _status ?? string.Empty; } }
        public int DisplayedClockSeconds { get { return _clock < 0 ? 0 : _clock; } }
        public int DisplayedLimitSeconds { get { return _limit < 0 ? 0 : _limit; } }
        public int DisplayedIntegrityPercent { get { return _integrity < 0 ? 0 : _integrity; } }
        public int DisplayedCheckpointsReached { get { return _reached < 0 ? 0 : _reached; } }
        public int DisplayedCheckpointCount { get { return _count < 0 ? 0 : _count; } }
        public int DisplayedPlacement { get { return _placement < 0 ? 0 : _placement; } }
        public int DisplayedFieldSize { get { return _fieldSize < 0 ? 0 : _fieldSize; } }
        public bool IsRewardBannerVisible { get { return _banner == 1; } }
        public bool IsPanelVisible { get { return _visible == 1; } }
        public int RenderCount { get { return _renderCount; } }

        /// <summary>Number of Text / GameObject writes performed (unchanged frames add none).</summary>
        public int TextWriteCount { get { return _textWriteCount; } }

        /// <summary>Last checkpoint label supplied to <see cref="RenderWaypoint"/>.</summary>
        public string DisplayedWaypointLabel { get { return _waypointCheckpointLabel ?? string.Empty; } }

        /// <summary>Last distance in meters supplied to <see cref="RenderWaypoint"/>.</summary>
        public float DisplayedWaypointDistanceMeters { get { return _waypointDistanceMeters; } }

        /// <summary>Last bearing in degrees supplied to <see cref="RenderWaypoint"/>.</summary>
        public float DisplayedWaypointBearingDegrees { get { return _waypointBearingDegrees; } }

        /// <summary>Assigns the optional waypoint Text label at runtime.</summary>
        public void ConfigureWaypointLabel(Text label)
        {
            _waypointLabel = label;
            if (_waypointLabel != null && !string.IsNullOrEmpty(_waypointFormatted))
            {
                SetText(_waypointLabel, _waypointFormatted);
            }
        }

        /// <summary>
        /// Renders the active checkpoint waypoint readout. Steady-state calls with unchanged label and rounded
        /// distance/bearing perform zero Text writes and allocate zero bytes of GC memory.
        /// </summary>
        public void RenderWaypoint(string checkpointLabel, float distanceMeters, float bearingDegrees)
        {
            string safeLabel = checkpointLabel ?? string.Empty;
            float safeDist = (float.IsNaN(distanceMeters) || float.IsInfinity(distanceMeters) || distanceMeters < 0f)
                ? 0f
                : distanceMeters;
            float safeBearing = (float.IsNaN(bearingDegrees) || float.IsInfinity(bearingDegrees))
                ? 0f
                : bearingDegrees;

            _waypointDistanceMeters = safeDist;
            _waypointBearingDegrees = safeBearing;

            int distRounded = Mathf.RoundToInt(safeDist);
            int bearingRounded = Mathf.RoundToInt(safeBearing);

            bool sameLabel = ReferenceEquals(safeLabel, _waypointCheckpointLabel)
                || string.Equals(safeLabel, _waypointCheckpointLabel, StringComparison.Ordinal);
            if (sameLabel && distRounded == _waypointDistanceRounded && bearingRounded == _waypointBearingRounded)
            {
                return;
            }

            _waypointCheckpointLabel = safeLabel;
            _waypointDistanceRounded = distRounded;
            _waypointBearingRounded = bearingRounded;

            int len = 0;
            if (safeLabel.Length > 0)
            {
                len = AppendString(_waypointBuffer, len, safeLabel);
                len = AppendString(_waypointBuffer, len, " — ");
            }

            len = AppendInt(_waypointBuffer, len, distRounded);
            len = AppendString(_waypointBuffer, len, " m (");
            len = AppendInt(_waypointBuffer, len, bearingRounded);
            len = AppendString(_waypointBuffer, len, "°)");

            _waypointFormatted = new string(_waypointBuffer, 0, len);
            SetText(_waypointLabel, _waypointFormatted);
        }

        public static string FormatClock(int seconds)
        {
            return ClockStrings[Mathf.Clamp(seconds, 0, MaxClockSeconds)];
        }

        /// <summary>Renders a snapshot. <paramref name="jobTitle"/> should be the catalog display name (a stable string).</summary>
        public void Render(in JobSessionSnapshot snapshot, string jobTitle)
        {
            _renderCount++;
            bool hasJob = snapshot.Status != JobSessionStatus.Idle;
            SetVisible(hasJob ? 1 : 0);

            string title = jobTitle ?? string.Empty;
            if (!ReferenceEquals(title, _title))
            {
                _title = title;
                SetText(_titleText, title);
            }

            int clock = Mathf.Clamp(Mathf.FloorToInt(snapshot.ElapsedSeconds), 0, MaxClockSeconds);
            if (clock != _clock)
            {
                _clock = clock;
                SetText(_clockText, ClockStrings[clock]);
            }

            int limit = Mathf.Clamp(Mathf.CeilToInt(snapshot.TimeLimitSeconds), 0, MaxClockSeconds);
            if (limit != _limit)
            {
                _limit = limit;
                SetText(_limitText, ClockStrings[limit]);
            }

            int integrity = Mathf.Clamp(Mathf.RoundToInt(snapshot.CargoIntegrity * 100f), 0, 100);
            if (!hasJob)
            {
                integrity = 0;
            }

            if (integrity != _integrity)
            {
                _integrity = integrity;
                SetText(_integrityText, PercentStrings[integrity]);
                if (_integrityFill != null)
                {
                    _integrityFill.fillAmount = integrity / 100f;
                }
            }

            SetCount(ref _reached, snapshot.CheckpointsReached, _checkpointsReachedText);
            SetCount(ref _count, snapshot.CheckpointCount, _checkpointCountText);
            SetCount(ref _placement, snapshot.RallyPlacement, _placementText);
            SetCount(ref _fieldSize, snapshot.RallyFieldSize, _fieldSizeText);

            string status = SelectStatus(in snapshot);
            if (!ReferenceEquals(status, _status))
            {
                _status = status;
                SetText(_statusText, status);
            }

            int banner = snapshot.Status == JobSessionStatus.Completed && !snapshot.RewardCollected ? 1 : 0;
            if (banner != _banner)
            {
                _banner = banner;
                SetActive(_rewardBanner, banner == 1);
            }
        }

        public static string SelectStatus(in JobSessionSnapshot s)
        {
            switch (s.Status)
            {
                case JobSessionStatus.Active:
                    return s.CheckpointsReached == 0 ? StatusStaging : StatusActive;
                case JobSessionStatus.Completed:
                    return StatusCompleted;
                case JobSessionStatus.Abandoned:
                    return StatusAbandoned;
                case JobSessionStatus.Failed:
                    switch (s.FailReason)
                    {
                        case JobFailReason.CargoDestroyed: return StatusCargoDestroyed;
                        case JobFailReason.TimeExpired: return StatusTimeExpired;
                        case JobFailReason.BikeHardSeized: return StatusSeized;
                        default: return StatusFailed;
                    }
                default:
                    return StatusIdle;
            }
        }

        private void SetCount(ref int cached, int value, Text target)
        {
            int v = Mathf.Clamp(value, 0, MaxCount);
            if (v == cached)
            {
                return;
            }

            cached = v;
            SetText(target, CountStrings[v]);
        }

        private void SetVisible(int visible)
        {
            if (visible == _visible)
            {
                return;
            }

            _visible = visible;
            SetActive(_panelRoot, visible == 1);
        }

        private void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                _textWriteCount++;
                target.SetActive(active);
            }
        }

        private void SetText(Text target, string value)
        {
            if (target != null)
            {
                _textWriteCount++;
                target.text = value;
            }
        }

        private static int AppendString(char[] buffer, int offset, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return offset;
            }

            int max = buffer.Length;
            for (int i = 0; i < value.Length && offset < max; i++)
            {
                buffer[offset++] = value[i];
            }

            return offset;
        }

        private static int AppendInt(char[] buffer, int offset, int value)
        {
            if (offset >= buffer.Length)
            {
                return offset;
            }

            long v = value;
            if (v < 0)
            {
                buffer[offset++] = '-';
                v = -v;
            }

            if (v == 0)
            {
                if (offset < buffer.Length)
                {
                    buffer[offset++] = '0';
                }

                return offset;
            }

            int start = offset;
            while (v > 0 && offset < buffer.Length)
            {
                buffer[offset++] = (char)('0' + (v % 10));
                v /= 10;
            }

            int left = start;
            int right = offset - 1;
            while (left < right)
            {
                char tmp = buffer[left];
                buffer[left] = buffer[right];
                buffer[right] = tmp;
                left++;
                right--;
            }

            return offset;
        }
    }
}
