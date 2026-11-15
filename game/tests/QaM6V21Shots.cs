using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>QA pictures for M6-V21 (windowed): team colour on both palettes in all eight player colours, and the props in the hand through idle, walk and attack.</summary>
/// <remarks>
/// <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V21Shots.tscn -- --colours &lt;dir&gt;</c> saves
/// <c>colours&lt;set&gt;-40m.png</c> / <c>-20m.png</c> (+ a grey copy of the 40 m one): four players (columns; <c>--set 1</c>
/// colours 1-4, <c>--set 2</c> colours 5-8, so two runs show all eight) x the six model types (rows: Heavy Infantry, Crossbowman, Laborer, Raider, Desert Archer, Camp Follower), facing the camera.
/// <c>-- --props &lt;dir&gt;</c> saves close-ups from a test camera 4 m away: <c>props-&lt;id&gt;-idle.png</c>, <c>-walk-N.png</c> and
/// <c>-attack-N.png</c> (N = four phases of the clip) for the Heavy Infantry, Raider and Desert Archer. Prints "QA SHOTS DONE".
/// </remarks>
public partial class QaM6V21Shots : Node
{
    private Match _match = null!;
    private Simulation _sim = null!;
    private RtsCamera _camera = null!;
    private UnitViews _views = null!;
    private int _set = 1;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string? colours = Arg(args, "--colours");
        string? props = Arg(args, "--props");
        try
        {
            GameData data = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data")).Data!;
            _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(_match);
            var flags = new System.Collections.Generic.List<string> { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud" };
            // Four players (the most a generated map seats); --set 2 gives them colours 5-8.
            int set = Arg(args, "--set") == "2" ? 2 : 1;
            if (colours != null)
            {
                flags.AddRange(new[] { "--players", "4", "--no-combat" });
                for (int p = 0; p < 4; p++) flags.AddRange(new[] { "--colour", (p + 1).ToString(CultureInfo.InvariantCulture), (p + 1 + (set - 1) * 4).ToString(CultureInfo.InvariantCulture) });
            }
            _set = set;
            _match.Start(data, LaunchOptions.Parse(flags.ToArray()));
            _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
            _camera = _match.GetNode<RtsCamera>("RtsCamera");
            _camera.EdgePanEnabled = false;
            _views = _match.GetNode<UnitViews>("World3D/UnitViews");
            while (_sim.TickNumber < 3) await Frames(1);
            if (colours != null) await Colours(data, colours);
            if (props != null) await Props(data, props);
        }
        catch (Exception ex)
        {
            GD.Print($"QA SHOTS FAIL: {ex}");
        }
        GD.Print("QA SHOTS DONE");
        GetTree().Quit();
    }

