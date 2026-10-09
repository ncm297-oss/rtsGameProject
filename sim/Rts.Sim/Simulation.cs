using System;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.Vision;

namespace Rts.Sim;

/// <summary>Owns a <see cref="World"/> and advances it one fixed 50 ms tick at a time.</summary>
/// <remarks>
/// The only entry points that change state are <see cref="Enqueue"/> (deferred) and
/// <see cref="Tick"/>. Phase order follows docs/03 "Tick model"; phases after "apply commands"
/// are added by later milestones.
/// </remarks>
public sealed class Simulation
{
    private readonly CommandQueue _commands;
    private readonly int[] _nextSequence;
    private ReplayRecorder? _recorder;

    /// <summary>Creates a simulation for the given match setup.</summary>
    public Simulation(SimConfig config) : this(config, null)
    {
    }

    /// <summary>Test seam: a simulation on a hand-made map instead of the generated one.</summary>
    internal Simulation(SimConfig config, Heightmap? map)
    {
        World = new World(config, map);
        _commands = new CommandQueue(config.CommandCapacity);
        _nextSequence = new int[config.PlayerCount];
    }

    /// <summary>The simulated state. Read it; never write it from outside the sim.</summary>
    public World World { get; }

    /// <summary>Number of the next tick to run; 0 before the first <see cref="Tick"/>.</summary>
    public int TickNumber => World.TickNumber;

    /// <summary>Number of commands waiting to apply.</summary>
    public int PendingCommandCount => _commands.Count;

    /// <summary>Queues a command for tick <c>TickNumber + 1</c>, stamped with the player's next sequence number.</summary>
    /// <remarks>
    /// Always one tick ahead, whether called between ticks (input) or during one (AI think), so a
    /// command never changes the tick that is already running. A command for an unknown player, of an
    /// undefined kind, or with flag bits its kind doesn't take (<see cref="Command.IsWellFormed"/>) is
    /// refused with an exception and changes nothing, so everything accepted can be recorded and read
    /// back (BUG-0054). A well-formed command with a bad payload (a NaN target, a dead unit) is
    /// accepted and dropped when it applies.
    /// </remarks>
    public void Enqueue(Command command)
    {
        if ((uint)command.Player >= (uint)_nextSequence.Length)
            throw new ArgumentOutOfRangeException(nameof(command), $"Unknown player {command.Player}.");
        if (!command.IsWellFormed())
            throw new ArgumentException($"Malformed command: kind {(int)command.Kind}, flags {command.Flags}.", nameof(command));
        command.Tick = World.TickNumber + 1;
        command.Sequence = _nextSequence[command.Player];
        _commands.Add(in command); // throws when full; bump the counter only once accepted (BUG-0004)
        _nextSequence[command.Player]++;
        _recorder?.OnEnqueued(in command);
    }

