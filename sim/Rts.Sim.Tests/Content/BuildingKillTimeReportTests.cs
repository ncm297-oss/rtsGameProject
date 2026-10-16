using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Combat;
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
/// shot within 10 s. D18 measured three levers; the owner took set (C) (the Structure multipliers melee 0.4 to 0.7, pierce
/// 0.2 to 0.35, magic 0.4 to 0.6, siege 3.0 kept, and every building's armor halved, rounded down), which D19 shipped on
/// 2026-10-15. So "today" is the shipped data, and "before D19" puts D18's shipped values back in an in-memory copy
/// (<see cref="TestDataDir"/> temp folder, never the shipped files) for the today-vs-before table. Today must meet every
/// band cell. Both columns run on today's sim, so since the surround rule (M5-P1, merged 2026-10-16) "before D19" is D18's
/// data on the new movement and its seconds are not D18's own report's (re-run on the merged tree 2026-10-16: today 20 HI
/// 11.8 / 23.1 / 45.0 s, 20 Raiders 9.8 / 19.4 / 35.8 s, 12 / 12; D19's branch, before the rule, read 15.6 / 43.8 / 68.7 s
/// and 13.6 / 36.1 / 57.4 s; after the sim's M5-H2 merge the Raiders' hall cell reads 19.5 s, every other cell unchanged).
/// Player 1 is kept in by a far-corner worker placed after the attackers (BUG-0761: a lone House trains nothing, so without
/// it player 1 is out on tick 0 and the House collapses), and a fall that isn't player 0's kill fails the run.
/// <para>
/// The band (the Producer's default, D18 brief): 20 Age I line units (the 20 Heavy Infantry and 20 Raider rows) raze a
/// House in at most 20 s, an infantry hall in 60 s, a Town Hall in 2 min; and each siege group (Catapults, Sappers) does at
/// least 2x the 20 Heavy Infantry's damage per second per gold + wood spent against the same building. The scene follows
/// <see cref="CounterTriangleScene"/>'s (seed 7, blocks 5 wide, 1.8 m apart, a player-0 spotter past the building so the
/// fog keeps the order), with the block's front rank 10 m from the building's west side, every attacker ordered to Attack
/// it. Asserted: every run ran, and today meets every band cell (12 / 12); the seconds are for the owner. The siege ratio
/// is also printed against the 20 Raiders (BUG-0721): since the surround rule the Catapults fall under 2x the Raider's damage
/// per cost on a hall and a Town Hall; those two cells are pinned as measured (BUG-0780), not counted in the band.
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

    /// <summary>The building armor D18 shipped, by slot (halving rounded down can't be undone, so it is listed).</summary>
    public static int ArmorBeforeD19(string slot) => slot switch
    {
        "town_hall" or "watch_tower" => 5,
        "house" or "camp" => 3,
        _ => 4,
    };

    /// <summary>The shipped data with D18's building damage put back: Structure melee 0.4, pierce 0.2, magic 0.4, the armor of <see cref="ArmorBeforeD19"/>.</summary>
    public static GameData BeforeD19()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/damage_table.json", root =>
        {
            foreach (JsonNode? t in root["damageTypes"]!.AsArray())
            {
                double? to = (string)t!["id"]! switch { "melee" => 0.4, "pierce" => 0.2, "magic" => 0.4, _ => null };
                if (to != null) t["multipliers"]!["structure"] = to.Value;
            }
        });
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/buildings.json", root =>
            {
                foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["armor"] = ArmorBeforeD19((string)b["slot"]!);
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
        // BUG-0761 (rule 24): a House trains nothing, so player 1 with no unit would be out on tick 0 and the House collapse
        // (no attacker involved). Placed after the attackers so their unit slots, and so the measured cells, don't shift.
        TestSim.KeepIn(sim, 1);
        UnitStore u = w.Units;
        var struck = new bool[n];
        var before = new int[n];
        int t = 0;
        int hpBefore = w.Buildings.Hp[b.Index];
        bool razedByAttackers = false;
        while (t < MaxTicks && w.Buildings.IsAlive(b))
        {
            for (int k = 0; k < n; k++) before[k] = u.IsAlive(attackers[k]) ? u.CooldownTicks[attackers[k].Index] : 0;
            hpBefore = w.Buildings.Hp[b.Index];
            sim.Tick();
            t++;
            if (!w.Buildings.IsAlive(b))
                foreach (DeathEvent d in w.Deaths)
                    if (d.IsBuilding && d.Victim == b) razedByAttackers = !d.Collapsed && d.KillerOwner == 0;
            if (t > StruckWithin) continue;
            // A swing or shot starts the cooldown, so a cooldown that went up this tick is a strike begun.
            for (int k = 0; k < n; k++)
                if (u.IsAlive(attackers[k]) && u.CooldownTicks[attackers[k].Index] > before[k]) struck[k] = true;
        }
        bool alive = w.Buildings.IsAlive(b);
        if (!alive)
        {
            // BUG-0761: a fall that isn't the attackers' kill (a collapse, say) must fail, never print as a razing.
            Assert.True(razedByAttackers, $"{attackerKey} v {bd.Key}: the building fell on tick {t} but not to player 0's attackers");
            Assert.True(hpBefore < bd.Hp, $"{attackerKey} v {bd.Key}: the building fell on tick {t} at full hp, never struck");
            Assert.True(struck.Any(s => s), $"{attackerKey} v {bd.Key}: razed on tick {t} with no attacker having struck");
        }
        return new Run(alive ? int.MaxValue : t, alive ? w.Buildings.Hp[b.Index] : 0, struck.Count(s => s), n,
            n * CounterTriangleScene.Cost(data.Units[type]), bd.Hp);
    }

    private static string Seconds(Run r) => r.Ticks == int.MaxValue
        ? string.Create(CultureInfo.InvariantCulture, $"> {MaxTicks / Tps} s ({100.0 * r.HpLeft / r.BuildingHp:F0} % hp left)")
        : string.Create(CultureInfo.InvariantCulture, $"{(double)r.Ticks / Tps:F1} s");

    /// <summary>Hit points razed a second per 1,000 gold + wood (a building that stood counts what it lost).</summary>
    private static double RatePerCost(Run r) =>
        (r.BuildingHp - r.HpLeft) / (Math.Min(r.Ticks, MaxTicks) / (double)Tps) / r.Cost * 1000.0;

    /// <summary>The "before D19" copy really holds D18's values, and the shipped data is set (C) of them: half the armor, the new Structure column.</summary>
    [Fact]
    public void BeforeD19_HoldsD18sValues_AndTheShippedDataIsSetC()
    {
        GameData before = BeforeD19(), today = TestSim.Data;
        Assert.Equal(today.Buildings.Length, before.Buildings.Length);
        for (int b = 0; b < today.Buildings.Length; b++)
        {
            Assert.InRange(before.Buildings[b].Armor, 3, 5);
            Assert.Equal(before.Buildings[b].Armor / 2, today.Buildings[b].Armor);
        }
        foreach ((string type, float was, float now) in new[] { ("melee", 0.4f, 0.7f), ("pierce", 0.2f, 0.35f), ("magic", 0.4f, 0.6f), ("siege", 3.0f, 3.0f) })
        {
            DamageTable bt = before.DamageTable, tt = today.DamageTable;
            Assert.Equal(was, bt.Multiplier(bt.DamageTypeKeys.IndexOf(type), bt.ArmorClassKeys.IndexOf("structure")));
            Assert.Equal(now, tt.Multiplier(tt.DamageTypeKeys.IndexOf(type), tt.ArmorClassKeys.IndexOf("structure")));
        }
    }

    [Fact]
    public void AttackerGroups_v_Buildings_TodayAndBeforeD19_Report()
    {
        var variants = new (string Name, GameData Data)[]
        {
            ("today (D19, set C shipped)", TestSim.Data),
            ("before D19 (Structure 0.4 / 0.2 / 0.4, armor 5 / 3 / 4; D18's data on today's movement)", BeforeD19()),
        };
        var runs = new Run[variants.Length, Groups.Length, Targets.Length];
        var met = new int[variants.Length];
        var raiderMisses = new List<string>();
        int cells = 0;
        _out.WriteLine("D18 report 4, re-based in D19 (2026-10-15): seconds to raze a lone building (no defenders, flat, combat on); [struck within 10 s]; band check where one applies");
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
                        // Against both line rows (BUG-0721 item 1): the Raider, cheaper and faster, is the harder bar. The band
                        // cell is the Heavy Infantry's (the D18 brief's wording); the Raider side is counted and pinned apart.
                        double vsHi = RatePerCost(r) / RatePerCost(runs[v, 0, x]);
                        double vsRaider = RatePerCost(r) / RatePerCost(runs[v, 1, x]);
                        cell += string.Create(CultureInfo.InvariantCulture, $" {vsHi:F1}x HI / {vsRaider:F1}x Raider per cost");
                        ok = vsHi >= 2.0;
                        if (vsRaider < 2.0)
                        {
                            cell += " (MISS vs Raider)";
                            if (v == 0) raiderMisses.Add($"{Groups[g].Key} {Targets[x].Name}");
                        }
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

        _out.WriteLine("");
        _out.WriteLine($"| Attackers | " + string.Join(" | ", Targets.Select(x => x.Name + " today / before")) + " |");
        _out.WriteLine("| --- | --- | --- | --- |");
        for (int g = 0; g < Groups.Length; g++)
            _out.WriteLine($"| {Groups[g].Count} {Groups[g].Key} | " +
                string.Join(" | ", Enumerable.Range(0, Targets.Length).Select(x => $"{Seconds(runs[0, g, x])} / {Seconds(runs[1, g, x])}")) + " |");
        Assert.Equal(12, cells);
        Assert.True(met[0] == cells, $"the shipped data meets {met[0]} / {cells} band cells, D19 shipped set (C) for {cells} / {cells}");
        // Measured 2026-10-16 (D19 + the surround rule, D-H5): against the Raider row the Catapults reach 2.8x on a House but
        // 1.7x / 1.6x on a hall / Town Hall; the Sappers clear 2x everywhere. Pinned, not loosened: BUG-0780 holds the call.
        _out.WriteLine($"siege cells under 2x the Raider's damage per cost (today): {(raiderMisses.Count == 0 ? "none" : string.Join("; ", raiderMisses))}");
        Assert.Equal(new[] { "malazan_catapult Infantry hall", "malazan_catapult Town Hall" }, raiderMisses);
    }
}
