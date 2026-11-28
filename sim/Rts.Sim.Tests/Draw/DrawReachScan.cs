using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-SH3 (BUG-1854 / 1860 / 1731): the draw rule's "can still train" read straight from the stores, every term scanned (no
/// counter), for the pre-trace (run on the base too, so it uses nothing M6-SH3 added to the sim), the clause tests and the
/// property row "old rule spent implies new rule spent". The three rules are here: <see cref="OldCan"/> is M6-S13's (gold only,
/// any queued unit, the map's gold with any live worker), <see cref="Sh3Of"/> / <see cref="Sh3Can"/> M6-SH3's exact rule of
/// docs/01 2026-11-27 (second row), kept under its own name, and <see cref="Of"/> / <see cref="NewCan"/> the rule with
/// M6-S15's drop-off clause (BUG-1893, docs/01 2026-11-27-1857 (2)), scanned in <see cref="DropOffClause"/>.
/// </summary>
internal static class DrawReachScan
{
    /// <summary>One seat's terms. <c>Clause</c>: 'f' a free unit type, 'a' a unit being trained or waiting with room, 'b' a unit type it can pay for, 'c' (M6-S15) the drop-off clause, '-' none.</summary>
    internal readonly record struct Seat(
        int Training, int WaitingWithRoom, int WaitingNoRoom, bool Producer, int Workers, int DropOffs,
        long Gold, long Wood, long CarriedGold, long CarriedWood, long MapGold, long MapWood, string Affordable, char Clause);

    /// <summary>The seat's terms under the current rule (M6-SH3's plus M6-S15's drop-off clause).</summary>
    internal static Seat Of(World w, int p) => Of(w, p, dropOffClause: true);

    /// <summary>The seat's terms under M6-SH3's exact rule (no drop-off clause).</summary>
    internal static Seat Sh3Of(World w, int p) => Of(w, p, dropOffClause: false);

