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
/// replay round-trips and plays back equal. M4-4b-1: Sappers on both sides throw the Cusser (friendly fire, enemy
/// buildings) among four Keeps a side, some casts aimed at a building. M4-4b-2: a three-player match where two Whirlwind
/// players' Priests throw Sandstorms (overlapping storms of two owners) at a Malazan army and at each other.
/// </summary>
public class AbilityFuzzStressTests
{
    private const int Ticks = 2000;
    private const int PerSide = 40;
    private const int SappersPerSide = 6;
    private readonly ITestOutputHelper _out;

    public AbilityFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_cadre_mage", "malazan_cadre_mage", "malazan_heavy_infantry", "malazan_crossbowman" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "malazan_cadre_mage" };

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * (PerSide + SappersPerSide), CommandCapacity: 16 * PerSide + 128));
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
        // M4-4b-1: Sappers and Keeps from their own stream, so the armies above are the M4-4a ones.
        var extra = new SimRng(seed, 173);
        for (int k = 0; k < SappersPerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                int c = open[extra.NextInt(0, open.Count)];
                sim.Enqueue(Command.SpawnUnit(side, TestSim.Data.FindUnit("malazan_sapper"), g.CellCenter(c % g.Width, c / g.Width)));
            }
        for (int k = 0; k < 4; k++)
            for (int side = 0; side < 2; side++)
            {
                int c = open[extra.NextInt(0, open.Count)];
                int keep = TestSim.Data.FindBuilding(side == 0 ? "malazan_garrison_keep" : "whirlwind_holy_camp");
                sim.Enqueue(Command.SpawnBuilding(side, keep, g.CellCenter(c % g.Width, c / g.Width)));
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
            else if (shape == 6 && sim.World.Buildings.Count > 0)
            {
                int j = rng.NextInt(0, sim.World.Buildings.Capacity);
                if (sim.World.Buildings.Alive[j]) point = Combat.CombatSystem.BuildingCentre(sim.World, j) + new Vector2(rng.NextFloat() * 8f - 4f, 0f);
            }
            else if (shape <= 5 && u.Alive[h.Index]) point = u.Position[h.Index] + new Vector2(rng.NextFloat() * 20f - 10f, rng.NextFloat() * 20f - 10f); // near: often in range
            int index = rng.NextInt(0, 8) == 0 ? rng.NextInt(-1, 5) : 0;
            bool queued = rng.NextInt(0, 4) == 0;
            int kind = rng.NextInt(0, 10);
            if (kind < 7) sim.Enqueue(Command.UseAbility(player, h, index, point, queued));
            else if (kind < 9) sim.Enqueue(Command.Move(player, h, point, queued));
            else sim.Enqueue(Command.AttackMove(player, h, point, queued));
        }
    }

    /// <summary>The first live Cadre Mage (slot order) off cooldown and not casting, with an enemy unit within 12 m, casts Telas Fire at the nearest one.</summary>
    private static void AimedCast(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        int mage = TestSim.Data.FindUnit("malazan_cadre_mage");
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.TypeId[i] != mage || u.CastAbility[i] >= 0) continue;
            if (u.AbilityReadyTick[i * Data.DataLimits.MaxUnitAbilities] > sim.TickNumber) continue; // on cooldown
            Assert.Equal(TestSim.Data.FindAbility("telas_fire"), TestSim.Data.Units[mage].Abilities[0]);
            int best = -1;
            float bestD2 = 144f;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (!u.Alive[j] || u.Owner[j] == u.Owner[i]) continue;
                float d2 = Vector2.DistanceSquared(u.Position[i], u.Position[j]);
                if (d2 < bestD2) (best, bestD2) = (j, d2);
            }
            if (best < 0) continue;
            sim.Enqueue(Command.UseAbility(u.Owner[i], new EntityHandle(i, u.Generation[i]), 0, u.Position[best]));
            return;
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
        int starts = 0, resolves = 0, statusTicks = 0, cussers = 0;
        int cusser = w.Data.FindAbility("cusser");
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 5 == 3)
            {
                Spam(a, open, ref rngA, seenA);
                Spam(b, open, ref rngB, seenB);
            }
            // M4-H2: an aimed Telas Fire every 400 ticks (a command, so the twins and the replay see it), so the run always
            // holds statuses for the invariants below; the random spam alone burned one unit or none on seed 1.
            if (t % 400 == 200)
            {
                AimedCast(a);
                AimedCast(b);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            foreach (AbilityEvent e in w.AbilityEvents)
            {
                if (e.Resolved) resolves++; else starts++;
                if (e.Resolved && e.Ability == cusser) cussers++;
            }
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
        _out.WriteLine($"seed {seed}: cast starts {starts}, resolves {resolves} ({cussers} Cussers), buildings left {w.Buildings.Count}, status-ticks {statusTicks}, kills {w.Kills[0]}/{w.Kills[1]}");
        Assert.True(resolves > 5, $"only {resolves} casts resolved");
        Assert.True(statusTicks > 0, "nobody burned");
        Assert.True(cussers > 3, $"only {cussers} Cussers resolved");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }

    // ---------- M4-4b-2: Sandstorms, three players ----------

    private const int ZoneArmy = 24;
    private const int PriestsPerSide = 8;
    private static readonly string[] MalazanArmy = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_cadre_mage", "malazan_crossbowman" };
    private static readonly string[] WhirlwindArmy = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_desert_archer" };

    /// <summary>Player 0 Malazan, players 1 and 2 Whirlwind with Priests, all packed in the map's middle third so the storms land on fights.</summary>
    private static Simulation NewZoneSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 3, UnitCapacity: 3 * ZoneArmy + 2 * PriestsPerSide, CommandCapacity: 16 * ZoneArmy + 256));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Middle(g);
        var rng = new SimRng(seed, 181);
        int priest = TestSim.Data.FindUnit("whirlwind_priest");
        for (int k = 0; k < ZoneArmy; k++)
            for (int side = 0; side < 3; side++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                int type = TestSim.Data.FindUnit((side == 0 ? MalazanArmy : WhirlwindArmy)[rng.NextInt(0, 4)]);
                sim.Enqueue(Command.SpawnUnit(side, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        for (int k = 0; k < PriestsPerSide; k++)
            for (int side = 1; side < 3; side++)
            {
                int c = open[rng.NextInt(0, open.Count)];
                sim.Enqueue(Command.SpawnUnit(side, priest, g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    private static List<int> Middle(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (g.IsPassable(x, y) && x > g.Width / 3 && x < 2 * g.Width / 3 && y > g.Height / 3 && y < 2 * g.Height / 3) open.Add(c);
        }
        return open;
    }

    /// <summary>Hostile orders aimed at casters: Sandstorms near a unit of any player (in range or not), bad indices and points, other players' units, Moves and attack-moves.</summary>
    private static void ZoneSpam(Simulation sim, List<int> open, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int priest = sim.World.Data.FindUnit("whirlwind_priest");
        for (int n = 0; n < 10; n++)
        {
            int i = rng.NextInt(0, u.Capacity);
            // Half the orders go to the next live Priest from a random slot, so the storms keep coming.
            if (rng.NextInt(0, 2) == 0)
                for (int step = 0; step < u.Capacity; step++, i = (i + 1) % u.Capacity)
                    if (u.Alive[i] && u.TypeId[i] == priest) break;
            var h = new EntityHandle(i, u.Generation[i]);
            int player = rng.NextInt(0, 12) == 0 ? rng.NextInt(0, 3) : (u.Alive[i] ? u.Owner[i] : 0);
            int target = rng.NextInt(0, u.Capacity);
            int c = open[rng.NextInt(0, open.Count)];
            Vector2 point = u.Alive[target] ? u.Position[target] + new Vector2(rng.NextFloat() * 6f - 3f, rng.NextFloat() * 6f - 3f) : g.CellCenter(c % g.Width, c / g.Width);
            int shape = rng.NextInt(0, 16);
            if (shape == 0) point = new Vector2(point.X, -3f);           // off the map
            else if (shape == 1) point = new Vector2(float.NaN, point.Y); // garbage
            int index = rng.NextInt(0, 10) == 0 ? rng.NextInt(-1, 3) : 0;
            int kind = rng.NextInt(0, 10);
            if (kind < 7) sim.Enqueue(Command.UseAbility(player, h, index, point, rng.NextInt(0, 5) == 0));
            else if (kind < 9) sim.Enqueue(Command.AttackMove(player, h, point, false));
            else sim.Enqueue(Command.Move(player, h, point, false));
        }
    }

    /// <summary>
    /// M4-4b-2 criterion 5: 3 players, two of them Whirlwind with Priests casting Sandstorm into fights, 3 seeds x 2,000 ticks:
    /// twins hash-equal after every tick; every zone is a known ability's with 1-240 ticks left; the stores' counters and every
    /// unit's blind in force follow their entries; storms were cast and units Blinded; the replay round-trips and plays back.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(11UL)]
    public void Sandstorms_ThreePlayers_TwinsHashEqualEveryTick_InvariantsHold_AndTheReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewZoneSim(seed, rec), b = NewZoneSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        ZoneStore z = w.Zones;
        List<int> open = Middle(w.NavGrid);
        var rngA = new SimRng(seed, 182);
        var rngB = new SimRng(seed, 182);
        int sandstorm = w.Data.FindAbility("sandstorm");
        int storms = 0, maxLive = 0, blindTicks = 0, overlaps = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 4 == 1) // on the vision cadence: casts and storms that start on update ticks
            {
                ZoneSpam(a, open, ref rngA);
                ZoneSpam(b, open, ref rngB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            foreach (AbilityEvent e in w.AbilityEvents)
                if (e.Resolved && e.Ability == sandstorm) storms++;
            int live = 0, blockers = 0;
            bool owner1 = false, owner2 = false;
            for (int k = 0; k < z.Capacity; k++)
            {
                if (!z.Alive[k]) continue;
                live++;
                if (z.BlocksVision(k)) blockers++;
                owner1 |= z.Owner[k] == 1;
                owner2 |= z.Owner[k] == 2;
                Assert.True(z.AbilityId[k] == sandstorm && z.TicksRemaining[k] is > 0 and <= 240 && z.Owner[k] is 1 or 2,
                    $"seed {seed} tick {t}: bad zone {k}");
            }
            Assert.True(live == z.Count && blockers == z.BlockerCount, $"seed {seed} tick {t}: zone counters {z.Count}/{z.BlockerCount}, actual {live}/{blockers}");
            if (owner1 && owner2) overlaps++;
            maxLive = Math.Max(maxLive, live);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                int blind = -1;
                for (int k = 0; k < s.Count[i]; k++)
                {
                    int at = i * StatusStore.PerUnit + k;
                    Assert.True((uint)s.StatusId[at] < (uint)w.Data.Statuses.Length && s.TicksRemaining[at] > 0, $"seed {seed} tick {t}: bad status on {i}");
                    if (w.Data.Statuses[s.StatusId[at]].Kind == StatusKind.Blind) blind = s.StatusId[at];
                }
                Assert.True(s.BlindOf(i) == blind, $"seed {seed} tick {t}: unit {i} blind in force {s.BlindOf(i)}, entries say {blind}");
                if (blind >= 0) blindTicks++;
            }
        }
        _out.WriteLine($"seed {seed}: {storms} Sandstorms, at most {maxLive} live, {overlaps} ticks with both Whirlwind players' storms, {blindTicks} Blinded unit-ticks, kills {w.Kills[0]}/{w.Kills[1]}/{w.Kills[2]}");
        Assert.True(storms >= 5, $"only {storms} Sandstorms resolved"); // Priests die in the fights: about 10 a seed
        Assert.True(blindTicks > 0, "nobody was Blinded");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
