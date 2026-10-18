using Rts.Sim.Combat;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2: what a round-trip scene did, so a test can check it exercised its subject (a ring of claims, a cast under way at
/// a save, a live zone, a detection that comes and goes, a 5-deep queue holding a tech, a felled tree, a razed building, a
/// defeated seat). Fed the reference after every tick; the "at" sets are the ticks the harness saved at.
/// </summary>
internal sealed class SceneProbe
{
    private readonly int[] _saveTicks;

    internal SceneProbe(int[] saveTicks) => _saveTicks = saveTicks;

    internal readonly HashSet<int> CastingAt = new(), ZoneAt = new(), ClaimAt = new(), SiteAt = new(), FullTechQueueAt = new(), DefeatedAt = new(),
        PendingAt = new(), ProjectileAt = new();
    internal bool Detected, HiddenFromStealthy, AttackMoved, Cast, TreeFelled, Razed, Built, Gathered, Fought;

    /// <summary>A unit trained or in a production queue.</summary>
    internal bool Trained;

    /// <summary>A tech researched or in a production queue.</summary>
    internal bool Researched;

    /// <summary>The observe hook: records the reference's state after a tick.</summary>
    internal void Observe(Simulation sim)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int t = sim.TickNumber;
        bool atSave = Array.IndexOf(_saveTicks, t) >= 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Mode[i] == CombatMode.AttackMove) AttackMoved = true;
            if (u.Target[i] != default) Fought = true;
            if (atSave && u.CastAbility[i] >= 0) CastingAt.Add(t);
            if (u.Statuses.HasStealth(i))
                for (int p = 0; p < w.Config.PlayerCount; p++)
                {
                    if (p == u.Owner[i]) continue;
                    if (w.IsDetectedBy(p, i)) Detected = true;
                    else if (w.IsStealthed(i)) HiddenFromStealthy = true;
                }
        }
        if (w.AbilityEvents.Length > 0) Cast = true;
        // A cast sent between ticks applies at the next tick's start (one tick after it is stamped), so at a save it may
        // still be a pending UseAbility: it resolves after the save as well.
        for (int c = 0; c < sim.PendingCommandCount && atSave; c++)
            if (sim.PendingAt(c).Kind == Commands.CommandKind.UseAbility) CastingAt.Add(t);
        if (atSave && w.Zones.Count > 0) ZoneAt.Add(t);
        if (atSave && w.Projectiles.Count > 0) ProjectileAt.Add(t);
        if (atSave && w.SurroundHolder.Any(h => h != 0)) ClaimAt.Add(t);
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            if (atSave && b.UnderConstruction[k]) SiteAt.Add(t);
            bool tech = false;
            for (int q = 0; q < b.QueueCount[k]; q++) tech |= b.QueueIsTechAt(k, q);
            Researched |= tech; // a tech queued counts as researching
            for (int q = 0; q < b.QueueCount[k]; q++) Trained |= !b.QueueIsTechAt(k, q); // and a unit queued as training
            if (atSave && b.QueueCount[k] == 5 && tech) FullTechQueueAt.Add(t);
        }
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (atSave && w.IsDefeated(p)) DefeatedAt.Add(t);
            if (w.BuildingLosses[p] > 0) Razed = true;
            if (w.UnitsTrained[p] > 0) Trained = true;
            if (w.BuildingsBuilt[p] > 0) Built = true;
            if (w.GatheredGold[p] + w.GatheredWood[p] > 0) Gathered = true;
            for (int tech = 0; tech < w.Data.Techs.Length; tech++)
                if (w.HasTech(p, tech)) Researched = true;
        }
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.HighWater; k++)
            if (r.Generation[k] > 1) TreeFelled = true;
    }

    /// <summary>One line for the test output.</summary>
    public override string ToString() =>
        $"casting at [{string.Join(",", CastingAt.Order())}], zone at [{string.Join(",", ZoneAt.Order())}], claims at [{string.Join(",", ClaimAt.Order())}], " +
        $"site at [{string.Join(",", SiteAt.Order())}], 5-deep tech queue at [{string.Join(",", FullTechQueueAt.Order())}], defeated at [{string.Join(",", DefeatedAt.Order())}], " +
        $"projectiles at [{string.Join(",", ProjectileAt.Order())}]; detected {Detected}, hidden {HiddenFromStealthy}, attack-moved {AttackMoved}, cast {Cast}, " +
        $"fought {Fought}, node felled {TreeFelled}, razed {Razed}, researched {Researched}, trained {Trained}, built {Built}, gathered {Gathered}";
}
