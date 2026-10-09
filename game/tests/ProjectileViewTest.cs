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
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M4-V3 on the real Match scene: projectiles and impact marks. Per seed (1, 6) a mixed fight near the map centre (Heavy
/// Infantry, Crossbowmen, Sappers and Catapults against Raiders, Desert Archers and Priests), started by A + click on an
/// enemy through the viewport, run by the match's own <see cref="SimRunner"/> at 1x and then 8x (30 fps, so a frame runs
/// several ticks). Every frame: one drawn instance per live projectile slot; each within one tick's step of its slot's
/// <c>Position</c>; an aimed shot on the segment <c>PrevPosition → Position</c> at the launch height; a lob on the ground at
/// launch and on its last drawn tick and more than 0.5 m up mid-flight; a mark for every landing, drawn the frame after its
/// tick and gone after its life; nothing drawn after the fight; the hash twin. Then a 200 v 200 brawl from the start blocks
/// (0 bytes over 300 frames with 100+ shots in flight) and a 500 v 500 one past the projectile store's capacity.
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/ProjectileViewTest.tscn</c> (seeds 1 and 6;
/// <c>-- --seed N</c> for one); prints "PROJECTILE VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with
/// <c>-- --shots &lt;dir&gt;</c> it also saves the fight with shots in the air (<c>projectiles-seedN.png</c>).</remarks>
public partial class ProjectileViewTest : Node
{
    private const float Eps = 1e-3f;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private ProjectileViews _views = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private string? _shots;

    // Filled by the test's own Ticked handler (subscribed after the views', so the views have taken each tick first).
    private int _ticksSinceCheck, _impactsSinceCheck;
    private long _lastCheckedTick;

    // Per-seed tallies.
    private int _frames, _framesMulti, _maxInFlight, _aimedSamples, _lobLaunch, _lobLanding, _lobMid, _marksSeen;
    private float _lobMidLowest = float.MaxValue, _lobEndHighest;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            ulong[] seeds = { 1, 6 };
            int at = Array.IndexOf(args, "--seed");
            if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong one)) seeds = new[] { one };
            at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            foreach (ulong seed in seeds) await Fight(seed);
            await SteadyBrawl(seeds[0]);
            await Overflow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Engine.MaxFps = 0;
        foreach (string f in _failures.Take(60)) GD.Print($"PROJECTILE VIEW TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"PROJECTILE VIEW TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("PROJECTILE VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--no-bases", "--mute", "--no-fog" }; // every shot in flight is checked (M4-V4)
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // ticked by the test until the fight starts
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _views = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _screen = GetViewport().GetVisibleRect().Size;
        _runner.Ticked += OnTicked;
    }

    private async Task EndMatch()
    {
        _runner.Ticked -= OnTicked;
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void OnTicked(Simulation sim)
    {
        _ticksSinceCheck++;
        _impactsSinceCheck += sim.World.Impacts.Length;
        // The views took this tick's landings before this handler ran (subscribed first, in Match.Start).
        Check(_views.Marks.LastCollectedTick == sim.World.TickNumber || sim.World.Impacts.Length == 0,
            $"tick {sim.World.TickNumber}: {sim.World.Impacts.Length} impacts not collected by the views");
    }

    // ---- 1: the mixed fight, every frame checked ----

    private async Task Fight(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--zoom", "26");
        System.Numerics.Vector2 mid = Stage();
        _sim.Tick();
        _sim.Tick();
        Check(U.Count == 30, $"seed {seed}: {U.Count} units staged, want 30");
        _camera.SetFocus(mid.X, mid.Y);
        await Frame();
        await Frame();

        // A + click on an enemy with every own unit selected (the player's way); the enemy side attack-moves back.
        int n = _sel.BoxSelect(Vector2.Zero, _screen, add: false);
        Check(n >= 10, $"seed {seed}: only {n} own units selected by the screen box");
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px)) throw new InvalidOperationException($"seed {seed}: no clean enemy pixel");
        Key(Godot.Key.A);
        LeftClick(px);
        Check(_sel.IssuedCount(CommandKind.Attack) >= n, $"seed {seed}: A + click on enemy {enemy.Index} issued {_sel.IssuedCount(CommandKind.Attack)} Attacks for {n}");
        System.Numerics.Vector2 west = Centroid(0);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 1) _sim.Enqueue(Command.AttackMove(1, new EntityHandle(i, U.Generation[i]), west));
        GD.Print($"seed {seed}: A + click on enemy {enemy.Index} with {n} selected");

        ResetTallies();
        _lastCheckedTick = W.TickNumber;
        Engine.MaxFps = 30; // 8x then runs ~5 ticks a frame, as on a slow machine
        _runner.GameSpeed = 1.0;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        bool shot = false;
        int startTick = W.TickNumber, end = -1;
        while (W.TickNumber < startTick + 6000)
        {
            await Frame();
            CheckFrame(seed);
            if (!shot && _views.ShownAimed >= 5 && LobHigh()) shot = await Shot($"projectiles-seed{seed}");
            if (W.TickNumber > startTick + 160 && _runner.GameSpeed < 8.0) _runner.GameSpeed = 8.0;
            bool over = Centroid(0).X == float.MaxValue || Centroid(1).X == float.MaxValue || (W.TickNumber > startTick + 3000);
            if (over && end < 0) end = W.TickNumber;
            if (end >= 0 && W.Projectiles.Count == 0 && _views.Marks.Count == 0 && W.TickNumber > end + 20) break;
        }
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        Engine.MaxFps = 0;
        // After the fight: nothing drawn.
        await Frame();
        _views.Sync(W, 1f);
        int marks = 0;
        for (int i = 0; i < _views.Marks.Capacity; i++) if (_views.MarkTransform(i).Basis.Scale != Vector3.Zero) marks++;
        Check(_views.Shown == 0 && _views.AimedMesh.VisibleInstanceCount == 0 && _views.LobMesh.VisibleInstanceCount == 0 && marks == 0,
            $"seed {seed} after the fight: {_views.Shown} shots, {_views.AimedMesh.VisibleInstanceCount} / {_views.LobMesh.VisibleInstanceCount} instances, {marks} marks drawn");
        GD.Print($"seed {seed}: fight over at tick {end} ({W.Units.Count} units left); {_frames} frames ({_framesMulti} ran 2+ ticks), max {_maxInFlight} in flight, " +
            $"{_views.Tracker.Started} shots tracked ({_views.Tracker.Reused} slot reuses seen); aimed samples {_aimedSamples}; lob samples launch {_lobLaunch} / mid {_lobMid} / landing {_lobLanding}, " +
            $"lowest mid {_lobMidLowest:F2} m, highest end {_lobEndHighest:F2} m; marks {_views.Marks.Added} ({_marksSeen} landings), {_views.Marks.Replaced} replaced");
        Check(_views.Tracker.Started > 30 && _aimedSamples > 100, $"seed {seed}: too few shots ({_views.Tracker.Started}, {_aimedSamples} aimed samples)");
        Check(_lobLaunch > 0 && _lobMid > 0 && _lobLanding > 0, $"seed {seed}: lob samples launch {_lobLaunch} mid {_lobMid} landing {_lobLanding}");
        Check(_framesMulti > 20, $"seed {seed}: only {_framesMulti} frames ran several ticks (8x)");
        Check(_views.Marks.Added == _marksSeen && _marksSeen > 30, $"seed {seed}: {_marksSeen} landings, {_views.Marks.Added} marks");
        Twin(seed);
        await EndMatch();
    }

    private void ResetTallies()
    {
        _frames = _framesMulti = _maxInFlight = _aimedSamples = _lobLaunch = _lobLanding = _lobMid = _marksSeen = 0;
        _lobMidLowest = float.MaxValue;
        _lobEndHighest = 0f;
        _ticksSinceCheck = _impactsSinceCheck = 0;
    }

    // The state the views drew last frame (no tick ran since: the runner ticks in its own _Process, before the views').
    private void CheckFrame(ulong seed)
    {
        long tick = W.TickNumber;
        _frames++;
        if (_ticksSinceCheck >= 2) _framesMulti++;
        ProjectileStore s = W.Projectiles;
        _maxInFlight = Math.Max(_maxInFlight, s.Count);
        string at = $"seed {seed} tick {tick} ({_runner.GameSpeed:0}x)";
        Check(_views.Shown == s.Count && _views.ShownAimed + _views.ShownLobs == s.Count, $"{at}: {_views.Shown} drawn ({_views.ShownAimed} + {_views.ShownLobs}), {s.Count} in flight");
        int bad = 0;
        for (int k = 0; k < _views.Shown && k < s.Capacity; k++)
        {
            int i = _views.DrawnSlot(k);
            if (!s.Alive[i]) { if (bad++ < 3) Check(false, $"{at}: drawn shot {k} is dead slot {i}"); continue; }
            Vector3 o = _views.DrawnAt(k);
            var xz = new System.Numerics.Vector2(o.X, o.Z);
            System.Numerics.Vector2 p = s.Position[i], q = s.PrevPosition[i];
            float step = System.Numerics.Vector2.Distance(p, q);
            if (System.Numerics.Vector2.Distance(xz, p) > step + Eps && bad++ < 3) Check(false, $"{at}: slot {i} drawn at {xz}, {System.Numerics.Vector2.Distance(xz, p):F3} m from its position (step {step:F3})");
            float ground = TerrainHeight.At(W.Heightmap, o.X, o.Z);
            float h = o.Y - ground;
            if (!_views.DrawnIsLob(k))
            {
                _aimedSamples++;
                if (SegmentDistance(xz, q, p) > Eps && bad++ < 3) Check(false, $"{at}: aimed slot {i} drawn {SegmentDistance(xz, q, p):F4} m off its segment {q} -> {p}");
                if (MathF.Abs(h - ProjectileViews.LaunchHeight) > Eps && bad++ < 3) Check(false, $"{at}: aimed slot {i} {h:F3} m above the ground");
                continue;
            }
            float t = _views.Tracker.Progress(i, xz);
            bool launch = p == q;
            bool landing = !launch && System.Numerics.Vector2.Distance(s.Target[i], p) <= step + Eps;
            if (launch || landing)
            {
                if (launch) _lobLaunch++; else _lobLanding++;
                _lobEndHighest = MathF.Max(_lobEndHighest, h);
                if (h > 0.2f && bad++ < 3) Check(false, $"{at}: lob slot {i} {h:F3} m up at its {(launch ? "launch" : "landing")}");
            }
            if (t >= 0.4f && t <= 0.6f)
            {
                _lobMid++;
                _lobMidLowest = MathF.Min(_lobMidLowest, h);
                if (h <= 0.5f && bad++ < 3) Check(false, $"{at}: lob slot {i} only {h:F3} m up mid-flight (t {t:F2}, length {_views.Tracker.Length[i]:F1})");
            }
        }

        // Marks: every landing since the last check is a mark, every live mark drawn, none drawn past its life.
        ImpactMarks m = _views.Marks;
        _marksSeen += _impactsSinceCheck;
        for (int i = 0; i < m.Capacity; i++)
        {
            Transform3D tr = _views.MarkTransform(i);
            if (!m.Active[i])
            {
                if (tr.Basis.Scale != Vector3.Zero && bad++ < 3) Check(false, $"{at}: free mark slot {i} still drawn");
                continue;
            }
            if (!m.Drawn[i] && bad++ < 3) Check(false, $"{at}: mark {i} (tick {m.StartTick[i]}) not drawn the frame after its tick");
            if (m.ExpiresAt[i] <= tick && m.StartTick[i] <= _lastCheckedTick && bad++ < 3) Check(false, $"{at}: mark {i} from tick {m.StartTick[i]} still up past {m.ExpiresAt[i]}");
            if ((MathF.Abs(tr.Origin.X - m.Position[i].X) > Eps || MathF.Abs(tr.Origin.Z - m.Position[i].Y) > Eps || tr.Basis.Scale.X <= 0f) && bad++ < 3)
                Check(false, $"{at}: mark {i} drawn at {tr.Origin}, landing at {m.Position[i]}");
        }
        _ticksSinceCheck = _impactsSinceCheck = 0;
        _lastCheckedTick = tick;
    }

    // A lob drawn between a third and two thirds of its arc (high in the air), for the screenshot.
    private bool LobHigh()
    {
        for (int k = 0; k < _views.Shown; k++)
        {
            if (!_views.DrawnIsLob(k)) continue;
            Vector3 o = _views.DrawnAt(k);
            float t = _views.Tracker.Progress(_views.DrawnSlot(k), new System.Numerics.Vector2(o.X, o.Z));
            if (t > 0.33f && t < 0.67f) return true;
        }
        return false;
    }

    private static float SegmentDistance(System.Numerics.Vector2 x, System.Numerics.Vector2 a, System.Numerics.Vector2 b)
    {
        System.Numerics.Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 0f ? Math.Clamp(System.Numerics.Vector2.Dot(x - a, ab) / len2, 0f, 1f) : 0f;
        return System.Numerics.Vector2.Distance(x, a + ab * t);
    }

    // ---- 2: 200 v 200 at the centre: 0 bytes a frame from the first volley of 100+ shots ----
    // `--units 200` start blocks were tried first: the armies cross the map strung out and never had 100 in the air at
    // once (500 v 500: 94), so both brawls are staged as long lines either side of the centre. Shots come in volleys
    // (shipped flights are a third of a cooldown or less), so the 300 frames start at the first tick with 100 in flight.

    private async Task SteadyBrawl(ulong seed)
    {
        StartMatch(seed, "--units", "0");
        StageBlocks(200, rows: 61);
        _sim.Tick();
        _sim.Tick();
        Check(U.Count == 400, $"brawl 200: {U.Count} units staged");
        AttackMoveAll();
        int ticks = 0, most = 0;
        while (W.Projectiles.Count < 100 && ticks < 2000) { Tick(); ticks++; most = Math.Max(most, W.Projectiles.Count); }
        Check(W.Projectiles.Count >= 100, $"brawl 200: at most {most} shots in flight in {ticks} ticks");
        long bytes = 0;
        int least = int.MaxValue;
        long sum = 0;
        long trackerStarted = _views.Tracker.Started, marksAdded = _views.Marks.Added;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            _views.CollectTick(W); // what SimRunner.Ticked does
            _views.Sync(W, f % 3 / 3f);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            least = Math.Min(least, W.Projectiles.Count);
            most = Math.Max(most, W.Projectiles.Count);
            sum += W.Projectiles.Count;
            Check(_views.Shown == W.Projectiles.Count, $"brawl 200 frame {f}: {_views.Shown} drawn, {W.Projectiles.Count} in flight");
        }
        GD.Print($"brawl 200 v 200 (seed {seed}): {ticks} ticks to 100 in flight; 300 frames (100 ticks) with {least}-{most} in flight (mean {sum / 300.0:F0}), " +
            $"{_views.Tracker.Started - trackerStarted} shots started, {_views.Marks.Added - marksAdded} marks: {bytes} bytes");
        Check(bytes == 0, $"brawl 200: 300 steady frames allocated {bytes} bytes");
        Check(most >= 100 && sum / 300.0 >= 30, $"brawl 200: {least}-{most} in flight (mean {sum / 300.0:F0}) during the 300 frames");
        Twin(seed);
        await EndMatch();
    }

    // ---- 3: 500 v 500, more shooters than projectile slots ----

    private async Task Overflow()
    {
        StartMatch(2, "--units", "0");
        StageBlocks(500, rows: 101);
        _sim.Tick();
        _sim.Tick();
        Check(U.Count == 1000, $"brawl 500: {U.Count} units staged");
        AttackMoveAll();
        int cap = W.Projectiles.Capacity, full = 0, most = 0;
        for (int t = 0; t < 900 && full < 40; t++)
        {
            Tick();
            _views.Sync(W, 0.5f);
            most = Math.Max(most, W.Projectiles.Count);
            if (W.Projectiles.Count == cap) full++;
            if (!Check(_views.Shown == W.Projectiles.Count && _views.Tracker.Count == W.Projectiles.Count && W.Projectiles.Count <= cap,
                $"brawl 500 tick {W.TickNumber}: {_views.Shown} drawn, {_views.Tracker.Count} tracked, {W.Projectiles.Count} in flight of {cap}")) break;
        }
        GD.Print($"brawl 500 v 500: capacity {cap}, most in flight {most}, {full} ticks full; {_views.Tracker.Started} shots drawn; marks {_views.Marks.Added} ({_views.Marks.Replaced} replaced, {_views.Marks.Count} up)");
        Check(full > 0, $"brawl 500: the store never filled (most {most} of {cap})");
        Twin(2);
        await EndMatch();
    }

    // A tick the way SimRunner runs one: then its Ticked handlers.
    private void Tick()
    {
        _sim.Tick();
        _views.CollectTick(W);
    }

    private void AttackMoveAll()
    {
        System.Numerics.Vector2[] c = { Centroid(0), Centroid(1) };
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), c[1 - U.Owner[i]]));
    }

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
    }

    // ---- staging ----

    // Malazan west of the central cell, Whirlwind east: (type, nearest and farthest column from the centre, count).
    private System.Numerics.Vector2 Stage()
    {
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        (string Type, int Near, int Far, int Count)[][] groups =
        {
            new[] { ("malazan_heavy_infantry", 3, 4, 4), ("malazan_crossbowman", 5, 7, 5), ("malazan_sapper", 5, 6, 3), ("malazan_catapult", 8, 10, 3) },
            new[] { ("whirlwind_raider", 3, 4, 4), ("whirlwind_desert_archer", 5, 7, 8), ("whirlwind_priest", 6, 7, 3) },
        };
        var used = new HashSet<int>();
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1;
            foreach ((string key, int near, int far, int count) in groups[p])
            {
                int type = _data.FindUnit(key), placed = 0;
                for (int ring = 0; ring < 30 && placed < count; ring++)
                    for (int c = 0; c < G.Width * G.Height && placed < count; c++)
                    {
                        int x = c % G.Width, y = c / G.Width, dx = (x - cx) * dir;
                        if (dx < near || dx > far || Math.Abs(y - cy) != ring || !(field.CostAt(c) <= 24f) || !used.Add(c)) continue;
                        _sim.Enqueue(Command.SpawnUnit(p, type, G.CellCenter(x, y)));
                        placed++;
                    }
                if (placed < count) throw new InvalidOperationException($"only {placed} spots for {key}");
            }
        }
        return G.CellCenter(cx, cy);
    }

    // Shooter-heavy lines either side of the central cell, one unit a cell, `rows` long, from 2 columns out.
    private static readonly string[][] BlockMix =
    {
        new[] { "malazan_crossbowman", "malazan_crossbowman", "malazan_cadre_mage", "malazan_crossbowman", "malazan_sapper", "malazan_catapult", "malazan_heavy_infantry" },
        new[] { "whirlwind_desert_archer", "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_desert_archer", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_raider" },
    };

    private void StageBlocks(int perSide, int rows)
    {
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1, placed = 0;
            for (int dx = 2; dx < G.Width && placed < perSide; dx++)
                for (int dy = 0; dy < rows && placed < perSide; dy++)
                {
                    int x = cx + dir * dx, y = cy + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
                    if ((uint)x >= (uint)G.Width || (uint)y >= (uint)G.Height || !(field.CostAt(y * G.Width + x) < 200f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(p, _data.FindUnit(BlockMix[p][placed % BlockMix[p].Length]), G.CellCenter(x, y)));
                    placed++;
                }
            if (placed < perSide) throw new InvalidOperationException($"only {placed} block spots for player {p}");
        }
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == player) { sum += U.Position[i]; n++; }
        return n > 0 ? sum / n : new System.Numerics.Vector2(float.MaxValue);
    }

    // ---- input helpers (as AttackOrderViewTest) ----

    private bool InPlayArea(Vector2 p) => p.X > 30 && p.X < _screen.X - 30 && p.Y > 60 && p.Y < _screen.Y - 270;

    private int UnitUnder(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        return UnitPicker.PickRay(U.Alive, U.PrevPosition, U.Position, U.Radius, W.Heightmap, (float)_runner.Alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out _);
    }

    private bool TryEnemyPixel(out EntityHandle enemy, out Vector2 px)
    {
        for (int i = 0; i < U.Capacity; i++)
        {
            // Only an enemy player 0 sees: an Attack on an unseen one is dropped since M4-3a (the M4-VH1 sweep).
            if (!U.Alive[i] || U.Owner[i] != 1 || !W.Fog.CanSeeUnit(0, i)) continue;
            if (!_sel.TryScreenPosition(i, out px) || !InPlayArea(px) || UnitUnder(px) != i) continue;
            if (!_sel.EnemyAt(px, out enemy, out bool b) || b || enemy.Index != i) continue;
            return true;
        }
        enemy = default;
        px = default;
        return false;
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    // Windowed only: the frame as drawn, saved as <name>.png in --shots. True once saved.
    private async Task<bool> Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return true;
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path} ({_views.ShownAimed} aimed, {_views.ShownLobs} lobs in the air)");
        await Task.CompletedTask;
        return true;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
