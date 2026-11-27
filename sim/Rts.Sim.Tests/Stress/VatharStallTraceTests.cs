using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-8 (BUG-1621 / BUG-1610, rule 57): the trace of the four-seat Vathar Crossing free-for-all on seed 2 (every seat a
/// Normal AI; <see cref="FarExpandProbeQaStressTests"/>' set-up, so the same match: 88,998 ticks, seat 3 the winner). A
/// measurement with no bound: it plays the match to its end (at most 144,000 ticks) and prints, every 2,000 ticks from
/// 28,000 to 40,000, at 60,000 and at the end, one line per live seat: gold and wood, gold gathered so far, workers (on gold /
/// on wood / other), army units and half-pop, finished halls and unit-producing buildings (sites aside), the gold nodes
/// the seat's view knows that are live and not marked dropped (and the live gold nodes on the map), the reserved step of the
/// seat's last think (or the hall rebuild's), what the military manager is doing (defend / hunt / out on a wave / gathering)
/// and the tick of the seat's last command. Then, to find why the match lasts: every 4,000 ticks from 32,000 each standing
/// seat's buildings (cell, and whether each other standing seat sees or has explored it) and a worker's place; each seat's
/// commands from 30,000 to 41,000 and its defeat tick; and from 30,000 a timeline of every change of a standing seat's
/// wave / hunt state, with, while it hunts, the sweep point nearest each enemy Town Hall and whether this hunt visited it.
/// Nothing in the match is changed: the hooks only read. docs/03 "Implementation (M7-8)" reads the cause from it.
/// </summary>
[Trait("Category", "Stress")]
public class VatharStallTraceTests(ITestOutputHelper output)
{
    private const int Bound = 144_000;

    private sealed class SeatTrace
    {
        public int LastSent = -1, ReserveStep = -1, Thinks;
        public bool HallReserved;
        public int ReserveGold, ReserveWood;
    }

    private static bool Sampled(int tick) => tick is >= 28_000 and <= 40_000 && tick % 2_000 == 0 || tick == 60_000;

    [Fact]
    public void VatharCrossingSeed2_FourSeats_Trace()
    {
        const int players = 4;
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal, 0);
        Simulation sim = HuntStressTests.Match("vathar_crossing", seats, 2UL, units: 1024);
        World w = sim.World;
        var trace = new SeatTrace[players];
        for (int p = 0; p < players; p++)
        {
            SeatTrace t = trace[p] = new SeatTrace();
            AiPlayer ai = sim.AiOf(p)!;
            output.WriteLine($"seat {p}: faction {w.Data.Factions[w.FactionOf(p)].Key}, opening {ai.Opening.Id}");
            ai.SentForTests = c => t.LastSent = c.Tick;
            ai.ThoughtForTests = a =>
            {
                t.Thinks++;
                t.ReserveStep = a.ReserveStep;
                t.HallReserved = a.HallReserved;
                t.ReserveGold = a.ReserveGold;
                t.ReserveWood = a.ReserveWood;
            };
        }
        output.WriteLine("| tick | seat | gold / wood | gold gathered | workers (gold / wood / other) | army (units, pop) | halls | producers | known live gold nodes (map) | reserved step | military | last command |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        var fell = new int[players];
        Array.Fill(fell, -1);
        var hunt = new List<string>();
        var sent = new List<string>[players];
        for (int p = 0; p < players; p++)
        {
            int player = p;
            sent[p] = new List<string>();
            Action<Command>? inner = sim.AiOf(p)!.SentForTests;
            sim.AiOf(p)!.SentForTests = c =>
            {
                inner?.Invoke(c);
                if (c.Tick is >= 30_000 and <= 41_000 && sent[player].Count < 40)
                    sent[player].Add($"{c.Tick} {c.Kind}{(c.Kind is CommandKind.Train or CommandKind.Build ? " " + (c.Kind == CommandKind.Train ? w.Data.Units[c.TypeId].Key : w.Data.Buildings[c.TypeId].Key) : "")}");
            };
        }
        // The hunter's timeline once two seats are left: every change of its wave state, sweep point or known enemy buildings.
        var timeline = new List<string>();
        var lastKey = new string[players];
        while (!sim.MatchOver && sim.TickNumber < Bound)
        {
            sim.Tick();
            for (int p = 0; p < players; p++) if (fell[p] < 0 && w.IsDefeated(p)) fell[p] = sim.TickNumber;
            if (sim.TickNumber >= 30_000)
                for (int p = 0; p < players; p++)
                {
                    if (w.IsDefeated(p)) continue;
                    Blackboard bb = sim.AiOf(p)!.Board;
                    string key = $"wave {bb.WaveActive} hunt {bb.Hunting} sweep {bb.HuntSweep} ring {bb.HuntRing} pass {bb.HuntPass} hunts {bb.HuntsStarted} known {bb.KnownEnemyBuildings} target ({bb.WaveTargetPoint.X:0},{bb.WaveTargetPoint.Y:0}) defending {bb.Defending}"
                        + (bb.Hunting ? HallPoints(sim, p) : "");
                    if (key == lastKey[p] || timeline.Count >= 600) continue;
                    lastKey[p] = key;
                    timeline.Add($"{sim.TickNumber} seat {p}: {key}");
                }
            if (Sampled(sim.TickNumber)) Lines(sim, trace);
            if (sim.TickNumber >= 30_000 && sim.TickNumber % 4_000 == 0) hunt.Add(HuntLine(sim));
        }
        Lines(sim, trace);
        output.WriteLine("");
        output.WriteLine("The end game, every 4,000 ticks from 32,000: each standing seat's military state, its wave's target, its buildings (type @ cell, and whether each other standing seat sees it now, has explored its cell, or never) and a worker.");
        foreach (string l in hunt) output.WriteLine(l);
        output.WriteLine("");
        for (int p = 0; p < players; p++)
            output.WriteLine($"seat {p}: defeated at {fell[p]}; commands 30,000-41,000 (first 40): {string.Join(", ", sent[p])}");
        output.WriteLine("");
        for (int p = 0; p < players; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            int start = p < ai.View.StartCount ? ai.View.StartCell(p) : -1;
            output.WriteLine($"seat {p}: start cell ({start % w.NavGrid.Width},{start / w.NavGrid.Width})");
        }
        output.WriteLine("Timeline from 30,000 (every change of a standing seat's wave / hunt state):");
        foreach (string l in timeline) output.WriteLine(l);
        output.WriteLine($"end: tick {sim.TickNumber}, over {sim.MatchOver}, hash {sim.StateHash():X16}");
        Assert.True(sim.TickNumber > 40_000, "the trace reaches past the silent span");
    }

    /// <summary>For a hunting seat: each enemy Town Hall standing (the world's truth), the hunt's sweep point nearest its centre, how far, and whether this hunt has visited it.</summary>
    private static string HallPoints(Simulation sim, int p)
    {
        World w = sim.World;
        AiPlayer ai = sim.AiOf(p)!;
        int cols = ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);
        int points = MilitaryManager.HuntPoints(cols, rows);
        var sb = new System.Text.StringBuilder(" | halls:");
        var b = w.Buildings;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (!b.Alive[s] || b.Owner[s] == p || b.UnderConstruction[s] || w.Data.Buildings[b.TypeId[s]].Slot != BuildingSlot.TownHall) continue;
            var centre = ai.BuildingCentre(b.TypeId[s], b.Cell[s]);
            int best = -1;
            float least = float.MaxValue;
            for (int k = 0; k < points; k++)
            {
                float d = System.Numerics.Vector2.Distance(centre, MilitaryManager.HuntPoint(ai, cols, rows, k));
                if (d < least) (least, best) = (d, k);
            }
            sb.Append($" p{b.Owner[s]} hall ({centre.X:0},{centre.Y:0}): point {best} {least:0} m visited {ai.Board.HuntVisited[best]}");
        }
        return sb.ToString();
    }

    private static string HuntLine(Simulation sim)
    {
        World w = sim.World;
        var sb = new System.Text.StringBuilder($"{sim.TickNumber}:");
        int width = w.NavGrid.Width;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p)) continue;
            Blackboard bb = sim.AiOf(p)!.Board;
            sb.Append($" seat {p} [{(bb.Defending ? "defend" : bb.WaveActive && bb.Hunting ? "hunt" : bb.WaveActive ? "out" : "gathering")}, target ({bb.WaveTargetPoint.X:0},{bb.WaveTargetPoint.Y:0}) sweep {bb.HuntSweep} pass {bb.HuntPass} ring {bb.HuntRing}; buildings:");
            var b = w.Buildings;
            int listed = 0;
            for (int s = 0; s < b.Capacity && listed < 6; s++)
            {
                if (!b.Alive[s] || b.Owner[s] != p) continue;
                listed++;
                int cell = b.Cell[s];
                string seen = "";
                for (int q = 0; q < w.Config.PlayerCount; q++)
                    if (q != p && !w.IsDefeated(q)) seen += $" p{q}:{(w.Fog.CanSeeBuilding(q, s) ? "sees" : w.Fog.IsExplored(q, cell) ? "explored" : "never")}";
                sb.Append($" {w.Data.Buildings[b.TypeId[s]].Key}{(b.UnderConstruction[s] ? "(site)" : "")}@({cell % width},{cell / width}){seen}");
            }
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
                if (u.Alive[s] && u.Owner[s] == p && EconomySystem.IsWorker(w, s))
                {
                    sb.Append($"; a worker at ({u.Position[s].X:0},{u.Position[s].Y:0}) {u.State[s]}");
                    break;
                }
            sb.Append("]");
        }
        return sb.ToString();
    }

    private void Lines(Simulation sim, SeatTrace[] trace)
    {
        World w = sim.World;
        GameData d = w.Data;
        int mapGold = 0;
        var res = w.Resources;
        for (int i = 0; i < res.Capacity; i++)
            if (res.Alive[i] && res.Remaining[i] > 0 && d.Resources[res.TypeId[i]].Resource == ResourceKind.Gold) mapGold++;
        for (int p = 0; p < trace.Length; p++)
        {
            if (w.IsDefeated(p)) continue;
            AiPlayer ai = sim.AiOf(p)!;
            SeatTrace t = trace[p];
            int onGold = 0, onWood = 0, other = 0, army = 0, armyHalf = 0;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != p) continue;
                if (EconomySystem.IsWorker(w, s))
                {
                    var node = u.GatherNode[s];
                    if (node.Index >= 0 && w.Resources.IsAlive(node) && (u.State[s] is Rts.Sim.Entities.UnitState.Gathering or Rts.Sim.Entities.UnitState.Returning or Rts.Sim.Entities.UnitState.Moving))
                    {
                        if (d.Resources[w.Resources.TypeId[node.Index]].Resource == ResourceKind.Gold) onGold++;
                        else onWood++;
                    }
                    else other++;
                    continue;
                }
                army++;
                armyHalf += d.Units[u.TypeId[s]].HalfPop;
            }
            int halls = 0, producers = 0;
            var b = w.Buildings;
            for (int s = 0; s < b.Capacity; s++)
            {
                if (!b.Alive[s] || b.Owner[s] != p || b.UnderConstruction[s]) continue;
                BuildingDef def = d.Buildings[b.TypeId[s]];
                if (def.Slot == BuildingSlot.TownHall) halls++;
                bool trainsArmy = false;
                foreach (int ut in d.UnitsTrainedAt(def.Id)) if (d.Units[ut].Slot != UnitSlot.Worker) trainsArmy = true;
                if (trainsArmy) producers++;
            }
            int known = 0, r = -1;
            while (ai.View.NextResource(ref r, out SeenResource n))
                if (d.Resources[n.TypeId].Resource == ResourceKind.Gold && ai.Board.NodeDropped[r] != n.Handle.Generation && w.Resources.IsAlive(n.Handle)) known++;
            string reserve = t.HallReserved ? $"hall rebuild ({t.ReserveGold} / {t.ReserveWood})"
                : t.ReserveStep >= 0 ? $"#{t.ReserveStep} {Name(ai.Steps[t.ReserveStep])} ({t.ReserveGold} / {t.ReserveWood})" : "none";
            Blackboard bb = ai.Board;
            string military = bb.Defending ? "defend" : bb.WaveActive && bb.Hunting ? "hunt" : bb.WaveActive ? "out" : "wave gathering";
            output.WriteLine($"| {sim.TickNumber} | {p} | {w.Gold[p]} / {w.Wood[p]} | {w.GatheredGold[p]} | {onGold + onWood + other} ({onGold} / {onWood} / {other}) | {army}, {armyHalf / 2.0} | {halls} | {producers} | "
                + $"{known} ({mapGold}) | {reserve} | {military} (waves {bb.WavesStarted}, hunts {bb.HuntsStarted}) | {t.LastSent} |");
        }
    }

    private static string Name(AiStep s) => s.Action == AiStepAction.Expand ? "expand" : $"{s.Action.ToString().ToLowerInvariant()} {s.TargetKey}";
}
