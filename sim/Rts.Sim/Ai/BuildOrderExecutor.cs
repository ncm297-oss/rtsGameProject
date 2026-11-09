using System;
using Rts.Sim.Data;
using Rts.Sim.Economy;

namespace Rts.Sim.Ai;

/// <summary>
/// Walks the build order of the AI's picked <c>ai.json</c> opening (M5-8a) (M5-2, docs/03 "Implementation (M5-2)", the executor's semantics the data
/// track writes against). Every think, every step whose trigger has been reached and that is not complete is worked, in
/// file order, at most one command per step: steps run side by side.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A step is due from the first think its trigger holds (<c>time</c>: the tick number reaches it; <c>workers</c>: the
/// player's live workers; <c>pop</c>: its population, half-pop) and stays due. BUG-1050: while the AI spends down
/// (<see cref="AiPlayer.SpendingDown"/>), the first <c>time</c> step not yet due comes due at that think, one a think. Its first command waits
/// <c>buildOrderSlack</c> after that (an <c>expand</c> also waits for <c>expandAfter</c>).</item>
/// <item><c>train</c>: <c>count</c> Train commands accepted over the step's life, one per think; <c>build</c>,
/// <c>research</c> and <c>expand</c>: complete on the first accepted command (a research also once the tech is researched or
/// queued by anyone). D15 C (M5-4f): a <c>build</c> step of the faction's House is complete, no command sent, once the
/// player's population cap is the hard cap (<c>popCap</c>).</item>
/// <item>A command is judged at the first think after it applied (<see cref="PendingOrder"/>); until then the step sends
/// nothing. A refused one is sent again at a later think (a refused Build's anchor gets a retry mark, so the site search moves
/// on); a step that can't act (no gold, no site, no building, no worker) simply waits (one whose site search found nothing
/// waits <see cref="NoSiteRetryThinks"/> thinks before it searches again, BUG-0791; one stopped before any search, its
/// requires unmet or the building store full, doesn't wait, BUG-0910). Never more than one command per step
/// per think, so a refusal is retried, not spammed.</item>
/// <item><c>build</c> searches round the player's Town Hall (a camp round the known tree nearest it); <c>expand</c> places a
/// Town Hall round the known gold mine nearest the player's hall with no own or seen hall within
/// <see cref="AiRulesDef.ExpandReachCells"/>, else waits.</item>
/// <item>M5-4d slice 2, <b>rebuilding</b> (docs/03 "Implementation (M5-4d slice 2)"): before the steps, a <c>build</c> step
/// done whose type the player now holds fewer of (sites included) than its done <c>build</c> steps of that type comes due
/// again, the newest such step a type a think (<see cref="ReopenLost"/>); and with no Town Hall and a worker alive the hall
/// comes first (<see cref="HallFirst"/>): no step is worked while it is sent, in flight or banked for.</item>
/// </list>
/// </remarks>
internal static class BuildOrderExecutor
{
    /// <summary>Works every due step once (see the type's remarks).</summary>
    internal static void Run(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        var steps = ai.Opening.BuildOrder; // M5-8a: the picked opening's
        int slack = ai.Knobs.BuildOrderSlackTicks;
        ReopenLost(ai); // M5-4d slice 2
        if (HallFirst(ai)) return;
        bool mayReserve = ai.MayReserve(); // BUG-1021 / BUG-1025: a wave-sized army, the base not defended
        bool pulled = false; // BUG-1050: a time step pulled due early this think
        for (int i = 0; i < steps.Length; i++)
        {
            if (board.StepDone[i]) continue;
            AiStep step = steps[i];
            if (board.StepDueTick[i] < 0)
            {
                bool early = false;
                if (!Triggered(ai, step))
                {
                    // BUG-1050: while the AI spends down, the first time step not yet due comes due now (one a think, file
                    // order): a big start buys the opening's timed steps sooner instead of waiting in the bank for the clock.
                    if (step.Trigger != AiStepTrigger.Time || pulled || !ai.SpendingDown) continue;
                    early = pulled = true;
                }
                board.StepDueTick[i] = step.Trigger == AiStepTrigger.Time && !early ? step.At : ai.Now;
            }
            if (ai.Now < board.StepDueTick[i] + slack) continue;
            if (step.Action == AiStepAction.Expand && ai.Now < ai.Knobs.ExpandAfterTicks) continue;

            ref PendingOrder pending = ref board.StepPending[i];
            if (pending.Kind != PendingKind.None)
            {
                if (ai.Now <= pending.AppliesAt) continue; // not applied yet
                Judge(ai, i, step, ref pending);
                if (board.StepDone[i]) continue;
            }
            if (step.Action == AiStepAction.Research && ai.TechResearchedOrQueued(step.Target))
            {
                board.StepDone[i] = true;
                continue;
            }
            // D15 option C (the owner, 2026-10-14): a house adds nothing at the hard cap, so its step is done unsent.
            if (step.Action == AiStepAction.Build && step.Target == ai.HouseType && ai.View.HalfPopCap >= ai.Data.Rules.HalfPopCap)
            {
                board.StepDone[i] = true;
                continue;
            }
            // BUG-0791: a site search that found nothing is not run again for NoSiteRetryThinks thinks. In a crowded scene the
            // affordable step's search scanned the whole placement radius at every think (the storms fuzz's think, 0.5 to
            // 1.6 ms). A site freed meanwhile is taken at most that many thinks late.
            if (ai.Now < board.StepNoSiteUntil[i]) continue;
            // M5-4g: the first step from here on that only its cost holds back is the think's reserved step; the train
            // steps after it (and the production manager) spend only above its cost.
            if (mayReserve && ai.ReserveStep < 0 && step.Action != AiStepAction.Train && Reservable(ai, step, out int gold, out int wood)) ai.SetReserve(i, gold, wood);
            ai.NoSiteFound = false;
            if (Act(ai, step, ref pending, ai.ReserveStep >= 0)) ai.ExecutorSent = true;
            else if (ai.NoSiteFound) board.StepNoSiteUntil[i] = ai.Now + NoSiteRetryThinks * ai.ThinkTicks;
        }
    }

