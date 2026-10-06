using System.Numerics;
using System.Text.RegularExpressions;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-3b read-only proof: props, minimap (resources + dots) and unit-view reads every frame never change the state hash, on the Match's map with 2,000 units.</summary>
public class PropsHashTwinTests
{
    private static Simulation Spawned(ulong seed, int perPlayer)
    {
        Simulation sim = PropLayoutTests.MatchSim(seed, units: 2000);
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        for (int p = 0; p < 2; p++)
        {
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) sim.Enqueue(Command.SpawnUnit(p, k % TestSim.UnitTypeCount, spots[k]));
        }
        return sim;
    }

    [Fact]
    public void ViewReadsEveryFrame_HashEqualsABareTwin_EveryTick_600Ticks_2000Units()
    {
        Simulation a = Spawned(1, 1000), b = Spawned(1, 1000);
        World w = a.World;
        UnitStore u = w.Units;
        ResourceStore r = w.Resources;
        Assert.True(w.ResourcePlacement.Forests > 0 && w.ResourcePlacement.Mines > 0);
        var props = new PropLayout(w.Data.Resources, r.Capacity);
        var mini = new MinimapRaster(w.Heightmap, w.NavGrid, new uint[] { 0x4B4F55, 0xC8892E }, u.Capacity);
        // Tree slots to fell mid-run (in both twins), so the views relist while units walk.
        int[] fell = Enumerable.Range(0, r.Capacity).Where(i => r.Alive[i] && r.TypeId[i] == ResourceMaps.Tree).Take(3).ToArray();
        Assert.Equal(3, fell.Length);
        int[] mines = Enumerable.Range(0, r.Capacity).Where(i => r.Alive[i] && r.TypeId[i] == ResourceMaps.Mine).ToArray();
        long sink = 0;

        for (int tick = 0; tick < 600; tick++)
        {
            // Every 150 ticks both armies march to a different mine: across the map, round forests and mines.
            if (tick % 150 == 2)
            {
                int m = mines[tick / 150 % mines.Length];
                Vector2 goal = w.NavGrid.CellCenter(r.Cell[m] % w.NavGrid.Width, r.Cell[m] / w.NavGrid.Width);
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    Command cmd = Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), goal);
                    a.Enqueue(cmd);
                    b.Enqueue(cmd);
                }
            }
            if (tick is 200 or 300 or 400)
            {
                int slot = fell[tick / 100 - 2];
                Assert.True(a.World.Resources.Take(a.World.Resources.HandleOf(slot), int.MaxValue) > 0);
                Assert.True(b.World.Resources.Take(b.World.Resources.HandleOf(slot), int.MaxValue) > 0);
            }

            ulong before = a.StateHash();
            for (int frame = 0; frame < 3; frame++)
            {
                props.Refresh(w.Heightmap, w.NavGrid, r.Alive, r.TypeId, r.Cell);
                mini.DrawResources(w.Data.Resources, w.NavGrid.Version, r.Alive, r.TypeId, r.Cell);
                sink += mini.DrawDots(u.Alive, u.Position, u.Owner);
                // What UnitViews reads: interpolated ground point, terrain height, facing.
                ReadOnlySpan<bool> alive = u.Alive;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!alive[i]) continue;
                    Vector2 p = Vector2.Lerp(u.PrevPosition[i], u.Position[i], frame / 3f);
                    sink += (long)(TerrainHeight.At(w.Heightmap, p.X, p.Y) + u.Facing[i]);
                }
            }
            Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(b.StateHash() == a.StateHash(), $"tick {a.TickNumber}: view-read sim diverged from its twin");
        }
        Assert.Equal(2000, u.Count);
        Assert.True(sink != 0);
        // One fill at the start, one per felled tree; nothing else changed passability.
        Assert.Equal(4, props.Rebuilds);
        Assert.Equal(4, mini.ResourceDraws);
        Assert.Equal(w.ResourcePlacement.Trees - 3, props.CountOf(ResourceMaps.Tree));
    }

    [Fact]
    public void ViewApiSource_NeverMutatesTheResourceStoreOrGrid_AndOnlyFlowArrowsTouchTheFieldCache()
    {
        string dir = Path.Combine(RepoRoot(), "sim", "Rts.Sim", "ViewApi");
        string[] forbidden =
        {
            @"\.Take\s*\(", @"\.Spawn\s*\(", @"\bResourceStore\b", @"\bSetResource\b", @"\bClearResource\b", @"\bBumpVersion",
            @"\bTryGetCached\b", @"\.Get\s*\(",
        };
        var hits = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//")) continue;
                foreach (string p in forbidden)
                    if (Regex.IsMatch(line, p)) hits.Add($"{name}:{i + 1}: {p}: {line}");
                // The M2-5 flow-arrow layer peeks the cache; nothing else in ViewApi may reach it.
                if (name != "FlowArrowLayout.cs" && Regex.IsMatch(line, @"\bFlowFieldCache\b|\bFlowFields\b"))
                    hits.Add($"{name}:{i + 1}: field cache: {line}");
            }
        }
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RtsGame.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("RtsGame.sln not found");
    }
}
