using System;
using System.Collections.Immutable;
using Rts.Sim.Replays;

namespace Rts.Game;

/// <summary>One save file whose header reads, as the Load Game list shows it (M6-V14): where it is, when it was saved, and its header's seed, seats and tick.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="When">The local time it was saved (from the file name; the file's write time for a name without one).</param>
/// <param name="Seed">The header's seed.</param>
/// <param name="Seats">The header's seats (faction id, human / AI, difficulty), one per player.</param>
/// <param name="Tick">The tick the save was made at (50 ms each): the match clock it resumes at.</param>
public sealed record SaveEntry(string Path, DateTime When, ulong Seed, ImmutableArray<ReplaySeat> Seats, int Tick);
