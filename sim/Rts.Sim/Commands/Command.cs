using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Commands;

/// <summary>One player or AI instruction; the only way anything outside the sim changes sim state.</summary>
/// <remarks><see cref="Tick"/> and <see cref="Sequence"/> are stamped by <c>Simulation.Enqueue</c>.</remarks>
public struct Command
{
    /// <summary>What to do.</summary>
    public CommandKind Kind;
    /// <summary>Issuing player index.</summary>
    public int Player;
    /// <summary>Tick on which the command applies.</summary>
    public int Tick;
    /// <summary>Per-player issue order, used to break ties within a tick.</summary>
    public int Sequence;
    /// <summary>Unit type for <see cref="CommandKind.SpawnUnit"/>.</summary>
    public int TypeId;
    /// <summary>Target position (x, z) in meters.</summary>
    public Vector2 Position;
    /// <summary>The unit a unit order (<see cref="CommandKind.Move"/>) applies to.</summary>
    public EntityHandle Unit;

    /// <summary>True when the payload is safe to apply: positional commands need a finite position.</summary>
    public readonly bool IsValid() => Kind switch
    {
        CommandKind.SpawnUnit or CommandKind.Move => float.IsFinite(Position.X) && float.IsFinite(Position.Y),
        _ => true,
    };

    /// <summary>A command that does nothing.</summary>
    public static Command Noop(int player) => new() { Kind = CommandKind.Noop, Player = player };

    /// <summary>A command that spawns one unit for <paramref name="player"/>.</summary>
    public static Command SpawnUnit(int player, int typeId, Vector2 position) =>
        new() { Kind = CommandKind.SpawnUnit, Player = player, TypeId = typeId, Position = position };

    /// <summary>A command that sends <paramref name="player"/>'s <paramref name="unit"/> to <paramref name="target"/> (meters).</summary>
    public static Command Move(int player, EntityHandle unit, Vector2 target) =>
        new() { Kind = CommandKind.Move, Player = player, Unit = unit, Position = target };
}
