using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>QA (M2-H1) minimap readability capture: lone units of both factions on each worst-case cell kind (beside the border ring, beside a cliff lip, on a ramp, on the highest plateau), saved as PNGs.</summary>
/// <remarks>
/// Windowed only. Run: <c>&amp; $env:GODOT --path game res://tests/QaH1DotsShot.tscn -- --units 100 --out C:/tmp/qa-dots.png</c>.
/// Writes the frame, a 4x nearest crop of the minimap (<c>-minimap.png</c>), and prints each lone
/// unit's crop pixel. A look tool for QA, not a pass/fail test. Lone units need free slots: use
/// --units up to 994 at the 2,000-slot default.
/// </remarks>
public partial class QaH1DotsShot : Node
{
    private const int FramesBeforeShot = 90;
    private Match _match = null!;
    private Simulation _sim = null!;
    private string _out = "user://qa-dots.png";
    private int _frames;
    private readonly List<(string what, NVector2 at)> _lone = new();

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
        NavGrid g = _sim.World.NavGrid;
        Heightmap m = _sim.World.Heightmap;
        int top = 0;
        for (int y = 0; y < m.Height; y++) for (int x = 0; x < m.Width; x++) top = Math.Max(top, m.LevelAt(x, y));
        bool Border(int x, int y) => g.IsPassable(x, y) && (!g.IsPassable(x - 1, y) || !g.IsPassable(x + 1, y) || !g.IsPassable(x, y - 1) || !g.IsPassable(x, y + 1))
            && (x <= 2 || y <= 2 || x >= m.Width - 3 || y >= m.Height - 3);
        bool Lip(int x, int y) => g.IsPassable(x, y) && ((g.FlagsAt(x + 1, y) & NavFlags.Cliff) != 0 || (g.FlagsAt(x - 1, y) & NavFlags.Cliff) != 0
            || (g.FlagsAt(x, y + 1) & NavFlags.Cliff) != 0 || (g.FlagsAt(x, y - 1) & NavFlags.Cliff) != 0);
        bool Ramp(int x, int y) => g.IsPassable(x, y) && m.IsRamp(x, y);
        bool High(int x, int y) => g.IsPassable(x, y) && !m.IsRamp(x, y) && m.LevelAt(x, y) == top && !Lip(x, y);
        var taken = new List<(int, int)>();
        Place(data, 0, "Malazan beside border", Find(m, 3, m.Height / 2, Border, taken));
        Place(data, 1, "Whirlwind beside border", Find(m, m.Width - 4, m.Height / 3, Border, taken));
        Place(data, 0, "Malazan beside cliff lip", Find(m, m.Width / 5, m.Height / 5, Lip, taken));
        Place(data, 1, "Whirlwind beside cliff lip", Find(m, m.Width * 4 / 5, m.Height * 4 / 5, Lip, taken));
        Place(data, 0, "Malazan on ramp", Find(m, m.Width * 4 / 5, m.Height / 5, Ramp, taken));
        Place(data, 1, "Whirlwind on ramp", Find(m, m.Width / 5, m.Height * 4 / 5, Ramp, taken));
        Place(data, 0, $"Malazan on level {top}", Find(m, m.Width * 3 / 4, m.Height * 2 / 3, High, taken));
    }

    private void Place(GameData data, int player, string what, (int x, int y) cell)
    {
        NVector2 p = _sim.World.NavGrid.CellCenter(cell.x, cell.y);
        _sim.Enqueue(Command.SpawnUnit(player, data.Factions[player].Units[0], p));
        _lone.Add((what, p));
        GD.Print($"{what}: cell ({cell.x}, {cell.y})");
    }

    // Nearest matching cell to (ox, oy) at least 10 cells from every unit and every other pick.
    private (int, int) Find(Heightmap m, int ox, int oy, Func<int, int, bool> match, List<(int, int)> taken)
    {
        var u = _sim.World.Units;
        for (int r = 0; r < 128; r++)
            for (int y = oy - r; y <= oy + r; y++)
                for (int x = ox - r; x <= ox + r; x++)
                {
                    if (Math.Max(Math.Abs(x - ox), Math.Abs(y - oy)) != r || x < 0 || y < 0 || x >= m.Width || y >= m.Height) continue;
                    if (!match(x, y)) continue;
                    bool clear = true;
                    foreach ((int tx, int ty) in taken) if (Math.Max(Math.Abs(x - tx), Math.Abs(y - ty)) < 10) clear = false;
                    for (int i = 0; i < u.Capacity && clear; i++)
                        if (u.Alive[i] && NVector2.Distance(u.Position[i], _sim.World.NavGrid.CellCenter(x, y)) < 20f) clear = false;
                    if (!clear) continue;
                    taken.Add((x, y));
                    return (x, y);
                }
        throw new InvalidOperationException("no matching cell");
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
        crop.SavePng(full.Replace(".png", "-minimap.png"));
        int alive = 0;
        var u = _sim.World.Units;
        foreach ((string what, NVector2 at) in _lone)
        {
            bool found = false;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && NVector2.Distance(u.Position[i], at) < 2f) found = true;
            if (found) alive++;
            NVector2 px = mini.Fit.ToPixel(at);
            GD.Print($"{what}: {(found ? "spawned" : "MISSING")}, crop pixel ({px.X * 4:F0}, {px.Y * 4:F0}), screen ({r.Position.X + px.X:F0}, {r.Position.Y + px.Y:F0})");
        }
        GD.Print($"saved {full}; {alive}/{_lone.Count} lone units alive; {u.Count} units");
        GetTree().Quit();
    }
}
