using Godot;

namespace Rts.Game;

/// <summary>Top-left developer readout: tick, game speed, last tick cost, FPS, selected units, active subgroup, A targeting. Dev-only text, not player-facing.</summary>
public partial class DebugOverlay : CanvasLayer
{
    /// <summary>The runner to report on; set by <see cref="Match"/>.</summary>
    public SimRunner? Runner { get; set; }

    /// <summary>The selection to count; set by <see cref="Match"/>.</summary>
    public SelectionController? Selection { get; set; }

    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
    }

    public override void _Process(double delta)
    {
        if (Runner?.Simulation == null) return;
        _label.Text = $"tick {Runner.Simulation.TickNumber}   speed {Runner.GameSpeed:0.##}x   " +
            $"tick {Runner.LastTickMs:0.000} ms   {Engine.GetFramesPerSecond():0} fps   sel {Selection?.Selection.Count ?? 0}" +
            SelectionSuffix();
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