    private static Seat Of(World w, int p, bool dropOffClause)
    {
        GameData d = w.Data;
        BuildingStore b = w.Buildings;
        int training = 0, waitingRoom = 0, waitingNoRoom = 0, dropOffs = 0;
        bool producer = false;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p) continue;
            for (int q = 0; q < b.QueueCount[k]; q++)
            {
                if (b.QueueIsTechAt(k, q)) continue;
                if (q == 0 && b.Progress[k] > 0) training++;
                else if (w.HalfPop[p] + d.Units[b.QueueTypeAt(k, q)].ItemHalfPop <= w.HalfPopCap[p]) waitingRoom++;
                else waitingNoRoom++;
            }
            if (b.UnderConstruction[k]) continue;
            BuildingDef def = d.Buildings[b.TypeId[k]];
            if (d.UnitsTrainedAt(def.Id).Length > 0) producer = true;
            if (def.DropOff) dropOffs++;
        }
        UnitStore u = w.Units;
        int workers = 0;
        long carriedGold = 0, carriedWood = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != p) continue;
            if (d.Units[u.TypeId[i]].IsWorker) workers++;
            if (u.CargoKind[i] == ResourceKind.Gold) carriedGold += u.Cargo[i];
            else carriedWood += u.Cargo[i];
        }
        long mapGold = 0, mapWood = 0;
        ResourceStore r = w.Resources;
        for (int n = 0; n < r.Capacity; n++)
        {
            if (!r.Alive[n]) continue;
            if (d.Resources[r.TypeId[n]].Resource == ResourceKind.Gold) mapGold += r.Remaining[n];
            else mapWood += r.Remaining[n];
        }
        bool reach = workers > 0 && dropOffs > 0;
        long gold = w.Gold[p] + (reach ? carriedGold + mapGold : 0), wood = w.Wood[p] + (reach ? carriedWood + mapWood : 0);
        var affordable = new List<string>();
        bool free = false;
        foreach (int t in UnitsOf(w, p))
        {
            UnitDef def = d.Units[t];
            if (def.CostGold == 0 && def.CostWood == 0) free = true;
            if (AtLimit(w, p, t)) continue;
            if (gold >= def.CostGold && wood >= def.CostWood) affordable.Add(def.Key);
        }
        char clause = free ? 'f' : training > 0 || waitingRoom > 0 ? 'a' : (producer || workers > 0) && affordable.Count > 0 ? 'b' : '-';
        if (clause == '-' && dropOffClause && DropOffClause(w, p, workers, dropOffs, carriedGold, carriedWood, mapGold, mapWood)) clause = 'c';
        return new Seat(training, waitingRoom, waitingNoRoom, producer, workers, dropOffs, w.Gold[p], w.Wood[p], carriedGold, carriedWood,
            mapGold, mapWood, string.Join(",", affordable), clause);
    }

    /// <summary>The current rule's answer for an undefeated seat of the ledger.</summary>
    internal static bool NewCan(World w, int p) => Of(w, p).Clause != '-';

    /// <summary>M6-SH3's exact rule's answer (no drop-off clause), kept for the property rows.</summary>
    internal static bool Sh3Can(World w, int p) => Sh3Of(w, p).Clause != '-';

    /// <summary>
    /// M6-S15's clause alone, scanned (BUG-1893): a live worker, no finished drop-off of its own, and some drop-off building type
    /// of its faction (every one with no faction) whose gold and wood the bank pays, such that the bank less that price plus
    /// the carried and map resources pays a unit type not at its limit.
    /// </summary>
    internal static bool DropOffClause(World w, int p)
    {
        Seat s = Sh3Of(w, p);
        return DropOffClause(w, p, s.Workers, s.DropOffs, s.CarriedGold, s.CarriedWood, s.MapGold, s.MapWood);
    }

    private static bool DropOffClause(World w, int p, int workers, int dropOffs, long carriedGold, long carriedWood, long mapGold, long mapWood)
    {
        if (workers == 0 || dropOffs > 0) return false;
        GameData d = w.Data;
        int f = w.FactionOf(p);
        foreach (BuildingDef b in d.Buildings)
        {
            if (!b.DropOff || f >= 0 && b.Faction != f) continue;
            if (w.Gold[p] < b.CostGold || w.Wood[p] < b.CostWood) continue;
            long gold = w.Gold[p] - b.CostGold + carriedGold + mapGold, wood = w.Wood[p] - b.CostWood + carriedWood + mapWood;
            foreach (int t in UnitsOf(w, p))
                if (!AtLimit(w, p, t) && gold >= d.Units[t].CostGold && wood >= d.Units[t].CostWood) return true;
        }
        return false;
    }

    /// <summary>
    /// M6-S13's rule, scanned: a unit item in a queue; or a producer (finished unit-producing building or live worker) whose bank
    /// gold, plus the gold its units carry, plus (with a live worker) the gold left on the map, reaches its faction's cheapest
    /// unit's gold; a faction with a 0-gold unit always can.
    /// </summary>
    internal static bool OldCan(World w, int p)
    {
        GameData d = w.Data;
        int cheapest = int.MaxValue;
        foreach (int t in UnitsOf(w, p)) cheapest = Math.Min(cheapest, d.Units[t].CostGold);
        if (cheapest == 0) return true;
        Seat s = Of(w, p);
        if (s.Training + s.WaitingWithRoom + s.WaitingNoRoom > 0) return true;
        if (!s.Producer && s.Workers == 0) return false;
        long gold = s.Gold + s.CarriedGold + (s.Workers > 0 ? s.MapGold : 0);
        return gold >= cheapest;
    }

    /// <summary>The unit types the rule looks at for seat <paramref name="p"/>: its faction's, or every type for a seat with no faction (test data).</summary>
    internal static IEnumerable<int> UnitsOf(World w, int p)
    {
        int f = w.FactionOf(p);
        if (f >= 0)
        {
            foreach (int t in w.Data.Factions[f].Units) yield return t;
        }
        else
        {
            for (int t = 0; t < w.Data.Units.Length; t++) yield return t;
        }
    }

    /// <summary>Whether a Train of type <paramref name="t"/> would be refused at its <c>maxAlive</c> now (live + queued items x spawnCount, scanned).</summary>
    internal static bool AtLimit(World w, int p, int t)
    {
        UnitDef def = w.Data.Units[t];
        if (def.MaxAlive <= 0) return false;
        int count = 0;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == p && u.TypeId[i] == t) count++;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p) continue;
            for (int q = 0; q < b.QueueCount[k]; q++)
                if (!b.QueueIsTechAt(k, q) && b.QueueTypeAt(k, q) == t) count += def.SpawnCount;
        }
        return count + def.SpawnCount > def.MaxAlive;
    }

    /// <summary>One line for a report: the terms and the clause, and the old rule's answer.</summary>
    internal static string Line(World w, int p)
    {
        Seat s = Of(w, p);
        string clause = s.Clause switch { 'f' => "a free unit", 'a' => "(a) a unit queued with room", 'b' => "(b) can pay", 'c' => "(b) the drop-off clause", _ => "none: cannot train" };
        return $"p{p}{(w.IsDefeated(p) ? " (out)" : "")}: bank {s.Gold} gold / {s.Wood} wood, workers {s.Workers}, finished drop-offs {s.DropOffs}, producer {(s.Producer ? "yes" : "no")}, "
            + $"carried {s.CarriedGold} gold / {s.CarriedWood} wood, map left {s.MapGold} gold / {s.MapWood} wood, queued: training {s.Training}, waiting with room {s.WaitingWithRoom}, waiting at the cap {s.WaitingNoRoom}, "
            + $"pop {w.HalfPop[p] / 2.0}/{w.HalfPopCap[p] / 2.0}; payable now [{s.Affordable}]; new rule: {clause}; old rule: {(OldCan(w, p) ? "can" : "cannot")}";
    }
}
