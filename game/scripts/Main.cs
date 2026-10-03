using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>Root scene script. For now it only proves the game can load the sim library.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print($"Rts.Sim {SimInfo.Version}");
    }
}
