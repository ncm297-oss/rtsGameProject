using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D18's reports (session 2026-10-14-0946): the preconditions the report tables rest on. The lever edits of
/// <c>BuildingKillTimeReportTests</c> and <c>LineShockSweepReportTests</c> really change the loaded data and never touch
/// the shipped files; the menu defaults the trees and spending reports copy as constants still match the game scripts;
/// and the trees table's per-start count holds under the game's own seated config, by an integer hand count.
/// </summary>
public class D18ReportPreconditionQaTests
{
    private readonly ITestOutputHelper _out;

    public D18ReportPreconditionQaTests(ITestOutputHelper output) => _out = output;

    private static Dictionary<string, string> ShippedDigests()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string f in Directory.GetFiles(TestDataDir.Shipped, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            d[Path.GetRelativePath(TestDataDir.Shipped, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        return d;
    }

    /// <summary>The D18 lever edits (A, B, the x1.5 sweep's fields), applied the reports' way, take effect in the loaded copy and leave every shipped file byte-identical.</summary>
    [Fact]
    public void D18LeverEdits_ChangeTheLoadedCopy_AndLeaveShippedFilesByteIdentical()
    {
        Dictionary<string, string> before = ShippedDigests();
        ulong shippedHash = TestSim.Data.ContentHash();
        string tempPath;
        GameData edited;
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            tempPath = dir.Path;
            dir.EditJson("common/damage_table.json", root =>
            {
                foreach (JsonNode? t in root["damageTypes"]!.AsArray())
                {
                    double? to = (string)t!["id"]! switch { "melee" => 0.7, "pierce" => 0.35, "magic" => 0.6, _ => null };
                    if (to != null) t["multipliers"]!["structure"] = to.Value;
                }
            });
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/buildings.json", root =>
                {
                    foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["armor"] = (int)b["armor"]! / 2;
                });
            foreach ((string faction, string id) in new[] { ("malazan", "malazan_heavy_infantry"), ("whirlwind", "whirlwind_raider") })
            {
                dir.SetUnitField(faction, id, "attack.bonusVs.mounted", "1.5");
                dir.SetUnitField(faction, id, "hp", "120");
                dir.SetUnitField(faction, id, "armor", "2");
                dir.SetUnitField(faction, id, "cost.gold", "60");
            }
            DataLoadResult r = DataLoader.LoadAll(dir.Path);
            Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
            edited = r.Data!;
        }

        // The edits took (a mistyped key would add a field and leave the lever a no-op).
        DamageTable dt = edited.DamageTable;
        int structure = dt.ArmorClassKeys.IndexOf("structure");
        Assert.Equal(0.7f, dt.Multiplier(dt.DamageTypeKeys.IndexOf("melee"), structure));
        Assert.Equal(0.35f, dt.Multiplier(dt.DamageTypeKeys.IndexOf("pierce"), structure));
        Assert.Equal(0.6f, dt.Multiplier(dt.DamageTypeKeys.IndexOf("magic"), structure));
        Assert.Equal(3.0f, dt.Multiplier(dt.DamageTypeKeys.IndexOf("siege"), structure));
        for (int b = 0; b < edited.Buildings.Length; b++)
            Assert.Equal(TestSim.Data.Buildings[b].Armor / 2, edited.Buildings[b].Armor);
        foreach (string id in new[] { "malazan_heavy_infantry", "whirlwind_raider" })
        {
            UnitDef u = edited.Units[edited.FindUnit(id)];
            Assert.Equal(120, u.Hp);
            Assert.Equal(2, u.Armor);
            Assert.Equal(60, u.CostGold);
        }
        Assert.NotEqual(shippedHash, edited.ContentHash());

        // The shipped files and the shipped data are untouched; the copy is gone.
        Assert.Equal(before, ShippedDigests());
        Assert.Equal(shippedHash, TestSim.Data.ContentHash());
        Assert.Equal(shippedHash, DataLoader.LoadAll(TestDataDir.Shipped).Data!.ContentHash());
        Assert.False(Directory.Exists(tempPath), $"temp copy {tempPath} left behind");
    }

    /// <summary>The menu defaults the D18 reports copy as constants (seed 1, the 128 map, 12 forests, 8 mines) are the game scripts' values.</summary>
    [Fact]
    public void D18Reports_MenuConstants_MatchTheGameScripts()
    {
        string scripts = Path.Combine(TestDataDir.RepoRoot(), "game", "scripts");
        string launch = File.ReadAllText(Path.Combine(scripts, "LaunchOptions.cs"));
        string setup = File.ReadAllText(Path.Combine(scripts, "SkirmishSetup.cs"));
        Assert.Matches(new Regex(@"public const ulong DefaultSeed = 1;"), setup);
        Assert.Matches(new Regex(@"public const int DefaultForests = 12;"), launch);
        Assert.Matches(new Regex(@"public const int DefaultMines = 8;"), launch);
        Assert.Matches(new Regex(@"MapSizes = \{ 128,"), launch);
        Assert.Equal(128, MapGenParams.Default.Width);
        Assert.Equal(128, MapGenParams.Default.Height);
    }

    /// <summary>
    /// The trees table's seed-1 rows by a different route: the game's seated config (SimRunner's: player 1 a Normal AI),
    /// an integer squared cell distance instead of float metres, equal to the bare config the report uses.
    /// </summary>
    [Fact]
    public void TreesNearStart_Seed1_HandCount_SameInTheGamesSeatedConfig()
    {
        var map = MapGenParams.Default with { Forests = 12, GoldMines = 8 };
        GameData data = TestSim.Data;
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal));
        var game = new Simulation(new SimConfig(1, 2, 400, 1024) { Data = data, Map = map, Seats = seats });
        var bare = new Simulation(TestSim.Config(1, 2, 16, 64) with { Map = map });
        int tree = data.FindResource("tree");
        float cell = MapConstants.CellSize;
        for (int p = 0; p < 2; p++)
        {
            (int n30, int n60, int nearest2) Count(Simulation sim)
            {
                World w = sim.World;
                int width = w.NavGrid.Width, c = w.StartLocations[p].Cell;
                int sx = c % width, sy = c / width, n30 = 0, n60 = 0, nearest2 = int.MaxValue;
                var r = w.Resources;
                for (int k = 0; k < r.Capacity; k++)
                {
                    if (!r.Alive[k] || r.TypeId[k] != tree) continue;
                    int dx = r.Cell[k] % width - sx, dy = r.Cell[k] / width - sy, d2 = dx * dx + dy * dy;
                    nearest2 = Math.Min(nearest2, d2);
                    if (d2 * cell * cell <= 30f * 30f) n30++;
                    if (d2 * cell * cell <= 60f * 60f) n60++;
                }
                return (n30, n60, nearest2);
            }
            Assert.Equal(bare.World.StartLocations[p].Cell, game.World.StartLocations[p].Cell);
            Assert.Equal(Count(bare), Count(game));
            (int a30, int a60, int near2) = Count(game);
            _out.WriteLine($"seed 1 start {p}: trees <= 30 m {a30}, <= 60 m {a60}, nearest {MathF.Sqrt(near2) * cell:F1} m");
        }
    }
}
