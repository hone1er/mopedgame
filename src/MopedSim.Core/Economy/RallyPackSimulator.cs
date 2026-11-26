#nullable enable

using System;
using System.Collections.Generic;

namespace MopedSim.Core
{
    /// <summary>
    /// Tunables for the Bay Swarm AI moped pack (injectable; defaults model a stock 50cc group-ride pace of ~20-28 mph).
    /// </summary>
    public sealed class RallyPackConfig
    {
        /// <summary>AI riders in the pack (field size = this + the player).</summary>
        public int AiRiderCount { get; set; } = 7;

        public float MinCruiseSpeedMps { get; set; } = 9.0f;
        public float MaxCruiseSpeedMps { get; set; } = 12.5f;

        /// <summary>Per-tick random speed variation as a fraction of cruise speed (±).</summary>
        public float SpeedJitterFraction { get; set; } = 0.06f;

        /// <summary>Speed-change rate (m/s²) while riders chase their target speed.</summary>
        public float AccelerationMps2 { get; set; } = 1.5f;

        /// <summary>Fraction of speed lost per % of uphill grade on the current leg.</summary>
        public float GradeSpeedPenaltyPerPercent { get; set; } = 0.025f;

        public float MinGradeSpeedFactor { get; set; } = 0.4f;
        public float MaxGradeSpeedFactor { get; set; } = 1.15f;

        /// <summary>Chance per minute that an AI rider breaks down and drops out (DNF).</summary>
        public float BreakdownChancePerMinute { get; set; } = 0.01f;

        /// <summary>Distance behind the pack median beyond which the player counts as "off the back".</summary>
        public float FallOffBackGapMeters { get; set; } = 120f;
    }

    /// <summary>
    /// Mutable per-rider state of one AI pack rider (read-only outside <see cref="RallyPackSimulator"/>).
    /// </summary>
    public sealed class RallyRiderState
    {
        public string RiderId { get; }
        public string DisplayName { get; }
        public float CruiseSpeedMps { get; }
        public float CurrentSpeedMps { get; internal set; }
        public float ProgressMeters { get; internal set; }
        public int CheckpointsPassed { get; internal set; }
        public bool IsFinished { get; internal set; }
        public float FinishTimeSeconds { get; internal set; }
        public bool IsDnf { get; internal set; }

        internal RallyRiderState(string riderId, string displayName, float cruiseSpeedMps)
        {
            RiderId = riderId;
            DisplayName = displayName;
            CruiseSpeedMps = cruiseSpeedMps;
            CurrentSpeedMps = cruiseSpeedMps * 0.5f;
        }
    }

    /// <summary>
    /// Deterministic <see cref="ISimRandom"/>-driven AI moped pack for weekend Bay Swarm rallies. Tracks every rival's
    /// checkpoint progress and speed, the player's placement (1..N), and whether the player earned the pack survival bonus
    /// (finished without seizing). Step with explicit dt; identical seeds and inputs give identical results.
    /// </summary>
    public sealed class RallyPackSimulator
    {
        private static readonly string[] RiderNames =
        {
            "Fog Rat", "Tunnel Rd Terror", "Mandela Missile", "Grizzly Goat", "Muni Rail Dodger", "Lake Merritt Loop",
            "Jack London Jet", "Pillar Bandit", "Embarcadero Echo", "Karl's Kid", "Parts Alley Pete", "Ferry Fiend",
        };

        private readonly ISimRandom _rng;
        private readonly List<RallyRiderState> _riders;
        private readonly float[] _cumulative;
        private readonly float[] _legGrades;
        private readonly float[] _medianScratch;

        public RallyPackConfig Config { get; }

        public IReadOnlyList<RallyRiderState> Riders
        {
            get { return _riders; }
        }

        /// <summary>Cumulative route distance (m) at each checkpoint; index 0 (the start) is 0.</summary>
        public IReadOnlyList<float> CheckpointDistancesMeters
        {
            get { return _cumulative; }
        }

        public float RouteLengthMeters { get; }
        public float ElapsedSeconds { get; private set; }

        /// <summary>Field size including the player.</summary>
        public int FieldSize
        {
            get { return _riders.Count + 1; }
        }

        public float PlayerProgressMeters { get; private set; }
        public bool PlayerFinished { get; private set; }
        public float PlayerFinishTimeSeconds { get; private set; }

        /// <summary>Placement frozen at the moment the player finished (0 until then).</summary>
        public int PlayerFinalPlacement { get; private set; }

        /// <summary>True once the player's engine seized (soft or hard) at any point during the rally.</summary>
        public bool PlayerEverSeized { get; private set; }

