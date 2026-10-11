using System;
using Rts.Sim;

namespace Rts.Game;

/// <summary>Each unit slot's maximum hit points for the current tick, filled once and read by every view that needs it (M4-VH3, BUG-0490).</summary>
/// <remarks>
/// <see cref="UnitViews"/> (the first-sight hit flash) and <see cref="CombatViews"/> (the hp bars) both need
/// <c>World.MaxHp</c> per slot each frame; <c>Match</c> hands them one instance. A maximum moves only on a tick (a spawn, a
/// death, an hp tech), so the array is refilled on the first read of a new tick (or a new <see cref="World"/>) and reused
/// by every later read of that tick: at most one fill a frame, none on a frame the runner ran no tick. A copy of sim data,
/// no gameplay state. Allocation-free after construction.
/// </remarks>
public sealed class SlotMaxHp
{
    private readonly int[] _values;
    private World? _world;
    private int _tick = -1;

    /// <summary>An array for <paramref name="unitCapacity"/> slots, empty until the first <see cref="Refreshed"/>.</summary>
    public SlotMaxHp(int unitCapacity) => _values = new int[Math.Max(0, unitCapacity)];

    /// <summary>Fills of the array so far.</summary>
    public int Fills { get; private set; }

    /// <summary>The per-slot maximum for <paramref name="world"/>'s current tick (0 for a dead slot; <see cref="UnitViews.FillMaxHp"/>), refilled only when the tick or world changed.</summary>
    public int[] Refreshed(World world)
    {
        if (!ReferenceEquals(world, _world) || world.TickNumber != _tick)
        {
            UnitViews.FillMaxHp(world, _values);
            _world = world;
            _tick = world.TickNumber;
            Fills++;
        }
        return _values;
    }
}
