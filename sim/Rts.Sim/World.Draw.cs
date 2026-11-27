using Rts.Sim.Economy;
using Rts.Sim.Save;

namespace Rts.Sim;

// M6-S13: the owner's draw rule (docs/02 "Game flow" 4, docs/03 "Implementation (M6-S13)", docs/01 2026-11-25-1437). A match
// is spent on a tick when it is not over, at least two sides that ever built still stand, and no undefeated seat can still
// train a unit (M6-SH3 made "can still train" exact: CanStillTrain). The clock counts the spent ticks in a row; a tick that is not spent, on which a non-summoned unit died or on
// which a seat was defeated sets it back to 0. When it reaches rules.json's draw.afterSeconds the match is drawn: over for
// good, nobody defeated. Data without the draw block has no rule: the clock stays 0.
public sealed partial class World
{
    // The ticks the match has been spent without a break (0 to DrawWaitTicks; DrawWaitTicks exactly when drawn). Hashed and
    // saved only when not 0 (with _drawn), so a match that is never spent hashes and saves as before the rule.
    private int _drawClock;
    private bool _drawn;
    // M6-SH3: per seat, the unit types "can still train" looks at (its faction's; every type for a seat with no faction, test
    // data) and whether one of them costs nothing (gold and wood 0: the seat always can). Derived from the data and the seats.
    private int[][] _drawUnits = System.Array.Empty<int[]>();
    private bool[] _drawFree = System.Array.Empty<bool>();

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

    /// <summary>Fills the per-seat unit lists and free flags from the data and the seats (derived: in every save and replay header).</summary>
    private void InitDraw(SimConfig config)
    {
        _drawUnits = new int[config.PlayerCount][];
        _drawFree = new bool[config.PlayerCount];
        for (int p = 0; p < config.PlayerCount; p++)
        {
            int faction = FactionOf(p);
            int[] units;
            if (faction >= 0) units = System.Linq.ImmutableArrayExtensions.ToArray(Data.Factions[faction].Units);
            else
            {
                // Test data without factions: every unit type is the seat's.
                units = new int[Data.Units.Length];
                for (int t = 0; t < units.Length; t++) units[t] = t;
            }
            _drawUnits[p] = units;
            foreach (int t in units)
                if (Data.Units[t].CostGold == 0 && Data.Units[t].CostWood == 0) _drawFree[p] = true;
        }
    }

    /// <summary>
    /// M6-SH3 (docs/02 "Game flow" 4, docs/03 "Implementation (M6-SH3)" B, docs/01 2026-11-27 second row): whether
    /// <paramref name="player"/> can still train a unit. (a) A unit item of its is being trained, or one waits in a queue with
    /// population room for it now; or (b) it can produce (a finished unit-producing building or a live worker) and, for at least
    /// one unit type of its faction not at its <c>maxAlive</c> limit, its gold reach is at least the type's gold cost and its
    /// wood reach at least its wood cost. A resource's reach is the bank, plus, only with a live worker and a finished drop-off
    /// of its own, what its units carry and what is left on the map. Population room is not asked in (b). A faction with a
    /// unit that costs nothing always can. Reads counters only; allocates nothing.
    /// </summary>
    public bool CanStillTrain(int player)
    {
        if (!_ledger.Has(player)) return false;
        if (_drawFree[player] || _ledger.TrainingUnits[player] > 0) return true;
        int[] units = _drawUnits[player];
        Data.GameData data = Data;
        // (a) a waiting item with room now: any waiting unit type whose item fits under the cap.
        if (_ledger.QueuedUnits[player] > _ledger.TrainingUnits[player])
        {
            int room = _ledger.HalfPopCap[player] - _ledger.HalfPop[player];
            for (int t = 0; t < data.Units.Length; t++)
                if (_ledger.WaitingUnits(player, t) > 0 && data.Units[t].ItemHalfPop <= room) return true;
        }
        // (b) can produce and can pay for a type not at its limit.
        bool worker = _ledger.LiveWorkers[player] > 0;
        if (!worker && _ledger.ProductionBuildings[player] == 0) return false;
        long gold = _ledger.Gold[player], wood = _ledger.Wood[player];
        if (worker && _ledger.FinishedDropOffs[player] > 0)
        {
            gold += _ledger.CarriedGold[player] + Resources.GoldLeft;
            wood += _ledger.CarriedWood[player] + Resources.WoodLeft;
        }
        for (int k = 0; k < units.Length; k++)
        {
            Data.UnitDef def = data.Units[units[k]];
            if (gold < def.CostGold || wood < def.CostWood) continue;
            if (def.MaxAlive > 0 && _ledger.LimitedCount(player, units[k]) + def.SpawnCount > def.MaxAlive) continue;
            return true;
        }
        return false;
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
