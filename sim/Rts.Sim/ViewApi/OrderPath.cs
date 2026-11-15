using System;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;

namespace Rts.Sim.ViewApi;

/// <summary>
/// M6-V22: a unit's order path for the queued-orders drawing: where its current order is taking it, then each shift-queued
/// entry in order (the unit store's <c>QueueCount</c> / <c>QueueKind</c> / <c>QueuePosition</c> / <c>QueueTypeId</c>, an
/// Attack's target as <c>UnitStore.QueuedTarget</c> decodes it). Read only: no tick, state or hash change, no allocation
/// after construction. Fog-agnostic: the caller decides what its player may see.
/// </summary>
/// <remarks>
/// Built once per match from the unit store's arrays (fixed for a match: the store never grows), the buildings, the
/// grid and the data; the view passes them in (ViewApi never names the world or the unit store).
/// </remarks>
public sealed class OrderPath
{
    /// <summary>The most points a unit's path has: its current order and a full queue.</summary>
    public const int MaxPoints = OrderConstants.QueueCapacity + 1;

    private readonly GameData _data;
    private readonly NavGrid _grid;
    private readonly BuildingStore _buildings;
    private readonly bool[] _alive;
    private readonly int[] _generation, _typeId, _castAbility, _queueCount, _queueTypeId;
    private readonly Vector2[] _position, _goal, _anchor, _castPoint, _gatherSite, _queuePosition;
    private readonly UnitState[] _state;
    private readonly CombatMode[] _mode;
    private readonly EntityHandle[] _target, _buildTarget, _gatherNode;
    private readonly bool[] _targetIsBuilding;
    private readonly CommandKind[] _queueKind;

    /// <summary>Reads the unit store's arrays (pass the store's own; they are only read), the buildings, the grid and the data.</summary>
    public OrderPath(GameData data, NavGrid grid, BuildingStore buildings,
        bool[] alive, int[] generation, int[] typeId, Vector2[] position, UnitState[] state, Vector2[] goal,
        CombatMode[] mode, Vector2[] anchorPosition, EntityHandle[] target, bool[] targetIsBuilding,
        int[] castAbility, Vector2[] castPoint, EntityHandle[] buildTarget, EntityHandle[] gatherNode, Vector2[] gatherSite,
        int[] queueCount, CommandKind[] queueKind, Vector2[] queuePosition, int[] queueTypeId)
    {
        _data = data;
        _grid = grid;
        _buildings = buildings;
        _alive = alive;
        _generation = generation;
        _typeId = typeId;
        _position = position;
        _state = state;
        _goal = goal;
        _mode = mode;
        _anchor = anchorPosition;
        _target = target;
        _targetIsBuilding = targetIsBuilding;
        _castAbility = castAbility;
        _castPoint = castPoint;
        _buildTarget = buildTarget;
        _gatherNode = gatherNode;
        _gatherSite = gatherSite;
        _queueCount = queueCount;
        _queueKind = queueKind;
        _queuePosition = queuePosition;
        _queueTypeId = queueTypeId;
    }

    /// <summary>Whether <paramref name="unit"/> is a live unit (its slot alive with this generation).</summary>
    public bool IsAlive(EntityHandle unit) =>
        (uint)unit.Index < (uint)_alive.Length && _alive[unit.Index] && _generation[unit.Index] == unit.Generation;

    /// <summary>
    /// Fills <paramref name="into"/> with <paramref name="unit"/>'s order points, current order first (when it has one:
    /// a cast, an explicit Attack, an attack-move leg, a build or repair, a gather loop, or a plain walk), then the queue,
    /// head first; returns how many were written (at most the span's length). 0 for a dead or stale handle.
    /// </summary>
    public int Fill(EntityHandle unit, Span<OrderPoint> into)
    {
        if (!IsAlive(unit) || into.Length == 0) return 0;
        int i = unit.Index, n = 0;
        if (Current(i, out OrderPoint current)) into[n++] = current;
        int head = i * OrderConstants.QueueCapacity, count = _queueCount[i];
        for (int k = 0; k < count && n < into.Length; k++) into[n++] = Queued(i, head + k);
        return n;
    }

    /// <summary>
    /// The first Shift-placed building's rule (M6-V22, Producer decision): true when worker <paramref name="slot"/>'s Build
    /// should be queued behind what it is doing, an order that ends (a walk, an attack or a fight, a build, a repair, a cast,
    /// or orders already queued); false when it should replace it, a gather loop (it never ends, so a queued Build would
    /// never start: the sim starts a queued order only when the unit is Idle) or nothing at all. False for a dead slot.
    /// </summary>
    public bool QueuesBehind(int slot)
    {
        if ((uint)slot >= (uint)_alive.Length || !_alive[slot]) return false;
        if (OnLoop(slot)) return false;
        if (_queueCount[slot] > 0) return true;
        if (_buildTarget[slot].Generation != 0 || _target[slot].Generation != 0 || _castAbility[slot] >= 0) return true;
        return _state[slot] == UnitState.Moving;
    }

