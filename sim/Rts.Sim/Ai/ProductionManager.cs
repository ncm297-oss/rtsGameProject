using Rts.Sim.Data;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's army production (M5-2, docs/03 "Implementation (M5-2)"): when the build order sent nothing this think, it trains
/// the current age's most under-weighted composition unit (fewest own live + queued half-pop per unit of weight, M5-3:
/// weights are population shares; composition order on ties) among those a finished own building with an empty queue may train now, if what is left this think pays for it;
/// else it waits (it doesn't spend on a cheaper unit instead). M5-4g: it spends only above the reserve of the build order's
/// reserved step (<see cref="AiPlayer.CanAffordAboveReserve"/>). BUG-1050: while the AI spends down
/// (<see cref="AiPlayer.SpendingDown"/>, a starting-resources preset's <c>aiSpendAbove</c>) it trains even on a think the build
/// order sent, one Train per producer, at producers whose queue holds fewer than <see cref="AiRulesDef.SpendDownQueue"/> (<c>ai.spendDownQueue</c>), until the bank
/// is down to the threshold or nothing more can be trained. M5-9a: each composition weight is shifted toward what counters the
/// enemy units the AI remembers seeing: weight x (1 + <c>counterStrength</c> x <see cref="AiPlayer.CounterShare"/> of the
/// unit's slot); no unit is added and none weighted below the opening's own. M6-S15 C (BUG-1890): when the pick can never be
/// paid for (income cannot close the gap), it buys instead what it can (<see cref="SpendInstead"/>).
/// </summary>
internal static class ProductionManager
{
    /// <summary>One Train at most a think; while spending down, one per producer (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (ai.ExecutorSent && !ai.SpendingDown) return;
        // Ends: each Train sent marks its building busy for the rest of the think (BuildingBusyUntil), so at most one per producer.
        while (TrainOne(ai) && ai.SpendingDown) { }
    }

    /// <summary>Sends the current composition's most under-weighted unit at a producer that may take it; true when one was sent.</summary>
    private static bool TrainOne(AiPlayer ai)
    {
        int pick = Pick(ai);
        // Not judged: the next think simply looks at the queues again. M5-4g: only above the build order's reserve.
        if (pick >= 0 && ai.TryTrain(pick, emptyQueueOnly: !ai.SpendingDown, ref ai.Board.ProductionPending, aboveReserve: true)) return true;
        return ai.PickNeverPayable() && SpendInstead(ai);
    }

    /// <summary>
    /// M6-S15 C (BUG-1890, docs/01 2026-11-27-1857 (4)): production's pick can never be paid for
    /// (<see cref="AiPlayer.PickNeverPayable"/>), so instead it trains (1) the most under-weighted unit of the current
    /// composition it can pay for now (<see cref="Pick"/>'s share order, producer test and unit limit), else (2) the first unit
    /// type of its faction in data order, the worker included, that it can pay for now at a finished own building with an
    /// empty queue (population room and the unit limit as the train check has them). A reserve holds these buys only while
    /// the seat could ever fill it (<see cref="AiPlayer.ReserveFillable"/>). One Train; true when it was sent.
    /// </summary>
    private static bool SpendInstead(AiPlayer ai)
    {
        bool hold = ai.ReserveFillable;
        int pick = Pick(ai, payableOnly: true, holdReserve: hold);
        if (pick >= 0 && ai.TryTrain(pick, emptyQueueOnly: !ai.SpendingDown, ref ai.Board.ProductionPending, aboveReserve: hold)) return true;
        if ((uint)ai.Faction >= (uint)ai.Data.Factions.Length) return false;
        var units = ai.Data.Factions[ai.Faction].Units;
        for (int k = 0; k < units.Length; k++)
            if (ai.TryTrain(units[k], emptyQueueOnly: true, ref ai.Board.ProductionPending, aboveReserve: hold)) return true;
        return false;
    }

    /// <summary>
    /// The unit type production trains next (-1: none): the current age's most under-weighted composition entry that some
    /// producer may take now, cost aside (<see cref="CanTrainSomewhere"/>). Production waits for this one rather than buy a
    /// cheaper one, so M5-9a round 2's starved check (<see cref="AiPlayer.Starved"/>, BUG-1154) judges the seat on it too.
    /// Allocates nothing.
    /// </summary>
    internal static int Pick(AiPlayer ai) => Pick(ai, payableOnly: false, holdReserve: false);

    /// <summary>See <see cref="Pick(AiPlayer)"/>; M6-S15 C: with <paramref name="payableOnly"/> only an entry the seat can pay for now (above the reserve with <paramref name="holdReserve"/>).</summary>
    private static int Pick(AiPlayer ai, bool payableOnly, bool holdReserve)
    {
        var compositions = ai.Opening.Compositions; // M5-8a: the picked opening's
        if (compositions.Length == 0) return -1;
        AiComposition mix = compositions[System.Math.Clamp(ai.View.Age, 1, compositions.Length) - 1];
        int pick = -1;
        float pickShare = float.MaxValue;
        for (int k = 0; k < mix.Units.Length; k++)
        {
            int type = mix.Units[k];
            UnitDef def = ai.Data.Units[type];
            // M5-9a: the weight shifted up by the share of the remembered enemy the unit's slot counters, times the
            // difficulty's counterStrength; never below the opening's own (strength 0 multiplies by exactly 1).
            float weight = mix.Weights[k] * (1f + ai.Knobs.CounterStrength * ai.CounterShare[(int)def.Slot]);
            // Weights are population shares (Requests 36 a): a 3-pop unit fills three times what a 1-pop one does.
            float share = (ai.Live[type] + ai.Queued[type]) * def.HalfPop / weight;
            // M8-4 (Request 56 G4): a type at its maxAlive (this think's Trains counted) is skipped, so the next by share is
            // trained rather than the seat waiting on it.
            if (share >= pickShare || ai.AtUnitLimit(type) || !CanTrainSomewhere(ai, type)) continue;
            if (payableOnly && !(holdReserve ? ai.CanAffordAboveReserve(def.CostGold, def.CostWood) : ai.CanAfford(def.CostGold, def.CostWood))) continue;
            pick = type;
            pickShare = share;
        }
        return pick;
    }

    /// <summary>Whether a finished own building with an empty queue (spending down: fewer than <see cref="AiRulesDef.SpendDownQueue"/> (<c>ai.spendDownQueue</c>)) and no AI command waiting on it may train <paramref name="type"/> now (its cost aside).</summary>
    private static bool CanTrainSomewhere(AiPlayer ai, int type)
    {
        int at = ai.Data.Units[type].TrainedAtTypeId;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            int slot = ai.OwnBuildingSlots[k];
            if (ai.OwnBuildingSites[k] || ai.OwnBuildingTypes[k] != at || ai.Board.BuildingBusyUntil[slot] >= ai.Now
                || ai.View.QueueCount(slot) >= (ai.SpendingDown ? ai.Rules.SpendDownQueue : 1)) continue;
            if (ai.View.CanTrain(slot, type, out Economy.TrainError why) || why == Economy.TrainError.CannotAfford) return true;
        }
        return false;
    }
}
