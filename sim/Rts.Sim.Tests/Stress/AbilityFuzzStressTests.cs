using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-4a criterion 5: two armies with Cadre Mages on generated maps, 2,000 ticks, 3 seeds, under hostile UseAbility spam
/// (in and out of range, queued, any ability index, dead and recycled casters, other players' units, points off the map
/// and NaN) mixed with Moves and attack-moves. Twins hash-equal after every tick; every status is a known id with ticks
/// left on a live unit; no unit carries a cast it has no ability for; casts resolved and statuses ticked; the recorded
/// replay round-trips and plays back equal.
/// </summary>
public class AbilityFuzzStressTests
{
    private const int Ticks = 2000;
    private const int PerSide = 40;
    private readonly ITestOutputHelper _out;

    public AbilityFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_cadre_mage", "malazan_cadre_mage", "malazan_heavy_infantry", "malazan_crossbowman" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "malazan_cadre_mage" };

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * PerSide, CommandCapacity: 16 * PerSide + 64));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 171);
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                int type = TestSim.Data.FindUnit((side == 0 ? Army0 : Army1)[rng.NextInt(0, 4)]);
                sim.Enqueue(Command.SpawnUnit(side, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    private static List<int> Open(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        return open;
    }

    /// <summary>A burst of hostile orders: mostly UseAbility in every shape, some Moves and attack-moves to keep things walking.</summary>
    private static void Spam(Simulation sim, List<int> open, ref SimRng rng, List<EntityHandle> everSeen)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int n = 0; n < 12; n++)
        {
            EntityHandle h;
            int pick = rng.NextInt(0, 10);
            if (pick == 0 && everSeen.Count > 0) h = everSeen[rng.NextInt(0, everSeen.Count)]; // maybe dead or recycled
            else
            {
                int i = rng.NextInt(0, u.Capacity);
                h = new EntityHandle(i, u.Generation[i]);
            }
            int player = rng.NextInt(0, 10) == 0 ? rng.NextInt(0, 2) : (u.Alive[h.Index] ? u.Owner[h.Index] : 0);
            int c = open[rng.NextInt(0, open.Count)];
            Vector2 point = g.CellCenter(c % g.Width, c / g.Width);
            int shape = rng.NextInt(0, 12);
            if (shape == 0) point = new Vector2(-10f, point.Y);                       // off the map
            else if (shape == 1) point = new Vector2(float.NaN, point.Y);              // garbage
            else if (shape <= 5 && u.Alive[h.Index]) point = u.Position[h.Index] + new Vector2(rng.NextFloat() * 20f - 10f, rng.NextFloat() * 20f - 10f); // near: often in range
            int index = rng.NextInt(0, 8) == 0 ? rng.NextInt(-1, 5) : 0;
            bool queued = rng.NextInt(0, 4) == 0;
            int kind = rng.NextInt(0, 10);
            if (kind < 7) sim.Enqueue(Command.UseAbility(player, h, index, point, queued));
            else if (kind < 9) sim.Enqueue(Command.Move(player, h, point, queued));
            else sim.Enqueue(Command.AttackMove(player, h, point, queued));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    public void HostileUseAbilitySpam_TwinsHashEqualEveryTick_InvariantsHold_AndTheReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        List<int> open = Open(w.NavGrid);
        var rngA = new SimRng(seed, 172);
        var rngB = new SimRng(seed, 172);
        var seenA = new List<EntityHandle>();
        var seenB = new List<EntityHandle>();
        int starts = 0, resolves = 0, statusTicks = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 5 == 3)
            {
                Spam(a, open, ref rngA, seenA);
                Spam(b, open, ref rngB, seenB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            foreach (AbilityEvent e in w.AbilityEvents)
                if (e.Resolved) resolves++; else starts++;
            int casters = 0, withStatuses = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.CastAbility[i] >= 0) casters++;
                if (s.Count[i] > 0) withStatuses++;
                if (!u.Alive[i])
                {
                    Assert.True(s.Count[i] == 0 && u.CastAbility[i] == -1, $"seed {seed} tick {t}: dead slot {i} keeps ability state");
                    continue;
                }
                if (t % 50 == 0) { seenA.Add(new EntityHandle(i, u.Generation[i])); seenB.Add(new EntityHandle(i, b.World.Units.Generation[i])); }
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Assert.True(u.CastAbility[i] < def.Abilities.Length, $"seed {seed} tick {t}: unit {i} casts an ability it lacks");
                Assert.True((u.State[i] == UnitState.Casting) == (u.CastAbility[i] >= 0 && u.CastTicks[i] > 0), $"seed {seed} tick {t}: unit {i} state {u.State[i]} vs cast");
                statusTicks += s.Count[i];
                for (int k = 0; k < s.Count[i]; k++)
                {
                    int at = i * StatusStore.PerUnit + k;
                    Assert.True((uint)s.StatusId[at] < (uint)w.Data.Statuses.Length && s.TicksRemaining[at] > 0, $"seed {seed} tick {t}: bad status on {i}");
                }
            }
            // The phase skips' counters match the stores.
            Assert.True(casters == u.CasterCount && withStatuses == s.UnitsWithStatuses, $"seed {seed} tick {t}: counters {u.CasterCount}/{s.UnitsWithStatuses}, actual {casters}/{withStatuses}");
        }
        _out.WriteLine($"seed {seed}: cast starts {starts}, resolves {resolves}, status-ticks {statusTicks}, kills {w.Kills[0]}/{w.Kills[1]}");
        Assert.True(resolves > 5, $"only {resolves} casts resolved");
        Assert.True(statusTicks > 0, "nobody burned");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