    /// <summary>
    /// M5-4d slice 2: one pass over the steps counts, per building type, the <c>build</c> steps done and the newest of them;
    /// one over the census counts the player's buildings of each type (sites included, what the fog grants of its own). A
    /// type held fewer times than its done steps lost one: its newest done step becomes due again now (<c>StepDone</c> false,
    /// one accepted command fewer, due at this think less the slack, so it acts at once; the site search, the reserve and
    /// <see cref="NoSiteRetryThinks"/> as for any step). One step a type a think: a second loss reopens the next one at the next
    /// think. A House step at the hard cap stays done (D15 C). An <c>expand</c> step is never reopened (a lost expansion is
    /// not rebuilt yet). Why the newest: the file's earlier steps came first for a reason, so the last one built is the
    /// least needed of them, and a reopened step later in the file never holds an earlier one's reserve.
    /// </summary>
    internal static void ReopenLost(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        var steps = ai.Opening.BuildOrder;
        int[] done = ai.DoneOfType, newest = ai.NewestDoneStep, held = ai.HeldOfType;
        int types = ai.Data.Buildings.Length;
        Array.Clear(done);
        bool any = false;
        for (int i = 0; i < steps.Length; i++)
        {
            AiStep step = steps[i];
            if (!board.StepDone[i] || step.Action != AiStepAction.Build || (uint)step.Target >= (uint)types) continue;
            done[step.Target]++;
            newest[step.Target] = i;
            any = true;
        }
        if (!any) return;
        Array.Clear(held);
        for (int k = 0; k < ai.OwnBuildingCount; k++) held[ai.OwnBuildingTypes[k]]++;
        bool atCap = ai.View.HalfPopCap >= ai.Data.Rules.HalfPopCap;
        for (int t = 0; t < types; t++)
        {
            if (done[t] <= held[t] || t == ai.HouseType && atCap) continue;
            int i = newest[t];
            board.StepDone[i] = false;
            board.StepQueued[i] = Math.Max(0, board.StepQueued[i] - 1);
            board.StepDueTick[i] = Math.Max(0, ai.Now - ai.Knobs.BuildOrderSlackTicks); // no slack: due now
            board.StepPending[i] = default;
        }
        // Scratch: left empty between thinks, so a loaded seat equals the saved one with nothing stored (the AIST layout is unchanged).
        Array.Clear(done);
        Array.Clear(newest);
        Array.Clear(held);
    }

