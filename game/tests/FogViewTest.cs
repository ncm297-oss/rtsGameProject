using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;

namespace Rts.Game.Tests;

/// <summary>
/// M4-V4 on the real Match scene: the fog of war on screen. Per seed (1, 6) a match with bases and 40 units a side, the
/// armies attack-moved at each other, 900 ticks, one frame after each tick, and every frame: the fog texture's version is
/// the fog's and, after every update, its bytes are the fog's; uploads at most one per 4 ticks (+1); every unit view shown
/// exactly when <c>Fog.CanSeeUnit(0, slot)</c>, every building view exactly when <c>CanSeeBuilding</c>; hp bars only over
/// shown units; shots drawn only from visible cells (drawn + hidden = in flight); the minimap's fog layer equal to the
/// texture (alpha per cell). The minimap right-click: on a visible enemy's dot one Attack per selected unit, on a hidden
/// enemy's spot a Move. A Desert Archer shooting from outside a held Heavy Infantry's sight: its arrows hidden until they
/// fly into a visible cell, the archer never shown. Then 300 steady frames at <c>--units 500</c> (0 bytes in the view syncs), the hash twins, and a
/// <c>--no-fog</c> match (everything shown, the texture all visible, uploaded once).
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/FogViewTest.tscn</c> (seeds 1 and 6;
/// <c>-- --seed N</c> for one); prints "FOG VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with
/// <c>-- --shots &lt;dir&gt;</c> it also saves <c>fog-seedN-tickT.png</c> at ticks 2, 300 and 900 of each seed.</remarks>
public partial class FogViewTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private CombatViews _combat = null!;
    private ProjectileViews _shots = null!;
    private Minimap _mini = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private string? _shotsDir;

    // Per-seed tallies.
    private int _frames, _unitMismatch, _buildingMismatch, _barMismatch, _shotMismatch, _textureChecks, _miniChecks;
    private int _enemyShown, _enemyHidden, _flips, _buildingShownEnemy, _buildingHiddenEnemy, _hiddenShots, _drawnShots;
    private bool[] _lastShown = Array.Empty<bool>();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            ulong[] seeds = { 1, 6 };
            int at = Array.IndexOf(args, "--seed");
            if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong one)) seeds = new[] { one };
            at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shotsDir = args[at + 1];
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            CheckOptionsAndText();
            foreach (ulong seed in seeds) await Fog(seed);
            await HiddenShooter(seeds[0]);
            await Steady(seeds[0]);
            await NoFog(seeds[0]);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"FOG VIEW TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"FOG VIEW TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("FOG VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // ---- 0: the flag and the text ----

    private void CheckOptionsAndText()
    {
        Check(LaunchOptions.Parse(new[] { "--no-fog" }).NoFog, "--no-fog not parsed");
        Check(!LaunchOptions.Parse(new[] { "--no-combat" }).NoFog, "--no-fog set without the flag");
        LaunchOptions o = LaunchOptions.Parse(new[] { "--no-fog", "--units", "7" });
        Check(o.NoFog && o.UnitsPerPlayer == 7, "--no-fog swallowed the next flag");
        UiText? ui = UiText.Shared;
        Check(ui != null && ui.UnexploredPlacementText == "Unexplored", $"ui.json placement.unexplored is '{ui?.UnexploredPlacementText}'");
        string json = System.IO.File.ReadAllText(UiText.DefaultPath);
        var errors = new List<string>();
        Check(UiText.Parse(json.Replace("\"unexplored\": \"Unexplored\"", "\"unexplored_x\": \"Unexplored\""), errors) == null
            && errors.Any(e => e.Contains("placement.unexplored")), $"ui.json without placement.unexplored loads: {string.Join("; ", errors)}");
    }

    // ---- 1: the fog on screen, every frame checked ----

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // ticked by the test, one frame a tick
        _sim = _runner.Simulation!;
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _shots = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _screen = GetViewport().GetVisibleRect().Size;
        _lastShown = new bool[U.Capacity];
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private async Task Fog(ulong seed)
    {
        StartMatch(seed, "--units", "40", "--zoom", "60");
        Check(_fog.View != null && _fog.View.Enabled && _fog.View.Player == SelectionController.LocalPlayer, $"seed {seed}: fog not bound for the local player");
        Check(_fog.Image.GetFormat() == Image.Format.R8 && _fog.Image.GetWidth() == W.Fog.Width && _fog.Image.GetHeight() == W.Fog.Height,
            $"seed {seed}: texture {_fog.Image.GetFormat()} {_fog.Image.GetWidth()} x {_fog.Image.GetHeight()}");
        var terrain = (MeshInstance3D)_match.GetNode("World3D/TerrainView");
        Check(terrain.Mesh.SurfaceGetMaterial(0) is ShaderMaterial tm && tm.GetShaderParameter("fog_tex").As<Texture2D>() == _fog.Texture,
            $"seed {seed}: the terrain's material does not sample the fog texture");
        Check(_combat.CorpseMesh.Mesh.SurfaceGetMaterial(0) is ShaderMaterial cm && cm.GetShaderParameter("fog_tex").As<Texture2D>() == _fog.Texture,
            $"seed {seed}: the corpse material does not sample the fog texture");
        _sim.Tick();
        _sim.Tick();
        await Frame();
        await Shot($"fog-seed{seed}-tick{W.TickNumber}");
        await MinimapClicks(seed);

        // The armies at each other (player 0's way through commands, as the player's would be).
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            System.Numerics.Vector2 goal = U.Owner[i] == 0 ? east : west;
            _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), goal));
        }
        ResetTallies();
        int startTick = W.TickNumber, uploads0 = _fog.Uploads, lastVersion = -1;
        for (int t = 0; t < 900; t++)
        {
            _sim.Tick();
            await Frame();
            int ticks = W.TickNumber - startTick;
            CheckFrame(seed, ref lastVersion);
            Check(_fog.Uploads - uploads0 <= ticks / VisionConstants.UpdateInterval + 1, $"seed {seed}: {_fog.Uploads - uploads0} uploads over {ticks} ticks");
            if (ticks == 300 || ticks == 900) await Shot($"fog-seed{seed}-tick{W.TickNumber}");
        }
        GD.Print($"seed {seed}: {_frames} frames, {_fog.Uploads - uploads0} uploads over 900 ticks ({_textureChecks} texture and {_miniChecks} minimap checks); " +
            $"enemy unit-frames shown {_enemyShown} hidden {_enemyHidden}, {_flips} flips; enemy building-frames shown {_buildingShownEnemy} hidden {_buildingHiddenEnemy}; " +
            $"shots drawn {_drawnShots} hidden {_hiddenShots}; mismatches units {_unitMismatch} buildings {_buildingMismatch} bars {_barMismatch} shots {_shotMismatch}");
        Check(_unitMismatch == 0 && _buildingMismatch == 0 && _barMismatch == 0 && _shotMismatch == 0,
            $"seed {seed}: mismatches units {_unitMismatch} buildings {_buildingMismatch} bars {_barMismatch} shots {_shotMismatch}");
        Check(_enemyShown > 0 && _enemyHidden > 0 && _flips > 0, $"seed {seed}: enemies shown {_enemyShown} / hidden {_enemyHidden}, {_flips} flips: the fog never hid or showed one");
        Check(_buildingHiddenEnemy > 0, $"seed {seed}: the enemy Town Hall was never hidden");
        Check(_textureChecks >= 900 / VisionConstants.UpdateInterval - 1 && _miniChecks == _textureChecks, $"seed {seed}: {_textureChecks} texture checks, {_miniChecks} minimap checks");
        Twin(seed);
        await EndMatch();
    }

    private void ResetTallies()
    {
        _frames = _unitMismatch = _buildingMismatch = _barMismatch = _shotMismatch = _textureChecks = _miniChecks = 0;
        _enemyShown = _enemyHidden = _flips = _buildingShownEnemy = _buildingHiddenEnemy = _hiddenShots = _drawnShots = 0;
    }

    // What the views drew this frame against the fog (no tick ran since the frame: the test ticks before awaiting it).
    private void CheckFrame(ulong seed, ref int lastVersion)
    {
        _frames++;
        string at = $"seed {seed} tick {W.TickNumber}";
        FogView view = _fog.View!;
        int version = W.Fog.Version(0);
        if (!Check(view.TextureVersion == version, $"{at}: texture at version {view.TextureVersion}, fog at {version}")) return;
        if (version != lastVersion)
        {
            // An update: the uploaded image's bytes are the fog's, and the minimap's layer is the texture's.
            lastVersion = version;
            byte[] img = _fog.Image.GetData();
            ReadOnlySpan<byte> fog = W.Fog.Visibility(0);
            Check(fog.SequenceEqual(img), $"{at}: the texture's bytes differ from the fog's");
            _textureChecks++;
            Check(_mini.FogVersion == version, $"{at}: minimap fog layer at version {_mini.FogVersion}");
            byte[] layer = _mini.FogImage.GetData();
            int bad = 0;
            for (int c = 0; c < img.Length; c++)
                if (layer[c * 4 + 3] != FogView.MinimapAlpha(img[c]) || (layer[c * 4] | layer[c * 4 + 1] | layer[c * 4 + 2]) != 0) bad++;
            Check(bad == 0, $"{at}: {bad} minimap fog pixels differ from the texture");
            _miniChecks++;
        }
        for (int i = 0; i < U.Capacity; i++)
        {
            bool want = W.Fog.CanSeeUnit(0, i), shown = _units.IsShown(i);
            if (want != shown && _unitMismatch++ < 3) Check(false, $"{at}: unit {i} (owner {U.Owner[i]}, alive {U.Alive[i]}) shown {shown}, CanSeeUnit {want}");
            if (shown && _units.ViewOf(i) is { Visible: false }) Check(false, $"{at}: unit {i} marked shown but its node is hidden");
            if (!U.Alive[i] || U.Owner[i] == 0) continue;
            if (shown) _enemyShown++; else _enemyHidden++;
            if (shown != _lastShown[i]) _flips++;
            _lastShown[i] = shown;
        }
        BuildingStore b = W.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            bool want = W.Fog.CanSeeBuilding(0, i), shown = _buildings.IsShown(i);
            if (want != shown && _buildingMismatch++ < 3) Check(false, $"{at}: building {i} (owner {b.Owner[i]}) shown {shown}, CanSeeBuilding {want}");
            if (!b.Alive[i] || b.Owner[i] == 0) continue;
            if (shown) _buildingShownEnemy++; else _buildingHiddenEnemy++;
        }
        for (int k = 0; k < _combat.ShownBars; k++)
            if (!W.Fog.CanSeeUnit(0, _combat.BarSlot(k)) && _barMismatch++ < 3) Check(false, $"{at}: a bar over hidden unit {_combat.BarSlot(k)}");
        ProjectileStore s = W.Projectiles;
        if (_shots.Shown + _shots.HiddenShots != s.Count && _shotMismatch++ < 3) Check(false, $"{at}: {_shots.Shown} drawn + {_shots.HiddenShots} hidden, {s.Count} in flight");
        for (int k = 0; k < _shots.Shown; k++)
        {
            int slot = _shots.DrawnSlot(k);
            if (!W.Fog.IsVisible(0, FogView.CellOf(W.Fog.Width, W.Fog.Height, s.Position[slot])) && _shotMismatch++ < 3) Check(false, $"{at}: shot {slot} drawn in a cell player 0 doesn't see");
        }
        _drawnShots += _shots.Shown;
        _hiddenShots += _shots.HiddenShots;
    }

    // ---- 2: the minimap right-click ----

    private async Task MinimapClicks(ulong seed)
    {
        Rect2 rect = _mini.GetGlobalRect();
        int n = _sel.BoxSelect(Vector2.Zero, _screen, add: false);
        if (!Check(n > 0, $"seed {seed}: nothing selected by the screen box")) return;
        // A visible enemy whose dot no other enemy's dot overlaps, and a hidden one with no shown enemy within 6 cells.
        int seen = -1, hidden = -1;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] == 0) continue;
            bool shown = _fog.View!.ShowsUnit(i);
            if (shown && seen < 0 && _mini.Raster!.EnemyDotAt(U.Position[i], _fog.View.UnitShown, U.Position, U.Owner, 0) == i) seen = i;
            if (!shown && hidden < 0 && NearestShownEnemy(U.Position[i]) > 12f) hidden = i;
        }
        GD.Print($"seed {seed}: minimap clicks with {n} selected, visible enemy {seen}, hidden enemy {hidden}");
        if (!Check(seen >= 0 && hidden >= 0, $"seed {seed}: no clean visible ({seen}) or hidden ({hidden}) enemy for the minimap")) return;
        int attacks = _sel.IssuedCount(CommandKind.Attack), moves = _sel.IssuedCount(CommandKind.Move), commands = _runner.Recorder!.CommandCount;
        RightClick(rect, U.Position[seen]);
        Check(_sel.IssuedCount(CommandKind.Attack) - attacks == n && _sel.IssuedCount(CommandKind.Move) == moves,
            $"seed {seed}: right-click on visible enemy {seen}'s dot issued {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks / {_sel.IssuedCount(CommandKind.Move) - moves} Moves for {n}");
        _sim.Tick();
        int good = 0;
        for (int k = commands; k < _runner.Recorder.CommandCount; k++)
        {
            Command c = _runner.Recorder.CommandAt(k);
            if (c.Kind == CommandKind.Attack && c.Target.Index == seen && c.Target.Generation == U.Generation[seen] && c.Player == 0) good++;
        }
        Check(good == n, $"seed {seed}: {good} recorded Attacks on enemy {seen}, want {n}");
        attacks = _sel.IssuedCount(CommandKind.Attack);
        RightClick(rect, U.Position[hidden]);
        Check(_sel.IssuedCount(CommandKind.Move) - moves == n && _sel.IssuedCount(CommandKind.Attack) == attacks,
            $"seed {seed}: right-click on hidden enemy {hidden}'s spot issued {_sel.IssuedCount(CommandKind.Move) - moves} Moves / {_sel.IssuedCount(CommandKind.Attack) - attacks} Attacks for {n}");
        // The 3D view: a hidden enemy is never an Attack target (its pixel, if on screen, means the ground).
        int checkedPixels = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] == 0 || _fog.View!.ShowsUnit(i) || !_sel.TryScreenPosition(i, out Vector2 px)) continue;
            if (px.X < 0 || px.Y < 0 || px.X > _screen.X || px.Y > _screen.Y) continue;
            checkedPixels++;
            if (_sel.EnemyAt(px, out EntityHandle t, out bool isB) && !isB && t.Index == i) Check(false, $"seed {seed}: hidden enemy {i} is a click target");
        }
        _sim.Tick();
        await Frame();
        GD.Print($"seed {seed}: {checkedPixels} hidden enemies on screen checked against the 3D pick");
    }

    private float NearestShownEnemy(System.Numerics.Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] != 0 && _fog.View!.ShowsUnit(i)) best = Math.Min(best, System.Numerics.Vector2.Distance(p, U.Position[i]));
        return best;
    }

    private void RightClick(Rect2 rect, System.Numerics.Vector2 ground)
    {
        System.Numerics.Vector2 px = _mini.Fit.ToPixel(ground);
        Vector2 at = rect.Position + new Vector2(px.X, px.Y);
        Push(Button(MouseButton.Right, true, at));
        Push(Button(MouseButton.Right, false, at));
    }

    // ---- 3: a shooter the player doesn't see ----

    // A Desert Archer (range 14 m, sight 18 m) 5 cells across and 5 down from a held Heavy Infantry (sight 14 m: cells with
    // dx^2 + dy^2 <= 49): 14.1 m apart, in the archer's reach and outside the infantry's circle, on one level. Its arrows
    // start in a cell player 0 doesn't see and are drawn only once they fly into one it does (docs/03 Known limits (1)).
    private async Task HiddenShooter(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--zoom", "30");
        Rts.Sim.Map.NavGrid g = W.NavGrid;
        int hx = -1, hy = -1;
        int cx = g.Width / 2, cy = g.Height / 2;
        for (int r = 0; r < 40 && hx < 0; r++)
            for (int y = cy - r; y <= cy + r && hx < 0; y++)
                for (int x = cx - r; x <= cx + r && hx < 0; x++)
                {
                    bool ok = true;
                    for (int d = -1; d <= 6 && ok; d++)
                        for (int e = -1; e <= 6 && ok; e++)
                            ok = g.IsPassable(x + d, y + e) && W.Heightmap.LevelAt(x + d, y + e) == W.Heightmap.LevelAt(x, y) && !W.Heightmap.IsRamp(x + d, y + e);
                    if (ok) { hx = x; hy = y; }
                }
        if (!Check(hx >= 0, "hidden shooter: no flat 8 x 8 patch near the centre")) { await EndMatch(); return; }
        _sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_heavy_infantry"), g.CellCenter(hx, hy)));
        _sim.Enqueue(Command.SpawnUnit(1, _data.FindUnit("whirlwind_desert_archer"), g.CellCenter(hx + 5, hy + 5)));
        _sim.Tick();
        _sim.Tick();
        int hi = -1, archer = -1;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) { if (U.Owner[i] == 0) hi = i; else archer = i; }
        if (!Check(hi >= 0 && archer >= 0, "hidden shooter: units not spawned")) { await EndMatch(); return; }
        var hiH = new EntityHandle(hi, U.Generation[hi]);
        _sim.Enqueue(Command.HoldPosition(0, hiH));
        _sim.Enqueue(Command.Attack(1, new EntityHandle(archer, U.Generation[archer]), hiH, isBuilding: false));
        _camera.SetFocus(U.Position[hi].X, U.Position[hi].Y);
        ResetTallies();
        int lastVersion = -1, archerShown = 0;
        for (int t = 0; t < 240 && U.Alive[hi]; t++)
        {
            _sim.Tick();
            await Frame();
            CheckFrame(seed, ref lastVersion);
            if (_units.IsShown(archer)) archerShown++;
        }
        GD.Print($"hidden shooter (seed {seed}): {_frames} frames, shots drawn {_drawnShots} hidden {_hiddenShots}, archer shown {archerShown} frames, " +
            $"infantry hp {U.Hp[hi]}; mismatches units {_unitMismatch} shots {_shotMismatch}");
        Check(_hiddenShots > 0 && _drawnShots > 0, $"hidden shooter: shots drawn {_drawnShots}, hidden {_hiddenShots} (want both)");
        Check(archerShown == 0, $"hidden shooter: the archer was shown {archerShown} frames");
        Check(_unitMismatch == 0 && _shotMismatch == 0 && _barMismatch == 0, $"hidden shooter: mismatches units {_unitMismatch} shots {_shotMismatch} bars {_barMismatch}");
        Twin(seed);
        await EndMatch();
    }

    // ---- 4: 300 steady frames at --units 500, 0 bytes ----

    private async Task Steady(ulong seed)
    {
        StartMatch(seed, "--units", "500");
        _sim.Tick();
        _sim.Tick();
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
        // Warm-up: every view and the minimap through a few hundred ticks (nodes, pools, the JIT).
        for (int t = 0; t < 240; t++)
        {
            _sim.Tick();
            SyncAll(0.5f);
        }
        long bytes = 0;
        int uploads0 = _fog.Uploads, ticks0 = W.TickNumber, flips = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            SyncAll(f % 3 / 3f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            for (int i = 0; i < U.Capacity; i++)
                if (_units.IsShown(i) != W.Fog.CanSeeUnit(0, i)) flips++;
        }
        int uploads = _fog.Uploads - uploads0, ticks = W.TickNumber - ticks0;
        GD.Print($"steady (seed {seed}, {U.Count} units): 300 frames, {ticks} ticks, {uploads} uploads: {bytes} bytes");
        Check(bytes == 0, $"steady: 300 frames at --units 500 allocated {bytes} bytes");
        Check(flips == 0, $"steady: {flips} unit views disagreed with CanSeeUnit");
        Check(uploads <= ticks / VisionConstants.UpdateInterval + 1 && uploads > 0, $"steady: {uploads} uploads over {ticks} ticks");
        Twin(seed);
        await EndMatch();
    }

    // One frame of every fog-reading view, as their _Process would run it (plus the minimap's 5 Hz refresh).
    private void SyncAll(float alpha)
    {
        _fog.Sync(W);
        _units.Sync(W, alpha, 0.016f);
        _buildings.Sync(W);
        _combat.Sync(W, alpha);
        _shots.Sync(W, alpha);
        _mini.SyncFog(W);
        if (W.TickNumber % Minimap.RefreshTicks == 0) _mini.Refresh(_sim);
    }

    // ---- 5: --no-fog ----

    private async Task NoFog(ulong seed)
    {
        StartMatch(seed, "--units", "40", "--no-fog");
        for (int t = 0; t < 120; t++)
        {
            _sim.Tick();
            if (t % 10 == 0) await Frame();
        }
        await Frame();
        FogView view = _fog.View!;
        Check(!view.Enabled && _fog.Uploads == 1, $"--no-fog: enabled {view.Enabled}, {_fog.Uploads} uploads");
        Check(_fog.Image.GetData().All(b => b == VisionConstants.Visible), "--no-fog: the texture is not all visible");
        Check(_mini.FogImage.GetData().Where((b, k) => k % 4 == 3).All(a => a == 0), "--no-fog: the minimap fog layer is not clear");
        int wrongUnits = 0, wrongBuildings = 0, hiddenByFog = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (_units.IsShown(i) != U.Alive[i]) wrongUnits++;
            if (U.Alive[i] && !W.Fog.CanSeeUnit(0, i)) hiddenByFog++;
        }
        for (int i = 0; i < W.Buildings.Capacity; i++)
            if (_buildings.IsShown(i) != W.Buildings.Alive[i]) wrongBuildings++;
        GD.Print($"--no-fog (seed {seed}): {U.Count} units all shown ({hiddenByFog} the fog would hide), {W.Buildings.Count} buildings");
        Check(wrongUnits == 0 && wrongBuildings == 0, $"--no-fog: {wrongUnits} unit and {wrongBuildings} building views not shown as alive");
        Check(hiddenByFog > 0, "--no-fog: no unit the fog would hide, so the check proved nothing");
        await EndMatch();
    }

    // ---- helpers ----

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        // The army only (the start workers stand by their Town Hall).
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return n > 0 ? sum / n : new System.Numerics.Vector2(float.MaxValue);
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private static InputEventMouseButton Button(MouseButton b, bool pressed, Vector2 at) =>
        new() { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0 };

    // Windowed only: the frame as drawn, saved as <name>.png in --shots.
    private async Task Shot(string name)
    {
        if (_shotsDir == null || DisplayServer.GetName() == "headless") return;
        await Frame();
        await Frame();
        string path = $"{_shotsDir}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