    private async Task Colours(GameData data, string dir)
    {
        string[] ids = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_laborer", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_camp_follower" };
        NVector2 c = Centre() + new NVector2(0f, -5f);
        for (int p = 0; p < 4; p++)
            for (int r = 0; r < ids.Length; r++)
                _sim.Enqueue(Command.SpawnUnit(p, data.FindUnit(ids[r]), c + new NVector2(-3f + p * 2f, -4f + r * 1.6f)));
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(0f, 0.6f)));
        at = _sim.TickNumber;
        while (_sim.TickNumber < at + 40) await Frames(1);
        uint[] rgb = _match.PlayerRgb;
        GD.Print($"colours: players {string.Join(' ', Array.ConvertAll(rgb, x => x.ToString("X6")))}");
        _camera.SetFocus(c.X, c.Y + 1f);
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            await Frames(10);
            Image img = GetViewport().GetTexture().GetImage();
            string name = $"colours{_set}-{zoom:0}m";
            img.SavePng(System.IO.Path.Combine(dir, name + ".png"));
            GD.Print($"saved {name}.png");
            if (zoom != CameraLimits.DefaultZoom) continue;
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
            img.SavePng(System.IO.Path.Combine(dir, name + "-gray.png"));
        }
    }

    private async Task Props(GameData data, string dir)
    {
        var cam = new Camera3D { Fov = 40f };
        _match.GetNode<Node3D>("World3D").AddChild(cam);
        UnitStore u = _sim.World.Units;
        NVector2 c = Centre();
        int xbow = data.FindUnit("malazan_crossbowman");
        foreach ((string id, string foe, float gap) in new[] { ("malazan_heavy_infantry", "whirlwind_raider", 1.4f), ("whirlwind_raider", "malazan_heavy_infantry", 1.4f), ("whirlwind_desert_archer", "malazan_crossbowman", 9f) })
        {
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            int type = data.FindUnit(id);
            // Idle, facing +y (toward the camera on +Z).
            Spawn(0, type, c);
            await Ticks(4);
            int slot = SlotOf(u, 0, type);
            _sim.Enqueue(Command.Move(0, Handle(u, slot), u.Position[slot] + new NVector2(0f, 0.5f)));
            await Ticks(30);
            await Shoot(cam, slot, new Vector3(1.2f, 1.6f, 3.6f), $"props-{id}-idle", dir);
            await Shoot(cam, slot, new Vector3(3.8f, 1.4f, 0.2f), $"props-{id}-idle-side", dir);
            // Walk toward the camera; the camera follows.
            _sim.Enqueue(Command.Move(0, Handle(u, slot), u.Position[slot] + new NVector2(0f, 14f)));
            for (int k = 0; k < 4; k++)
            {
                await Ticks(3);
                await Shoot(cam, slot, new Vector3(2.4f, 1.5f, 3.2f), $"props-{id}-walk-{k}", dir);
            }
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            // Attack: the foe on +x at the gap; side view from +Z.
            Spawn(0, type, c);
            Spawn(1, data.FindUnit(foe), c + new NVector2(gap, 0f));
            await Ticks(4);
            slot = SlotOf(u, 0, type);
            int foeSlot = SlotOf(u, 1, data.FindUnit(foe));
            if (slot < 0 || foeSlot < 0) { GD.Print($"props {id}: spawn failed"); continue; }
            u.Hp[foeSlot] = 100000; // staging: the foe outlives the pictures (test write between ticks)
            u.Hp[slot] = 100000;
            _sim.Enqueue(Command.Attack(0, Handle(u, slot), Handle(u, foeSlot), false));
            for (int t = 0; t < 100 && u.State[slot] != UnitState.Attacking; t++) await Ticks(1);
            GD.Print($"props {id}: state {u.State[slot]}, clip {_views.AnimatorOf(slot)?.CurrentAnimation}");
            for (int k = 0; k < 4; k++)
            {
                await Frames(9);
                await Shoot(cam, slot, new Vector3(0.6f, 1.4f, 3.6f), $"props-{id}-attack-{k}", dir);
            }
        }
        cam.QueueFree();
    }

    private async Task Shoot(Camera3D cam, int slot, Vector3 offset, string name, string dir)
    {
        Vector3 at = _views.ViewOf(slot)!.GlobalPosition;
        cam.GlobalPosition = at + offset;
        cam.LookAt(at + new Vector3(0f, 0.2f, 0f), Vector3.Up);
        cam.MakeCurrent();
        await Frames(2);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name + ".png"));
        AnimationPlayer? a = _views.AnimatorOf(slot);
        GD.Print($"saved {name}.png ({a?.CurrentAnimation} at {a?.CurrentAnimationPosition.ToString("0.00", CultureInfo.InvariantCulture)} s)");
    }

    private static EntityHandle Handle(UnitStore u, int slot) => new(slot, u.Generation[slot]);

    private static int SlotOf(UnitStore u, int owner, int type)
    {
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == owner && u.TypeId[i] == type) return i;
        return -1;
    }

    private async Task Ticks(int n)
    {
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + n) await Frames(1);
    }

    private NVector2 Centre()
    {
        World w = _sim.World;
        Rts.Sim.Map.Heightmap h = w.Heightmap;
        const int bw = 24, bh = 16;
        int best = int.MaxValue;
        NVector2 found = new NVector2(h.Width, h.Height) * (Rts.Sim.Map.MapConstants.CellSize / 2f);
        for (int y0 = 0; y0 + bh < h.Height; y0 += 2)
            for (int x0 = 0; x0 + bw < h.Width; x0 += 2)
            {
                int level = h.LevelAt(x0, y0);
                bool ok = true;
                for (int y = y0; y < y0 + bh && ok; y++)
                    for (int x = x0; x < x0 + bw && ok; x++)
                        ok = h.LevelAt(x, y) == level && !h.IsRamp(x, y) && w.NavGrid.IsPassable(x, y);
                int d = Math.Abs(x0 + bw / 2 - h.Width / 2) + Math.Abs(y0 + bh / 2 - h.Height / 2);
                if (!ok || d >= best) continue;
                best = d;
                found = new NVector2(x0 + bw / 2f, y0 + bh / 2f) * Rts.Sim.Map.MapConstants.CellSize;
            }
        return found;
    }

    private void Spawn(int player, int type, NVector2 at) => _sim.Enqueue(Command.SpawnUnit(player, type, at));

    private static string? Arg(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
