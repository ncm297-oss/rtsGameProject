using Godot;

namespace Rts.Game;

/// <summary>Debug capture for <c>--screenshot &lt;path&gt; --screenshot-after &lt;seconds&gt;</c>: saves the viewport as PNG, then quits.</summary>
/// <remarks>
/// Headless runs use the dummy renderer, which has no pixels, so they log that the capture is
/// unavailable and quit with code 0.
/// </remarks>
public partial class Screenshotter : Node
{
    private string? _path;
    private double _remaining;

    /// <summary>Arms the capture; does nothing when <paramref name="path"/> is null.</summary>
    public void Arm(string? path, double afterSeconds)
    {
        _path = path;
        _remaining = afterSeconds;
        SetProcess(path != null);
    }

    public override void _Ready()
    {
        if (_path == null) SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (_path == null) return;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print($"Screenshot unavailable in headless mode; not writing {_path}.");
            Finish(0);
            return;
        }
        _remaining -= delta;
        if (_remaining > 0) return;

        Image image = GetViewport().GetTexture().GetImage();
        string full = ProjectSettings.GlobalizePath(_path);
        Error result = image.SavePng(full);
        if (result != Error.Ok)
        {
            GD.PushError($"Screenshot: could not write {full} ({result}).");
            Finish(1);
            return;
        }
        GD.Print($"Screenshot saved to {full}");
        Finish(0);
    }

    private void Finish(int exitCode)
    {
        _path = null;
        SetProcess(false);
        GetTree().Quit(exitCode);
    }
}
