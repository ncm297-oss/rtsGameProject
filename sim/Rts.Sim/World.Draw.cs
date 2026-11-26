using Rts.Sim.Economy;
using Rts.Sim.Save;

namespace Rts.Sim;

// M6-S13: the owner's draw rule (docs/02 "Game flow" 4, docs/03 "Implementation (M6-S13)", docs/01 2026-11-25-1437). A match
// is spent on a tick when it is not over, at least two sides that ever built still stand, and no undefeated seat can still
// train a unit. The clock counts the spent ticks in a row; a tick that is not spent, on which a non-summoned unit died or on
// which a seat was defeated sets it back to 0. When it reaches rules.json's draw.afterSeconds the match is drawn: over for
// good, nobody defeated. Data without the draw block has no rule: the clock stays 0.
public sealed partial class World
{
    // The ticks the match has been spent without a break (0 to DrawWaitTicks; DrawWaitTicks exactly when drawn). Hashed and
    // saved only when not 0 (with _drawn), so a match that is never spent hashes and saves as before the rule.
    private int _drawClock;
    private bool _drawn;
    // Per seat, the gold cost of its faction's cheapest unit (any unit of the faction); int.MaxValue for a faction with none.
    private int[] _drawCheapest = System.Array.Empty<int>();

    /// <summary>M6-S13: the wait in ticks the match must stay spent before it is drawn (<c>rules.json</c> <c>draw.afterSeconds</c>); 0 = the data has no draw rule.</summary>
    public int DrawWaitTicks => Data.Rules.DrawAfterTicks;

    /// <summary>
    /// M6-S13: true once the match ended in a draw (it stayed spent for <see cref="DrawWaitTicks"/>): <see cref="MatchOver"/> is
    /// true with it, nobody was defeated by it, and both stay true for good. Hashed and saved.
    /// </summary>
    public bool MatchDrawn => _drawn;

    /// <summary>M6-S13: the ticks the match has been spent in a row (0 when it is not, or with no draw rule; <see cref="DrawWaitTicks"/> once drawn).</summary>
    public int DrawClock => _drawClock;

    /// <summary>
    /// M6-S13, for the view's countdown: the ticks left before the draw while the clock runs (1 to <see cref="DrawWaitTicks"/>
    /// - 1 after the first spent tick); -1 when it is not running (no draw rule, the match is not spent, or it is over).
    /// </summary>
    public int DrawTicksLeft => _drawClock == 0 || _drawn || _matchOverLatched ? -1 : DrawWaitTicks - _drawClock;

    /// <summary>Whether the draw state is away from its start (a clock running or a draw): the state hash and the save carry it only then.</summary>
    internal bool DrawStateSet => _drawClock != 0 || _drawn;

    /// <summary>Fills the per-seat cheapest-unit table from the data and the seats (derived: in every save and replay header).</summary>
    private void InitDraw(SimConfig config)
    {
        _drawCheapest = new int[config.PlayerCount];
        for (int p = 0; p < config.PlayerCount; p++)
        {
            int faction = FactionOf(p), cheapest = int.MaxValue;
            if (faction >= 0)
            {
                foreach (int t in Data.Factions[faction].Units)
                    if (Data.Units[t].CostGold < cheapest) cheapest = Data.Units[t].CostGold;
            }
            else
            {
                // Test data without factions: every unit type is the seat's.
                foreach (Data.UnitDef u in Data.Units)
                    if (u.CostGold < cheapest) cheapest = u.CostGold;
            }
            _drawCheapest[p] = cheapest;
        }
    }

    /// <summary>
    /// M6-S13: whether <paramref name="player"/> can still train a unit (docs/03 "Implementation (M6-S13)"): it has a unit in a
    /// production queue, or it can produce (a finished unit-producing building or a live worker) and its banked gold, plus the
    /// gold its units carry, plus (with a live worker) the gold left in every gold node, is at least its faction's cheapest
    /// unit's gold cost. A faction with a 0-gold unit always can. Wood is not looked at. Reads counters only.
    /// </summary>
    public bool CanStillTrain(int player)
    {
        if (!_ledger.Has(player)) return false;
        int cheapest = _drawCheapest[player];
        if (cheapest == 0 || _ledger.QueuedUnits[player] > 0) return true;
        bool worker = _ledger.LiveWorkers[player] > 0;
        if (!worker && _ledger.ProductionBuildings[player] == 0) return false;
        long gold = (long)_ledger.Gold[player] + _ledger.CarriedGold[player];
        if (worker) gold += Resources.GoldLeft;
        return gold >= cheapest;
    }

