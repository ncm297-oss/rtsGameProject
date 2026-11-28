using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Tests.Draw;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-SH3 A (BUG-1855): the trace of the Pale Hills Normal v Normal match on seed 7 (seed 8 is the same match), built as
/// <see cref="MapFileTests.MapMatch"/> builds it, to 72,000 ticks. A measurement with no bound, modelled on
/// <see cref="VatharStallTraceTests"/>: every 6,000 ticks from 48,000 one line per seat (bank, workers, army, queued units,
/// wave / starved state, hunts begun, enemy buildings known, the map's gold left); at the end <c>MatchSpent</c> and each
/// seat's "can still train" with its terms (<see cref="DrawReachScan"/>); then each seat's buildings with whether the other
/// seat sees or has explored them, its army's place, and a timeline of every change of a seat's wave / hunt state from 48,000.
/// The hooks only read. docs/03 "Implementation (M6-SH3)" A reads the cause from it.
/// </summary>
[Trait("Category", "Stress")]
public class PaleHillsStallTraceTests(ITestOutputHelper output)
{
    private const int Bound = 72_000;

    [Fact]
    public void PaleHillsSeed7_NormalVersusNormal_Trace()
    {
        Simulation sim = MapFileTests.MapMatch(MapFileTests.PaleHills, 7UL, false, out _);
        World w = sim.World;
        const int players = 2;
        var lastSent = new int[players];
        var lastKind = new string[players];
        for (int p = 0; p < players; p++)
        {
            int player = p;
            AiPlayer ai = sim.AiOf(p)!;
            output.WriteLine($"seat {p}: faction {w.Data.Factions[w.FactionOf(p)].Key}, opening {ai.Opening.Id}");
            lastSent[p] = -1;
            lastKind[p] = "-";
            ai.SentForTests = c =>
            {
                lastSent[player] = c.Tick;
                lastKind[player] = c.Kind.ToString();
            };
        }
        output.WriteLine("| tick | seat | gold / wood | workers | army (units, pop) | pop / cap | queued units | wave | starved since (due) | hunts | known enemy buildings | map gold left | last command |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        var timeline = new List<string>();
        var lastKey = new string[players];
        var places = new List<string>();
        while (!sim.MatchOver && sim.TickNumber < Bound)
        {
            sim.Tick();
            if (sim.TickNumber >= 48_000)
                for (int p = 0; p < players; p++)
                {
                    if (w.IsDefeated(p)) continue;
                    Blackboard bb = sim.AiOf(p)!.Board;
                    string key = $"wave {bb.WaveActive} hunt {bb.Hunting} sweep {bb.HuntSweep} pass {bb.HuntPass} hunts {bb.HuntsStarted} known {bb.KnownEnemyBuildings} target ({bb.WaveTargetPoint.X:0},{bb.WaveTargetPoint.Y:0}) defending {bb.Defending} starved {bb.StarvedSince}";
                    if (key == lastKey[p] || timeline.Count >= 400) continue;
                    lastKey[p] = key;
                    timeline.Add($"{sim.TickNumber} seat {p}: {key}");
                }
            if (sim.TickNumber >= 48_000 && sim.TickNumber % 6_000 == 0)
            {
                Lines(sim, lastSent, lastKind);
                places.Add(Places(sim));
            }
        }
        if (sim.TickNumber % 6_000 != 0) Lines(sim, lastSent, lastKind);
        output.WriteLine("");
        output.WriteLine($"end: tick {sim.TickNumber}, over {sim.MatchOver}, spent {w.MatchSpent}, hash {sim.StateHash():X16}");
        for (int p = 0; p < players; p++) output.WriteLine($"  can still train: {DrawReachScan.Line(w, p)}; World.CanStillTrain {w.CanStillTrain(p)}");
        output.WriteLine("");
        output.WriteLine("Every 6,000 ticks from 48,000: each seat's buildings (type @ cell; whether the other seat sees it, has explored its cell, or never), its army's centre and spread, and its workers' states.");
        foreach (string l in places) output.WriteLine(l);
        output.WriteLine("");
        output.WriteLine("Timeline from 48,000 (every change of a seat's wave / hunt state, first 400):");
        foreach (string l in timeline) output.WriteLine(l);
        Assert.True(sim.TickNumber > 48_000, "the trace reaches the sampled span");
    }

    private void Lines(Simulation sim, int[] lastSent, string[] lastKind)
    {
        World w = sim.World;
        GameData d = w.Data;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p))
            {
                output.WriteLine($"| {sim.TickNumber} | {p} | out | | | | | | | | | | |");
                continue;
            }
            AiPlayer ai = sim.AiOf(p)!;
            Blackboard bb = ai.Board;
            int workers = 0, army = 0, armyHalf = 0;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                if (EconomySystem.IsWorker(w, s)) workers++;
                else
                {
                    army++;
                    armyHalf += d.Units[u.TypeId[s]].HalfPop;
                }
            }
            string wave = bb.Defending ? "defend" : bb.WaveActive && bb.Hunting ? "hunt" : bb.WaveActive ? "out" : "gathering";
            output.WriteLine($"| {sim.TickNumber} | {p} | {w.Gold[p]} / {w.Wood[p]} | {workers} | {army}, {armyHalf / 2.0} | {w.HalfPop[p] / 2.0} / {w.HalfPopCap[p] / 2.0} | {w.Ledger.QueuedUnits[p]} | {wave} (waves {bb.WavesStarted}) | "
                + $"{bb.StarvedSince} ({ai.StarvedWaveDue}) | {bb.HuntsStarted} | {bb.KnownEnemyBuildings} | {w.Resources.GoldLeft} | {lastSent[p]} {lastKind[p]} |");
        }
    }

    private static string Places(Simulation sim)
    {
        World w = sim.World;
        GameData d = w.Data;
        int width = w.NavGrid.Width;
        var sb = new System.Text.StringBuilder($"{sim.TickNumber}:");
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p)) continue;
            sb.Append($" seat {p} [buildings:");
            var b = w.Buildings;
            for (int s = 0; s < b.Capacity; s++)
            {
                if (!b.Alive[s] || b.Owner[s] != p) continue;
                int cell = b.Cell[s];
                string seen = "";
                for (int q = 0; q < w.Config.PlayerCount; q++)
                    if (q != p && !w.IsDefeated(q)) seen = w.Fog.CanSeeBuilding(q, s) ? "sees" : w.Fog.IsExplored(q, cell) ? "explored" : "never";
                sb.Append($" {d.Buildings[b.TypeId[s]].Key}{(b.UnderConstruction[s] ? "(site)" : "")}@({cell % width},{cell / width}):{seen}");
            }
            var u = w.Units;
            float sx = 0, sy = 0, far = 0;
            int n = 0, idle = 0, gathering = 0, other = 0;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                if (EconomySystem.IsWorker(w, s))
                {
                    if (u.State[s] == Entities.UnitState.Idle) idle++;
                    else if (EconomySystem.OnLoop(u, s)) gathering++;
                    else other++;
                    continue;
                }
                sx += u.Position[s].X;
                sy += u.Position[s].Y;
                n++;
            }
            if (n > 0)
            {
                var c = new System.Numerics.Vector2(sx / n, sy / n);
                for (int s = 0; s < u.Capacity; s++)
                    if (u.Alive[s] && u.Owner[s] == p && !EconomySystem.IsWorker(w, s)) far = Math.Max(far, System.Numerics.Vector2.Distance(c, u.Position[s]));
                sb.Append($"; army {n} at ({c.X:0},{c.Y:0}) within {far:0} m");
            }
            else sb.Append("; no army");
            sb.Append($"; workers idle {idle}, on a loop {gathering}, other {other}]");
        }
        return sb.ToString();
    }
}
