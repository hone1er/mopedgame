using System;

namespace MopedSim.Core
{
    /// <summary>
    /// Runtime-independent deterministic SplitMix64 + XorShift64* PRNG guaranteeing bit-identical sequences across .NET and Unity.
    /// </summary>
    public sealed class SeededSimRandom : ISimRandom
    {
        private ulong _state;

        /// <summary>
        /// The initial seed used to construct this generator.
        /// </summary>
        public uint Seed { get; }

        /// <summary>
        /// Initializes a deterministic random generator from an unsigned 32-bit seed.
        /// </summary>
        public SeededSimRandom(uint seed)
        {
            Seed = seed;
            // Warm up via SplitMix64 so seed 0 produces a well-distributed non-zero state.
            ulong z = (ulong)seed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            _state = z ^ (z >> 31);
            if (_state == 0UL)
            {
                _state = 0x9E3779B97F4A7C15UL;
            }
        }

        /// <summary>
        /// Initializes a deterministic random generator from a signed 32-bit seed.
        /// </summary>
        public SeededSimRandom(int seed) : this(unchecked((uint)seed))
        {
        }

        /// <summary>
        /// Returns the next uniformly distributed 64-bit unsigned integer.
        /// </summary>
        public ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <inheritdoc />
        public double NextDouble()
        {
            // Upper 53 bits mapped into [0.0, 1.0)
            ulong value = NextUInt64() >> 11;
            return value * (1.0 / (1UL << 53));
        }

        /// <summary>
        /// Returns a single-precision float in [0.0f, 1.0f).
        /// </summary>
        public float NextFloat()
        {
            return (float)NextDouble();
        }

        /// <summary>
        /// Returns a single-precision float in [minInclusive, maxExclusive).
        /// </summary>
        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive + (maxExclusive - minInclusive) * NextFloat();
        }

        /// <inheritdoc />
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive > maxExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minInclusive),
                    "minInclusive must be less than or equal to maxExclusive.");
            }
            if (minInclusive == maxExclusive)
            {
                return minInclusive;
            }

            ulong range = (ulong)((long)maxExclusive - (long)minInclusive);
            ulong value = NextUInt64() % range;
            return (int)((long)minInclusive + (long)value);
        }
    }
}
