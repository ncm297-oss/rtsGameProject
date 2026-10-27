using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2: a scripted player for the round-trip scenes, the command stream that "gathers, builds, trains, researches,
/// attack-moves and casts" (criterion 2). It reads the reference sim between ticks and returns commands; the harness
/// enqueues the same ones into every load, so the script itself never needs to agree with a load. Every rule is on a fixed
/// tick pattern per player and picks units and spots in slot / cell order, so a run is the same every time.
/// </summary>
internal sealed class MatchScript
{
    private readonly int _players;
    private readonly int _commandBudget;

    /// <summary>A script for players 0 to <paramref name="players"/> - 1, at most <paramref name="commandBudget"/> commands a tick.</summary>
    internal MatchScript(int players, int commandBudget = 64)
    {
        _players = players;
        _commandBudget = commandBudget;
    }

    /// <summary>This tick's commands for every undefeated player.</summary>
    internal void Run(Simulation sim, List<Command> output)
    {
        World w = sim.World;
        int t = sim.TickNumber;
        for (int p = 0; p < _players; p++)
        {
            if (w.IsDefeated(p) || output.Count >= _commandBudget) continue;
            if (t % 20 == p) Gather(w, p, output);
            if (t % 150 == 10 + p) Build(w, p, output);
            if (t % 50 == 30 + p && !Has(w, p, BuildingSlot.Forge)) Build(w, p, output, devSpawn: BuildingSlot.Forge); // a Forge to research at
            if (t % 60 == 5 + p) Produce(w, p, output, research: t % 120 == 65 + p); // every other turn researches first
            if (t % 200 == 100 + p) AttackMove(w, p, output);
            if (t % 40 == 7 + p) Cast(w, p, output);
        }
    }

