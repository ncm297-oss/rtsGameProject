using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

/// <summary>Seed helpers for tests whose bounds were measured on one particular generated map.</summary>
public static class TestSeeds
{
    /// <summary>
    /// The seed that, with BUG-0014's seed mixing, gives exactly the RNG streams (so the map, the spawns,
    /// everything) that <paramref name="oldSeed"/> gave before the mixing: <c>SimRng.MixSeed(PreMix(s)) == s</c>.
    /// </summary>
    /// <remarks>
    /// M1-6 uses it for the tests whose thresholds or preconditions were calibrated on a specific
    /// pre-M1-6 map (open ground east of the center, a 32.9% vs 33% arrival bound), so they keep
    /// testing the scenario they were written for instead of whatever map their seed number now gives.
    /// New tests should use plain seeds.
    /// </remarks>
    public static ulong PreMix(ulong oldSeed)
    {
        // Undo SimRng.MixSeed step by step: xor-shifts, then the odd multipliers, then the gamma.
        ulong z = UnXorShift(oldSeed, 31);
        z = unchecked(z * Inverse(0x94D049BB133111EBUL));
        z = UnXorShift(z, 27);
        z = unchecked(z * Inverse(0xBF58476D1CE4E5B9UL));
        z = UnXorShift(z, 30);
        return unchecked(z - 0x9E3779B97F4A7C15UL);
    }

    // Inverse of x ^ (x >> s): x = y ^ (y >> s) ^ (y >> 2s) ^ ... (the terms telescope).
    private static ulong UnXorShift(ulong y, int s)
    {
        ulong x = y;
        for (int t = s; t < 64; t += s) x ^= y >> t;
        return x;
    }

    // Multiplicative inverse of an odd number mod 2^64 by Newton's iteration (each step doubles the correct bits).
    private static ulong Inverse(ulong c)
    {
        ulong inv = c; // correct to 3 bits for any odd c
        for (int i = 0; i < 6; i++) inv = unchecked(inv * (2 - c * inv));
        return inv;
    }
}
