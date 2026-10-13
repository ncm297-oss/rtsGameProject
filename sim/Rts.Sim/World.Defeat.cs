using System;

namespace Rts.Sim;

// M5-4b: the defeat rule (docs/02 "Game flow" 4, docs/03 "Implementation (M5-4b)"). A player with no live building (sites
// included) who had one before is defeated, once and for good; a player who never built is never defeated.
public sealed partial class World
{
    // At most one event per player a tick; sized in the constructor.
    private readonly DefeatEvent[] _defeatEvents;

    /// <summary>Whether <paramref name="player"/> is defeated: it had a building and has none left (checked once a tick, after phase 11). Sticky and hashed; false for no such player.</summary>
    public bool IsDefeated(int player) => _ledger.Has(player) && _ledger.Defeated[player];

    /// <summary>Whether <paramref name="player"/> has ever had a building (a site counts). Sticky and hashed; false for no such player.</summary>
    public bool EverBuilt(int player) => _ledger.Has(player) && _ledger.EverBuilt[player];

    /// <summary>
    /// The players defeated in the last tick run, in player order, each with its tick. Emptied at the start of every tick like
    /// <see cref="Deaths"/>. Output for views, not state: not hashed (the flags it reports are).
    /// </summary>
    public ReadOnlySpan<DefeatEvent> DefeatEvents => _defeatEvents.AsSpan(0, DefeatEventCount);

    /// <summary>Number of entries in <see cref="DefeatEvents"/>.</summary>
    internal int DefeatEventCount { get; private set; }

    // BUG-0672: set by UpdateDefeats the first tick the result holds, never cleared (a player that never built placing its
    // first building later doesn't take the result back). Hashed.
    private bool _matchOverLatched;

    /// <summary>
    /// True once at most one player that ever built is undefeated while at least two ever built: the match has a winner (or,
    /// when the last ones fell on the same tick, none). Never true while fewer than two players have built; once true, true
    /// for good (latched by the defeat check every tick).
    /// </summary>
    public bool MatchOver => _matchOverLatched || HasResult();

    /// <summary>Whether the match result is latched (<see cref="MatchOver"/>; for the state hash).</summary>
    internal bool MatchOverLatched => _matchOverLatched;

    private bool HasResult()
    {
        int built = 0, standing = 0;
        for (int p = 0; p < _ledger.EverBuilt.Length; p++)
        {
            if (!_ledger.EverBuilt[p]) continue;
            built++;
            if (!_ledger.Defeated[p]) standing++;
        }
        return built >= 2 && standing <= 1;
    }

    /// <summary>Empties <see cref="DefeatEvents"/> (start of a tick).</summary>
    internal void ClearDefeatEvents() => DefeatEventCount = 0;

    /// <summary>
    /// Phase 11b: every player that ever built, isn't defeated yet and has no live building now is defeated, in player order,
    /// with an event. Runs after every phase that can take a building away (a cancel in phase 1, an ability in phase 6, a hit
    /// or a landing in phase 11).
    /// </summary>
    internal void UpdateDefeats()
    {
        for (int p = 0; p < _ledger.Defeated.Length; p++)
        {
            if (!_ledger.EverBuilt[p] || _ledger.Defeated[p] || _ledger.LiveBuildings[p] > 0) continue;
            _ledger.Defeated[p] = true;
            _defeatEvents[DefeatEventCount++] = new DefeatEvent(p, TickNumber);
        }
        if (!_matchOverLatched && HasResult()) _matchOverLatched = true;
    }
}