    /// <summary>
    /// M5-4d slice 2: with no Town Hall (finished or a site) and a worker alive, a Build of the faction's Town Hall comes
    /// before any step: at the last hall's anchor (<see cref="AiPlayer.LostHallCell"/>) when the player may place it there,
    /// else by the usual ring search round that anchor's centre (round the player's first building, or its workers'
    /// centroid, with no anchor remembered). Its command waits to be judged in <see cref="Blackboard.HousePending"/> (one
    /// base Build at a time, judged as the economy judges a house: a refused anchor gets a retry mark, but the lost hall's
    /// own anchor gets one excused retry first while the query still allows it, <see cref="HallRetry"/>). True (the steps
    /// wait this think) while it is sent, in flight, or held back only by its cost or by no worker free to build it this
    /// think: then its cost is the think's reserve (<see cref="AiPlayer.HallReserved"/>), so production spends
    /// only above it and the gatherers lean to what it lacks. False when there is nothing to rebuild or it can't be built
    /// for another reason (no site found, its requires, the building store full, a kind it lacks with no node to fill it):
    /// the steps go on rather than wait for good, and it is tried again at the next think.
    /// </summary>
    internal static bool HallFirst(AiPlayer ai)
    {
        if (ai.TownHallCell >= 0 || ai.WorkerCount == 0 || ai.TownHallType < 0) return false;
        Blackboard board = ai.Board;
        ref PendingOrder pending = ref board.HousePending;
        bool excused = false;
        if (pending.Kind != PendingKind.None)
        {
            if (ai.Now <= pending.AppliesAt) return pending.TypeId == ai.TownHallType; // in flight (a house Build: steps go on)
            if (!ai.BuildAccepted(pending))
            {
                // M5-H5 (BUG-1118 item 1, BUG-1148): a hall Build at the lost hall's own anchor that the anchor still takes as
                // far as the player can tell gets one excused retry there with no retry mark: its site may have been razed
                // before this judge (then the old spot is rebuilt, not a cell off it). A refusal the query can't see (a unit
                // in the way, hidden or not, or the never-seal rule) refuses the retry too, and that one is marked, so the
                // next search moves off the spot rather than send the same Build every think.
                excused = pending.TypeId == ai.TownHallType && pending.Cell == ai.LostHallCell && pending.Slot != HallRetry
                    && ai.View.CanPlace(pending.TypeId, pending.Cell, out _);
                if (!excused) board.MarkBad(pending.TypeId, pending.Cell);
            }
            pending = default;
        }
        int lost = ai.LostHallCell, centre = RebuildCentre(ai, lost);
        if (centre < 0) return false;
        BuildingDef d = ai.Data.Buildings[ai.TownHallType];
        ai.NoSiteFound = false;
        if (ai.TryBuild(ai.TownHallType, centre, lost, ref pending))
        {
            if (excused && pending.Cell == lost) pending.Slot = HallRetry; // saved with the pending order: no new field
            ai.ExecutorSent = true;
            return true;
        }
        if (ai.NoSiteFound || !PlaceableButForCost(ai, ai.TownHallType) || !Fillable(ai, d.CostGold, d.CostWood)) return false;
        ai.SetHallReserve(d.CostGold, d.CostWood); // short of its cost, or no builder free this think
        return true;
    }

