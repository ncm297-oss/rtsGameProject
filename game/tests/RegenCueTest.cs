using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V3a: the selection panel's "Regenerating" cue (<see cref="SelectionPanel.RegenLabel"/>, text <c>hud.regenerating</c>), on
/// a forged copy whose third faction has <c>faction.json</c> <c>regen { "hpPerSecond": 1, "delay": 6 }</c> (the Teblor page's
/// bonus; <see cref="ThirdFactionForge"/>). The local player plays it (<c>--faction 1 third</c>); a Whirlwind Desert Archer
/// shoots its Heavy Infantry (on hold) and is walked away, then back for a second hit. One tick a frame, the panel synced
/// before every look; each frame the cue must be exactly "below its maximum and 6 s (120 ticks) since its last hit" (an
/// oracle from the raw store, not from <see cref="RegenCue"/>):
/// <list type="number">
/// <item>hurt: no cue on the hit's frame;</item>
/// <item>no cue on any frame before 6 s;</item>
/// <item>the cue from the first frame at 6 s on, while it heals 1 HP every 20 ticks;</item>
/// <item>gone on the frame after the next hit;</item>
/// <item>gone at full health (and never back while full).</item>
/// </list>
/// Also: 60 steady frames with the cue up allocate 0 bytes in the panel's Sync; two units selected (the grid) or a building
/// shows no cue; the cue's text is the data's. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the panel with the cue.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/RegenCueTest.tscn</c>; prints "REGEN CUE TEST PASS" or FAIL lines.
/// </summary>
public partial class RegenCueTest : Node
{
    private const int DelayTicks = 120;

