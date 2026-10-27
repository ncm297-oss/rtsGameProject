using Rts.Sim.Data;

namespace Rts.Sim;

/// <summary>
/// One player's seat in a match (M5-2): the faction it plays, who controls it, for an AI seat how well it plays, and
/// (M6-S4) its team. <see cref="SimConfig.Seats"/> holds one per player; recorded in the replay header (format 5, the team
/// since format 7) and the save header (the team since format 3).
/// </summary>
/// <param name="Faction">The faction id (<see cref="GameData.Factions"/>); -1 only with data that has no factions.</param>
/// <param name="Controller">Human or AI.</param>
/// <param name="Difficulty">The AI's difficulty (<see cref="AiDef.Difficulty"/>); kept but unused on a human seat.</param>
/// <param name="Team">
/// M6-S4: 0 (the default) for no team, the seat is a side of its own (free-for-all); 1 to <see cref="MaxTeam"/> a named
/// team, whose seats are allies (<see cref="World.AreAllied"/>).
/// </param>
public readonly record struct PlayerSeat(int Faction, SeatController Controller, AiDifficulty Difficulty, int Team = 0)
{
    /// <summary>The highest team number a seat may have (M6-S4: named teams are 1-8; 0 is no team).</summary>
    public const int MaxTeam = 8;
}