    /// <summary>Runs one tick: applies this tick's commands in (player, sequence) order, rebuilds the spatial hash, trains and spawns units, runs gather loops, then builders and repairers, starts queued orders, acquires targets, moves units, swings and applies damage and death, then bumps <see cref="TickNumber"/>.</summary>
    public void Tick()
    {
        World.Units.SnapshotPrevPositions();
        World.ClearDeaths(); // the last tick's death events have been read by now
        World.ClearImpacts(); // and its projectile landings (M4-2b)
        World.ClearAbilityEvents(); // and its cast starts and resolves (M4-4a)

        // M4-3a: the initial fog stamp, before the first tick's commands, so units already in the world (placed before
        // the first tick) are seen from tick 0. The first phase-12 update is tick 1's, the tick the start commands apply in.
        if (World.TickNumber == 0) World.Fog.Update();

        // Phase 1: apply commands.
        _commands.Sort();
        int due = _commands.CountDue(World.TickNumber);
        for (int i = 0; i < due; i++)
            Apply(in _commands[i]);
        _commands.RemoveFront(due);

        // Neighbour index for every later phase; right after commands so this tick's spawns are queryable.
        World.Spatial.Rebuild(World.Units);

        // Phase 3: production (M3-4): training timers, spawns, rally orders (their walks move in phase 9).
        ProductionSystem.Run(World);

        // Phase 4: economy (M3-2): gather loops and drop-offs; their walks start here and move in phase 9.
        EconomySystem.Run(World);
        // M3-3: builders and repairers, after the gatherers.
        ConstructionSystem.Run(World);

        // Phase 5: statuses count down, damage over time lands, the expired end (M4-4a).
        StatusSystem.Run(World);

        // Phase 6: casters walk into range, cast timers count down, finished casts resolve (M4-4a).
        AbilitySystem.Run(World);

        // Phase 7: Idle units start their next shift-queued order, then target acquisition (M4-1).
        OrderSystem.Run(World);
        if (World.CombatEnabled)
        {
            CombatSystem.Acquire(World);
            TowerSystem.Acquire(World); // M4-3b: buildings that shoot pick a unit
        }

        // Phases 8-9: flow fields (fetched or built on demand) and movement.
        MovementSystem.Run(World);

        // Phase 10: projectiles in flight move a step (M4-2b), then swings, wind-ups and cooldowns (M4-1); hits queue for
        // phase 11 and shots fly from the next tick.
        // Phase 11: damage and death (M4-1), then the projectiles that arrived land (M4-2b). All off with SimConfig.Combat false.
        if (World.CombatEnabled)
        {
            ProjectileSystem.Fly(World);
            CombatSystem.Attack(World);
            TowerSystem.Attack(World); // M4-3b: towers wind up and fire, after the units
            CombatSystem.Resolve(World);
        }

        // Phase 12: fog of war (M4-3a), every 4 ticks (1, 5, 9, ...) for every player.
        VisionSystem.Run(World);

        // Phase 13: cleanup.
        World.TickNumber++;

        // Phase 14: replay checkpoint hash, when a recorder is attached and the tick is due.
        _recorder?.OnTicked();
    }

    /// <summary>Connects the one recorder this sim reports accepted commands and finished ticks to.</summary>
    internal void AttachRecorder(ReplayRecorder recorder)
    {
        if (_recorder != null) throw new InvalidOperationException("A replay recorder is already attached.");
        _recorder = recorder;
    }

