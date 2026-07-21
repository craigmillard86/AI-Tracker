namespace Hap.Synth;

/// <summary>
/// Deterministic, dependency-free PRNG (SplitMix64). The same seed yields the
/// same stream on every platform and runtime — the byte-identical-output
/// guarantee (HAP-2 acceptance criterion; research D8) rests on this, NOT on
/// <see cref="System.Random"/>, whose algorithm is not contractually stable
/// across .NET versions. Pure unchecked ulong arithmetic keeps it reproducible.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed) => _state = unchecked((ulong)seed);

    private ulong NextRaw()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform integer in the closed interval [minInclusive, maxInclusive].</summary>
    public int Next(int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive)
        {
            throw new ArgumentException(
                $"maxInclusive ({maxInclusive}) must be >= minInclusive ({minInclusive}).");
        }

        ulong range = (ulong)(maxInclusive - minInclusive) + 1UL;
        return minInclusive + (int)(NextRaw() % range);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextRaw() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Returns true with probability <paramref name="probability"/>.</summary>
    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count - 1)];
}
