using System;
using Rts.Sim.Abilities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Which status markers the view draws over units (M4-V6b, docs/03 "Implementation (M4-V6b)"): one per active status of
/// each unit the screen shows, side by side in the order the statuses were first applied. Read-only on the
/// <see cref="StatusStore"/>; allocation-free.
/// </summary>
public static class StatusMarkers
{
    /// <summary>
    /// Writes a <see cref="StatusMark"/> for every active entry of every shown unit slot into <paramref name="marks"/>, slots
    /// ascending and each unit's entries in store order, and returns how many (at most its length; a unit whose row would
    /// not fit is left out whole). An entry is active while its <c>TicksRemaining</c> is above 0 and its status id is below
    /// <paramref name="statusCount"/> (the number of status defs).
    /// </summary>
    /// <param name="statuses">The unit store's statuses.</param>
    /// <param name="shown">Per unit slot, whether the screen shows it: the fog's <c>UnitShown</c>, or the store's <c>Alive</c> without fog.</param>
    /// <param name="statusCount">The number of status defs (<c>GameData.Statuses.Length</c>).</param>
    /// <param name="marks">Output, sized for the most markers drawn (unit capacity x <see cref="StatusStore.PerUnit"/> holds every case).</param>
    public static int Collect(StatusStore statuses, ReadOnlySpan<bool> shown, int statusCount, Span<StatusMark> marks)
    {
        int[] count = statuses.Count, id = statuses.StatusId, left = statuses.TicksRemaining;
        int slots = Math.Min(shown.Length, count.Length), k = 0;
        for (int i = 0; i < slots; i++)
        {
            int n = count[i];
            if (n <= 0 || !shown[i]) continue;
            if (n > StatusStore.PerUnit) n = StatusStore.PerUnit;
            int head = i * StatusStore.PerUnit, row = 0;
            for (int e = 0; e < n; e++)
                if (Active(id[head + e], left[head + e], statusCount)) row++;
            if (row == 0) continue;
            if (k + row > marks.Length) break;
            int place = 0;
            for (int e = 0; e < n; e++)
            {
                if (!Active(id[head + e], left[head + e], statusCount)) continue;
                marks[k++] = new StatusMark(i, id[head + e], place++, row);
            }
        }
        return k;
    }

    /// <summary>The sideways offset of marker <paramref name="place"/> in a row of <paramref name="row"/> markers <paramref name="spacing"/> apart, centred on the unit: -spacing / 2 and +spacing / 2 for a row of two.</summary>
    public static float RowOffset(int place, int row, float spacing) => (place - (row - 1) * 0.5f) * spacing;

    private static bool Active(int status, int ticksLeft, int statusCount) => ticksLeft > 0 && (uint)status < (uint)statusCount;
}