    /// <summary>The centre of player <paramref name="p"/>'s first live building in slot order, or null.</summary>
    internal static Vector2? HomeOf(World w, int p)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == p)
                return StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[k]], b.Cell[k]);
        return null;
    }

    /// <summary>Idle workers go to the nearest node: even slots a mine, odd slots a tree (either when the other kind is gone).</summary>
    private void Gather(World w, int p, List<Command> output)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity && output.Count < _commandBudget; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != p || u.State[i] != UnitState.Idle || !w.Data.Units[u.TypeId[i]].IsWorker) continue;
            Vector2? node = NearestNode(w, u.Position[i], i % 2 == 0 ? ResourceKind.Gold : ResourceKind.Wood) ?? NearestNode(w, u.Position[i], null);
            if (node != null) output.Add(Command.Gather(p, new EntityHandle(i, u.Generation[i]), node.Value));
        }
    }

    private static Vector2? NearestNode(World w, Vector2 from, ResourceKind? kind)
    {
        ResourceStore r = w.Resources;
        NavGrid g = w.NavGrid;
        Vector2? best = null;
        float bestD = float.MaxValue;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || (kind != null && w.Data.Resources[r.TypeId[k]].Resource != kind)) continue;
            Vector2 at = g.CellCenter(r.Cell[k] % g.Width, r.Cell[k] / g.Width);
            float d = Vector2.DistanceSquared(at, from);
            if (d < bestD)
            {
                bestD = d;
                best = at;
            }
        }
        return best;
    }

    // The buildings the script places, in turn (the faction's type in each slot).
    private static readonly BuildingSlot[] s_buildOrder =
        { BuildingSlot.House, BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.Forge, BuildingSlot.House, BuildingSlot.Camp };

    /// <summary>
    /// The player's first worker (slot order) places the next building of <see cref="s_buildOrder"/> on the first spot near
    /// home that <see cref="World.CanPlace"/> allows; with <paramref name="devSpawn"/> a finished building of that slot is
    /// dev-spawned there instead.
    /// </summary>
    private static void Build(World w, int p, List<Command> output, BuildingSlot? devSpawn = null)
    {
        Vector2? home = HomeOf(w, p);
        int worker = FirstWorker(w, p);
        if (home == null || worker < 0) return;
        int built = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p) built++;
        int type = StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), devSpawn ?? s_buildOrder[built % s_buildOrder.Length]);
        if (type < 0) return;
        NavGrid g = w.NavGrid;
        g.WorldToCell(home.Value, out int hx, out int hy);
        for (int r = 5; r <= 16; r += 2)
            for (int dy = -r; dy <= r; dy += 3)
                for (int dx = -r; dx <= r; dx += 3)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < r - 2) continue; // this ring only
                    int x = hx + dx, y = hy + dy;
                    if (!g.InBounds(x, y) || !(devSpawn != null ? w.Buildings.Fits(type, y * g.Width + x) && NoUnitNear(w, type, x, y) : w.CanPlace(p, type, y * g.Width + x, out _))) continue;
                    UnitStore u = w.Units;
                    output.Add(devSpawn != null ? Command.SpawnBuilding(p, type, g.CellCenter(x, y))
                        : Command.Build(p, new EntityHandle(worker, u.Generation[worker]), type, g.CellCenter(x, y)));
                    return;
                }
    }

    /// <summary>Whether <paramref name="p"/> has a live building of <paramref name="slot"/>.</summary>
    private static bool Has(World w, int p, BuildingSlot slot)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == p && w.Data.Buildings[b.TypeId[k]].Slot == slot) return true;
        return false;
    }

    /// <summary>No live unit within a cell of a <paramref name="type"/> footprint anchored at (<paramref name="x"/>, <paramref name="y"/>) (a dev spawn refuses a unit inside it).</summary>
    private static bool NoUnitNear(World w, int type, int x, int y)
    {
        BuildingDef def = w.Data.Buildings[type];
        float cs = MapConstants.CellSize;
        float x0 = (x - 1) * cs, y0 = (y - 1) * cs, x1 = (x + def.FootprintWidth + 1) * cs, y1 = (y + def.FootprintHeight + 1) * cs;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Position[i].X >= x0 && u.Position[i].X < x1 && u.Position[i].Y >= y0 && u.Position[i].Y < y1) return false;
        return true;
    }

    private static int FirstWorker(World w, int p)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == p && w.Data.Units[u.TypeId[i]].IsWorker) return i;
        return -1;
    }

    /// <summary>
    /// With <paramref name="research"/> the player's first tech it may research (buildings in slot order) is queued first;
    /// then every other finished building trains its first unit it may while its queue is short.
    /// </summary>
    private void Produce(World w, int p, List<Command> output, bool research)
    {
        BuildingStore b = w.Buildings;
        int researchedAt = -1;
        for (int k = 0; k < b.Capacity && researchedAt < 0 && research; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p || b.UnderConstruction[k]) continue;
            foreach (int tech in w.Data.TechsResearchableAt(b.TypeId[k]))
                if (w.CanResearch(p, k, tech, out _))
                {
                    output.Add(Command.Research(p, StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[k]], b.Cell[k]), tech));
                    researchedAt = k;
                    break;
                }
        }
        for (int k = 0; k < b.Capacity && output.Count < _commandBudget; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p || b.UnderConstruction[k] || k == researchedAt || b.QueueCount[k] >= 3) continue;
            Vector2 at = StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[k]], b.Cell[k]);
            foreach (int unit in w.Data.UnitsTrainedAt(b.TypeId[k]))
                if (w.CanTrain(p, k, unit, out _))
                {
                    output.Add(Command.Train(p, at, unit));
                    break;
                }
        }
    }

    /// <summary>The player's non-workers attack-move to the next undefeated player's home.</summary>
    private void AttackMove(World w, int p, List<Command> output)
    {
        Vector2? target = null;
        for (int k = 1; k < _players && target == null; k++)
        {
            int enemy = (p + k) % _players;
            if (!w.IsDefeated(enemy)) target = HomeOf(w, enemy);
        }
        if (target == null) return;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity && output.Count < _commandBudget; i++)
            if (u.Alive[i] && u.Owner[i] == p && !w.Data.Units[u.TypeId[i]].IsWorker && u.CastAbility[i] < 0)
                output.Add(Command.AttackMove(p, new EntityHandle(i, u.Generation[i]), target.Value));
    }

    /// <summary>Casters whose first ability is ready cast it at the nearest enemy unit within 30 m (a self aura at their own feet).</summary>
    private void Cast(World w, int p, List<Command> output)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity && output.Count < _commandBudget; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != p || u.CastAbility[i] >= 0) continue;
            UnitDef def = w.Data.Units[u.TypeId[i]];
            if (def.Abilities.Length == 0 || u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities] > w.TickNumber) continue;
            Vector2? enemy = NearestEnemy(w, p, u.Position[i], 30f);
            if (enemy != null) output.Add(Command.UseAbility(p, new EntityHandle(i, u.Generation[i]), 0, enemy.Value));
        }
    }

    private static Vector2? NearestEnemy(World w, int p, Vector2 from, float within)
    {
        UnitStore u = w.Units;
        Vector2? best = null;
        float bestD = within * within;
        for (int j = 0; j < u.Capacity; j++)
        {
            if (!u.Alive[j] || !w.IsEnemy(p, u.Owner[j])) continue; // M6-S4: not an ally's either
            float d = Vector2.DistanceSquared(u.Position[j], from);
            if (d <= bestD)
            {
                bestD = d;
                best = u.Position[j];
            }
        }
        return best;
    }
}