    /// <summary>
    /// M5-H5 (BUG-1148): <see cref="PendingOrder.Slot"/> of a hall Build that is the excused retry at the lost hall's anchor
    /// (a Build's <c>Slot</c> is otherwise unused, 0): refused again, it gets the retry mark.
    /// </summary>
    private const int HallRetry = 1;

    /// <summary>M5-4d slice 2: the cell the hall rebuild searches round: the lost hall's centre, else <see cref="HallCentre"/>, else the workers' centroid's cell.</summary>
    private static int RebuildCentre(AiPlayer ai, int lost)
    {
        BuildingDef d = ai.Data.Buildings[ai.TownHallType];
        if (lost >= 0) return ai.CentreCellOf(d.FootprintWidth, d.FootprintHeight, lost);
        int hall = HallCentre(ai);
        if (hall >= 0) return hall;
        float x = 0f, y = 0f;
        int n = 0;
        for (int k = 0; k < ai.WorkerCount; k++)
        {
            if (!ai.View.TryGetUnit(ai.WorkerSlots[k], out SeenUnit u)) continue;
            x += u.Position.X;
            y += u.Position.Y;
            n++;
        }
        if (n == 0) return -1;
        int w = ai.View.MapWidth, h = ai.View.MapHeight;
        int cx = Math.Clamp((int)(x / n / Map.MapConstants.CellSize), 0, w - 1), cy = Math.Clamp((int)(y / n / Map.MapConstants.CellSize), 0, h - 1);
        return cy * w + cx;
    }

    /// <summary>Thinks a build or expand step waits after its site search found no anchor before it searches again (BUG-0791).</summary>
    internal const int NoSiteRetryThinks = 5;

    /// <summary>Whether <paramref name="step"/>'s trigger holds now.</summary>
    private static bool Triggered(AiPlayer ai, in AiStep step) => step.Trigger switch
    {
        AiStepTrigger.Time => ai.Now >= step.At,
        AiStepTrigger.Workers => ai.WorkerCount >= step.At,
        _ => ai.View.HalfPop >= step.At,
    };

    /// <summary>Judges step <paramref name="i"/>'s applied command: accepted counts (and may complete the step); a refused Build marks its anchor.</summary>
    private static void Judge(AiPlayer ai, int i, in AiStep step, ref PendingOrder pending)
    {
        Blackboard board = ai.Board;
        bool accepted = pending.Kind switch
        {
            PendingKind.Build => ai.BuildAccepted(pending),
            PendingKind.Train => ai.TrainAccepted(pending),
            _ => ai.TechResearchedOrQueued(pending.TypeId),
        };
        if (!accepted && pending.Kind == PendingKind.Build) board.MarkBad(pending.TypeId, pending.Cell);
        pending = default;
        if (!accepted) return;
        board.StepQueued[i]++;
        if (step.Action != AiStepAction.Train || board.StepQueued[i] >= step.Count) board.StepDone[i] = true;
    }

