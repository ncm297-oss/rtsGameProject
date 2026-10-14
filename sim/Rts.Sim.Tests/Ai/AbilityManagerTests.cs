using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c criterion 1: <see cref="AbilityManager"/> on the flat scene (combat off: the commands are what is checked, and
/// abilities resolve without it). Player 0 is the AI (Normal unless given); player 1 a human whose units the test places and
/// spots for player 0. Both factions' build orders are quiet, no wave starts, no scout goes, and the defense radius is its
/// minimum (8 m), so nothing else orders the casters; they stand on the rally point (read after a first think), so the rally
/// rule leaves them alone. Ability numbers (range, radius, cooldown) are read from <see cref="GameData"/>.
/// </summary>
public class AbilityManagerTests
{
    private static GameData Data(int castMinTargets = 2, Action<TestDataDir>? more = null) => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", f == "malazan" ? "malazan_laborer" : "whirlwind_camp_follower", 1));
                root["attack"] = new JsonObject { ["minArmyPop"] = 100, ["minArmyCost"] = 0 };
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

    /// <summary>Player 0 (AI, <paramref name="faction"/>) against player 1 (human, the other faction); no workers, no money, one think run (the rally point is set).</summary>
    private static Simulation Scene(GameData data, AiDifficulty difficulty = AiDifficulty.Normal, int faction = 0)
    {
        PlayerSeat[] seats = { new(faction, SeatController.Ai, difficulty), new(1 - faction, SeatController.Human, difficulty) };
        Simulation sim = AiScenes.Flat(seats, data, workers: 0);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        NextThink(sim);
        Assert.True(sim.AiOf(0)!.Board.RallyVersion > 0);
        return sim;
    }

    /// <summary>Ticks to the next think of player 0 and through it (its commands are logged by then).</summary>
    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static EntityHandle Caster(Simulation sim, string type, Vector2 offset = default) =>
        CombatScenes.Place(sim, 0, sim.World.Data.FindUnit(type), sim.AiOf(0)!.Board.Rally + offset);

    /// <summary><paramref name="n"/> units of player 1 of <paramref name="type"/> at <paramref name="at"/>, 1 m apart along y round it, spotted by player 0.</summary>
    private static List<EntityHandle> Enemies(Simulation sim, string type, int n, Vector2 at)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 1, sim.World.Data.FindUnit(type), at + new Vector2(0f, k - (n - 1) / 2f));
            CombatScenes.Spot(sim, 0, h);
            list.Add(h);
        }
        return list;
    }

    private static AbilityDef Ability(string key)
    {
        GameData d = TestSim.Data;
        for (int a = 0; a < d.Abilities.Length; a++)
            if (d.Abilities[a].Key == key) return d.Abilities[a];
        throw new InvalidOperationException(key);
    }

    /// <summary>The AI's casts, each with its caster's position at the think that sent it.</summary>
    private static List<(Command Cast, Vector2 From)> Casts(Simulation sim)
    {
        var list = new List<(Command, Vector2)>();
        sim.AiOf(0)!.SentForTests = c =>
        {
            if (c.Kind == CommandKind.UseAbility) list.Add((c, sim.World.Units.Position[c.Unit.Index]));
        };
        return list;
    }

    private static Vector2 Centroid(Simulation sim, List<EntityHandle> units)
    {
        Vector2 sum = Vector2.Zero;
        foreach (EntityHandle h in units) sum += sim.World.Units.Position[h.Index];
        return sum / units.Count;
    }

    /// <summary>(a) A Normal Cadre Mage casts Telas Fire on three enemies in range within two thinks, at their centroid; the cast lands (its cooldown starts).</summary>
    [Fact]
    public void ACadreMage_CastsTelasFire_AtThreeEnemiesInRange_WithinTwoThinks()
    {
        Simulation sim = Scene(s_data.Value);
        EntityHandle mage = Caster(sim, "malazan_cadre_mage");
        List<EntityHandle> enemies = Enemies(sim, "whirlwind_raider", 3, sim.AiOf(0)!.Board.Rally + new Vector2(9f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        NextThink(sim);
        if (casts.Count == 0) NextThink(sim);
        (Command cast, Vector2 from) = Assert.Single(casts);
        Assert.Equal((mage, 0), (cast.Unit, cast.TypeId));
        Assert.True(Vector2.Distance(cast.Position, Centroid(sim, enemies)) < 0.5f, $"cast at {cast.Position}, the enemies at {Centroid(sim, enemies)}");
        Assert.True(Vector2.Distance(cast.Position, from) <= Ability("telas_fire").Range);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.True(sim.World.Units.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities] > 0, "the cast did not land");
    }

    /// <summary>(a) A lone enemy in range is no target at the shipped castMinTargets (2): no cast in five thinks.</summary>
    [Fact]
    public void ACadreMage_DoesNotCast_AtALoneEnemy()
    {
        Simulation sim = Scene(s_data.Value);
        Caster(sim, "malazan_cadre_mage");
        Enemies(sim, "whirlwind_raider", 1, sim.AiOf(0)!.Board.Rally + new Vector2(9f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        for (int k = 0; k < 5; k++) NextThink(sim);
        Assert.Empty(casts);
    }

    /// <summary>(b) A Sapper with no group of enemy units in reach throws the Cusser at the enemy building it sees (pulled back into range); the building takes the hit.</summary>
    [Fact]
    public void ASapper_ThrowsTheCusser_AtAnEnemyBuilding_WhenNoGroupQualifies()
    {
        Simulation sim = Scene(s_data.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        AbilityDef cusser = Ability("cusser");
        EntityHandle sapper = Caster(sim, "malazan_sapper");
        // A tent of player 1 east of the rally point: its centre within the Cusser's range + radius, beyond its range.
        Vector2 rally = ai.Board.Rally;
        int cx = (int)(rally.X / 2f) + 3, cy = (int)(rally.Y / 2f) - 1;
        Assert.True(w.Buildings.Spawn(1, w.Data.FindBuilding("whirlwind_tent"), cy * AiScenes.FlatSize + cx, out EntityHandle tent));
        Vector2 centre = ai.BuildingCentre(w.Buildings.TypeId[tent.Index], w.Buildings.Cell[tent.Index]);
        float d = Vector2.Distance(centre, rally);
        Assert.True(d > cusser.Range && d < cusser.Range + cusser.Radius, $"precondition: the tent's centre is {d:F1} m away");
        w.Fog.Update();
        Enemies(sim, "whirlwind_raider", 1, rally + new Vector2(0f, 8f)); // a lone unit in reach is no group
        int hp = w.Buildings.Hp[tent.Index];
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        NextThink(sim);
        (Command cast, Vector2 from) = Assert.Single(casts);
        Assert.Equal(sapper, cast.Unit);
        Assert.True(Vector2.Distance(cast.Position, from) <= cusser.Range, $"{Vector2.Distance(cast.Position, from):F2} m from the Sapper");
        Assert.True(Vector2.Distance(cast.Position, centre) <= cusser.Radius);
        Assert.True(Vector2.Distance(cast.Position, from) > cusser.Radius, "the Sapper stands in its own blast");
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.True(w.Buildings.Hp[tent.Index] < hp, "the tent took no hit");
    }

    /// <summary>(c) Sandstorm lands on the enemy group's centroid in range, and on a group beyond its range (within range + radius) at the range's edge, toward it.</summary>
    [Theory]
    [InlineData(10f)]
    [InlineData(22f)]
    public void Sandstorm_LandsOnTheCentroid_NeverBeyondItsRange(float away)
    {
        Simulation sim = Scene(s_data.Value, faction: 1);
        AbilityDef storm = Ability("sandstorm");
        Assert.True(away < storm.Range + storm.Radius - 1f);
        Caster(sim, "whirlwind_priest");
        List<EntityHandle> enemies = Enemies(sim, "malazan_heavy_infantry", 4, sim.AiOf(0)!.Board.Rally + new Vector2(away, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        NextThink(sim);
        (Command cast, Vector2 from) = Assert.Single(casts);
        Vector2 centroid = Centroid(sim, enemies);
        float range = Vector2.Distance(cast.Position, from);
        Assert.True(range <= storm.Range, $"cast {range:F2} m away");
        if (Vector2.Distance(centroid, from) <= storm.Range) Assert.True(Vector2.Distance(cast.Position, centroid) < 0.01f);
        else
        {
            Assert.True(range > storm.Range - 0.1f, $"cast {range:F2} m away, short of the range");
            Vector2 toward = Vector2.Normalize(centroid - from), sent = Vector2.Normalize(cast.Position - from);
            Assert.True(Vector2.Dot(toward, sent) > 0.9999f, "not on the way to the centroid");
        }
    }

    /// <summary>(d) castMinTargets' edges: at 1 a lone enemy is a target; at 50 three are not.</summary>
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(50, 3, 0)]
    public void CastMinTargets_AtItsEdges(int min, int enemies, int expected)
    {
        Simulation sim = Scene(Data(castMinTargets: min));
        Assert.Equal(min, sim.AiOf(0)!.Rules.CastMinTargets);
        Caster(sim, "malazan_cadre_mage");
        Enemies(sim, "whirlwind_raider", enemies, sim.AiOf(0)!.Board.Rally + new Vector2(9f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.Equal(expected, casts.Count);
    }

    /// <summary>
    /// (e) After a cast lands the caster is not ordered again while its cooldown runs; the cast is judged by the cooldown
    /// moving (the tries start again); and once the cooldown is over it casts again.
    /// </summary>
    [Fact]
    public void ACasterOnCooldown_IsNotReordered_AndTheCastIsJudgedByTheCooldown()
    {
        Simulation sim = Scene(s_data.Value);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mage = Caster(sim, "malazan_cadre_mage");
        Enemies(sim, "whirlwind_raider", 3, ai.Board.Rally + new Vector2(9f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        NextThink(sim);
        Assert.Single(casts);
        Assert.Equal(1, ai.Board.CastTries[mage.Index]);
        int ready = 0;
        while (ready == 0) { NextThink(sim); ai.View.TryGetAbilityReady(mage.Index, 0, out ready); }
        NextThink(sim);
        Assert.Equal(0, ai.Board.CastTries[mage.Index]); // judged: it landed
        while (sim.TickNumber < ready - ai.ThinkTicks) NextThink(sim);
        Assert.Single(casts);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.Equal(2, casts.Count);
    }

    /// <summary>
    /// (e) A cast that never lands (the test stops the caster every tick it casts) is sent <see cref="AbilityManager.MaxTries"/>
    /// times at the same point, then left alone; once the enemies move a few meters it is sent again.
    /// </summary>
    [Fact]
    public void ACastThatNeverLands_IsSentMaxTriesTimes_ThenAgainWhenThePointMoves()
    {
        Simulation sim = Scene(s_data.Value);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mage = Caster(sim, "malazan_cadre_mage");
        List<EntityHandle> enemies = Enemies(sim, "whirlwind_raider", 3, ai.Board.Rally + new Vector2(9f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        void Run(int thinks)
        {
            int until = sim.TickNumber + thinks * ai.ThinkTicks;
            while (sim.TickNumber < until)
            {
                if (sim.World.Units.CastAbility[mage.Index] >= 0) sim.Enqueue(Command.Stop(0, mage));
                sim.Tick();
            }
        }
        Run(12);
        Assert.Equal(AbilityManager.MaxTries, casts.Count);
        Assert.Equal(0, sim.World.Units.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities]);
        foreach (EntityHandle e in enemies) sim.Enqueue(Command.Move(1, e, sim.World.Units.Position[e.Index] + new Vector2(3f, 0f)));
        Run(6);
        Assert.True(casts.Count > AbilityManager.MaxTries, "not sent again at the new point");
    }

    /// <summary>(f) An Easy seat (useAbilities false) sends no UseAbility in 6,000 ticks; its Normal twin casts.</summary>
    [Fact]
    public void AnEasySeat_NeverCasts_ItsNormalTwinDoes()
    {
        foreach ((AiDifficulty d, bool casts) in new[] { (AiDifficulty.Easy, false), (AiDifficulty.Normal, true) })
        {
            Simulation sim = Scene(s_data.Value, d);
            Caster(sim, "malazan_cadre_mage");
            Enemies(sim, "whirlwind_raider", 3, sim.AiOf(0)!.Board.Rally + new Vector2(9f, 0f));
            List<(Command Cast, Vector2 From)> log = Casts(sim);
            while (sim.TickNumber < 6000) sim.Tick();
            Assert.True(casts == log.Count > 0, $"{d}: {log.Count} casts");
            Assert.Equal(log.Count, sim.AiOf(0)!.Board.CastsSent);
        }
    }

    /// <summary>(g) The Cusser (friendly fire) is not thrown at a group with an own unit within its radius; with that unit gone it is.</summary>
    [Fact]
    public void AFriendlyFireAbility_IsNeverCast_WithAnOwnUnitInItsRadius()
    {
        Simulation sim = Scene(s_data.Value);
        AiPlayer ai = sim.AiOf(0)!;
        AbilityDef cusser = Ability("cusser");
        Caster(sim, "malazan_sapper");
        List<EntityHandle> enemies = Enemies(sim, "whirlwind_raider", 3, ai.Board.Rally + new Vector2(5f, 0f));
        // Between the Sapper and the group: within the radius of the group's centroid, and near enough the rally point to be left there.
        EntityHandle friend = CombatScenes.Place(sim, 0, sim.World.Data.FindUnit("malazan_heavy_infantry"), Centroid(sim, enemies) - new Vector2(2f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        for (int k = 0; k < 4; k++) NextThink(sim);
        Assert.Empty(casts);
        sim.World.Units.Free(friend);
        NextThink(sim);
        (Command cast, Vector2 from) = Assert.Single(casts);
        Assert.True(Vector2.Distance(cast.Position, from) > cusser.Radius, "thrown at its own feet");
    }

    /// <summary>A self / aura ability (test-only data on the Cadre Mage) is cast at the caster when castMinTargets enemies stand within its radius, not with one.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void ASelfAura_IsCastAtTheCaster_WhenEnoughEnemiesAreNear(int enemies, int expected)
    {
        GameData data = s_aura.Value;
        Simulation sim = Scene(data);
        EntityHandle mage = Caster(sim, "malazan_cadre_mage");
        Enemies(sim, "whirlwind_raider", enemies, sim.AiOf(0)!.Board.Rally + new Vector2(2f, 0f));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        NextThink(sim);
        Assert.Equal(expected, casts.Count);
        if (expected == 0) return;
        Assert.Equal(mage, casts[0].Cast.Unit);
        Assert.Equal(casts[0].From, casts[0].Cast.Position);
    }

    /// <summary>
    /// BUG-0700 (the door fuzz, seed 4): enemy units standing off the map (a dev spawn may put one there) moved the group's
    /// centroid off it, and the AI sent a <c>UseAbility</c> the door refuses, again and again. The point is kept on the map now
    /// (and dropped when that leaves it out of range). A Cadre Mage by the west edge, three Raiders 2 m past it.
    /// </summary>
    [Fact]
    public void AGroupOffTheMap_IsCastAtThePointOnTheMapNearestIt()
    {
        Simulation sim = Scene(Data(castMinTargets: 1));
        float y = sim.AiOf(0)!.Board.Rally.Y;
        Caster(sim, "malazan_cadre_mage", new Vector2(4f - sim.AiOf(0)!.Board.Rally.X, 0f));
        Enemies(sim, "whirlwind_raider", 3, new Vector2(-2f, y));
        List<(Command Cast, Vector2 From)> casts = Casts(sim);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.NotEmpty(casts);
        Assert.All(casts, c => Assert.True(sim.World.NavGrid.WorldToCell(c.Cast.Position, out _, out _), $"a cast off the map at {c.Cast.Position}"));
    }

    private static readonly Lazy<GameData> s_aura = new(() => Data(more: dir =>
    {
        dir.EditJson("factions/malazan/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_cry", ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "selfAura", ["radius"] = 4,
            ["castTime"] = 0, ["cooldown"] = 10, ["affects"] = "own_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "applyStatus", ["status"] = "frenzied", ["magnitude"] = 0.5, ["duration"] = 1 }),
        }));
        dir.SetUnitField("malazan", "malazan_cadre_mage", "abilities", "[\"zz_test_cry\"]");
    }));
}
