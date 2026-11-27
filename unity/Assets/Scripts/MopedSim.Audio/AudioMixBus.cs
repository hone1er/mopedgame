using System.Collections.Generic;
using UnityEngine;

namespace MopedSim.Audio
{
    public enum AudioMixCategory
    {
        Engine = 0,
        Foley = 1,
        Ambience = 2,
    }

    /// <summary>
    /// Code-only mix registry. AudioSources register with a category and base volume; the effective
    /// AudioSource.volume is always base * category bus gain. Master is applied separately via AudioListener.volume.
    /// Allocation-free after registration.
    /// </summary>
    public static class AudioMixBus
    {
        private struct Entry
        {
            public AudioSource Source;
            public AudioMixCategory Category;
            public float BaseVolume;
        }

        private static readonly List<Entry> Entries = new List<Entry>(8);
        private static readonly float[] Gains = { 1f, 1f, 1f };

        public static int RegisteredCount { get { return Entries.Count; } }

        public static float GetGain(AudioMixCategory category)
        {
            return Gains[(int)category];
        }

        /// <summary>Pure gain math: base volume scaled by bus gain, clamped to [0,1].</summary>
        public static float ComputeVolume(float baseVolume, float busGain)
        {
            return Mathf.Clamp01(baseVolume) * Mathf.Clamp01(busGain);
        }

        public static void SetGain(AudioMixCategory category, float gain)
        {
            Gains[(int)category] = Mathf.Clamp01(gain);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (e.Source == null)
                {
                    Entries.RemoveAt(i);
                    continue;
                }

                if (e.Category == category)
                {
                    e.Source.volume = ComputeVolume(e.BaseVolume, Gains[(int)category]);
                }
            }
        }

        /// <summary>Registers (or re-registers) a source and immediately applies the bus gain.</summary>
        public static void Register(AudioSource source, AudioMixCategory category, float baseVolume)
        {
            if (source == null)
            {
                return;
            }

            Entries.RemoveAll(IsDead);
            Entry entry = new Entry { Source = source, Category = category, BaseVolume = Mathf.Clamp01(baseVolume) };
            int index = IndexOf(source);
            if (index >= 0)
            {
                Entries[index] = entry;
            }
            else
            {
                Entries.Add(entry);
            }

            source.volume = ComputeVolume(entry.BaseVolume, Gains[(int)category]);
        }

        public static void Unregister(AudioSource source)
        {
            int index = IndexOf(source);
            if (index >= 0)
            {
                Entries.RemoveAt(index);
            }
        }

        public static bool IsRegistered(AudioSource source)
        {
            return IndexOf(source) >= 0;
        }

        /// <summary>Updates a registered source's base volume (e.g. ambience following fog) and reapplies bus gain.</summary>
        public static void SetBaseVolume(AudioSource source, float baseVolume)
        {
            int index = IndexOf(source);
            if (index < 0)
            {
                return;
            }

            Entry e = Entries[index];
            e.BaseVolume = Mathf.Clamp01(baseVolume);
            Entries[index] = e;
            source.volume = ComputeVolume(e.BaseVolume, Gains[(int)e.Category]);
        }

        /// <summary>Test hook: restores unity gains and clears registrations.</summary>
        public static void ResetForTests()
        {
            Entries.Clear();
            Gains[0] = Gains[1] = Gains[2] = 1f;
        }

        private static readonly System.Predicate<Entry> IsDead = e => e.Source == null;

        private static int IndexOf(AudioSource source)
        {
            if (source == null)
            {
                return -1;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Source == source)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
