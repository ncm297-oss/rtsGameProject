using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// BUG-0745 (the BUG-0033 family: a walker blocked by its own idle units): a walker pressed against a cliff whose way on runs
/// along the cliff past idle friends. Its sidestep round them cancelled the flow step's way along the wall, so the slide was
/// nothing and it stood pinned for good (the M4 playable scene's Sapper, seed 1, replayed in a bare sim).
/// </summary>
public class WallPinTests
{
    private readonly ITestOutputHelper _out;

    public WallPinTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// A cliff column at x = 10 from row 11 down, open below it; the walker at its west face just above the gap's corner, idle
    /// friends standing in the gap (the replay's positions, moved to this map), its goal east past the cliff. It gets past the
    /// corner within 200 ticks (before the fix it stood within 0.1 m of where it started and gave up on every Move).
    /// </summary>
    [Fact]
    public void AWalkerPinnedOnACliffByItsSidestepRoundIdleFriends_GetsPast()
    {
        var rows = new string[32];
        for (int y = 0; y < 32; y++) rows[y] = y >= 11 ? new string('0', 10) + "1" + new string('0', 21) : new string('0', 32);
        Simulation sim = LocalMovementTests.SimOn(LocalMovementTests.Rows(rows), 16, combat: false);
        int small = LocalMovementTests.TypeWithRadius(0.4f), big = LocalMovementTests.TypeWithRadius(0.9f);
        // The replay's idle army round the Sapper at tick 2290, relative to its cell (89, 51) -> (9, 11) here.
        (int Type, float X, float Y)[] friends =
        {
            (small, 19.23f, 21.32f), (small, 20.52f, 20.80f), (small, 19.83f, 20.79f), (small, 21.08f, 20.47f),
            (small, 21.74f, 20.43f), (small, 17.59f, 23.09f), (big, 18.20f, 22.04f),
        };
        foreach ((int type, float x, float y) in friends) CombatScenes.Place(sim, 0, type, new Vector2(x, y));
        EntityHandle walker = CombatScenes.Place(sim, 0, small, new Vector2(19.45f, 23.44f));
        Assert.False(sim.World.NavGrid.IsPassable(10, 11));
        Assert.True(sim.World.NavGrid.IsPassable(10, 10));
        sim.Enqueue(Command.Move(0, walker, new Vector2(40f, 40f)));
        UnitStore u = sim.World.Units;
        bool past = false;
        int ticks = 0;
        for (; ticks < 200 && !past; ticks++)
        {
            sim.Tick();
            past = u.Position[walker.Index].X > 22f;
        }
        _out.WriteLine($"walker at {u.Position[walker.Index]} after {ticks} ticks, state {u.State[walker.Index]}, give-ups {u.GiveUps[walker.Index]}");
        Assert.True(past, $"the walker stood pinned at {u.Position[walker.Index]} ({u.State[walker.Index]}, give-ups {u.GiveUps[walker.Index]})");
    }
}