    /// <summary>64-bit FNV-1a hash of all gameplay state: tick, units (Hold, order queues, gather loops and cargo, combat state included), RNG streams, flow-field cache metadata, nav grid versions (and the movement pass's last seen block version), resource nodes, buildings, projectiles in flight (production queues, research items and rally points included), the fog's explored and visible bits and high-ground reveals (M4-3a; visible bits BUG-0215), player totals, researched techs, kills and losses, and pending commands.</summary>
    /// <remarks>
    /// Derived state is left out: the spatial hash (rebuilt from the units every tick), the fog's byte map and update versions (M4-3a: copies of its hashed bits),
    /// Speed/Radius (they follow from TypeId), and population (M3-4: <see cref="World.HalfPop"/> follows from the live
    /// units and the started production items, <see cref="World.HalfPopCap"/> from the finished buildings). The flow-field cache's keys, versions and LRU stamps
    /// are in, because they decide which units wait under the build cap (BUG-0021); the fields'
    /// contents are not, since they follow from the grid and the key.
    /// </remarks>
    public ulong StateHash()
    {
        var h = new StateHasher();
        h.Add(World.TickNumber);

        UnitStore u = World.Units;
        h.Add(u.Capacity);
        for (int i = 0; i < u.Capacity; i++)
        {
            // Generations matter even for dead slots: they decide future handles.
            h.Add(u.Generation[i]);
            h.Add(u.Alive[i]);
            if (!u.Alive[i]) continue;
            h.Add(u.Position[i]);
            h.Add(u.PrevPosition[i]);
            h.Add(u.Velocity[i]);
            h.Add(u.Facing[i]);
            h.Add(u.Owner[i]);
            h.Add(u.TypeId[i]);
            // Orders (M1-7) ride in the high half of the state word, which is zero for a unit with
            // no Hold and an empty queue, so such a unit hashes exactly as before the queue existed.
            // M4-1: bit 17 flags combat state, added after the economy words. M4-4a: bit 18 ability state, after the combat words.
            bool combat = HasCombat(World, i);
            bool ability = HasAbilityState(u, i);
            h.Add((ulong)(byte)u.State[i] | ((ulong)(OrderBits(u, i) | (combat ? 1u << 17 : 0u) | (ability ? 1u << 18 : 0u)) << 32));
            h.Add(u.Goal[i]);
            h.Add(u.GoalCell[i]);
            h.Add(u.OrderTick[i]);
            h.Add(u.StuckTicks[i]);
            h.Add(u.BestRemaining[i]);
            h.Add(u.WalkBack[i]);
            AddOrdersToHash(ref h, u, i);
            if (HasEconomy(u, i))
            {
                h.Add(u.GatherNode[i].Index);
                h.Add(u.GatherNode[i].Generation);
                h.Add(u.GatherSite[i]);
                h.Add(u.GatherProgress[i]);
                h.Add(u.Cargo[i]);
                h.Add((int)u.CargoKind[i]);
                h.Add(u.BuildTarget[i].Index);
                h.Add(u.BuildTarget[i].Generation);
            }
            if (combat) AddCombatToHash(ref h, u, i);
            if (ability) AddAbilityToHash(ref h, u, i);
        }
        h.Add(u.FreeCount);
        for (int i = 0; i < u.FreeCount; i++)
            h.Add(u.FreeListAt(i));

        for (int s = 0; s < World.RngCount; s++)
        {
            h.Add(World.Rng(s).State);
            h.Add(World.Rng(s).Increment);
        }

        World.FlowFields.AddToHash(ref h);

        // M3-1: resource nodes and the grid version (passability changes with them; it used to be unhashed).
        h.Add(World.NavGrid.Version);
        // M3-2b: the closing-change counter and the movement pass's last view of it (they decide which fields are usable and when progress marks reset).
        h.Add(World.NavGrid.BlockVersion);
        h.Add(World.SeenBlockVersion);
        World.Resources.AddToHash(ref h);

        // M3-2: buildings and the players' totals.
        World.Buildings.AddToHash(ref h);
        // M4-2b: projectiles in flight; an empty store adds nothing, so a match without a shot hashes as before.
        World.Projectiles.AddToHash(ref h);
        // M4-3a: the fog's explored and visible bits (BUG-0215: the visible bits are state) and the high-ground reveals in force.
        World.Fog.AddToHash(ref h, World.TickNumber);
        for (int p = 0; p < World.Gold.Length; p++)
        {
            // M3-5: a player with any researched tech sets the high half of its gold word and its tech words follow;
            // without techs it hashes exactly as before.
            bool techs = World.Techs.Any(p);
            // M4-1: likewise bit 33 for a player with kills or losses, followed by the two counts.
            bool deaths = World.Kills[p] != 0 || World.Losses[p] != 0;
            h.Add((ulong)(uint)World.Gold[p] | (techs ? 1UL << 32 : 0UL) | (deaths ? 1UL << 33 : 0UL));
            h.Add(World.Wood[p]);
            if (techs) World.Techs.AddToHash(ref h, p);
            if (deaths)
            {
                h.Add(World.Kills[p]);
                h.Add(World.Losses[p]);
            }
        }

        for (int p = 0; p < _nextSequence.Length; p++)
            h.Add(_nextSequence[p]);
        h.Add(_commands.Count);
        for (int i = 0; i < _commands.Count; i++)
        {
            ref readonly Command c = ref _commands[i];
            // Flags 0: as before M1-7. M4-2a: bit 63 flags an attack target, whose words follow.
            bool target = c.Target != default || c.TargetIsBuilding;
            h.Add((ulong)(uint)c.Kind | ((ulong)(uint)c.Flags << 32) | (target ? 1UL << 63 : 0UL));
            h.Add(c.Player);
            h.Add(c.Tick);
            h.Add(c.Sequence);
            h.Add(c.TypeId);
            h.Add(c.Position);
            h.Add(c.Unit.Index);
            h.Add(c.Unit.Generation);
            if (target)
            {
                h.Add(c.Target.Index);
                h.Add((ulong)(uint)c.Target.Generation | (c.TargetIsBuilding ? 1UL << 32 : 0UL));
            }
        }
        return h.Value;
    }

