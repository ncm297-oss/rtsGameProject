using Rts.Sim.Data;

namespace Rts.Sim;

/// <summary>
/// One player's seat in a match (M5-2): the faction it plays, who controls it, and, for an AI seat, how well it plays.
/// <see cref="SimConfig.Seats"/> holds one per player; recorded in the replay header (format 5).
/// </summary>
/// <param name="Faction">The faction id (<see cref="GameData.Factions"/>); -1 only with data that has no factions.</param>
/// <param name="Controller">Human or AI.</param>
/// <param name="Difficulty">The AI's difficulty (<see cref="AiDef.Difficulty"/>); kept but unused on a human seat.</param>
public readonly record struct PlayerSeat(int Faction, SeatController Controller, AiDifficulty Difficulty);