    /// <summary>
    /// M6-S13: whether the match is spent now: not over, at least two sides that ever built still stand, and no undefeated
    /// seat <see cref="CanStillTrain"/>. Seats walked in player order.
    /// </summary>
    public bool MatchSpent => !MatchOver && TwoSidesStand() && !AnySeatCanTrain();

    private bool TwoSidesStand()
    {
        int first = -1;
        for (int p = 0; p < _ledger.EverBuilt.Length; p++)
        {
            if (!_ledger.EverBuilt[p] || _ledger.Defeated[p]) continue;
            if (first < 0) first = p;
            else if (!AreAllied(first, p)) return true;
        }
        return false;
    }

    private bool AnySeatCanTrain()
    {
        for (int p = 0; p < _ledger.Defeated.Length; p++)
            if (!_ledger.Defeated[p] && CanStillTrain(p)) return true;
        return false;
    }

    /// <summary>
    /// Phase 11c (M6-S13), right after the defeat check (a win on this tick stands: a match with a result is not spent): the
    /// clock goes up on a spent tick, back to 0 on a tick that is not spent, on which a non-summoned unit died or a seat was
    /// defeated; at the wait the match is drawn (the result latched, no seat defeated, nothing freed, no event). Nothing to do
    /// without a draw rule or once drawn.
    /// </summary>
    internal void UpdateDraw()
    {
        // The units freed since the last check (this tick's deaths: phases 1-11; a summon's death or expiry is not counted),
        // zeroed every tick so the count is 0 between ticks whether or not the rule runs.
        bool died = Units.PermanentFrees != 0;
        Units.PermanentFrees = 0;
        if (DrawWaitTicks == 0 || _drawn) return;
        if (died || DefeatEventCount > 0 || !MatchSpent)
        {
            _drawClock = 0;
            return;
        }
        if (++_drawClock < DrawWaitTicks) return;
        _drawn = true;
        _matchOverLatched = true;
    }

    /// <summary>Rebuilds every count the draw check reads from the stores (a load; a test that wrote store arrays directly).</summary>
    internal void RecountDraw() => _ledger.RecountDraw(Units, Buildings);

    /// <summary>The draw part (<c>DRAW</c>, inside <c>WRLD</c>): written exactly when the data has a draw rule and the state is set, so a world whose clock is 0 and that is not drawn saves exactly as before the rule.</summary>
    private void WriteDraw(SaveWriter w)
    {
        if (DrawWaitTicks == 0 || !DrawStateSet) return;
        w.Tag(SaveFormat.TagDraw);
        w.Write(_drawClock);
        w.Write(_drawn);
    }

    /// <summary>
    /// Reads what <see cref="WriteDraw"/> wrote, after the result flag. A part on data without a draw rule, one that holds
    /// nothing (clock 0, not drawn), a clock below 0 or above the wait, a clock at the wait without the draw, a draw whose
    /// clock is not the wait or a draw without the result flag is refused, never trusted.
    /// </summary>
    private void ReadDraw(SaveReader r)
    {
        _drawClock = 0;
        _drawn = false;
        if (!r.TryTag(SaveFormat.TagDraw)) return;
        int wait = DrawWaitTicks;
        int clock = r.ReadInt32();
        bool drawn = r.ReadBool();
        if (!r.Check(wait > 0 && clock >= 0 && clock <= wait && (clock != 0 || drawn))) return;
        if (!r.Check(drawn ? clock == wait && _matchOverLatched : clock < wait)) return;
        _drawClock = clock;
        _drawn = drawn;
    }
}
