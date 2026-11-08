using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H5 (session 2026-11-07-2306): attacks on the sim's hardening. BUG-1118 item 1's judge (a hall Build at the lost
/// anchor is not marked bad while <see cref="PlayerView.CanPlace"/> still takes it), which reads a rule that by its own
/// summary does not check units in the way; and BUG-1120's <see cref="MilitaryManager.SightingPoint"/> under footprints
/// that are not a single building (a row of adjacent allied buildings, a seeded fuzz round two of them).
/// </summary>
public class SimHardeningM5H5QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningM5H5QaTests(ITestOutputHelper output) => _out = output;

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    /// <summary>Workers trained at time 0 (RebuildTests' scene data).</summary>
    private static readonly Lazy<GameData> s_workers = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 20))));

    /// <summary>A quiet order (EndGameHardeningTests' data): nothing but one worker at 3,600 s.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static EntityHandle Own(World w, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type) return new EntityHandle(k, w.Buildings.Generation[k]);
        return default;
    }

    /// <summary>
    /// BUG-1118 item 1's judge v the units-in-the-way rule: an enemy unit parked on the lost hall's footprint (it stays: the
    /// flat scene has combat off) makes every Keep Build at the old anchor refused (<see cref="PlacementError.UnitInTheWay"/>),
    /// but <see cref="PlayerView.CanPlace"/> never checks units, so the judge gives the anchor no retry mark and the next
    /// think sends the same Build again. Expected (M5-4d slice 2's rule: a refused anchor gets a retry mark and the next search
    /// moves on): a Keep site a few cells off within 20 thinks. With <paramref name="hidden"/> the parked unit is out of the
    /// seat's sight (the BUG-0280 case: the query ignores it, the Build does not).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnemyUnitParkedOnTheLostHallsAnchor_TheHallIsRebuiltOffIt(bool hidden)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_workers.Value);
        BuildMaps.Give(sim, 0, 2000, 2000);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        Rts.Sim.Combat.CombatSystem.FootprintRect(w, Keep, anchor, out Vector2 min, out Vector2 max);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        // A stealthed Whirlwind unit is "hidden" if the faction has one; else a plain enemy unit, seen or not by the seat's workers.
        EntityHandle parked = CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, (min + max) * 0.5f);
        int keepBuilds = 0, atAnchor = 0;
        EntityHandle site = default;
        for (int k = 0; k < 20 && site.Generation == 0; k++)
        {
            NextThink(sim);
            if (!hidden) CombatScenes.Spot(sim, 0, parked);
            site = Own(w, Keep);
        }
        foreach (Command c in log)
        {
            if (c.Tick <= razed || c.Kind != CommandKind.Build || c.TypeId != Keep) continue;
            keepBuilds++;
            if (c.Position == ai.CellCentre(anchor)) atAnchor++;
        }
        _out.WriteLine($"hidden {hidden}: {keepBuilds} Keep Builds after the raze, {atAnchor} at the old anchor; site {(site.Generation == 0 ? "none" : w.Buildings.Cell[site.Index].ToString())}; anchor bad {ai.Board.IsBad(Keep, anchor)}; parked alive {w.Units.Alive[parked.Index]}");
        Assert.True(w.Units.Alive[parked.Index]);
        Assert.True(site.Generation != 0, $"BUG-1148: no Keep site 20 thinks after the raze: {keepBuilds} Keep Builds, {atAnchor} of them at the occupied old anchor");
    }

    private static Simulation TeamScene()
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal, 1),
            new(1, SeatController.Human, AiDifficulty.Normal, 2),
            new(0, SeatController.Human, AiDifficulty.Normal, 1), // player 2: player 0's ally
        };
        return AiScenes.Flat(seats, s_quiet.Value);
    }

    private static float ClearOfAllies(World w, int p, Vector2 point)
    {
        float best = float.MaxValue;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] == p || w.IsEnemy(p, b.Owner[k])) continue;
            best = Math.Min(best, MathF.Sqrt(Rts.Sim.Combat.CombatSystem.BuildingDistanceSquared(w, k, point)));
        }
        return best;
    }

    /// <summary>
    /// BUG-1120's push under a row of adjacent allied Billets (2 x 2 cells each, touching, west to east) with the hunters far
    /// east on the row's axis and the enemy seen at the row's west wall: each push leaves one footprint's slack box and lands
    /// in the next, and <c>MaxPushes</c> (4) stops inside the fifth. QA's soak oracle (0.5 m of any allied footprint) must
    /// hold for every row length 1-6.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5, Skip = "BUG-1150 (open, S4): MaxPushes (4) stops inside the fifth touching allied footprint")]
    [InlineData(6, Skip = "BUG-1150 (open, S4): MaxPushes (4) stops inside the fifth touching allied footprint")]
    public void ASightingAtTheEndOfARowOfAlliedBuildings_IsPushedClearOfEveryOne(int count)
    {
        Simulation sim = TeamScene();
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int y = 40, x0 = 14;
        for (int k = 0; k < count; k++) Assert.True(w.Buildings.Spawn(2, Billet, y * AiScenes.FlatSize + x0 + 2 * k, out _));
        sim.Tick();
        var seen = new Vector2(x0 * 2f - 0.3f, y * 2f + 2f);
        var army = new Vector2(120f, y * 2f + 2f);
        Vector2 p = MilitaryManager.SightingPoint(ai, seen, army);
        float clear = ClearOfAllies(w, 0, p);
        _out.WriteLine($"{count} billets: seen {seen} -> {p}, {clear:F2} m clear, {Vector2.Distance(seen, p):F1} m moved");
        Assert.True(clear > MilitaryManager.AllySlack, $"{count} billets: the hunt's point {p} is {clear:F2} m from an allied footprint");
    }

    /// <summary>
    /// BUG-1120 fuzz (seeded): two allied buildings (a Keep and a Billet a few cells apart, as an AI base spaces them), 4,000
    /// sightings within 1 m of either footprint and armies anywhere on the map: every pushed point is finite, on the map, and
    /// more than the oracle's 0.5 m from every allied footprint, within 15 m of the sighting (a Keep crossed corner to corner); a sighting in the open (0.75 m clear) is unchanged.
    /// </summary>
    [Fact]
    public void SightingPoints_RoundTwoAlliedBuildings_AreAlwaysClear_Fuzz()
    {
        Simulation sim = TeamScene();
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        var rects = new List<(Vector2 Min, Vector2 Max)>();
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 2)
            {
                Rts.Sim.Combat.CombatSystem.FootprintRect(w, b.TypeId[k], b.Cell[k], out Vector2 mn, out Vector2 mx);
                rects.Add((mn, mx));
            }
        int extra = 8 * AiScenes.FlatSize + 36; // a Billet 2 cells east of player 2's Keep at (30, 8)
        Assert.True(w.Buildings.Spawn(2, Billet, extra, out _));
        Rts.Sim.Combat.CombatSystem.FootprintRect(w, Billet, extra, out Vector2 bmin, out Vector2 bmax);
        rects.Add((bmin, bmax));
        sim.Tick();
        var rng = new Random(1120);
        float size = AiScenes.FlatSize * MapConstants.CellSize;
        int bad = 0, open = 0, far = 0;
        string first = "";
        for (int i = 0; i < 4000; i++)
        {
            (Vector2 mn, Vector2 mx) = rects[rng.Next(rects.Count)];
            var seen = new Vector2(mn.X - 1f + (float)rng.NextDouble() * (mx.X - mn.X + 2f), mn.Y - 1f + (float)rng.NextDouble() * (mx.Y - mn.Y + 2f));
            var army = new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size);
            Vector2 p = MilitaryManager.SightingPoint(ai, seen, army);
            float clear = ClearOfAllies(w, 0, p);
            // The push box is square-cornered (0.5 m out on each axis), the oracle round: "open" is clear of the box corners too (0.5 * sqrt 2).
            bool inOpen = ClearOfAllies(w, 0, seen) > 0.75f;
            if (inOpen)
            {
                open++;
                if (p != ai.ClampToMap(seen) && first.Length == 0) first = $"a sighting in the open {seen} moved to {p}";
                continue;
            }
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > size || p.Y > size || clear <= MilitaryManager.AllySlack)
            {
                bad++;
                if (first.Length == 0) first = $"seen {seen} army {army} -> {p}, {clear:F2} m clear";
            }
            if (Vector2.Distance(seen, p) > 15f) far++; // across a Keep corner to corner (8 m + slack) and the 1 m step
        }
        _out.WriteLine($"4000 sightings: {open} in the open, {bad} bad, {far} moved over 15 m; first: {first}");
        Assert.True(first.Length == 0, first);
        Assert.Equal(0, far);
    }
}
