using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA re-check (2026-10-08-1435, BUG-0217): combat's scan now asks the vision rule only of a candidate that would beat
/// its best so far. These scenes put an unseen enemy where it would win the comparison (nearer, tied on distance with
/// the lower slot, or a better tier) next to seen ones, on the two-level map: a level-0 Crossbowman (sight 18 m) never
/// sees the plateau beyond the 4 m lip. The spatial hash returns enemies bucket row by bucket row (2 cells a bucket), so
/// the rows are chosen to have the unseen one scanned before the seen ones (and, in one row, after): a scan that let a
/// rejected candidate move its best distance or tier would pick nothing or the wrong one. The pick must be the best seen
/// enemy every time.
/// </summary>
public class VisionGateOrderQaTests
{
    /// <summary>Level 0, the scanner's cell.</summary>
    private static readonly Vector2 Low = FogMaps.Cell(16, 30);

    private static EntityHandle Held(Simulation sim, int owner, int type, Vector2 at)
    {
        EntityHandle h = Place(sim, owner, type, at);
        sim.World.Units.Hold[h.Index] = true;
        sim.World.Units.CooldownTicks[h.Index] = 1_000_000; // never swings: nothing reveals anyone
        return h;
    }

    /// <summary>Ticks until the scanner has a target (at most 2 scan intervals) and returns it.</summary>
    private static EntityHandle FirstPick(Simulation sim, EntityHandle scanner, Action? beforeTick = null)
    {
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 8 && u.Target[scanner.Index].Generation == 0; t++)
        {
            beforeTick?.Invoke();
            sim.Tick();
        }
        Assert.False(u.TargetIsBuilding[scanner.Index]);
        return u.Target[scanner.Index];
    }

    private static void AssertUnseen(Simulation sim, EntityHandle scanner, EntityHandle unseen)
    {
        Assert.Equal(1, sim.World.Fog.LevelAt(sim.World.Units.Position[unseen.Index]));
        Assert.False(VisionSystem.UnitSeesUnit(sim.World, scanner.Index, unseen.Index));
    }

    /// <summary>An unseen enemy 11.7 m away (plateau) and a seen one 12 m away (level 0), the unseen one scanned first or last: the seen one.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NearerUnseen_LosesToFartherSeen(bool unseenScannedFirst)
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        EntityHandle scanner = Place(sim, 0, Crossbowman, Low);
        EntityHandle unseen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(21, unseenScannedFirst ? 27 : 33));
        EntityHandle seen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(16, unseenScannedFirst ? 36 : 24));
        AssertUnseen(sim, scanner, unseen);
        Assert.Equal(seen, FirstPick(sim, scanner));
    }

    /// <summary>Two enemies exactly 10 m away, the unseen one (plateau) in the lower slot and scanned first: the tie-break must not hand it the pick, nor block the seen one.</summary>
    [Fact]
    public void TieOnDistance_UnseenLowerSlotScannedFirst_LosesToSeen()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        EntityHandle scanner = Place(sim, 0, Crossbowman, Low);
        EntityHandle unseen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(21, 30));
        EntityHandle seen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(16, 35));
        UnitStore u = sim.World.Units;
        Assert.True(unseen.Index < seen.Index);
        Assert.Equal(Vector2.DistanceSquared(Low, u.Position[unseen.Index]), Vector2.DistanceSquared(Low, u.Position[seen.Index]));
        AssertUnseen(sim, scanner, unseen);
        Assert.Equal(seen, FirstPick(sim, scanner));
    }

    /// <summary>The unseen one is the scanner's last attacker (tier 0) and scanned first, the seen one a plain combatant (tier 1): the seen one.</summary>
    [Fact]
    public void BetterTierUnseen_ScannedFirst_LosesToSeen()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        UnitStore u = sim.World.Units;
        EntityHandle scanner = Place(sim, 0, Crossbowman, Low);
        EntityHandle unseen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(21, 27));
        EntityHandle seen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(16, 36));
        AssertUnseen(sim, scanner, unseen);
        Assert.Equal(seen, FirstPick(sim, scanner, () => u.LastAttacker[scanner.Index] = unseen));
    }

    /// <summary>
    /// Scanned in this order: a seen enemy 14 m away, an unseen one 11.7 m away (plateau), a seen one 12 m away. The
    /// rejected unseen candidate must not stop the last one beating the first.
    /// </summary>
    [Fact]
    public void RejectedUnseenCandidate_DoesNotBlockTheNextSeenOne()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        EntityHandle scanner = Place(sim, 0, Crossbowman, Low);
        Held(sim, 1, HeavyInfantry, FogMaps.Cell(16, 23));
        EntityHandle unseen = Held(sim, 1, HeavyInfantry, FogMaps.Cell(21, 27));
        EntityHandle mid = Held(sim, 1, HeavyInfantry, FogMaps.Cell(16, 36));
        AssertUnseen(sim, scanner, unseen);
        Assert.Equal(mid, FirstPick(sim, scanner));
    }
}
