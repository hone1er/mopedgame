namespace MopedSim.Core
{
    /// <summary>
    /// Deterministic pseudo-random number generator abstraction injected into simulation ticks.
    /// </summary>
    public interface ISimRandom
    {
        /// <summary>
        /// Returns a double in [0.0, 1.0).
        /// </summary>
        double NextDouble();

        /// <summary>
        /// Returns an integer in [minInclusive, maxExclusive).
        /// </summary>
        int NextInt(int minInclusive, int maxExclusive);
    }
}
