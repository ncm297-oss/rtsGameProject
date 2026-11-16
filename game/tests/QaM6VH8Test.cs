using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA (M6-VH8): attacks on the queued-order rings, the zoom sizing, the full-queue Build refusal and the overlay line, on
/// the real Match scene and shipped data. Rows:
/// (R) rings: a Stop queued after a Stop nests one ring out; four no-place entries stop at <see cref="OrderPathView.MaxRingLevel"/>;
///     a Hold as the only queued order is a ring with no disc in it; two units with the same Stop at one point share a ring;
///     two units with a Stop and a Hold at one point (printed: they are drawn at the same radius);
/// (F) full queue: 30 workers, 15 with 8 queued and 15 idle, a queued Build reaches only the 15; all 30 full: nothing sent,
///     one Refused sound, the refusal counted; a second queued Build before the tick for a worker at 7 (printed);
/// (Z) zoom changed every frame while a queue is drawn: the drawn factors follow the camera; windowed, the ring and disc
///     instance transforms read back at the expected radius;
/// (O) overlay line: capsule types (models off) unchanged and the cargo marker where it was; per model type the gap over
///     the highest bone of the clips its state map plays (printed); a hurt giant on the steepest ground near the base has
///     its bar at its line over the ground point.
/// Windowed with <c>-- --shots &lt;dir&gt;</c>: hurt Teblor types beside a Heavy Infantry in every clip at its highest pose
/// (zoom 20), a hurt giant on a slope, a hurt giant behind the hall.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH8Test.tscn</c>; prints "QA M6-VH8 TEST PASS".</remarks>
public partial class QaM6VH8Test : Node
{
    private const string Tag = "QA M6-VH8 TEST";
    private const float Tolerance = 0.1f;
    private static readonly PropertyInfo LedgerProperty = typeof(World).GetProperty("Ledger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private OrderPathView _view = null!;
    private RtsCamera _camera = null!;
    private Sfx _sfx = null!;
    private int _hi, _laborer, _house;
    private NVector2 _hall, _away, _perp, _t;
    private string? _shots;
    private bool Windowed => DisplayServer.GetName() != "headless";

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            CapsuleTypes();
            UnitViews.UseModels = true;
            await StartMatch();
            Stage();
            await Rings();
            await FullQueues();
            await ZoomChurn();
            await Overlays();
            await EndMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (O) capsule types with models off (--placeholder-art) ----

    private void CapsuleTypes()
    {
        bool was = UnitViews.UseModels;
        UnitViews.UseModels = false;
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        int changed = 0;
        for (int t = 0; t < _data.Units.Length; t++)
            if (views.DrawsModel(t) || views.OverlayLine(t) != UnitViews.DrawnHeight(_data.Units[t])) changed++;
        Check(changed == 0, $"(O) placeholder art: {changed} of {_data.Units.Length} types not on their capsule's top");
        GD.Print($"(O) placeholder art: {_data.Units.Length - changed} of {_data.Units.Length} types' overlay line is the capsule's top");
        views.Free();
        UnitViews.UseModels = was;
    }

    // ---- (R) rings ----

    private async Task Rings()
    {
        // A Stop after a Stop, and four no-place entries in a row.
        EntityHandle a = Spawn(0, _hi, Passable(_t));
        EntityHandle b = Spawn(0, _hi, Passable(_t + _perp * 2f));
        NVector2 goal = Passable(_t + _away * 20f), p = OpenGround(_t + _away * 12f + _perp * 8f);
        _sim.Enqueue(Command.Move(0, a, goal));
        _sim.Enqueue(Command.Move(0, a, p, queued: true));
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, a, queued: true));
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        Apply();
        _sel.SelectOnly(a);
        await Frames();
        _view.Sync(W, 1f);
        var rings = new List<(CommandKind Kind, int Level)>();
        for (int k = 0; k < _view.MarkerCount; k++)
            if (_view.MarkerNoPlace(k) && Near(_view.MarkerPoint(k), p)) rings.Add((_view.MarkerKind(k), _view.MarkerLevel(k)));
        string list = string.Join(", ", rings.Select(r => $"{r.Kind}@{r.Level}"));
        GD.Print($"(R) Move, Stop, Stop, Hold, Stop queued at one point: rings [{list}], {_view.RingsNode.Multimesh.VisibleInstanceCount} ring instances");
        Check(rings.Count == 4 && rings[0] == (CommandKind.Stop, 0) && rings[1] == (CommandKind.Stop, 1) && rings[2] == (CommandKind.HoldPosition, 2)
              && rings[3] == (CommandKind.Stop, 2), $"(R) Stop after Stop: rings [{list}], want Stop@0, Stop@1, Hold@2, Stop@2");
        for (int k = 0; k < _view.MarkerCount; k++)
            Check(!_view.MarkerNoPlace(k) || _view.MarkerLevel(k) <= OrderPathView.MaxRingLevel, $"(R) ring {k} at level {_view.MarkerLevel(k)}");

