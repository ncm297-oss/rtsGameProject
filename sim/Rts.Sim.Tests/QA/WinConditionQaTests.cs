using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M5-4e, session 2026-10-14-0946): the new win condition (docs/02 "Game flow" 4, docs/03 "Implementation (M5-4e)"):
/// out once a player that has built has no live unit and no finished unit-producing building. Attacked with real AI
/// matches (the counts recounted and the rule checked both ways every tick), the exact tick a hall finishes as its last unit
/// goes, the door on the buildings a defeated player keeps standing (its site, its Forge), and never-built seats mixed with
/// built ones in a four-seat match.
/// </summary>
public class WinConditionQaTests
{
    private readonly ITestOutputHelper _out;

    public WinConditionQaTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves(2));

    private static int BuildingType(World w, int player, BuildingSlot slot) => StartBase.BuildingOfSlot(w.Data, w.FactionOf(player), slot);

    private static int UnitType(World w, int player, UnitSlot slot) => StartBase.UnitOfSlot(w.Data, w.FactionOf(player), slot);

    private static Vector2 At(Simulation sim, int x, int y) => sim.World.NavGrid.CellCenter(x, y);

    private static EntityHandle Spawn(Simulation sim, int player, int type, int x, int y, bool site = false)
    {
        Assert.True(sim.World.Buildings.Spawn(player, type, y * sim.World.NavGrid.Width + x, out EntityHandle h, site));
        return h;
    }

    private static Vector2 Centre(Simulation sim, EntityHandle b)
    {
        BuildingStore s = sim.World.Buildings;
        BuildingDef d = sim.World.Data.Buildings[s.TypeId[b.Index]];
        int w = sim.World.NavGrid.Width, cell = s.Cell[b.Index];
        return new Vector2((cell % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (cell / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    private static void Applied(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    /// <summary>
    /// The oracle, both ways: each player's counts equal a recount from the stores; a player that has built and isn't
    /// defeated has a unit or a finished unit-producing building (no defeat missed); a defeated player has neither (nothing
    /// came back after the defeat).
    /// </summary>
    private static void CheckRule(World w, string at)
    {
        int players = w.Ledger.Defeated.Length;
        Span<int> units = stackalloc int[players], production = stackalloc int[players];
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) units[u.Owner[i]]++;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && !b.UnderConstruction[k] && !w.Data.UnitsTrainedAt(b.TypeId[k]).IsEmpty) production[b.Owner[k]]++;
        for (int p = 0; p < players; p++)
        {
            Assert.True(units[p] == w.LiveUnits(p), $"{at}: player {p} LiveUnits {w.LiveUnits(p)}, recount {units[p]}");
            Assert.True(production[p] == w.ProductionBuildings(p), $"{at}: player {p} ProductionBuildings {w.ProductionBuildings(p)}, recount {production[p]}");
            if (w.EverBuilt(p) && !w.IsDefeated(p))
                Assert.True(units[p] > 0 || production[p] > 0, $"{at}: player {p} built, has nothing that keeps it in, and isn't defeated");
            if (w.IsDefeated(p))
                Assert.True(units[p] == 0 && production[p] == 0, $"{at}: defeated player {p} has {units[p]} units, {production[p]} production buildings");
        }
    }

    /// <summary>
    /// Four AI seats on the flat scene, combat on, the early-wave data: the weak seats' halls at 1 hp and (with
    /// <paramref name="takeWorkers"/>) their workers gone. 4,000 ticks; after every tick <see cref="CheckRule"/>, and every
    /// defeat event's player has 0 of both on its tick. Also: a defeated seat's AI stays silent.
    /// </summary>
    [Theory]
    [InlineData(3UL, 0b1100, true)]
    [InlineData(7UL, 0b0011, true)]
    [InlineData(5UL, 0b0110, false)]
    [InlineData(11UL, 0b1001, false)]
    public void FourAiSeats_TheCountsEqualARecount_AndTheRuleHoldsBothWays_EveryTick(ulong seed, int weak, bool takeWorkers)
    {
        var seats = new PlayerSeat[]
        {
            new(0, SeatController.Ai, AiDifficulty.Easy), new(1, SeatController.Ai, AiDifficulty.Normal),
            new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Flat(seats, s_early.Value, units: 600, commands: 1024, seed: seed, combat: true);
        World w = sim.World;
        for (int p = 0; p < 4; p++) BuildMaps.SetTotals(sim, p, (weak >> p & 1) != 0 ? 0 : 3000, (weak >> p & 1) != 0 ? 0 : 3000);
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && (weak >> w.Buildings.Owner[k] & 1) != 0) w.Buildings.SetHp(k, 1);
        if (takeWorkers)
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && (weak >> w.Units.Owner[i] & 1) != 0) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        for (int p = 0; p < 4; p++)
        {
            if ((weak >> p & 1) != 0) continue;
            (int hx, int hy) = AiScenes.HallAt(p);
            int line = UnitType(w, p, UnitSlot.Line);
            int dx = p == 0 ? 6 : p == 1 ? -4 : 0, dy = p == 2 ? 6 : p == 3 ? -4 : 0;
            for (int k = 0; k < 8; k++) CombatScenes.Place(sim, p, line, w.NavGrid.CellCenter(hx + dx + k % 4, hy + dy + k / 4));
        }
        CheckRule(w, $"seed {seed} before tick 0");
        var sentAt = new int[4];
        Array.Fill(sentAt, -1);
        int defeats = 0, maxUnits = 0;
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            CheckRule(w, $"seed {seed} tick {sim.TickNumber - 1}");
            foreach (DefeatEvent e in w.DefeatEvents)
            {
                defeats++;
                sentAt[e.Player] = sim.AiOf(e.Player)!.CommandsSent;
            }
            for (int p = 0; p < 4; p++)
            {
                maxUnits = Math.Max(maxUnits, w.LiveUnits(p));
                if (sentAt[p] >= 0) Assert.Equal(sentAt[p], sim.AiOf(p)!.CommandsSent);
            }
        }
        _out.WriteLine($"seed {seed} weak {Convert.ToString(weak, 2)} workers taken {takeWorkers}: {defeats} defeats, most units of one side {maxUnits}, over {sim.MatchOver}; " +
            $"units {w.LiveUnits(0)}/{w.LiveUnits(1)}/{w.LiveUnits(2)}/{w.LiveUnits(3)}, production {w.ProductionBuildings(0)}/{w.ProductionBuildings(1)}/{w.ProductionBuildings(2)}/{w.ProductionBuildings(3)}");
        Assert.True(maxUnits > 10, "precondition: the seats trained an army");
        if (takeWorkers) Assert.True(defeats > 0, "precondition: a weak seat with no worker and a 1-hp hall fell");
    }

    /// <summary>
    /// A hall site, its only builder a summon (the player's last unit): the hall's last work lands in phase 4, the expiry in
    /// phase 5. Expiring on the finishing tick: the hall finished first, the side stays in. Expiring one tick before: the site
    /// never finishes and the side is out on that tick, the site standing.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public void AHallFinishing_OnTheTickItsLastUnitExpires_KeepsTheSideIn(int offset, bool defeated)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(2, 2, 32, 64), LocalMovementTests.Flat(48)));
        World w = sim.World;
        int hall = BuildingType(w, 0, BuildingSlot.TownHall), workerType = UnitType(w, 0, UnitSlot.Worker);
        Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 34);
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        Assert.True(w.Units.TrySpawn(0, workerType, w.Data.Units[workerType], At(sim, 15, 15), int.MaxValue, out EntityHandle worker));
        sim.Enqueue(Command.Build(0, worker, hall, At(sim, 10, 10)));
        Applied(sim);
        int s = w.Buildings.SlotAt(10, 10);
        Assert.True(s >= 0 && w.Buildings.UnderConstruction[s]);
        EntityHandle site = w.Buildings.HandleOf(s);
        int needed = w.Buildings.WorkNeeded(hall);
        int step = 0;
        for (int t = 0; t < 2000 && step == 0; t++)
        {
            int before = w.Buildings.Work[s];
            sim.Tick();
            step = w.Buildings.Work[s] - before;
        }
        Assert.True(step > 0, "the builder never worked");
        // Stop where the next tick doesn't finish the site and the one after does.
        while (w.Buildings.Work[s] + 2 * step < needed) sim.Tick();
        Assert.True(w.Buildings.Work[s] + step < needed, "precondition: the site finishes on the next tick");
        int finishing = sim.TickNumber + 1;
        w.Units.ExpiresAtTick[worker.Index] = finishing + offset;
        Assert.False(w.IsDefeated(0));
        sim.Tick();
        if (offset < 0)
        {
            Assert.False(w.Units.IsAlive(worker));
            Assert.Equal(new[] { new DefeatEvent(0, finishing - 1) }, w.DefeatEvents.ToArray());
        }
        sim.Tick();
        Assert.False(w.Units.IsAlive(worker));
        Assert.True(w.Buildings.IsAlive(site), "the site went");
        Assert.Equal(defeated, w.Buildings.UnderConstruction[s]);
        Assert.Equal(defeated ? 0 : 1, w.ProductionBuildings(0));
        Assert.Equal(0, w.LiveUnits(0));
        Assert.Equal(defeated, w.IsDefeated(0));
        Assert.Equal(defeated, sim.MatchOver);
        for (int t = 0; t < 50; t++) sim.Tick();
        Assert.Equal(defeated, w.IsDefeated(0));
        CheckRule(w, "end");
    }

    /// <summary>
    /// The door on the buildings a defeated player keeps: player 0 holds a finished Forge researching, a finished House and a
    /// Town Hall site, and its last unit (a summon) expires. After the defeat, a Cancel of its own hall site, a CancelTrain
    /// and a second Research at its own Forge, a SetRally / ClearRally there, a Train at it, a SpawnUnit and a SpawnBuilding
    /// all hash equal, every tick, to a twin that sent Noops; the site stands, the research stays queued, the gold doesn't
    /// move. The control (<paramref name="defeated"/> false: a keeper unit keeps the side in) shows the same commands do
    /// apply to a side still in, so the door is what drops them.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADefeatedPlayersCommandsOnItsStandingBuildings_AreDropped(bool defeated)
    {
        int forgeSlot = -1, techA = -1, techB = -1;
        (Simulation Sim, EntityHandle Site, EntityHandle Forge) Make()
        {
            var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(5, 2, 64, 256), LocalMovementTests.Flat(48)));
            World w = sim.World;
            EntityHandle forge = Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.Forge), 6, 6);
            Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.House), 6, 20);
            EntityHandle site = Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.TownHall), 16, 6, site: true);
            Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 36, 36);
            CombatScenes.Place(sim, 1, UnitType(w, 1, UnitSlot.Worker), At(sim, 40, 30));
            BuildMaps.SetTotals(sim, 0, 50_000, 50_000);
            int worker = UnitType(w, 0, UnitSlot.Worker);
            Assert.True(w.Units.TrySpawn(0, worker, w.Data.Units[worker], At(sim, 30, 30), sim.TickNumber + 6, out _));
            if (!defeated) TestSim.KeepIn(sim, 0);
            forgeSlot = forge.Index;
            if (techA < 0)
            {
                foreach (int tech in w.Data.TechsResearchableAt(w.Buildings.TypeId[forge.Index]))
                {
                    if (!w.CanResearch(0, forge.Index, tech, out _)) continue;
                    if (techA < 0) techA = tech;
                    else if (techB < 0) techB = tech;
                }
                Assert.True(techA >= 0 && techB >= 0, "precondition: two techs researchable at the Forge now");
            }
            sim.Enqueue(Command.Research(0, Centre(sim, forge), techA));
            Applied(sim);
            Assert.Equal(1, w.Buildings.QueueCount[forge.Index]);
            for (int t = 0; t < 8; t++) sim.Tick();
            Assert.Equal(defeated, w.IsDefeated(0));
            return (sim, site, forge);
        }

        var a = Make();
        var b = Make();
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        World wa = a.Sim.World;
        int gold = wa.Gold[0], wood = wa.Wood[0];
        Vector2 forgeAt = Centre(a.Sim, a.Forge), siteAt = Centre(a.Sim, a.Site);
        Command[] rows =
        {
            Command.Cancel(0, siteAt),
            Command.CancelTrain(0, forgeAt, 0),
            Command.Research(0, forgeAt, techB),
            Command.SetRally(0, wa.Buildings.Cell[a.Forge.Index], At(a.Sim, 30, 30)),
            Command.ClearRally(0, forgeAt),
            Command.SpawnUnit(0, UnitType(wa, 0, UnitSlot.Line), At(a.Sim, 24, 24)),
            Command.SpawnBuilding(0, BuildingType(wa, 0, BuildingSlot.House), At(a.Sim, 24, 40)),
        };
        bool anyDiffered = false;
        int progressAtDefeat = wa.Buildings.Progress[forgeSlot];
        foreach (Command c in rows)
        {
            a.Sim.Enqueue(c);
            b.Sim.Enqueue(Command.Noop(0));
            for (int t = 0; t < 4; t++)
            {
                a.Sim.Tick();
                b.Sim.Tick();
                if (a.Sim.PendingCommandCount != 0) continue;
                bool same = a.Sim.StateHash() == b.Sim.StateHash();
                if (defeated) Assert.True(same, $"{c.Kind}: the runs differ after tick {a.Sim.TickNumber}");
                else anyDiffered |= !same;
            }
        }
        if (!defeated)
        {
            // The control: the side still in, the Cancel took its site (refunded) and the commands applied.
            Assert.True(anyDiffered, "control: the commands changed nothing for a side still in");
            Assert.False(wa.Buildings.IsAlive(a.Site), "control: the Cancel of a side still in didn't apply");
            return;
        }
        Assert.True(wa.IsDefeated(0));
        Assert.True(wa.Buildings.IsAlive(a.Site), "a defeated player's Cancel took its site");
        Assert.True(wa.Buildings.UnderConstruction[a.Site.Index]);
        Assert.Equal(0, wa.LiveUnits(0));
        Assert.Equal(0, wa.ProductionBuildings(0));
        Assert.Equal((gold, wood), (wa.Gold[0], wa.Wood[0]));
        _out.WriteLine($"after the defeat: the Forge's queue {wa.Buildings.QueueCount[forgeSlot]}, progress {progressAtDefeat} -> {wa.Buildings.Progress[forgeSlot]} over {rows.Length * 4} ticks, tech A researched {wa.Techs.Has(0, techA)}");
        CheckRule(wa, "end");
    }

    /// <summary>
    /// Four seats, one never built (units only): players 0, 1 and 3 have a hall each, player 2 an army and no building. Player
    /// 1's hall goes: out. Player 2's units all die: not out (never built), the match goes on. Player 3's hall goes: out, and
    /// the match is over (player 0 the last that built standing) while player 2, never built, is ignored. Player 2 then places
    /// a House and spawns a unit (both apply: it isn't defeated): the result stays latched; its unit dies: out, the House
    /// standing (a House alone doesn't keep a side in), one event.
    /// </summary>
    [Fact]
    public void FourSeats_ANeverBuiltSeatMixedIn_IgnoredByTheResult_ThenBuildsAndFalls()
    {
        Simulation sim = CombatScenes.Flat(64, 128, 4);
        World w = sim.World;
        Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.TownHall), 6, 6);
        EntityHandle h1 = Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 50, 6);
        EntityHandle h3 = Spawn(sim, 3, BuildingType(w, 3, BuildingSlot.TownHall), 50, 50);
        var army2 = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) army2.Add(CombatScenes.Place(sim, 2, UnitType(w, 2, UnitSlot.Line), At(sim, 8 + k, 52)));
        sim.Tick();
        CheckRule(w, "start");
        Assert.False(sim.MatchOver);
        w.Buildings.Free(h1);
        sim.Tick();
        Assert.Equal(new[] { new DefeatEvent(1, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.False(sim.MatchOver);
        foreach (EntityHandle u in army2) w.Units.Free(u);
        for (int t = 0; t < 5; t++)
        {
            sim.Tick();
            Assert.Equal(0, w.DefeatEvents.Length);
        }
        Assert.False(w.IsDefeated(2));
        Assert.False(w.EverBuilt(2));
        Assert.False(sim.MatchOver);
        w.Buildings.Free(h3);
        sim.Tick();
        Assert.Equal(new[] { new DefeatEvent(3, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.True(sim.MatchOver);
        CheckRule(w, "over");
        sim.Enqueue(Command.SpawnUnit(2, UnitType(w, 2, UnitSlot.Worker), At(sim, 20, 52)));
        sim.Enqueue(Command.SpawnBuilding(2, BuildingType(w, 2, BuildingSlot.House), At(sim, 30, 52)));
        // The defeated players' own tries are dropped on the same tick.
        sim.Enqueue(Command.SpawnUnit(1, UnitType(w, 1, UnitSlot.Worker), At(sim, 50, 20)));
        sim.Enqueue(Command.SpawnBuilding(3, BuildingType(w, 3, BuildingSlot.TownHall), At(sim, 40, 30)));
        Applied(sim);
        Assert.True(w.EverBuilt(2));
        Assert.Equal(1, w.LiveUnits(2));
        Assert.Equal(0, w.LiveUnits(1));
        Assert.Equal(0, w.LiveUnits(3));
        Assert.Equal(0, w.ProductionBuildings(3));
        Assert.False(w.IsDefeated(2));
        Assert.True(sim.MatchOver, "the latched result was taken back");
        EntityHandle worker2 = default;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 2) worker2 = new EntityHandle(i, w.Units.Generation[i]);
        w.Units.Free(worker2);
        sim.Tick();
        Assert.Equal(new[] { new DefeatEvent(2, sim.TickNumber - 1) }, w.DefeatEvents.ToArray());
        Assert.True(w.IsDefeated(2));
        Assert.False(w.IsDefeated(0));
        Assert.Equal(1, w.Ledger.LiveBuildings[2]);
        Assert.True(sim.MatchOver);
        CheckRule(w, "end");
    }

    /// <summary>
    /// Measured, not a rule (BUG-0703): since M5-4e a defeated player can keep buildings standing, and nothing in the sim
    /// switches them off. Player 0, defeated (its last unit gone; a finished tower and a Forge researching left), still
    /// shoots: its tower kills player 1's soldier parked in range after the defeat, a kill counted for player 0. The row
    /// prints what happened and asserts the preconditions (and the current behaviour, so a change is seen).
    /// </summary>
    [Fact]
    public void ADefeatedPlayersTower_KeepsShooting_Measured()
    {
        Simulation sim = CombatScenes.Flat(48, 64, 2);
        World w = sim.World;
        TestSim.Explored(sim);
        EntityHandle tower = Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.WatchTower), 20, 20);
        Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 38, 38);
        EntityHandle last = CombatScenes.Place(sim, 0, UnitType(w, 0, UnitSlot.Worker), At(sim, 4, 40));
        sim.Tick();
        Assert.False(w.IsDefeated(0));
        w.Units.Free(last);
        sim.Tick();
        Assert.True(w.IsDefeated(0), "precondition: defeated with the tower standing");
        Assert.True(w.Buildings.IsAlive(tower));
        EntityHandle soldier = CombatScenes.Place(sim, 1, UnitType(w, 1, UnitSlot.Line), At(sim, 24, 21));
        int hp0 = w.Units.Hp[soldier.Index];
        int kills0 = w.Kills[0];
        for (int t = 0; t < 1200 && w.Units.IsAlive(soldier); t++) sim.Tick();
        bool dead = !w.Units.IsAlive(soldier);
        _out.WriteLine($"a defeated player's tower vs a soldier parked in range for 60 s: soldier dead {dead} (hp {hp0} -> {(dead ? 0 : w.Units.Hp[soldier.Index])}), player 0's kills {kills0} -> {w.Kills[0]}");
        Assert.True(dead, "the defeated player's tower stopped shooting (behaviour changed: update BUG-0703)");
        Assert.Equal(kills0 + 1, w.Kills[0]);
    }

    /// <summary>
    /// Determinism of a defeat by the new rule in a fight: two identical runs (player 1's hall razed, then its two workers
    /// hunted by player 0's soldiers through shift-queued Attacks) hash equal after every tick, the defeat lands on the same
    /// tick, and the rule holds both ways every tick (the hall down first, the side out only with its last worker).
    /// </summary>
    [Fact]
    public void ADefeatByTheLastUnitsDeath_HashTwinsEqual_AndTheFlagIsHashed()
    {
        (Simulation Sim, EntityHandle W1, EntityHandle W2) Make()
        {
            Simulation sim = CombatScenes.Flat(48, 64, 2);
            World w = sim.World;
            Spawn(sim, 0, BuildingType(w, 0, BuildingSlot.TownHall), 6, 6);
            EntityHandle hall = Spawn(sim, 1, BuildingType(w, 1, BuildingSlot.TownHall), 34, 34);
            w.Buildings.SetHp(hall.Index, 1);
            EntityHandle w1 = CombatScenes.Place(sim, 1, UnitType(w, 1, UnitSlot.Worker), At(sim, 30, 28));
            EntityHandle w2 = CombatScenes.Place(sim, 1, UnitType(w, 1, UnitSlot.Worker), At(sim, 28, 30));
            for (int k = 0; k < 4; k++)
            {
                EntityHandle s = CombatScenes.Place(sim, 0, CombatScenes.HeavyInfantry, At(sim, 32 + k % 2, 31 + k / 2));
                sim.Enqueue(Command.Attack(0, s, hall, isBuilding: true));
                CombatScenes.Spot(sim, 0, w1);
                CombatScenes.Spot(sim, 0, w2);
                sim.Enqueue(Command.Attack(0, s, w1, isBuilding: false, queued: true));
                sim.Enqueue(Command.Attack(0, s, w2, isBuilding: false, queued: true));
            }
            return (sim, w1, w2);
        }

        var a = Make();
        var b = Make();
        int defeatedAt = -1;
        for (int t = 0; t < 3000 && (defeatedAt < 0 || a.Sim.TickNumber < defeatedAt + 100); t++)
        {
            a.Sim.Tick();
            b.Sim.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"the twins differ after tick {a.Sim.TickNumber - 1}");
            Assert.Equal(a.Sim.World.IsDefeated(1), b.Sim.World.IsDefeated(1));
            CheckRule(a.Sim.World, $"tick {a.Sim.TickNumber - 1}");
            if (defeatedAt < 0 && a.Sim.World.IsDefeated(1)) defeatedAt = a.Sim.TickNumber - 1;
        }
        Assert.True(defeatedAt >= 0, $"player 1 stood: {a.Sim.World.LiveUnits(1)} units, {a.Sim.World.ProductionBuildings(1)} halls");
        Assert.True(a.Sim.MatchOver);
        Assert.False(a.Sim.World.Units.IsAlive(a.W1) || a.Sim.World.Units.IsAlive(a.W2));
        _out.WriteLine($"defeated at tick {defeatedAt}");
    }
}
