#nullable enable

using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Deterministic math helpers safe across netstandard2.1 and Unity 6.
    /// </summary>
    internal static class SimMath
    {
        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (Abs(delta) <= maxDelta)
            {
                return target;
            }
            return current + (delta > 0f ? maxDelta : -maxDelta);
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Abs(float value)
        {
            return value < 0f ? -value : value;
        }

        public static float Pow(float x, float y)
        {
            return (float)Math.Pow(x, y);
        }

        public static float Exp(float x)
        {
            return (float)Math.Exp(x);
        }

        public static float Sin(float radians)
        {
            return (float)Math.Sin(radians);
        }
    }
}
