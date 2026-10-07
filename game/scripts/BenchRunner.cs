using System;
using System.Globalization;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary><c>--bench &lt;seconds&gt;</c> (M2-7): plays the <see cref="BenchScript"/> through the real selection, order, minimap and camera code, times every frame, prints one <c>bench:</c> line and quits with 0.</summary>
/// <remarks>
/// Created by <see cref="Match.Start"/> when the flag is given. It waits until the start armies
/// exist (tick 2) and any <c>--screenshot</c> is taken, skips <see cref="WarmUpFrames"/>, then feeds each frame's delta to the
/// script and to a <see cref="FrameTimeStats"/>. Orders go through
/// <see cref="SelectionController.BoxSelect"/>, <see cref="SelectionController.OrderAt"/>,
/// <see cref="SelectionController.BeginAttackMove"/> / <see cref="SelectionController.AttackMoveClick"/>
/// and <see cref="SelectionController.Order"/>, so the sim changes only through enqueued commands;
/// camera moves through <see cref="Minimap.JumpTo"/> (or <see cref="RtsCamera.SetFocus"/> under
/// <c>--no-hud</c>) and <see cref="RtsCamera.SetZoom"/>. Holds no gameplay state. Allocates nothing
/// per frame; the closing line is built once.
/// </remarks>
public partial class BenchRunner : Node
{
    /// <summary>The one result line's prefix; nothing else the game prints starts with it.</summary>
    public const string LinePrefix = "bench:";

    /// <summary>Frames to skip after the armies exist before timing: the first draws of new unit nodes compile pipelines (a one-time ~85 ms hitch on the dev PC), which is load time, not play.</summary>
    public const int WarmUpFrames = 30;

    // Screen points as fractions of the viewport: the A + click and the three Shift-queued moves.
    private static readonly Vector2 AttackPoint = new(0.8f, 0.35f);
    private static readonly Vector2[] QueuePoints = { new(0.2f, 0.3f), new(0.5f, 0.2f), new(0.8f, 0.6f) };

    private SimRunner _runner = null!;
    private SelectionController _selection = null!;
    private RtsCamera _camera = null!;
    private Minimap? _minimap;
    private Screenshotter? _shot;
    private float _startZoom;
    private long _ticksAtStart;
    private double _tickMsAtStart;
    private int _warmUp;
    private double _worstAt;
    private BenchStep _lastStep, _worstAfter;

    /// <summary>The script; null until <see cref="Init"/>.</summary>
    public BenchScript? Script { get; private set; }

    /// <summary>Frame times measured since the run started.</summary>
    public FrameTimeStats Stats { get; } = new();

    /// <summary>Quit with code 0 after printing the line (the launch flag); tests turn it off.</summary>
    public bool QuitOnFinish { get; set; } = true;

    /// <summary>True from the first timed frame until the line is printed.</summary>
    public bool Running { get; private set; }

    /// <summary>True once the line is printed.</summary>
    public bool Finished { get; private set; }

    /// <summary>The printed <c>bench:</c> line; null until <see cref="Finished"/>.</summary>
    public string? Line { get; private set; }

    /// <summary>The printed "Bench running for ..." info line; null until <see cref="Running"/>. Invariant culture, like <see cref="Line"/> (BUG-0103).</summary>
    public string? StartLine { get; private set; }

    /// <summary>The printed "Bench worst frame ..." info line; null until <see cref="Finished"/>.</summary>
    public string? WorstLine { get; private set; }

    /// <summary>Camera focus right after each of the four minimap jumps of the latest loop (corner order).</summary>
    public System.Numerics.Vector2[] JumpFocus { get; } = new System.Numerics.Vector2[4];

    /// <summary>Minimap jumps done.</summary>
    public int Jumps { get; private set; }

    /// <summary>Selection size after the latest box select.</summary>
    public int LastBoxSelected { get; private set; }

    /// <summary>Local army centre (meters) when the latest <see cref="BenchStep.OrderAcross"/> ran.</summary>
    public System.Numerics.Vector2 AcrossFrom { get; private set; }

    /// <summary>Where the latest <see cref="BenchStep.OrderAcross"/> sent the selection (<see cref="BenchTarget.TryAcross"/>).</summary>
    public System.Numerics.Vector2 AcrossTarget { get; private set; }

    /// <summary>Times <see cref="BenchStep.OrderAcross"/> issued an order.</summary>
    public int AcrossOrders { get; private set; }

    /// <summary>Sim ticks run during the timed span.</summary>
    public long Ticks => _runner.TickTimes.Total - _ticksAtStart;

    /// <summary>Connects the bench to the match; <paramref name="seconds"/> is the timed span, <paramref name="startZoom"/> the zoom <see cref="BenchStep.FocusArmy"/> restores.</summary>
    public void Init(SimRunner runner, SelectionController selection, RtsCamera camera, Minimap? minimap, Screenshotter? shot, double seconds, float startZoom)
    {
        _runner = runner;
        _selection = selection;
        _camera = camera;
        _minimap = minimap;
        _shot = shot;
        _startZoom = startZoom;
        Script = new BenchScript(seconds);
    }

