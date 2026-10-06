using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

/// <summary>M3-1: the unrolled <see cref="StateHasher.Add(ulong)"/> is still byte-wise FNV-1a, and <see cref="StateHasher.AddWord"/> tells words apart.</summary>
public class StateHasherTests
{
    private static ulong ReferenceFnv1a(params ulong[] values)
    {
        ulong h = 14695981039346656037UL;
        foreach (ulong v in values)
            for (int i = 0; i < 8; i++)
                h = unchecked((h ^ (byte)(v >> (i * 8))) * 1099511628211UL);
        return h;
    }

    [Fact]
    public void Add_IsByteWiseFnv1a_LittleEndian()
    {
        ulong[] values = { 0, 1, 0xFF, 0x0123456789ABCDEFUL, ulong.MaxValue, 1UL << 63, 0x8000_0000UL };
        var h = new StateHasher();
        foreach (ulong v in values) h.Add(v);
        Assert.Equal(ReferenceFnv1a(values), h.Value);
        // The published FNV-1a 64 of the empty input is the offset basis.
        Assert.Equal(14695981039346656037UL, new StateHasher().Value);
    }

    [Fact]
    public void AddWord_IsStable_AndEverySingleChangedWordChangesTheHash()
    {
        static ulong Hash(params ulong[] words)
        {
            var h = new StateHasher();
            foreach (ulong w in words) h.AddWord(w);
            return h.Value;
        }
        ulong baseHash = Hash(1, 2, 3, 4);
        Assert.Equal(baseHash, Hash(1, 2, 3, 4));
        for (int pos = 0; pos < 4; pos++)
        {
            foreach (int bit in new[] { 0, 31, 32, 63 })
            {
                ulong[] w = { 1, 2, 3, 4 };
                w[pos] ^= 1UL << bit;
                Assert.NotEqual(baseHash, Hash(w));
            }
        }
        Assert.NotEqual(Hash(1, 2), Hash(2, 1));
        Assert.NotEqual(Hash(0), Hash(0, 0));
    }
}