        // A Hold as the only queued order: a ring round the walk's end, and no disc there (the current order has none).
        _sim.Enqueue(Command.Move(0, b, p));
        _sim.Enqueue(Command.HoldPosition(0, b, queued: true));
        Apply();
        _sel.SelectOnly(b);
        await Frames();
        _view.Sync(W, 1f);
        Check(_view.MarkerCount == 1 && _view.MarkerNoPlace(0) && _view.MarkerKind(0) == CommandKind.HoldPosition && Near(_view.MarkerPoint(0), p)
              && _view.MarkersNode.Multimesh.VisibleInstanceCount == 0 && _view.RingsNode.Multimesh.VisibleInstanceCount == 1,
            $"(R) Hold only: {_view.MarkerCount} markers, {_view.MarkersNode.Multimesh.VisibleInstanceCount} discs, {_view.RingsNode.Multimesh.VisibleInstanceCount} rings");
        GD.Print($"(R) Hold as the only queued order: one ring at the walk's end, no disc ({_view.MarkerDrawnRadius(0):0.00} m)");

        // Two units, one Stop each at one point: one shared ring. A Stop and a Hold at one point: both level 0.
        foreach (EntityHandle h in new[] { a, b }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
        NVector2 q = OpenGround(_t + _away * 6f - _perp * 10f);
        foreach (EntityHandle h in new[] { a, b })
        {
            _sim.Enqueue(Command.Move(0, h, goal));
            _sim.Enqueue(Command.Move(0, h, q, queued: true));
            _sim.Enqueue(Command.Stop(0, h, queued: true));
        }
        Apply();
        _sel.Selection.Clear();
        _sel.Selection.Add(a);
        _sel.Selection.Add(b);
        await Frames();
        _view.Sync(W, 1f);
        Check(_view.RingsNode.Multimesh.VisibleInstanceCount == 1 && _view.MarkersNode.Multimesh.VisibleInstanceCount == 1,
            $"(R) two units, same Stop: {_view.RingsNode.Multimesh.VisibleInstanceCount} rings, {_view.MarkersNode.Multimesh.VisibleInstanceCount} discs, want 1 and 1");
        _sim.Enqueue(Command.Move(0, b, goal));
        _sim.Enqueue(Command.Move(0, b, q, queued: true));
        _sim.Enqueue(Command.HoldPosition(0, b, queued: true));
        Apply();
        await Frames();
        _view.Sync(W, 1f);
        var shared = new List<string>();
        for (int k = 0; k < _view.MarkerCount; k++)
            if (_view.MarkerNoPlace(k)) shared.Add($"{_view.MarkerKind(k)} level {_view.MarkerLevel(k)} radius {_view.MarkerDrawnRadius(k):0.00} m");
        GD.Print($"(R) a Stop (unit a) and a Hold (unit b) at one point: [{string.Join("; ", shared)}]");
        if (Windowed && _shots != null)
        {
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(q.X, q.Y + 2f);
            await Shot("rings-stop-and-hold-shared-point");
            _camera.SetFocus(p.X, p.Y + 2f);
            _sel.SelectOnly(a);
            _sim.Enqueue(Command.Move(0, a, goal));
            _sim.Enqueue(Command.Move(0, a, p, queued: true));
            _sim.Enqueue(Command.Stop(0, a, queued: true));
            _sim.Enqueue(Command.Stop(0, a, queued: true));
            _sim.Enqueue(Command.HoldPosition(0, a, queued: true));
            Apply();
            await Shot("rings-stop-stop-hold");
        }
        _sel.Selection.Clear();
        foreach (EntityHandle h in new[] { a, b }) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // ---- (F) full queues with 30 workers ----

    private async Task FullQueues()
    {
        var workers = new List<EntityHandle>();
        NVector2 block = Passable(_t - _away * 4f - _perp * 14f);
        for (int k = 0; k < 30; k++) workers.Add(Spawn(0, _laborer, Passable(block + new NVector2(k % 6, k / 6) * 1.3f)));
        NVector2 far = Passable(block + _away * 40f);
        for (int k = 0; k < 15; k++)
        {
            _sim.Enqueue(Command.Move(0, workers[k], far));
            for (int j = 0; j < OrderConstants.QueueCapacity; j++) _sim.Enqueue(Command.Move(0, workers[k], Passable(far + _perp * (j + 1)), queued: true));
        }
        Apply();
        int full = workers.Count(h => U.QueueCount[h.Index] == OrderConstants.QueueCapacity);
        Check(full == 15, $"(F) {full} workers hold a full queue, want 15");
        _sel.Selection.Clear();
        foreach (EntityHandle h in workers) _sel.Selection.Add(h);
        int anchor = Anchor(block - _away * 8f);
        if (!Check(anchor >= 0, "(F) no house spot")) return;
        await Gap();
        int start = _sim.PendingCommandCount, cmd = _sfx.PlayCount(SfxEvent.Command), refused = _sfx.PlayCount(SfxEvent.Refused), drops = _sel.RefusedBuilds;
        int sent = _sel.OrderBuild(_house, anchor, queued: true);
        List<Command> list = Pending(start);
        bool onlyIdle = list.All(c => c.Kind == CommandKind.Build && c.IsQueued && workers.IndexOf(c.Unit) >= 15);
        Check(sent == 15 && list.Count == 15 && onlyIdle, $"(F) 15 full + 15 idle, queued Build: returned {sent}, sent {list.Count}, all to idle workers {onlyIdle}");
        Check(_sfx.PlayCount(SfxEvent.Command) == cmd + 1 && _sfx.PlayCount(SfxEvent.Refused) == refused && _sel.RefusedBuilds == drops,
            $"(F) 15 full + 15 idle: {_sfx.PlayCount(SfxEvent.Command) - cmd} Command, {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused, {_sel.RefusedBuilds - drops} refusals (want 1, 0, 0)");
        Apply();
        int built = workers.Skip(15).Count(h => U.QueueCount[h.Index] >= 0 && U.State[h.Index] != UnitState.Idle);
        GD.Print($"(F) 15 full + 15 idle: {sent} Builds sent, to the idle 15 only; {built} of them busy after the tick");

        // All 30 full: nothing sent, one Refused sound, one refusal.
        foreach (EntityHandle h in workers.Skip(15))
        {
            _sim.Enqueue(Command.Move(0, h, far));
            for (int j = 0; j < OrderConstants.QueueCapacity; j++) _sim.Enqueue(Command.Move(0, h, Passable(far - _perp * (j + 1)), queued: true));
        }
        Apply();
        full = workers.Count(h => U.QueueCount[h.Index] == OrderConstants.QueueCapacity);
        Check(full == 30, $"(F) {full} workers hold a full queue, want 30");
        await Gap();
        start = _sim.PendingCommandCount;
        cmd = _sfx.PlayCount(SfxEvent.Command);
        refused = _sfx.PlayCount(SfxEvent.Refused);
        drops = _sel.RefusedBuilds;
        int anchor2 = Anchor(block - _away * 8f + _perp * 10f);
        sent = _sel.OrderBuild(_house, anchor2 >= 0 ? anchor2 : anchor, queued: true);
        list = Pending(start);
        Check(sent == 0 && list.Count == 0 && _sfx.PlayCount(SfxEvent.Refused) == refused + 1 && _sfx.PlayCount(SfxEvent.Command) == cmd && _sel.RefusedBuilds == drops + 1,
            $"(F) 30 full: returned {sent}, sent {list.Count}, {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused, {_sfx.PlayCount(SfxEvent.Command) - cmd} Command, {_sel.RefusedBuilds - drops} refusals (want 0, 0, 1, 0, 1)");
        GD.Print($"(F) 30 full: nothing sent, one Refused sound, refusal counted");

        // The behind-a-walk rule (the ghost's first Shift placement) with all 30 full and walking: each would be queued, so refused.
        await Gap();
        start = _sim.PendingCommandCount;
        sent = _sel.OrderBuild(_house, anchor2 >= 0 ? anchor2 : anchor, queued: false, queueBehindEndingOrders: true);
        list = Pending(start);
        Check(sent == 0 && list.Count == 0, $"(F) 30 full, first Shift placement behind their walks: returned {sent}, sent {list.Count}, want 0 and 0");

        // A worker at 7: two queued Builds before the tick both pass the view's check; the sim drops the second.
        EntityHandle seven = workers[0];
        _sim.Enqueue(Command.Move(0, seven, far));
        for (int j = 0; j < OrderConstants.QueueCapacity - 1; j++) _sim.Enqueue(Command.Move(0, seven, Passable(far + _perp * (j + 1)), queued: true));
        Apply();
        _sel.SelectOnly(seven);
        await Gap();
        start = _sim.PendingCommandCount;
        drops = _sel.RefusedBuilds;
        int first = _sel.OrderBuild(_house, anchor, queued: true);
        await Gap();
        int second = _sel.OrderBuild(_house, anchor2 >= 0 ? anchor2 : anchor, queued: true);
        int pending = Pending(start).Count;
        Apply();
        GD.Print($"(F) a worker at 7 queued, two queued Builds in one tick: view returned {first} and {second} ({pending} sent, {_sel.RefusedBuilds - drops} refused in the view); queue after the tick {U.QueueCount[seven.Index]}");
        _sel.Selection.Clear();
        foreach (EntityHandle h in workers) _sim.Enqueue(Command.Stop(0, h));
        Apply();
    }

    // ---- (Z) zoom changed every frame while a queue is drawn ----

    private async Task ZoomChurn()
    {
        EntityHandle a = Spawn(0, _hi, Passable(_t + _perp * 4f));
        NVector2 goal = Passable(_t + _away * 20f), p = OpenGround(_t + _away * 10f + _perp * 12f);
        _sim.Enqueue(Command.Move(0, a, goal));
        _sim.Enqueue(Command.Move(0, a, p, queued: true));
        _sim.Enqueue(Command.Stop(0, a, queued: true));
        Apply();
        _sel.SelectOnly(a);
        _camera.SetFocus(p.X, p.Y);
        float[] zooms = { 20f, 60f, 35f, 30f, 29.9f, 45f, 20f, 60f, 60f, 21f };
        int bad = 0, readBack = 0, readBad = 0;
        foreach (float z in zooms)
        {
            _camera.SetZoom(z);
            await Frame();
            await Frame();
            float want = OrderPathView.ZoomScale(_camera.Zoom, OrderPathView.MarkerFullZoom);
            if (MathF.Abs(_view.MarkerZoomScale - want) > 1e-5f || MathF.Abs(_view.LineZoomScale - OrderPathView.ZoomScale(_camera.Zoom, OrderPathView.LineFullZoom)) > 1e-5f) bad++;
            if (!Windowed) continue;
            // The dummy renderer keeps no instance data; windowed, the written transforms are read back.
            int disc = -1, ring = -1;
            for (int k = 0; k < _view.MarkerCount; k++)
                if (Near(_view.MarkerPoint(k), p)) { if (_view.MarkerNoPlace(k)) ring = k; else disc = k; }
            if (disc < 0 || ring < 0) { readBad++; continue; }
            float discX = _view.MarkersNode.Multimesh.GetInstanceTransform(0).Basis.Column0.Length();
            float ringX = _view.RingsNode.Multimesh.GetInstanceTransform(0).Basis.Column0.Length();
            readBack++;
            if (MathF.Abs(discX - _view.MarkerDrawnRadius(disc)) > 1e-3f || MathF.Abs(ringX - _view.MarkerDrawnRadius(ring)) > 1e-3f)
            {
                readBad++;
                GD.Print($"(Z) zoom {z}: disc written {discX:0.000} m (want {_view.MarkerDrawnRadius(disc):0.000}), ring {ringX:0.000} m (want {_view.MarkerDrawnRadius(ring):0.000})");
            }
        }
        Check(bad == 0, $"(Z) {bad} of {zooms.Length} zoom changes where the drawn factor did not follow the camera");
        Check(readBad == 0, $"(Z) {readBad} zoom changes where the written instance radius was stale");
        GD.Print($"(Z) {zooms.Length} zoom changes while drawn: factors followed every time; {(Windowed ? $"{readBack} instance read-backs matched" : "instance read-back skipped headless")}");
        _sel.Selection.Clear();
        _sim.Enqueue(Command.Stop(0, a));
        Apply();
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    // ---- (O) overlay line on model types, a giant on a slope, a giant behind the hall ----

    private async Task Overlays()
    {
        var units = _match.GetNode<UnitViews>("World3D/UnitViews");
        var combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        UnitModels models = units.Models!;
        // Per model type: the highest bone over the clips its state map plays (UnitViews.ClipFor's six, mapped by the type's clip
        // map) against the line, printed: the line is set by the highest of every clip the library holds.
        for (int t = 0; t < _data.Units.Length; t++)
        {
            if (!units.DrawsModel(t)) continue;
            UnitDef def = _data.Units[t];
            bool worker = def.Slot == UnitSlot.Worker;
            var played = new List<UnitModels.Clip> { UnitModels.Clip.Idle, UnitModels.Clip.Walk, UnitModels.Clip.Attack };
            if (worker) played.AddRange(new[] { UnitModels.Clip.GatherGold, UnitModels.Clip.GatherWood, UnitModels.Clip.Build });
            (float top, string bone, string clip) = await TopOver(models, t, played);
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"(O) {def.Key}: over the clips it plays ({string.Join("/", played)}) the highest bone is {bone} in {clip} at {top:F3} m; line {units.OverlayLine(t):F3} m, {units.OverlayLine(t) - top:F3} m over it"));
        }

        // A hurt giant on the steepest open ground near the hall, and one north of the hall (behind it from the camera).
        int warrior = _data.FindUnit("teblor_warrior");
        NVector2 slope = Steepest(_hall, G.Width * MapConstants.CellSize);
        EntityHandle onSlope = Spawn(0, warrior, slope);
        int hall = HallSlot(0);
        BuildingDef hd = _data.Buildings[W.Buildings.TypeId[hall]];
        NVector2 behind = Passable(_hall + new NVector2(0f, -(hd.FootprintHeight * MapConstants.CellSize / 2f + 1.5f)));
        EntityHandle hidden = Spawn(0, warrior, behind);
        EntityHandle hi = Spawn(0, _hi, Passable(behind + new NVector2(2.5f, 0f)));
        foreach (EntityHandle h in new[] { onSlope, hidden, hi }) U.Hp[h.Index] = W.MaxHp(h.Index) / 2;
        _camera.SetZoom(CameraLimits.MinZoom);
        _camera.SetFocus(slope.X, slope.Y + 2f);
        for (int k = 0; k < 90; k++) await Frame(); // past the hit flash
        int bar = -1;
        for (int k = 0; k < combat.ShownBars; k++) if (combat.BarSlot(k) == onSlope.Index) bar = k;
        float grade = Grade(slope);
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"(O) hurt Warrior on ground of grade {grade:F2} (height {Height(slope):F2} m): bar {(bar >= 0 ? "shown" : "missing")}, line {units.OverlayLine(warrior):F2} m over its ground point"));
        Check(bar >= 0, "(O) the hurt Warrior on the slope has no bar");
        if (Windowed && _shots != null)
        {
            await Shot("giant-on-slope-zoom20");
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(behind.X, behind.Y + 6f);
            await Shot("giant-behind-hall-zoom40");
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(behind.X, behind.Y + 3f);
            await Shot("giant-behind-hall-zoom20");
            await ClipPictures(units);
        }
    }

    // Every Teblor model type and a Heavy Infantry, hurt, side by side, each frozen in every clip at its highest pose.
    private async Task ClipPictures(UnitViews units)
    {
        NVector2 spot = OpenGround(_t - _away * 2f);
        var types = new List<int>();
        for (int t = 0; t < _data.Units.Length; t++) if (units.DrawsModel(t) && _data.Units[t].Key.StartsWith("teblor_")) types.Add(t);
        types.Add(_hi);
        var handles = new List<EntityHandle>();
        for (int k = 0; k < types.Count; k++)
        {
            EntityHandle h = Spawn(0, types[k], Passable(spot + new NVector2((k - types.Count / 2f) * 2.6f, 0f)));
            U.Hp[h.Index] = W.MaxHp(h.Index) / 2;
            handles.Add(h);
        }
        _camera.SetZoom(CameraLimits.MinZoom);
        _camera.SetFocus(spot.X, spot.Y + 2f);
        for (int k = 0; k < 90; k++) await Frame(); // past the hit flash the hp drop started
        units.ProcessMode = ProcessModeEnum.Disabled; // no more advancing: each figure keeps the pose set below
        foreach (StringName clip in UnitModels.ClipNames)
        {
            for (int k = 0; k < handles.Count; k++)
            {
                AnimationPlayer? anim = units.AnimatorOf(handles[k].Index);
                if (anim == null || !anim.HasAnimation(clip)) continue;
                anim.PlaybackDefaultBlendTime = 0.0;
                anim.Play(clip);
                double best = 0, bestY = double.MinValue, len = anim.CurrentAnimationLength;
                Skeleton3D? skel = FindSkeleton(anim.GetParent());
                for (int s = 0; s <= 16 && skel != null; s++)
                {
                    anim.Seek(len * s / 16, true);
                    await Frame(); // the skeleton applies the pose at the frame's end
                    double y = 0;
                    for (int b = 0; b < skel.GetBoneCount(); b++) y = Math.Max(y, (skel.GlobalTransform * skel.GetBoneGlobalPose(b)).Origin.Y);
                    if (y > bestY) (best, bestY) = (len * s / 16, y);
                }
                anim.Seek(best, true);
            }
            await Shot($"teblor-bars-{clip}-zoom20");
        }
        units.ProcessMode = ProcessModeEnum.Inherit;
    }

    private static Skeleton3D? FindSkeleton(Node? n) =>
        n?.FindChildren("*", "Skeleton3D", true, false) is { Count: > 0 } found ? found[0] as Skeleton3D : null;

    // The highest counted bone (UnitModels' rule) over 17 poses of each of `clips` (mapped through the type's library), at scale.
    private async Task<(float Top, string Bone, string Clip)> TopOver(UnitModels models, int type, List<UnitModels.Clip> clips)
    {
        UnitDef def = _data.Units[type];
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = models.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer anim);
        holder.AddChild(model);
        model.Scale = new Vector3(def.Scale, def.Scale, def.Scale);
        anim.PlaybackDefaultBlendTime = 0.0;
        Skeleton3D skel = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
        (float top, string bone, string clipName) = (float.MinValue, "", "");
        foreach (UnitModels.Clip c in clips)
        {
            StringName name = UnitModels.ClipNames[(int)c];
            if (!anim.HasAnimation(name)) name = UnitModels.ClipNames[(int)UnitModels.Clip.Idle];
            if (!anim.HasAnimation(name)) continue;
            anim.Play(name);
            double len = anim.CurrentAnimationLength;
            for (int k = 0; k <= UnitModels.TopBoneSamples; k++)
            {
                anim.Seek(len * k / UnitModels.TopBoneSamples, true);
                await Frame();
                for (int b = 0; b < skel.GetBoneCount(); b++)
                {
                    string n = skel.GetBoneName(b), lower = n.ToLowerInvariant();
                    if (lower.Contains("ik") || lower == "root" || skel.GetBoneParent(b) < 0) continue;
                    float y = (skel.GlobalTransform * skel.GetBoneGlobalPose(b)).Origin.Y;
                    if (y > top) (top, bone, clipName) = (y, n, name.ToString());
                }
            }
        }
        holder.Free();
        return (top, bone, clipName);
    }

    // ---- staging and helpers ----

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
        SimRunner runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _view = _match.GetNode<OrderPathView>("World3D/OrderPaths");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sfx = _match.GetNode<Sfx>("Sfx");
        Tick(2);
        await Frames();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void Stage()
    {
        _hi = _data.FindUnit("malazan_heavy_infantry");
        _laborer = StartBase.UnitOfSlot(_data, W.FactionOf(0), UnitSlot.Worker);
        _house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        _hall = StartBase.FootprintCenter(G, _data.Buildings[W.Buildings.TypeId[HallSlot(0)]], W.Buildings.Cell[HallSlot(0)]);
        var middle = new NVector2(G.Width, G.Height) * MapConstants.CellSize / 2f;
        _away = NVector2.Normalize(middle - _hall);
        _perp = new NVector2(-_away.Y, _away.X);
        _t = Passable(_hall + _away * 24f);
        SetMoney(50000, 50000);
    }

    private int HallSlot(int player)
    {
        for (int k = 0; k < W.Buildings.Capacity; k++)
            if (W.Buildings.Alive[k] && W.Buildings.Owner[k] == player && _data.Buildings[W.Buildings.TypeId[k]].Slot == BuildingSlot.TownHall) return k;
        throw new InvalidOperationException($"player {player} has no hall");
    }

    private int Anchor(NVector2 near)
    {
        BuildingDef def = _data.Buildings[_house];
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            NVector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = NVector2.Distance(c, near);
            if (d >= bestD || d > 30f || !W.CanPlace(0, _house, cell, out _)) continue;
            (best, bestD) = (cell, d);
        }
        return best;
    }

    // The passable ramp cell (NavFlags.Ramp) nearest `around` whose ground is tilted most; `around` itself when the map has none.
    private NVector2 Steepest(NVector2 around, float radius)
    {
        NVector2 best = Passable(around);
        float bestScore = float.NegativeInfinity;
        for (int y = 1; y < G.Height - 1; y++)
            for (int x = 1; x < G.Width - 1; x++)
            {
                if (!G.IsPassable(x, y) || (G.FlagsAt(x, y) & NavFlags.Ramp) == 0) continue;
                NVector2 c = G.CellCenter(x, y);
                float score = Grade(c) * 100f - NVector2.Distance(c, around) / radius;
                if (score > bestScore) (best, bestScore) = (c, score);
            }
        return best;
    }

    private float Grade(NVector2 p) =>
        Math.Max(MathF.Abs(Height(p + new NVector2(0.5f, 0f)) - Height(p - new NVector2(0.5f, 0f))), MathF.Abs(Height(p + new NVector2(0f, 0.5f)) - Height(p - new NVector2(0f, 0.5f))));

    private float Height(NVector2 p) => TerrainHeight.At(W.Heightmap, p.X, p.Y);

    private NVector2 Passable(NVector2 p)
    {
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 1, G.Width - 2), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 1, G.Height - 2);
        int cell = FlowField.NearestPassable(G, y * G.Width + x);
        if (cell < 0) throw new InvalidOperationException($"nothing passable near {p}");
        return G.CellCenter(cell % G.Width, cell / G.Width);
    }

    private NVector2 OpenGround(NVector2 p)
    {
        G.WorldToCell(Passable(p), out int px, out int py);
        for (int r = 0; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    bool open = true;
                    for (int oy = -2; oy <= 2 && open; oy++)
                        for (int ox = -2; ox <= 2 && open; ox++)
                            if (!G.IsPassable(px + dx + ox, py + dy + oy)) open = false;
                    if (open) return G.CellCenter(px + dx, py + dy);
                }
        return Passable(p);
    }

    private static bool Near(Vector3 v, NVector2 p) => NVector2.Distance(new NVector2(v.X, v.Z), p) < Tolerance;

    private EntityHandle Spawn(int owner, int type, NVector2 at)
    {
        var alive = new bool[U.Capacity];
        for (int i = 0; i < U.Capacity; i++) alive[i] = U.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(owner, type, at));
        Apply();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !alive[i]) return new EntityHandle(i, U.Generation[i]);
        throw new InvalidOperationException($"spawn of {_data.Units[type].Key} at {at} refused");
    }

    private void SetMoney(int gold, int wood)
    {
        object ledger = LedgerProperty.GetValue(W)!;
        ((int[])ledger.GetType().GetProperty("Gold")!.GetValue(ledger)!)[0] = gold;
        ((int[])ledger.GetType().GetProperty("Wood")!.GetValue(ledger)!)[0] = wood;
    }

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private void Apply()
    {
        int n = 0;
        while (_sim.PendingCommandCount > 0 && n < 4)
        {
            _sim.Tick();
            n++;
        }
        Check(_sim.PendingCommandCount == 0, $"commands still pending after {n} ticks");
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private async Task Gap() => await ToSignal(GetTree().CreateTimer(Sfx.MinGapMs / 1000.0 * 2.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private async Task Shot(string name)
    {
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
