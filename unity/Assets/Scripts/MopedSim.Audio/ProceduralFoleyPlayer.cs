using UnityEngine;

namespace MopedSim.Audio
{
    /// <summary>Foley bus voice: procedural wrench click one-shots. Zero per-call allocation.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralFoleyPlayer : MonoBehaviour
    {
        [SerializeField] private int _seed = 0x51F0A3;
        [SerializeField] private float _baseVolume = 0.8f;

        private AudioSource _source;
        private AudioClip _click;

        public AudioSource Source { get { return _source; } }
        public int PlayCount { get; private set; }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            float[] data = ProceduralAmbientClips.CreateClick(_seed);
            _click = AudioClip.Create("FoleyWrenchClick", data.Length, 1, ProceduralAmbientClips.SampleRate, false);
            _click.SetData(data, 0);
            AudioMixBus.Register(_source, AudioMixCategory.Foley, _baseVolume);
        }

        private void OnDestroy()
        {
            AudioMixBus.Unregister(_source);
            if (_click != null)
            {
                Destroy(_click);
            }
        }

        /// <summary>Plays a ratchet click. Failed actions play quieter.</summary>
        public void PlayWrenchClick(bool succeeded)
        {
            if (_source == null || _click == null)
            {
                return;
            }

            _source.PlayOneShot(_click, succeeded ? 1f : 0.45f);
            PlayCount++;
        }
    }
}
