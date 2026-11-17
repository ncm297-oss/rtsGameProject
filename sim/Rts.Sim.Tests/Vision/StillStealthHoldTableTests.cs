using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.StillScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criterion 7: the forest (M8-1) and the stand-still stealth share one Stealthed entry with any timed application. On
/// the <see cref="StillScenes.Both"/> copy a Malazan Heavy Infantry has both rules (the same status). Five events happen in
/// every order the rules allow (each hold before its release): the forest takes hold (a tree appears beside it) and lets go
/// (the tree is felled), the stand-still rule takes hold (it last moved 60 ticks before) and lets go (it is nudged 1 mm, a
/// shove's seam, which keeps it beside the tree), and a timed Stealthed is applied (40 or 200 ticks). A twin with neither
/// rule (a Whirlwind Raider in its own sim) gets the same timed application. After every tick: each rule holds exactly while
/// its event window is open; the entry's timed part equals the twin's tick for tick (never shortened or lengthened); the
/// entry exists exactly while a rule holds it or the twin's runs; and the unit is Stealthed exactly then.
/// </summary>
public class StillStealthHoldTableTests
{
    private enum Ev { ForestOn, ForestOff, StillOn, StillOff, Timed }

    private static readonly int[] EventTicks = { 100, 130, 160, 190, 220 };

    private const int End = 480;

    /// <summary>Every order of the five events with each hold before its release (30), with a short and a long timed application.</summary>
    public static IEnumerable<object[]> Orders()
    {
        foreach (int timed in new[] { 40, 200 })
            foreach (int[] p in Permutations(new[] { 0, 1, 2, 3, 4 }))
            {
                int Pos(Ev e) => Array.IndexOf(p, (int)e);
                if (Pos(Ev.ForestOn) > Pos(Ev.ForestOff) || Pos(Ev.StillOn) > Pos(Ev.StillOff)) continue;
                yield return new object[] { string.Join(",", p.Select(x => ((Ev)x).ToString())), timed };
            }
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (int k = 0; k < items.Length; k++)
            foreach (int[] rest in Permutations(items.Where((_, j) => j != k).ToArray()))
                yield return new[] { items[k] }.Concat(rest).ToArray();
    }

    [Fact]
    public void TheTable_HasThirtyOrdersForEachTimedLength()
    {
        Assert.Equal(60, Orders().Count());
        Assert.Equal(60, Orders().Select(o => $"{o[0]}/{o[1]}").Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void HeldEntry_NeitherShortensNorLengthensATimedStealthed_InEveryOrder(string order, int timedTicks)
    {
        GameData d = Both;
        int status = Stealthed(d);
        Ev[] events = order.Split(',').Select(Enum.Parse<Ev>).ToArray();
        int At(Ev e) => EventTicks[Array.IndexOf(events, e)];
        int forestOn = At(Ev.ForestOn), forestOff = At(Ev.ForestOff), stillOn = At(Ev.StillOn), stillOff = At(Ev.StillOff), timedAt = At(Ev.Timed);

        Simulation main = Scene(d, combat: false), twin = Scene(d, combat: false);
        UnitStore u = main.World.Units;
        EntityHandle h = CombatScenes.Place(main, 0, d.FindUnit("malazan_heavy_infantry"), StillScenes.At(main, 21, 20));
        EntityHandle t = CombatScenes.Place(twin, 1, d.FindUnit("whirlwind_raider"), StillScenes.At(twin, 21, 20));
        Assert.True(main.World.AnyForestStealth);
        EntityHandle tree = default;
        float nudge = 0.001f;
        for (int tick = 0; tick < End; tick++)
        {
            // Between ticks, before tick `tick`: the events due now.
            if (tick == forestOn) tree = ForestScenes.Tree(main, 20, 20);
            if (tick == forestOff) Assert.True(main.World.Resources.Take(tree, int.MaxValue) > 0);
            // A nudge before tick k is seen by k's pass and dated k - 1: the rule holds from k - 1 + 60. So nudge before
            // every tick up to stillOn - 59, and from stillOff on.
            if ((tick >= 1 && tick <= stillOn - 59) || tick >= stillOff)
            {
                u.Position[h.Index] += new Vector2(nudge, 0f);
                nudge = -nudge;
            }
            if (tick == timedAt)
            {
                StatusSystem.Apply(main.World, h.Index, status, 0f, timedTicks, 0);
                StatusSystem.Apply(twin.World, t.Index, status, 0f, timedTicks, 1);
            }
            main.Tick();
            twin.Tick();

            string at = $"{order} / {timedTicks}, tick {tick}";
            bool forest = tick >= forestOn && tick < forestOff, still = tick >= stillOn && tick < stillOff;
            Assert.True(forest == ForestHeld(main, h), $"{at}: forest held {ForestHeld(main, h)}");
            Assert.True(still == Held(main, h), $"{at}: stand-still held {Held(main, h)}");
            int twinAt = twin.World.Units.Statuses.IndexOf(t.Index, status);
            int twinOwn = twinAt < 0 ? -1 : twin.World.Units.Statuses.TicksRemaining[twinAt];
            int own = OwnTicks(main, h);
            if (forest || still) Assert.True(own == Math.Max(0, twinOwn), $"{at}: timed part {own}, twin {twinOwn}");
            else Assert.True(own == twinOwn, $"{at}: entry {own}, twin {twinOwn}");
            Assert.True(StatusSystem.Holders(main.World, h.Index, status) == (forest ? 1 : 0) + (still ? 1 : 0), at);
            Assert.True((forest || still || twinOwn > 0) == main.World.IsStealthed(h.Index), $"{at}: stealthed {main.World.IsStealthed(h.Index)}");
            Assert.True(main.World.Units.Statuses.StealthedUnits == (own >= 0 ? 1 : 0), at);
        }
        Assert.False(main.World.Units.Statuses.HasStealth(h.Index)); // every holder gone, the timed part run out: no entry outlives them
    }
}