    // EconomySystem.OnLoop's test: a gather order holds a node handle (real handles never have generation 0).
    private bool OnLoop(int i) => _gatherNode[i].Generation != 0;

    // The current order with a place, if any. A retaliating or leashed unit (Mode Retaliate / Returning) is not on an order.
    private bool Current(int i, out OrderPoint point)
    {
        point = new OrderPoint { IsCurrent = true, HasPlace = true };
        if (_castAbility[i] >= 0)
        {
            point.Kind = CommandKind.UseAbility;
            point.Point = _castPoint[i];
            point.HasPlace = !NoTarget(_typeId[i], _castAbility[i]);
            return true;
        }
        if (_target[i].Generation != 0 && _mode[i] == CombatMode.Ordered)
        {
            point.Kind = CommandKind.Attack;
            SetTarget(ref point, _target[i], _targetIsBuilding[i]);
            return true;
        }
        if (_mode[i] == CombatMode.AttackMove)
        {
            point.Kind = CommandKind.AttackMove;
            point.Point = _anchor[i];
            return true;
        }
        if (_buildTarget[i].Generation != 0)
        {
            EntityHandle site = _buildTarget[i];
            if (!_buildings.IsAlive(site)) return false;
            point.Kind = _buildings.UnderConstruction[site.Index] ? CommandKind.Build : CommandKind.Repair;
            point.Point = StartBase.FootprintCenter(_grid, _data.Buildings[_buildings.TypeId[site.Index]], _buildings.Cell[site.Index]);
            return true;
        }
        if (OnLoop(i))
        {
            point.Kind = CommandKind.Gather;
            point.Point = _gatherSite[i];
            return true;
        }
        if (_state[i] == UnitState.Moving && _mode[i] == CombatMode.None)
        {
            point.Kind = CommandKind.Move;
            point.Point = _goal[i];
            return true;
        }
        return false;
    }

    private OrderPoint Queued(int i, int entry)
    {
        CommandKind kind = _queueKind[entry];
        var point = new OrderPoint { Kind = kind, HasPlace = true, Point = _queuePosition[entry] };
        switch (kind)
        {
            case CommandKind.Attack:
                // QueuePosition holds the target's slot (x) and generation (y) here, never a place (UnitStore.QueuedTarget).
                var target = new EntityHandle((int)_queuePosition[entry].X, (int)_queuePosition[entry].Y);
                point.Point = Vector2.Zero;
                SetTarget(ref point, target, _queueTypeId[entry] == 1);
                break;
            case CommandKind.Stop:
            case CommandKind.HoldPosition:
                point.HasPlace = false;
                point.Point = Vector2.Zero;
                break;
            case CommandKind.UseAbility:
                point.HasPlace = !NoTarget(_typeId[i], _queueTypeId[entry]);
                break;
            case CommandKind.Build:
                // The site goes where the point's cell anchors the footprint (ConstructionSystem.StartBuild): mark its centre.
                int type = _queueTypeId[entry];
                if ((uint)type < (uint)_data.Buildings.Length && _grid.WorldToCell(point.Point, out int x, out int y))
                    point.Point = StartBase.FootprintCenter(_grid, _data.Buildings[type], y * _grid.Width + x);
                break;
        }
        return point;
    }

    private void SetTarget(ref OrderPoint point, EntityHandle target, bool isBuilding)
    {
        point.Target = target;
        point.TargetIsBuilding = isBuilding;
        if (isBuilding)
        {
            point.TargetLive = _buildings.IsAlive(target);
            if (point.TargetLive)
                point.Point = StartBase.FootprintCenter(_grid, _data.Buildings[_buildings.TypeId[target.Index]], _buildings.Cell[target.Index]);
        }
        else
        {
            point.TargetLive = IsAlive(target);
            if (point.TargetLive) point.Point = _position[target.Index];
        }
    }

    // Whether ability index `index` of unit type `type` (UnitDef.Abilities) takes no target (a self aura or a summon).
    private bool NoTarget(int type, int index)
    {
        UnitDef def = _data.Units[type];
        if ((uint)index >= (uint)def.Abilities.Length) return false;
        return AbilityCaster.IsNoTarget(_data.Abilities[def.Abilities[index]].Kind);
    }
}
