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
/// QA M4-V3 (2026-10-08-1435) on the real Match scene, the attacks the developer's <c>ProjectileViewTest</c> doesn't make.
/// (1) The real <c>--units 500</c> start blocks, combat on, everyone attack-moved at the other army: from the first shot to
/// the end of 900 ticks, every frame's <c>CollectTick</c> + <c>Sync</c> allocates 0 bytes, drawn == in flight == tracked,
/// and the hash twin holds. (2) 500 v 500 archer-heavy lines past the projectile store's capacity, five ticks a frame (8x
/// at 30 fps): 0 bytes while the store is full, every aimed shot on its segment even for the longest (re-led) step, and the
/// no impact mark's first drawn frame is fully transparent (BUG-0221; its age read before Sync marks it drawn). (3) Render alpha
/// outside [0, 1] and NaN never extrapolate; <c>Sync</c> leaves the sim hash unchanged.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV7Test.tscn</c>; prints "QA M4-V3 TEST PASS".</remarks>
public partial class QaV7Test : Node
{
    private const float Eps = 1e-3f;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private ProjectileViews _views = null!;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            await StartBlocks500(3);
            await ArcherLinesOverflow(5);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M4-V3 TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"QA M4-V3 TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("QA M4-V3 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--no-bases", "--mute", "--no-hud", "--no-fog" }; // every shot in flight is checked (M4-V4)
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks
        _sim = _runner.Simulation!;
        _views = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        _views.Runner = null; // the test calls CollectTick / Sync itself, exactly once each
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void Tick()
    {
        _sim.Tick();
        _views.CollectTick(W); // what SimRunner.Ticked does
    }

