using System;
using System.Collections.Immutable;
using Rts.Sim.Replays;

namespace Rts.Game;

/// <summary>One readable replay file as the Load Game list shows it (M6-V9): where it is, when it was recorded, and its header's seed, seats and length.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="When">The local time the match started (from the file name; the file's write time for a name without one).</param>
/// <param name="Seed">The header's seed.</param>
/// <param name="Seats">The header's seats as played (faction id, human / AI, difficulty, team: format 7's, 0 before it), one per player.</param>
/// <param name="TickCount">Ticks recorded (50 ms each).</param>
public sealed record ReplayEntry(string Path, DateTime When, ulong Seed, ImmutableArray<ReplaySeat> Seats, int TickCount)
{
    /// <summary>M6-V17: the header's starting-resources preset id (format 8's <c>starting-resources</c> line; <c>standard</c> before format 8).</summary>
    public string StartingResources { get; init; } = Rts.Sim.Data.StartingResourcesDef.StandardId;
}
