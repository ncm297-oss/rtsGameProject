using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Commands;

/// <summary>One player or AI instruction; the only way anything outside the sim changes sim state.</summary>
/// <remarks><see cref="Tick"/> and <see cref="Sequence"/> are stamped by <c>Simulation.Enqueue</c>.</remarks>
public struct Command
{
    /// <summary><see cref="Flags"/> bit: append the order to the unit's order queue instead of replacing it (shift-queue).</summary>
    public const int QueuedFlag = 1;

    /// <summary>Every <see cref="Flags"/> bit the sim knows; a command or replay with another bit set is invalid.</summary>
    public const int KnownFlags = QueuedFlag;

    /// <summary>What to do.</summary>
    public CommandKind Kind;
    /// <summary>Issuing player index.</summary>
    public int Player;
    /// <summary>Tick on which the command applies.</summary>
    public int Tick;
    /// <summary>Per-player issue order, used to break ties within a tick.</summary>
    public int Sequence;
    /// <summary>Unit type for <see cref="CommandKind.SpawnUnit"/>; building type for <see cref="CommandKind.SpawnBuilding"/>.</summary>
    public int TypeId;
    /// <summary>Target position (x, z) in meters.</summary>
    public Vector2 Position;
    /// <summary>The unit a unit order (<see cref="CommandKind.Move"/>, Stop, HoldPosition, AttackMove, Gather) applies to.</summary>
    public EntityHandle Unit;
    /// <summary>Option bits; see <see cref="QueuedFlag"/>.</summary>
    public int Flags;

    /// <summary>True when <see cref="QueuedFlag"/> is set.</summary>
    public readonly bool IsQueued => (Flags & QueuedFlag) != 0;

    /// <summary>True for the kinds addressed to one unit (<see cref="Unit"/>), which can be queued.</summary>
    public readonly bool IsUnitOrder => Kind is CommandKind.Move or CommandKind.Stop or CommandKind.HoldPosition or CommandKind.AttackMove or CommandKind.Gather;

    /// <summary>True when <see cref="Kind"/> is a defined <see cref="CommandKind"/> and <see cref="Flags"/> holds only known bits, set on unit orders only: what <c>Simulation.Enqueue</c> accepts and a replay may hold.</summary>
    /// <remarks>
    /// <see cref="QueuedFlag"/> means nothing on a <see cref="CommandKind.Noop"/>, a
    /// <see cref="CommandKind.SpawnUnit"/> or a <see cref="CommandKind.SpawnBuilding"/>, so it is refused there rather than carried, ignored, in the
    /// replay and the hash (BUG-0056). Written as a switch, not <c>Enum.IsDefined</c>, so a new kind is
    /// refused until it is added here on purpose.
    /// </remarks>
    public readonly bool IsWellFormed()
    {
        bool knownKind = Kind is CommandKind.Noop or CommandKind.SpawnUnit or CommandKind.Move or CommandKind.Stop
            or CommandKind.HoldPosition or CommandKind.AttackMove or CommandKind.SpawnBuilding or CommandKind.Gather;
        if (!knownKind || (Flags & ~KnownFlags) != 0) return false;
        return Flags == 0 || IsUnitOrder;
    }

    /// <summary>True when the payload is safe to apply: well formed (<see cref="IsWellFormed"/>), and positional commands need a finite position.</summary>
    public readonly bool IsValid()
    {
        if (!IsWellFormed()) return false;
        return Kind switch
        {
            CommandKind.SpawnUnit or CommandKind.Move or CommandKind.AttackMove or CommandKind.SpawnBuilding or CommandKind.Gather =>
                float.IsFinite(Position.X) && float.IsFinite(Position.Y),
            _ => true,
        };
    }

    /// <summary>A command that does nothing.</summary>
    public static Command Noop(int player) => new() { Kind = CommandKind.Noop, Player = player };

    /// <summary>A command that spawns one unit for <paramref name="player"/>.</summary>
    public static Command SpawnUnit(int player, int typeId, Vector2 position) =>
        new() { Kind = CommandKind.SpawnUnit, Player = player, TypeId = typeId, Position = position };

    /// <summary>A command that sends <paramref name="player"/>'s <paramref name="unit"/> to <paramref name="target"/> (meters).</summary>
    public static Command Move(int player, EntityHandle unit, Vector2 target) => Move(player, unit, target, false);

    /// <summary>As <see cref="Move(int, EntityHandle, Vector2)"/>; <paramref name="queued"/> appends it to the unit's order queue.</summary>
    public static Command Move(int player, EntityHandle unit, Vector2 target, bool queued) =>
        UnitOrder(CommandKind.Move, player, unit, target, queued);

    /// <summary>A command that stops <paramref name="player"/>'s <paramref name="unit"/> (queued: once its earlier orders are done).</summary>
    public static Command Stop(int player, EntityHandle unit, bool queued = false) =>
        UnitOrder(CommandKind.Stop, player, unit, default, queued);

    /// <summary>A command that makes <paramref name="player"/>'s <paramref name="unit"/> stop and hold its ground.</summary>
    public static Command HoldPosition(int player, EntityHandle unit, bool queued = false) =>
        UnitOrder(CommandKind.HoldPosition, player, unit, default, queued);

    /// <summary>A command that attack-moves <paramref name="player"/>'s <paramref name="unit"/> to <paramref name="target"/> (meters).</summary>
    public static Command AttackMove(int player, EntityHandle unit, Vector2 target, bool queued = false) =>
        UnitOrder(CommandKind.AttackMove, player, unit, target, queued);

    /// <summary>A dev / test command placing a building of <paramref name="typeId"/> for <paramref name="player"/> with its anchor (lowest x, y) cell at <paramref name="position"/> (meters).</summary>
    public static Command SpawnBuilding(int player, int typeId, Vector2 position) =>
        new() { Kind = CommandKind.SpawnBuilding, Player = player, TypeId = typeId, Position = position };

    /// <summary>A command sending <paramref name="player"/>'s worker <paramref name="unit"/> to gather the node at <paramref name="node"/> (meters, any point of its footprint).</summary>
    public static Command Gather(int player, EntityHandle unit, Vector2 node, bool queued = false) =>
        UnitOrder(CommandKind.Gather, player, unit, node, queued);

    private static Command UnitOrder(CommandKind kind, int player, EntityHandle unit, Vector2 target, bool queued) =>
        new() { Kind = kind, Player = player, Unit = unit, Position = target, Flags = queued ? QueuedFlag : 0 };
}
