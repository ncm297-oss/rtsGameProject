using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-5 criterion 6: three hostile players on a generated map with the stealth test data (<see cref="StealthTests.Data"/>):
/// Heavy Infantry that cast the test-only stealth, Laborers that detect 10 m, Crossbowmen, Priests casting Sandstorm, and a
/// Watchtower (16 m detector) each, attack-moving into each other for 2,000 ticks. Twins hash-equal after every tick; the
/// stealth counter is the recount; no enemy sees (<c>Fog.CanSeeUnit</c>) a stealthed unit it doesn't detect; no unit or
/// tower takes a target that is hidden from its owner, and none keeps one past two scans; one seed's replay round-trips.
/// </summary>
public class StealthFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int Infantry = 8, Detectors = 3, Bowmen = 5, Priests = 2;
    private readonly ITestOutputHelper _out;

    public StealthFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => StealthTests.Data;

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(new SimConfig(seed, Players, Players * (Infantry + Detectors + Bowmen + Priests) + 8, 1024) { Data = Data });
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 451);
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"),
            bow = Data.FindUnit("malazan_crossbowman"), priest = Data.FindUnit("whirlwind_priest"), tower = Data.FindBuilding("malazan_watchtower");
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
        {
            int px = cx + (p - 1) * 10, py = cy + (p == 1 ? 8 : 0);
            int tc = Near(g, open, px, py, ref rng);
            sim.Enqueue(Command.SpawnBuilding(p, tower, g.CellCenter(tc % g.Width, tc / g.Width)));
            for (int k = 0; k < Infantry + Detectors + Bowmen + Priests; k++)
            {
                int c = Near(g, open, px, py, ref rng);
                int type = k < Infantry ? heavy : k < Infantry + Detectors ? laborer : k < Infantry + Detectors + Bowmen ? bow : priest;
                sim.Enqueue(Command.SpawnUnit(p, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
        return sim;
    }

    private static int Near(NavGrid g, List<int> open, int x, int y, ref SimRng rng)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            int px = x + rng.NextInt(-6, 7), py = y + rng.NextInt(-6, 7);
            if (px >= 0 && py >= 0 && px < g.Width && py < g.Height && g.IsPassable(px, py)) return py * g.Width + px;
        }
        return open[rng.NextInt(0, open.Count)];
    }

    private static List<int> Open(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        return open;
    }

    /// <summary>
    /// Every 150 ticks: each fighter attack-moves to a random live enemy, each Laborer walks there, each Priest casts Sandstorm
    /// on it. Every 40 ticks: each Heavy Infantry casts its stealth with chance 1/3.
    /// </summary>
    private static void Orders(Simulation sim, int t, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"), priest = Data.FindUnit("whirlwind_priest");
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            if (t % 40 == 3 && u.TypeId[i] == heavy && rng.NextInt(0, 3) == 0)
            {
                sim.Enqueue(Command.UseAbility(owner, h, 0, u.Position[i]));
                continue;
            }
            if (t % 150 != 2) continue;
            Vector2 to = u.Position[i];
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != owner) { to = u.Position[j]; break; }
            }
            if (u.TypeId[i] == laborer) sim.Enqueue(Command.Move(owner, h, to));
            else if (u.TypeId[i] == priest) sim.Enqueue(Command.UseAbility(owner, h, 0, to));
            else sim.Enqueue(Command.AttackMove(owner, h, to));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(7UL)]
    public void ThreeArmiesWithStealthDetectorsTowersAndStorms_TwinsEqual_TheRuleHolds_ReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[seed == 1UL ? 1 : 0];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        var rngA = new SimRng(seed, 452);
        var rngB = new SimRng(seed, 452);
        var prevTarget = new EntityHandle[u.Capacity];
        var hiddenTicks = new int[u.Capacity];
        var prevTower = new EntityHandle[bs.Capacity];
        int stealthedUnitTicks = 0, detectedPairs = 0, hiddenPairs = 0, reveals = 0, storms = 0, towers = 0;
        int revealed = Data.FindStatus("revealed");
        for (int t = 0; t < Ticks; t++)
        {
            Orders(a, t, ref rngA);
            Orders(b, t, ref rngB);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            int carriers = 0;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (!u.Alive[j]) continue;
                bool has = false;
                for (int k = 0; k < u.Statuses.Count[j]; k++)
                    has |= Data.Statuses[u.Statuses.StatusId[j * Abilities.StatusStore.PerUnit + k]].Kind == StatusKind.Stealthed;
                Assert.Equal(has, u.Statuses.HasStealth(j));
                if (has) carriers++;
                int r = u.Statuses.IndexOf(j, revealed);
                if (r >= 0 && u.Statuses.TicksRemaining[r] == Data.Rules.RevealTicks) reveals++;
                if (!w.IsStealthed(j)) continue;
                stealthedUnitTicks++;
                for (int p = 0; p < Players; p++)
                {
                    if (p == u.Owner[j]) continue;
                    if (w.IsDetectedBy(p, j)) detectedPairs++;
                    else
                    {
                        hiddenPairs++;
                        Assert.False(w.Fog.CanSeeUnit(p, j), $"seed {seed} tick {t}: player {p} sees undetected stealthed {j}");
                    }
                }
            }
            Assert.Equal(carriers, u.Statuses.StealthedUnits);
            for (int i = 0; i < u.Capacity; i++)
            {
                EntityHandle target = u.Alive[i] && !u.TargetIsBuilding[i] ? u.Target[i] : default;
                bool live = target.Generation != 0 && u.IsAlive(target);
                bool hidden = live && w.HiddenFrom(u.Owner[i], target.Index);
                // A new target is never one hidden from the unit's owner (the fuzz gives no explicit Attack).
                if (live && target != prevTarget[i]) Assert.False(hidden, $"seed {seed} tick {t}: unit {i} took hidden {target.Index}");
                // A kept one that went hidden is dropped by the lost-from-sight rule within two scans.
                hiddenTicks[i] = hidden ? hiddenTicks[i] + 1 : 0;
                Assert.True(hiddenTicks[i] <= 2 * CombatConstants.ScanInterval, $"seed {seed} tick {t}: unit {i} keeps hidden {target.Index}");
                prevTarget[i] = live ? target : default;
            }
            for (int k = 0; k < bs.Capacity; k++)
            {
                EntityHandle target = bs.Alive[k] ? bs.TowerTarget[k] : default;
                bool live = target.Generation != 0 && u.IsAlive(target);
                if (live && target != prevTower[k]) Assert.False(w.HiddenFrom(bs.Owner[k], target.Index), $"seed {seed} tick {t}: tower {k} took hidden {target.Index}");
                prevTower[k] = live ? target : default;
            }
            if (w.Zones.BlockerCount > 0) storms++;
        }
        for (int k = 0; k < bs.Capacity; k++)
            if (bs.Alive[k]) towers++;
        _out.WriteLine($"seed {seed}: {stealthedUnitTicks} stealthed unit-ticks, {detectedPairs} detected / {hiddenPairs} hidden (unit, enemy) ticks, {reveals} reveals, {storms} storm ticks, {towers} towers standing, units left {u.Count}");
        Assert.True(stealthedUnitTicks > 100, $"only {stealthedUnitTicks} stealthed unit-ticks");
        Assert.True(detectedPairs > 0 && hiddenPairs > 0, $"{detectedPairs} detected, {hiddenPairs} hidden");
        Assert.True(reveals > 0, "no reveal");
        Assert.True(storms > 0, "no storm");
        if (rec.Length == 0) return;
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
        Assert.Equal(Ticks, result.TicksRun);
    }
}