    /// <summary>
    /// Unit <paramref name="i"/>'s order summary for <see cref="StateHash"/>: bit 0 Hold, bit 1 a
    /// non-zero queue count, then one bit per queue entry that isn't default, and bit 16 for gather-loop or
    /// cargo state (M3-2). Zero for a unit with
    /// no orders; <see cref="AddOrdersToHash"/> then adds nothing. A queued Attack's target (M4-2a) lives in the entry's
    /// position and type id (<see cref="UnitStore.QueuedTarget"/>), so it is hashed with them.
    /// </summary>
    private static uint OrderBits(UnitStore u, int i)
    {
        uint bits = u.Hold[i] ? 1u : 0u;
        if (u.QueueCount[i] != 0) bits |= 2u;
        int head = i * OrderConstants.QueueCapacity;
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
            if (u.QueueKind[head + k] != CommandKind.Noop || u.QueuePosition[head + k] != Vector2.Zero || u.QueueTypeId[head + k] != 0) bits |= 4u << k;
        if (HasEconomy(u, i)) bits |= 1u << 16;
        return bits;
    }

    /// <summary>
    /// True when any combat field (M4-1) of unit <paramref name="i"/> isn't at its spawn value: hit points below (or
    /// above) the type's, a target, a cooldown, a wind-up, a last attacker, an anchor, a combat mode, chase memory (BUG-0137), or a pending re-pick (BUG-0154, never set between ticks). Flagged by bit 17
    /// of the order word, then hashed, so a unit that never fought hashes exactly as before M4-1.
    /// </summary>
    private static bool HasCombat(World world, int i)
    {
        UnitStore u = world.Units;
        int fullHp = (uint)u.TypeId[i] < (uint)world.Data.Units.Length ? world.Data.Units[u.TypeId[i]].Hp : 0;
        return u.Hp[i] != fullHp || u.Target[i] != default || u.TargetIsBuilding[i] || u.CooldownTicks[i] != 0 || u.WindupTicks[i] != 0
            || u.LastAttacker[i] != default || u.AnchorPosition[i] != Vector2.Zero || u.Mode[i] != CombatMode.None
            || u.ChaseBest[i] != 0f || u.ChaseStall[i] != 0 || u.Ignored[i] != default || u.IgnoredIsBuilding[i] || u.GiveUps[i] != 0
            || u.Repick[i] || u.ChasePrev[i] != default || u.ChasePrevIsBuilding[i];
    }

    /// <summary>Unit <paramref name="i"/>'s combat fields, as flagged by bit 17 of <see cref="OrderBits"/>.</summary>
    private static void AddCombatToHash(ref StateHasher h, UnitStore u, int i)
    {
        h.Add(u.Hp[i]);
        h.Add(u.Target[i].Index);
        h.Add((ulong)(uint)u.Target[i].Generation | (u.TargetIsBuilding[i] ? 1UL << 32 : 0UL));
        h.Add(u.CooldownTicks[i]);
        h.Add(u.WindupTicks[i]);
        h.Add(u.LastAttacker[i].Index);
        h.Add(u.LastAttacker[i].Generation);
        h.Add(u.AnchorPosition[i]);
        // BUG-0154's re-pick flag rides above the mode's byte: it is cleared within the tick it is set, so between ticks
        // the word is the mode alone and the hash stream is what it was before the flag.
        h.Add((int)u.Mode[i] | (u.Repick[i] ? 1 << 8 : 0));
        // BUG-0137's chase memory.
        h.Add(u.ChaseBest[i]);
        h.Add(u.ChaseStall[i]);
        h.Add(u.Ignored[i].Index);
        h.Add((ulong)(uint)u.Ignored[i].Generation | (u.IgnoredIsBuilding[i] ? 1UL << 32 : 0UL));
        h.Add(u.GiveUps[i]);
        // BUG-0149: the target held before this one (a switch back keeps the stall count).
        h.Add(u.ChasePrev[i].Index);
        h.Add((ulong)(uint)u.ChasePrev[i].Generation | (u.ChasePrevIsBuilding[i] ? 1UL << 32 : 0UL));
    }

    /// <summary>
    /// True when any ability field (M4-4a) of unit <paramref name="i"/> isn't at its spawn value: a cast or walk to one,
    /// a cooldown ever started, or a status. Flagged by bit 18 of the order word, so a unit that never cast nor was hit by
    /// an ability hashes exactly as before M4-4a.
    /// </summary>
    private static bool HasAbilityState(UnitStore u, int i)
    {
        if (u.CastAbility[i] != -1 || u.CastTicks[i] != 0 || u.CastPoint[i] != Vector2.Zero || u.Statuses.Count[i] != 0) return true;
        int head = i * Data.DataLimits.MaxUnitAbilities;
        for (int k = 0; k < Data.DataLimits.MaxUnitAbilities; k++)
            if (u.AbilityReadyTick[head + k] != 0) return true;
        return false;
    }

