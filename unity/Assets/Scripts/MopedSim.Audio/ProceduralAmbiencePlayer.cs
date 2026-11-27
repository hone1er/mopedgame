using UnityEngine;

namespace MopedSim.Audio
{
    /// <summary>Ambience bus voice: low procedural wind/city bed that swells with fog intensity.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralAmbiencePlayer : MonoBehaviour
    {
        [SerializeField] private int _seed = 0x7A11E5;
        [SerializeField] private float _minLevel = 0.15f;
        [SerializeField] private float _maxLevel = 0.5f;

        private AudioSource _source;

        public AudioSource Source { get { return _source; } }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.loop = true;
            _source.spatialBlend = 0f;
            float[] data = ProceduralAmbientClips.CreateWindLoop(_seed, 3f);
            AudioClip clip = AudioClip.Create("AmbienceWindLoop", data.Length, 1, ProceduralAmbientClips.SampleRate, false);
            clip.SetData(data, 0);
            _source.clip = clip;
            AudioMixBus.Register(_source, AudioMixCategory.Ambience, _minLevel);
            _source.playOnAwake = true;
            if (Application.isPlaying)
            {
                _source.Play();
            }
        }

        private void OnDestroy()
        {
            AudioMixBus.Unregister(_source);
            if (_source != null && _source.clip != null)
            {
                Destroy(_source.clip);
            }
        }

        public void SetFogIntensity(float fog01)
        {
            AudioMixBus.SetBaseVolume(_source, Mathf.Lerp(_minLevel, _maxLevel, Mathf.Clamp01(fog01)));
        }
    }
}
