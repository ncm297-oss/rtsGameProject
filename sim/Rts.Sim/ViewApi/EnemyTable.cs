using System;

namespace Rts.Sim.ViewApi;

/// <summary>
/// M6-V16: the alliance answer the view's enemy pickers read (<see cref="UnitPicker"/>'s right-click Attack, the minimap's
/// enemy dot), as a table: entry p says whether player p is an enemy of the viewing player (the view fills it from
/// <c>World.IsEnemy(viewer, p)</c> once per match, since teams are fixed). The pickers read it as a span: no delegate, no
/// allocation per click. Read-only.
/// </summary>
public static class EnemyTable
{
    /// <summary>
    /// Whether a unit or building of <paramref name="owner"/> is an enemy of <paramref name="viewer"/>: <paramref name="enemies"/>'s
    /// entry for it (false past the table's end); with an empty table, the free-for-all rule (any owner but the viewer).
    /// </summary>
    public static bool IsEnemyOwner(int owner, int viewer, ReadOnlySpan<bool> enemies) =>
        enemies.IsEmpty ? owner != viewer : (uint)owner < (uint)enemies.Length && enemies[owner];
}