    /// <summary>
    /// M5-4g: whether a due <c>build</c> / <c>research</c> / <c>expand</c> step (<paramref name="gold"/> / <paramref name="wood"/>
    /// its cost) can't be paid from what is left this think while nothing but its cost holds it back as far as the
    /// type's rules tell: a build's requires met (<see cref="PlayerView.CanPlaceAnywhere"/> refusing on the cost at most) with
    /// somewhere to search round; an expand's free mine known; a research's requires met at a finished own building
    /// that researches it (a full queue is a wait, not a block). A step its rules hold back would keep the purse banked
    /// for nothing, and take the reserve from the steps after it.
    /// </summary>
    private static bool Reservable(AiPlayer ai, in AiStep step, out int gold, out int wood)
    {
        gold = wood = 0;
        GameData d = ai.Data;
        switch (step.Action)
        {
            case AiStepAction.Research:
                if ((uint)step.Target >= (uint)d.Techs.Length) return false;
                (gold, wood) = (d.Techs[step.Target].CostGold, d.Techs[step.Target].CostWood);
                return !ai.CanAfford(gold, wood) && Fillable(ai, gold, wood) && ResearchableSomewhere(ai, step.Target);
            case AiStepAction.Build:
                if ((uint)step.Target >= (uint)d.Buildings.Length) return false;
                (gold, wood) = (d.Buildings[step.Target].CostGold, d.Buildings[step.Target].CostWood);
                return !ai.CanAfford(gold, wood) && Fillable(ai, gold, wood) && PlaceableButForCost(ai, step.Target) && BuildCentre(ai, step.Target) >= 0;
            case AiStepAction.Expand:
                if (ai.TownHallType < 0) return false;
                (gold, wood) = (d.Buildings[ai.TownHallType].CostGold, d.Buildings[ai.TownHallType].CostWood);
                return !ai.CanAfford(gold, wood) && Fillable(ai, gold, wood) && PlaceableButForCost(ai, ai.TownHallType) && ExpansionMine(ai) >= 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// BUG-1023: whether gathering could fill the purse up to the cost: each kind the purse is short of has a node the
    /// gatherers could be sent to (<see cref="AiPlayer.KnowsNode"/>). A step short of a kind no gatherer can reach would bank
    /// its other kind for nothing (Hard seed 2: 150 gold held for 8 minutes for a building with no wood in reach).
    /// </summary>
    private static bool Fillable(AiPlayer ai, int gold, int wood) =>
        (ai.Gold >= gold || ai.KnowsNode(ResourceKind.Gold)) && (ai.Wood >= wood || ai.KnowsNode(ResourceKind.Wood));

    /// <summary>M5-4g: whether only the cost (of the player's whole bank, the view's) can stop a building of <paramref name="type"/> anywhere.</summary>
    private static bool PlaceableButForCost(AiPlayer ai, int type) =>
        ai.View.CanPlaceAnywhere(type, out PlacementError why) || why == PlacementError.CannotAfford;

    /// <summary>M5-4g: whether a finished own building may research <paramref name="tech"/> but for its cost or a full queue.</summary>
    private static bool ResearchableSomewhere(AiPlayer ai, int tech)
    {
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            if (ai.OwnBuildingSites[k]) continue;
            if (ai.View.CanResearch(ai.OwnBuildingSlots[k], tech, out ResearchError why)
                || why is ResearchError.CannotAfford or ResearchError.QueueFull) return true;
        }
        return false;
    }

    /// <summary>Sends the step's next command, if it can now; true when one was sent. M5-4g: a train step after the reserved one spends only above the reserve (<paramref name="aboveReserve"/>).</summary>
    private static bool Act(AiPlayer ai, in AiStep step, ref PendingOrder pending, bool aboveReserve)
    {
        switch (step.Action)
        {
            case AiStepAction.Train:
                return ai.TryTrain(step.Target, emptyQueueOnly: false, ref pending, aboveReserve);
            case AiStepAction.Research:
                return ai.TryResearch(step.Target, ref pending);
            case AiStepAction.Build:
                return ai.TryBuild(step.Target, BuildCentre(ai, step.Target), ref pending);
            default:
                int mine = ExpansionMine(ai);
                return mine >= 0 && ai.TryBuild(ai.TownHallType, mine, ref pending);
        }
    }

    /// <summary>Where a build step searches: round the player's Town Hall; a camp round the known tree nearest it.</summary>
    private static int BuildCentre(AiPlayer ai, int type)
    {
        int hall = HallCentre(ai);
        if (hall < 0 || type != ai.CampType) return hall;
        return NearestNode(ai, ResourceKind.Wood, hall, -1);
    }

    /// <summary>The cell the player's Town Hall is centred on (its first building when it has no hall); -1 with none.</summary>
    internal static int HallCentre(AiPlayer ai)
    {
        if (ai.TownHallCell >= 0)
        {
            BuildingDef d = ai.Data.Buildings[ai.TownHallType];
            return ai.CentreCellOf(d.FootprintWidth, d.FootprintHeight, ai.TownHallCell);
        }
        if (ai.OwnBuildingCount == 0) return -1;
        BuildingDef b = ai.Data.Buildings[ai.OwnBuildingTypes[0]];
        return ai.CentreCellOf(b.FootprintWidth, b.FootprintHeight, ai.OwnBuildingCells[0]);
    }

    /// <summary>
    /// The anchor cell of the known node of <paramref name="kind"/> nearest <paramref name="fromCell"/> (squared cell distance,
    /// slot order on ties); with <paramref name="clearOfHalls"/> &gt;= 0 only one with no own or seen Town Hall within that many
    /// cells; with <paramref name="skipDropped"/> not one the gatherers marked dropped (<c>Board.NodeDropped</c>, as
    /// <see cref="AiPlayer.KnowsNode"/> reads it). -1 for none.
    /// </summary>
    internal static int NearestNode(AiPlayer ai, ResourceKind kind, int fromCell, int clearOfHalls, bool skipDropped = false)
    {
        int w = ai.View.MapWidth, fx = fromCell % w, fy = fromCell / w;
        int best = -1;
        long bestDistance = long.MaxValue;
        int s = -1;
        while (ai.View.NextResource(ref s, out SeenResource node))
        {
            if (ai.Data.Resources[node.TypeId].Resource != kind) continue;
            if (skipDropped && ai.Board.NodeDropped[s] == node.Handle.Generation) continue;
            long dx = node.Cell % w - fx, dy = node.Cell / w - fy, d = dx * dx + dy * dy;
            if (d >= bestDistance) continue;
            if (clearOfHalls >= 0 && HallNear(ai, node.Cell, clearOfHalls)) continue;
            best = node.Cell;
            bestDistance = d;
        }
        return best;
    }

    /// <summary>
    /// The known gold mine an expand step builds by: the nearest the player's hall with no own or seen hall within reach and
    /// not marked dropped by its gatherers (BUG-1023 item 3: a mine they found gone was still reserved for and built by);
    /// -1 for none.
    /// </summary>
    internal static int ExpansionMine(AiPlayer ai)
    {
        if (ai.TownHallType < 0) return -1;
        int hall = HallCentre(ai);
        return hall < 0 ? -1 : NearestNode(ai, ResourceKind.Gold, hall, ai.Rules.ExpandReachCells, skipDropped: true);
    }

    /// <summary>Whether an own Town Hall, or an enemy one the player sees or remembers, stands within <paramref name="reach"/> cells (Chebyshev, anchor to anchor) of <paramref name="cell"/>.</summary>
    internal static bool HallNear(AiPlayer ai, int cell, int reach)
    {
        int w = ai.View.MapWidth, x = cell % w, y = cell / w;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
            if (ai.Data.Buildings[ai.OwnBuildingTypes[k]].Slot == BuildingSlot.TownHall && Near(ai.OwnBuildingCells[k], x, y, reach, w)) return true;
        int s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
            if (b.Owner != ai.Player && ai.Data.Buildings[b.TypeId].Slot == BuildingSlot.TownHall && Near(b.Cell, x, y, reach, w)) return true;
        s = -1;
        while (ai.View.NextGhost(ref s, out Vision.BuildingGhost g))
            if (ai.Data.Buildings[g.TypeId].Slot == BuildingSlot.TownHall && Near(g.Cell, x, y, reach, w)) return true;
        return false;
    }

    private static bool Near(int cell, int x, int y, int reach, int w) => Math.Abs(cell % w - x) <= reach && Math.Abs(cell / w - y) <= reach;
}
