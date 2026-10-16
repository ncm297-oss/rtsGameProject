using System.Globalization;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;
using static Rts.Sim.Tests.Scenario.CounterTriangleScene;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D18 report 5 (D13's open end: the Line v Shock counter rests on the line's hp, armor and cost): with the line units'
/// <c>bonusVs.mounted</c> back at ×1.5 (the owner's decision of 2026-10-14, shipped by D19) in in-memory copies of the data, the line
/// units' hp (100-140 step 10), armor (1-3) and gold cost (50-80 step 10) are swept one at a time and in pairs, the same
/// value on both line units (Heavy Infantry, Raider), and each set runs the four Line v Shock match-ups of the counter
/// triangle (Heavy Infantry v Horse Raider, Raider v Wickan Lancer, both seats). The band is docs/factions' "Balance
/// baseline" one: the winner keeps 40-65 % of its cost in both seats, the seats at most 15 points apart. Each line unit
/// meets only its own pair, so the report picks the best set per line unit.
/// <para>
/// The fight is <see cref="Scenario.CounterTriangleScene.Fight"/>'s with the data as a parameter (the shared one reads the
/// shipped data only); a precondition pins it to the shared one on the shipped data in all four match-ups, so a scene
/// change fails here instead of drifting (BUG-0230 item 2). Line v Ranged (no counter, so no band) and Shock v Ranged
/// (no line unit fights in it) are printed for today and the best sets. Only the preconditions are asserted.
/// </para>
/// </summary>
public class LineShockSweepReportTests
{
    private const string Hi = "malazan_heavy_infantry", Raider = "whirlwind_raider";

    private static readonly (string Line, string Shock)[] LineShock = { (Hi, "whirlwind_horse_raider"), (Raider, "malazan_wickan_lancer") };
    private static readonly (string A, string B)[] Others =
    {
        (Hi, "whirlwind_desert_archer"), (Raider, "malazan_crossbowman"),
        ("malazan_wickan_lancer", "whirlwind_desert_archer"), ("whirlwind_horse_raider", "malazan_crossbowman"),
    };

    private readonly ITestOutputHelper _out;

    public LineShockSweepReportTests(ITestOutputHelper output) => _out = output;

    /// <summary><see cref="Scenario.CounterTriangleScene.Fight"/> on <paramref name="data"/>, step for step.</summary>
    private static (Side A, Side B, int Ticks) FightWith(GameData data, string keyA, string keyB, int seatA)
    {
        int typeA = data.FindUnit(keyA), typeB = data.FindUnit(keyB);
        int nA = CountFor(data, typeA), nB = CountFor(data, typeB);
        int units = nA + nB + 4;
        var sim = new Simulation(new SimConfig(Seed, 2, units, 8 * units + 32) { Data = data }, LocalMovementTests.Flat(MapSize));
        UnitStore u = sim.World.Units;
        float half = MapSize * Map.MapConstants.CellSize / 2f;
        var center = new Vector2(half, half);
        var mid = new Vector2[2];
        var handles = new List<EntityHandle>[] { new(), new() };
        int[] seats = { seatA, 1 - seatA }, types = { typeA, typeB }, counts = { nA, nB };
        int[] order = seatA == 0 ? new[] { 0, 1 } : new[] { 1, 0 };
        foreach (int side in order)
        {
            float dir = (side == 0) == (seatA == 0) ? -1f : 1f;
            int ranks = (counts[side] + BlockWidth - 1) / BlockWidth;
            mid[side] = center + new Vector2(dir * (FrontOffset + (ranks - 1) * Spacing / 2f), 0f);
            for (int k = 0; k < counts[side]; k++)
            {
                int rank = k / BlockWidth, file = k % BlockWidth;
                var at = center + new Vector2(dir * (FrontOffset + rank * Spacing), (file - (BlockWidth - 1) / 2f) * Spacing);
                handles[side].Add(Place(sim, seats[side], types[side], at));
            }
        }
        foreach (int side in order)
            foreach (EntityHandle h in handles[side])
                sim.Enqueue(Command.AttackMove(seats[side], h, mid[1 - side]));
        int ticks = RunUntil(sim, () => !handles[0].Any(u.IsAlive) || !handles[1].Any(u.IsAlive), MaxTicks);
        Side Result(int side) => new(new[] { keyA, keyB }[side], counts[side], counts[side] * Cost(data.Units[types[side]]),
            handles[side].Count(u.IsAlive), handles[side].Where(u.IsAlive).Sum(h => u.Hp[h.Index]),
            handles[side].Count(u.IsAlive) * Cost(data.Units[types[side]]));
        return (Result(0), Result(1), ticks);
    }

