using MopedSim.Core;
using UnityEngine;

namespace MopedSim.Audio
{
    /// <summary>
    /// Procedural 2-stroke engine and mechanical Foley voice driven solely by EngineTelemetrySnapshot.
    /// Main thread: <see cref="ApplyTelemetry"/> maps telemetry to an immutable <see cref="TwoStrokeAudioParameters"/>
    /// snapshot (zero GC). Audio thread: OnAudioFilterRead renders it through the pure <see cref="TwoStrokeDsp"/> synth.
    /// Cues: RPM harmonics, four-stroking blubber (18-35 Hz AM), powerband pipe ring, lean pre-seize ping,
    /// exhaust flange pop, centrifugal clutch chatter, wind roar, belt squeal / chain clatter.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class TwoStrokeAudioDriver : MonoBehaviour
    {
        [SerializeField] private float _masterVolume = TwoStrokeAudioParameters.DefaultMasterVolume;
        [SerializeField] private float _idleRpm = TwoStrokeAudioParameters.DefaultIdleRpm;
        [SerializeField] private bool _mechanicDiagnosticHints = true;
        [SerializeField] private int _noiseSeed = 0x2C1B3C6D;

        // Guards the parameter hand-off between the main thread (writer) and the audio thread (reader).
        // Monitor on a pre-allocated object performs no managed allocation.
        private readonly object _parameterLock = new object();
        private TwoStrokeAudioParameters _published = TwoStrokeAudioParameters.Silent;
        private TwoStrokeAudioParameters _current = TwoStrokeAudioParameters.Silent;

        // Owned exclusively by the audio thread once Awake has run.
        private TwoStrokeSynthState _voice;
        private bool _voiceReady;
        private int _telemetryApplyCount;

        public EngineTelemetrySnapshot LastTelemetry { get; private set; }

        /// <summary>Most recent cue snapshot produced by <see cref="ApplyTelemetry"/> (main thread view).</summary>
        public TwoStrokeAudioParameters CurrentParameters { get { return _current; } }

        public int TelemetryApplyCount { get { return _telemetryApplyCount; } }
        public bool MechanicDiagnosticHints { get { return _mechanicDiagnosticHints; } }
        public int OutputSampleRate { get { return _voice.SampleRateHz; } }

        public float CurrentRpmCue { get { return LastTelemetry.Rpm; } }
        public float FourStrokeCue { get { return _current.BlubberDepth; } }
        public float PowerbandCue { get { return _current.ResonanceGain; } }
        public float PingCue { get { return _current.PingEnergy; } }
        public float LeakCue { get { return _current.LeakPopGain; } }
        public float ClutchChatterCue { get { return _current.ClutchChatterGain; } }
        public float BeltSquealCue { get { return _current.BeltSquealGain; } }
        public float WindRoarCue { get { return _current.WindRoarGain; } }

        private void Awake()
        {
            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0)
            {
                // Audio disabled (e.g. some headless batchmode hosts): keep a valid voice so offline paths still work.
                sampleRate = TwoStrokeDsp.FallbackSampleRate;
            }

            _voice = TwoStrokeSynthState.Create(sampleRate, unchecked((uint)_noiseSeed));
            _voiceReady = true;

            AudioSource source = GetComponent<AudioSource>();
            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 0f;
            if (source.clip == null)
            {
                // Silent carrier clip keeps the AudioSource (and its OnAudioFilterRead chain) running.
                source.clip = AudioClip.Create("TwoStrokeSynthCarrier", sampleRate, 1, sampleRate, false);
            }

            source.Play();
        }

        public void SetDiagnosticHints(bool enabled)
        {
            _mechanicDiagnosticHints = enabled;
        }

        /// <summary>
        /// Feeds the latest telemetry and republishes the cue snapshot for the audio thread. Zero allocations; safe to
        /// call every FixedUpdate/Update.
        /// </summary>
        public void ApplyTelemetry(in EngineTelemetrySnapshot telemetry)
        {
            LastTelemetry = telemetry;
            _current = TwoStrokeAudioParameters.FromTelemetry(in telemetry, _masterVolume, _idleRpm, _mechanicDiagnosticHints);
            lock (_parameterLock)
            {
                _published = _current;
            }

            _telemetryApplyCount++;
        }

        /// <summary>Back-compat entry point used by AppCompositionRoot; forwards to <see cref="ApplyTelemetry"/>.</summary>
        public void UpdateTelemetry(in EngineTelemetrySnapshot telemetry)
        {
            ApplyTelemetry(in telemetry);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_voiceReady)
            {
                return;
            }

            TwoStrokeAudioParameters p;
            lock (_parameterLock)
            {
                p = _published;
            }

            TwoStrokeDsp.Render(in p, ref _voice, data, channels);
        }
    }
}
