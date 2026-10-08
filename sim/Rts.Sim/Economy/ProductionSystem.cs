using System;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Sim.Economy;

/// <summary>
/// Production (docs/03 "Implementation (M3-4)" and "(M3-5)"): the apply steps of <see cref="CommandKind.Train"/>,
/// <see cref="CommandKind.Research"/>, <see cref="CommandKind.CancelTrain"/>, <see cref="CommandKind.SetRally"/> and
/// <see cref="CommandKind.ClearRally"/>, and tick phase 3, where every finished building works on the head of its queue:
/// it spawns a trained unit when it is done, or sets the owner's tech flag for a researched tech.
/// </summary>
/// <remarks>
/// An item is paid when queued and refunded in full when cancelled. The head item starts (taking its population as a
/// reservation) only while the owner has room under the cap, then counts one tick a tick up to its type's train ticks;
/// complete, it spawns on the nearest free cell round the footprint (<see cref="FreeCellSearch"/>) and walks to the
/// rally point, or waits there for a free cell. A tech starts at once (no population) and spawns nothing (M3-5).
/// Buildings go in slot order; bounded scans only, no allocation.
/// </remarks>
public static class ProductionSystem
{
    /// <summary>The rule behind <see cref="World.CanTrain"/> and <c>Train</c>: the first one broken, or <see cref="TrainError.None"/>.</summary>
    internal static TrainError Check(World world, int player, int slot, int unitType)
    {
        BuildingStore b = world.Buildings;
        if ((uint)slot >= (uint)b.Capacity || !b.Alive[slot] || b.Owner[slot] != player || b.UnderConstruction[slot]) return TrainError.NoBuilding;
        GameData data = world.Data;
        if ((uint)unitType >= (uint)data.Units.Length) return TrainError.UnknownType;
        UnitDef def = data.Units[unitType];
        if (world.FactionOf(player) != def.Faction) return TrainError.WrongFaction;
        if (def.TrainedAtTypeId != b.TypeId[slot]) return TrainError.NotTrainedHere;
        // M3-6: checked when the Train applies, not again at completion (Requirements).
        if (!Requirements.Met(world, player, def.RequiresTechs, def.RequiresBuildings)) return TrainError.LockedByRequirement;
        if (b.QueueCount[slot] >= EconomyConstants.ProductionQueueCapacity) return TrainError.QueueFull;
        if (world.Gold[player] < def.CostGold || world.Wood[player] < def.CostWood) return TrainError.CannotAfford;
        return TrainError.None;
    }

    /// <summary>The rule behind <see cref="World.CanResearch"/> and <c>Research</c> (M3-5): the first one broken, or <see cref="ResearchError.None"/>.</summary>
    /// <remarks>M3-6: <c>requires</c> and <c>requiresAnyOf</c> are checked when the Research applies, not again at completion.</remarks>
    internal static ResearchError CheckResearch(World world, int player, int slot, int tech)
    {
        BuildingStore b = world.Buildings;
        if ((uint)slot >= (uint)b.Capacity || !b.Alive[slot] || b.Owner[slot] != player || b.UnderConstruction[slot]) return ResearchError.NoBuilding;
        GameData data = world.Data;
        if ((uint)tech >= (uint)data.Techs.Length) return ResearchError.UnknownTech;
        TechDef def = data.Techs[tech];
        if (def.Faction >= 0 && def.Faction != world.FactionOf(player)) return ResearchError.WrongFaction;
        if (ImmutableArray.BinarySearch(data.TechsResearchableAt(b.TypeId[slot]), tech) < 0) return ResearchError.NotResearchedHere;
        if (!Requirements.Met(world, player, def.RequiresTechs, def.RequiresBuildings) || !Requirements.AnyOfMet(world, player, def))
            return ResearchError.Requires;
        if (world.HasTech(player, tech)) return ResearchError.AlreadyResearched;
        if (b.IsTechQueued(player, tech)) return ResearchError.AlreadyQueued;
        if (b.QueueCount[slot] >= EconomyConstants.ProductionQueueCapacity) return ResearchError.QueueFull;
        if (world.Gold[player] < def.CostGold || world.Wood[player] < def.CostWood) return ResearchError.CannotAfford;
        return ResearchError.None;
    }

    /// <summary>Slot of <paramref name="player"/>'s finished building covering nav cell (<paramref name="x"/>, <paramref name="y"/>), or -1 (none, another player's, a site).</summary>
    private static int OwnFinishedAt(World world, int player, int x, int y)
    {
        BuildingStore b = world.Buildings;
        int k = b.SlotAt(x, y);
        return k >= 0 && b.Owner[k] == player && !b.UnderConstruction[k] ? k : -1;
    }