    /// <summary>A's share of its cost kept, as the page prints it; negative (minus B's share) when B wins.</summary>
    private static int Keeps(Side a, Side b) => a.Left > 0
        ? (int)Math.Round(100.0 * a.CostLeft / a.FieldedCost, MidpointRounding.AwayFromZero)
        : -(int)Math.Round(100.0 * b.CostLeft / b.FieldedCost, MidpointRounding.AwayFromZero);

    private static string Cell(int keeps) => keeps >= 0 ? $"{keeps} %" : $"loses ({-keeps} %)";

    /// <summary>Points outside the band over both seats (a loss counts 40 + the winner's share), plus the seat gap over 15.</summary>
    private static int Distance(int seat0, int seat1)
    {
        static int D(int k) => k < 0 ? 40 - k : Math.Max(0, k - 65) + Math.Max(0, 40 - k);
        return D(seat0) + D(seat1) + Math.Max(0, Math.Abs(seat0 - seat1) - 15);
    }

    private static GameData With(int? hp, int? armor, int? gold)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        foreach ((string faction, string id) in new[] { ("malazan", Hi), ("whirlwind", Raider) })
        {
            dir.SetUnitField(faction, id, "attack.bonusVs.mounted", "1.5");
            if (hp is int h) dir.SetUnitField(faction, id, "hp", h.ToString(CultureInfo.InvariantCulture));
            if (armor is int a) dir.SetUnitField(faction, id, "armor", a.ToString(CultureInfo.InvariantCulture));
            if (gold is int g) dir.SetUnitField(faction, id, "cost.gold", g.ToString(CultureInfo.InvariantCulture));
        }
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    private static int[] Cells(GameData data) => LineShock
        .SelectMany(p => new[] { 0, 1 }.Select(seat => { (Side l, Side s, _) = FightWith(data, p.Line, p.Shock, seat); return Keeps(l, s); }))
        .ToArray();

