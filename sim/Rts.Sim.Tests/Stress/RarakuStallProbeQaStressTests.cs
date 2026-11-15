using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M6-S10 (session 2026-11-15-0506): a probe of BUG-1460 (Raraku Normal v Normal seed 1 no longer ends inside 72,000).
/// Report only. On the shipped data (the cure + 0.5) and on the knob-off copy (the cure alone) it watches every worker of both
/// seats for the brief's "held" (a live worker on its loop, node alive, 1,200 ticks without a delivery, within 2 m of where it
/// stood 600 ticks earlier) over the whole match, and every 6,000 ticks prints each seat's bank, workers (on a loop / idle),
/// army, buildings (and how many can train a worker), and for idle workers the distance from their nearest own drop-off to the
/// nearest live tree and gold mine: why a winner's workers stand idle.
/// </summary>
[Trait("Category", "Stress")]
public class RarakuStallProbeQaStressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1UL, 0.5)]
    [InlineData(1UL, 0.0)]
    [InlineData(3UL, 0.5)]
    public void Raraku_Probe(ulong seed, double overlap)
    {
        GameData data = overlap == 0 ? GatherOverlapScenes.Off : TestSim.Data;
        var seats = new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal, 0), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal, 0) };
        int size = data.Maps[data.FindMap("raraku")].Size;
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, units: 400, data: data,
            configure: c => c with { MapId = "raraku", Map = c.Map with { Width = size, Height = size } });
        World w = sim.World;
        UnitStore u = w.Units;
        const int Ring = 601;
        var trail = new Vector2[u.Capacity, Ring];
        var last = new int[u.Capacity];
        var cargo = new int[u.Capacity];
        var node = new EntityHandle[u.Capacity];
        var gen = new int[u.Capacity];
        var held = new List<string>();
        CriterionMatch.Result r = CriterionMatch.Play(sim, 72_000, checks: false, onTick: s =>
        {
            int now = s.TickNumber;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                if (u.Generation[i] != gen[i]) { gen[i] = u.Generation[i]; last[i] = now; node[i] = default; cargo[i] = 0; }
                trail[i, now % Ring] = u.Position[i];
                EntityHandle nd = u.GatherNode[i];
                if (nd != node[i] || nd.Generation == 0 || !w.Resources.IsAlive(nd)) last[i] = now;
                if (cargo[i] > 0 && u.Cargo[i] == 0) last[i] = now;
                cargo[i] = u.Cargo[i];
                node[i] = nd;
                if (now - last[i] == 1201)
                {
                    float moved = Vector2.Distance(trail[i, (now - 600) % Ring], u.Position[i]);
                    if (moved <= 2f)
                        held.Add($"tick {now}: unit {i} p{u.Owner[i]} {u.State[i]} at {u.Position[i]} cargo {u.Cargo[i]} goal {u.Goal[i]} stuck {u.StuckTicks[i]}");
                }
            }
            if (now % 6000 != 0) return;
            string line = $"[{overlap}] tick {now}:";
            for (int p = 0; p < 2; p++)
            {
                int workers = 0, loop = 0, others = 0, buildings = 0, trainsWorker = 0, dropOffs = 0;
                float idleTree = float.NaN, idleGold = float.NaN;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != p) continue;
                    if (w.Data.Units[u.TypeId[i]].IsWorker)
                    {
                        workers++;
                        if (u.GatherNode[i].Generation != 0) loop++;
                    }
                    else others++;
                }
                var drops = new List<Vector2>();
                for (int k = 0; k < w.Buildings.Capacity; k++)
                {
                    if (!w.Buildings.Alive[k] || w.Buildings.Owner[k] != p) continue;
                    buildings++;
                    BuildingDef def = w.Data.Buildings[w.Buildings.TypeId[k]];
                    foreach (int t in w.Data.UnitsTrainedAt(w.Buildings.TypeId[k]))
                        if (w.Data.Units[t].IsWorker && !w.Buildings.UnderConstruction[k]) { trainsWorker++; break; }
                    if (def.DropOff && !w.Buildings.UnderConstruction[k])
                    {
                        dropOffs++;
                        drops.Add(Economy.EconomySystem.Center(w.NavGrid, w.Buildings.Cell[k], def.FootprintWidth, def.FootprintHeight));
                    }
                }
                if (workers > loop && drops.Count > 0)
                {
                    idleTree = Nearest(w, drops, ResourceKind.Wood);
                    idleGold = Nearest(w, drops, ResourceKind.Gold);
                }
                line += $" p{p}: gold {w.Gold[p]} wood {w.Wood[p]}, workers {workers} (loop {loop}), army {others}, buildings {buildings} (worker-trainers {trainsWorker}, drop-offs {dropOffs})"
                    + (float.IsNaN(idleTree) ? "" : $", nearest wood to a drop-off {idleTree:F1} m, gold {idleGold:F1} m") + ";";
            }
            output.WriteLine(line);
        });
        output.WriteLine($"[{overlap}] seed {seed}: over {r.Over} at {r.EndTick}, winner {r.Winner}, waves {r.Waves}, hunts {r.Hunts}; held workers {held.Count}");
        foreach (string h in held) output.WriteLine("  HELD " + h);
    }

    private static float Nearest(World w, List<Vector2> from, ResourceKind kind)
    {
        float best = float.PositiveInfinity;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Data.Resources[w.Resources.TypeId[n]].Resource != kind) continue;
            int c = w.Resources.Cell[n];
            Vector2 at = w.NavGrid.CellCenter(c % w.NavGrid.Width, c / w.NavGrid.Width);
            foreach (Vector2 f in from) best = MathF.Min(best, Vector2.Distance(at, f));
        }
        return best;
    }
}
