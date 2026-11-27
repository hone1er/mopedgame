using MopedSim.Audio;
using NUnit.Framework;
using UnityEngine;

namespace MopedSim.Tests.EditMode
{
    public sealed class AudioMixBusTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            AudioMixBus.ResetForTests();
            _go = new GameObject("AudioMixBusTest");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            AudioMixBus.ResetForTests();
        }

        [Test]
        public void ComputeVolume_MultipliesAndClamps()
        {
            Assert.AreEqual(0.25f, AudioMixBus.ComputeVolume(0.5f, 0.5f), 1e-6f);
            Assert.AreEqual(1f, AudioMixBus.ComputeVolume(2f, 3f), 1e-6f);
            Assert.AreEqual(0f, AudioMixBus.ComputeVolume(0.8f, -1f), 1e-6f);
        }

        [Test]
        public void SliderValue_DrivesOnlyMatchingCategorySources()
        {
            AudioSource foley = _go.AddComponent<AudioSource>();
            AudioSource amb = new GameObject("amb").AddComponent<AudioSource>();
            try
            {
                AudioMixBus.Register(foley, AudioMixCategory.Foley, 0.8f);
                AudioMixBus.Register(amb, AudioMixCategory.Ambience, 0.4f);

                AudioMixBus.SetGain(AudioMixCategory.Foley, 0.5f);
                Assert.AreEqual(0.4f, foley.volume, 1e-5f);
                Assert.AreEqual(0.4f, amb.volume, 1e-5f);

                AudioMixBus.SetGain(AudioMixCategory.Ambience, 0f);
                Assert.AreEqual(0f, amb.volume, 1e-6f);
                Assert.AreEqual(0.4f, foley.volume, 1e-5f);

                AudioMixBus.SetBaseVolume(foley, 1f);
                Assert.AreEqual(0.5f, foley.volume, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(amb.gameObject);
            }
        }

        [Test]
        public void Register_AppliesExistingGain_AndUnregisterStopsUpdates()
        {
            AudioMixBus.SetGain(AudioMixCategory.Foley, 0.5f);
            AudioSource s = _go.AddComponent<AudioSource>();
            AudioMixBus.Register(s, AudioMixCategory.Foley, 1f);
            Assert.IsTrue(AudioMixBus.IsRegistered(s));
            Assert.AreEqual(0.5f, s.volume, 1e-5f);

            AudioMixBus.Unregister(s);
            Assert.IsFalse(AudioMixBus.IsRegistered(s));
            Assert.AreEqual(0, AudioMixBus.RegisteredCount);
            AudioMixBus.SetGain(AudioMixCategory.Foley, 0.1f);
            Assert.AreEqual(0.5f, s.volume, 1e-5f);
        }

        [Test]
        public void Register_Twice_DoesNotDuplicate()
        {
            AudioSource s = _go.AddComponent<AudioSource>();
            AudioMixBus.Register(s, AudioMixCategory.Engine, 1f);
            AudioMixBus.Register(s, AudioMixCategory.Engine, 0.5f);
            Assert.AreEqual(1, AudioMixBus.RegisteredCount);
        }

        [Test]
        public void ProceduralClips_AreDeterministicAndBounded()
        {
            float[] a = ProceduralAmbientClips.CreateClick(7);
            float[] b = ProceduralAmbientClips.CreateClick(7);
            CollectionAssert.AreEqual(a, b);
            float[] wind = ProceduralAmbientClips.CreateWindLoop(7, 1f);
            float peak = 0f;
            for (int i = 0; i < wind.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(wind[i]));
            }

            Assert.Greater(peak, 0.01f);
            Assert.LessOrEqual(peak, 1f);
        }

        [Test]
        public void WindLoop_SeamIsContinuous()
        {
            float[] wind = ProceduralAmbientClips.CreateWindLoop(7, 1f);
            float seam = Mathf.Abs(wind[0] - wind[wind.Length - 1]);
            Assert.Less(seam, 0.1f);
        }
    }
}