    private void AttackMoveAll()
    {
        System.Numerics.Vector2[] c = { Centroid(0), Centroid(1) };
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), c[1 - U.Owner[i]]));
    }

    // ---- (1) the real --units 500 start ----

    private async Task StartBlocks500(ulong seed)
    {
        StartMatch(seed, "--units", "500");
        Tick();
        Tick();
        Check(U.Count == 1000, $"units 500: {U.Count} units after the spawn ticks");
        AttackMoveAll();
        int waited = 0;
        while (W.Projectiles.Count == 0 && waited < 3000) { Tick(); waited++; _views.Sync(W, 0.5f); }
        Check(W.Projectiles.Count > 0, $"units 500: no shot in {waited} ticks");
        long bytes = 0;
        int frames = 0, most = 0, startedBefore = (int)_views.Tracker.Started;
        long marksBefore = _views.Marks.Added;
        for (int t = 0; t < 900; t++)
        {
            _sim.Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            _views.CollectTick(W);
            for (int f = 0; f < 2; f++)
            {
                _views.Sync(W, f == 0 ? 0.25f : 0.75f);
                frames++;
            }
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            most = Math.Max(most, W.Projectiles.Count);
            if (!Check(_views.Shown == W.Projectiles.Count && _views.Tracker.Count == W.Projectiles.Count,
                    $"units 500 tick {W.TickNumber}: {_views.Shown} drawn, {_views.Tracker.Count} tracked, {W.Projectiles.Count} in flight")) break;
        }
        GD.Print($"units 500 (seed {seed}): first shot after {waited} ticks; 900 ticks / {frames} frames, most {most} in flight of {W.Projectiles.Capacity}, " +
            $"{_views.Tracker.Started - startedBefore} shots, {_views.Marks.Added - marksBefore} marks ({_views.Marks.Replaced} replaced): {bytes} bytes in CollectTick + Sync");
        Check(bytes == 0, $"units 500: {bytes} bytes over 900 ticks + {frames} frames");
        Twin($"units 500 seed {seed}");
        await EndMatch();
    }

    // ---- (2) archer lines past the store's capacity, 5 ticks a frame ----

    private static readonly string[][] Mix =
    {
        new[] { "malazan_crossbowman", "malazan_crossbowman", "malazan_crossbowman", "malazan_cadre_mage", "malazan_catapult", "malazan_sapper" },
        new[] { "whirlwind_desert_archer", "whirlwind_desert_archer", "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_desert_archer", "whirlwind_raider" },
    };

    private async Task ArcherLinesOverflow(ulong seed)
    {
        StartMatch(seed, "--units", "0");
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1, placed = 0;
            for (int dx = 3; dx < G.Width && placed < 500; dx++)
                for (int dy = 0; dy < 101 && placed < 500; dy++)
                {
                    int x = cx + dir * dx, y = cy + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
                    if ((uint)x >= (uint)G.Width || (uint)y >= (uint)G.Height || !(field.CostAt(y * G.Width + x) < 200f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(p, _data.FindUnit(Mix[p][placed % Mix[p].Length]), G.CellCenter(x, y)));
                    placed++;
                }
        }
        Tick();
        Tick();
        Check(U.Count == 1000, $"lines: {U.Count} units");
        AttackMoveAll();
        int cap = W.Projectiles.Capacity, full = 0, most = 0, frames = 0, newMarks = 0, invisibleFirst = 0, offSegment = 0;
        float longestStep = 0f, longestLobGap = 0f;
        long fullBytes = 0;
        var undrawn = new bool[_views.Marks.Capacity];
        var firstAge = new float[_views.Marks.Capacity];
        var lastPos = new System.Numerics.Vector2[cap];
        var wasAlive = new bool[cap];
        for (int t = 0; t < 1200; t += 5)
        {
            bool hitCap = false;
            long viewBytes = 0;
            for (int k = 0; k < 5; k++)
            {
                // A lob's last drawn point vs where it lands (one tick later): the arc's visual gap.
                for (int i = 0; i < cap; i++) { wasAlive[i] = W.Projectiles.Alive[i]; lastPos[i] = W.Projectiles.Position[i]; }
                _sim.Tick();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                _views.CollectTick(W);
                viewBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                if (W.Projectiles.Count == cap) hitCap = true;
                for (int i = 0; i < cap; i++)
                    if (wasAlive[i] && !W.Projectiles.Alive[i] && _views.Tracker.Launch.Length > i)
                        longestLobGap = MathF.Max(longestLobGap, LobGap(i, lastPos[i]));
            }
            // Age depends on the Drawn flag (BUG-0221), so the age Sync draws a new mark at is read before Sync marks it drawn
            // (same tick and alpha; Sync's CollectTick is a no-op for this tick and Expire never takes an undrawn mark).
            for (int i = 0; i < undrawn.Length; i++)
            {
                undrawn[i] = _views.Marks.Active[i] && !_views.Marks.Drawn[i];
                firstAge[i] = undrawn[i] ? _views.Marks.Age(i, W.TickNumber, 0.5f) : 0f;
            }
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            _views.Sync(W, 0.5f);
            viewBytes += GC.GetAllocatedBytesForCurrentThread() - b1;
            frames++;
            if (hitCap) { fullBytes += viewBytes; full++; }
            for (int i = 0; i < undrawn.Length; i++)
            {
                if (!undrawn[i]) continue;
                newMarks++;
                // ProjectileViews draws a mark at alpha 0.85 x (1 - age); the headless renderer keeps no instance colours to read back.
                if (!_views.Marks.Drawn[i]) Check(false, $"lines tick {W.TickNumber}: mark {i} not marked drawn by Sync");
                if (0.85f * (1f - firstAge[i]) < 0.02f) invisibleFirst++;
            }
            most = Math.Max(most, W.Projectiles.Count);
            for (int k = 0; k < _views.Shown; k++)
            {
                if (_views.DrawnIsLob(k)) continue;
                int i = _views.DrawnSlot(k);
                System.Numerics.Vector2 p = W.Projectiles.Position[i], q = W.Projectiles.PrevPosition[i];
                longestStep = MathF.Max(longestStep, System.Numerics.Vector2.Distance(p, q));
                Vector3 o = _views.DrawnAt(k);
                if (SegmentDistance(new System.Numerics.Vector2(o.X, o.Z), q, p) > Eps) offSegment++;
            }
            Check(_views.Shown == W.Projectiles.Count && W.Projectiles.Count <= cap, $"lines tick {W.TickNumber}: {_views.Shown} drawn, {W.Projectiles.Count} in flight of {cap}");
            await Task.CompletedTask;
        }
        GD.Print($"lines 500 v 500 (seed {seed}, 5 ticks a frame): capacity {cap}, most {most}, {full} frames filled the store: {fullBytes} bytes; longest aimed step {longestStep:F2} m, " +
            $"{offSegment} drawn off their segment; longest gap from a lob's last drawn point to its landing {longestLobGap:F2} m; " +
            $"{newMarks} marks first drawn, {invisibleFirst} of them fully transparent on that frame (BUG-0221)");
        Check(full > 0, $"lines: the store never filled (most {most} of {cap})");
        Check(fullBytes == 0, $"lines: {fullBytes} bytes over {full} frames that filled the store");
        Check(offSegment == 0, $"lines: {offSegment} aimed shots drawn off PrevPosition -> Position");
        Check(newMarks > 0 && invisibleFirst == 0, $"lines: {invisibleFirst} of {newMarks} marks first drawn fully transparent (BUG-0221)");

        // (3) Alpha outside [0, 1] and NaN: never extrapolated; the sim hash does not move across Sync.
        if (W.Projectiles.Count == 0) for (int k = 0; k < 40 && W.Projectiles.Count == 0; k++) Tick();
        ulong hash = _sim.StateHash();
        foreach (float alpha in new[] { -3f, 1.8f, float.NaN, float.PositiveInfinity, 0f, 1f })
        {
            _views.Sync(W, alpha);
            for (int k = 0; k < _views.Shown; k++)
            {
                int i = _views.DrawnSlot(k);
                Vector3 o = _views.DrawnAt(k);
                float d = SegmentDistance(new System.Numerics.Vector2(o.X, o.Z), W.Projectiles.PrevPosition[i], W.Projectiles.Position[i]);
                if (d > Eps || !float.IsFinite(o.Y)) { Check(false, $"alpha {alpha}: slot {i} drawn at {o}, {d:F3} m off its segment"); break; }
            }
        }
        Check(_sim.StateHash() == hash, "Sync moved the sim hash");
        Twin($"lines seed {seed}");
        await EndMatch();
    }

    // How far short of its landing point a lob's last drawn position was (0 for an aimed shot or an untracked slot).
    private float LobGap(int slot, System.Numerics.Vector2 last)
    {
        if (!_views.Tracker.IsLob[slot]) return 0f;
        // The landing is in this tick's impacts at the shot's target; the nearest impact of a lob type stands for it.
        float best = float.MaxValue;
        foreach (var imp in W.Impacts)
            if (_data.Projectiles[imp.ProjectileTypeId].Kind == ProjectileKind.Lob)
                best = MathF.Min(best, System.Numerics.Vector2.Distance(imp.Position, last));
        return best == float.MaxValue ? 0f : best;
    }

    private void Twin(string what)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"{what}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"{what}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == player) { sum += U.Position[i]; n++; }
        return n > 0 ? sum / n : new System.Numerics.Vector2(float.MaxValue);
    }

    private static float SegmentDistance(System.Numerics.Vector2 x, System.Numerics.Vector2 a, System.Numerics.Vector2 b)
    {
        System.Numerics.Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 0f ? Math.Clamp(System.Numerics.Vector2.Dot(x - a, ab) / len2, 0f, 1f) : 0f;
        return System.Numerics.Vector2.Distance(x, a + ab * t);
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