    public override void _Process(double delta)
    {
        if (Finished || Script == null || _runner.Simulation is not Simulation sim) return;
        if (!Running)
        {
            // The armies spawn on tick 1; a pending screenshot goes first.
            if (sim.TickNumber < 2 || (_shot?.Pending ?? false)) return;
            if (_warmUp++ < WarmUpFrames) return;
            Running = true;
            _ticksAtStart = _runner.TickTimes.Total;
            _tickMsAtStart = _runner.TotalTickMs;
            Vector2 size = _camera.GetViewport().GetVisibleRect().Size;
            StartLine = StartLineText(Script.Duration, size, DisplayServer.WindowGetVsyncMode().ToString(), DisplayServer.ScreenGetRefreshRate(), DisplayServer.GetName());
            GD.Print(StartLine);
            return; // this frame's delta is from before the start
        }
        if (delta * 1000.0 > Stats.Worst)
        {
            _worstAt = Script.Elapsed;
            _worstAfter = _lastStep;
        }
        Stats.Add(delta * 1000.0);
        if (Script.Advance(delta, out BenchScript.Entry step))
        {
            Run(step, sim);
            _lastStep = step.Step;
        }
        if (Script.Finished) Finish();
    }

    private void Run(BenchScript.Entry step, Simulation sim)
    {
        Vector2 view = _camera.GetViewport().GetVisibleRect().Size;
        switch (step.Step)
        {
            case BenchStep.FocusArmy:
                if (TryArmyCentre(sim.World.Units, out System.Numerics.Vector2 c)) _camera.SetFocus(c.X, c.Y);
                _camera.SetZoom(_startZoom);
                break;
            case BenchStep.BoxSelectArmy:
                LastBoxSelected = _selection.BoxSelect(new Vector2(1, 1), view - new Vector2(1, 1), add: false);
                break;
            case BenchStep.OrderAcross:
                // BUG-0101: a far point on the other side of the map, not the enemy block next door.
                if (TryArmyCentre(sim.World.Units, out System.Numerics.Vector2 from) && BenchTarget.TryAcross(sim.World.NavGrid, from, out System.Numerics.Vector2 to))
                {
                    AcrossFrom = from;
                    AcrossTarget = to;
                    AcrossOrders++;
                    _selection.Order(CommandKind.Move, new Vector2(to.X, to.Y), queued: false);
                }
                break;
            case BenchStep.MinimapJump:
                Jump(step.Arg, sim.World.Heightmap);
                break;
            case BenchStep.ZoomIn:
                _camera.SetZoom(CameraLimits.MinZoom);
                break;
            case BenchStep.ZoomOut:
                _camera.SetZoom(CameraLimits.MaxZoom);
                break;
            case BenchStep.AttackMove:
                _selection.BeginAttackMove();
                _selection.AttackMoveClick(AttackPoint * view);
                break;
            case BenchStep.QueuePoint:
                _selection.OrderAt(CommandKind.Move, QueuePoints[step.Arg] * view, queued: true);
                break;
            case BenchStep.Hold:
            case BenchStep.Stop:
                _selection.CancelTargeting(); // as the H and S keys do
                _selection.Order(step.Step == BenchStep.Stop ? CommandKind.Stop : CommandKind.HoldPosition, null, queued: false);
                break;
        }
    }

    // A minimap click 6 px inside corner `corner` of the drawn map; the camera directly without a HUD.
    private void Jump(int corner, Heightmap map)
    {
        bool right = corner is 1 or 2, bottom = corner >= 2;
        if (_minimap?.Raster != null)
        {
            (System.Numerics.Vector2 pos, System.Numerics.Vector2 size) = _minimap.Fit.MapRect;
            const float inset = 6f;
            var px = new Vector2(right ? pos.X + size.X - inset : pos.X + inset, bottom ? pos.Y + size.Y - inset : pos.Y + inset);
            _minimap.JumpTo(px);
        }
        else
        {
            float w = map.Width * MapConstants.CellSize, h = map.Height * MapConstants.CellSize;
            _camera.SetFocus(right ? w : 0f, bottom ? h : 0f);
        }
        JumpFocus[corner] = _camera.Focus;
        Jumps++;
    }

    private static bool TryArmyCentre(UnitStore u, out System.Numerics.Vector2 centre)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            sum += u.Position[i];
            n++;
        }
        centre = n > 0 ? sum / n : default;
        return n > 0;
    }

    /// <summary>The "Bench running for ..." line, invariant culture.</summary>
    public static string StartLineText(double seconds, Vector2 viewport, string vsync, double refreshHz, string display) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Bench running for {seconds:0.###} s ({BenchScript.Sequence.Length} steps per {BenchScript.LoopSeconds:0.##} s loop), " +
            $"viewport {viewport.X:0} x {viewport.Y:0}, vsync {vsync} at {refreshHz:0.#} Hz, display {display}");

    /// <summary>The "Bench worst frame ..." line, invariant culture.</summary>
    public static string WorstLineText(double worstMs, double atSeconds, BenchStep after) =>
        string.Create(CultureInfo.InvariantCulture, $"Bench worst frame {worstMs:0.00} ms at {atSeconds:0.00} s, after step {after}");

    private void Finish()
    {
        Running = false;
        Finished = true;
        long ticks = Ticks;
        double avgTick = ticks > 0 ? (_runner.TotalTickMs - _tickMsAtStart) / ticks : 0;
        // Frames drawn in the timed span over its length (= 1000 / avg); BUG-0102: not Godot's once-a-second counter.
        double fps = Script!.Elapsed > 0 ? Stats.Count / Script.Elapsed : 0;
        Line = string.Create(CultureInfo.InvariantCulture,
            $"{LinePrefix} seconds {Script!.Elapsed:0.0} frames {Stats.Count} avg {Stats.Average:0.00} ms p50 {Stats.Percentile(0.5):0.00} ms " +
            $"p99 {Stats.Percentile(0.99):0.00} ms worst {Stats.Worst:0.00} ms fps {fps:0.0} ticks {ticks} avgTick {avgTick:0.000} ms");
        WorstLine = WorstLineText(Stats.Worst, _worstAt, _worstAfter);
        GD.Print(WorstLine);
        GD.Print(Line);
        if (QuitOnFinish) GetTree().Quit(0);
    }
}
