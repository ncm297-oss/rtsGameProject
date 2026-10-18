using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;

namespace Rts.Sim.ViewApi;

/// <summary>
/// M6-V13: which opening an AI seat played, worked out from the seed alone, for a sim with no AI (a watched replay plays every
/// seat human, so <see cref="Simulation.AiOpening"/> is -1 there). Read-only: it draws from a fresh copy of the seat's stream,
/// never the sim's.
/// </summary>
/// <remarks>
/// The same construction <see cref="Simulation"/> makes for an AI seat: a <see cref="SimRng"/> on
/// <c>(seed, RngStream.Ai(player))</c> handed to <see cref="AiPlayer.PickOpening"/>, the first draw that stream makes.
/// </remarks>
public static class AiOpeningLookup
{
    /// <summary>The index in the faction's <see cref="AiDef.Openings"/> (of <paramref name="openingCount"/>) an AI of <paramref name="difficulty"/> in seat <paramref name="player"/> plays on <paramref name="seed"/>.</summary>
    public static int Pick(ulong seed, int player, AiDifficulty difficulty, int openingCount)
    {
        var rng = new SimRng(seed, (ulong)RngStream.Ai(player));
        return AiPlayer.PickOpening(openingCount, difficulty, ref rng);
    }
}