    /// <summary>Unit <paramref name="i"/>'s ability fields, as flagged by bit 18: the cast, every cooldown's ready tick, and every status field.</summary>
    private static void AddAbilityToHash(ref StateHasher h, UnitStore u, int i)
    {
        h.Add(u.CastAbility[i]);
        h.Add(u.CastTicks[i]);
        h.Add(u.CastPoint[i]);
        int head = i * Data.DataLimits.MaxUnitAbilities;
        for (int k = 0; k < Data.DataLimits.MaxUnitAbilities; k++)
            h.Add(u.AbilityReadyTick[head + k]);
        u.Statuses.AddToHash(ref h, i);
    }

    /// <summary>True when any gather-loop or cargo field (M3-2) or the build target (M3-3) of unit <paramref name="i"/> isn't default; flagged in <see cref="OrderBits"/>, then hashed.</summary>
    private static bool HasEconomy(UnitStore u, int i) =>
        u.BuildTarget[i] != default || u.GatherNode[i] != default || u.GatherSite[i] != Vector2.Zero || u.GatherProgress[i] != 0f || u.Cargo[i] != 0 || u.CargoKind[i] != default;

    /// <summary>The queue count and every non-default queue entry of unit <paramref name="i"/>, as flagged by <see cref="OrderBits"/> (already hashed), so the stream stays unambiguous.</summary>
    private static void AddOrdersToHash(ref StateHasher h, UnitStore u, int i)
    {
        if (u.QueueCount[i] != 0) h.Add(u.QueueCount[i]);
        int head = i * OrderConstants.QueueCapacity;
        for (int k = 0; k < OrderConstants.QueueCapacity; k++)
        {
            if (u.QueueKind[head + k] == CommandKind.Noop && u.QueuePosition[head + k] == Vector2.Zero && u.QueueTypeId[head + k] == 0) continue;
            // A queued Build's type id (M3-3) rides in the high half of the kind word: zero for every other kind, so they hash as before.
            // A queued Attack's building flag does too, and its target's slot and generation are the position (M4-2a).
            h.Add((ulong)(uint)u.QueueKind[head + k] | ((ulong)(uint)u.QueueTypeId[head + k] << 32));
            h.Add(u.QueuePosition[head + k]);
        }
    }

    private void Apply(in Command command)
    {
        // Malformed input (e.g. a NaN ray cast) is dropped, like a spawn into a full store (BUG-0006).
        if (!command.IsValid()) return;
        switch (command.Kind)
        {
            case CommandKind.Noop:
                break;
            case CommandKind.SpawnUnit:
                ApplySpawn(in command);
                break;
            case CommandKind.Move:
            case CommandKind.Stop:
            case CommandKind.HoldPosition:
            case CommandKind.AttackMove:
            case CommandKind.Gather:
            case CommandKind.Build:
            case CommandKind.Repair:
            case CommandKind.Attack:
            case CommandKind.UseAbility:
                OrderSystem.Apply(World, in command);
                break;
            case CommandKind.Cancel:
                ConstructionSystem.ApplyCancel(World, in command);
                break;
            case CommandKind.SpawnBuilding:
                EconomySystem.ApplySpawnBuilding(World, in command);
                break;
            case CommandKind.Train:
                ProductionSystem.ApplyTrain(World, in command);
                break;
            case CommandKind.CancelTrain:
                ProductionSystem.ApplyCancelTrain(World, in command);
                break;
            case CommandKind.SetRally:
                ProductionSystem.ApplySetRally(World, in command);
                break;
            case CommandKind.ClearRally:
                ProductionSystem.ApplyClearRally(World, in command);
                break;
            case CommandKind.Research:
                ProductionSystem.ApplyResearch(World, in command);
                break;
        }
    }

    private void ApplySpawn(in Command command)
    {
        // An unknown type or a full store drops the spawn: a command must never crash the sim. A dev spawn ignores the
        // population cap but counts toward the population (M3-4).
        GameData data = World.Data;
        if ((uint)command.TypeId >= (uint)data.Units.Length) return;
        World.Units.TrySpawn(command.Player, command.TypeId, data.Units[command.TypeId], command.Position, out _);
    }
}