    private readonly List<string> _failures = new();
    private string? _shots;
    private int _framesChecked, _hiddenWaiting, _shownHealing;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://regen-cue-test-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] user = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < user.Length; i++)
                if (user[i] == "--shots") _shots = user[i + 1];
            await Frames(1);
            GameData data = ThirdFactionForge.Forge(ProjectSettings.GlobalizePath("res://data"), root, "Forged Third Host", out _, regenHpPerSecond: 1, regenDelay: 6);
            int third = data.FindFaction(ThirdFactionForge.Id);
            Check(data.Factions[third].RegenPerTick == 20 && data.Factions[third].RegenDelayTicks == DelayTicks,
                $"forged regen: {data.Factions[third].RegenPerTick} a tick after {data.Factions[third].RegenDelayTicks} ticks");
            await Stage(data, third);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"REGEN CUE TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("REGEN CUE TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Stage(GameData data, int third)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "0", "--workers", "0", "--no-fog", "--mute", "--zoom", "20", "--faction", "1", ThirdFactionForge.Id }));
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        runner.FixedFrameDelta = 0.05; // one tick a frame
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        UnitStore u = w.Units;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var panel = match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        UiText ui = UiText.Shared!;
        try
        {
            Check(w.FactionOf(0) == third, $"player 0 plays faction {w.FactionOf(0)} (want {third})");
            Check(panel.RegenLabel.Text == ui.Hud(HudText.Regenerating) && panel.RegenLabel.Text.Length > 0, $"the cue reads '{panel.RegenLabel.Text}'");
            while (sim.TickNumber < 3) await Frames(1);
            int heavyType = data.FindUnit(ThirdFactionForge.Id + "_heavy_infantry"), archerType = data.FindUnit("whirlwind_desert_archer");
            // Mid-map, far from both halls (a hall's tower shot at the archer would turn it on the hall).
            System.Numerics.Vector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            System.Numerics.Vector2 at = SameLevel(w, mid, 0, 12);
            // The archer on the unit's plateau (a cliff between them blocks its sight, and an Attack on an unseen unit is dropped).
            System.Numerics.Vector2 shootFrom = SameLevel(w, at, 4, 6), away = SameLevel(w, at, 20, 30);
            EntityHandle heavy = await SpawnOne(sim, 0, heavyType, at);
            EntityHandle mate = await SpawnOne(sim, 0, heavyType, at + new System.Numerics.Vector2(0f, -4f));
            EntityHandle archer = await SpawnOne(sim, 1, archerType, shootFrom);
            if (!Check(heavy != default && mate != default && archer != default, "the spawns did not land")) return;
            sim.Enqueue(Command.HoldPosition(0, heavy));
            sim.Enqueue(Command.HoldPosition(0, mate));
            sel.SelectOnly(heavy);
            camera.SetFocus(at.X + 3f, at.Y + 2f);
            int max = w.MaxHp(heavy.Index);
            await Look(sim, panel, heavy, "full, before any hit");
            Check(!panel.RegenLabel.Visible && u.Hp[heavy.Index] == max, "a full unit shows the cue");

            // (1) hurt, (2) no cue for 6 s, (3) the cue after: the archer shoots it down by 40 hp and walks away.
            int hit1 = await Hits(sim, panel, heavy, archer, shootFrom, away, max - 40);
            if (!Check(hit1 >= 0, "the archer never hit")) return;
            Check(!panel.RegenLabel.Visible, "a cue on the hit's frame");
            int firstCue = -1;
            while (sim.TickNumber < u.LastDamagedTick[heavy.Index] + DelayTicks + 60)
            {
                await Frames(1);
                await Look(sim, panel, heavy, "waiting then healing");
                if (firstCue < 0 && panel.RegenLabel.Visible) firstCue = sim.TickNumber;
            }
            int last = u.LastDamagedTick[heavy.Index];
            Check(firstCue == last + DelayTicks, $"the cue first showed with next tick {firstCue} (last hit in tick {last}: want {last + DelayTicks}, 6 s)");
            int hpHealing = u.Hp[heavy.Index];
            await Shot("regen-cue");

            // Steady: 60 frames with the cue up allocate nothing in the panel's Sync.
            long bytes = 0;
            for (int k = 0; k < 60; k++)
            {
                await Frames(1);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                panel.Sync();
                bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                Check(panel.RegenLabel.Visible, $"the cue dropped at steady frame {k}");
            }
            Check(bytes == 0, $"60 steady frames of the panel with the cue: {bytes} bytes");
            Check(u.Hp[heavy.Index] > hpHealing, $"no healing while the cue showed ({hpHealing} -> {u.Hp[heavy.Index]})");
            GD.Print($"  cue at 6 s after the hit in tick {last}; 60 steady frames {bytes} bytes; hp {hpHealing} -> {u.Hp[heavy.Index]} of {max}");

            // Layout: the cue's text fits its label (not clipped) and the label overlaps no stat name beside it.
            Label cue = panel.RegenLabel;
            float textWidth = cue.GetThemeFont("font").GetStringSize(cue.Text, HorizontalAlignment.Left, -1, cue.GetThemeFontSize("font_size")).X;
            Check(textWidth <= cue.Size.X, $"the cue's text is {textWidth:0} px in a {cue.Size.X:0} px label");
            for (int r = 0; r < 5; r++)
                Check(!cue.GetGlobalRect().Intersects(panel.StatName(r).GetGlobalRect()), $"the cue {cue.GetGlobalRect()} overlaps stat name {r} {panel.StatName(r).GetGlobalRect()}");
            Check(!cue.GetGlobalRect().Intersects(panel.StateLabel.GetGlobalRect()), $"the cue overlaps the state {panel.StateLabel.GetGlobalRect()}");
            GD.Print($"  cue '{cue.Text}' {textWidth:0} px in {cue.GetGlobalRect()}");

            // Two units selected: the grid, no cue. A building selected: no cue. Back to the one unit: the cue.
            sel.SelectOnly(heavy);
            sel.Selection.Add(mate);
            panel.Sync();
            Check(panel.ShownKind == 2 && !panel.RegenLabel.Visible, $"two selected: kind {panel.ShownKind}, cue {panel.RegenLabel.Visible}");
            int hall = -1;
            for (int i = 0; i < w.Buildings.Capacity && hall < 0; i++)
                if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 0) hall = i;
            Check(hall >= 0 && sel.SelectBuilding(hall), "no own building to select");
            panel.Sync();
            Check(panel.ShownKind == 3 && !panel.RegenLabel.Visible, $"a building selected: kind {panel.ShownKind}, cue {panel.RegenLabel.Visible}");
            sel.SelectOnly(heavy);
            panel.Sync();
            Check(panel.ShownKind == 1 && panel.RegenLabel.Visible, "the one unit again: no cue");

            // (4) gone on the next hit.
            int hit2 = await Hits(sim, panel, heavy, archer, shootFrom, away, u.Hp[heavy.Index] - 1);
            Check(hit2 > hit1 && !panel.RegenLabel.Visible, $"second hit in tick {hit2}: the cue {panel.RegenLabel.Visible}");

            // (5) gone at full health: five ticks a frame now, every frame still checked against the oracle.
            runner.FixedFrameDelta = 0.25;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (u.Hp[heavy.Index] < max && clock.Elapsed.TotalSeconds < 120)
            {
                await Frames(1);
                await Look(sim, panel, heavy, "healing to full");
            }
            Check(u.Hp[heavy.Index] == max && !panel.RegenLabel.Visible, $"at full ({u.Hp[heavy.Index]} / {max}): the cue {panel.RegenLabel.Visible}");
            for (int k = 0; k < 20; k++)
            {
                await Frames(1);
                await Look(sim, panel, heavy, "full");
            }
            Check(_hiddenWaiting >= DelayTicks - 5 && _shownHealing > 100, $"frames: {_hiddenWaiting} waiting without the cue, {_shownHealing} healing with it");
            GD.Print($"  {_framesChecked} frames checked: {_hiddenWaiting} inside the delay (no cue), {_shownHealing} healing (cue); full at tick {sim.TickNumber}");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // Walks the archer to `shootFrom` (an Attack needs the target in sight), orders it at the unit until the unit is down to
    // `hpAtMost`, then walks it away; returns the last hit's tick (-1 if it never got there). Every frame is looked at.
    private async Task<int> Hits(Simulation sim, SelectionPanel panel, EntityHandle unit, EntityHandle archer, System.Numerics.Vector2 shootFrom,
        System.Numerics.Vector2 away, int hpAtMost)
    {
        UnitStore u = sim.World.Units;
        int before = u.LastDamagedTick[unit.Index], start = sim.TickNumber;
        sim.Enqueue(Command.Move(1, archer, shootFrom));
        while (System.Numerics.Vector2.Distance(u.Position[archer.Index], u.Position[unit.Index]) > 13f && sim.TickNumber < start + 600)
        {
            await Frames(1);
            await Look(sim, panel, unit, "the archer walking in");
        }
        sim.Enqueue(Command.Attack(1, archer, unit, isBuilding: false));
        while ((u.LastDamagedTick[unit.Index] == before || u.Hp[unit.Index] > hpAtMost) && sim.TickNumber < start + 1500)
        {
            await Frames(1);
            await Look(sim, panel, unit, "under fire");
        }
        if (u.LastDamagedTick[unit.Index] == before || u.Hp[unit.Index] > hpAtMost)
        {
            GD.Print($"  no hit: archer alive {u.Alive[archer.Index]} at {u.Position[archer.Index]} state {u.State[archer.Index]} mode {u.Mode[archer.Index]}; "
                + $"unit at {u.Position[unit.Index]} hp {u.Hp[unit.Index]}, tick {sim.TickNumber}");
            return -1;
        }
        int hit = u.LastDamagedTick[unit.Index];
        sim.Enqueue(Command.Move(1, archer, away));
        return hit;
    }

    // The panel synced, then its cue against the oracle: below the maximum, and 120 ticks since the last hit (or never hit).
    private Task Look(Simulation sim, SelectionPanel panel, EntityHandle unit, string when)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        panel.Sync();
        int last = u.LastDamagedTick[unit.Index];
        bool below = u.Hp[unit.Index] < w.MaxHp(unit.Index);
        bool want = below && (last < 0 || sim.TickNumber - last >= DelayTicks);
        _framesChecked++;
        if (below && !want) _hiddenWaiting++;
        if (want) _shownHealing++;
        Check(panel.RegenLabel.Visible == want, $"{when}: next tick {sim.TickNumber}, last hit {last}, hp {u.Hp[unit.Index]} / {w.MaxHp(unit.Index)}: cue {panel.RegenLabel.Visible}, want {want}");
        return Task.CompletedTask;
    }

    // A passable cell centre on the level of `at`'s cell, between `min` and `max` cells from it (cells are 2 m) (the nearest such ring first).
    private static System.Numerics.Vector2 SameLevel(World w, System.Numerics.Vector2 at, int min, int max)
    {
        Rts.Sim.Map.NavGrid g = w.NavGrid;
        g.WorldToCell(at, out int x0, out int y0);
        int level = g.LevelAt(x0, y0);
        for (int r = min; r <= max; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = x0 + dx, y = y0 + dy;
                    if (g.IsPassable(x, y) && g.LevelAt(x, y) == level) return g.CellCenter(x, y);
                }
        throw new InvalidOperationException($"no cell on level {level} {min}-{max} cells from {at}");
    }

    private async Task<EntityHandle> SpawnOne(Simulation sim, int player, int type, System.Numerics.Vector2 at)
    {
        UnitStore u = sim.World.Units;
        var before = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) before[i] = u.Alive[i];
        sim.Enqueue(Command.SpawnUnit(player, type, at));
        int t0 = sim.TickNumber;
        while (sim.TickNumber < t0 + 2) await Frames(1);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i] && u.Owner[i] == player) return new EntityHandle(i, u.Generation[i]);
        return default;
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok && _failures.Count < 200) _failures.Add(message);
        return ok;
    }
}
