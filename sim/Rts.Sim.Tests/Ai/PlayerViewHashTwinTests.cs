using Rts.Sim.Ai;
using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1 criterion 3: reading never writes. Two runs of <see cref="PlayerViewScene"/> get the same commands; run A builds a
/// fresh <see cref="PlayerView"/> for every player every tick, keeps one more per player across ticks, and reads everything
/// both answer (every cursor, every cell, the economy and the queues); run B never makes one. Their state hashes are equal
/// after every tick, and A's hash doesn't move across the reads.
/// </summary>
public class PlayerViewHashTwinTests
{
    private const int Ticks = 1000;

    [Theory]
    [InlineData(2UL)]
    [InlineData(9UL)]
    public void ReadingEveryPlayersView_EveryTick_LeavesTheHashEqualToABareTwin(ulong seed)
    {
        Simulation a = PlayerViewScene.New(seed), b = PlayerViewScene.New(seed);
        var rngA = new SimRng(seed, 512);
        var rngB = new SimRng(seed, 512);
        var kept = new PlayerView[PlayerViewScene.Players];
        for (int p = 0; p < kept.Length; p++) kept[p] = new PlayerView(a.World, p);
        long sum = 0;
        for (int t = 0; t < Ticks; t++)
        {
            PlayerViewScene.Orders(a, t, ref rngA);
            PlayerViewScene.Orders(b, t, ref rngB);
            a.Tick();
            b.Tick();
            ulong before = a.StateHash();
            for (int p = 0; p < PlayerViewScene.Players; p++)
            {
                long fresh = PlayerViewReader.ReadAll(new PlayerView(a.World, p), cells: true);
                Assert.Equal(fresh, PlayerViewReader.ReadAll(kept[p], cells: true));
                sum += fresh;
            }
            Assert.Equal(before, a.StateHash());
            Assert.True(before == b.StateHash(), $"seed {seed}: the twins differ after tick {t}");
        }
        Assert.NotEqual(0, sum);
    }
}
