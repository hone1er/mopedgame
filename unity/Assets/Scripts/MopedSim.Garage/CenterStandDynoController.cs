using System;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Garage
{
    /// <summary>
    /// Center-stand / DIY roller dyno: runs the bike in place through BikeSimulation.Step (Core's internal longitudinal load
    /// model provides the simulated drag), tracking RPM, wheel speed, CHT, and AFR peaks for jetting and leak checks.
    /// Only steps while a run is active; MopedSim.App enables it exclusively in the Garage session state so exactly one
    /// stepper advances the bike per frame. FixedUpdate is allocation-free.
    /// </summary>
    public sealed class CenterStandDynoController : MonoBehaviour
    {
        [SerializeField] private bool _autoStepInFixedUpdate = true;
        [SerializeField] private float _throttle = 1f;
        [SerializeField] private Transform _rearWheelVisual;

        private GarageBikeContext _context;
        private bool _running;
        private bool _choke;
        private float _pedalStartSeconds;
        private float _peakPowerWatts;
        private float _elapsedSeconds;
        private float _peakRpm;
        private float _peakWheelSpeedMps;
        private float _peakChtCelsius;
        private float _minAfr;
        private float _maxAfr;
        private float _wheelAngleDeg;
        private EngineTelemetrySnapshot _latest;

        public event Action<EngineTelemetrySnapshot> RunCompleted;

        public bool IsRunning
        {
            get { return _running; }
        }

        public bool AutoStepInFixedUpdate
        {
            get { return _autoStepInFixedUpdate; }
            set { _autoStepInFixedUpdate = value; }
        }

        public float ElapsedSeconds
        {
            get { return _elapsedSeconds; }
        }

        public float PeakRpm
        {
            get { return _peakRpm; }
        }

        public float PeakWheelSpeedMps
        {
            get { return _peakWheelSpeedMps; }
        }

        public float PeakChtCelsius
        {
            get { return _peakChtCelsius; }
        }

        public float MinAfr
        {
            get { return _minAfr; }
        }

        public float MaxAfr
        {
            get { return _maxAfr; }
        }

        public EngineTelemetrySnapshot Latest
        {
            get { return _latest; }
        }

        public float Throttle
        {
            get { return _throttle; }
            set { _throttle = Mathf.Clamp01(value); }
        }

        /// <summary>Seconds at the start of a run during which the rider kick-starts on the roller (pedal assist). 0 = none.</summary>
        public float PedalStartSeconds
        {
            get { return _pedalStartSeconds; }
            set { _pedalStartSeconds = Mathf.Max(0f, value); }
        }

        /// <summary>Highest crank power (W) seen during the current / last run.</summary>
        public float PeakPowerWatts
        {
            get { return _peakPowerWatts; }
        }

        public bool Choke
        {
            get { return _choke; }
            set { _choke = value; }
        }

        public void Bind(GarageBikeContext context)
        {
            _context = context;
            _running = false;
            if (_context != null)
            {
                _latest = _context.Bike.LatestTelemetry;
            }
        }

        public void StartRun(float throttle)
        {
            if (_context == null)
            {
                return;
            }

            Throttle = throttle;
            _running = true;
            _elapsedSeconds = 0f;
            _peakRpm = 0f;
            _peakPowerWatts = 0f;
            _peakWheelSpeedMps = 0f;
            _peakChtCelsius = 0f;
            _minAfr = float.MaxValue;
            _maxAfr = 0f;
            _context.Bike.VehicleSpeedMps = 0f;
        }

        public void StopRun()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            if (_context != null)
            {
                _context.Bike.VehicleSpeedMps = 0f;
            }

            Action<EngineTelemetrySnapshot> handler = RunCompleted;
            if (handler != null)
            {
                handler.Invoke(_latest);
            }
        }

        private void FixedUpdate()
        {
            if (_autoStepInFixedUpdate)
            {
                SimulateStep(Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Advances the dyno run by dt seconds. Zero GC allocations.
        /// </summary>
        public void SimulateStep(float dt)
        {
            if (!_running || _context == null || dt <= 0f)
            {
                return;
            }

            RideInputState input = new RideInputState(_throttle, pedalAssist: _elapsedSeconds < _pedalStartSeconds, choke: _choke);
            _latest = _context.Bike.Step(dt, in input, _context.Random);
            _elapsedSeconds += dt;

            if (_latest.PowerOutputWatts > _peakPowerWatts)
            {
                _peakPowerWatts = _latest.PowerOutputWatts;
            }

            if (_latest.Rpm > _peakRpm)
            {
                _peakRpm = _latest.Rpm;
            }

            if (_latest.VehicleSpeedMps > _peakWheelSpeedMps)
            {
                _peakWheelSpeedMps = _latest.VehicleSpeedMps;
            }

            if (_latest.ChtCelsius > _peakChtCelsius)
            {
                _peakChtCelsius = _latest.ChtCelsius;
            }

            if (_latest.IsRunning)
            {
                if (_latest.EffectiveAfr < _minAfr)
                {
                    _minAfr = _latest.EffectiveAfr;
                }

                if (_latest.EffectiveAfr > _maxAfr)
                {
                    _maxAfr = _latest.EffectiveAfr;
                }
            }

            if (_rearWheelVisual != null)
            {
                float radius = Mathf.Max(0.15f, _context.Bike.Archetype.WheelRadiusM);
                _wheelAngleDeg = Mathf.Repeat(_wheelAngleDeg + (_latest.VehicleSpeedMps / radius) * Mathf.Rad2Deg * dt, 360f);
                _rearWheelVisual.localRotation = Quaternion.Euler(_wheelAngleDeg, 0f, 0f);
            }

            if (_latest.IsSeized)
            {
                StopRun();
            }
        }

        /// <summary>
        /// Runs a fixed-length dyno pull synchronously (tests, plug chops) and returns the final telemetry.
        /// </summary>
        public EngineTelemetrySnapshot RunFor(float seconds, float throttle, float dt)
        {
            StartRun(throttle);
            float step = dt > 0f ? dt : 0.02f;
            int steps = Mathf.CeilToInt(Mathf.Max(0f, seconds) / step);
            for (int i = 0; i < steps && _running; i++)
            {
                SimulateStep(step);
            }

            EngineTelemetrySnapshot final = _latest;
            StopRun();
            return final;
        }
    }
}