        /// <summary>Current distance behind the median active AI rider (0 when level, ahead, or finished).</summary>
        public float GapBehindPackMeters { get; private set; }

        public float MaxGapBehindPackMeters { get; private set; }

        /// <summary>Total seconds spent more than <see cref="RallyPackConfig.FallOffBackGapMeters"/> behind the pack.</summary>
        public float SecondsOffTheBack { get; private set; }

        /// <summary>Pack survival bonus: finished the rally without seizing.</summary>
        public bool IsPackSurvivalBonusEarned
        {
            get { return PlayerFinished && !PlayerEverSeized; }
        }

        public RallyPackSimulator(JobDefinition job, ISimRandom rng, RallyPackConfig? config = null)
            : this((job ?? throw new ArgumentNullException(nameof(job))).Checkpoints, rng, config)
        {
        }

        public RallyPackSimulator(IReadOnlyList<JobCheckpointSpec> checkpoints, ISimRandom rng, RallyPackConfig? config = null)
        {
            if (checkpoints == null)
            {
                throw new ArgumentNullException(nameof(checkpoints));
            }
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Config = config ?? new RallyPackConfig();

            _cumulative = ComputeCumulativeDistances(checkpoints);
            _legGrades = new float[_cumulative.Length];
            for (int i = 0; i < checkpoints.Count; i++)
            {
                _legGrades[i] = checkpoints[i] != null ? checkpoints[i].GradePercent : 0f;
            }
            float last = _cumulative.Length > 0 ? _cumulative[_cumulative.Length - 1] : 0f;
            RouteLengthMeters = last > 1f ? last : 1f;

            int count = Config.AiRiderCount < 0 ? 0 : Config.AiRiderCount;
            _riders = new List<RallyRiderState>(count);
            _medianScratch = new float[count];
            float minSpeed = SimMath.Max(0.5f, Config.MinCruiseSpeedMps);
            float maxSpeed = SimMath.Max(minSpeed, Config.MaxCruiseSpeedMps);
            for (int i = 0; i < count; i++)
            {
                float cruise = minSpeed + (maxSpeed - minSpeed) * (float)_rng.NextDouble();
                string name = RiderNames[i % RiderNames.Length];
                if (i >= RiderNames.Length)
                {
                    name = name + " " + (i / RiderNames.Length + 1);
                }
                _riders.Add(new RallyRiderState("rider_" + (i + 1).ToString("00"), name, cruise));
            }
        }

        /// <summary>
        /// Cumulative straight-line distances (m) between consecutive checkpoints in list order; index 0 is 0.
        /// </summary>
        public static float[] ComputeCumulativeDistances(IReadOnlyList<JobCheckpointSpec> checkpoints)
        {
            if (checkpoints == null)
            {
                throw new ArgumentNullException(nameof(checkpoints));
            }
            var result = new float[checkpoints.Count];
            double total = 0.0;
            for (int i = 1; i < checkpoints.Count; i++)
            {
                JobCheckpointSpec a = checkpoints[i - 1];
                JobCheckpointSpec b = checkpoints[i];
                if (a != null && b != null)
                {
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double dz = b.Z - a.Z;
                    total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }
                result[i] = (float)total;
            }
            return result;
        }

