using Godot;
using Rts.Sim;
using Rts.Sim.Data;

namespace Rts.Game;

/// <summary>Boot scene script: prints the sim version, loads game data, and starts the match.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        // The smoke gate (tools/qa/smoke.ps1) greps for this banner.
        GD.Print($"Rts.Sim {SimInfo.Version}");

        DataLoadResult result = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!result.Ok)
        {
            foreach (DataError error in result.Errors)
                GD.PushError(error.ToString());
            GetTree().Quit(1);
            return;
        }

        // Children are ready before their parent, so the match is started from here, after its nodes exist.
        GetNode<Match>("Match").Start(result.Data!, LaunchOptions.FromCommandLine());
    }
}
