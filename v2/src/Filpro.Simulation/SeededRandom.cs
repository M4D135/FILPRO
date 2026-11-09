namespace Filpro.Simulation;

/// <summary>
/// Générateur pseudo-aléatoire déterministe (SplitMix64) : une même graine produit toujours la même suite, afin que la
/// démonstration soit reproductible d'un lancement à l'autre. Aucune source d'entropie réelle n'est utilisée.
/// </summary>
public sealed class SeededRandom(ulong seed)
{
    private ulong state = seed;

    public static SeededRandom FromSeed(int seed) => new(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x123456789ABCDEF0UL));

    public ulong NextUInt64()
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Entier dans [0, exclusiveMax) ; exclusiveMax doit être positif.</summary>
    public int Next(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    public bool NextBool(int percentTrue = 50) => Next(100) < percentTrue;
}
