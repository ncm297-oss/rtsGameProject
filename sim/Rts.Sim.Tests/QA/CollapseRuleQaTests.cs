using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H2 (session 2026-10-16-0847): attacks on the collapse rule (BUG-0703, docs/02 "Game flow" 4, docs/03 "The collapse"):
/// a defeated player's every building is freed on its defeat tick, one <see cref="DeathEvent.Collapsed"/> event each, neither a
/// kill nor a loss. Beyond the developer's store-seam rows: a 4-seat fuzz whose defeats come from combat (towers, Forges
/// researching, worked sites, Houses, halls), twin-hashed every tick, with every invariant of the rule checked after each tick;
/// two players out on the same tick; the last builder killed on its site; a tower mid-wind-up; a collapsed building's slot
/// reused with stale surround claims on it.
/// </summary>
public class CollapseRuleQaTests
{
    private readonly ITestOutputHelper _out;

    public CollapseRuleQaTests(ITestOutputHelper output) => _out = output;

    private const int Players = 4;
    private const int Size = 64;

    private static int BType(World w, int p, BuildingSlot slot) => StartBase.BuildingOfSlot(w.Data, w.FactionOf(p), slot);

    private static int UType(World w, int p, UnitSlot slot) => StartBase.UnitOfSlot(w.Data, w.FactionOf(p), slot);

    private static (int X, int Y) Base(int p) => (p % 2 == 0 ? 14 : 46, p < 2 ? 14 : 46);

    /// <summary>A building of <paramref name="slot"/> for <paramref name="p"/> near its base: the first anchor on a fixed spiral the store takes, or default.</summary>
    private static EntityHandle Place(Simulation sim, int p, BuildingSlot slot, bool site, ref SimRng rng)
    {
        World w = sim.World;
        int type = BType(w, p, slot);
        if (type < 0) return default;
        (int bx, int by) = Base(p);
        for (int tries = 0; tries < 60; tries++)
        {
            int x = bx + rng.NextInt(-9, 10), y = by + rng.NextInt(-9, 10);
            if (x < 1 || y < 1 || x >= Size - 5 || y >= Size - 5) continue;
            if (w.Buildings.Spawn(p, type, y * Size + x, out EntityHandle h, site)) return h;
        }
        return default;
    }

    private static Vector2 Centre(World w, int slot)
    {
        CombatSystem.BuildingRect(w, slot, out Vector2 min, out Vector2 max);
        return (min + max) * 0.5f;
    }

