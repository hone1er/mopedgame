using System;

namespace MopedSim.Audio
{
    /// <summary>Deterministic, engine-free generators for the procedural Foley click and Ambience wind loop.</summary>
    public static class ProceduralAmbientClips
    {
        public const int SampleRate = 22050;

        public static float[] CreateClick(int seed)
        {
            int length = SampleRate / 12; // ~83 ms
            float[] data = new float[length];
            FillClick(data, seed);
            return data;
        }

        public static void FillClick(float[] data, int seed)
        {
            uint state = unchecked((uint)seed) | 1u;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float env = (float)Math.Exp(-t * 70.0);
                float noise = NextSigned(ref state);
                float tone = (float)Math.Sin(2.0 * Math.PI * 1800.0 * t);
                data[i] = (noise * 0.6f + tone * 0.4f) * env * 0.8f;
            }
        }

        public static float[] CreateWindLoop(int seed, float seconds)
        {
            int length = Math.Max(SampleRate / 2, (int)(SampleRate * seconds));
            float[] data = new float[length];
            FillWindLoop(data, seed);
            return data;
        }

        /// <summary>Low-passed noise; the tail is folded into the head so the wrap is continuous.</summary>
        public static void FillWindLoop(float[] data, int seed)
        {
            int n = data.Length;
            int fade = Math.Min(SampleRate / 4, n / 4);
            uint state = unchecked((uint)seed) | 1u;
            float lp = 0f;
            float[] head = new float[fade];
            for (int i = 0; i < n + fade; i++)
            {
                lp += (NextSigned(ref state) - lp) * 0.05f;
                float v = lp * 2.2f;
                if (i < n)
                {
                    data[i] = v;
                }
                else
                {
                    head[i - n] = v;
                }
            }

            // head[k] is the natural continuation of data[n-1]; blend it into the start.
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                data[i] = head[i] * (1f - a) + data[i] * a;
            }
        }

        private static float NextSigned(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return ((state >> 8) / 8388608f) - 1f;
        }
    }
}
