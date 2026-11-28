using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S15 B (BUG-1891), trace first: the four-seat Pale Hills free-for-all seed 4 (<see cref="DrawStalledRowsReportTests.Build"/>),
/// from 48,000 to 72,000 every 6,000 ticks, why the gold left on the map is not mined and why loaded workers do not deliver.
/// Per live gold node: its cell and gold; per undefeated seat, at its think on that tick: whether its view knows the node and
/// with what gold, its dropped mark, its gather reach (<see cref="AiPlayer.InGatherReach"/>, <see cref="AiPlayer.ReachLifted"/>),
/// the distance to its nearest finished drop-off, the defense, <see cref="AiPlayer.KnowsNode"/>; its workers by state and the
/// kind they were last sent to; and each loaded worker's state, place and cargo. A measurement with no bound; it reads only
/// what the base d26338d3 has, so it runs on the base too.
/// </summary>
[Trait("Category", "Stress")]
public class GoldLeftTraceTests(ITestOutputHelper output)
{
    [Fact]
    public void PaleHillsFourSeats_Seed4_TheGoldLeftAndTheLoadedWorkers()
    {
        Simulation sim = DrawStalledRowsReportTests.Build("pale_hills", 4, 4, null);
        World w = sim.World;
        int players = w.Config.PlayerCount;
        var gathers = new int[players];
        for (int p = 0; p < players; p++)
        {
            int player = p;
            sim.AiOf(p)!.SentForTests = c =>
            {
                if (c.Tick < 48_000 || c.Kind != CommandKind.Gather) return;
                gathers[player]++;
            };
        }
        var lines = new List<string>();
        for (int p = 0; p < players; p++)
        {
            int player = p;
            sim.AiOf(p)!.ThoughtForTests = ai =>
            {
                if (ai.Now < 48_000 || ai.Now % 6000 != 0) return;
                lines.Add(Seat(w, ai, player));
            };
        }
        while (!sim.MatchOver && sim.TickNumber < 72_001)
        {
            sim.Tick();
            if (sim.TickNumber >= 48_000 && sim.TickNumber % 6000 == 1)
            {
                foreach (string l in lines) output.WriteLine(l);
                lines.Clear();
                output.WriteLine($"GOLDTRACE tick {sim.TickNumber - 1}: map gold {w.Resources.GoldLeft}; nodes: {Nodes(w)}; Gathers sent from 48,000 per seat: {string.Join(" / ", gathers)}");
            }
        }
    }

    private static string Nodes(World w)
    {
        var parts = new List<string>();
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Data.Resources[w.Resources.TypeId[n]].Resource != ResourceKind.Gold) continue;
            int cell = w.Resources.Cell[n], width = w.NavGrid.Width;
            parts.Add($"slot {n} at ({cell % width}, {cell / width}) {w.Resources.Remaining[n]} gold");
        }
        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static string Seat(World w, AiPlayer ai, int p)
    {
        if (w.IsDefeated(p)) return $"GOLDTRACE   p{p} out";
        var s = new System.Text.StringBuilder();
        s.Append($"GOLDTRACE   p{p} tick {ai.Now}: bank {w.Gold[p]} g / {w.Wood[p]} w, workers {ai.WorkerCount}, has drop-off {ai.HasDropOff}, defending {ai.Board.Defending}, reach lifted {ai.ReachLifted()}, KnowsNode(gold) {ai.KnowsNode(ResourceKind.Gold)}, short of gold for the pick {ai.PickShortOf(ResourceKind.Gold)}, scout phase {ai.Board.ScoutPhase} (blind re-scouts lost {ai.Board.BlindScoutsLost}, one out {ai.Board.BlindScoutOut}), far phase {ai.Board.FarPhase}; ");
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Data.Resources[w.Resources.TypeId[n]].Resource != ResourceKind.Gold) continue;
            bool known = ai.View.TryGetResource(n, out SeenResource seen);
            Vector2 centre = ai.NodeCentre(w.Resources.TypeId[n], w.Resources.Cell[n]);
            s.Append($"[node {n}: explored {ai.View.IsExplored(w.Resources.Cell[n])}, known {known}{(known ? $" ({seen.Remaining} g seen)" : "")}, dropped mark {(ai.Board.NodeDropped[n] == w.Resources.Generation[n] ? "yes" : "no")}, in reach {ai.InGatherReach(centre)}, nearest drop-off {NearestDropOff(w, p, centre):F0} m] ");
        }
        var states = new SortedDictionary<string, int>();
        var loaded = new List<string>();
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            int slot = ai.WorkerSlots[k];
            string kind = (ai.Board.GatherKind[slot] & 3) switch { 1 => "gold", 2 => "wood", _ => "-" };
            string key = $"{w.Units.State[slot]}/{kind}";
            states[key] = states.GetValueOrDefault(key) + 1;
            if (w.Units.Cargo[slot] > 0)
                loaded.Add($"#{slot} {w.Units.State[slot]} {w.Units.CargoKind[slot]} {w.Units.Cargo[slot]} at ({w.Units.Position[slot].X:F0}, {w.Units.Position[slot].Y:F0}) drop-off {NearestDropOff(w, p, w.Units.Position[slot]):F0} m, last sent {kind}");
        }
        s.Append("workers: ").Append(string.Join(", ", states.Select(kv => $"{kv.Key} {kv.Value}")));
        if (loaded.Count > 0) s.Append("; loaded: ").Append(string.Join("; ", loaded.Take(8)));
        return s.ToString();
    }

    /// <summary>Metres from <paramref name="at"/> to seat <paramref name="p"/>'s nearest finished drop-off's centre (-1 with none).</summary>
    private static float NearestDropOff(World w, int p, Vector2 at)
    {
        float best = -1f;
        for (int k = 0; k < w.Buildings.Capacity; k++)
        {
            if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p || w.Buildings.UnderConstruction[k]) continue;
            BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[k]];
            if (!d.DropOff) continue;
            int cell = w.Buildings.Cell[k], width = w.NavGrid.Width;
            var c = new Vector2(cell % width + d.FootprintWidth * 0.5f, cell / width + d.FootprintHeight * 0.5f) * Map.MapConstants.CellSize;
            float dist = Vector2.Distance(c, at);
            if (best < 0 || dist < best) best = dist;
        }
        return best;
    }
}
