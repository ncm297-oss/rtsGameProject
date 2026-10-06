using System.Diagnostics;
using Godot;
using Rts.Sim;
using Rts.Sim.ViewApi;

namespace Rts.Game;

/// <summary>Developer readout and the F12 debug overlay (docs/03 "Debug tooling"). Dev-only text, not player-facing.</summary>
/// <remarks>
/// The top-left label is always on (M2-1): tick, game speed, last tick cost, FPS, selected units,
/// active subgroup, A targeting. The overlay (M2-5, input action <c>debug_overlay</c>, F12; launch flag
/// <c>--debug-overlay</c> starts with it on) adds the nav grid (<see cref="NavOverlayView"/>), the flow
/// arrows of the selection's goal around the camera (<see cref="FlowArrowsView"/>), the tick-time graph
/// (<see cref="TickGraph"/>) and a second label line with entity counts and tick averages. It is off by
/// default; while off its layers are hidden, have no <c>_Process</c> and are not even built.
/// Reads the sim only; never enqueues.
/// </remarks>
public partial class DebugOverlay : CanvasLayer
{
    /// <summary>The runner to report on; set by <see cref="Match"/>.</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The selection to count; set by <see cref="Match"/>.</summary>
    public SelectionController? Selection { get; set; }

    private Label _label = null!;
    private TickGraph _graph = null!;
    private RtsCamera? _camera;
    private NavOverlayView? _nav;
    private FlowArrowsView? _arrows;
    private readonly Stopwatch _watch = new();

    /// <summary>True while the overlay layers are shown.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Times the overlay was toggled (input or <see cref="SetEnabled"/> changing the state).</summary>
    public int Toggles { get; private set; }

    /// <summary>Wall-clock cost of the last <see cref="SyncLayers"/> in milliseconds (nav, arrows, counts; not the label text or the graph's draw).</summary>
    public double LastLayersMs { get; private set; }

    /// <summary>Live units at the last <see cref="SyncLayers"/>.</summary>
    public int LiveUnits { get; private set; }

    /// <summary>Moving units at the last <see cref="SyncLayers"/>.</summary>
    public int MovingUnits { get; private set; }

    /// <summary>Cached flow fields at the last <see cref="SyncLayers"/>.</summary>
    public int CachedFields { get; private set; }

    /// <summary>Goal cell whose arrows were shown at the last <see cref="SyncLayers"/>, or -1.</summary>
    public int ShownGoal { get; private set; } = -1;

    /// <summary>The graph control.</summary>
    public TickGraph Graph => _graph;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _graph = GetNode<TickGraph>("TickGraph");
        _graph.Visible = false;
    }

    /// <summary>Connects the overlay layers; call once after the sim exists. <paramref name="startOn"/> is <c>--debug-overlay</c>.</summary>
    public void Init(SimRunner runner, SelectionController selection, RtsCamera camera, NavOverlayView nav, FlowArrowsView arrows, bool startOn)
    {
        Runner = runner;
        Selection = selection;
        _camera = camera;
        _nav = nav;
        _arrows = arrows;
        _graph.Samples = runner.TickTimes;
        nav.Visible = false;
        arrows.Visible = false;
        SetEnabled(startOn);
    }

    /// <summary>Shows or hides the overlay layers.</summary>
    public void SetEnabled(bool on)
    {
        if (on == Enabled) return;
        Enabled = on;
        Toggles++;
        _graph.Visible = on;
        if (_nav != null) _nav.Visible = on;
        if (_arrows != null) _arrows.Visible = on;
        // While off the arrows don't follow the sim, so the first frame back must relist.
        if (on) _arrows?.Layout?.Invalidate();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("debug_overlay") && !e.IsEcho()) SetEnabled(!Enabled);
    }

    public override void _Process(double delta)
    {
        if (Runner?.Simulation is not Simulation sim) return;
        if (Enabled) SyncLayers();
        string text = $"tick {sim.TickNumber}   speed {Runner.GameSpeed:0.##}x   " +
            $"tick {Runner.LastTickMs:0.000} ms   {Engine.GetFramesPerSecond():0} fps   sel {Selection?.Selection.Count ?? 0}" +
            SelectionSuffix();
        if (Enabled)
        {
            TickTimeRing ring = Runner.TickTimes;
            text += $"\nunits {LiveUnits}   moving {MovingUnits}   fields {CachedFields}/{sim.World.FlowFields.Capacity}   " +
                $"tick avg {ring.Average:0.000} ms   worst {ring.Worst:0.000} ms ({ring.Count})" +
                (ShownGoal >= 0 ? $"   arrows {_arrows?.ShownCount ?? 0}" : "");
        }
        _label.Text = text;
    }

    /// <summary>One frame of overlay work: nav grid (rebuilt only on a grid version change), flow arrows (relisted only on a change), counts, graph redraw. Allocates nothing at steady state.</summary>
    public void SyncLayers()
    {
        if (Runner?.Simulation is not Simulation sim) return;
        _watch.Restart();
        World world = sim.World;
        _nav?.Sync(world);
        int goal = Selection != null ? FlowArrowLayout.GoalOf(Selection.Selection.Items, world.Units.Alive, world.Units.Generation, world.Units.GoalCell) : -1;
        if (_arrows != null && _camera != null) _arrows.Sync(world, goal, _camera.Focus);
        ShownGoal = goal;
        LiveUnits = world.Units.Count;
        MovingUnits = DebugCounts.Moving(world.Units.Alive, world.Units.State);
        CachedFields = world.FlowFields.Count;
        _graph.QueueRedraw();
        _watch.Stop();
        LastLayersMs = _watch.Elapsed.TotalMilliseconds;
    }

    // "sub <typeId> <n>/<m>" for the active Tab subgroup, then " A" while attack-move targeting.
    private string SelectionSuffix()
    {
        if (Selection == null) return "";
        Rts.Sim.ViewApi.Subgroups sub = Selection.Subgroups;
        string text = sub.Count > 0 ? $"   sub {sub.ActiveType} {sub.Index + 1}/{sub.Count}" : "";
        return Selection.Targeting ? text + "   A" : text;
    }
}
