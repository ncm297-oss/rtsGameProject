using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Scenario;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D18 report 4 (the owner's playtest note 6: buildings take too long to kill): each attacker group, as shipped, against a
/// lone player-1 House, infantry hall and Town Hall (the other faction's; both factions' rows are equal in hp, armor and
/// footprint), combat on, flat ground, no defenders: seconds to raze and the share of attackers that started a swing or a
/// shot within 10 s. Then the same under three levers applied to in-memory copies of the data (<see cref="TestDataDir"/>
/// temp folders, never the shipped files): (A) the Structure multipliers melee 0.4 to 0.7, pierce 0.2 to 0.35, magic 0.4
/// to 0.6, siege 3.0 kept; (B) every building's armor halved, rounded down; (C) both.
/// <para>
/// The band (the Producer's default, D18 brief): 20 Age I line units (the 20 Heavy Infantry and 20 Raider rows) raze a
/// House in at most 20 s, an infantry hall in 60 s, a Town Hall in 2 min; and each siege group (Catapults, Sappers) does at
/// least 2x the 20 Heavy Infantry's damage per second per gold + wood spent against the same building. The scene follows
/// <see cref="CounterTriangleScene"/>'s (seed 7, blocks 5 wide, 1.8 m apart, a player-0 spotter past the building so the
/// fog keeps the order), with the block's front rank 10 m from the building's west side, every attacker ordered to Attack
/// it. Only that every run ran is asserted: the tables and the proposed lever are for the owner.
/// </para>
/// </summary>
public class BuildingKillTimeReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int MaxTicks = 6000;
    private const int StruckWithin = 10 * Tps;
    private const int MapSize = 64;
    private const int AnchorX = 34, AnchorY = 30;
    private const float FrontGap = 10f;

    private static readonly (string Key, int Count, bool Line, bool Siege)[] Groups =
    {
        ("malazan_heavy_infantry", 20, true, false),
        ("whirlwind_raider", 20, true, false),
        ("malazan_crossbowman", 20, false, false),
        ("whirlwind_raider", 10, false, false),
        ("malazan_catapult", 2, false, true),
        ("malazan_sapper", 2, false, true),
    };

    private static readonly (BuildingSlot Slot, string Name, float BandSeconds)[] Targets =
    {
        (BuildingSlot.House, "House", 20f),
        (BuildingSlot.InfantryHall, "Infantry hall", 60f),
        (BuildingSlot.TownHall, "Town Hall", 120f),
    };

    private readonly ITestOutputHelper _out;

    public BuildingKillTimeReportTests(ITestOutputHelper output) => _out = output;

    /// <summary>One run: ticks to raze (<see cref="int.MaxValue"/> if it stood <see cref="MaxTicks"/>), the hp left then, the attackers that struck within 10 s.</summary>
    private readonly record struct Run(int Ticks, int HpLeft, int Struck, int Count, int Cost, int BuildingHp);

    private static GameData Variant(bool structure, bool armor)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        if (structure)
            dir.EditJson("common/damage_table.json", root =>
            {
                foreach (JsonNode? t in root["damageTypes"]!.AsArray())
                {
                    double? to = (string)t!["id"]! switch { "melee" => 0.7, "pierce" => 0.35, "magic" => 0.6, _ => null };
                    if (to != null) t["multipliers"]!["structure"] = to.Value;
                }
            });
        if (armor)
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/buildings.json", root =>
                {
                    foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["armor"] = (int)b["armor"]! / 2;
                });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    /// <summary>The other faction's building of <paramref name="slot"/> (the attackers' enemy).</summary>
    private static int BuildingFor(GameData data, string attackerKey, BuildingSlot slot)
    {
        int enemy = 1 - data.Units[data.FindUnit(attackerKey)].Faction;
        int b = Array.FindIndex(data.Buildings.ToArray(), d => d.Faction == enemy && d.Slot == slot);
        Assert.True(b >= 0, $"no {slot} for faction {enemy}");
        return b;
    }

    private static Run Raze(GameData data, string attackerKey, int n, int buildingType)
    {
        var sim = new Simulation(new SimConfig(CounterTriangleScene.Seed, 2, n + 8, 8 * n + 32) { Data = data }, LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        BuildingDef bd = data.Buildings[buildingType];
        Assert.True(w.Buildings.Spawn(1, buildingType, AnchorY * MapSize + AnchorX, out EntityHandle b));
        Spotter(sim, 0, At(sim, AnchorX + bd.FootprintWidth + 3, AnchorY));
        int type = data.FindUnit(attackerKey);
        Vector2 west = At(sim, AnchorX, AnchorY) - new Vector2(1f, 1f); // the anchor cell's corner: the footprint's west side
        float midY = west.Y + bd.FootprintHeight;
        const int width = CounterTriangleScene.BlockWidth;
        const float spacing = CounterTriangleScene.Spacing;
        var attackers = new EntityHandle[n];
        for (int k = 0; k < n; k++)
        {
            int rank = k / width, file = k % width;
            int across = Math.Min(width, n - rank * width);
            var at = new Vector2(west.X - FrontGap - rank * spacing, midY + (file - (across - 1) / 2f) * spacing);
            attackers[k] = Place(sim, 0, type, at);
            sim.Enqueue(Command.Attack(0, attackers[k], b, isBuilding: true));
        }
        UnitStore u = w.Units;
        var struck = new bool[n];
        var before = new int[n];
        int t = 0;
        while (t < MaxTicks && w.Buildings.IsAlive(b))
        {
            for (int k = 0; k < n; k++) before[k] = u.IsAlive(attackers[k]) ? u.CooldownTicks[attackers[k].Index] : 0;
            sim.Tick();
            t++;
            if (t > StruckWithin) continue;
            // A swing or shot starts the cooldown, so a cooldown that went up this tick is a strike begun.
            for (int k = 0; k < n; k++)
                if (u.IsAlive(attackers[k]) && u.CooldownTicks[attackers[k].Index] > before[k]) struck[k] = true;
        }
        bool alive = w.Buildings.IsAlive(b);
        return new Run(alive ? int.MaxValue : t, alive ? w.Buildings.Hp[b.Index] : 0, struck.Count(s => s), n,
            n * CounterTriangleScene.Cost(data.Units[type]), bd.Hp);
    }

    private static string Seconds(Run r) => r.Ticks == int.MaxValue
        ? string.Create(CultureInfo.InvariantCulture, $"> {MaxTicks / Tps} s ({100.0 * r.HpLeft / r.BuildingHp:F0} % hp left)")
        : string.Create(CultureInfo.InvariantCulture, $"{(double)r.Ticks / Tps:F1} s");

    /// <summary>Hit points razed a second per 1,000 gold + wood (a building that stood counts what it lost).</summary>
    private static double RatePerCost(Run r) =>
        (r.BuildingHp - r.HpLeft) / (Math.Min(r.Ticks, MaxTicks) / (double)Tps) / r.Cost * 1000.0;

    [Fact]
    public void AttackerGroups_v_Buildings_TodayAndThreeLevers_Report()
    {
        var variants = new (string Name, GameData Data)[]
        {
            ("today", TestSim.Data),
            ("(A) Structure 0.7 / 0.35 / 0.6", Variant(structure: true, armor: false)),
            ("(B) building armor halved", Variant(structure: false, armor: true)),
            ("(C) A + B", Variant(structure: true, armor: true)),
        };
        var runs = new Run[variants.Length, Groups.Length, Targets.Length];
        var met = new int[variants.Length];
        int cells = 0;
        _out.WriteLine("D18 report 4, 2026-10-14: seconds to raze a lone building (no defenders, flat, combat on); [struck within 10 s]; band check where one applies");
        for (int v = 0; v < variants.Length; v++)
        {
            GameData data = variants[v].Data;
            _out.WriteLine("");
            _out.WriteLine($"### {variants[v].Name}");
            _out.WriteLine("| Attackers (cost) | " + string.Join(" | ", Targets.Select(x => x.Name)) + " |");
            _out.WriteLine("| --- | --- | --- | --- |");
            for (int g = 0; g < Groups.Length; g++)
                for (int x = 0; x < Targets.Length; x++)
                {
                    runs[v, g, x] = Raze(data, Groups[g].Key, Groups[g].Count, BuildingFor(data, Groups[g].Key, Targets[x].Slot));
                    Assert.True(runs[v, g, x].Ticks > 0, $"{variants[v].Name} {Groups[g].Key}: nothing ran");
                }
            int vcells = 0;
            for (int g = 0; g < Groups.Length; g++)
            {
                var row = new List<string>();
                for (int x = 0; x < Targets.Length; x++)
                {
                    Run r = runs[v, g, x];
                    string cell = $"{Seconds(r)} [{r.Struck} / {r.Count}]";
                    bool? ok = null;
                    if (Groups[g].Line) ok = r.Ticks != int.MaxValue && r.Ticks <= Targets[x].BandSeconds * Tps;
                    if (Groups[g].Siege)
                    {
                        double ratio = RatePerCost(r) / RatePerCost(runs[v, 0, x]);
                        cell += string.Create(CultureInfo.InvariantCulture, $" {ratio:F1}x HI per cost");
                        ok = ratio >= 2.0;
                    }
                    if (ok is bool b)
                    {
                        cell += b ? " ok" : " MISS";
                        vcells++;
                        if (b) met[v]++;
                    }
                    row.Add(cell);
                }
                int cost = runs[v, g, 0].Cost;
                _out.WriteLine($"| {Groups[g].Count} {Groups[g].Key} ({cost}) | " + string.Join(" | ", row) + " |");
            }
            cells = vcells;
            _out.WriteLine($"band cells met: {met[v]} / {vcells}");
        }

        // The smallest set: today, then A (3 numbers in one file), then B (every building's armor), then C.
        int pick = Array.FindIndex(met, m => m == cells);
        int nearest = Array.IndexOf(met, met.Max());
        _out.WriteLine("");
        _out.WriteLine(pick >= 0
            ? $"Proposed (the smallest lever set that meets every band cell): {variants[pick].Name}"
            : $"No lever set meets every band cell; nearest: {variants[nearest].Name} ({met[nearest]} / {cells})");
        int show = pick >= 0 ? pick : nearest;
        _out.WriteLine("");
        _out.WriteLine($"| Attackers | " + string.Join(" | ", Targets.Select(x => x.Name + $" today / {(show == 0 ? "today" : variants[show].Name[..3])}")) + " |");
        _out.WriteLine("| --- | --- | --- | --- |");
        for (int g = 0; g < Groups.Length; g++)
            _out.WriteLine($"| {Groups[g].Count} {Groups[g].Key} | " +
                string.Join(" | ", Enumerable.Range(0, Targets.Length).Select(x => $"{Seconds(runs[0, g, x])} / {Seconds(runs[show, g, x])}")) + " |");
    }
}