    [Fact]
    public void LineStats_Sweep_WithBonusX15_Report()
    {
        // Precondition: the local fight is the shared scene's on the shipped data.
        foreach ((string line, string shock) in LineShock)
            for (int seat = 0; seat < 2; seat++)
                Assert.Equal(Fight(line, shock, seat), FightWith(TestSim.Data, line, shock, seat));

        var sets = new List<(string Name, int? Hp, int? Armor, int? Gold)> { ("x1.5 alone (the shipped hp / armor / cost)", null, null, null) };
        int[] hps = { 100, 110, 120, 130, 140 }, armors = { 1, 2, 3 }, golds = { 50, 60, 70, 80 };
        foreach (int h in hps) sets.Add(($"hp {h}", h, null, null));
        foreach (int a in armors) sets.Add(($"armor {a}", null, a, null));
        foreach (int g in golds) sets.Add(($"gold {g}", null, null, g));
        foreach (int h in hps) foreach (int a in armors) sets.Add(($"hp {h}, armor {a}", h, a, null));
        foreach (int h in hps) foreach (int g in golds) sets.Add(($"hp {h}, gold {g}", h, null, g));
        foreach (int a in armors) foreach (int g in golds) sets.Add(($"armor {a}, gold {g}", null, a, g));

        int[] today = Cells(TestSim.Data);
        _out.WriteLine("D18 report 5, 2026-10-14: Line v Shock, the line unit's share of its cost kept (band 40-65 %, seats <= 15 apart)");
        _out.WriteLine("Shipped: Heavy Infantry 130 hp / armor 3 / 54 g, Raider 108 hp / armor 1 / 48 g, both x1.5 v Mounted since D19 (x1.1 before); every set below has x1.5");
        _out.WriteLine("");
        _out.WriteLine("| Set (both line units) | HI v Horse Raider seat 0 | seat 1 | off band | Raider v Lancer seat 0 | seat 1 | off band |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        _out.WriteLine(Row("shipped (x1.5 since D19)", today));
        var results = new List<(string Name, int[] Cells, GameData Data)>();
        foreach ((string name, int? hp, int? armor, int? gold) in sets)
        {
            GameData data = With(hp, armor, gold);
            int[] c = Cells(data);
            results.Add((name, c, data));
            _out.WriteLine(Row(name, c));
        }

        _out.WriteLine("");
        int[] alone = results[0].Cells;
        _out.WriteLine(alone.SequenceEqual(today)
            ? "x1.5 alone changed none of the four cells."
            : $"x1.5 alone moved the cells from {string.Join(" / ", today.Select(Cell))} to {string.Join(" / ", alone.Select(Cell))}.");
        for (int p = 0; p < LineShock.Length; p++)
        {
            var best = results.OrderBy(r => Distance(r.Cells[2 * p], r.Cells[2 * p + 1])).ThenBy(r => results.IndexOf(r)).First();
            int d = Distance(best.Cells[2 * p], best.Cells[2 * p + 1]);
            string unit = TestSim.Data.Units[TestSim.Data.FindUnit(LineShock[p].Line)].DisplayName;
            _out.WriteLine($"{unit}: {(d == 0 ? "in the band with" : $"no set lands in the band; nearest ({d} points off)")}: {best.Name} " +
                $"({Cell(best.Cells[2 * p])} / {Cell(best.Cells[2 * p + 1])}); lever that moves it most: {Strongest(results, p)}");
            Others_(best.Name, best.Data);
        }
        Others_("shipped", TestSim.Data);
    }

    private static string Row(string name, int[] c) =>
        $"| {name} | {Cell(c[0])} | {Cell(c[1])} | {Distance(c[0], c[1])} | {Cell(c[2])} | {Cell(c[3])} | {Distance(c[2], c[3])} |";

    /// <summary>The single lever whose sweep spans the widest range of pair <paramref name="p"/>'s off-band points.</summary>
    private static string Strongest(List<(string Name, int[] Cells, GameData Data)> results, int p)
    {
        string best = "";
        int span = -1;
        foreach (string lever in new[] { "hp ", "armor ", "gold " })
        {
            int[] ds = results.Where(r => r.Name.StartsWith(lever, StringComparison.Ordinal) && !r.Name.Contains(',', StringComparison.Ordinal))
                .Select(r => Distance(r.Cells[2 * p], r.Cells[2 * p + 1])).ToArray();
            if (ds.Max() - ds.Min() > span) (span, best) = (ds.Max() - ds.Min(), $"{lever.Trim()} ({ds.Min()}-{ds.Max()} points off over its sweep)");
        }
        return best;
    }

    private void Others_(string name, GameData data)
    {
        var cells = new List<string>();
        foreach ((string a, string b) in Others)
            for (int seat = 0; seat < 2; seat++)
            {
                (Side x, Side y, _) = FightWith(data, a, b, seat);
                cells.Add($"{data.Units[data.FindUnit(a)].DisplayName} v {data.Units[data.FindUnit(b)].DisplayName} seat {seat}: {Cell(Keeps(x, y))}");
            }
        _out.WriteLine($"  others under {name}: {string.Join("; ", cells)}");
    }
}
