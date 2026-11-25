using System;
using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Audio
{
    /// <summary>
    /// Procedural 2-stroke engine and mechanical Foley synthesizer driven solely by EngineTelemetrySnapshot.
    /// Runs inside OnAudioFilterRead (audio thread) using only blittable cached floats written from the main thread.
    /// Cues: RPM harmonics, four-stroking blubber (18-35 Hz AM), powerband pipe resonance, lean pre-seize ping,
    /// exhaust flange pop, centrifugal clutch chatter, belt squeal / chain clatter.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class TwoStrokeAudioDriver : MonoBehaviour
    {
        [SerializeField] private float _masterVolume = 0.35f;
        [SerializeField] private float _idleRpm = 1700f;
        [SerializeField] private bool _mechanicDiagnosticHints = true;

        // Values written on main thread, read on audio thread (float writes are atomic on supported platforms).
        private volatile float _rpm;
        private volatile float _throttle;
        private volatile float _fourStrokeAmount;
        private volatile float _powerbandAmount;
        private volatile float _pingAmount;
        private volatile float _leakAmount;
        private volatile float _clutchChatter;
        private volatile float _beltSqueal;
        private volatile float _chainClatter;
        private volatile float _stalled;

        private double _crankPhase;
        private double _blubberPhase;
        private double _resonancePhase;
        private double _squealPhase;
        private uint _noiseState = 0x9E3779B9u;
        private float _pingEnvelope;
        private float _popEnvelope;
        private int _sampleRate = 48000;
        private int _pingCountdown;
        private int _popCountdown;

        public EngineTelemetrySnapshot LastTelemetry { get; private set; }

        public float CurrentRpmCue { get { return _rpm; } }
        public float FourStrokeCue { get { return _fourStrokeAmount; } }
        public float PowerbandCue { get { return _powerbandAmount; } }
        public float PingCue { get { return _pingAmount; } }
        public float LeakCue { get { return _leakAmount; } }
        public float ClutchChatterCue { get { return _clutchChatter; } }
        public float BeltSquealCue { get { return _beltSqueal; } }

        private void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            AudioSource source = GetComponent<AudioSource>();
            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 0f;
            if (source.clip == null)
            {
                // Dummy silent clip keeps the AudioSource (and its filter chain) running.
                source.clip = AudioClip.Create("TwoStrokeSynthCarrier", _sampleRate, 1, _sampleRate, false);
            }

            source.Play();
        }

        public void SetDiagnosticHints(bool enabled)
        {
            _mechanicDiagnosticHints = enabled;
        }

        /// <summary>
        /// Feeds the latest telemetry. Safe to call every FixedUpdate/Update; zero allocations.
        /// </summary>
        public void UpdateTelemetry(in EngineTelemetrySnapshot telemetry)
        {
            LastTelemetry = telemetry;

            bool running = !telemetry.IsStalled && !telemetry.IsSeized && telemetry.Rpm > 200f;
            _stalled = running ? 0f : 1f;
            _rpm = telemetry.Rpm;
            _throttle = Mathf.Clamp01(telemetry.ThrottlePosition);
            _fourStrokeAmount = telemetry.IsFourStroking ? 1f : 0f;
            _powerbandAmount = telemetry.IsPowerbandActive ? 1f : 0f;

            float ping = 0f;
            if (telemetry.IsDetonating)
            {
                ping = 1f;
            }
            else if (telemetry.ChtCelsius > 206f || telemetry.DetonationRisk > 0.3f)
            {
                ping = Mathf.Clamp01(Mathf.Max((telemetry.ChtCelsius - 206f) / 24f, telemetry.DetonationRisk));
            }

            _pingAmount = _mechanicDiagnosticHints ? ping : ping * 0.6f;
            _leakAmount = Mathf.Clamp01(telemetry.ExhaustLeakFactor);
            _clutchChatter = telemetry.CurrentGear > 0 || telemetry.ClutchEngagement > 0.05f
                ? Mathf.Clamp01(1f - telemetry.ClutchEngagement) * Mathf.Clamp01(_throttle * 2f)
                : 0f;
            _beltSqueal = telemetry.IsBeltSnapped ? 1f : Mathf.Clamp01(telemetry.MisfireFactor * 0.25f);
            _chainClatter = telemetry.IsChainThrown ? 1f : 0f;
        }

        private float NextNoise()
        {
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 17;
            _noiseState ^= _noiseState << 5;
            return (_noiseState / (float)uint.MaxValue) * 2f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float rpm = _rpm;
            float throttle = _throttle;
            float stalled = _stalled;
            float fourStroke = _fourStrokeAmount;
            float powerband = _powerbandAmount;
            float ping = _pingAmount;
            float leak = _leakAmount;
            float clutch = _clutchChatter;
            float squeal = _beltSqueal;
            float chain = _chainClatter;

            float effectiveRpm = Mathf.Max(rpm, _idleRpm * 0.6f * (1f - stalled));
            // Two-stroke fires once per revolution.
            double fireFreq = effectiveRpm / 60.0;
            double invSr = 1.0 / _sampleRate;
            float amp = _masterVolume * (1f - stalled) * (0.35f + 0.65f * throttle);
            double blubberFreq = 18.0 + 17.0 * throttle;
            double resonanceFreq = 1800.0 + effectiveRpm * 0.12;

            for (int i = 0; i < data.Length; i += channels)
            {
                _crankPhase += fireFreq * invSr;
                if (_crankPhase >= 1.0)
                {
                    _crankPhase -= 1.0;
                }

                double ph = _crankPhase * 2.0 * Math.PI;
                float tone = (float)(Math.Sin(ph) * 0.55 + Math.Sin(ph * 2.0) * 0.28 + Math.Sin(ph * 3.0) * 0.14);
                float burst = _crankPhase < 0.25 ? 1f - (float)(_crankPhase / 0.25) : 0f;
                float sample = (tone * 0.6f + burst * NextNoise() * 0.5f) * amp;

                if (fourStroke > 0.5f)
                {
                    _blubberPhase += blubberFreq * invSr;
                    if (_blubberPhase >= 1.0)
                    {
                        _blubberPhase -= 1.0;
                    }

                    float am = 0.45f + 0.55f * (float)Math.Abs(Math.Sin(_blubberPhase * Math.PI));
                    sample *= am;
                }

                if (powerband > 0.5f)
                {
                    _resonancePhase += resonanceFreq * invSr;
                    if (_resonancePhase >= 1.0)
                    {
                        _resonancePhase -= 1.0;
                    }

                    sample += (float)Math.Sin(_resonancePhase * 2.0 * Math.PI) * 0.22f * amp;
                }

                if (ping > 0.01f)
                {
                    if (--_pingCountdown <= 0)
                    {
                        if (NextNoise() > 1f - ping * 0.9f)
                        {
                            _pingEnvelope = 1f;
                        }

                        _pingCountdown = (int)(_sampleRate * 0.012f);
                    }

                    _pingEnvelope *= 0.9985f;
                    sample += _pingEnvelope * NextNoise() * 0.5f * ping;
                }

                if (leak > 0.01f)
                {
                    if (--_popCountdown <= 0)
                    {
                        _popEnvelope = leak;
                        _popCountdown = (int)(_sampleRate * (0.06f + 0.1f * (1f - leak)));
                    }

                    _popEnvelope *= 0.9970f;
                    sample += _popEnvelope * NextNoise() * 0.7f;
                }

                if (clutch > 0.01f)
                {
                    sample += (float)Math.Sign(Math.Sin(_crankPhase * 2.0 * Math.PI * 7.0)) * 0.08f * clutch * amp;
                }

                if (squeal > 0.01f)
                {
                    _squealPhase += (2200.0 + 600.0 * squeal) * invSr;
                    if (_squealPhase >= 1.0)
                    {
                        _squealPhase -= 1.0;
                    }

                    sample += (float)Math.Sin(_squealPhase * 2.0 * Math.PI) * 0.12f * squeal;
                }

                if (chain > 0.5f)
                {
                    sample += NextNoise() * 0.18f * (float)Math.Abs(Math.Sin(_crankPhase * Math.PI * 9.0));
                }

                sample = (float)Math.Tanh(sample);
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] = sample;
                }
            }
        }
    }
}
