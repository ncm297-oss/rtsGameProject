using Rts.Sim.Data;

namespace Rts.Sim.Replays;

/// <summary>
/// One player's seat as a replay header records it (format 5, M5-2): the faction by its string id (so the file reads without
/// the data; <c>-</c> for data without factions), who controlled it, the AI difficulty, and (format 7, M6-S4) its team.
/// </summary>
/// <param name="Faction">The faction's string id (<c>FactionDef.Key</c>), or <c>-</c>.</param>
/// <param name="Controller">Human or AI when recorded (playback plays every seat as human: the recorded commands play the AI's part).</param>
/// <param name="Difficulty">The seat's AI difficulty.</param>
/// <param name="Team">The seat's team (<see cref="PlayerSeat.Team"/>: 0 no team, 1-8); 0 for a format 5-6 replay, which has no team token.</param>
public readonly record struct ReplaySeat(string Faction, SeatController Controller, AiDifficulty Difficulty, int Team = 0);