    private static int OwnFinishedAt(World world, int player, Vector2 position) =>
        world.NavGrid.WorldToCell(position, out int x, out int y) ? OwnFinishedAt(world, player, x, y) : -1;

    /// <summary>Applies <see cref="CommandKind.Train"/>: queues the unit and takes its cost; dropped for anything <see cref="Check"/> refuses.</summary>
    internal static void ApplyTrain(World world, in Command command)
    {
        int k = OwnFinishedAt(world, command.Player, command.Position);
        if (k < 0 || Check(world, command.Player, k, command.TypeId) != TrainError.None) return;
        UnitDef def = world.Data.Units[command.TypeId];
        world.TrySpend(command.Player, def.CostGold, def.CostWood);
        world.Buildings.Enqueue(k, command.TypeId);
    }

    /// <summary>Applies <see cref="CommandKind.Research"/> (M3-5): queues the tech and takes its cost; dropped for anything <see cref="CheckResearch"/> refuses.</summary>
    internal static void ApplyResearch(World world, in Command command)
    {
        int k = OwnFinishedAt(world, command.Player, command.Position);
        if (k < 0 || CheckResearch(world, command.Player, k, command.TypeId) != ResearchError.None) return;
        TechDef def = world.Data.Techs[command.TypeId];
        world.TrySpend(command.Player, def.CostGold, def.CostWood);
        world.Buildings.EnqueueTech(k, command.TypeId);
    }

    /// <summary>Applies <see cref="CommandKind.CancelTrain"/>: item <see cref="Command.TypeId"/>, a unit or a tech (M3-5), leaves the queue with a full refund (a started head also loses its progress and a unit's reservation); dropped for a bad index or no own finished building.</summary>
    internal static void ApplyCancelTrain(World world, in Command command)
    {
        int k = OwnFinishedAt(world, command.Player, command.Position);
        if (k < 0 || (uint)command.TypeId >= (uint)world.Buildings.QueueCount[k]) return;
        world.Buildings.RemoveQueued(k, command.TypeId);
    }

    /// <summary>Applies <see cref="CommandKind.SetRally"/>: the own finished building covering cell <see cref="Command.TypeId"/> rallies to <see cref="Command.Position"/>; dropped for a target off the map or no such building.</summary>
    internal static void ApplySetRally(World world, in Command command)
    {
        NavGrid g = world.NavGrid;
        if ((uint)command.TypeId >= (uint)(g.Width * g.Height) || !g.WorldToCell(command.Position, out _, out _)) return;
        int k = OwnFinishedAt(world, command.Player, command.TypeId % g.Width, command.TypeId / g.Width);
        if (k >= 0) world.Buildings.SetRally(k, true, command.Position);
    }

    /// <summary>Applies <see cref="CommandKind.ClearRally"/>: the own finished building covering <see cref="Command.Position"/> loses its rally point.</summary>
    internal static void ApplyClearRally(World world, in Command command)
    {
        int k = OwnFinishedAt(world, command.Player, command.Position);
        if (k >= 0) world.Buildings.SetRally(k, false, Vector2.Zero);
    }

