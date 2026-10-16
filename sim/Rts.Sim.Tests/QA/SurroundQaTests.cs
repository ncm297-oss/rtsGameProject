using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, pathing slice 1 (M5-P1, session 2026-10-15-2325, "surround a building", <see cref="SurroundRule"/>): attacks on the
/// claim rule, independent of the developer's <c>Combat/SurroundTests</c>. Every scene checks, on every tick, the claim
/// invariants (no spot held by two live claimers of its building; no two claimers of one owner walking to one point;
/// positions finite and on the map) and, where stated, the order invariant (an ordered attacker on a standing building
/// never drops its order). Most scenes top the building's hit points up between ticks (a scene about standing room, not
/// damage) so a balance change can't end them early; rows that compare with the rule off use the
/// <see cref="World.SurroundRuleOn"/> seam.
/// </summary>
[Collection(SerialCollection.Name)]
public class SurroundQaTests
{
    private const int MapSize = 64;
    private readonly ITestOutputHelper _out;

    public SurroundQaTests(ITestOutputHelper output) => _out = output;

    // ---------- helpers ----------

    private static int BuildingOf(GameData data, int faction, BuildingSlot slot)
    {
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        throw new InvalidOperationException($"no {slot} for faction {faction}");
    }

    private static int EnemyBuilding(GameData data, int attackerType, BuildingSlot slot) => BuildingOf(data, 1 - data.Units[attackerType].Faction, slot);

    private static Simulation NewSim(ulong seed, int players, int units, Heightmap? map = null, GameData? data = null)
    {
        SimConfig c = TestSim.Config(seed, players, units, 16 * units + 64);
        if (data != null) c = c with { Data = data };
        return new Simulation(c, map ?? LocalMovementTests.Flat(MapSize));
    }

    /// <summary>The spot key unit <paramref name="i"/> really holds (named back by the holder array), or -1.</summary>
    private static int HeldSpot(World w, int i)
    {
        int c = w.SurroundClaim[i];
        return c >= 0 && w.SurroundHolder[c] == i + 1 ? c : -1;
    }

    /// <summary>Edge gap (m) from unit <paramref name="i"/> to building slot <paramref name="b"/>'s footprint.</summary>
    private static float GapTo(World w, int i, int b)
    {
        CombatSystem.BuildingRect(w, b, out Vector2 min, out Vector2 max);
        Vector2 p = w.Units.Position[i];
        return Vector2.Distance(p, Vector2.Clamp(p, min, max)) - w.Units.Radius[i];
    }

    private static void TopUp(World w, EntityHandle b)
    {
        if (w.Buildings.IsAlive(b)) w.Buildings.SetHp(b.Index, w.Data.Buildings[w.Buildings.TypeId[b.Index]].Hp);
    }

