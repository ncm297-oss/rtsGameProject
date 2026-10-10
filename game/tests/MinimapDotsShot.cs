using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>Minimap dot readability capture (BUG-0064): a match plus one lone Whirlwind unit on a ramp cell and one lone Malazan unit beside a cliff lip, saved as PNGs.</summary>
/// <remarks>
/// Windowed only (headless has no framebuffer). Run:
/// <c>&amp; $env:GODOT --path game res://tests/MinimapDotsShot.tscn -- --units 100 --out C:/tmp/dots.png</c>.
/// Writes the viewport to the --out path and the minimap enlarged 4x (nearest) next to it as
/// <c>*-minimap.png</c>, prints both lone units' cells and minimap pixels, then quits. A look tool,
/// not a pass/fail test: the PNGs are for a human to inspect.
/// </remarks>
public partial class MinimapDotsShot : Node
{
    private const int FramesBeforeShot = 90;

    private Match _match = null!;
    private Simulation _sim = null!;
    private string _out = "user://minimap-dots.png";
    private int _frames;
    private NVector2 _ramp, _lip;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string units = "100";
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--units") units = args[i + 1];
            else if (args[i] == "--out") _out = args[i + 1];
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        GameData data = loaded.Data!;
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(data, LaunchOptions.Parse(new[] { "--units", units }));
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;

        NavGrid grid = _sim.World.NavGrid;
        Heightmap map = _sim.World.Heightmap;
        // Away from the start blocks (around the map centre), so each unit really is alone.
        (int rx, int ry) = Nearest(map.Width * 3 / 4, map.Height * 3 / 5, (x, y) => map.IsRamp(x, y) && grid.IsPassable(x, y), -1, -1);
        (int cx, int cy) = Nearest(map.Width * 2 / 5, map.Height * 4 / 5, (x, y) => grid.IsPassable(x, y) && NextToCliff(grid, x, y), rx, ry);
        _ramp = grid.CellCenter(rx, ry);
        _lip = grid.CellCenter(cx, cy);
        // Needs two free slots: at the 2,000 default capacity use --units 999 for the large shot.
        _sim.Enqueue(Command.SpawnUnit(1, data.Factions[1].Units[0], _ramp));
        _sim.Enqueue(Command.SpawnUnit(0, data.Factions[0].Units[0], _lip));
        GD.Print($"lone faction-1 unit on ramp cell ({rx}, {ry}); lone faction-0 unit beside a cliff lip at ({cx}, {cy})");
    }

    public override void _Process(double delta)
    {
        if (++_frames != FramesBeforeShot) return;
        Image shot = GetViewport().GetTexture().GetImage();
        string full = ProjectSettings.GlobalizePath(_out);
        shot.SavePng(full);
        var mini = _match.GetNode<Minimap>("Hud/Minimap");
        Rect2 r = mini.GetGlobalRect();
        Image crop = shot.GetRegion(new Rect2I((Vector2I)r.Position, (Vector2I)r.Size));
        crop.Resize(crop.GetWidth() * 4, crop.GetHeight() * 4, Image.Interpolation.Nearest);
        string cropPath = full.Replace(".png", "-minimap.png");
        crop.SavePng(cropPath);
        GD.Print($"saved {full} and {cropPath} (minimap at {r.Position}, {r.Size}; {_sim.World.Units.Count} units)");
        MinimapTransformPrint(mini);
        SceneExit.Quit(this, 0);
    }

    // Prints where each lone unit's dot sits in the 4x crop, to find it when looking.
    private void MinimapTransformPrint(Minimap mini)
    {
        var u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (NVector2.Distance(u.Position[i], _ramp) > 2f && NVector2.Distance(u.Position[i], _lip) > 2f) continue;
            NVector2 px = mini.Fit.ToPixel(u.Position[i]);
            GD.Print($"lone unit slot {i} owner {u.Owner[i]} at {u.Position[i]}: crop pixel ({px.X * 4:F0}, {px.Y * 4:F0})");
        }
    }

    private static bool NextToCliff(NavGrid g, int x, int y) =>
        (g.FlagsAt(x + 1, y) & NavFlags.Cliff) != 0 || (g.FlagsAt(x - 1, y) & NavFlags.Cliff) != 0
        || (g.FlagsAt(x, y + 1) & NavFlags.Cliff) != 0 || (g.FlagsAt(x, y - 1) & NavFlags.Cliff) != 0;

    // The matching cell nearest (ox, oy), at least 12 cells from (ax, ay) so the two dots don't touch.
    private static (int, int) Nearest(int ox, int oy, Func<int, int, bool> match, int ax, int ay)
    {
        for (int r = 0; r < 64; r++)
            for (int y = oy - r; y <= oy + r; y++)
                for (int x = ox - r; x <= ox + r; x++)
                {
                    if (Math.Max(Math.Abs(x - ox), Math.Abs(y - oy)) != r) continue;
                    if (ax >= 0 && Math.Max(Math.Abs(x - ax), Math.Abs(y - ay)) < 12) continue;
                    if (match(x, y)) return (x, y);
                }
        throw new InvalidOperationException("no matching cell near the search origin");
    }
}
