using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Spatial;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M6-S4 (teams, slice 1): the seat range, the result rule's edges (same-tick falls, a team-0 seat beside a team, a
/// never-built ally, the collapse tick), a seeded 2v2 command fuzz with hostile Attack orders at allies (per-tick target
/// invariants, a twin run hash-equal every tick, the spatial hash's enemy queries against a brute force on the live units),
/// and the save / replay header sweeps (every team byte value, every hostile team token, 16 seats on 8 teams).
/// </summary>
public class TeamQaTests
{
    private readonly ITestOutputHelper _out;

    public TeamQaTests(ITestOutputHelper output) => _out = output;

    private static ImmutableArray<PlayerSeat> Seats(params int[] teams) => TeamTests.Seats(teams);

    private static Simulation Flat(int size, bool combat, int units, params int[] teams) =>
        new(TestSim.Config(Seed: 1, PlayerCount: teams.Length, UnitCapacity: units, CommandCapacity: 4096) with { Seats = Seats(teams), Combat = combat },
            LocalMovementTests.Flat(size));

    // ---------- the seat range ----------

    [Fact]
    public void Validate_AcceptsTeams0To8_RefusesEveryOtherWithTheSeatNamed()
    {
        for (int team = -3; team <= 12; team++)
        {
            SimConfig c = TestSim.Config(1, 3, 16, 16) with { Seats = Seats(0, team, 0) };
            if (team is >= 0 and <= 8) c.Validate();
            else
            {
                var e = Assert.Throws<ArgumentOutOfRangeException>(c.Validate);
                Assert.Contains("Seat 1", e.Message);
            }
        }
        foreach (int team in new[] { int.MinValue, int.MaxValue, 256, 1 << 16 })
            Assert.Throws<ArgumentOutOfRangeException>((TestSim.Config(1, 2, 16, 16) with { Seats = Seats(team, 0) }).Validate);
    }

    // ---------- the result ----------

    /// <summary>Each player a House and one worker far from the others (combat off); the match not over after the first tick.</summary>
    private static (Simulation Sim, EntityHandle[] Workers) Sides(int[] teams, bool[]? builds = null)
    {
        Simulation sim = Flat(96, false, 160, teams);
        World w = sim.World;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        var workers = new EntityHandle[teams.Length];
        for (int p = 0; p < teams.Length; p++)
        {
            int x = 6 + 12 * p, y = 10;
            if (builds == null || builds[p]) Assert.True(w.Buildings.Spawn(p, house, y * 96 + x, out _));
            workers[p] = Place(sim, p, Laborer, At(sim, x, 60));
        }
        sim.Tick();
        Assert.False(sim.MatchOver);
        return (sim, workers);
    }

    /// <summary>Frees every listed player's worker, then runs one tick: they all fall on that tick.</summary>
    private static void Out(Simulation sim, EntityHandle[] workers, params int[] players)
    {
        foreach (int p in players) sim.World.Units.Free(workers[p]);
        sim.Tick();
        foreach (int p in players) Assert.True(sim.World.IsDefeated(p), $"player {p} is not out");
    }