    /// <summary>
    /// The claim invariants after a tick: no spot key held by two live units chasing the building it belongs to; no two
    /// live claimers of one owner walking to one goal point; positions finite and on the map.
    /// </summary>
    private static void AssertClaims(World w, string where)
    {
        UnitStore u = w.Units;
        var seen = new int[w.SurroundHolder.Length];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{where}: unit {i} at {p}");
            Assert.True(w.NavGrid.WorldToCell(p, out _, out _), $"{where}: unit {i} off the map at {p}");
            if (!u.TargetIsBuilding[i] || !w.Buildings.IsAlive(u.Target[i])) continue;
            int key = HeldSpot(w, i);
            if (key < 0 || key / w.SurroundSpots != u.Target[i].Index) continue;
            Assert.True(seen[key] == 0, $"{where}: units {seen[key] - 1} and {i} both hold spot {key}");
            seen[key] = i + 1;
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.State[i] != UnitState.Moving || HeldSpot(w, i) < 0 || !u.TargetIsBuilding[i]) continue;
            for (int j = i + 1; j < u.Capacity; j++)
                if (u.Alive[j] && u.State[j] == UnitState.Moving && HeldSpot(w, j) >= 0 && u.TargetIsBuilding[j]
                    && u.Owner[j] == u.Owner[i] && u.Goal[j] == u.Goal[i])
                    Assert.Fail($"{where}: units {i} and {j} both walk to {u.Goal[i]}");
        }
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks with the claim checks (and, with <paramref name="assertOrders"/>, the order check
    /// from the second tick, when the orders have applied); returns how many of <paramref name="attackers"/> started a
    /// swing, the longest run of ticks any attacker spent walking in reach of the building without a swing, the most
    /// striking at once and the number striking at the end.
    /// </summary>
    private static int Run(Simulation sim, EntityHandle[] attackers, EntityHandle building, int ticks, bool topUp, bool assertOrders,
        out int longestInReachWalk, out int maxStriking, out int lastStriking, string where)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        var struck = new bool[attackers.Length];
        var before = new int[attackers.Length];
        var walkRun = new int[attackers.Length];
        longestInReachWalk = 0;
        maxStriking = 0;
        lastStriking = 0;
        for (int t = 0; t < ticks; t++)
        {
            for (int k = 0; k < attackers.Length; k++) before[k] = u.IsAlive(attackers[k]) ? u.CooldownTicks[attackers[k].Index] : 0;
            if (topUp) TopUp(w, building);
            sim.Tick();
            AssertClaims(w, $"{where} tick {sim.TickNumber}");
            int striking = 0;
            for (int k = 0; k < attackers.Length; k++)
            {
                EntityHandle h = attackers[k];
                if (!u.IsAlive(h)) continue;
                int i = h.Index;
                if (u.CooldownTicks[i] > before[k]) struck[k] = true;
                if (u.State[i] == UnitState.Attacking) striking++;
                if (!w.Buildings.IsAlive(building)) continue;
                if (assertOrders && t >= 2) // the orders apply on the second tick
                    Assert.True(u.Target[i] == building && u.TargetIsBuilding[i] && u.Mode[i] == CombatMode.Ordered,
                        $"{where} tick {sim.TickNumber}: unit {i} dropped its order ({u.State[i]}, mode {u.Mode[i]}, give-ups {u.GiveUps[i]})");
                float range = w.Data.Units[u.TypeId[i]].Attack.Range;
                bool walkingInReach = u.State[i] == UnitState.Moving && u.Target[i] == building && GapTo(w, i, building.Index) <= range
                    && u.CooldownTicks[i] <= before[k] && u.WindupTicks[i] == 0;
                walkRun[k] = walkingInReach ? walkRun[k] + 1 : 0;
                longestInReachWalk = Math.Max(longestInReachWalk, walkRun[k]);
            }
            maxStriking = Math.Max(maxStriking, striking);
            lastStriking = striking;
        }
        return struck.Count(s => s);
    }

    /// <summary>A block of <paramref name="types"/>.Length attackers of <paramref name="owner"/>, <paramref name="width"/> wide, ranks going west from <paramref name="front"/>, each ordered to Attack <paramref name="building"/>.</summary>
    private static EntityHandle[] Block(Simulation sim, int[] types, Vector2 front, EntityHandle building, float spacing = 1.8f, int width = 5, int owner = 0)
    {
        var a = new EntityHandle[types.Length];
        for (int k = 0; k < types.Length; k++)
        {
            int rank = k / width, file = k % width;
            int across = Math.Min(width, types.Length - rank * width);
            a[k] = Place(sim, owner, types[k], front + new Vector2(-rank * spacing, (file - (across - 1) / 2f) * spacing));
            sim.Enqueue(Command.Attack(owner, a[k], building, isBuilding: true));
        }
        return a;
    }

    private static int[] Many(int type, int n)
    {
        var t = new int[n];
        Array.Fill(t, type);
        return t;
    }

    // ---------- same tick, same spot ----------

    // Two attackers equidistant from one spot (mirror images about it) resolve on one tick: they take two spots, both strike.
    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.45f)]
    public void TwoAttackersEquidistantFromOneSpot_TakeTwoSpots_BothStrike(float offset)
    {
        Simulation sim = NewSim(1, 2, 16);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 38, 30));
        CombatSystem.BuildingRect(w, b.Index, out Vector2 min, out _);
        // West face spot 1 (y = min.Y + 1.5): both units 6 m west of it, +/- offset along y.
        Vector2 spot = new(min.X - 0.65f, min.Y + 1.5f);
        EntityHandle a1 = Place(sim, 0, hi, spot + new Vector2(-6f, offset));
        EntityHandle a2 = Place(sim, 0, hi, spot + new Vector2(-6f, -offset));
        sim.Enqueue(Command.Attack(0, a1, b, isBuilding: true));
        sim.Enqueue(Command.Attack(0, a2, b, isBuilding: true));
        int struck = Run(sim, new[] { a1, a2 }, b, 120, true, true, out int walk, out _, out _, "equidistant");
        _out.WriteLine($"offset {offset}: {struck} / 2 struck; spots {HeldSpot(w, a1.Index)} and {HeldSpot(w, a2.Index)}; longest in-reach walk {walk}");
        Assert.Equal(2, struck);
        Assert.NotEqual(HeldSpot(w, a1.Index), HeldSpot(w, a2.Index));
    }

    // ---------- the owner of a claim dies (dev's (c)), retreats, is shoved ----------

    // A striker ordered away (a retreat): its spot is free at once and a waiter strikes from it within 40 ticks.
    [Fact]
    public void AStrikerOrderedAway_FreesItsSpot_AWaiterTakesItWithin40Ticks()
    {
        Simulation sim = NewSim(7, 2, 64);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 39, 30));
        EntityHandle[] a = Block(sim, Many(hi, 28), At(sim, 28, 31), b);
        Run(sim, a, b, 200, true, true, out _, out _, out _, "retreat warm-up");
        int victim = -1;
        foreach (EntityHandle h in a)
            if (u.State[h.Index] == UnitState.Attacking && HeldSpot(w, h.Index) >= 0) { victim = h.Index; break; }
        Assert.True(victim >= 0, "no striker holds a spot");
        int freed = HeldSpot(w, victim);
        var waiting = new bool[u.Capacity];
        foreach (EntityHandle h in a) waiting[h.Index] = HeldSpot(w, h.Index) < 0;
        Assert.Contains(true, waiting);
        sim.Enqueue(Command.Move(0, new EntityHandle(victim, u.Generation[victim]), At(sim, 10, 10)));
        int taken = -1;
        for (int t = 1; t <= 40 && taken < 0; t++)
        {
            TopUp(w, b);
            sim.Tick();
            AssertClaims(w, $"retreat tick {t}");
            int h = w.SurroundHolder[freed] - 1;
            if (h >= 0 && h != victim && u.Alive[h] && HeldSpot(w, h) == freed && u.State[h] == UnitState.Attacking && waiting[h]) taken = t;
        }
        _out.WriteLine($"spot {freed} left by a retreat: a waiter strikes from it after {taken} ticks");
        Assert.True(taken > 0, "the spot left by the retreating striker was not taken by a waiter within 40 ticks");
    }

    // Three own Laborers walk through the west face's strikers along the footprint (a shove): the line recovers.
    [Fact]
    public void AFriendWalkingThroughTheStrikers_TheLineRecoversWithin60Ticks()
    {
        Simulation sim = NewSim(7, 2, 64);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 39, 30));
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 28, 31), b);
        Run(sim, a, b, 240, true, true, out _, out int maxBefore, out int before, "shove warm-up");
        CombatSystem.BuildingRect(w, b.Index, out Vector2 min, out Vector2 max);
        for (int k = 0; k < 3; k++)
        {
            EntityHandle walker = Place(sim, 0, Laborer, new Vector2(min.X - 0.6f, max.Y + 4f + k));
            sim.Enqueue(Command.Move(0, walker, new Vector2(min.X - 0.6f, min.Y - 6f)));
        }
        Run(sim, a, b, 60, true, true, out _, out _, out int during, "shove pass");
        int struck = Run(sim, a, b, 60, true, true, out int walk, out _, out int after, "shove after");
        _out.WriteLine($"striking: before {before} (max {maxBefore}), 60 ticks into the pass {during}, 60 ticks later {after}; {struck} struck in the last 60; longest in-reach walk {walk}");
        Assert.True(after >= before - 1, $"the line did not recover: {after} striking, {before} before the shove");
    }

    // ---------- map edges, corners and cliffs ----------

    // A House in the map's corner (two faces on the blocked outer ring): the open faces fill, nobody gives up.
    [Fact]
    public void AHouseInTheMapCorner_OpenFacesFill_NobodyGivesUp()
    {
        Simulation sim = NewSim(3, 2, 64);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 1 * MapSize + 1, out EntityHandle b));
        Spotter(sim, 0, At(sim, 6, 6));
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 14, 10), b);
        int struck = Run(sim, a, b, 400, true, true, out int walk, out int max, out int last, "corner");
        _out.WriteLine($"corner House: {struck} / 20 struck in 400 ticks, at most {max} striking at once, {last} at the end; longest in-reach walk {walk}");
        // The ring of a corner House keeps its east and north faces and the NE corner: 4 + 4 + 1 spots.
        Assert.True(max >= 8, $"only {max} striking at once");
    }

    // A House against a cliff on its east (the plateau's rim cells are blocked): the other faces fill, nobody gives up.
    [Fact]
    public void AHouseAgainstACliff_OtherFacesFill_NobodyGivesUp()
    {
        var rows = new string[MapSize];
        for (int y = 0; y < MapSize; y++) rows[y] = new string('0', 36) + new string('1', MapSize - 36);
        Simulation sim = NewSim(3, 2, 64, LocalMovementTests.Rows(rows));
        World w = sim.World;
        TestSim.KeepIn(sim, 1, nearCorner: true);
        int hi = HeavyInfantry;
        Assert.False(w.NavGrid.IsPassable(36, 30), "the rim cell east of the House should be a cliff");
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 34, 36));
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 28, 31), b);
        int struck = Run(sim, a, b, 400, true, true, out int walk, out int max, out int last, "cliff");
        _out.WriteLine($"House on a cliff: {struck} / 20 struck in 400 ticks, at most {max} striking at once, {last} at the end; longest in-reach walk {walk}");
        // West, north and south faces and the SW and NW corners: 14 spots.
        Assert.True(max >= 12, $"only {max} striking at once");
    }

    // A sealed pocket on the attackers' side (the west face's cells walled in by the attackers' own Houses, store seam):
    // its spots are open ground no walk reaches. Measured (BUG-0732): the rule drops fewer orders than without it.
    [Fact]
    public void ASealedPocketOnTheNearFace_Measured()
    {
        var struckBy = new int[2];
        var drops = new int[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(31, 2, 64);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            UnitStore u = w.Units;
            TestSim.KeepIn(sim, 0, nearCorner: true);
            int hi = HeavyInfantry;
            int wall = BuildingOf(w.Data, w.Data.Units[hi].Faction, BuildingSlot.House);
            foreach ((int x, int y) in new[] { (31, 30), (32, 28), (32, 32) })
                Assert.True(w.Buildings.Spawn(0, wall, y * MapSize + x, out _), $"wall at ({x}, {y})");
            TestSim.KeepIn(sim, 1);
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
            Assert.True(w.NavGrid.IsPassable(33, 30) && w.NavGrid.IsPassable(33, 31), "the pocket should be open ground");
            Spotter(sim, 0, At(sim, 39, 30));
            EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 25, 31), b);
            struckBy[r] = Run(sim, a, b, 600, true, false, out int walk, out int max, out int last, $"pocket rule {r}");
            foreach (EntityHandle h in a) if (!(u.Target[h.Index] == b && u.Mode[h.Index] == CombatMode.Ordered)) drops[r]++;
            _out.WriteLine($"pocket, rule {(r == 1 ? "on" : "off")}: {struckBy[r]} / 20 struck in 600 ticks, at most {max} at once, {last} at the end; {drops[r]} dropped the order; longest in-reach walk {walk}");
        }
        Assert.True(drops[1] <= drops[0], $"the rule dropped more orders: {drops[1]} with, {drops[0]} without");
        Assert.True(struckBy[1] >= struckBy[0], $"the rule struck fewer: {struckBy[1]} with, {struckBy[0]} without");
    }

    // ---------- footprints: 1x1 (test data) and 4x4 ----------

    private static GameData OneByOneHouses()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach (string faction in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{faction}/buildings.json", root =>
            {
                foreach (JsonNode? bn in root["buildings"]!.AsArray())
                    if ((string?)bn!["slot"] == "house") bn["footprint"] = new JsonObject { ["width"] = 1, ["height"] = 1 };
            });
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }

    // A 1x1 footprint has 12 spots: they fill, the rest wait without a give-up.
    [Fact]
    public void AOneByOneBuilding_TwelveSpots_TheRestWait()
    {
        GameData data = OneByOneHouses();
        Simulation sim = NewSim(5, 2, 64, data: data);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = data.FindUnit("malazan_heavy_infantry");
        int house = EnemyBuilding(data, hi, BuildingSlot.House);
        Assert.Equal(1, data.Buildings[house].FootprintWidth);
        Assert.True(w.Buildings.Spawn(1, house, 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 38, 30));
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 28, 30), b);
        int struck = Run(sim, a, b, 400, true, true, out int walk, out int max, out int last, "1x1");
        _out.WriteLine($"1x1 House: {struck} / 20 struck in 400 ticks, at most {max} striking at once, {last} at the end; longest in-reach walk {walk}");
        Assert.True(max >= 8, $"only {max} striking at once round a 1x1");
    }

    // A 4x4 Town Hall, 40 Heavy Infantry from two sides, rule off and on: reported; the rule must not do worse.
    [Fact]
    public void AFourByFourTownHall_FortyFromTwoSides_Report()
    {
        var struck = new int[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(5, 2, 96);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            TestSim.KeepIn(sim, 1);
            int hi = HeavyInfantry;
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.TownHall), 30 * MapSize + 34, out EntityHandle b));
            Spotter(sim, 0, At(sim, 40, 40));
            EntityHandle[] west = Block(sim, Many(hi, 20), At(sim, 28, 32), b);
            EntityHandle[] south = Block(sim, Many(hi, 20), At(sim, 36, 22), b);
            EntityHandle[] all = west.Concat(south).ToArray();
            struck[r] = Run(sim, all, b, 200, true, r == 1, out int walk, out int max, out _, $"4x4 rule {r}");
            _out.WriteLine($"4x4 Town Hall, rule {(r == 1 ? "on" : "off")}: {struck[r]} / 40 struck within 200 ticks, at most {max} at once; longest in-reach walk {walk}");
        }
        Assert.True(struck[1] >= struck[0], $"the rule struck fewer: {struck[1]} with, {struck[0]} without");
    }

    // ---------- mixed radii ----------

    // Lancers / Horse Raiders (0.7 m) among line infantry (0.4 m), and Battering Rams (0.9 m): with the rule nobody gives
    // up, nobody walks in reach for over 5 s without a swing, and it does no worse than without it.
    [Theory]
    [InlineData("malazan_wickan_lancer", 5, "malazan_heavy_infantry", 15)]
    [InlineData("whirlwind_horse_raider", 10, "whirlwind_raider", 10)]
    [InlineData("whirlwind_battering_ram", 10, "whirlwind_raider", 0)]
    [InlineData("malazan_wickan_lancer", 20, "malazan_heavy_infantry", 0)]
    public void MixedRadii_NoLivelock_NoGiveUp(string big, int nBig, string small, int nSmall)
    {
        var counts = new int[2];
        var walks = new int[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(9, 2, 64);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            TestSim.KeepIn(sim, 1);
            int tb = w.Data.FindUnit(big), ts = w.Data.FindUnit(small);
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, tb, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
            Spotter(sim, 0, At(sim, 39, 30));
            // A deterministic mix: the big ones spread through the block.
            var types = new int[nBig + nSmall];
            int placedBig = 0;
            for (int k = 0; k < types.Length; k++)
            {
                bool wantBig = placedBig < nBig && (nSmall == 0 || k % ((types.Length + nBig - 1) / nBig) == 0 || types.Length - k <= nBig - placedBig);
                types[k] = wantBig ? tb : ts;
                if (wantBig) placedBig++;
            }
            EntityHandle[] a = Block(sim, types, At(sim, 27, 31), b, spacing: 2.2f);
            counts[r] = Run(sim, a, b, 400, true, r == 1, out walks[r], out int max, out int last, $"mixed {big} rule {r}");
            _out.WriteLine($"{nBig} {big} + {nSmall} {small}, rule {(r == 1 ? "on" : "off")}: {counts[r]} / {a.Length} struck in 400 ticks, at most {max} at once, {last} at the end; longest in-reach walk without a swing {walks[r]} ticks");
        }
        Assert.True(walks[1] <= 100, $"an attacker walked in reach without a swing for {walks[1]} ticks");
        Assert.True(counts[1] >= counts[0] - 1, $"the rule struck fewer: {counts[1]} with, {counts[0]} without");
    }

    // ---------- more attackers than spots ----------

    // 60 Heavy Infantry on one House (20 spots) for 1,000 ticks: with the rule nobody gives up, the ring stays full, and
    // the waiters re-walk no more than without the rule (no livelock of re-picks).
    [Fact]
    public void SixtyOnOneHouse_NobodyGivesUp_NoLivelock()
    {
        var changes = new int[2];
        var walked = new float[2];
        var minStriking = new int[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(11, 2, 96);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            UnitStore u = w.Units;
            TestSim.KeepIn(sim, 1);
            int hi = HeavyInfantry;
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
            Spotter(sim, 0, At(sim, 40, 30));
            EntityHandle[] a = Block(sim, Many(hi, 60), At(sim, 26, 31), b, width: 10);
            int struck = Run(sim, a, b, 400, true, r == 1, out int walk1, out int max1, out int last1, $"sixty warm-up rule {r}");
            var goal = new Vector2[a.Length];
            var prev = new Vector2[a.Length];
            for (int k = 0; k < a.Length; k++)
            {
                goal[k] = u.Goal[a[k].Index];
                prev[k] = u.Position[a[k].Index];
            }
            minStriking[r] = int.MaxValue;
            int dropped = 0;
            for (int t = 0; t < 600; t++)
            {
                TopUp(w, b);
                sim.Tick();
                AssertClaims(w, $"sixty tick {sim.TickNumber}");
                int striking = 0;
                for (int k = 0; k < a.Length; k++)
                {
                    int i = a[k].Index;
                    if (!(u.Target[i] == b && u.Mode[i] == CombatMode.Ordered))
                    {
                        dropped++;
                        continue;
                    }
                    if (u.Goal[i] != goal[k])
                    {
                        changes[r]++;
                        goal[k] = u.Goal[i];
                    }
                    if (u.State[i] == UnitState.Attacking) striking++;
                    else walked[r] += Vector2.Distance(prev[k], u.Position[i]);
                    prev[k] = u.Position[i];
                }
                minStriking[r] = Math.Min(minStriking[r], striking);
            }
            _out.WriteLine($"60 on a House, rule {(r == 1 ? "on" : "off")}: {struck} struck in the first 400 ticks (max {max1}, {last1} at 400); ticks 400-1000: min striking {minStriking[r]}, goal changes {changes[r]} ({changes[r] / 600f:F2} a tick), waiters walked {walked[r]:F0} m in all, {dropped} unit-ticks without the order; longest in-reach walk {walk1}");
            if (r == 1) Assert.Equal(0, dropped);
        }
        Assert.True(minStriking[1] >= 16, $"the ring emptied to {minStriking[1]}");
        Assert.True(changes[1] <= changes[0] * 3 / 2 + 60, $"the waiters re-walk far more with the rule: {changes[1]} goal changes, {changes[0]} without");
    }

    // ---------- ghost / remembered building ----------

    // An Attack on a building out of sight (remembered: seen, then the spotter gone): they walk, see it, surround it.
    [Fact]
    public void ARememberedBuilding_TheyWalkSeeItAndSurroundIt()
    {
        Simulation sim = NewSim(13, 2, 64);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.InfantryHall), 30 * MapSize + 44, out EntityHandle b));
        EntityHandle spotter = Spotter(sim, 0, At(sim, 46, 36));
        sim.Tick();
        u.Free(spotter);
        sim.Tick();
        sim.Tick();
        Assert.True(w.Fog.HasGhost(0, b), "player 0 should remember the hall");
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 14, 31), b);
        int struck = Run(sim, a, b, 600, true, true, out int walk, out int max, out int last, "ghost");
        _out.WriteLine($"remembered hall: {struck} / 20 struck in 600 ticks, at most {max} at once, {last} at the end; longest in-reach walk {walk}");
        Assert.True(struck >= 16, $"{struck} / 20 struck");
    }

    // The House falls (store seam) after its ring filled, then a new one stands in the same building slot: no stale claim
    // of the old one keeps anyone off the new one's spots.
    [Fact]
    public void ABuildingRebuiltInTheSameSlot_StaleClaimsHoldNothing()
    {
        Simulation sim = NewSim(13, 2, 64);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        int house = EnemyBuilding(w.Data, hi, BuildingSlot.House);
        Assert.True(w.Buildings.Spawn(1, house, 30 * MapSize + 34, out EntityHandle b1));
        Spotter(sim, 0, At(sim, 39, 30));
        EntityHandle[] a = Block(sim, Many(hi, 20), At(sim, 28, 31), b1);
        Run(sim, a, b1, 200, true, true, out _, out _, out _, "rebuild first");
        Assert.True(w.Buildings.Free(b1));
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.True(w.Buildings.Spawn(1, house, 30 * MapSize + 34, out EntityHandle b2));
        Assert.True(b1.Index == b2.Index, $"the rebuilt House took slot {b2.Index}, not {b1.Index}: the scene doesn't test slot reuse");
        foreach (EntityHandle h in a) sim.Enqueue(Command.Attack(0, h, b2, isBuilding: true));
        int struck = Run(sim, a, b2, 200, true, true, out int walk, out int max, out _, "rebuild second");
        _out.WriteLine($"rebuilt in slot {b2.Index}: {struck} / 20 struck within 200 ticks, at most {max} at once; longest in-reach walk {walk}");
        Assert.True(struck >= 16, $"{struck} / 20 struck on the rebuilt House");
    }

    // ---------- attack-move (the AI's waves) ----------

    // 20 Heavy Infantry attack-move past a lone House (their scans pick it): the rule spreads them as for an ordered
    // Attack. Walled on three sides (an attack-move ending at the House) the waiters keep on: no give-up. Measured against
    // the rule off; the open-ground row's remaining give-ups are BUG-0731.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttackMove_SurroundsTheHouse_OneOpenSideNoGiveUp(bool walled)
    {
        var struck = new int[2];
        var giveUps = new int[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(37, 2, 64);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            UnitStore u = w.Units;
            int hi = HeavyInfantry;
            if (walled)
            {
                TestSim.KeepIn(sim, 0, nearCorner: true);
                int wall = BuildingOf(w.Data, w.Data.Units[hi].Faction, BuildingSlot.House);
                foreach ((int x, int y) in new[] { (32, 30), (32, 32), (34, 32), (36, 32), (36, 30) })
                    Assert.True(w.Buildings.Spawn(0, wall, y * MapSize + x, out _), $"wall at ({x}, {y})");
            }
            TestSim.KeepIn(sim, 1);
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
            Spotter(sim, 0, At(sim, 35, 27));
            var a = new EntityHandle[20];
            for (int k = 0; k < 20; k++)
            {
                a[k] = Place(sim, 0, hi, At(sim, 33 + k % 5, 22 - k / 5));
                sim.Enqueue(Command.AttackMove(0, a[k], At(sim, 35, walled ? 31 : 40)));
            }
            var before = new int[20];
            var hit = new bool[20];
            var gu = new int[20];
            for (int t = 0; t < 400; t++)
            {
                for (int k = 0; k < 20; k++) before[k] = u.CooldownTicks[a[k].Index];
                TopUp(w, b);
                sim.Tick();
                AssertClaims(w, $"attack-move rule {r} tick {t}");
                for (int k = 0; k < 20; k++)
                {
                    int i = a[k].Index;
                    if (u.CooldownTicks[i] > before[k]) hit[k] = true;
                    if (u.GiveUps[i] > gu[k])
                    {
                        gu[k] = u.GiveUps[i];
                        _out.WriteLine($"  rule {r} tick {sim.TickNumber}: unit {i} gave up (state {u.State[i]}, mode {u.Mode[i]}, gap {GapTo(w, i, b.Index):F2}, held spot {HeldSpot(w, i)}, at {u.Position[i]})");
                    }
                }
            }
            foreach (EntityHandle h in a) giveUps[r] += u.GiveUps[h.Index];
            struck[r] = hit.Count(x => x);
            _out.WriteLine($"attack-move, walled {walled}, rule {(r == 1 ? "on" : "off")}: {struck[r]} / 20 struck in 400 ticks; give-ups {giveUps[r]}");
        }
        Assert.True(struck[1] >= struck[0], $"the rule struck fewer: {struck[1]} with, {struck[0]} without");
        Assert.True(giveUps[1] <= giveUps[0], $"the rule gave up more: {giveUps[1]} with, {giveUps[0]} without");
        if (walled) Assert.Equal(0, giveUps[1]);
    }

    // ---------- ranged attackers and other owners ----------

    // Crossbowmen among the infantry never hold a spot (the rule is for melee reach).
    [Fact]
    public void RangedAttackers_NeverHoldASpot()
    {
        Simulation sim = NewSim(17, 2, 64);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry, xb = Crossbowman;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 39, 30));
        var types = new int[20];
        for (int k = 0; k < 20; k++) types[k] = k % 2 == 0 ? hi : xb;
        EntityHandle[] a = Block(sim, types, At(sim, 28, 31), b);
        int claims = 0;
        for (int t = 0; t < 300; t++)
        {
            TopUp(w, b);
            sim.Tick();
            AssertClaims(w, $"ranged tick {t}");
            foreach (EntityHandle h in a)
            {
                if (u.TypeId[h.Index] == xb) Assert.True(HeldSpot(w, h.Index) < 0, $"tick {t}: crossbowman {h.Index} holds spot {HeldSpot(w, h.Index)}");
                else if (HeldSpot(w, h.Index) >= 0) claims++;
            }
        }
        Assert.True(claims > 100, $"the infantry held only {claims} claim-ticks: the scene didn't exercise the rule");
    }

    // Two enemies of player 1 raze one House (three seats): both sides strike and keep their orders. Reported: another
    // owner's claims count as held for this owner (SurroundRule.HeldByOther doesn't check the owner).
    [Fact]
    public void TwoOwnersOnOneHouse_BothStrike()
    {
        Simulation sim = NewSim(19, 3, 64);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Spotter(sim, 0, At(sim, 39, 30));
        Spotter(sim, 2, At(sim, 39, 32));
        int t2 = w.FactionOf(2) == w.Data.Units[hi].Faction ? hi : Raider;
        EntityHandle[] a0 = Block(sim, Many(hi, 12), At(sim, 28, 31), b);
        var a2 = new EntityHandle[12];
        for (int k = 0; k < 12; k++)
        {
            a2[k] = Place(sim, 2, t2, At(sim, 44 + k / 4, 29 + k % 4));
            sim.Enqueue(Command.Attack(2, a2[k], b, isBuilding: true));
        }
        EntityHandle[] all = a0.Concat(a2).ToArray();
        var struck = new bool[all.Length];
        var before = new int[all.Length];
        for (int t = 0; t < 300; t++)
        {
            for (int k = 0; k < all.Length; k++) before[k] = u.CooldownTicks[all[k].Index];
            TopUp(w, b);
            sim.Tick();
            AssertClaims(w, $"two owners tick {t}");
            for (int k = 0; k < all.Length; k++)
            {
                if (u.CooldownTicks[all[k].Index] > before[k]) struck[k] = true;
                if (t >= 2)
                    Assert.True(u.Target[all[k].Index] == b && u.Mode[all[k].Index] == CombatMode.Ordered, $"tick {t}: unit {all[k].Index} dropped its order");
            }
        }
        int s0 = struck.Take(12).Count(x => x), s2 = struck.Skip(12).Count(x => x);
        _out.WriteLine($"two owners on one House: player 0 {s0} / 12 struck, player 2 {s2} / 12 struck");
        Assert.True(s0 >= 6 && s2 >= 6, $"player 0 {s0}, player 2 {s2}");
    }

    // ---------- determinism ----------

    /// <summary>
    /// <paramref name="n"/> attackers (half Heavy Infantry from the west, half Raiders from the south) at an infantry hall,
    /// a House behind it queued as their next target.
    /// </summary>
    private static Simulation Twin(int n, out EntityHandle hall)
    {
        Simulation sim = NewSim(23, 2, n + 16);
        World w = sim.World;
        TestSim.KeepIn(sim, 1);
        int hi = HeavyInfantry, ra = Raider;
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.InfantryHall), 30 * MapSize + 34, out hall));
        Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.House), 30 * MapSize + 40, out EntityHandle house));
        Spotter(sim, 0, At(sim, 38, 36));
        EntityHandle[] west = Block(sim, Many(hi, n / 2), At(sim, 28, 31), hall);
        foreach (EntityHandle h in west) sim.Enqueue(Command.Attack(0, h, house, isBuilding: true, queued: true));
        for (int k = 0; k < n / 2; k++)
        {
            EntityHandle h = Place(sim, 0, ra, At(sim, 34 + k % 5, 24 - k / 5));
            sim.Enqueue(Command.Attack(0, h, hall, isBuilding: true));
            sim.Enqueue(Command.Attack(0, h, house, isBuilding: true, queued: true));
        }
        return sim;
    }

    // The same scene twice hashes equal on every tick for 2,000 ticks: 10 and 40 attackers, the hall razed for real (and
    // the House after it), and 40 with the hall topped up (a standing ring for the whole run).
    [Theory]
    [InlineData(10, false)]
    [InlineData(40, false)]
    [InlineData(40, true)]
    public void HashTwins_2000Ticks(int n, bool topUp)
    {
        Simulation x = Twin(n, out EntityHandle hx);
        Simulation y = Twin(n, out EntityHandle hy);
        for (int t = 0; t < 2000; t++)
        {
            if (topUp)
            {
                TopUp(x.World, hx);
                TopUp(y.World, hy);
            }
            x.Tick();
            y.Tick();
            AssertClaims(x.World, $"twin {n} tick {t}");
            Assert.True(x.StateHash() == y.StateHash(), $"{n} attackers: hashes split at tick {x.TickNumber}");
        }
        _out.WriteLine($"{n} attackers, top-up {topUp}: equal over 2,000 ticks; hall alive {x.World.Buildings.IsAlive(hx)}");
    }

    // The rule changes the hashed walk: rule on and off split, so the twins above compare something the rule decides.
    [Fact]
    public void RuleOnAndOff_SplitTheHash()
    {
        Simulation x = Twin(40, out _);
        Simulation y = Twin(40, out _);
        y.World.SurroundRuleOn = false;
        int split = -1;
        for (int t = 0; t < 400 && split < 0; t++)
        {
            x.Tick();
            y.Tick();
            if (x.StateHash() != y.StateHash()) split = x.TickNumber;
        }
        Assert.True(split > 0, "the rule never changed the hash");
    }

    // ---------- scale ----------

    // 500 Heavy Infantry (the design's unit budget) on one Town Hall (36 spots): ticks allocate nothing, and the tick cost with the rule and without is reported (wall clock: printed, not gated).
    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredOnATownHall_AllocatesNothing_CostReported()
    {
        var ms = new double[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(29, 2, 560);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            TestSim.KeepIn(sim, 1);
            int hi = HeavyInfantry;
            Assert.True(w.Buildings.Spawn(1, EnemyBuilding(w.Data, hi, BuildingSlot.TownHall), 30 * MapSize + 30, out EntityHandle b));
            Spotter(sim, 0, At(sim, 36, 36));
            Block(sim, Many(hi, 250), At(sim, 24, 32), b, spacing: 1.0f, width: 25);
            for (int k = 0; k < 250; k++)
            {
                EntityHandle h = Place(sim, 0, hi, At(sim, 40, 32) + new Vector2(k / 25 * 1.0f, (k % 25 - 12) * 1.0f));
                sim.Enqueue(Command.Attack(0, h, b, isBuilding: true));
            }
            for (int t = 0; t < 200; t++)
            {
                TopUp(w, b);
                sim.Tick();
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int t = 0; t < 200; t++)
            {
                TopUp(w, b);
                sim.Tick();
            }
            ms[r] = watch.Elapsed.TotalMilliseconds / 200;
            if (r == 1)
            {
                BuildingStore bs = w.Buildings;
                int full = w.Data.Buildings[bs.TypeId[b.Index]].Hp;
                AllocationProbe.AssertZero(() =>
                {
                    for (int t = 0; t < 40; t++)
                    {
                        bs.SetHp(b.Index, full);
                        sim.Tick();
                    }
                }, _out);
            }
        }
        _out.WriteLine($"500 attackers on a Town Hall, mean tick over 200 ticks (after 200): rule off {ms[0]:F2} ms, rule on {ms[1]:F2} ms");
    }
}
