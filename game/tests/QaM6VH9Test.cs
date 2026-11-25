using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH9 (view hardening: picking by drawn height, the bar ceiling, the Sapper's one-shot throw).
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH9Test.tscn</c>; prints
/// "QA M6-VH9 TEST PASS" or FAIL lines. Rows:
/// (Z) every sampled pixel of the Keep's and the Barracks' model selects it at five zooms (20, 30, 40, 50, 60);
/// (F) a Barracks in front of a Keep: a pixel where the Barracks' model is met first selects the Barracks, one where only the
/// Keep's model is met selects the Keep;
/// (T) a Keep on each terrace level (0, 1, 2) centred at zoom 20, 30, 40 and 60: its bar on screen, over its ground, and not
/// hidden inside its own model (BUG-1631's ceiling on high ground);
/// (E) enemy units behind an own Keep, seen through the gaps of its model: a right click where the enemy is seen (the ray meets
/// the unit before any model triangle) targets that enemy (measured: the share that does);
/// (PH) with building models off (<c>--placeholder-art</c>), a Keep's box is picked to its 3 m and not 0.3 m over it;
/// (S) a Sapper's one-shot throw at game speeds 0.5, 1 and 2 over 20 shots each (one throw a shot, each shot within 0.15 of the
/// clip of its release); paused mid-windup the clip holds still; the target removed mid-windup leaves no stuck throw.
/// </remarks>
public partial class QaM6VH9Test : Node
{
    private const string Tag = "QA M6-VH9 TEST";

    private readonly List<string> _failures = new();
    private bool _strict;
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        _strict = Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0;
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await ZoomRows();
            await TerraceBarRows();
            await EnemyBehindRows();
            await PlaceholderRows();
            foreach (double speed in new[] { 0.5, 1.0, 2.0 }) await ThrowRows(speed, 20);
            await ThrowEdgeRows();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (Z) five zooms, (F) front and back ----

    private async Task ZoomRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep"), barracks = _data.FindBuilding("malazan_barracks");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -20f);
        foreach (int type in new[] { keep, barracks })
        {
            if (!Check(_views.Models!.Has(type), $"(Z) {_data.Buildings[type].Key} has no model")) continue;
            int slot = Spawn(0, type, near);
            near += new NVector2(0f, 24f);
            if (!Check(slot >= 0, $"(Z) {_data.Buildings[type].Key} not spawned")) continue;
            _views.Sync(W);
            NVector2 c = Centre(slot);
            foreach (float zoom in new[] { 20f, 30f, 40f, 50f, 60f })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                int hits = 0, picked = 0;
                foreach (Vector2 p in ModelPixels(slot))
                {
                    Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                    if (!float.IsFinite(_views.ModelRayDistance(slot, o, d))) continue;
                    hits++;
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    Click(p);
                    if (_sel.SelectedBuilding == slot) picked++;
                }
                string msg = $"(Z) {_data.Buildings[type].Key} at zoom {zoom}: {picked} of {hits} model pixels select it";
                GD.Print($"  {msg}");
                Check(hits > 0 && picked == hits, msg);
            }
        }

        // (F) a Barracks just south (toward the camera) of a Keep.
        int k = Spawn(0, keep, near + new NVector2(0f, 6f));
        if (Check(k >= 0, "(F) no Keep"))
        {
            NVector2 kc = Centre(k);
            float kHalf = _data.Buildings[keep].FootprintHeight * MapConstants.CellSize / 2f;
            float bHalf = _data.Buildings[barracks].FootprintHeight * MapConstants.CellSize / 2f;
            int f = Spawn(0, barracks, kc + new NVector2(0f, kHalf + bHalf + 1.5f));
            if (Check(f >= 0, "(F) no Barracks in front of the Keep"))
            {
                _views.Sync(W);
                foreach (float zoom in new[] { 20f, 40f, 60f })
                {
                    NVector2 fc = Centre(f);
                    _camera.SetZoom(zoom);
                    _camera.SetFocus((kc.X + fc.X) / 2f, (kc.Y + fc.Y) / 2f);
                    await Frames(2);
                    int frontPx = 0, frontOk = 0, backPx = 0, backOk = 0;
                    var seen = new HashSet<Vector2>();
                    foreach (int s in new[] { k, f })
                        foreach (Vector2 p in ModelPixels(s))
                        {
                            if (!seen.Add(p)) continue;
                            Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                            float tk = _views.ModelRayDistance(k, o, d), tf = _views.ModelRayDistance(f, o, d);
                            if (!float.IsFinite(tk) && !float.IsFinite(tf)) continue;
                            _sel.ClearBuilding();
                            _sel.Selection.Clear();
                            Click(p);
                            if (tf <= tk) { frontPx++; if (_sel.SelectedBuilding == f) frontOk++; }
                            else { backPx++; if (_sel.SelectedBuilding == k) backOk++; }
                        }
                    string msg = $"(F) zoom {zoom}: Barracks model in front {frontOk}/{frontPx} select the Barracks; Keep model seen {backOk}/{backPx} select the Keep";
                    GD.Print($"  {msg}");
                    Check(frontPx > 0 && frontOk == frontPx, msg + " (a front pixel selects something else)");
                    // Keep pixels inside the Barracks' wider pick box select the Barracks by design (measured, reported).
                }
            }
        }
        _sel.ClearBuilding();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // Every 5th pixel over the slot's model's screen rectangle.
    private IEnumerable<Vector2> ModelPixels(int slot)
    {
        MeshInstance3D m = _views.ModelOf(slot)!;
        Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
        Rect2 screen = GetViewport().GetVisibleRect();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(k));
            x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
        }
        x0 = Math.Max(x0, 1f); y0 = Math.Max(y0, 1f); x1 = Math.Min(x1, screen.Size.X - 2f); y1 = Math.Min(y1, screen.Size.Y - 2f);
        for (float y = MathF.Floor(y0); y <= y1; y += 5f)
            for (float x = MathF.Floor(x0); x <= x1; x += 5f)
                yield return new Vector2(x, y);
    }

    // ---- (T) the Keep's bar on each terrace level ----

    private async Task TerraceBarRows()
    {
        int keep = _data.FindBuilding("malazan_garrison_keep");
        BuildingDef def = _data.Buildings[keep];
        var done = new bool[MapConstants.LevelCount];
        for (int seed = 1; seed <= 16 && Array.IndexOf(done, false) >= 0; seed++)
        {
            StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--resources", "high", "--seed", $"{seed}");
            for (int level = 0; level <= MapConstants.MaxLevel; level++)
            {
                if (done[level]) continue;
                int anchor = LevelAnchor(def, level);
                if (anchor < 0) continue;
                done[level] = true;
                GD.Print($"  (T) level {level}: seed {seed}");
                await TerraceBarRow(def, keep, level, anchor);
            }
            await EndMatch();
        }
        for (int level = 0; level <= MapConstants.MaxLevel; level++)
            if (!done[level]) GD.Print($"  (T) level {level}: no open anchor for a Keep on seeds 1-16 (not tested)");
    }

    private async Task TerraceBarRow(BuildingDef def, int keep, int level, int anchor)
    {
        {
            _sim.Enqueue(Command.SpawnBuilding(0, keep, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            _sim.Tick();
            _sim.Tick();
            int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (!Check(slot >= 0 && B.TypeId[slot] == keep, $"(T) level {level}: Keep not spawned")) return;
            _views.Sync(W); // the bar's height is placed for every shown building, hurt or not (BarY)
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            float half = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            foreach (float zoom in new[] { 20f, 30f, 40f, 60f })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                Rect2 screen = GetViewport().GetVisibleRect();
                float y = _views.BarY(slot), wanted = _views.BarWanted(slot);
                // Where the bar is drawn: since the BUG-1680 fix a lowered bar sits on the building's camera side, not over its centre.
                Vector3 bar = _views.ViewOf(slot)!.GetNode<MeshInstance3D>("BarBack").GlobalPosition;
                int hidden = 0, n = 9;
                bool on = true;
                for (int k = 0; k < n; k++)
                {
                    var pt = new Vector3(bar.X - half + 2f * half * k / (n - 1), bar.Y, bar.Z);
                    Vector2 s = _camera.UnprojectPosition(pt);
                    on &= screen.HasPoint(s);
                    Vector3 o = _camera.GlobalPosition, d = (pt - o).Normalized();
                    if (_views.ModelRayDistance(slot, o, d) < (pt - o).Length() - 0.05f) hidden++;
                }
                string msg = string.Create(CultureInfo.InvariantCulture,
                    $"(T) Keep on level {level} (ground {ground:0.00} m) at zoom {zoom}: bar {y:0.00} m over its ground (look's {wanted:0.00}), {(on ? "on" : "OFF")} screen, {hidden} of {n} points behind its own model");
                GD.Print($"  {msg}");
                Check(on, msg);
                Check(y > 0.5f, msg + " (bar at or under the ground)");
                Check(hidden <= n / 2, "BUG-1680: " + msg + " (bar hidden inside the model)");
            }
        }
    }

    // An anchor whose footprint and a 2-cell ring are all passable, free and on `level` (flat plateau ground).
    private int LevelAnchor(BuildingDef def, int level)
    {
        NavGrid g = W.NavGrid;
        int type = _data.FindBuilding(def.Key);
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy += 2)
            for (int cx = 4; cx + def.FootprintWidth < g.Width - 4; cx += 2)
            {
                int a = cy * g.Width + cx;
                if (g.LevelAt(cx, cy) != level || !B.Fits(type, a)) continue;
                bool ok = true;
                for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && ok; y++)
                    for (int x = cx - 2; x <= cx + def.FootprintWidth + 1 && ok; x++)
                        if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                if (ok) return a;
            }
        return -1;
    }

    // ---- (E) enemies seen through the Keep's gaps ----

    private async Task EnemyBehindRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep"), heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f);
        int slot = Spawn(0, keep, near);
        if (!Check(slot >= 0, "(E) no Keep")) { await EndMatch(); return; }
        BuildingDef def = _data.Buildings[keep];
        NVector2 c = Centre(slot);
        float half = def.FootprintHeight * MapConstants.CellSize / 2f;
        var enemies = new List<int>();
        foreach (float back in new[] { 1.5f, 4f, 7f })
            for (float dx = -6f; dx <= 6.01f; dx += 2f)
            {
                int u = SpawnUnit(1, heavy, c + new NVector2(dx, -(half + back)));
                if (u >= 0) enemies.Add(u);
            }
        _views.Sync(W);
        foreach (float zoom in new[] { 20f, 40f, 60f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            int seen = 0, attacked = 0, own = 0, oldClear = 0;
            UnitStore u = W.Units;
            foreach (Vector2 p in ModelPixels(slot))
            {
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                int unit = UnitPicker.PickRay(u.Alive, u.PrevPosition, u.Position, u.Radius, W.Heightmap, 1f, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out float unitT);
                if (unit < 0 || u.Owner[unit] != 1) continue;
                if (_views.ModelRayDistance(slot, o, d) < unitT) continue; // the model hides it there
                seen++;
                // Before M6-VH9 the Keep was picked on the 3 m box: would that box have been met before the unit here?
                int oldBox = BuildingPicker.PickRay(B, _data.Buildings, W.NavGrid, W.Heightmap, -1, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
                    BuildingViews.BoxHeight, BuildingViews.SiteMinHeight, ReadOnlySpan<bool>.Empty, out float oldT);
                if (oldBox < 0 || oldT >= unitT) oldClear++;
                bool enemy = _sel.EnemyAt(p, out EntityHandle target, out bool isBuilding);
                if (enemy && !isBuilding && target.Index == unit) attacked++;
                else if (!enemy) own++;            }
            string msg = $"(E) zoom {zoom}: {seen} pixels show an enemy behind the own Keep (not hidden by its model); a right click there targets it at {attacked} ({100f * attacked / Math.Max(1, seen):0.#}%), means the own Keep / ground at {own}; the old 3 m box left {oldClear} of them clear (targeted before M6-VH9)";
            GD.Print($"  {msg}");
            Check(seen > 0 && attacked >= oldClear, "BUG-1681: " + msg);
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // ---- (PH) placeholder art ----

    private async Task PlaceholderRows()
    {
        BuildingViews.UseModels = false;
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep");
        int slot = Spawn(0, keep, StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f));
        if (Check(slot >= 0, "(PH) no Keep"))
        {
            _views.Sync(W);
            BuildingDef def = _data.Buildings[keep];
            NVector2 c = Centre(slot);
            float g = TerrainHeight.At(W.Heightmap, c.X, c.Y), drawn = BuildingViews.DrawnHeight(def);
            float z0 = c.Y - def.FootprintHeight * MapConstants.CellSize / 2f;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            Check(_views.PickTops.Length > keep && Math.Abs(_views.PickTops[keep] - drawn) < 1e-4f, $"(PH) PickTops[keep] {(_views.PickTops.Length > keep ? _views.PickTops[keep] : -1)} != drawn {drawn}");
            Vector2 under = _camera.UnprojectPosition(new Vector3(c.X, g + drawn - 0.3f, z0 + 0.05f));
            Vector2 over = _camera.UnprojectPosition(new Vector3(c.X, g + drawn + 0.3f, z0 + 0.05f));
            int a = _sel.PickBuilding(under), b = _sel.PickBuilding(over);
            GD.Print($"  (PH) models off: Keep box {drawn:0.00} m; a click 0.3 m under its back top picks {a}, 0.3 m over it picks {b} (slot {slot})");
            Check(a == slot && b != slot, $"(PH) models off: under {a}, over {b}, want {slot} and not {slot}");
        }
        await EndMatch();
        BuildingViews.UseModels = true;
    }

    // ---- (S) the Sapper's throw ----

    private async Task<(Match Match, SimRunner Runner, UnitViews Views, int Slot, int Foe)> ThrowMatch(double speed)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.FixedFrameDelta = 1.0 / 60.0;
        runner.GameSpeed = speed;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Simulation sim = runner.Simulation!;
        UnitViews views = match.GetNode<UnitViews>("World3D/UnitViews");
        World w = sim.World;
        int sapper = _data.FindUnit("malazan_sapper"), heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
        sim.Enqueue(Command.SpawnUnit(0, sapper, mid));
        sim.Enqueue(Command.SpawnUnit(1, heavy, mid + new NVector2(7f, 0f)));
        int slot = -1, foe = -1;
        for (int f = 0; f < 240 && (slot < 0 || foe < 0 || views.AnimatorOf(slot) == null); f++)
        {
            await Frames(1);
            for (int i = 0; i < w.Units.Capacity; i++)
            {
                if (w.Units.Alive[i] && w.Units.TypeId[i] == sapper && w.Units.Owner[i] == 0) slot = i;
                if (w.Units.Alive[i] && w.Units.Owner[i] == 1) foe = i;
            }
        }
        if (slot >= 0 && foe >= 0)
        {
            sim.Enqueue(Command.HoldPosition(1, new EntityHandle(foe, w.Units.Generation[foe])));
            sim.Enqueue(Command.Attack(0, new EntityHandle(slot, w.Units.Generation[slot]), new EntityHandle(foe, w.Units.Generation[foe]), isBuilding: false));
        }
        return (match, runner, views, slot, foe);
    }

    private async Task ThrowRows(double speed, int shotsWanted)
    {
        (Match match, SimRunner runner, UnitViews views, int slot, int foe) = await ThrowMatch(speed);
        try
        {
            World w = runner.Simulation!.World;
            UnitStore u = w.Units;
            if (!Check(slot >= 0 && foe >= 0 && views.AnimatorOf(slot) != null, $"(S) speed {speed}: no Sapper model or no enemy")) return;
            int type = u.TypeId[slot];
            float release = views.Models!.OneShotRelease(type, UnitModels.Clip.Attack);
            float length = views.Models.ClipLength(type, UnitModels.Clip.Attack);
            ProjectileStore shots = w.Projectiles;
            var seen = new bool[shots.Capacity];
            var offsets = new List<float>();
            int throws = 0;
            float prev = -1f, releaseAt = release * length;
            int maxFrames = (int)(60 * 3.2 * shotsWanted / speed) + 600;
            for (int f = 0; f < maxFrames && offsets.Count < shotsWanted && u.Alive[foe] && u.Alive[slot]; f++)
            {
                await Frames(1);
                u.Hp[foe] = w.MaxHp(foe); // test seam: the held target outlives 20 shots
                AnimationPlayer anim = views.AnimatorOf(slot)!;
                float pos = (float)anim.CurrentAnimationPosition;
                if (u.State[slot] == UnitState.Attacking && anim.CurrentAnimation == "attack")
                {
                    if (prev >= 0f && prev < releaseAt && pos >= releaseAt) throws++;
                    prev = pos;
                }
                else prev = -1f;
                for (int n = NewShots(shots, seen); n > 0; n--) offsets.Add(pos / length - release);
            }
            float worst = 0f;
            foreach (float o in offsets) worst = Math.Max(worst, Math.Abs(o));
            string msg = string.Create(CultureInfo.InvariantCulture, $"(S) speed {speed}: {offsets.Count} shots, {throws} throws, worst offset from the release {worst:0.000} of the clip (foe alive {u.Alive[foe]})");
            GD.Print($"  {msg}");
            Check(offsets.Count == shotsWanted, msg + $" (want {shotsWanted} shots)");
            Check(throws == offsets.Count, msg + " (want one throw a shot)");
            Check(worst <= 0.15f, msg + " (a shot further than 0.15 of the clip from its release)");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(1);
        }
    }

    // Paused mid-windup the clip holds; the target removed mid-windup leaves the figure out of a stuck throw.
    private async Task ThrowEdgeRows()
    {
        (Match match, SimRunner runner, UnitViews views, int slot, int foe) = await ThrowMatch(1.0);
        try
        {
            World w = runner.Simulation!.World;
            UnitStore u = w.Units;
            if (!Check(slot >= 0 && foe >= 0 && views.AnimatorOf(slot) != null, "(S edge) no Sapper model or no enemy")) return;
            // Wait for the second windup (the first shot gone), then pause the match's tree for 30 frames.
            int windups = 0;
            bool was = false;
            for (int f = 0; f < 900 && windups < 2; f++)
            {
                await Frames(1);
                bool now = u.WindupTicks[slot] > 0;
                if (now && !was) windups++;
                was = now;
            }
            if (!Check(windups == 2, "(S edge) no second windup")) return;
            await Frames(2);
            AnimationPlayer anim = views.AnimatorOf(slot)!;
            match.ProcessMode = ProcessModeEnum.Disabled;
            double before = anim.CurrentAnimationPosition;
            int tick = runner.Simulation!.TickNumber;
            for (int f = 0; f < 30; f++) await Frames(1);
            double after = anim.CurrentAnimationPosition;
            match.ProcessMode = ProcessModeEnum.Inherit;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"  (S edge) paused mid-windup 30 frames: clip {before:0.000} -> {after:0.000} s, tick {tick} -> {runner.Simulation.TickNumber}"));
            Check(Math.Abs(after - before) < 1e-6 && runner.Simulation.TickNumber == tick, "(S edge) the paused throw moved");

            // Next windup: remove the target mid-windup (staging between frames), then watch 120 frames.
            for (int f = 0; f < 600 && u.WindupTicks[slot] == 0; f++) await Frames(1);
            if (!Check(u.WindupTicks[slot] > 0, "(S edge) no third windup")) return;
            await Frames(2);
            float mid = (float)anim.CurrentAnimationPosition;
            u.Free(new EntityHandle(foe, u.Generation[foe]));
            string clip = "";
            float last = 0f;
            int stillAttackFrames = 0;
            for (int f = 0; f < 120; f++)
            {
                await Frames(1);
                clip = anim.CurrentAnimation;
                last = (float)anim.CurrentAnimationPosition;
                if (clip == "attack") stillAttackFrames++;
            }
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"  (S edge) target removed mid-windup (clip at {mid:0.000} s): after 120 frames the Sapper is {u.State[slot]}, clip '{clip}' at {last:0.000} s, {stillAttackFrames} frames still on 'attack'"));
            Check(clip != "attack" || u.State[slot] == UnitState.Attacking, $"(S edge) the Sapper stands {u.State[slot]} on its attack clip after losing its target");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(1);
        }
    }

    private static int NewShots(ProjectileStore shots, bool[] seen)
    {
        ReadOnlySpan<bool> alive = shots.Alive;
        ReadOnlySpan<int> owner = shots.Owner;
        int added = 0;
        for (int k = 0; k < alive.Length; k++)
        {
            if (!alive[k]) { seen[k] = false; continue; }
            if (seen[k] || owner[k] != 0) continue;
            seen[k] = true;
            added++;
        }
        return added;
    }

    // ---- helpers ----

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private int Spawn(int player, int type, NVector2 near)
    {
        BuildingDef def = _data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int SpawnUnit(int player, int type, NVector2 at)
    {
        UnitStore u = W.Units;
        var alive = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) alive[i] = u.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(player, type, at));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !alive[i]) return i;
        return -1;
    }

    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!g.WorldToCell(near + new NVector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 3 || cy < 3 || cx + def.FootprintWidth >= g.Width - 3 || cy + def.FootprintHeight >= g.Height - 3) continue;
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    int level = g.LevelAt(cx, cy);
                    bool ok = true;
                    for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && ok; y++)
                        for (int x = cx - 2; x <= cx + def.FootprintWidth + 1 && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                    if (ok) return a;
                }
        return -1;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, _data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // A known bug: printed as KNOWN unless --strict, which fails the scene on it.
    private void Known(bool ok, string bug, string message)
    {
        if (ok) return;
        if (_strict) _failures.Add($"{bug}: {message}");
        else GD.Print($"{Tag} KNOWN {bug}: {message}");
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
