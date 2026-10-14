using System.Globalization;
using System.Numerics;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D18 report 3 (the owner's playtest note 2: is the default seed short of trees?): for seeds 1-7 of the 128 map with the
/// game's defaults (<c>game/scripts/LaunchOptions.DefaultForests</c> 12 and <c>DefaultMines</c> 8, the generator's other
/// defaults, two start locations), per start location: the trees within 30 m and 60 m of its centre cell, the wood in them,
/// and the nearest tree's distance. The menu's match is seed <see cref="MenuSeed"/> on the 128 map
/// (<c>game/scripts/SkirmishSetup.DefaultSeed</c> 1, the size list's first item <c>LaunchOptions.MapSizes[0]</c> 128; read
/// 2026-10-14). Only that each map has its two start locations and some trees is asserted: the table is for the owner.
/// </summary>
public class TreesNearBasesReportTests
{
    private const int MenuForests = 12;
    private const int MenuMines = 8;
    private const ulong MenuSeed = 1;

    private readonly ITestOutputHelper _out;

    public TreesNearBasesReportTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Seeds1To7_TreesNearEachStart_Report()
    {
        var map = MapGenParams.Default with { Forests = MenuForests, GoldMines = MenuMines };
        int tree = TestSim.Data.FindResource("tree");
        _out.WriteLine($"128 map, {MenuForests} forests, {MenuMines} mines, the generator's defaults (2026-10-14, shipped data); the menu's default match is seed {MenuSeed}");
        _out.WriteLine("");
        _out.WriteLine("| Seed | Start | Centre cell (x, y) | Trees <= 30 m | Wood <= 30 m | Trees <= 60 m | Wood <= 60 m | Nearest tree | Trees on the map |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        for (ulong seed = 1; seed <= 7; seed++)
        {
            var sim = new Simulation(TestSim.Config(seed, 2, 16, 64) with { Map = map });
            World w = sim.World;
            NavGrid g = w.NavGrid;
            var r = w.Resources;
            Assert.Equal(2, w.StartLocations.Length);
            int total = 0;
            for (int k = 0; k < r.Capacity; k++) if (r.Alive[k] && r.TypeId[k] == tree) total++;
            Assert.True(total > 0, $"seed {seed}: no trees");
            for (int p = 0; p < w.StartLocations.Length; p++)
            {
                int c = w.StartLocations[p].Cell;
                Vector2 at = g.CellCenter(c % g.Width, c / g.Width);
                int n30 = 0, n60 = 0, w30 = 0, w60 = 0;
                float nearest = float.MaxValue;
                for (int k = 0; k < r.Capacity; k++)
                {
                    if (!r.Alive[k] || r.TypeId[k] != tree) continue;
                    float d = Vector2.Distance(at, g.CellCenter(r.Cell[k] % g.Width, r.Cell[k] / g.Width));
                    nearest = Math.Min(nearest, d);
                    if (d <= 30f) { n30++; w30 += r.Remaining[k]; }
                    if (d <= 60f) { n60++; w60 += r.Remaining[k]; }
                }
                string menu = seed == MenuSeed ? " (menu)" : "";
                _out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {seed}{menu} | {p} | ({c % g.Width}, {c / g.Width}) | {n30} | {w30} | {n60} | {w60} | {nearest:F1} m | {total} |"));
            }
        }
    }
}