    /// <summary>Phase 3: every finished building with a queue, in slot order, starts, advances or spawns its head item.</summary>
    /// <remarks>
    /// Spawn cells come from the push-out's per-cell cache (<see cref="Pathfinding.FlowFieldCache.BuildScratch"/>, idle in
    /// this phase). The search reads only cells inside the bounding box of the building's plateau, so on the tick's first
    /// spawn attempt on a plateau that box is filled from one pass over the unit store (not the whole map, and no
    /// spatial query per cell), and units spawned this tick are seen by later spawns. A plateau found with no free cell
    /// is remembered for the rest of the tick (no cell frees in this phase), so twenty heads waiting there cost one walk
    /// (M3-H2, BUG-0097). The spatial hash is rebuilt at the end when a unit spawned, so later phases see it.
    /// </remarks>
    public static void Run(World world)
    {
        BuildingStore b = world.Buildings;
        Array.Clear(world.SpawnCacheCleared);
        Array.Clear(world.SpawnPlateauFull);
        bool spawned = false;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.QueueCount[k] == 0 || b.UnderConstruction[k]) continue;
            spawned |= Step(world, k);
        }
        if (spawned) world.Spatial.Rebuild(world.Units);
    }

    /// <summary>One building's tick; true if it spawned a unit.</summary>
    private static bool Step(World world, int k)
    {
        BuildingStore b = world.Buildings;
        ref int progress = ref b.ProgressOf(k);
        int ticks = b.ItemTicks(k, 0);
        if (progress == 0)
        {
            if (!TryStart(world, k)) return false;
        }
        else if (progress < ticks) progress++;
        if (progress < ticks) return false;
        bool spawned = false;
        if (b.QueueIsTechAt(k, 0))
            world.Techs.Set(b.Owner[k], b.QueueTypeAt(k, 0), true); // researched: nothing spawns
        else if (Spawn(world, k, world.Data.Units[b.QueueTypeAt(k, 0)]))
            spawned = true;
        else
            return false;
        b.PopTrained(k);
        // The next item starts in the same tick, so each one takes exactly its train or research ticks.
        if (b.QueueCount[k] > 0) TryStart(world, k);
        return spawned;
    }

    /// <summary>
    /// Starts slot <paramref name="k"/>'s head item and counts this tick: a tech at once (research takes no population,
    /// M3-5), a unit only if its population fits under the owner's cap, taking it as a reservation.
    /// </summary>
    private static bool TryStart(World world, int k)
    {
        BuildingStore b = world.Buildings;
        if (!b.QueueIsTechAt(k, 0))
        {
            UnitDef def = world.Data.Units[b.QueueTypeAt(k, 0)];
            int owner = b.Owner[k];
            if (world.HalfPop[owner] + def.HalfPop > world.HalfPopCap[owner]) return false;
            world.Ledger.AddHalfPop(owner, def.HalfPop);
        }
        b.ProgressOf(k) = 1;
        return true;
    }

    /// <summary>
    /// Spawns slot <paramref name="k"/>'s complete head item on the free cell nearest the rally point (or the footprint's
    /// center without one) in the nearest ring round the footprint, then sends it on; false, nothing changed, when no cell
    /// on the building's plateau is free or the unit store is full.
    /// </summary>
    private static bool Spawn(World world, int k, UnitDef def)
    {
        UnitStore u = world.Units;
        if (u.FreeCount == 0) return false;
        BuildingStore b = world.Buildings;
        NavGrid g = world.NavGrid;
        BuildingDef bd = world.Data.Buildings[b.TypeId[k]];
        int anchor = b.Cell[k], x0 = anchor % g.Width, y0 = anchor / g.Width;
        int[] taken = world.FlowFields.BuildScratch; // per cell: 1 free, 2 a unit's center in it (inside the plateau's box)
        int plateau = world.Plateaus.At(anchor);
        if ((uint)plateau >= (uint)world.SpawnPlateauFull.Length || world.SpawnPlateauFull[plateau]) return false;
        FillPlateauBox(world, taken, plateau);
        Vector2 from = b.HasRally[k] ? b.RallyPosition[k] : EconomySystem.Center(g, anchor, bd.FootprintWidth, bd.FootprintHeight);
        int cell = FreeCellSearch.Nearest(world, x0, y0, bd.FootprintWidth, bd.FootprintHeight, from, taken);
        if (cell < 0)
        {
            world.SpawnPlateauFull[plateau] = true;
            return false;
        }
        u.TrySpawn(b.Owner[k], b.QueueTypeAt(k, 0), def, g.CellCenter(cell % g.Width, cell / g.Width), out EntityHandle h);
        taken[cell] = 2;
        if (b.HasRally[k]) Rally(world, h.Index, b.RallyPosition[k]);
        return true;
    }

    /// <summary>
    /// The first time this tick: <paramref name="taken"/> over the bounding box of <paramref name="plateau"/> set to
    /// 1 (free), then 2 for each cell holding a live unit's center, from one pass over the unit store. The same answer the
    /// spatial hash would give cell by cell (it holds the units as they stand in this phase), at the cost of the box and
    /// the store, not a query per cell.
    /// </summary>
    private static void FillPlateauBox(World world, int[] taken, int plateau)
    {
        if (world.SpawnCacheCleared[plateau] || !world.Plateaus.Bounds(plateau, out int minX, out int minY, out int maxX, out int maxY)) return;
        world.SpawnCacheCleared[plateau] = true;
        NavGrid g = world.NavGrid;
        for (int y = minY; y <= maxY; y++) Array.Fill(taken, 1, y * g.Width + minX, maxX - minX + 1);
        UnitStore u = world.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !g.WorldToCell(u.Position[i], out int x, out int y)) continue;
            if (x >= minX && x <= maxX && y >= minY && y <= maxY) taken[y * g.Width + x] = 2;
        }
    }

    /// <summary>A trained unit's first order: a worker rallied onto a resource node gathers it (Age of Empires' rule); anyone else walks there by the Move rule.</summary>
    private static void Rally(World world, int i, Vector2 target)
    {
        NavGrid g = world.NavGrid;
        if (EconomySystem.IsWorker(world, i) && g.WorldToCell(target, out int x, out int y) && (g.FlagsAt(x, y) & NavFlags.Resource) != 0)
        {
            int node = EconomySystem.ResolveNode(world, target);
            if (node >= 0)
            {
                EconomySystem.StartGather(world, i, node);
                return;
            }
        }
        OrderSystem.MoveTo(world, i, target);
    }
}