    /// <summary>
    /// The fuzz scene: four players, each with a random mix of a House, a Forge (researching when a tech is open), a tower and a
    /// hall site with its workers on it; players 0 and 3 also a finished hall (they stay in while it stands) and a bigger army.
    /// Players 1 and 2 have a few units only, so combat takes them out with buildings standing.
    /// </summary>
    private static Simulation Scene(ulong seed)
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(Size, 400, Players, seed));
        World w = sim.World;
        var rng = new SimRng(seed, 7031);
        for (int p = 0; p < Players; p++)
        {
            BuildMaps.SetTotals(sim, p, 20_000, 20_000);
            bool strong = p == 0 || p == 3;
            (int bx, int by) = Base(p);
            int units = strong ? 14 + rng.NextInt(0, 8) : 2 + rng.NextInt(0, 3);
            for (int k = 0; k < units; k++)
            {
                UnitSlot slot = (k % 4) switch { 0 => UnitSlot.Worker, 1 or 2 => UnitSlot.Line, _ => UnitSlot.Ranged };
                int type = UType(w, p, slot);
                if (type < 0) type = UType(w, p, UnitSlot.Line);
                CombatScenes.Place(sim, p, type, CombatScenes.At(sim, bx + rng.NextInt(-4, 5), by + rng.NextInt(-4, 5), rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f));
            }
            if (rng.NextInt(0, 4) != 0) Place(sim, p, BuildingSlot.House, false, ref rng);
            if (rng.NextInt(0, 4) != 0) Place(sim, p, BuildingSlot.House, false, ref rng);
            EntityHandle forge = rng.NextInt(0, 4) != 0 ? Place(sim, p, BuildingSlot.Forge, false, ref rng) : default;
            if (rng.NextInt(0, 3) != 0) Place(sim, p, BuildingSlot.WatchTower, false, ref rng);
            if (rng.NextInt(0, 3) != 0) Place(sim, p, BuildingSlot.TownHall, true, ref rng);
            if (strong) Place(sim, p, BuildingSlot.TownHall, false, ref rng);
            if (forge.Generation != 0)
                foreach (int t in w.Data.TechsResearchableAt(w.Buildings.TypeId[forge.Index]))
                    if (w.CanResearch(p, forge.Index, t, out _))
                    {
                        sim.Enqueue(Command.Research(p, Centre(w, forge.Index), t));
                        break;
                    }
        }
        return sim;
    }

    /// <summary>This tick's random orders (identical for twins): attack-moves at enemy bases, Attacks on enemy buildings, joins and repairs of own buildings, Trains and Researches, the odd store-seam removal of a unit.</summary>
    private static void Orders(Simulation sim, ref SimRng rng, int t)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int n = 0; n < 8; n++)
        {
            int i = rng.NextInt(0, u.Capacity);
            int kind = rng.NextInt(0, 10);
            int k = rng.NextInt(0, b.Capacity);
            if (!u.Alive[i]) continue;
            int p = u.Owner[i];
            var h = new EntityHandle(i, u.Generation[i]);
            // A building: the first live one from slot k on (any owner).
            int slot = -1;
            for (int m = 0; m < b.Capacity && slot < 0; m++)
                if (b.Alive[(k + m) % b.Capacity]) slot = (k + m) % b.Capacity;
            if (kind <= 2)
            {
                int enemy = (p + 1 + rng.NextInt(0, Players - 1)) % Players;
                (int ex, int ey) = Base(enemy);
                sim.Enqueue(Command.AttackMove(p, h, CombatScenes.At(sim, ex, ey)));
            }
            else if (kind <= 5 && slot >= 0 && b.Owner[slot] != p) sim.Enqueue(Command.Attack(p, h, b.HandleOf(slot), isBuilding: true, queued: kind == 5));
            else if (kind == 6 && slot >= 0 && b.Owner[slot] == p)
            {
                if (b.UnderConstruction[slot]) sim.Enqueue(Command.Build(p, h, b.TypeId[slot], w.NavGrid.CellCenter(b.Cell[slot] % Size, b.Cell[slot] / Size)));
                else sim.Enqueue(Command.Repair(p, h, Centre(w, slot)));
            }
            else if (kind == 7 && slot >= 0)
            {
                var trains = w.Data.UnitsTrainedAt(b.TypeId[slot]);
                if (!trains.IsEmpty) sim.Enqueue(Command.Train(b.Owner[slot], Centre(w, slot), trains[rng.NextInt(0, trains.Length)]));
                else foreach (int tech in w.Data.TechsResearchableAt(b.TypeId[slot]))
                    {
                        sim.Enqueue(Command.Research(b.Owner[slot], Centre(w, slot), tech));
                        break;
                    }
            }
            else if (kind == 8) sim.Enqueue(Command.Move(p, h, CombatScenes.At(sim, rng.NextInt(2, Size - 2), rng.NextInt(2, Size - 2))));
        }
        // Every 97 ticks a unit of a random player is taken away (a store seam, no event), so defeats come sooner.
        if (t % 97 == 96)
        {
            int start = rng.NextInt(0, u.Capacity);
            for (int m = 0; m < u.Capacity; m++)
            {
                int j = (start + m) % u.Capacity;
                if (u.Alive[j] && u.Owner[j] != 0 && u.Owner[j] != 3)
                {
                    u.Free(new EntityHandle(j, u.Generation[j]));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The collapse rule's invariants, checked after every tick (call <see cref="Before"/> just before the tick and
    /// <see cref="After"/> just after): a defeated player has no building and no unit, and its gold, wood and techs never move
    /// after its defeat tick; every Collapsed event is a building of a player defeated this tick, no killer, not expired, its
    /// slot freed, no unit left targeting or building it, its footprint open (or a pocket); no player not defeated this tick
    /// collapses; each player's kills and losses move by exactly the tick's counted deaths; no projectile is fired by a player
    /// defeated on an earlier tick.
    /// </summary>
    private sealed class Checker
    {
        private readonly World _w;
        private readonly string _where;
        private readonly int _players;
        private readonly int[] _kills, _losses, _defeatTick, _goldAt, _woodAt, _queued, _builders;
        private readonly bool[][] _techsAt;
        private readonly bool[] _projAlive;
        private readonly (int Type, bool Site, int Cell)[] _before;
        public int Collapses, CollapsedSites, WorkedSites, ResearchingOrTraining, Towers, DefeatsWithBuildings;

        public Checker(World w, string where)
        {
            _w = w;
            _where = where;
            _players = w.Config.PlayerCount;
            _kills = w.Kills.ToArray();
            _losses = w.Losses.ToArray();
            _defeatTick = new int[_players];
            Array.Fill(_defeatTick, -1);
            _goldAt = new int[_players];
            _woodAt = new int[_players];
            _techsAt = new bool[_players][];
            _projAlive = new bool[w.Projectiles.Capacity];
            _queued = new int[w.Buildings.Capacity];
            _builders = new int[w.Buildings.Capacity];
            _before = new (int, bool, int)[w.Buildings.Capacity];
        }

        public void Before()
        {
            BuildingStore bs = _w.Buildings;
            UnitStore u = _w.Units;
            Array.Clear(_builders);
            for (int k = 0; k < bs.Capacity; k++)
            {
                _queued[k] = bs.Alive[k] ? bs.QueueCount[k] : 0;
                _before[k] = bs.Alive[k] ? (bs.TypeId[k], bs.UnderConstruction[k], bs.Cell[k]) : default;
            }
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.BuildTarget[i].Generation != 0 && bs.IsAlive(u.BuildTarget[i])) _builders[u.BuildTarget[i].Index]++;
        }

        public void After(int tick)
        {
            World w = _w;
            UnitStore u = w.Units;
            BuildingStore bs = w.Buildings;
            int width = w.NavGrid.Width;
            var outNow = new bool[_players];
            foreach (DefeatEvent e in w.DefeatEvents)
            {
                outNow[e.Player] = true;
                _defeatTick[e.Player] = tick;
                _goldAt[e.Player] = w.Gold[e.Player];
                _woodAt[e.Player] = w.Wood[e.Player];
                _techsAt[e.Player] = new bool[w.Data.Techs.Length];
                for (int tech = 0; tech < w.Data.Techs.Length; tech++) _techsAt[e.Player][tech] = w.HasTech(e.Player, tech);
            }
            var collapsedOf = new int[_players];
            foreach (DeathEvent d in w.Deaths)
            {
                if (d.Collapsed)
                {
                    Collapses++;
                    collapsedOf[d.VictimOwner]++;
                    Assert.True(d.IsBuilding && d.KillerOwner == -1 && !d.Expired, $"{_where} tick {tick}: {d}");
                    Assert.True(outNow[d.VictimOwner], $"{_where} tick {tick}: player {d.VictimOwner}'s building collapsed while it isn't out this tick");
                    Assert.False(bs.IsAlive(d.Victim), $"{_where} tick {tick}: collapsed {d.Victim} still alive");
                    int k = d.Victim.Index;
                    Assert.Equal(_before[k].Type, d.VictimType);
                    BuildingDef def = w.Data.Buildings[_before[k].Type];
                    for (int dy = 0; dy < def.FootprintHeight; dy++)
                        for (int dx = 0; dx < def.FootprintWidth; dx++)
                        {
                            int x = _before[k].Cell % width + dx, y = _before[k].Cell / width + dy;
                            NavFlags f = w.NavGrid.FlagsAt(x, y);
                            Assert.True(w.NavGrid.IsPassable(x, y) || (f & NavFlags.Pocket) != 0 || bs.SlotAt(x, y) >= 0,
                                $"{_where} tick {tick}: collapsed building {k}'s cell ({x}, {y}) stayed shut ({f})");
                        }
                    for (int i = 0; i < u.Capacity; i++)
                    {
                        if (!u.Alive[i]) continue;
                        Assert.False(u.TargetIsBuilding[i] && u.Target[i] == d.Victim, $"{_where} tick {tick}: unit {i} still targets collapsed {d.Victim}");
                        Assert.False(u.BuildTarget[i] == d.Victim, $"{_where} tick {tick}: unit {i} still builds collapsed {d.Victim}");
                    }
                    if (_before[k].Site)
                    {
                        CollapsedSites++;
                        if (_builders[k] > 0) WorkedSites++;
                    }
                    if (def.Attack != null) Towers++;
                    if (_queued[k] > 0) ResearchingOrTraining++;
                    continue;
                }
                if (d.Expired) continue;
                if (d.KillerOwner >= 0) _kills[d.KillerOwner]++;
                _losses[d.VictimOwner]++;
            }
            for (int p = 0; p < _players; p++)
                if (outNow[p] && collapsedOf[p] > 0) DefeatsWithBuildings++;
            Assert.Equal(_kills, w.Kills.ToArray());
            Assert.Equal(_losses, w.Losses.ToArray());
            for (int p = 0; p < _players; p++)
            {
                if (!w.IsDefeated(p)) continue;
                Assert.Equal(0, w.Ledger.LiveBuildings[p]);
                Assert.Equal(0, w.LiveUnits(p));
                Assert.Equal(0, w.Ledger.HalfPopProvided[p]);
                Assert.True((_goldAt[p], _woodAt[p]) == (w.Gold[p], w.Wood[p]), $"{_where} tick {tick}: defeated player {p}'s resources moved after its defeat tick {_defeatTick[p]}");
                for (int tech = 0; tech < w.Data.Techs.Length; tech++)
                    Assert.True(_techsAt[p][tech] == w.HasTech(p, tech), $"{_where} tick {tick}: defeated player {p} got tech {tech} after its defeat");
            }
            for (int k = 0; k < bs.Capacity; k++)
                if (bs.Alive[k]) Assert.False(w.IsDefeated(bs.Owner[k]), $"{_where} tick {tick}: defeated player {bs.Owner[k]}'s building {k} stands");
            ReadOnlySpan<bool> alive = w.Projectiles.Alive;
            for (int k = 0; k < alive.Length; k++)
            {
                if (alive[k] && !_projAlive[k])
                {
                    int o = w.Projectiles.Owner[k];
                    Assert.False(_defeatTick[o] >= 0 && _defeatTick[o] < tick, $"{_where} tick {tick}: player {o}, out since {_defeatTick[o]}, fired projectile {k}");
                }
                _projAlive[k] = alive[k];
            }
        }

        public string Summary() =>
            $"defeats at {string.Join(", ", _defeatTick)}; {Collapses} collapses ({CollapsedSites} sites, {WorkedSites} worked, {ResearchingOrTraining} researching / training, {Towers} towers); {DefeatsWithBuildings} defeats with buildings; kills {string.Join("/", _w.Kills.ToArray())}, losses {string.Join("/", _w.Losses.ToArray())}";
    }

    /// <summary>
    /// 4 seats x 2,000 ticks of the fuzz scene, twins hash-equal after every tick, <see cref="Checker"/>'s invariants after every
    /// tick. Preconditions: at least one defeat with buildings standing, two collapses.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void FourSeats_CombatDefeats_EveryCollapseInvariant_TwinsEqual(ulong seed)
    {
        Simulation a = Scene(seed), b = Scene(seed);
        var rngA = new SimRng(seed, 7032);
        var rngB = new SimRng(seed, 7032);
        var check = new Checker(a.World, $"seed {seed}");
        for (int t = 0; t < 2000; t++)
        {
            Orders(a, ref rngA, t);
            Orders(b, ref rngB, t);
            check.Before();
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber - 1}");
            check.After(a.TickNumber - 1);
        }
        _out.WriteLine($"seed {seed}: {check.Summary()}");
        Assert.True(check.DefeatsWithBuildings >= 1 && check.Collapses >= 2, $"seed {seed}: precondition: {check.Summary()}");
    }

    /// <summary>
    /// A 4-seat match, three AI seats (combat on, the early-wave data): seat 3 Hard with money, seat 0 a human seat whose 24
    /// soldiers the test attack-moves at the weak seats' halls every 200 ticks; seats 1 and 2 Easy, their hall
    /// at 1 hit point and no workers (staging), a House, a Forge researching and a tower beside it, then no money. Combat ends the weak seats (no seam on
    /// a unit after the staging). Twins hash-equal and <see cref="Checker"/>'s invariants after every tick, up to
    /// 9,000 ticks or 500 after both weak seats are out. Precondition: at least one weak seat out by play with buildings
    /// collapsing.
    /// </summary>
    [Theory]
    [InlineData(5UL)]
    public void FourAiSeats_WeakSeatsFallByPlay_EveryCollapseInvariant_TwinsEqual(ulong seed)
    {
        Simulation Make()
        {
            var seats = new PlayerSeat[]
            {
                new(0, SeatController.Human, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Easy),
                new(0, SeatController.Ai, AiDifficulty.Easy), new(1, SeatController.Ai, AiDifficulty.Hard),
            };
            Simulation sim = AiScenes.Flat(seats, AiScenes.EarlyWaves(2), units: 600, commands: 2048, seed: seed, combat: true, workers: 5);
            World w = sim.World;
            foreach (int p in new[] { 0, 3 }) BuildMaps.SetTotals(sim, p, 4000, 4000);
            // Seat 0 (human): 24 line soldiers by its hall, sent by the test (below).
            (int h0x, int h0y) = AiScenes.HallAt(0);
            int line0 = UType(w, 0, UnitSlot.Line);
            for (int k = 0; k < 24; k++) CombatScenes.Place(sim, 0, line0, w.NavGrid.CellCenter(h0x + k % 6, h0y + 7 + k / 6));
            foreach (int p in new[] { 1, 2 })
            {
                (int hx, int hy) = AiScenes.HallAt(p);
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p) w.Buildings.SetHp(k, 1);
                // Staging, before any tick: no workers (the AI hunts no units before M5-4d), so the hall razed in play ends it.
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i] && w.Units.Owner[i] == p) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
                int[] types = { BType(w, p, BuildingSlot.House), BType(w, p, BuildingSlot.Forge), BType(w, p, BuildingSlot.WatchTower) };
                EntityHandle forge = default;
                foreach (int type in types)
                {
                    bool placed = false;
                    for (int d = 6; d < 16 && !placed; d++)
                        foreach ((int dx, int dy) in new[] { (d, 0), (-d, 0), (0, d), (0, -d), (d, d), (-d, -d) })
                        {
                            if (placed) break;
                            if (w.Buildings.Spawn(p, type, (hy + dy) * w.NavGrid.Width + hx + dx, out EntityHandle h))
                            {
                                placed = true;
                                if (type == types[1]) forge = h;
                            }
                        }
                }
                BuildMaps.SetTotals(sim, p, 5000, 5000);
                if (forge.Generation != 0)
                    foreach (int t in w.Data.TechsResearchableAt(w.Buildings.TypeId[forge.Index]))
                        if (w.CanResearch(p, forge.Index, t, out _))
                        {
                            sim.Enqueue(Command.Research(p, Centre(w, forge.Index), t));
                            break;
                        }
            }
            return sim;
        }
        Simulation a = Make(), b = Make();
        World wa = a.World;
        a.Tick();
        b.Tick();
        foreach (Simulation s in new[] { a, b })
            foreach (int p in new[] { 1, 2 }) BuildMaps.SetTotals(s, p, 0, 0);
        var check = new Checker(wa, $"seed {seed}");
        int lastOut = -1, sentAt = -1;
        while (a.TickNumber < 9000 && (lastOut < 0 || a.TickNumber < lastOut + 500))
        {
            // Every 200 ticks seat 0's soldiers attack-move at the first weak seat still in (its hall).
            if (a.TickNumber % 200 == 1)
            {
                int target = !wa.IsDefeated(1) ? 1 : !wa.IsDefeated(2) ? 2 : -1;
                if (target >= 0)
                {
                    (int tx, int ty) = AiScenes.HallAt(target);
                    foreach (Simulation s in new[] { a, b })
                        for (int i = 0; i < s.World.Units.Capacity; i++)
                            if (s.World.Units.Alive[i] && s.World.Units.Owner[i] == 0)
                                s.Enqueue(Command.AttackMove(0, new EntityHandle(i, s.World.Units.Generation[i]), s.World.NavGrid.CellCenter(tx + 1, ty + 1)));
                    if (sentAt < 0) sentAt = a.TickNumber;
                }
            }
            check.Before();
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber - 1}");
            check.After(a.TickNumber - 1);
            if (lastOut < 0 && wa.IsDefeated(1) && wa.IsDefeated(2)) lastOut = a.TickNumber;
        }
        _out.WriteLine($"seed {seed} at tick {a.TickNumber}: {check.Summary()}; seat 3 waves {a.AiOf(3)!.Board.WavesStarted}");
        Assert.True(check.DefeatsWithBuildings >= 1, $"seed {seed}: precondition: no weak seat fell by play with buildings standing: {check.Summary()}");
    }

    /// <summary>
    /// Two players out on one tick (both last units taken away between ticks): both sides' buildings collapse that tick, the
    /// events in player order then slot order; player 0's chasers of both stand down that tick and its builder repairing
    /// nothing is untouched; kills and losses unmoved; twins hash equal.
    /// </summary>
    [Fact]
    public void TwoPlayersOutOnOneTick_BothCollapse_InPlayerThenSlotOrder()
    {
        Simulation Make()
        {
            Simulation sim = TestSim.Explored(CombatScenes.Flat(48, 64, 3));
            World w = sim.World;
            var rng = new SimRng(5, 1);
            CombatScenes.Place(sim, 1, UType(w, 1, UnitSlot.Worker), CombatScenes.At(sim, 40, 6));
            CombatScenes.Place(sim, 2, UType(w, 2, UnitSlot.Worker), CombatScenes.At(sim, 40, 40));
            // Player 2's buildings first in slot order, then player 1's: the events must still come player 1 first.
            Assert.True(w.Buildings.Spawn(2, BType(w, 2, BuildingSlot.House), 30 * 48 + 30, out _));
            Assert.True(w.Buildings.Spawn(1, BType(w, 1, BuildingSlot.House), 6 * 48 + 30, out _));
            Assert.True(w.Buildings.Spawn(2, BType(w, 2, BuildingSlot.Forge), 36 * 48 + 22, out _));
            Assert.True(w.Buildings.Spawn(1, BType(w, 1, BuildingSlot.WatchTower), 12 * 48 + 36, out _));
            Assert.True(w.Buildings.Spawn(0, BType(w, 0, BuildingSlot.TownHall), 20 * 48 + 4, out _));
            for (int k = 0; k < 4; k++)
            {
                EntityHandle hi = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Line), CombatScenes.At(sim, 20 + k, 20));
                int target = k % 2 == 0 ? 0 : 1; // slot 0 is player 2's House, slot 1 player 1's House
                sim.Enqueue(Command.Attack(0, hi, w.Buildings.HandleOf(target), isBuilding: true));
            }
            return sim;
        }
        Simulation a = Make(), b = Make();
        for (int t = 0; t < 30; t++)
        {
            a.Tick();
            b.Tick();
        }
        World wa = a.World;
        int[] kills = wa.Kills.ToArray(), losses = wa.Losses.ToArray();
        foreach (Simulation s in new[] { a, b })
            for (int i = 0; i < s.World.Units.Capacity; i++)
                if (s.World.Units.Alive[i] && s.World.Units.Owner[i] != 0) s.World.Units.Free(new EntityHandle(i, s.World.Units.Generation[i]));
        a.Tick();
        b.Tick();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(new[] { new DefeatEvent(1, a.TickNumber - 1), new DefeatEvent(2, a.TickNumber - 1) }, wa.DefeatEvents.ToArray());
        DeathEvent[] col = wa.Deaths.ToArray().Where(d => d.Collapsed).ToArray();
        Assert.Equal(new[] { (1, 1), (1, 3), (2, 0), (2, 2) }, col.Select(d => (d.VictimOwner, d.Victim.Index)).ToArray());
        Assert.Equal(kills, wa.Kills.ToArray());
        Assert.Equal(losses, wa.Losses.ToArray());
        for (int i = 0; i < wa.Units.Capacity; i++)
            if (wa.Units.Alive[i]) Assert.False(wa.Units.TargetIsBuilding[i] && wa.Units.Target[i].Generation != 0 && !wa.Buildings.IsAlive(wa.Units.Target[i]), $"unit {i} still chases a collapsed building");
        Assert.Equal(1, wa.Ledger.LiveBuildings[0]);
    }

    /// <summary>
    /// The last builder killed on its site by an enemy's swing (phase 11), its side's only unit: out on that tick, the site
    /// collapses in phase 11b. The builder's death is a kill and a loss; the site's collapse neither.
    /// </summary>
    [Fact]
    public void TheLastBuilderKilledOnItsSite_TheSiteCollapsesTheSameTick_OnlyTheBuilderCounts()
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(48, 64, 2));
        World w = sim.World;
        UnitStore u = w.Units;
        int house = BType(w, 1, BuildingSlot.House);
        Assert.True(w.Buildings.Spawn(1, house, 20 * 48 + 20, out EntityHandle site, site: true));
        EntityHandle worker = CombatScenes.Place(sim, 1, UType(w, 1, UnitSlot.Worker), CombatScenes.At(sim, 19, 21));
        u.Hp[worker.Index] = 1;
        sim.Enqueue(Command.Build(1, worker, house, w.NavGrid.CellCenter(20, 20)));
        for (int t = 0; t < 40 && w.Buildings.Work[site.Index] == 0; t++) sim.Tick();
        Assert.True(w.Buildings.Work[site.Index] > 0, "precondition: the worker works the site");
        EntityHandle hi = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Line), u.Position[worker.Index] + new Vector2(-1.2f, 0f));
        Assert.True(w.Buildings.Spawn(0, BType(w, 0, BuildingSlot.TownHall), 4 * 48 + 4, out _));
        sim.Enqueue(Command.Attack(0, hi, worker, isBuilding: false));
        int[] kills = w.Kills.ToArray(), losses = w.Losses.ToArray();
        int defeatAt = -1;
        for (int t = 0; t < 200 && defeatAt < 0; t++)
        {
            sim.Tick();
            if (w.IsDefeated(1)) defeatAt = sim.TickNumber - 1;
        }
        Assert.True(defeatAt >= 0, "never out");
        DeathEvent[] deaths = w.Deaths.ToArray();
        Assert.Contains(deaths, d => !d.IsBuilding && d.Victim == worker && d.KillerOwner == 0 && !d.Collapsed);
        Assert.Contains(deaths, d => d.IsBuilding && d.Victim == site && d.Collapsed && d.KillerOwner == -1);
        Assert.False(w.Buildings.IsAlive(site));
        Assert.Equal(kills[0] + 1, w.Kills[0]);
        Assert.Equal(losses[1] + 1, w.Losses[1]);
        Assert.Equal(kills[1], w.Kills[1]);
    }

    /// <summary>
    /// A tower mid-wind-up on the defeat tick (no shot yet) never fires: no projectile of the out player appears after the
    /// tick. A shot already in flight lands, as docs/03 says (a dead archer's does).
    /// </summary>
    [Fact]
    public void ATowerMidWindup_AtTheDefeat_NeverFires()
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(48, 64, 2));
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int towerType = BType(w, 0, BuildingSlot.WatchTower);
        Assert.True(b.Spawn(0, towerType, 20 * 48 + 20, out EntityHandle tower));
        EntityHandle last = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Worker), CombatScenes.At(sim, 4, 40));
        Assert.True(b.Spawn(1, BType(w, 1, BuildingSlot.TownHall), 40 * 48 + 40, out _));
        EntityHandle soldier = CombatScenes.Place(sim, 1, UType(w, 1, UnitSlot.Line), CombatScenes.At(sim, 24, 21));
        w.Units.Hp[soldier.Index] = 100_000;
        sim.Enqueue(Command.HoldPosition(1, soldier));
        int t = 0;
        for (; t < 600 && !(b.TowerWindup[tower.Index] > 1); t++) sim.Tick();
        Assert.True(b.TowerWindup[tower.Index] > 1, "precondition: the tower winds up");
        w.Units.Free(last);
        sim.Tick();
        Assert.True(w.IsDefeated(0));
        Assert.False(b.IsAlive(tower));
        int inFlight = 0;
        for (int k = 0; k < w.Projectiles.Capacity; k++) if (w.Projectiles.Alive[k] && w.Projectiles.Owner[k] == 0) inFlight++;
        for (int n = 0; n < 200; n++)
        {
            sim.Tick();
            int now = 0;
            for (int k = 0; k < w.Projectiles.Capacity; k++) if (w.Projectiles.Alive[k] && w.Projectiles.Owner[k] == 0) now++;
            Assert.True(now <= inFlight, $"tick {sim.TickNumber - 1}: player 0, out, has {now} shots in flight (was {inFlight})");
            inFlight = now;
        }
        Assert.Equal(0, inFlight);
    }

    /// <summary>
    /// A House of player 1 surrounded by player 0's infantry (claims held round slot 0). Player 1 goes out: the House
    /// collapses, every chaser stands down. Player 2 then places a House in the same slot (generation moved on) and player 0's
    /// infantry are ordered onto it: the stale claims hold nothing, so they spread over the spots (at least 12 of 16 strike).
    /// </summary>
    [Fact]
    public void ACollapsedBuildingsSlot_ReusedByARival_StaleClaimsHoldNothing()
    {
        Simulation sim = TestSim.Explored(CombatScenes.Flat(48, 64, 3));
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle last1 = CombatScenes.Place(sim, 1, UType(w, 1, UnitSlot.Worker), CombatScenes.At(sim, 44, 4));
        CombatScenes.Place(sim, 2, UType(w, 2, UnitSlot.Worker), CombatScenes.At(sim, 44, 44));
        Assert.True(w.Buildings.Spawn(1, BType(w, 1, BuildingSlot.House), 20 * 48 + 20, out EntityHandle house1));
        Assert.Equal(0, house1.Index);
        Assert.True(w.Buildings.Spawn(0, BType(w, 0, BuildingSlot.TownHall), 4 * 48 + 4, out _));
        CombatScenes.Spotter(sim, 0, CombatScenes.At(sim, 26, 21)); // player 0 sees the House (and the rival's later), so the Attacks apply
        var army = new List<EntityHandle>();
        for (int k = 0; k < 16; k++)
        {
            EntityHandle hi = CombatScenes.Place(sim, 0, UType(w, 0, UnitSlot.Line), CombatScenes.At(sim, 12 + k % 4, 12 + k / 4));
            u.Hp[hi.Index] = 100_000;
            army.Add(hi);
            sim.Enqueue(Command.Attack(0, hi, house1, isBuilding: true));
        }
        w.Buildings.SetHp(house1.Index, 1_000_000);
        for (int t = 0; t < 200; t++) sim.Tick();
        // Player 0's layer, slot 0: the holder entries the collapse leaves behind (nothing clears them).
        int held = 0;
        for (int s = 0; s < w.SurroundSpots; s++) if (w.SurroundHolder[s] != 0) held++;
        int striking = army.Count(h => u.State[h.Index] == UnitState.Attacking);
        _out.WriteLine($"before the collapse: {striking} / 16 striking, {held} holder entries round slot 0");
        Assert.True(held >= 8 && striking >= 10, $"precondition: {held} holder entries, {striking} striking");
        u.Free(last1);
        sim.Tick();
        Assert.True(w.IsDefeated(1));
        Assert.False(w.Buildings.IsAlive(house1));
        Assert.All(army, h => Assert.True(u.Target[h.Index].Generation == 0, "a chaser kept the collapsed House"));
        Assert.True(w.Buildings.Spawn(2, BType(w, 2, BuildingSlot.House), 20 * 48 + 20, out EntityHandle house2));
        Assert.Equal(0, house2.Index);
        w.Buildings.SetHp(house2.Index, 1_000_000);
        foreach (EntityHandle h in army) sim.Enqueue(Command.Attack(0, h, house2, isBuilding: true));
        var struck = new bool[army.Count];
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            for (int k = 0; k < army.Count; k++)
                if (u.State[army[k].Index] == UnitState.Attacking && u.Target[army[k].Index] == house2) struck[k] = true;
        }
        _out.WriteLine($"rival's House in the collapsed slot: {struck.Count(x => x)} / {army.Count} struck");
        Assert.True(struck.Count(x => x) >= 12, $"{struck.Count(x => x)} / {army.Count} struck");
    }
}