        /// <summary>
        /// Advances the AI pack by dt seconds and records the player's route progress and seize state.
        /// The player finishes automatically once <paramref name="playerProgressMeters"/> reaches the route length.
        /// </summary>
        public void Step(float dt, float playerProgressMeters, bool playerSeized)
        {
            if (!(dt > 0f))
            {
                dt = 0f;
            }

            if (playerSeized && !PlayerFinished)
            {
                PlayerEverSeized = true;
            }

            float elapsedBefore = ElapsedSeconds;
            ElapsedSeconds += dt;

            float jitter = SimMath.Max(0f, Config.SpeedJitterFraction);
            float breakdownPerSecond = SimMath.Max(0f, Config.BreakdownChancePerMinute) / 60f;
            for (int i = 0; i < _riders.Count; i++)
            {
                RallyRiderState r = _riders[i];
                if (r.IsFinished || r.IsDnf)
                {
                    continue;
                }

                // Fixed two draws per active rider per tick keeps the RNG stream stable.
                double jitterRoll = _rng.NextDouble();
                double breakdownRoll = _rng.NextDouble();
                if (dt <= 0f)
                {
                    continue;
                }

                if (breakdownRoll < breakdownPerSecond * dt)
                {
                    r.IsDnf = true;
                    r.CurrentSpeedMps = 0f;
                    continue;
                }

                float grade = GradeAt(r.ProgressMeters);
                float gradeFactor = SimMath.Clamp(1f - grade * Config.GradeSpeedPenaltyPerPercent, Config.MinGradeSpeedFactor, Config.MaxGradeSpeedFactor);
                float target = r.CruiseSpeedMps * gradeFactor * (1f + jitter * (float)(jitterRoll * 2.0 - 1.0));
                r.CurrentSpeedMps = SimMath.MoveTowards(r.CurrentSpeedMps, SimMath.Max(0f, target), SimMath.Max(0.01f, Config.AccelerationMps2) * dt);

                float next = r.ProgressMeters + r.CurrentSpeedMps * dt;
                if (next >= RouteLengthMeters)
                {
                    float remaining = RouteLengthMeters - r.ProgressMeters;
                    float t = r.CurrentSpeedMps > 1e-4f ? remaining / r.CurrentSpeedMps : dt;
                    r.FinishTimeSeconds = elapsedBefore + SimMath.Clamp(t, 0f, dt);
                    r.IsFinished = true;
                    next = RouteLengthMeters;
                }
                r.ProgressMeters = next;
                r.CheckpointsPassed = CountCheckpointsAt(next);
            }

            if (!PlayerFinished)
            {
                PlayerProgressMeters = SimMath.Clamp(SimMath.Max(PlayerProgressMeters, playerProgressMeters), 0f, RouteLengthMeters);
                if (PlayerProgressMeters >= RouteLengthMeters - 1e-3f)
                {
                    FinishPlayer();
                }
            }

            UpdatePackGap(dt);
        }

        /// <summary>
        /// Marks the player as finished at the current elapsed time and freezes their placement.
        /// </summary>
        public void FinishPlayer()
        {
            if (PlayerFinished)
            {
                return;
            }
            PlayerProgressMeters = RouteLengthMeters;
            PlayerFinishTimeSeconds = ElapsedSeconds;
            PlayerFinalPlacement = ComputeLivePlacement();
            PlayerFinished = true;
            GapBehindPackMeters = 0f;
        }

        /// <summary>
        /// Player placement 1..<see cref="FieldSize"/>: frozen once finished, otherwise ranked live by route progress
        /// (finished AI riders rank ahead; DNF riders rank behind the player).
        /// </summary>
        public int GetPlayerPlacement()
        {
            return PlayerFinished ? PlayerFinalPlacement : ComputeLivePlacement();
        }

        private int ComputeLivePlacement()
        {
            int ahead = 0;
            for (int i = 0; i < _riders.Count; i++)
            {
                RallyRiderState r = _riders[i];
                if (r.IsDnf)
                {
                    continue;
                }
                if (r.IsFinished || r.ProgressMeters > PlayerProgressMeters)
                {
                    ahead++;
                }
            }
            return ahead + 1;
        }

        private void UpdatePackGap(float dt)
        {
            if (PlayerFinished)
            {
                GapBehindPackMeters = 0f;
                return;
            }

            int n = 0;
            for (int i = 0; i < _riders.Count; i++)
            {
                if (!_riders[i].IsDnf)
                {
                    _medianScratch[n++] = _riders[i].ProgressMeters;
                }
            }
            if (n == 0)
            {
                GapBehindPackMeters = 0f;
                return;
            }
            Array.Sort(_medianScratch, 0, n);
            float median = (n & 1) == 1 ? _medianScratch[n / 2] : 0.5f * (_medianScratch[n / 2 - 1] + _medianScratch[n / 2]);
            GapBehindPackMeters = SimMath.Max(0f, median - PlayerProgressMeters);
            if (GapBehindPackMeters > MaxGapBehindPackMeters)
            {
                MaxGapBehindPackMeters = GapBehindPackMeters;
            }
            if (GapBehindPackMeters > Config.FallOffBackGapMeters)
            {
                SecondsOffTheBack += dt;
            }
        }

        private float GradeAt(float progressMeters)
        {
            for (int i = 1; i < _cumulative.Length; i++)
            {
                if (progressMeters < _cumulative[i])
                {
                    return _legGrades[i];
                }
            }
            return _legGrades.Length > 0 ? _legGrades[_legGrades.Length - 1] : 0f;
        }

        private int CountCheckpointsAt(float progressMeters)
        {
            int passed = 0;
            for (int i = 0; i < _cumulative.Length; i++)
            {
                if (progressMeters + 1e-3f >= _cumulative[i])
                {
                    passed++;
                }
            }
            return passed;
        }
    }
}
