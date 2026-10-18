using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// QA M5-4c (session 2026-10-13-1616): the AI's <see cref="AbilityManager"/> at its targeting edges. The scene is the
/// developer's (the flat 64 map, player 0 an AI with quiet build orders, no wave, no scout, the defense radius at its 8 m
/// minimum; player 1 a human whose units are placed and spotted for player 0), so nothing but the ability manager orders the
/// casters. Every number (range, radius, cooldown) is read from <see cref="GameData"/>.
/// </summary>
public class AbilityManagerQaTests
{
    internal static GameData Data(int castMinTargets = 2, Action<TestDataDir>? more = null) => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower", 1));
                AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 100, ["minArmyCost"] = 0 };
            });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["scoutAfter"] = 3600;
            ai["defendRadius"] = 8;
            ai["castMinTargets"] = castMinTargets;
        });
        more?.Invoke(dir);
    });

    private static readonly Lazy<GameData> s_data = new(() => Data());
    private static readonly Lazy<GameData> s_min50 = new(() => Data(castMinTargets: 50));

    internal static Simulation Scene(GameData data, AiDifficulty difficulty = AiDifficulty.Normal, int faction = 0, bool combat = false, int units = 256)
    {
        PlayerSeat[] seats = { new(faction, SeatController.Ai, difficulty), new(1 - faction, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data, workers: 0, combat: combat, units: units);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        NextThink(sim);
        Assert.True(sim.AiOf(0)!.Board.RallyVersion > 0);
        return sim;
    }

    internal static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    internal static AbilityDef Ability(GameData d, string key)
    {
        for (int a = 0; a < d.Abilities.Length; a++)
            if (d.Abilities[a].Key == key) return d.Abilities[a];
        throw new InvalidOperationException(key);
    }

    private static EntityHandle Caster(Simulation sim, string type, Vector2 at) =>
        CombatScenes.Place(sim, 0, sim.World.Data.FindUnit(type), at);

    private static EntityHandle Enemy(Simulation sim, string type, Vector2 at)
    {
        EntityHandle h = CombatScenes.Place(sim, 1, sim.World.Data.FindUnit(type), at);
        CombatScenes.Spot(sim, 0, h);
        return h;
    }

    private static List<Command> Casts(Simulation sim)
    {
        var list = new List<Command>();
        sim.AiOf(0)!.SentForTests = c => { if (c.Kind == CommandKind.UseAbility) list.Add(c); };
        return list;
    }

    /// <summary>
    /// A tight group of three exactly at <c>range + radius</c> is in the manager's reach (it is counted), but the point pulled
    /// back to the range (0.05 m inside it) leaves them outside the radius: no cast, and no walk in either. A step nearer
    /// (0.3 m inside the reach) it casts, at a point within range whose radius holds all three.
    /// </summary>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.01f, 0)] // just outside the reach
    [InlineData(-0.3f, 1)]
    public void AGroup_AtTheEdgeOfRangePlusRadius(float beyond, int expected)
    {
        Simulation sim = Scene(s_data.Value);
        AbilityDef fire = Ability(sim.World.Data, "telas_fire");
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        EntityHandle mage = Caster(sim, "malazan_cadre_mage", rally);
        float d = fire.Range + fire.Radius + beyond;
        // Three in a column across the line of fire, 0.6 m apart: all three within the radius of a point at the range's edge.
        for (int k = -1; k <= 1; k++) Enemy(sim, "whirlwind_raider", rally + new Vector2(d, 0.6f * k));
        List<Command> casts = Casts(sim);
        Vector2 from = sim.World.Units.Position[mage.Index];
        for (int t = 0; t < 3; t++) NextThink(sim);
        Assert.Equal(expected, casts.Count);
        foreach (Command c in casts) Assert.True(Vector2.Distance(c.Position, from) <= fire.Range, $"cast {Vector2.Distance(c.Position, from):F3} m away");
        Assert.True(Vector2.Distance(sim.World.Units.Position[mage.Index], from) < 0.5f, "the mage walked");
    }

    /// <summary>
    /// A group standing on a plateau no ramp reaches (a cliff island): the cast point is within range, so the caster never
    /// walks; the cast lands (ground target, no line of sight rule) and the sends stay bounded (at most a send per cooldown,
    /// plus MaxTries), never one a think.
    /// </summary>
    [Fact]
    public void AGroupOnAnUnreachablePlateau_IsCastOn_WithBoundedSends()
    {
        GameData data = s_data.Value;
        // 64 x 64, all level 0 but a 6 x 6 level-2 block at cells (40-45, 28-33): sheer cliffs all round.
        var rows = new string[AiScenes.FlatSize];
        for (int y = 0; y < rows.Length; y++)
        {
            char[] row = new string('0', AiScenes.FlatSize).ToCharArray();
            if (y >= 28 && y <= 33) for (int x = 40; x <= 45; x++) row[x] = '2';
            rows[y] = new string(row);
        }
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        var sim = new Simulation(new SimConfig(3, 2, 256, 512) { Data = data, Seats = seats.ToImmutableArray(), Combat = false }, LocalMovementTests.Rows(rows));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, StartBase.BuildingOfSlot(data, 0, BuildingSlot.TownHall), 30 * 64 + 8, out _));
        Assert.True(w.Buildings.Spawn(1, StartBase.BuildingOfSlot(data, 1, BuildingSlot.TownHall), 30 * 64 + 56, out _));
        TestSim.Explored(sim);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        NextThink(sim);
        AbilityDef fire = Ability(data, "telas_fire");
        Vector2 island = w.NavGrid.CellCenter(42, 30);
        Assert.False(w.NavGrid.IsPassable(42, 30) && w.NavGrid.LevelAt(42, 30) == w.NavGrid.LevelAt(30, 30), "precondition: a plateau");
        EntityHandle mage = Caster(sim, "malazan_cadre_mage", island - new Vector2(fire.Range - 2f, 0f));
        for (int k = -1; k <= 1; k++) Enemy(sim, "whirlwind_raider", island + new Vector2(0f, 0.6f * k));
        List<Command> casts = Casts(sim);
        int landed = 0;
        int until = sim.TickNumber + 4000;
        while (sim.TickNumber < until)
        {
            sim.Tick();
            foreach (Abilities.AbilityEvent e in w.AbilityEvents) if (e.Resolved && e.Caster == mage) landed++;
        }
        int cooldowns = 4000 / (fire.CooldownTicks + fire.CastTicks) + 1;
        Assert.True(landed > 0, "no cast landed on the plateau");
        Assert.True(casts.Count <= cooldowns + AbilityManager.MaxTries, $"{casts.Count} casts sent, {landed} landed, at most {cooldowns} cooldowns");
        Assert.True(casts.Count <= landed + AbilityManager.MaxTries, $"{casts.Count} casts sent, {landed} landed");
    }

    /// <summary>
    /// A defeated AI seat never casts. M5-4e re-staged: a live unit keeps a side in, so once its hall is down the scene takes
    /// its units away, casters too (the old scene, casters alive beside enemies after the defeat, can't happen any more);
    /// enemies are then set beside where the casters stood.
    /// </summary>
    [Fact]
    public void ADefeatedSeat_NeverCasts()
    {
        Simulation sim = Scene(s_data.Value, combat: true);
        World w = sim.World;
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        Caster(sim, "malazan_cadre_mage", rally);
        Caster(sim, "malazan_cadre_mage", rally + new Vector2(0f, 2f));
        // The AI's only building: its hall, razed by a test seam (hp to 1, then a hit from a raider of player 1 standing by it).
        int hall = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) hall = k;
        w.Buildings.SetHp(hall, 1);
        Vector2 hallAt = sim.AiOf(0)!.BuildingCentre(w.Buildings.TypeId[hall], w.Buildings.Cell[hall]);
        EntityHandle raider = CombatScenes.Place(sim, 1, w.Data.FindUnit("whirlwind_raider"), hallAt + new Vector2(-4f, 0f));
        sim.Enqueue(Command.Attack(1, raider, w.Buildings.HandleOf(hall), isBuilding: true));
        int guard = 0;
        while (w.Buildings.Alive[hall] && guard++ < 600) sim.Tick();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        sim.Tick();
        Assert.True(w.IsDefeated(0), "the AI seat was not defeated");
        List<Command> casts = Casts(sim);
        // Enemies the casters see, in reach (their own sight is enough, but spot them anyway); healed every tick (combat is on).
        var enemies = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) enemies.Add(Enemy(sim, "whirlwind_raider", rally + new Vector2(9f, k - 1.5f)));
        for (int t = 0; t < 600; t++)
        {
            foreach (EntityHandle e in enemies) if (w.Units.IsAlive(e)) w.Units.Hp[e.Index] = w.Data.Units[w.Units.TypeId[e.Index]].Hp;
            sim.Tick();
        }
        Assert.Empty(casts);
    }

    /// <summary>castMinTargets 50 (its maximum): fifty enemies packed within a Sandstorm's radius are cast on; forty-nine are not.</summary>
    [Theory]
    [InlineData(50, 1)]
    [InlineData(49, 0)]
    public void CastMinTargets50_FiftyInTheRadius_IsCastOn(int enemies, int expected)
    {
        Simulation sim = Scene(s_min50.Value, faction: 1, units: 512);
        AbilityDef storm = Ability(sim.World.Data, "sandstorm");
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        Caster(sim, "whirlwind_priest", rally);
        Vector2 centre = rally + new Vector2(storm.Range - 2f, 0f);
        // A 1 m grid inside a disc of radius (radius - 1.5 m), so separation pushes nobody out of the radius in the ticks before the think.
        int placed = 0;
        float keep = storm.Radius - 1.5f;
        for (int y = -6; y <= 6 && placed < enemies; y++)
            for (int x = -6; x <= 6 && placed < enemies; x++)
                if (x * x + y * y <= keep * keep) { Enemy(sim, "malazan_heavy_infantry", centre + new Vector2(x, y)); placed++; }
        Assert.Equal(enemies, placed);
        List<Command> casts = Casts(sim);
        NextThink(sim);
        Assert.Equal(expected, casts.Count);
    }

    /// <summary>
    /// Three Priests beside one group. BUG-0702 item 2 (M5-H2): one storm blinds and slows the group as well as three (the
    /// statuses refresh, they don't stack), so while it runs only the first Priest casts on the point; the others keep their
    /// cooldowns (before: all three cast on one point in one think), and one casts again once it is over. Also: no re-cast
    /// loop over 20 thinks (each Priest casts once at most).
    /// </summary>
    [Fact]
    public void ThreePriests_OneStormOnOnePoint_TheOthersHoldTheirs()
    {
        Simulation sim = Scene(s_data.Value, faction: 1);
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        for (int k = 0; k < 3; k++) Caster(sim, "whirlwind_priest", rally + new Vector2(0f, 1.5f * k));
        for (int k = 0; k < 4; k++) Enemy(sim, "malazan_heavy_infantry", rally + new Vector2(10f, k - 1.5f));
        List<Command> casts = Casts(sim);
        for (int t = 0; t < 20; t++) NextThink(sim); // 20 thinks: well inside the 45 s cooldown
        AbilityDef storm = Ability(sim.World.Data, "sandstorm");
        Assert.NotEmpty(casts);
        int ends = casts[0].Tick + storm.CastTicks + storm.DurationTicks;
        Assert.Single(casts.Where(c => c.Tick < ends && Vector2.Distance(c.Position, casts[0].Position) <= storm.Radius));
        Assert.Equal(casts.Count, casts.Select(c => c.Unit).Distinct().Count());
    }

    /// <summary>
    /// The Cusser and own units: one standing just outside the radius of the group's centroid doesn't hold the throw; one
    /// just inside does (the radius test is on unit centres, like the blast's).
    /// </summary>
    [Theory]
    [InlineData(0.3f, 1)]
    [InlineData(-0.3f, 0)]
    public void TheCusser_OwnUnitAtTheRadiusEdge(float beyond, int expected)
    {
        Simulation sim = Scene(s_data.Value);
        AbilityDef cusser = Ability(sim.World.Data, "cusser");
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        Caster(sim, "malazan_sapper", rally);
        Vector2 group = rally + new Vector2(cusser.Range - 0.5f, 0f);
        for (int k = -1; k <= 1; k++) Enemy(sim, "whirlwind_raider", group + new Vector2(0f, 0.7f * k));
        // North of the group (the Sapper is west), at radius + beyond from the group's centre.
        EntityHandle friend = CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("malazan_heavy_infantry"), group + new Vector2(0f, -(cusser.Radius + beyond)));
        List<Command> casts = Casts(sim);
        // One think only: separation moves the friend a little as ticks pass.
        NextThink(sim);
        Assert.Equal(expected, casts.Count);
        foreach (Command c in casts)
        {
            float d = Vector2.Distance(c.Position, sim.World.Units.Position[friend.Index]);
            Assert.True(d > cusser.Radius, $"thrown {d:F2} m from an own unit");
        }
    }

    /// <summary>
    /// BUG-0702 item 1 (un-skipped at M5-4f: the group is seeded from the enemy nearest the caster): two pairs of enemies either side of a Cadre Mage, 8 m east and 8 m west,
    /// each pair enough for castMinTargets 2. The centroid of all four is the mage's own spot, with no enemy within the
    /// radius of it, so no cast is ever sent. Expected by this row: one of the pairs is cast on.
    /// </summary>
    [Fact]
    public void TwoGroupsEitherSide_OneIsCastOn()
    {
        Simulation sim = Scene(s_data.Value);
        Vector2 rally = sim.AiOf(0)!.Board.Rally;
        Caster(sim, "malazan_cadre_mage", rally);
        foreach (float x in new[] { 8f, -8f })
            for (int k = 0; k < 2; k++) Enemy(sim, "whirlwind_raider", rally + new Vector2(x, k - 0.5f));
        List<Command> casts = Casts(sim);
        for (int t = 0; t < 3; t++) NextThink(sim);
        Assert.Single(casts);
    }
}
