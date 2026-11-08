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
/// unit's slot); no unit is added and none weighted below the opening's own.
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
        int age = ai.View.Age;
        var compositions = ai.Opening.Compositions; // M5-8a: the picked opening's
        if (compositions.Length == 0) return false;
        AiComposition mix = compositions[System.Math.Clamp(age, 1, compositions.Length) - 1];
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
            if (share >= pickShare || !CanTrainSomewhere(ai, type)) continue;
            pick = type;
            pickShare = share;
        }
        if (pick < 0) return false;
        // Not judged: the next think simply looks at the queues again. M5-4g: only above the build order's reserve.
        return ai.TryTrain(pick, emptyQueueOnly: !ai.SpendingDown, ref ai.Board.ProductionPending, aboveReserve: true);
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
