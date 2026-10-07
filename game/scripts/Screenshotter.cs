using Godot;

namespace Rts.Game;

/// <summary>Debug capture for <c>--screenshot &lt;path&gt; --screenshot-after &lt;seconds&gt;</c>: saves the viewport as PNG, then quits.</summary>
/// <remarks>
/// Headless runs use the dummy renderer, which has no pixels, so they log that the capture is
/// unavailable and quit with code 0. With <c>--bench</c> it doesn't quit after a good shot: the
/// bench waits for <see cref="Pending"/> to clear and runs next (M2-7).
/// </remarks>
public partial class Screenshotter : Node
{
    private string? _path;
    private double _remaining;
    private bool _quitAfter = true;

    /// <summary>True while a capture is armed and not yet taken.</summary>
    public bool Pending => _path != null;

    /// <summary>Arms the capture; does nothing when <paramref name="path"/> is null.</summary>
    /// <param name="quitAfter">Quit once the shot is saved; false when <c>--bench</c> runs after it (a failed save still quits with 1).</param>
    public void Arm(string? path, double afterSeconds, bool quitAfter = true)
    {
        _path = path;
        _remaining = afterSeconds;
        _quitAfter = quitAfter;
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
        if (_quitAfter || exitCode != 0) GetTree().Quit(exitCode);
    }
}