    [Fact]
    public void BothOfOneSide_FallingOnTheSameTick_EndsIt_TheOtherSideStands()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 1, 1, 2, 2 });
        Out(sim, workers, 2, 3);
        Assert.True(sim.MatchOver);
        Assert.False(sim.World.IsDefeated(0) || sim.World.IsDefeated(1));
    }

    [Fact]
    public void EverySideFallingOnTheSameTick_IsOver_WithNobodyStanding()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 1, 1, 2, 2 });
        Out(sim, workers, 0, 1, 2, 3);
        Assert.True(sim.MatchOver);
        Assert.All(Enumerable.Range(0, 4), p => Assert.True(sim.World.IsDefeated(p)));
    }

    [Fact]
    public void OneOfEachSide_FallingOnTheSameTick_IsNotOver()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 1, 2, 1, 2 });
        Out(sim, workers, 0, 1);
        Assert.False(sim.MatchOver, "one of each side fell: both sides still stand");
        Out(sim, workers, 3);
        Assert.True(sim.MatchOver);
    }

    /// <summary>A team-0 seat beside a team is a side of its own: it is no ally of the team, and the team doesn't win while it stands.</summary>
    [Fact]
    public void ATeam0Seat_BesideATeam_IsASideOfItsOwn()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 3, 0, 3, 0 });
        Out(sim, workers, 0, 2);
        Assert.False(sim.MatchOver, "two team-0 players still stand: two sides");
        Out(sim, workers, 1);
        Assert.True(sim.MatchOver);
    }

    /// <summary>Non-adjacent team numbers (5 and 8, the top) and seats out of order work as 1 and 2.</summary>
    [Fact]
    public void TeamNumbers_AreLabels_8AndNonAdjacentWork()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 8, 5, 5, 8 });
        Assert.True(sim.World.AreAllied(0, 3) && sim.World.AreAllied(1, 2) && !sim.World.AreAllied(0, 1));
        Out(sim, workers, 0);
        Out(sim, workers, 2);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 3);
        Assert.True(sim.MatchOver);
        Assert.False(sim.World.IsDefeated(1));
    }

    /// <summary>
    /// A never-built (sandbox) seat on a team neither keeps its team in nor counts as a side: with player 1 (team 1) never
    /// building, player 0 (team 1) falling ends the match for team 2, as a never-built seat counts for nothing in a free-for-all.
    /// </summary>
    [Fact]
    public void ANeverBuiltSeatOnATeam_DoesNotKeepItsTeamIn()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 1, 1, 2 }, new[] { true, false, true });
        Assert.False(sim.World.EverBuilt(1));
        Out(sim, workers, 0);
        Assert.True(sim.MatchOver);
        Assert.False(sim.World.IsDefeated(1), "a never-built seat is never defeated");
        // The other way: the never-built seat's team stands only through player 0.
        (sim, workers) = Sides(new[] { 1, 1, 2 }, new[] { true, false, true });
        Out(sim, workers, 2);
        Assert.True(sim.MatchOver);
    }

    /// <summary>A single team holding every builder never ends, even when all of it falls (fewer than two sides ever built).</summary>
    [Fact]
    public void OneTeamOnly_IsNeverOver()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 4, 4, 4 });
        Out(sim, workers, 0, 1);
        Assert.False(sim.MatchOver);
        Out(sim, workers, 2);
        Assert.False(sim.MatchOver, "one side only: no result (as a one-player sandbox)");
    }

    /// <summary>
    /// A defeated ally's buildings collapse on its defeat tick; its ally's buildings and units are untouched, the match goes on,
    /// and the collapse counts as no kill for anyone and no loss.
    /// </summary>
    [Fact]
    public void ADefeatedAllysCollapse_LeavesItsAllyWhole()
    {
        (Simulation sim, EntityHandle[] workers) = Sides(new[] { 1, 1, 2, 2 });
        World w = sim.World;
        int Count(int p)
        {
            int n = 0;
            for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p) n++;
            return n;
        }
        int allyHp = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) allyHp += w.Buildings.Hp[k];
        int kills = w.Kills[0] + w.Kills[1] + w.Kills[2] + w.Kills[3];
        Out(sim, workers, 0);
        Assert.Equal(0, Count(0));
        Assert.Equal(1, Count(1));
        int after = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) after += w.Buildings.Hp[k];
        Assert.Equal(allyHp, after);
        Assert.True(w.Units.IsAlive(workers[1]));
        Assert.Equal(kills, w.Kills[0] + w.Kills[1] + w.Kills[2] + w.Kills[3]);
        Assert.False(sim.MatchOver);
    }

    // ---------- an ally's vision-blocking zone, one site at a time ----------

    /// <summary>
    /// Player 1 (teams 1, 1, 0, 0) calls a Sandstorm; player 0 (its ally) and player 3 (an enemy) each have one Crossbowman,
    /// both outside the storm (<paramref name="inside"/> false) or both inside it. Returns, for the ally and the enemy: whether
    /// the player's fog shows <paramref name="probe"/>'s cell, whether the player sees the third party's Laborer standing there,
    /// and whether its Crossbowman sees that Laborer (the scans' rule).
    /// </summary>
    private static ((bool Cell, bool Player, bool Unit) Ally, (bool Cell, bool Player, bool Unit) Enemy) StormSight(bool inside)
    {
        Simulation sim = Flat(48, false, 64, 1, 1, 0, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 point = At(sim, 15, 24);
        EntityHandle priest = Place(sim, 1, ZoneSystemTests.Priest, At(sim, 10, 24));
        // Outside: both viewers 10 m south of the storm's centre, the Laborer at its centre. Inside: both viewers in the storm,
        // the Laborer 9 m east of the centre (outside the 6 m storm, in a Crossbowman's sight).
        Vector2 probe = inside ? point + new Vector2(9f, 0f) : point;
        EntityHandle ally = Place(sim, 0, Crossbowman, inside ? point + new Vector2(0f, 1f) : point + new Vector2(0f, 10f));
        EntityHandle enemy = Place(sim, 3, Crossbowman, inside ? point + new Vector2(0f, -1f) : point + new Vector2(1f, 10f));
        EntityHandle target = Place(sim, 2, Laborer, probe);
        foreach (EntityHandle h in new[] { ally, enemy, target }) u.Hold[h.Index] = true;
        sim.Enqueue(Command.UseAbility(1, priest, 0, point));
        RunUntil(sim, () => w.Zones.Count > 0, 200);
        Assert.Equal(1, w.Zones.Count);
        for (int t = 0; t < 8; t++) sim.Tick();
        int cell = w.Fog.CellOf(probe);
        return ((w.Fog.IsVisible(0, cell), w.Fog.CanSeeUnit(0, target.Index), Vision.VisionSystem.UnitSeesUnit(w, ally.Index, target.Index)),
            (w.Fog.IsVisible(3, cell), w.Fog.CanSeeUnit(3, target.Index), Vision.VisionSystem.UnitSeesUnit(w, enemy.Index, target.Index)));
    }

    /// <summary>
    /// From outside an ally's Sandstorm the ally's fog, its player view and its unit's sight all reach into it; the enemy's
    /// reach none. Each half alone (the fog's stamp rule, <c>FogStore.HidesFrom</c>, and the per-unit rule,
    /// <c>VisionSystem.ZoneHides</c>) is pinned: the developer's storm test kept passing with either reverted to the owner-only
    /// compare (QA mutation run, 2026-10-27-0158).
    /// </summary>
    [Fact]
    public void AnAllysSandstorm_FromOutside_TheAllysFogPlayerAndUnitSeeIn_TheEnemysDont()
    {
        var (ally, enemy) = StormSight(inside: false);
        Assert.True(ally.Cell, "the ally's fog hides its ally's storm");
        Assert.True(ally.Player, "the ally doesn't see the unit inside its ally's storm");
        Assert.True(ally.Unit, "the ally's unit doesn't see into its ally's storm");
        Assert.False(enemy.Cell || enemy.Player || enemy.Unit, "setup: the enemy sees into the storm");
    }

    /// <summary>Inside an ally's Sandstorm the ally's unit sees out as if it weren't there; the enemy's sees nothing outside.</summary>
    [Fact]
    public void AnAllysSandstorm_FromInside_TheAllySeesOut_TheEnemyIsBlinded()
    {
        var (ally, enemy) = StormSight(inside: true);
        Assert.True(ally.Cell, "the ally's fog is cut inside its ally's storm");
        Assert.True(ally.Player, "the ally inside its ally's storm doesn't see the unit outside");
        Assert.True(ally.Unit, "the ally's unit inside its ally's storm is blinded");
        // The enemy's fog and player view are cut to the 2 m blind; its unit's own sight rule (UnitSeesUnit) still reaches 9 m out
        // (Blinded limits acquiring and attacking to 3 m elsewhere, docs/02 "Statuses"), so only the first two are the setup check.
        Assert.False(enemy.Cell || enemy.Player, $"setup: the enemy inside sees out ({enemy})");
    }

    // ---------- abilities and passives (AbilitySystem.Resolve, PassiveSystem.OnDeath) ----------

    /// <summary>
    /// Teams 1, 1, 0: player 0's Sapper throws a Cusser (120 siege, friendly fire 0.5, hits buildings) on a point where a
    /// Laborer of each player stands and the ally (1) and the enemy (2) each have a House. Returns the damage each Laborer and
    /// each House took.
    /// </summary>
    private static (int Own, int Ally, int Enemy, int AllyHouse, int EnemyHouse) CusserDamage()
    {
        Simulation sim = Flat(48, true, 64, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        int house = TestSim.Data.FindBuilding("malazan_billet");
        Assert.True(b.Spawn(1, house, 20 * 48 + 20, out EntityHandle allyHouse)); // cells 20-21: 40-44 m
        Assert.True(b.Spawn(2, house, 20 * 48 + 24, out EntityHandle enemyHouse)); // cells 24-25: 48-52 m
        var point = new Vector2(46f, 46f); // 2 m from each footprint's edge
        EntityHandle sapper = Place(sim, 0, TestSim.Data.FindUnit("malazan_sapper"), point + new Vector2(0f, 4f));
        EntityHandle own = Place(sim, 0, Laborer, point + new Vector2(-0.4f, 0f));
        EntityHandle ally = Place(sim, 1, Laborer, point + new Vector2(0.4f, 0f));
        EntityHandle enemy = Place(sim, 2, Laborer, point + new Vector2(0f, -0.4f));
        foreach (EntityHandle h in new[] { sapper, own, ally, enemy }) u.Hold[h.Index] = true;
        int lab = u.Hp[own.Index], allyHp = b.Hp[allyHouse.Index], enemyHp = b.Hp[enemyHouse.Index];
        // Every Laborer survives the hit: give them the hp to (the damage is read from the drop).
        foreach (EntityHandle h in new[] { own, ally, enemy }) u.Hp[h.Index] = lab;
        sim.Enqueue(Command.UseAbility(0, sapper, 0, point));
        RunUntil(sim, () => b.Hp[enemyHouse.Index] < enemyHp || !u.IsAlive(enemy), 200);
        return (lab - (u.IsAlive(own) ? u.Hp[own.Index] : 0), lab - (u.IsAlive(ally) ? u.Hp[ally.Index] : 0), lab - (u.IsAlive(enemy) ? u.Hp[enemy.Index] : 0),
            allyHp - b.Hp[allyHouse.Index], enemyHp - b.Hp[enemyHouse.Index]);
    }

    /// <summary>A Cusser hurts an ally's Laborer as an own one (the 0.5 friendly-fire share), an enemy's in full, and never an ally's House, while the enemy's House beside it takes the hit.</summary>
    [Fact]
    public void ACusser_HurtsAnAllysUnitAsAnOwnOne_AndNeverAnAllysBuilding()
    {
        var d = CusserDamage();
        _out.WriteLine($"damage: {d}");
        Assert.True(d.Enemy > 0 && d.EnemyHouse > 0, $"setup: the Cusser hit nothing ({d})");
        Assert.True(d.Own > 0 && d.Own < d.Enemy, $"setup: no friendly-fire share on the own Laborer ({d})");
        Assert.Equal(d.Own, d.Ally);
        Assert.Equal(0, d.AllyHouse);
    }

    /// <summary>Tela's Fire (affects enemy units: Burning) cast on an own, an ally's and an enemy's Laborer burns only the enemy's.</summary>
    [Fact]
    public void TelasFire_BurnsOnlyTheEnemy_NotTheAlly()
    {
        Simulation sim = Flat(48, true, 64, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        var point = new Vector2(48f, 48f);
        EntityHandle mage = Place(sim, 0, TestSim.Data.FindUnit("malazan_cadre_mage"), point + new Vector2(0f, 10f));
        EntityHandle own = Place(sim, 0, Laborer, point + new Vector2(-0.5f, 0f));
        EntityHandle ally = Place(sim, 1, Laborer, point + new Vector2(0.5f, 0f));
        EntityHandle enemy = Place(sim, 2, Laborer, point + new Vector2(0f, -0.5f));
        foreach (EntityHandle h in new[] { mage, own, ally, enemy }) u.Hold[h.Index] = true;
        int burning = w.Data.FindStatus("burning");
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        RunUntil(sim, () => !u.IsAlive(enemy) || u.Statuses.IndexOf(enemy.Index, burning) >= 0, 200);
        Assert.True(u.IsAlive(enemy) && u.Statuses.IndexOf(enemy.Index, burning) >= 0, "setup: the enemy doesn't burn");
        Assert.True(u.Statuses.IndexOf(ally.Index, burning) < 0, "an ally's unit burns from Tela's Fire");
        Assert.True(u.Statuses.IndexOf(own.Index, burning) < 0);
    }

    /// <summary>A Zealot's death gives Martyrdom (Frenzied) to the listed Zealots of its owner and its owner's ally beside it, never to an enemy's.</summary>
    [Fact]
    public void Martyrdom_ReachesAnAllysZealot_NotAnEnemys()
    {
        Simulation sim = Flat(48, false, 64, 1, 1, 0);
        World w = sim.World;
        UnitStore u = w.Units;
        int zealot = TestSim.Data.FindUnit("whirlwind_zealot"), frenzied = w.Data.FindStatus("frenzied");
        var at = new Vector2(48f, 48f);
        EntityHandle dying = Place(sim, 0, zealot, at);
        EntityHandle own = Place(sim, 0, zealot, at + new Vector2(1f, 0f));
        EntityHandle ally = Place(sim, 1, zealot, at + new Vector2(-1f, 0f));
        EntityHandle enemy = Place(sim, 2, zealot, at + new Vector2(0f, 1f));
        sim.Tick();
        u.Hp[dying.Index] = 1;
        Combat.CombatSystem.HitUnit(w, new Combat.PendingHit(default, 2, dying, false, 1));
        Assert.False(u.IsAlive(dying));
        Assert.True(u.Statuses.IndexOf(own.Index, frenzied) >= 0, "setup: the own Zealot got no Martyrdom");
        Assert.True(u.Statuses.IndexOf(ally.Index, frenzied) >= 0, "the ally's Zealot got no Martyrdom");
        Assert.True(u.Statuses.IndexOf(enemy.Index, frenzied) < 0, "the enemy's Zealot got Martyrdom");
    }

    // ---------- a 2v2 command fuzz ----------

    private sealed record FuzzRun(List<ulong> Hashes, int Targeted, int AllyOrders, int[] Losses);

    /// <summary>
    /// Teams 1, 2, 1, 2 (each side two non-adjacent players: sides 0 and 1), 14 units a player of four types mixed into both
    /// halves of a 64 map, a tower and a tent each; every tick a few random orders (AttackMove, Move, Stop, Hold, and Attack on
    /// a random live unit or building of anyone, allies and own included). After every tick: no unit targets an ally's unit or
    /// building, no last attacker is an ally, no tower targets an ally's unit, hp in range, positions finite and on the map, and
    /// the spatial hash's enemy queries on the live units equal a brute force over the alliance rule.
    /// </summary>
    private static FuzzRun Fuzz(ulong seed, int ticks, bool check)
    {
        int[] teams = { 1, 2, 1, 2 };
        Simulation sim = Flat(64, true, 200, teams);
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        var rng = new Random((int)seed);
        var probe = new Random((int)seed + 7); // the oracle's queries: apart from the orders, so the unchecked twin sends the same ones
        int[] types = { HeavyInfantry, Crossbowman, ProjectileTests.Catapult, Laborer, HeavyInfantry, Crossbowman };
        float size = 64 * Map.MapConstants.CellSize;
        for (int p = 0; p < 4; p++)
        {
            for (int k = 0; k < 14; k++)
            {
                // Most of a side's units in its half, a few in the other's.
                bool home = rng.Next(5) != 0;
                float x = (p % 2 == 0) == home ? 10f + (float)rng.NextDouble() * 40f : size - 50f + (float)rng.NextDouble() * 40f;
                Place(sim, p, types[rng.Next(types.Length)], new Vector2(x, 10f + (float)rng.NextDouble() * (size - 20f)));
            }
            int tx = p % 2 == 0 ? 4 + 6 * (p / 2) : 52 - 6 * (p / 2);
            Assert.True(b.Spawn(p, TowerTests.Watchtower, (8 + 20 * (p / 2)) * 64 + tx, out _));
            Assert.True(b.Spawn(p, TowerTests.Tent, (40 + 10 * (p / 2)) * 64 + tx, out _));
        }
        int maxUnitHp = 0;
        foreach (UnitDef d in w.Data.Units) maxUnitHp = Math.Max(maxUnitHp, d.Hp * 2);
        var allied = new bool[16];
        for (int a = 0; a < 4; a++) for (int c = 0; c < 4; c++) allied[a * 4 + c] = w.AreAllied(a, c);
        var oracle = new SpatialHash(u.Capacity, w.NavGrid.Width, w.NavGrid.Height, allied, 4);
        var found = new int[u.Capacity];
        var live = new List<int>();
        var hashes = new List<ulong>();
        int targeted = 0, allyOrders = 0;
        var pending = new List<(EntityHandle Unit, EntityHandle Target, bool IsBuilding, int Due)>();
        for (int t = 0; t < ticks; t++)
        {
            live.Clear();
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) live.Add(i);
            for (int n = 0; n < 4 && live.Count > 0; n++)
            {
                int i = live[rng.Next(live.Count)];
                var unit = new EntityHandle(i, u.Generation[i]);
                int p = u.Owner[i];
                var at = new Vector2(2f + (float)rng.NextDouble() * (size - 4f), 2f + (float)rng.NextDouble() * (size - 4f));
                switch (rng.Next(6))
                {
                    case 0: sim.Enqueue(Command.Move(p, unit, at)); break;
                    case 1: sim.Enqueue(Command.Stop(p, unit)); break;
                    case 2: sim.Enqueue(Command.HoldPosition(p, unit)); break;
                    case 3: sim.Enqueue(Command.AttackMove(p, unit, at)); break;
                    default:
                        bool building = rng.Next(3) == 0;
                        int j = building ? rng.Next(b.Capacity) : live[rng.Next(live.Count)];
                        if (building && !b.Alive[j]) break;
                        EntityHandle target = building ? b.HandleOf(j) : new EntityHandle(j, u.Generation[j]);
                        int owner = building ? b.Owner[j] : u.Owner[j];
                        sim.Enqueue(Command.Attack(p, unit, target, building));
                        if (owner != p && w.AreAllied(p, owner))
                        {
                            allyOrders++;
                            pending.Add((unit, target, building, t + 2));
                        }
                        break;
                }
            }
            sim.Tick();
            hashes.Add(sim.StateHash());
            if (!check) continue;
            string at0 = $"seed {seed} tick {sim.TickNumber}";
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y) && pos.X >= 0f && pos.Y >= 0f && pos.X <= size && pos.Y <= size, $"{at0}: unit {i} at {pos}");
                Assert.InRange(u.Hp[i], 1, maxUnitHp);
                EntityHandle tg = u.Target[i];
                if (tg.Generation != 0)
                {
                    if (u.TargetIsBuilding[i])
                    {
                        if (b.IsAlive(tg)) Assert.True(w.IsEnemy(u.Owner[i], b.Owner[tg.Index]), $"{at0}: unit {i} (p{u.Owner[i]}) targets an ally's building");
                    }
                    else if (u.IsAlive(tg))
                    {
                        targeted++;
                        Assert.True(w.IsEnemy(u.Owner[i], u.Owner[tg.Index]), $"{at0}: unit {i} (p{u.Owner[i]}) targets an ally's unit {tg.Index} (p{u.Owner[tg.Index]})");
                    }
                }
                EntityHandle last = u.LastAttacker[i];
                if (last.Generation != 0 && u.IsAlive(last)) Assert.True(w.IsEnemy(u.Owner[i], u.Owner[last.Index]), $"{at0}: unit {i}'s last attacker is an ally");
            }
            for (int k = 0; k < b.Capacity; k++)
            {
                if (!b.Alive[k]) continue;
                EntityHandle tt = b.TowerTargets[k];
                if (tt.Generation != 0 && u.IsAlive(tt)) Assert.True(w.IsEnemy(b.Owner[k], u.Owner[tt.Index]), $"{at0}: tower {k} targets an ally's unit");
            }
            for (int k = pending.Count - 1; k >= 0; k--)
            {
                if (pending[k].Due > t) continue;
                (EntityHandle unit, EntityHandle target, bool isB, _) = pending[k];
                if (u.IsAlive(unit)) Assert.False(u.Target[unit.Index] == target && u.TargetIsBuilding[unit.Index] == isB, $"{at0}: an Attack on an ally was taken");
                pending.RemoveAt(k);
            }
            // The spatial hash, rebuilt on the live units with the world's alliance table, against the brute force.
            oracle.Rebuild(u);
            for (int q = 0; q < 6; q++)
            {
                var c = new Vector2((float)probe.NextDouble() * size, (float)probe.NextDouble() * size);
                float r = (float)probe.NextDouble() * 30f;
                int player = probe.Next(4);
                int n = oracle.QueryEnemies(c, r, player, found);
                int[] got = found[..Math.Min(n, found.Length)];
                Array.Sort(got);
                var want = new List<int>();
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i] || w.AreAllied(player, u.Owner[i])) continue;
                    float d2 = Vector2.DistanceSquared(u.Position[i], c);
                    if (!(d2 <= r * r)) continue;
                    want.Add(i);
                    if (d2 < bestD) (best, bestD) = (i, d2);
                }
                Assert.Equal(want.ToArray(), got);
                Assert.Equal(best >= 0, oracle.NearestEnemy(c, r, player, out int slot));
                Assert.Equal(best, slot);
                int enemies = 0;
                for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && w.IsEnemy(player, u.Owner[i])) enemies++;
                Assert.Equal(enemies, oracle.OthersThan(player));
            }
        }
        return new FuzzRun(hashes, targeted, allyOrders, w.Losses.ToArray());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void A2v2CommandFuzz_NoAllyEverTargeted_TheHashOracleHolds_AndTheTwinIsHashEqual(ulong seed)
    {
        FuzzRun a = Fuzz(seed, 1500, check: true);
        FuzzRun twin = Fuzz(seed, 1500, check: false);
        for (int t = 0; t < a.Hashes.Count; t++) Assert.True(a.Hashes[t] == twin.Hashes[t], $"seed {seed}: the twin's hash differs at tick {t + 1}");
        _out.WriteLine($"seed {seed}: {a.Targeted} unit-ticks of targeting, {a.AllyOrders} Attack orders at allies, losses {string.Join(", ", a.Losses)}");
        Assert.True(a.Targeted > 500, "no fight");
        Assert.True(a.AllyOrders > 20, "the fuzz sent few orders at allies");
        Assert.True(a.Losses[0] + a.Losses[2] > 0 && a.Losses[1] + a.Losses[3] > 0, "a side lost nothing");
        Assert.NotEqual(a.Hashes[^1], Fuzz(seed + 100, 50, check: false).Hashes[^1]);
    }

    // ---------- the save header ----------

    /// <summary>A 4-seat save (teams 1, 2, 1, 2) and the offset of seat 0's team byte: the team bytes end the header, just before the first section's tag.</summary>
    private static (byte[] Bytes, int TeamAt) FourSeatSave()
    {
        var sim = new Simulation(TestSim.Config(4, 4, 32, 64) with { Seats = Seats(1, 2, 1, 2) });
        for (int t = 0; t < 3; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        int hmap = bytes.AsSpan().IndexOf("HMAP"u8);
        Assert.True(hmap > 4);
        Assert.Equal(new byte[] { 1, 2, 1, 2 }, bytes[(hmap - 4)..hmap]);
        return (bytes, hmap - 4);
    }

    /// <summary>Every byte value at every seat's team byte: 0-8 load with that team (and only that seat's changes), 9-255 are InvalidHeader.</summary>
    [Fact]
    public void EveryTeamByteValue_AtEverySeat_LoadsOrIsInvalidHeader()
    {
        (byte[] good, int at) = FourSeatSave();
        int loads = 0;
        for (int seat = 0; seat < 4; seat++)
        {
            for (int v = 0; v < 256; v++)
            {
                byte[] bad = (byte[])good.Clone();
                bad[at + seat] = (byte)v;
                bad = SaveBytes.Rechecksum(bad);
                if (v > 8)
                {
                    Assert.True(SaveTestRun.TryLoad(TestSim.Data, bad) == SaveError.InvalidHeader, $"seat {seat} team {v}");
                    continue;
                }
                if (v % 4 != seat % 4 && v != 0 && v != 8) continue; // a sample of the loads: they build a world each
                Simulation loaded = SaveTestRun.Load(TestSim.Data, bad);
                loads++;
                for (int p = 0; p < 4; p++) Assert.Equal(p == seat ? v : good[at + p], loaded.World.TeamOf(p));
            }
        }
        Assert.True(loads >= 12);
    }

    /// <summary>16 seats on all of teams 0-8: the save carries every team and the loaded world's alliance table is the saved one's.</summary>
    [Fact]
    public void SixteenSeats_OnEveryTeam_RoundTrip_AndFollowTheUnsavedRun()
    {
        int[] teams = Enumerable.Range(0, 16).Select(p => p % 9).ToArray();
        var sim = new Simulation(TestSim.Config(9, 16, 64, 256) with { Seats = Seats(teams) });
        for (int p = 0; p < 16; p++) sim.Enqueue(Command.SpawnUnit(p, Laborer, new Vector2(8f + 6f * p, 20f + 3f * (p % 3))));
        for (int t = 0; t < 5; t++) sim.Tick();
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
        for (int a = 0; a < 16; a++)
        {
            Assert.Equal(teams[a], loaded.World.TeamOf(a));
            for (int c = 0; c < 16; c++) Assert.Equal(sim.World.AreAllied(a, c), loaded.World.AreAllied(a, c));
        }
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
    }

    // ---------- the replay header ----------

    private static Replay TeamReplay(params int[] teams)
    {
        var sim = new Simulation(TestSim.Config(Seed: 6, PlayerCount: teams.Length, UnitCapacity: 16, CommandCapacity: 16) with { Seats = Seats(teams) });
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        for (int t = 0; t < 20; t++) sim.Tick();
        return rec.ToReplay();
    }

    private static byte[] Reseal(string text) => ReplayFormat.Seal(text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)]);

    [Theory]
    [InlineData("0", ReplayError.None)]
    [InlineData("5", ReplayError.None)]
    [InlineData("8", ReplayError.None)]
    [InlineData("9", ReplayError.InvalidHeader)]
    [InlineData("10", ReplayError.InvalidHeader)]
    [InlineData("256", ReplayError.InvalidHeader)]
    [InlineData("-1", ReplayError.InvalidHeader)]
    [InlineData("2147483647", ReplayError.InvalidHeader)]
    [InlineData("-2147483648", ReplayError.InvalidHeader)]
    [InlineData("2147483648", ReplayError.Malformed)]
    [InlineData("-0", ReplayError.Malformed)]
    [InlineData("01", ReplayError.Malformed)]
    [InlineData("1.0", ReplayError.Malformed)]
    [InlineData("1e1", ReplayError.Malformed)]
    [InlineData("0x1", ReplayError.Malformed)]
    [InlineData("١", ReplayError.Malformed)] // an Arabic-Indic digit one
    public void EveryHostileTeamToken_AtTheLastSeat_IsRefused_ValidOnesPlay(string token, ReplayError expected)
    {
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(TeamReplay(1, 2, 1, 2)));
        const string line = "seat 3 whirlwind human normal 2\n";
        Assert.Contains(line, text);
        byte[] bytes = Reseal(text.Replace(line, $"seat 3 whirlwind human normal {token}\n"));
        ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
        Assert.Equal(expected, e);
        if (e != ReplayError.None) return;
        Assert.Equal(int.Parse(token), r!.Seats[3].Team);
        // A changed team plays the same short log: nobody fights in it, so the checkpoints still hold.
        Assert.True(ReplayPlayer.Run(r, TestSim.Data).Ok);
    }

    /// <summary>A free-for-all of 4 seats is still written as format 6 and reads back every team 0; written as format 7 it carries 0 tokens and plays the same.</summary>
    [Fact]
    public void AFourSeatFreeForAll_IsFormat6_ReadsTeam0_AndAsFormat7PlaysTheSame()
    {
        Replay ffa = TeamReplay(0, 0, 0, 0);
        Assert.Equal(6, ffa.FormatVersion);
        byte[] six = ReplayFormat.Write(ffa);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(six, out Replay? back));
        Assert.All(back!.Seats, s => Assert.Equal(0, s.Team));
        Assert.DoesNotContain(" normal 0\n", Encoding.ASCII.GetString(six));
        Replay seven = ReplayTestRun.With(back, formatVersion: 7);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(seven), out Replay? back7));
        Assert.Equal(7, back7!.FormatVersion);
        Assert.True(ReplayPlayer.Run(back7, TestSim.Data).Ok);
        Assert.True(ReplayPlayer.Run(back, TestSim.Data).Ok);
    }
}
