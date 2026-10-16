using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M4-V6a on the real Match scene: Telas Fire from the HUD. Two Cadre Mages (20 m and 30 m west of a knot of four Raiders,
/// a Laborer beside the Raiders as a spotter, <c>--no-combat</c> so nobody fights or wanders). Rows per seed: the card (a
/// "Telas Fire" button on Q with its grid key and a tooltip of the description and range / radius / cooldown from
/// <c>ui.json</c>; the row follows the active Tab subgroup); Q, Esc, the button and a right-click arm and cancel with no
/// command; the targeting rings (radius ring at the clicked enemy = the def's 3 m, range ring = 16 m round the nearer mage;
/// a point by the far mage moves it there); a click on an enemy sends exactly one <c>UseAbility</c> for the nearest ready
/// mage, which walks in (its cast-point ring drawn), stands "Casting" (panel, cast bar, F12 line) and burns the Raiders;
/// with that mage on cooldown a Shift + click queues one on the other mage only; with both on cooldown the button is dimmed
/// with the seconds left and Q, the button and the order send nothing; the hash twin. A second match at <c>--units 500</c>
/// measures 300 frames of the views, the card and the armed rings with casts running: 0 bytes, then its twin.
/// M4-V6b adds: the cast bar from the first tick (fill at least a nub, violet back); the resolve flash (none for the start,
/// one at the cast point growing to the def's radius, gone within 0.5 s, none for an enemy cast under the fog); the Burning
/// markers every tick exactly while the store's entries have ticks left; Shift keeping the ability armed (two queued casts
/// from one Q, right-click ends it); a Sapper's card with "Cusser" on Q and its 6 m / 3.5 m rings; after the twin, Slowed
/// and Burning side by side and hidden under the fog with their unit; and in the steady match 100 statuses drawn, the
/// selection panel back in the 0 B span (BUG-0340) and minimap right-clicks at 0 B.
/// M4-V6c adds: an own Priest's Sandstorm on two fresh Raiders (one disc at the resolve, the def's radius, drawn exactly
/// the zone's 240 ticks with the rule's alpha, falling over the last 20, gone at the 241st; the Raiders' Blinded marker
/// beside their Slowed one in <see cref="AbilityViews.BlindColor"/>; Telas Fire adds Burning, three markers side by side);
/// an enemy Priest's storm under the fog, alive in the store but never drawn until an own unit walking up sees its rim
/// (drawn exactly while <see cref="ZoneDiscs.SeesStorm"/> holds, in the owner's rim colour); and in the steady match 8
/// zones (two blocking) drawn at 0 B.
/// M4-V6d adds: <see cref="AbilityViews.FrenzyColor"/> at least 0.4 from each other marker colour in some channel; and per
/// seed a match on test data (the two selfAura abilities of the sim's <c>SelfAuraTests.AddTo</c> on the Heavy Infantry):
/// hovering the button rings the lead with the radius and arms nothing (0 B a frame), a click on it sends exactly one
/// <c>UseAbility</c> at the lead's position, the flash lands on the lead and the rally's Frenzied markers show in
/// FrenzyColor, Shift + click queues one for the second Heavy, the zone ability's key casts at once; its twin; and all four
/// markers side by side on a Heavy and a Raider.
/// M4-V7b adds, per seed, a match on test data (a test-only summon on the Cadre Mage, after the sim's <c>SummonTests.AddTo</c>;
/// Dryjhna's Prophecy without its Age II requirement): the summon's card (W, a tooltip without range or radius); hovering
/// draws no ring and arms nothing; a click sends exactly one <c>UseAbility</c> at the lead's position; the three Heavies
/// appear with their views at the resolve; at the expiry three <c>Expired</c> deaths, the views gone, no corpse marker and
/// K / L unchanged (sim, resource bar, F12 overlay); Shift + W through the viewport queues one for the second mage; the
/// twin; then (staging) Prophecy Zealots: 70 / 83 flashes on first sight and has a bar of 70 / 83, 83 / 83 neither (BUG-0450).
/// M5-V1 adds, per seed: the lost-cast cue (a mage ringed by holding enemy Raiders, another unit selected, drops its walk-in
/// after 3 s: exactly one cue, one Refused sound, the red disc and the <c>hud.cast_lost</c> words on the caster for 0.5 s; the
/// same walk-in cancelled by the player's Stop shows nothing), before the twin; in the cliff run the cast-point ring and the
/// resolve flash on the terrain on both levels (BUG-0520) and a sub-band ring placed twice rebuilt once (BUG-0521 item 1);
/// and in the steady match two rounds of 12 mages casting at once, the second at 0 B with no new pool node.
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/AbilityViewTest.tscn</c> (seeds 1 and 6; <c>-- --seed N</c> for one); prints "ABILITY VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves <c>ability-seedN-targeting.png</c>, <c>ability-seedN-casting.png</c>, <c>ability-seedN-first-tick.png</c>, <c>ability-seedN-flash.png</c>, <c>ability-seedN-burning.png</c>, <c>ability-seedN-cusser.png</c>, <c>ability-seedN-statuses.png</c>, <c>ability-seedN-storm.png</c>, <c>ability-seedN-blinded.png</c>, <c>ability-seedN-storm-enemy.png</c>, <c>ability-seedN-aura-hover.png</c>, <c>ability-seedN-aura-frenzied.png</c>, <c>ability-seedN-frenzied.png</c>, <c>ability-seedN-summoned.png</c>, <c>ability-seedN-cast-lost.png</c>, <c>ability-seedN-cliff-cast-ring.png</c> and <c>ability-seedN-cliff-flash.png</c>.</remarks>
public partial class AbilityViewTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;
    // Test staging only (after the twin): no shipped ability applies Slowed yet, so the rows apply it through the store.
    private static readonly MethodInfo ApplyStatus = typeof(Rts.Sim.Abilities.StatusStore).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private SelectionPanel _panel = null!;
    private CommandCard _card = null!;
    private DebugOverlay _overlay = null!;
    private AbilityViews _views = null!;
    private Sfx _sfx = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private string? _shots;
    private int _telas, _cusser, _burning, _slowed, _sandstorm, _blinded;
    private AbilityDef _def = null!;
    private EntityHandle _near, _far, _spotter, _sapper;
    private System.Numerics.Vector2 _knot;
    private Minimap _mini = null!;
    private readonly List<EntityHandle> _raiders = new();

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            ulong[] seeds = { 1, 6 };
            int at = Array.IndexOf(args, "--seed");
            if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong one)) seeds = new[] { one };
            at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _telas = _data.FindAbility("telas_fire");
            _def = _data.Abilities[_telas];
            _cusser = _data.FindAbility("cusser");
            _burning = _data.FindStatus("burning");
            _slowed = _data.FindStatus("slowed");
            _sandstorm = _data.FindAbility("sandstorm");
            _blinded = _data.FindStatus("blinded");
            UiRows();
            foreach (ulong seed in seeds) await Run(seed);
            foreach (ulong seed in seeds) await CliffRun(seed);
            FrenzyColourRow();
            OwnRimColourRow();
            foreach (ulong seed in seeds) await SelfAuraRun(seed);
            foreach (ulong seed in seeds) await SummonRun(seed);
            await Steady(seeds[0]);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"ABILITY VIEW TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"ABILITY VIEW TEST FAIL: ... {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("ABILITY VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The four new hud keys are data: present in the shipped file, each required by the loader.
    private void UiRows()
    {
        string json = System.IO.File.ReadAllText(UiText.DefaultPath).Replace("\r\n", "\n");
        foreach (HudText key in new[] { HudText.Radius, HudText.Cooldown, HudText.Seconds, HudText.Meters })
        {
            string name = UiText.Key(key);
            Check(_ui.Hud(key).Length > 0, $"ui.json hud.{name} is empty");
            string line = $"    \"{name}\": \"{_ui.Hud(key)}\"";
            int at = json.IndexOf(line, StringComparison.Ordinal);
            if (!Check(at >= 0, $"ui.json: line '{line}' not found")) continue;
            int end = json.IndexOf('\n', at);
            string without = json.Remove(at, end - at + 1).Replace(",\n  }", "\n  }");
            var errors = new List<string>();
            Check(UiText.Parse(without, errors) == null && errors.Count == 1 && errors[0].Contains($"hud.{name}"),
                $"ui.json without hud.{name}: {string.Join("; ", errors)}");
        }
        GD.Print($"ui: hud radius '{_ui.Hud(HudText.Radius)}', cooldown '{_ui.Hud(HudText.Cooldown)}', seconds '{_ui.Hud(HudText.Seconds)}', meters '{_ui.Hud(HudText.Meters)}'");
    }

    private void StartMatch(ulong seed, params string[] extra) => StartMatch(seed, _data, extra);

    private void StartMatch(ulong seed, GameData data, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--debug-overlay", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _sfx = _match.GetNode<Sfx>("Sfx");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _camera.EdgePanEnabled = false;
        _screen = GetViewport().GetVisibleRect().Size;
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // The two mages west of the centre (20 m and 30 m from the Raiders), four Raiders round a cell 6 east, a Laborer spotter.
    private System.Numerics.Vector2 Stage()
    {
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        var taken = new HashSet<int>();
        System.Numerics.Vector2 Spot(int x, int y)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int yy = Math.Max(0, y - 6); yy <= Math.Min(G.Height - 1, y + 6); yy++)
                for (int xx = Math.Max(0, x - 6); xx <= Math.Min(G.Width - 1, x + 6); xx++)
                {
                    int c = yy * G.Width + xx;
                    float d = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                    if (taken.Contains(c) || !(field.CostAt(c) <= 60f) || d >= bestD) continue;
                    bestD = d;
                    best = c;
                }
            if (best < 0) throw new InvalidOperationException($"no passable spot near ({x}, {y})");
            taken.Add(best);
            return G.CellCenter(best % G.Width, best / G.Width);
        }
        int mage = _data.FindUnit("malazan_cadre_mage"), raider = _data.FindUnit("whirlwind_raider"), laborer = _data.FindUnit("malazan_laborer");
        System.Numerics.Vector2 knot = Spot(cx + 6, cy);
        _sim.Enqueue(Command.SpawnUnit(1, raider, knot));
        _sim.Enqueue(Command.SpawnUnit(1, raider, Spot(cx + 6, cy + 1)));
        _sim.Enqueue(Command.SpawnUnit(1, raider, Spot(cx + 7, cy)));
        _sim.Enqueue(Command.SpawnUnit(1, raider, Spot(cx + 6, cy - 1)));
        _sim.Enqueue(Command.SpawnUnit(0, mage, Spot(cx - 4, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, mage, Spot(cx - 9, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, laborer, Spot(cx + 3, cy + 3)));
        _sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_sapper"), Spot(cx - 6, cy + 4)));
        return knot;
    }

    private async Task Run(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat");
        System.Numerics.Vector2 knot = Stage();
        _knot = knot;
        for (int t = 0; t < 8; t++) _sim.Tick(); // spawned, and a fog update shows the Raiders
        _raiders.Clear();
        var mages = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.Owner[i] == 1) _raiders.Add(h);
            else if (U.TypeId[i] == _data.FindUnit("malazan_cadre_mage")) mages.Add(h);
            else if (U.TypeId[i] == _data.FindUnit("malazan_sapper")) _sapper = h;
            else _spotter = h;
        }
        if (!Check(_raiders.Count == 4 && mages.Count == 2 && _sapper.Generation != 0, $"seed {seed}: staged {_raiders.Count} Raiders, {mages.Count} mages, sapper {_sapper}")) { await EndMatch(); return; }
        mages.Sort((a, b) => System.Numerics.Vector2.Distance(U.Position[a.Index], knot).CompareTo(System.Numerics.Vector2.Distance(U.Position[b.Index], knot)));
        (_near, _far) = (mages[0], mages[1]);
        System.Numerics.Vector2 mid = (U.Position[_near.Index] + knot) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();

        await CardRows(seed);
        await CancelRows(seed);
        await NearestRows(seed);
        await CastRows(seed);
        await BurnRow(seed);
        await OneOnCooldownShift(seed);
        await BothOnCooldown(seed);
        FogFlashRow(seed);
        await ShiftTwoRow(seed);
        await WalkingShiftRow(seed);
        await CusserRow(seed);
        await StormRow(seed);
        await EnemyStormRow(seed);
        await LostCastRows(seed);
        Twin(seed);
        await StatusRows(seed);
        await EndMatch();
    }

    // ---- rows ----

    private async Task CardRows(ulong seed)
    {
        await Select(_near, _far);
        Check(_card.ActionAt(0) == CardCommand.Ability && _card.AbilityAt(0) == _telas && _card.ButtonAt(0).Visible,
            $"seed {seed} card: cell 0 is {_card.ActionAt(0)} ability {_card.AbilityAt(0)}, want Ability {_telas}");
        for (int i = 1; i < 5; i++) Check(_card.ActionAt(i) == CardCommand.None, $"seed {seed} card: cell {i} is {_card.ActionAt(i)}");
        Check(_card.ActionAt(5) == CardCommand.AttackMove && _card.ActionAt(8) == CardCommand.Move, $"seed {seed} card: the unit commands moved");
        Check(_card.NameAt(0).Text == _def.DisplayName, $"seed {seed} card: name '{_card.NameAt(0).Text}', want '{_def.DisplayName}'");
        Check(_card.HintAt(0).Text == _card.GridKey(0) && _card.GridKey(0).Length > 0, $"seed {seed} card: hint '{_card.HintAt(0).Text}', grid key '{_card.GridKey(0)}'");
        string m = _ui.Hud(HudText.Meters), s = _ui.Hud(HudText.Seconds);
        string tip = $"{_def.Description}\n{_ui.Hud(HudText.Range)} 16 {m}  {_ui.Hud(HudText.Radius)} 3 {m}  {_ui.Hud(HudText.Cooldown)} 25 {s}";
        Check(_card.ButtonAt(0).TooltipText == tip, $"seed {seed} card: tooltip '{_card.ButtonAt(0).TooltipText}', want '{tip}'");
        Check(_card.AbilitySecondsAt(0) == 0 && !_card.ButtonAt(0).Disabled && _card.CostAt(0).Text.Length == 0,
            $"seed {seed} card: ready button shows {_card.AbilitySecondsAt(0)} s, disabled {_card.ButtonAt(0).Disabled}, '{_card.CostAt(0).Text}'");
        // The row follows the active Tab subgroup: the Laborer's card has none.
        await Select(_spotter, _near);
        bool sawMage = false, sawLaborer = false;
        for (int k = 0; k < _sel.Subgroups.Count; k++)
        {
            bool mage = _data.Units[_sel.Subgroups.ActiveType].Abilities.Length > 0;
            Check(_card.ActionAt(0) == (mage ? CardCommand.Ability : CardCommand.None), $"seed {seed} card: active {_data.Units[_sel.Subgroups.ActiveType].Key}, cell 0 {_card.ActionAt(0)}");
            sawMage |= mage;
            sawLaborer |= !mage;
            Key(Godot.Key.Tab);
            await Frames();
        }
        Check(sawMage && sawLaborer, $"seed {seed} card: Tab did not show both subgroups");
        int layouts = _card.Layouts;
        for (int f = 0; f < 10; f++) await Frame();
        Check(_card.Layouts == layouts, $"seed {seed} card: steady frames rewrote the card {_card.Layouts - layouts} times");
        GD.Print($"seed {seed}: card '{_card.NameAt(0).Text}' on {_card.GridKey(0)}, tooltip ok, follows Tab");
    }

    private async Task CancelRows(ulong seed)
    {
        await Select(_near, _far);
        int pending = PendingCount(), moves = _sel.IssuedCount(CommandKind.Move);
        Key(Godot.Key.Q);
        Check(_sel.Targeting && _sel.TargetAbility == _telas && _sel.TargetKind == CommandKind.UseAbility, $"seed {seed}: Q armed {_sel.TargetKind} {_sel.TargetAbility}");
        await Frames();
        Check(Label().Contains($"cast {_telas}"), $"seed {seed}: F12 label lacks 'cast {_telas}': {Label()}");
        Key(Godot.Key.Escape);
        Check(!_sel.Targeting, $"seed {seed}: Esc left targeting armed");
        _card.ButtonAt(0).EmitSignal(BaseButton.SignalName.Pressed);
        Check(_sel.TargetAbility == _telas, $"seed {seed}: the button did not arm Telas Fire");
        if (TryGroundPixel(out Vector2 ground)) RightClick(ground);
        else Check(false, $"seed {seed}: no ground pixel");
        Check(!_sel.Targeting && PendingCount() == pending && _sel.IssuedCount(CommandKind.Move) == moves,
            $"seed {seed}: right-click while armed: targeting {_sel.Targeting}, {PendingCount() - pending} commands");
        GD.Print($"seed {seed}: Q / Esc / button / right-click: armed and cancelled, no command");
        await Frame();
    }

    private async Task NearestRows(ulong seed)
    {
        await Select(_near, _far);
        Key(Godot.Key.Q);
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px)) { Check(false, $"seed {seed}: no clean enemy pixel"); return; }
        _views.Sync(W, 0.5f, px, false);
        System.Numerics.Vector2 enemyAt = U.Position[enemy.Index];
        Check(_views.TargetingShown && _views.RadiusRing.Visible && _views.RangeRing.Visible, $"seed {seed} rings: shown {_views.TargetingShown}");
        Check(_views.ShownRadius == _def.Radius && _views.ShownRange == _def.Range && _def.Radius == 3f && _def.Range == 16f,
            $"seed {seed} rings: radius {_views.ShownRadius} (def {_def.Radius}), range {_views.ShownRange} (def {_def.Range})");
        Check(_views.RadiusCenter == enemyAt, $"seed {seed} rings: radius ring at {_views.RadiusCenter}, enemy at {enemyAt}");
        Check(_views.RangeCaster == _near.Index, $"seed {seed} rings: range ring round {_views.RangeCaster}, want the nearer mage {_near.Index}");
        Check(Math.Abs(_views.RadiusRing.Position.X - enemyAt.X) < 1e-3f && Math.Abs(_views.RadiusRing.Position.Z - enemyAt.Y) < 1e-3f, $"seed {seed} rings: radius node at {_views.RadiusRing.Position}");
        Vector3 nearAt = UnitViews.GroundPoint(W, _near.Index, 0.5f);
        (int radiusOff, int radiusPoints, _, _, string radiusFirst) = OnGround((x, z) => _views.RingSurfaceAt(false, x, z), _views.RadiusCenter, new[] { _def.Radius - AbilityViews.RingWidth / 2f }, AbilityViews.Lift);
        (int rangeOff, int rangePoints, _, _, string rangeFirst) = OnGround((x, z) => _views.RingSurfaceAt(true, x, z), new System.Numerics.Vector2(nearAt.X, nearAt.Z), new[] { _def.Range - AbilityViews.RingWidth / 2f }, AbilityViews.Lift);
        Check(radiusOff == 0 && rangeOff == 0 && radiusPoints > 40 && rangePoints > 40,
            $"seed {seed} rings: not on the ground: radius {radiusOff} of {radiusPoints} (first {radiusFirst}), range {rangeOff} of {rangePoints} (first {rangeFirst})");
        // A point by the far mage: it would cast there.
        System.Numerics.Vector2 byFar = U.Position[_far.Index] + new System.Numerics.Vector2(-3f, 0f);
        Check(_sel.PickCaster(_telas, byFar, false, out int k) == _far.Index && k == 0, $"seed {seed}: a point by the far mage picks {_sel.PickCaster(_telas, byFar, false, out _)}");
        Vector2 farPx = _camera.UnprojectPosition(new Vector3(byFar.X, TerrainHeight.At(W.Heightmap, byFar.X, byFar.Y), byFar.Y));
        if (InPlayArea(farPx) && _sel.AbilityPoint(farPx, out System.Numerics.Vector2 farPoint) && System.Numerics.Vector2.Distance(farPoint, byFar) < 3f)
        {
            _views.Sync(W, 0.5f, farPx, false);
            Check(_views.RangeCaster == _far.Index, $"seed {seed} rings: by the far mage the range ring is round {_views.RangeCaster}");
        }
        _views.Sync(W, 0.5f, px, false);
        Input.WarpMouse(px);
        await Shot($"ability-seed{seed}-targeting");
        GD.Print($"seed {seed}: rings radius {_views.ShownRadius} m at enemy {enemy.Index}, range {_views.ShownRange} m round mage {_near.Index}");
    }

    private async Task CastRows(ulong seed)
    {
        // Still armed from NearestRows.
        if (!Check(_sel.TargetAbility == _telas, $"seed {seed} cast: not armed")) return;
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px)) { Check(false, $"seed {seed} cast: no enemy pixel"); return; }
        System.Numerics.Vector2 point = U.Position[enemy.Index];
        int from = PendingCount(), sounds = _sfx.PlayCount(SfxEvent.Command);
        LeftClick(px);
        List<Command> sent = Pending(from);
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == _near && sent[0].TypeId == 0 && sent[0].Position == point && !sent[0].IsQueued,
            $"seed {seed} cast: sent {Describe(sent)}, want one UseAbility for {_near} at {point}");
        Check(_sfx.PlayCount(SfxEvent.Command) == sounds + 1 && !_sel.Targeting && _sel.LastCaster == _near,
            $"seed {seed} cast: {_sfx.PlayCount(SfxEvent.Command) - sounds} sounds, targeting {_sel.Targeting}");
        // Out of range (20 m): it walks in, its cast-point ring drawn.
        _sim.Tick();
        _sim.Tick();
        _views.Sync(W, 0.5f, px, false);
        Check(U.State[_near.Index] == UnitState.Moving && U.CastAbility[_near.Index] == 0, $"seed {seed} cast: the mage is {U.State[_near.Index]} cast {U.CastAbility[_near.Index]}, want walking in");
        Check(_views.ShownCircles == 1 && _views.CircleSlot(0) == _near.Index && _views.ShownBars == 0, $"seed {seed} cast: {_views.ShownCircles} cast rings, {_views.ShownBars} bars while walking");
        int walked = 0;
        while (U.State[_near.Index] != UnitState.Casting && walked < 400) { _sim.Tick(); walked++; }
        if (!Check(U.State[_near.Index] == UnitState.Casting, $"seed {seed} cast: never started casting")) return;
        // The cast's first tick (M4-V6b, BUG-0342): no flash for a start; the bar is drawn with at least a nub of fill.
        long added = _views.Flashes.Added;
        _views.Sync(W, 0.5f, px, false);
        Check(_views.Flashes.Added == added && _views.ShownFlashes == 0, $"seed {seed} cast: the cast start drew {_views.Flashes.Added - added} flashes");
        Check(_views.ShownBars == 1 && _views.BarFill(0) < 0.2f, $"seed {seed} first tick: {_views.ShownBars} bars, fill {(_views.ShownBars > 0 ? _views.BarFill(0) : -1)}");
        Check(_views.BarFillLength(0) >= AbilityViews.BarThickness - 1e-4f && _views.BarFillLength(0) <= AbilityViews.BarLength,
            $"seed {seed} first tick: the fill is {_views.BarFillLength(0)} m long, want at least the bar's thickness {AbilityViews.BarThickness}");
        await Select(_near);
        await Shot($"ability-seed{seed}-first-tick");
        _sim.Tick();
        _sim.Tick();
        await Select(_near);
        _views.Sync(W, 0.5f, px, false);
        Check(_panel.StateLabel.Text == _ui.StateText(UnitState.Casting) && _ui.StateText(UnitState.Casting).Length > 0, $"seed {seed} cast: panel state '{_panel.StateLabel.Text}'");
        Check(_views.ShownBars == 1 && _views.BarSlot(0) == _near.Index && _views.BarFill(0) > 0f && _views.BarFill(0) < 1f,
            $"seed {seed} cast: {_views.ShownBars} bars, fill {(_views.ShownBars > 0 ? _views.BarFill(0) : -1)}");
        Check(Math.Abs(_views.BarFill(0) - AbilityCaster.Progress(U.CastTicks[_near.Index], _def.CastTicks)) < 1e-6f, $"seed {seed} cast: bar fill off the cast ticks");
        Check(_views.ShownCircles == 1, $"seed {seed} cast: {_views.ShownCircles} cast rings while casting");
        Check(Label().Contains("casting 1"), $"seed {seed} cast: F12 label lacks 'casting 1': {Label()}");
        await Shot($"ability-seed{seed}-casting");
        int ticks = 0;
        while (!Resolved(_near) && ticks < 40) { _sim.Tick(); ticks++; }
        Check(Resolved(_near), $"seed {seed} cast: no resolve within 40 ticks of casting");
        int burning = _raiders.Count(r => U.IsAlive(r) && U.Statuses.Count[r.Index] > 0);
        Check(burning >= 1 && U.Statuses.Count[enemy.Index] > 0, $"seed {seed} cast: {burning} Raiders with a status after the resolve (the clicked one: {U.Statuses.Count[enemy.Index]})");
        added = _views.Flashes.Added;
        _views.Sync(W, 0.5f, px, false);
        Check(_views.ShownBars == 0 && _views.ShownCircles == 0, $"seed {seed} cast: {_views.ShownBars} bars, {_views.ShownCircles} rings after the resolve");
        await FlashRows(seed, point, added);
        await Frames();
        Check(_card.AbilitySecondsAt(0) == 25 && _card.ButtonAt(0).Disabled, $"seed {seed} cast: the caster's button shows {_card.AbilitySecondsAt(0)} s");
        GD.Print($"seed {seed}: one UseAbility for mage {_near.Index}, walked {walked} ticks, cast, {burning} Raiders burning, button 25 s");
    }

    // The resolve flash (M4-V6b): one, at the cast point, growing to the def's radius, gone within 0.5 s of game time.
    private async Task FlashRows(ulong seed, System.Numerics.Vector2 point, long addedBefore)
    {
        int slot = FlashAt(point);
        if (!Check(_views.Flashes.Added == addedBefore + 1 && _views.ShownFlashes == 1 && slot >= 0,
            $"seed {seed} flash: {_views.Flashes.Added - addedBefore} added, {_views.ShownFlashes} shown, at the point {slot}")) return;
        Check(_views.Flashes.Ability[slot] == _telas && _views.FlashTransform(slot).Basis.X.Length() < _def.Radius,
            $"seed {seed} flash: ability {_views.Flashes.Ability[slot]}, radius {_views.FlashTransform(slot).Basis.X.Length()} at its start");
        await Shot($"ability-seed{seed}-flash");
        float widest = 0f;
        int goneAfter = -1;
        for (int t = 1; t <= 14 && goneAfter < 0; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0f, Vector2.Zero, false);
            if (_views.Flashes.Active[slot]) widest = Math.Max(widest, _views.FlashTransform(slot).Basis.X.Length());
            else goneAfter = t;
        }
        Check(Math.Abs(widest - _def.Radius) < 1e-4f, $"seed {seed} flash: widest {widest} m, want the def's radius {_def.Radius}");
        Check(goneAfter > 0 && goneAfter <= Rts.Sim.ViewApi.ResolveFlashes.LifetimeTicks && _views.FlashTransform(slot).Basis.X == Vector3.Zero && _views.ShownFlashes == 0,
            $"seed {seed} flash: gone after {goneAfter} ticks (want within {Rts.Sim.ViewApi.ResolveFlashes.LifetimeTicks}), shown {_views.ShownFlashes}");
        GD.Print($"seed {seed}: one resolve flash at the cast point, {widest} m wide at most, gone after {goneAfter} ticks; none for the start");
    }

    private int FlashAt(System.Numerics.Vector2 point)
    {
        Rts.Sim.ViewApi.ResolveFlashes f = _views.Flashes;
        for (int i = 0; i < f.Capacity; i++)
            if (f.Active[i] && f.Point[i] == point) return i;
        return -1;
    }

    // Burning markers (M4-V6b) every tick until the flames die: exactly one per shown unit whose Burning entry has ticks left.
    private async Task BurnRow(ulong seed)
    {
        Check(_views.StatusColor(_burning) == AbilityViews.DamageOverTimeColor && _views.StatusColor(_slowed) == AbilityViews.SlowColor,
            $"seed {seed} burn: colours {_views.StatusColor(_burning)} / {_views.StatusColor(_slowed)}");
        FogView fog = _views.Fog!.Refreshed(W)!;
        int burnTicks = 0, bad = 0, markerTicks = 0;
        for (int t = 0; t < 120; t++)
        {
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            int want = 0;
            bool any = false;
            for (int i = 0; i < U.Capacity; i++)
            {
                int at = U.Statuses.IndexOf(i, _burning);
                bool on = U.Alive[i] && at >= 0 && U.Statuses.TicksRemaining[at] > 0;
                any |= on;
                int marks = Marks(i, _burning);
                if (on && fog.ShowsUnit(i)) want++;
                if (marks != (on && fog.ShowsUnit(i) ? 1 : 0) && bad++ < 3)
                    Check(false, $"seed {seed} burn tick {W.TickNumber}: unit {i} burning {on} shown {fog.ShowsUnit(i)}, {marks} Burning markers");
            }
            if (_views.ShownMarkers != want && bad++ < 3) Check(false, $"seed {seed} burn tick {W.TickNumber}: {_views.ShownMarkers} markers, want {want}");
            if (any) burnTicks++;
            if (_views.ShownMarkers > 0) markerTicks++;
            if (t == 4) await Shot($"ability-seed{seed}-burning");
            if (!any && t > 0) break;
            _sim.Tick();
        }
        Check(bad == 0, $"seed {seed} burn: {bad} mismatched ticks");
        Check(burnTicks > 20 && markerTicks == burnTicks && _views.ShownMarkers == 0, $"seed {seed} burn: {burnTicks} burning ticks, {markerTicks} with markers, {_views.ShownMarkers} left after");
        GD.Print($"seed {seed}: Burning markers on {markerTicks} ticks, exactly while the entries had ticks left; none after");
    }

    // Markers drawn this frame of status `status` over unit slot `unit`.
    private int Marks(int unit, int status)
    {
        int n = 0;
        for (int k = 0; k < _views.ShownMarkers; k++)
        {
            Rts.Sim.ViewApi.StatusMark m = _views.MarkerAt(k);
            if (m.Unit == unit && m.Status == status) n++;
        }
        return n;
    }

    // An enemy mage casts under the fog (M4-V6b): the flash is collected but never drawn, and goes on time.
    private void FogFlashRow(ulong seed)
    {
        int mage = _data.FindUnit("malazan_cadre_mage");
        int cell = HiddenCell(60f);
        if (!Check(cell >= 0, $"seed {seed} fog flash: no hidden cell")) return;
        System.Numerics.Vector2 at = G.CellCenter(cell % G.Width, cell / G.Width);
        _sim.Enqueue(Command.SpawnUnit(1, mage, at));
        _sim.Tick();
        _sim.Tick(); // stamped for the next tick: applied by the second
        EntityHandle caster = default;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 1 && U.TypeId[i] == mage) caster = new EntityHandle(i, U.Generation[i]);
        if (!Check(caster.Generation != 0, $"seed {seed} fog flash: no enemy mage")) return;
        System.Numerics.Vector2 point = U.Position[caster.Index] + new System.Numerics.Vector2(3f, 0f);
        _sim.Enqueue(Command.UseAbility(1, caster, 0, point));
        long added = _views.Flashes.Added;
        int shownTicks = 0, ticks = 0;
        bool resolved = false;
        for (; ticks < 60 && !resolved; ticks++)
        {
            _sim.Tick();
            resolved = Resolved(caster);
            _views.Sync(W, 0.5f, Vector2.Zero, false);
        }
        int slot = FlashAt(point);
        // Only this flash counts: an own resolve collected earlier may still be fading in the open.
        if (slot >= 0 && _views.FlashTransform(slot).Basis.X != Vector3.Zero) shownTicks++;
        Check(resolved && _views.Flashes.Added == added + 1 && slot >= 0 && !W.Fog.IsVisible(0, FogView.CellOf(W.Fog.Width, W.Fog.Height, point)),
            $"seed {seed} fog flash: resolved {resolved}, {_views.Flashes.Added - added} collected, slot {slot}");
        for (int t = 0; t < 12; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            if (slot >= 0 && _views.FlashTransform(slot).Basis.X != Vector3.Zero) shownTicks++;
        }
        Check(shownTicks == 0 && (slot < 0 || (!_views.Flashes.Active[slot] && _views.FlashTransform(slot).Basis.X == Vector3.Zero)),
            $"seed {seed} fog flash: drawn on {shownTicks} ticks, still active {(slot >= 0 && _views.Flashes.Active[slot])}");
        GD.Print($"seed {seed}: an enemy cast under the fog resolved after {ticks} ticks: collected, never drawn, gone on time");
    }

    // A passable cell no own unit sees, at least `far` m from every own unit.
    private int HiddenCell(float far)
    {
        for (int y = 2; y < G.Height - 2; y += 3)
            for (int x = 2; x < G.Width - 2; x += 3)
            {
                int c = y * G.Width + x;
                if (!G.IsPassable(x, y) || W.Fog.IsVisible(0, c)) continue;
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                bool clear = true;
                for (int i = 0; i < U.Capacity && clear; i++)
                    if (U.Alive[i] && U.Owner[i] == 0 && System.Numerics.Vector2.Distance(U.Position[i], p) < far) clear = false;
                if (clear) return c;
            }
        return -1;
    }

    // BUG-0342: with Shift held the ability stays armed, so one Q queues two casts (the second on the other mage).
    private async Task ShiftTwoRow(ulong seed)
    {
        int waited = 0;
        while (waited < 800 && !(OffCooldown(_near) && OffCooldown(_far))) { _sim.Tick(); waited++; }
        if (!Check(OffCooldown(_near) && OffCooldown(_far), $"seed {seed} shift two: the mages never both came off cooldown")) return;
        await Select(_near, _far);
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px)) { Check(false, $"seed {seed} shift two: no enemy pixel"); return; }
        Input.ActionPress("order_queue");
        Key(Godot.Key.Q);
        int from = PendingCount();
        LeftClick(px);
        List<Command> first = Pending(from);
        bool armed = _sel.Targeting && _sel.TargetAbility == _telas;
        _sim.Tick();
        _sim.Tick(); // the first caster is busy now (its cast in hand), so the queued pick passes it over
        from = PendingCount();
        if (TryEnemyPixel(out _, out Vector2 px2)) px = px2;
        LeftClick(px);
        List<Command> second = Pending(from);
        bool stillArmed = _sel.Targeting && _sel.TargetAbility == _telas;
        Input.ActionRelease("order_queue");
        Check(first.Count == 1 && first[0].Kind == CommandKind.UseAbility && first[0].IsQueued, $"seed {seed} shift two: first click sent {Describe(first)}");
        Check(second.Count == 1 && second[0].Kind == CommandKind.UseAbility && second[0].IsQueued && first.Count == 1 && second[0].Unit != first[0].Unit,
            $"seed {seed} shift two: second click sent {Describe(second)} after {Describe(first)}");
        Check(armed && stillArmed, $"seed {seed} shift two: armed after the first click {armed}, after the second {stillArmed}");
        int pending = PendingCount();
        if (TryGroundPixel(out Vector2 ground)) RightClick(ground);
        Check(!_sel.Targeting && PendingCount() == pending, $"seed {seed} shift two: right-click left targeting {_sel.Targeting}, {PendingCount() - pending} commands");
        int ticks = 0;
        while (ticks < 400 && !(U.CastAbility[_near.Index] < 0 && U.CastAbility[_far.Index] < 0 && U.QueueCount[_near.Index] == 0 && U.QueueCount[_far.Index] == 0)) { _sim.Tick(); ticks++; }
        GD.Print($"seed {seed}: Shift held: one Q, two queued UseAbility (units {(first.Count > 0 ? first[0].Unit.Index : -1)}, {(second.Count > 0 ? second[0].Unit.Index : -1)}), still armed; right-click ended it");
        await Gap();
    }

    // M4-VH2 (BUG-0370): both mages walking under a Move, Shift + Q, three clicks in one frame with no tick between: one
    // queued cast for each mage (a cast waiting in a queue, or sent and not yet applied, makes its mage busy), the third
    // refused (nothing sent, disarmed), and both casts resolve once the walks end.
    private async Task WalkingShiftRow(ulong seed)
    {
        int waited = 0;
        while (waited < 800 && !(OffCooldown(_near) && OffCooldown(_far))) { _sim.Tick(); waited++; }
        if (!Check(OffCooldown(_near) && OffCooldown(_far), $"seed {seed} walking shift: the mages never both came off cooldown")) return;
        foreach (EntityHandle m in new[] { _near, _far })
        {
            System.Numerics.Vector2 at = U.Position[m.Index];
            _sim.Enqueue(Command.Move(SelectionController.LocalPlayer, m, at + new System.Numerics.Vector2(-6f, 0f)));
        }
        _sim.Tick();
        _sim.Tick(); // the Moves apply in the second tick
        Check(U.State[_near.Index] == UnitState.Moving && U.State[_far.Index] == UnitState.Moving,
            $"seed {seed} walking shift: mages {U.State[_near.Index]} / {U.State[_far.Index]}, want Moving");
        await Select(_near, _far);
        // The Raiders may have burned by now: aim at the ground where they stood.
        Vector2 px = _camera.UnprojectPosition(new Vector3(_knot.X, TerrainHeight.At(W.Heightmap, _knot.X, _knot.Y), _knot.Y));
        if (!InPlayArea(px) && !TryGroundPixel(out px)) { Check(false, $"seed {seed} walking shift: no ground pixel"); return; }
        Input.ActionPress("order_queue");
        Key(Godot.Key.Q);
        int from = PendingCount();
        LeftClick(px);
        LeftClick(px);
        bool armed = _sel.Targeting && _sel.TargetAbility == _telas;
        LeftClick(px);
        List<Command> sent = Pending(from);
        bool disarmed = !_sel.Targeting;
        Input.ActionRelease("order_queue");
        Check(sent.Count == 2 && sent[0].Kind == CommandKind.UseAbility && sent[1].Kind == CommandKind.UseAbility && sent[0].IsQueued && sent[1].IsQueued
            && sent[0].Unit != sent[1].Unit, $"seed {seed} walking shift: three clicks in one tick sent {Describe(sent)}, want one queued cast per mage");
        Check(armed && disarmed, $"seed {seed} walking shift: armed after two clicks {armed}, disarmed by the refused third {disarmed}");
        int nearResolves = 0, farResolves = 0, ticks = 0;
        while (ticks < 700 && (nearResolves == 0 || farResolves == 0))
        {
            _sim.Tick();
            ticks++;
            nearResolves += Resolves(_near);
            farResolves += Resolves(_far);
        }
        Check(nearResolves == 1 && farResolves == 1, $"seed {seed} walking shift: resolves near {nearResolves}, far {farResolves} in {ticks} ticks, want one each");
        GD.Print($"seed {seed}: two walking mages, three Shift clicks in one tick: {sent.Count} queued casts on units {(sent.Count > 0 ? sent[0].Unit.Index : -1)}, {(sent.Count > 1 ? sent[1].Unit.Index : -1)}, the third refused; both resolved in {ticks} ticks");
        while (ticks < 1000 && !(U.CastAbility[_near.Index] < 0 && U.CastAbility[_far.Index] < 0 && U.QueueCount[_near.Index] == 0 && U.QueueCount[_far.Index] == 0)) { _sim.Tick(); ticks++; }
        await Gap();
    }

    // Resolves of `caster` in the last tick's ability events (a sync helper: spans can't be walked in an async method).
    private int Resolves(EntityHandle caster)
    {
        int n = 0;
        foreach (AbilityEvent e in W.AbilityEvents) if (e.Resolved && e.Caster == caster) n++;
        return n;
    }

    private bool OffCooldown(EntityHandle mage) => U.IsAlive(mage) && U.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities] <= W.TickNumber && U.CastAbility[mage.Index] < 0;

    // The Cusser (M4-4b-1) from data alone: a Sapper's card shows it on Q; Q arms it with a 6 m reach and a 3.5 m area.
    private async Task CusserRow(ulong seed)
    {
        AbilityDef cusser = _data.Abilities[_cusser];
        System.Numerics.Vector2 sapperAt = U.Position[_sapper.Index];
        _camera.SetFocus(sapperAt.X, sapperAt.Y); // in the open, not under the minimap
        await Select(_sapper);
        int k = Array.IndexOf(_data.Units[U.TypeId[_sapper.Index]].Abilities.ToArray(), _cusser);
        Check(k == 0 && _card.ActionAt(k) == CardCommand.Ability && _card.AbilityAt(k) == _cusser && _card.ButtonAt(k).Visible,
            $"seed {seed} cusser: list index {k}, cell {_card.ActionAt(0)} ability {_card.AbilityAt(0)}");
        Check(_card.NameAt(0).Text == cusser.DisplayName && cusser.DisplayName == "Cusser", $"seed {seed} cusser: card name '{_card.NameAt(0).Text}'");
        Check(_card.HintAt(0).Text == _card.GridKey(0) && _card.GridKey(0) == "Q", $"seed {seed} cusser: hint '{_card.HintAt(0).Text}', grid key '{_card.GridKey(0)}'");
        Key(Godot.Key.Q);
        Check(_sel.Targeting && _sel.TargetAbility == _cusser, $"seed {seed} cusser: Q armed {_sel.TargetKind} {_sel.TargetAbility}");
        System.Numerics.Vector2 near = U.Position[_sapper.Index] + new System.Numerics.Vector2(-2.5f, 2.5f);
        Vector2 px = _camera.UnprojectPosition(new Vector3(near.X, TerrainHeight.At(W.Heightmap, near.X, near.Y), near.Y));
        if (!InPlayArea(px) && !TryGroundPixel(out px)) { Check(false, $"seed {seed} cusser: no ground pixel"); return; }
        _views.Sync(W, 0.5f, px, false);
        Check(_views.TargetingShown && _views.ShownRange == 6f && _views.ShownRadius == 3.5f && cusser.Range == 6f && cusser.Radius == 3.5f && _views.RangeCaster == _sapper.Index,
            $"seed {seed} cusser: rings shown {_views.TargetingShown}, range {_views.ShownRange} round {_views.RangeCaster}, radius {_views.ShownRadius}");
        Input.WarpMouse(px);
        await Shot($"ability-seed{seed}-cusser");
        Key(Godot.Key.Escape);
        Check(!_sel.Targeting, $"seed {seed} cusser: Esc left it armed");
        System.Numerics.Vector2 mid = (U.Position[_near.Index] + _knot) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();
        GD.Print($"seed {seed}: Sapper card '{_card.NameAt(0).Text}' on {_card.GridKey(0)}; rings {_views.ShownRange} m / {_views.ShownRadius} m");
    }

    // M4-V6c: an own Priest's Sandstorm on two fresh Raiders. One disc from the resolve for the zone's 240 ticks, the def's
    // radius on the ground at the centre, the rule's alpha (0.35, falling over the last 20 ticks), a sand rim; gone at the
    // 241st. The Raiders inside show Blinded beside Slowed; a mage's Telas Fire on them adds Burning (three markers).
    private async Task StormRow(ulong seed)
    {
        AbilityDef storm = _data.Abilities[_sandstorm];
        int raider = _data.FindUnit("whirlwind_raider"), priest = _data.FindUnit("whirlwind_priest");
        var before = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(i);
        System.Numerics.Vector2 point = _knot + new System.Numerics.Vector2(0f, 1f);
        _sim.Enqueue(Command.SpawnUnit(1, raider, _knot));
        _sim.Enqueue(Command.SpawnUnit(1, raider, _knot + new System.Numerics.Vector2(0f, 2f)));
        _sim.Enqueue(Command.SpawnUnit(0, priest, _knot - new System.Numerics.Vector2(12f, 0f)));
        // A fresh mage for the Telas Fire (the two staged ones are on their 25 s cooldown from the rows before).
        int mageType = _data.FindUnit("malazan_cadre_mage");
        _sim.Enqueue(Command.SpawnUnit(0, mageType, _knot - new System.Numerics.Vector2(10f, 3f)));
        for (int t = 0; t < 6; t++) _sim.Tick();
        var fresh = new List<EntityHandle>();
        EntityHandle caster = default, mage = default;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || before.Contains(i)) continue;
            if (U.TypeId[i] == raider) fresh.Add(new EntityHandle(i, U.Generation[i]));
            else if (U.TypeId[i] == priest) caster = new EntityHandle(i, U.Generation[i]);
            else if (U.TypeId[i] == mageType) mage = new EntityHandle(i, U.Generation[i]);
        }
        if (!Check(fresh.Count == 2 && caster.Generation != 0 && mage.Generation != 0, $"seed {seed} storm: {fresh.Count} fresh Raiders, priest {caster}, mage {mage}")) return;
        _camera.SetFocus(point.X, point.Y);
        _sim.Enqueue(Command.UseAbility(0, caster, 0, point));
        int ticks = 0, early = 0;
        while (ticks < 200 && !Resolved(caster))
        {
            _sim.Tick();
            ticks++;
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            if (!Resolved(caster) && DiscAt(point) >= 0) early++;
        }
        if (!Check(Resolved(caster) && early == 0, $"seed {seed} storm: resolved {Resolved(caster)} after {ticks} ticks, a disc before it on {early}")) return;
        var alphas = new List<float>();
        int bad = 0, gone = 0;
        bool sideBySide = false, three = false, fired = false;
        string firstBad = "";
        for (int t = 0; t < 400; t++)
        {
            if (t > 0)
            {
                _sim.Tick();
                _views.Sync(W, 0.5f, Vector2.Zero, false);
            }
            int k = DiscAt(point);
            if (k < 0)
            {
                gone = t;
                break;
            }
            ZoneDisc d = _views.ZoneDiscAt(k);
            Vector3 at = _views.ZoneDiscPosition(k);
            Color fill = _views.ZoneDiscFill(k), rim = _views.ZoneDiscRim(k);
            bool ok = d.Own && d.Radius == storm.Radius && W.Zones.Alive[d.Slot] && fill.A == ZoneDiscs.Alpha(W.Zones.TicksRemaining[d.Slot])
                && Math.Abs(at.Y - (TerrainHeight.At(W.Heightmap, point.X, point.Y) + AbilityViews.ZoneLift)) < 1e-4f && at.X == point.X && at.Z == point.Y
                && SameRgb(rim, AbilityViews.OwnZoneRimColor) && SameRgb(fill, AbilityViews.ZoneFillColor);
            if (!ok && bad++ == 0) firstBad = $"tick {t}: {d}, at {at}, fill {fill}, rim {rim}, {W.Zones.TicksRemaining[d.Slot]} ticks left";
            alphas.Add(fill.A);
            if (t == 10)
            {
                // Both fresh Raiders stand in the storm: Blinded beside Slowed, in their colours.
                EntityHandle r = fresh[0];
                int kb = MarkIndex(r.Index, _blinded), ks = MarkIndex(r.Index, _slowed);
                sideBySide = Check(kb >= 0 && ks >= 0 && Marks(fresh[1].Index, _blinded) == 1 && Marks(fresh[1].Index, _slowed) == 1
                    && _views.StatusColor(_blinded) == AbilityViews.BlindColor && _views.StatusColor(_slowed) == AbilityViews.SlowColor
                    && Math.Abs(Math.Abs(_views.MarkerPosition(kb).X - _views.MarkerPosition(ks).X) - AbilityViews.MarkerSpacing * MarkerScale()) < 1e-3f
                    && Math.Abs(_views.MarkerPosition(kb).Y - _views.MarkerPosition(ks).Y) < 1e-4f,
                    $"seed {seed} storm: Raider {r.Index} markers blinded {kb}, slowed {ks}; Raider {fresh[1].Index} {Marks(fresh[1].Index, _blinded)}+{Marks(fresh[1].Index, _slowed)}");
                await Shot($"ability-seed{seed}-storm");
            }
            if (t == 10)
            {
                // The fresh mage's Telas Fire on the Raiders.
                fired = true;
                _sim.Enqueue(Command.UseAbility(0, mage, 0, U.Position[fresh[0].Index]));
            }
            if (t > 10 && !three)
                foreach (EntityHandle r in fresh)
                {
                    if (!U.IsAlive(r) || Marks(r.Index, _burning) != 1 || Marks(r.Index, _blinded) != 1 || Marks(r.Index, _slowed) != 1) continue;
                    three = true;
                    Color cb = _views.StatusColor(_blinded), cs = _views.StatusColor(_slowed), cd = _views.StatusColor(_burning);
                    Check(cb != cs && cb != cd && cb != AbilityViews.OtherStatusColor && cs != cd,
                        $"seed {seed} blinded: colours {cb} {cs} {cd} not distinct");
                    await Shot($"ability-seed{seed}-blinded");
                    GD.Print($"seed {seed}: Raider {r.Index} shows Blinded, Slowed and Burning side by side at storm tick {t}");
                    break;
                }
        }
        int n = alphas.Count;
        bool holds = n == storm.DurationTicks;
        for (int t = 0; t < n - ZoneDiscs.FadeTicks && holds; t++) holds = alphas[t] == ZoneDiscs.DiscAlpha;
        bool falls = n > ZoneDiscs.FadeTicks;
        for (int t = n - ZoneDiscs.FadeTicks; t < n && falls; t++) falls = alphas[t] < alphas[t - 1];
        Check(bad == 0, $"seed {seed} storm: {bad} wrong discs, first {firstBad}");
        Check(holds && falls && gone == storm.DurationTicks && storm.DurationTicks == 240,
            $"seed {seed} storm: drawn {n} ticks (gone at {gone}), want {storm.DurationTicks}; alpha holds {holds}, falls {falls}");
        Check(sideBySide && fired && three, $"seed {seed} storm: Blinded beside Slowed {sideBySide}, Telas Fire sent {fired}, three markers {three}");
        GD.Print($"seed {seed}: own Sandstorm drawn {n} ticks, alpha {alphas[0]} to {alphas[n - 1]} over the last {ZoneDiscs.FadeTicks}; gone at tick {gone}");
        System.Numerics.Vector2 mid = (U.Position[_near.Index] + _knot) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();
    }

    // M5-V1 (Requests 34): the lost-cast cue. A fresh mage ringed by eight holding enemy Raiders is ordered (through the
    // selection) to cast Telas Fire on the knot, out of range: its walk-in is stuck and the sim drops the cast after 3 s (one
    // Dropped event). With another unit selected, the cue fires exactly once: one LostCastCues entry, one Refused sound, the
    // red disc under the mage and the hud.cast_lost words over it for 0.5 s, then nothing; the cast-point ring ends with the
    // cast. Then the same walk-in, cancelled after 1 s by the player's Stop (S), shows nothing over 100 ticks.
    private async Task LostCastRows(ulong seed)
    {
        int mageType = _data.FindUnit("malazan_cadre_mage"), raider = _data.FindUnit("whirlwind_raider");
        System.Numerics.Vector2 at = OpenSpot(U.Position[_near.Index] + new System.Numerics.Vector2(0f, 12f));
        if (!Check(!float.IsNaN(at.X), $"seed {seed} lost cast: no open spot")) return;
        var before = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(i);
        _sim.Enqueue(Command.SpawnUnit(0, mageType, at));
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            _sim.Enqueue(Command.SpawnUnit(1, raider, at + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a))));
        }
        _sim.Tick();
        _sim.Tick();
        EntityHandle mage = default;
        int walls = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || before.Contains(i)) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.TypeId[i] == mageType) mage = h;
            else
            {
                _sim.Enqueue(Command.HoldPosition(1, h));
                walls++;
            }
        }
        if (!Check(mage.Generation != 0 && walls == 8, $"seed {seed} lost cast: staged mage {mage}, {walls} Raiders")) return;
        _sim.Tick();
        _camera.SetFocus(at.X, at.Y);
        Check(_views.CastLostText == _ui.Hud(HudText.CastLost) && _ui.Hud(HudText.CastLost).Length > 0,
            $"seed {seed} lost cast: the views' text '{_views.CastLostText}', ui.json hud.cast_lost '{_ui.Hud(HudText.CastLost)}'");

        // (1) The drop: ordered through the selection, then the HUD moves on to another unit.
        await Select(mage);
        System.Numerics.Vector2 point = _knot;
        Check(System.Numerics.Vector2.Distance(at, point) > _def.Range + 2f, $"seed {seed} lost cast: the knot is only {System.Numerics.Vector2.Distance(at, point):0.0} m off");
        int from = PendingCount();
        Check(_sel.AbilityOrder(_telas, point, false) && Pending(from).Count == 1, $"seed {seed} lost cast: the order sent {Describe(Pending(from))}");
        await Select(_spotter);
        await Gap(); // past the order's Command sound, so a Refused one counts
        long cues = _views.LostCasts.Added;
        int refused = _sfx.PlayCount(SfxEvent.Refused);
        int drops = 0, dropTick = -1, cueTicks = 0, ringTicks = 0, wrong = 0, ticks = 0;
        string firstWrong = "";
        bool shot = false;
        for (; ticks < 200; ticks++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            int d = Drops(mage);
            drops += d;
            if (d > 0) dropTick = ticks;
            bool ring = false;
            for (int k = 0; k < _views.ShownCircles; k++) if (_views.CircleSlot(k) == mage.Index) ring = true;
            if (ring) ringTicks++;
            if (dropTick < 0)
            {
                // The order applies in the second tick (stamped for the next tick number); the ring shows from then on.
                if ((_views.ShownCues != 0 || (!ring && ticks > 0)) && wrong++ == 0) firstWrong = $"tick {ticks} before the drop: {_views.ShownCues} cues, ring {ring}";
                continue;
            }
            if (ring && wrong++ == 0) firstWrong = $"tick {ticks}: the cast ring outlived the drop";
            if (_views.ShownCues == 0) continue;
            cueTicks++;
            int slot = _views.CueSlot(0);
            GroundDisc? disc = _views.CueNode(slot);
            Label3D label = _views.CueLabel(slot);
            Vector3 ground = UnitViews.GroundPoint(W, mage.Index, 0.5f);
            bool ok = _views.ShownCues == 1 && _views.LostCasts.Caster[slot] == mage && disc != null && disc.Visible
                && Math.Abs(disc.Position.X - ground.X) < 1e-3f && Math.Abs(disc.Position.Z - ground.Z) < 1e-3f
                && disc.Radius > U.Radius[mage.Index] && Math.Abs(disc.Lift - AbilityViews.CueLift) < 1e-6f
                && label.Visible && label.Text == _ui.Hud(HudText.CastLost) && label.Position.Y > ground.Y + 1f;
            if (!ok && wrong++ == 0) firstWrong = $"tick {ticks}: {_views.ShownCues} cues, caster {_views.LostCasts.Caster[slot]}, disc {disc?.Visible} at {disc?.Position} r {disc?.Radius} (mage at {ground}), label {label.Visible} '{label.Text}' at {label.Position}";
            if (!shot)
            {
                shot = true;
                await Shot($"ability-seed{seed}-cast-lost");
            }
            if (ticks > dropTick + 30) break;
        }
        for (int t = 0; t < 30 && dropTick >= 0; t++)
        {
            _sim.Tick();
            ticks++;
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            drops += Drops(mage);
            if (_views.ShownCues != 0) cueTicks++;
        }
        bool labelsHidden = true;
        for (int c = 0; c < _views.LostCasts.Capacity; c++) if (_views.CueLabel(c).Visible) labelsHidden = false;
        GD.Print($"seed {seed}: lost cast: ringed mage {mage.Index} dropped its walk-in on tick {dropTick} of the order (ring drawn {ringTicks} ticks), "
            + $"{_views.LostCasts.Added - cues} cue, {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused sound, the cue drawn {cueTicks} ticks, selection {_sel.Selection.Count} ({(_sel.Selection.Count > 0 ? _sel.Selection.Items[0].Index : -1)})");
        Check(drops == 1 && dropTick >= 59 && dropTick <= 90, $"seed {seed} lost cast: {drops} drops, the first on tick {dropTick} (want one, after the 3 s patience)");
        Check(_views.LostCasts.Added == cues + 1 && _sfx.PlayCount(SfxEvent.Refused) == refused + 1,
            $"seed {seed} lost cast: {_views.LostCasts.Added - cues} cues, {_sfx.PlayCount(SfxEvent.Refused) - refused} Refused sounds, want one each");
        Check(wrong == 0, $"seed {seed} lost cast: {wrong} wrong frames, first {firstWrong}");
        Check(cueTicks >= LostCastCues.LifetimeTicks - 1 && cueTicks <= LostCastCues.LifetimeTicks + 1 && _views.ShownCues == 0 && labelsHidden && _views.CuePool.InUse == 0,
            $"seed {seed} lost cast: the cue drawn {cueTicks} ticks (want {LostCastCues.LifetimeTicks}), {_views.ShownCues} left, labels hidden {labelsHidden}, {_views.CuePool.InUse} discs in use");
        Check(_sel.Selection.Count == 1 && _sel.Selection.Items[0] == _spotter, $"seed {seed} lost cast: the selection moved");

        // (2) The player's own cancel: the same walk-in, a Stop 1 s in. No event, no cue, no sound.
        await Select(mage);
        Check(_sel.AbilityOrder(_telas, point, false), $"seed {seed} cancel: no order sent");
        for (int t = 0; t < 20; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
        }
        Check(U.CastAbility[mage.Index] >= 0 && U.State[mage.Index] == UnitState.Moving, $"seed {seed} cancel: the mage is {U.State[mage.Index]} cast {U.CastAbility[mage.Index]}, want walking in");
        await Gap();
        cues = _views.LostCasts.Added;
        refused = _sfx.PlayCount(SfxEvent.Refused);
        int stops = _sel.IssuedCount(CommandKind.Stop);
        Key(Godot.Key.S);
        Check(_sel.IssuedCount(CommandKind.Stop) == stops + 1, $"seed {seed} cancel: S sent {_sel.IssuedCount(CommandKind.Stop) - stops} Stops");
        int cancelDrops = 0, cancelCues = 0;
        for (int t = 0; t < 100; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            cancelDrops += Drops(mage);
            if (_views.ShownCues != 0) cancelCues++;
        }
        Check(U.CastAbility[mage.Index] < 0 && cancelDrops == 0 && cancelCues == 0 && _views.LostCasts.Added == cues && _sfx.PlayCount(SfxEvent.Refused) == refused,
            $"seed {seed} cancel: cast {U.CastAbility[mage.Index]}, {cancelDrops} drops, cue drawn {cancelCues} ticks, {_views.LostCasts.Added - cues} cues, {_sfx.PlayCount(SfxEvent.Refused) - refused} sounds; want none");
        GD.Print($"seed {seed}: cancel: the walk-in stopped by S: no drop, no cue, no sound over 100 ticks");
        System.Numerics.Vector2 mid = (U.Position[_near.Index] + _knot) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();
    }

    // Dropped events of `caster` in the last tick.
    private int Drops(EntityHandle caster)
    {
        int n = 0;
        foreach (AbilityEvent e in W.AbilityEvents) if (e.Dropped && e.Caster == caster) n++;
        return n;
    }

    // The point nearest `near` whose cells within 3 m are all passable plain ground of one level with no unit within 5 m; NaN if none.
    private System.Numerics.Vector2 OpenSpot(System.Numerics.Vector2 near)
    {
        Heightmap hm = W.Heightmap;
        int nx = (int)(near.X / MapConstants.CellSize), ny = (int)(near.Y / MapConstants.CellSize);
        var best = new System.Numerics.Vector2(float.NaN, float.NaN);
        float bestD = float.MaxValue;
        for (int y = Math.Max(3, ny - 20); y <= Math.Min(G.Height - 4, ny + 20); y++)
            for (int x = Math.Max(3, nx - 20); x <= Math.Min(G.Width - 4, nx + 20); x++)
            {
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                float d = System.Numerics.Vector2.Distance(p, near);
                if (d >= bestD) continue;
                bool ok = true;
                for (int dy = -2; dy <= 2 && ok; dy++)
                    for (int dx = -2; dx <= 2 && ok; dx++)
                        ok = G.IsPassable(x + dx, y + dy) && !hm.IsRamp(x + dx, y + dy) && hm.LevelAt(x + dx, y + dy) == hm.LevelAt(x, y);
                for (int i = 0; i < U.Capacity && ok; i++) if (U.Alive[i] && System.Numerics.Vector2.Distance(U.Position[i], p) < 5f) ok = false;
                if (!ok) continue;
                best = p;
                bestD = d;
            }
        return best;
    }

    // BUG-0430 / BUG-0462 item 1 (M4-VH3): two own Sandstorms across a cliff (one centred on the plateau 1 m from the drop,
    // one on the low ground 3 m off it) and Telas Fire's two targeting rings there (the radius ring on the cursor 1 m from
    // the drop, the 16 m range ring round a mage on the plateau): every sampled point of each disc and ring is drawn on the
    // terrain under it (within 0.05 m of TerrainHeight.At + its lift) on both levels.
    private async Task CliffRun(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat", "--no-fog");
        for (int t = 0; t < 4; t++) _sim.Tick();
        Heightmap hm = W.Heightmap;
        // A straight east-facing cliff over 9 rows: two plateau cells, then a plain cell one level lower (no ramp).
        int fx = -1, fy = -1;
        for (int y = 12; y < hm.Height - 12 && fx < 0; y++)
            for (int x = 12; x < hm.Width - 12 && fx < 0; x++)
            {
                int level = hm.LevelAt(x, y);
                bool ok = level >= 1;
                for (int dy = -4; dy <= 4 && ok; dy++)
                    ok = hm.LevelAt(x, y + dy) == level && hm.LevelAt(x - 1, y + dy) == level && !hm.IsRamp(x, y + dy) && !hm.IsRamp(x - 1, y + dy)
                        && hm.LevelAt(x + 1, y + dy) == level - 1 && !hm.IsRamp(x + 1, y + dy);
                if (ok) (fx, fy) = (x, y);
            }
        if (!Check(fx >= 0, $"seed {seed} cliff: no straight plateau edge found")) { await EndMatch(); return; }
        const float cs = MapConstants.CellSize;
        float drop = (fx + 1) * cs, row = (fy + 0.5f) * cs;
        System.Numerics.Vector2 high = new(drop - 1f, row), low = new(drop + 3f, row);
        MethodInfo create = typeof(ZoneSystem).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
        AbilityDef storm = _data.Abilities[_sandstorm];
        create.Invoke(null, new object[] { W, 0, storm, high });
        create.Invoke(null, new object[] { W, 0, storm, low });
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        if (!Check(_views.ShownZones == 2, $"seed {seed} cliff: {_views.ShownZones} discs, want 2")) { await EndMatch(); return; }
        float[] discRadii = { 0.3f * storm.Radius, 0.65f * storm.Radius, storm.Radius - AbilityViews.ZoneRimWidth / 2f };
        for (int k = 0; k < 2; k++)
        {
            int kk = k;
            ZoneDisc d = _views.ZoneDiscAt(k);
            (int bad, int n, float lo, float hi, string first) = OnGround((x, z) => _views.ZoneSurfaceAt(kk, x, z), d.Center, discRadii, AbilityViews.ZoneLift);
            GD.Print($"seed {seed} cliff: storm {k} at {d.Center} (cliff at x {drop}, cell ({fx}, {fy}) level {hm.LevelAt(fx, fy)}): {n} points on ground {lo:0.0}-{hi:0.0} m, {bad} off it");
            Check(bad == 0 && n > 150 && hi - lo >= MapConstants.LevelHeight - 0.01f,
                $"seed {seed} cliff: storm {k} at {d.Center}: {bad} of {n} points not on the ground (ground {lo:0.00}-{hi:0.00} m), first {first}");
        }
        for (int k = 0; k < 2; k++)
        {
            int off = MeshOffGround(_views.ZoneNode(_views.ZoneDiscAt(k).Slot), out int verts, out string firstOff);
            Check(off == 0 && verts >= 6, $"seed {seed} cliff: storm {k}'s mesh: {off} of {verts} vertices off their cell's ground, first {firstOff}");
        }
        _camera.SetFocus(drop + 1f, row);
        await Frames();
        await Shot($"ability-seed{seed}-cliff-storms");

        // The targeting rings: a mage on the plateau, Telas Fire armed, the cursor on `high`.
        int mageType = _data.FindUnit("malazan_cadre_mage");
        _sim.Enqueue(Command.SpawnUnit(0, mageType, new System.Numerics.Vector2(drop - 3f, row - 2f)));
        _sim.Tick();
        _sim.Tick();
        EntityHandle mage = default;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == mageType) mage = new EntityHandle(i, U.Generation[i]);
        if (!Check(mage.Generation != 0, $"seed {seed} cliff: no mage")) { await EndMatch(); return; }
        await Select(mage);
        Check(_sel.BeginAbility(_telas), $"seed {seed} cliff: Telas Fire not armed");
        Vector2 px = _camera.UnprojectPosition(new Vector3(high.X, TerrainHeight.At(hm, high.X, high.Y), high.Y));
        _views.Sync(W, 0.5f, px, false);
        if (Check(_views.TargetingShown && _views.RangeRing.Visible && _views.RangeCaster == mage.Index && System.Numerics.Vector2.Distance(_views.RadiusCenter, high) < 0.5f,
            $"seed {seed} cliff rings: shown {_views.TargetingShown}, range {_views.RangeRing.Visible} round {_views.RangeCaster} (mage {mage.Index}), radius ring at {_views.RadiusCenter}, want {high}"))
        {
            Vector3 mageAt = UnitViews.GroundPoint(W, mage.Index, 0.5f);
            (int bad, int n, float lo, float hi, string first) = OnGround((x, z) => _views.RingSurfaceAt(false, x, z), _views.RadiusCenter,
                new[] { _views.ShownRadius - AbilityViews.RingWidth / 2f }, AbilityViews.Lift);
            Check(bad == 0 && n > 40 && hi - lo >= MapConstants.LevelHeight - 0.01f, $"seed {seed} cliff: radius ring: {bad} of {n} points not on the ground ({lo:0.00}-{hi:0.00} m), first {first}");
            (int rbad, int rn, float rlo, float rhi, string rfirst) = OnGround((x, z) => _views.RingSurfaceAt(true, x, z), new System.Numerics.Vector2(mageAt.X, mageAt.Z),
                new[] { _views.ShownRange - AbilityViews.RingWidth / 2f }, AbilityViews.Lift);
            Check(rbad == 0 && rn > 40 && rhi - rlo >= MapConstants.LevelHeight - 0.01f, $"seed {seed} cliff: range ring: {rbad} of {rn} points not on the ground ({rlo:0.00}-{rhi:0.00} m), first {rfirst}");
            GD.Print($"seed {seed} cliff rings: radius {n} points ({lo:0.0}-{hi:0.0} m) {bad} off; range {rn} points ({rlo:0.0}-{rhi:0.0} m) {rbad} off");
            foreach (GroundDisc ring in new[] { _views.RadiusRing, _views.RangeRing })
            {
                int off = MeshOffGround(ring, out int verts, out string firstOff);
                Check(off == 0 && verts >= 6, $"seed {seed} cliff: {ring.Name}'s mesh: {off} of {verts} vertices off their cell's ground, first {firstOff}");
            }
        }
        Input.WarpMouse(px);
        await Shot($"ability-seed{seed}-cliff-rings");
        _sel.CancelTargeting();
        await CliffCastRows(seed, mage, high);
        Bug0521Row(seed, high);
        await EndMatch();
    }

    // BUG-0520 (M5-V1): the plateau mage casts Telas Fire on `high` (1 m from the drop, so its 3 m area spans both levels).
    // Its cast-point ring while it casts and the resolve flash (on its first frame, half grown, and grown) are drawn on the
    // terrain + lift at every sampled point, on both levels, every vertex on its cell's surface; the ring isn't rebuilt while
    // its cast point stays put. (Flat at the centre's height, the part over the low ground floated 4 m up.)
    private async Task CliffCastRows(ulong seed, EntityHandle mage, System.Numerics.Vector2 high)
    {
        if (!Check(_sel.AbilityOrder(_telas, high, false), $"seed {seed} cliff cast: no order sent")) return;
        int ticks = 0;
        while (U.State[mage.Index] != UnitState.Casting && ticks < 60) { _sim.Tick(); ticks++; }
        _sim.Tick();
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        int k = -1;
        for (int c = 0; c < _views.ShownCircles; c++) if (_views.CircleSlot(c) == mage.Index) k = c;
        if (!Check(U.State[mage.Index] == UnitState.Casting && k >= 0, $"seed {seed} cliff cast: the mage is {U.State[mage.Index]}, ring {k}")) return;
        float r = _def.Radius;
        GroundDisc ring = _views.CastRing(k);
        (int bad, int n, float lo, float hi, string first) = OnGround((x, z) => _views.CastRingSurfaceAt(k, x, z), U.CastPoint[mage.Index],
            new[] { r * (1f - AbilityViews.CastRingShare / 2f) }, AbilityViews.Lift);
        Check(bad == 0 && n > 40 && hi - lo >= MapConstants.LevelHeight - 0.01f, $"seed {seed} cliff: cast ring: {bad} of {n} points not on the ground ({lo:0.00}-{hi:0.00} m), first {first}");
        int off = MeshOffGround(ring, out int verts, out string firstOff);
        Check(off == 0 && verts >= 6, $"seed {seed} cliff: the cast ring's mesh: {off} of {verts} vertices off their cell's ground, first {firstOff}");
        // The row tells the old drawing apart: a flat ring at the centre's height misses points over the other level.
        System.Numerics.Vector2 cp = U.CastPoint[mage.Index];
        float flatY = TerrainHeight.At(W.Heightmap, cp.X, cp.Y) + AbilityViews.Lift;
        (int flatBad, int flatN, _, _, _) = OnGround((x, z) => flatY, cp, new[] { r * (1f - AbilityViews.CastRingShare / 2f) }, AbilityViews.Lift);
        Check(flatBad > 0, $"seed {seed} cliff: a flat ring at the centre's height would miss no point ({flatN} sampled): the row can't tell the bug");
        int rebuilds = ring.Rebuilds;
        for (int t = 0; t < 3; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
        }
        Check(ring.Rebuilds == rebuilds && _views.CastRing(k) == ring, $"seed {seed} cliff: the cast ring was rebuilt {ring.Rebuilds - rebuilds} times with its cast point still");
        GD.Print($"seed {seed} cliff cast ring: {n} points ({lo:0.0}-{hi:0.0} m) {bad} off (flat at the centre's height: {flatBad} of {flatN} off), {verts} vertices {off} off, no rebuild over 3 ticks");
        await Shot($"ability-seed{seed}-cliff-cast-ring");

        ticks = 0;
        while (!Resolved(mage) && ticks < 40) { _sim.Tick(); ticks++; }
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        int slot = FlashAt(high);
        if (!Check(Resolved(mage) && slot >= 0 && _views.FlashNode(slot) is { Visible: true }, $"seed {seed} cliff flash: resolved {Resolved(mage)}, slot {slot}")) return;
        GroundDisc flash = _views.FlashNode(slot)!;
        float share = flash.DrawnShare;
        (int fbad, int fn, float flo, float fhi, string ffirst) = OnGround((x, z) => _views.FlashSurfaceAt(slot, x, z), high, new[] { 0.2f * r, 0.4f * r }, AbilityViews.Lift);
        Check(share < 0.7f && fbad == 0 && fn > 60 && fhi - flo >= MapConstants.LevelHeight - 0.01f,
            $"seed {seed} cliff: the flash's first frame (share {share:0.00}): {fbad} of {fn} points not on the ground ({flo:0.00}-{fhi:0.00} m), first {ffirst}");
        int frebuilds = flash.Rebuilds;
        for (int t = 0; t < 5; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
        }
        (int gbad, int gn, float glo, float ghi, string gfirst) = OnGround((x, z) => _views.FlashSurfaceAt(slot, x, z), high, new[] { 0.3f * r, 0.65f * r, 0.95f * r }, AbilityViews.Lift);
        Check(flash.DrawnShare == 1f && gbad == 0 && gn > 150 && ghi - glo >= MapConstants.LevelHeight - 0.01f,
            $"seed {seed} cliff: the grown flash (share {flash.DrawnShare:0.00}): {gbad} of {gn} points not on the ground ({glo:0.00}-{ghi:0.00} m), first {gfirst}");
        float flashFlatY = TerrainHeight.At(W.Heightmap, high.X, high.Y) + AbilityViews.Lift;
        (int flashFlatBad, _, _, _, _) = OnGround((x, z) => flashFlatY, high, new[] { 0.3f * r, 0.65f * r, 0.95f * r }, AbilityViews.Lift);
        Check(flashFlatBad > 0, $"seed {seed} cliff: a flat flash at the centre's height would miss no point");
        int foff = MeshOffGround(flash, out int fverts, out string ffoff);
        Check(foff == 0 && fverts >= 6 && flash.Rebuilds == frebuilds, $"seed {seed} cliff: the flash's mesh: {foff} of {fverts} vertices off, rebuilt {flash.Rebuilds - frebuilds} times while it grew");
        GD.Print($"seed {seed} cliff flash: first frame {fn} points ({flo:0.0}-{fhi:0.0} m) {fbad} off at share {share:0.00}; grown {gn} points {gbad} off (flat: {flashFlatBad} off); {fverts} vertices {foff} off; no rebuild while growing");
        await Shot($"ability-seed{seed}-cliff-flash");
    }

    // BUG-0521 item 1 (M5-V1): a ring whose band is wider than its radius (a negative hole) placed again where it is isn't
    // rebuilt (Place compared the raw hole with the clamped one it stored, so it rebuilt every frame).
    private void Bug0521Row(ulong seed, System.Numerics.Vector2 at)
    {
        var tiny = new GroundDisc { Name = "Bug0521Ring" };
        AddChild(tiny);
        float radius = 0.1f;
        for (int f = 0; f < 5; f++) tiny.Place(W.Heightmap, at, radius, radius - AbilityViews.RingWidth, AbilityViews.Lift);
        Check(tiny.Rebuilds == 1 && tiny.Hole == 0f && tiny.Radius == radius, $"seed {seed} BUG-0521: a 0.1 m ring placed 5 times in one spot was rebuilt {tiny.Rebuilds} times (hole {tiny.Hole})");
        GD.Print($"seed {seed}: BUG-0521 item 1: a {radius} m ring (hole {radius - AbilityViews.RingWidth:0.00}) placed 5 times: {tiny.Rebuilds} rebuild");
        RemoveChild(tiny);
        tiny.QueueFree();
    }

    // Reads the disc's drawn mesh back from Godot: every vertex (node position + vertex) must lie on the top surface of the
    // cell its triangle covers (the cell of the triangle's centroid) plus the disc's lift, within 0.05 m (in fact exact).
    private int MeshOffGround(GroundDisc disc, out int verts, out string first)
    {
        verts = 0;
        first = "";
        if (disc.Mesh is not ImmediateMesh mesh || mesh.GetSurfaceCount() != 1) return 1;
        Vector3[] v = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        verts = v.Length;
        int off = 0;
        const float cs = MapConstants.CellSize;
        for (int t = 0; t + 2 < v.Length; t += 3)
        {
            Vector3 c = disc.Position + (v[t] + v[t + 1] + v[t + 2]) / 3f;
            int cx = (int)MathF.Floor(c.X / cs), cy = (int)MathF.Floor(c.Z / cs);
            for (int i = t; i < t + 3; i++)
            {
                Vector3 w = disc.Position + v[i];
                float ground = TerrainHeight.InCell(W.Heightmap, cx, cy, w.X, w.Z) + disc.Lift;
                if (!(Math.Abs(w.Y - ground) <= 0.05f) && off++ == 0) first = $"{w} on cell ({cx}, {cy}) at {ground:0.00}";
            }
        }
        return off;
    }

    // Samples `surface` (the drawn height at a ground point, NaN where nothing is drawn) on circles of `radii` round `c`, 72
    // points each, against the terrain + `lift`. A point within 1 cm of a cell edge is skipped (at a cliff both heights are
    // the ground there, and At takes the larger cell's), as is one off the map. Returns the misses, the points, the ground's
    // lowest and highest height among them, and the first miss.
    private (int Bad, int Points, float Lo, float Hi, string First) OnGround(Func<float, float, float> surface, System.Numerics.Vector2 c, float[] radii, float lift)
    {
        const float cs = MapConstants.CellSize;
        Heightmap hm = W.Heightmap;
        int bad = 0, n = 0;
        float lo = float.MaxValue, hi = float.MinValue;
        string first = "";
        foreach (float r in radii)
            for (int a = 0; a < 72; a++)
            {
                float x = c.X + r * MathF.Cos(a * MathF.Tau / 72f), z = c.Y + r * MathF.Sin(a * MathF.Tau / 72f);
                if (x < 0f || z < 0f || x >= hm.Width * cs || z >= hm.Height * cs) continue;
                float ux = x / cs - MathF.Floor(x / cs), uz = z / cs - MathF.Floor(z / cs);
                if (ux < 0.005f || ux > 0.995f || uz < 0.005f || uz > 0.995f) continue;
                float ground = TerrainHeight.At(hm, x, z), drawn = surface(x, z);
                n++;
                lo = Math.Min(lo, ground);
                hi = Math.Max(hi, ground);
                if (!(Math.Abs(drawn - (ground + lift)) <= 0.05f) && bad++ == 0) first = $"({x:0.00}, {z:0.00}) drawn {drawn:0.00}, ground {ground:0.00} + {lift}";
            }
        return (bad, n, lo, hi, first);
    }

    // M4-V6c: an enemy Priest's Sandstorm under the fog is alive in the store but never drawn until an own unit walking up
    // sees its rim; from then it is drawn exactly while a cell within its radius + 2 m is visible, in the owner's colour.
    private async Task EnemyStormRow(ulong seed)
    {
        AbilityDef storm = _data.Abilities[_sandstorm];
        int priest = _data.FindUnit("whirlwind_priest");
        int cell = HiddenCellNear(36f);
        if (!Check(cell >= 0, $"seed {seed} enemy storm: no hidden cell")) return;
        System.Numerics.Vector2 p = G.CellCenter(cell % G.Width, cell / G.Width);
        var before = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(i);
        // A Wickan Lancer (fast) of our own on the way from the army, 6 m beyond where its sight would reach the rim.
        int lancer = _data.FindUnit("malazan_wickan_lancer");
        float sight = _data.Units[lancer].Sight;
        System.Numerics.Vector2 dir = System.Numerics.Vector2.Normalize(Centroid(0) - p);
        _sim.Enqueue(Command.SpawnUnit(1, priest, p));
        _sim.Enqueue(Command.SpawnUnit(0, lancer, p + dir * (storm.Radius + ZoneDiscs.RimWidth + sight + 6f)));
        _sim.Tick();
        _sim.Tick();
        EntityHandle caster = default, walker = default;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || before.Contains(i)) continue;
            if (U.TypeId[i] == priest) caster = new EntityHandle(i, U.Generation[i]);
            else if (U.TypeId[i] == lancer) walker = new EntityHandle(i, U.Generation[i]);
        }
        if (!Check(caster.Generation != 0 && walker.Generation != 0, $"seed {seed} enemy storm: priest {caster}, walker {walker}")) return;
        // The walker stops where half its sight reaches past the storm's edge: the rim is seen, nothing inside is.
        System.Numerics.Vector2 to = p + dir * (storm.Radius + 0.5f * sight);
        _sim.Enqueue(Command.UseAbility(1, caster, 0, p));
        int slot = -1, ticks = 0, hidden = 0, drawn = 0, wrong = 0;
        bool shot = false;
        string firstWrong = "";
        Color owner = UnitViews.ColorFromRgb(_match.PlayerRgb[1]); // the owner's player colour (M6-V3, BUG-0631 item 2)
        for (; ticks < 400; ticks++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            if (slot < 0)
            {
                for (int k = 0; k < W.Zones.Capacity; k++)
                    if (W.Zones.Alive[k] && W.Zones.Owner[k] == 1 && W.Zones.Center[k] == p) slot = k;
                if (slot >= 0) _sim.Enqueue(Command.Move(0, walker, to)); // the walk starts once the storm is up
            }
            if (slot < 0) continue;
            if (!W.Zones.Alive[slot]) break;
            bool sees = ZoneDiscs.SeesStorm(W.Fog.Visibility(0), W.Fog.Width, W.Fog.Height, p, storm.Radius);
            int at = DiscAt(p);
            if ((at >= 0) != sees && wrong++ == 0) firstWrong = $"tick {ticks}: drawn {at >= 0}, rim seen {sees}";
            if (at < 0)
            {
                if (drawn == 0) hidden++;
                continue;
            }
            drawn++;
            ZoneDisc d = _views.ZoneDiscAt(at);
            if ((d.Own || d.Slot != slot || !SameRgb(_views.ZoneDiscRim(at), owner)) && wrong++ == 0) firstWrong = $"tick {ticks}: {d}, rim {_views.ZoneDiscRim(at)}, want {owner}";
            if (!shot && drawn >= 4)
            {
                shot = true;
                System.Numerics.Vector2 mid = (U.Position[walker.Index] + p) / 2f;
                _camera.SetFocus(mid.X, mid.Y);
                await Shot($"ability-seed{seed}-storm-enemy");
            }
            if (drawn >= 40) break;
        }
        Check(slot >= 0 && hidden >= Rts.Sim.Vision.VisionConstants.UpdateInterval && drawn > 0 && wrong == 0,
            $"seed {seed} enemy storm: slot {slot}, live but hidden {hidden} ticks, then drawn {drawn}; {wrong} wrong, first {firstWrong}; walker {System.Numerics.Vector2.Distance(U.Position[walker.Index], p):0.0} m from the centre");
        GD.Print($"seed {seed}: an enemy Sandstorm under the fog: live, not drawn for {hidden} ticks, drawn once the walker saw its rim ({drawn} ticks checked)");
        System.Numerics.Vector2 back = (U.Position[_near.Index] + _knot) / 2f;
        _camera.SetFocus(back.X, back.Y);
        await Frames();
    }

    // The passable cell no own unit sees, at least `far` m from every own unit and 12 cells in from the map's edges, nearest
    // the own units (so the walk up to it is short and the shot shows the ground round it).
    private int HiddenCellNear(float far)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int y = 12; y < G.Height - 12; y += 2)
            for (int x = 12; x < G.Width - 12; x += 2)
            {
                int c = y * G.Width + x;
                if (!G.IsPassable(x, y) || W.Fog.IsVisible(0, c)) continue;
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                float d = float.MaxValue;
                for (int i = 0; i < U.Capacity; i++)
                    if (U.Alive[i] && U.Owner[i] == 0) d = Math.Min(d, System.Numerics.Vector2.Distance(U.Position[i], p));
                if (d >= far && d < bestD) { bestD = d; best = c; }
            }
        return best;
    }

    // The drawn disc whose zone is centred on `point`, or -1.
    private int DiscAt(System.Numerics.Vector2 point)
    {
        for (int k = 0; k < _views.ShownZones; k++)
            if (_views.ZoneDiscAt(k).Center == point) return k;
        return -1;
    }

    // The marker of `status` over `unit`, or -1.
    private int MarkIndex(int unit, int status)
    {
        for (int k = 0; k < _views.ShownMarkers; k++)
        {
            Rts.Sim.ViewApi.StatusMark m = _views.MarkerAt(k);
            if (m.Unit == unit && m.Status == status) return k;
        }
        return -1;
    }

    // The bars' zoom growth (the markers' spacing grows with it).
    private float MarkerScale() => Math.Max(1f, _camera.Zoom / CombatViews.BarFullZoom);

    private static bool SameRgb(Color a, Color b) => Math.Abs(a.R - b.R) < 1e-5f && Math.Abs(a.G - b.G) < 1e-5f && Math.Abs(a.B - b.B) < 1e-5f;

    // After the twin (the store is written directly): Slowed and Burning side by side, then hidden under the fog with their unit.
    private async Task StatusRows(ulong seed)
    {
        // Two fresh Raiders on the knot (the first four may have burned to death by now), seen at the next fog update.
        int raider = _data.FindUnit("whirlwind_raider");
        var before = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(i);
        _sim.Enqueue(Command.SpawnUnit(1, raider, _knot));
        _sim.Enqueue(Command.SpawnUnit(1, raider, _knot + new System.Numerics.Vector2(0f, 2f)));
        for (int t = 0; t < 6; t++) _sim.Tick();
        var fresh = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && !before.Contains(i) && U.Owner[i] == 1 && U.TypeId[i] == raider) fresh.Add(new EntityHandle(i, U.Generation[i]));
        if (!Check(fresh.Count == 2 && W.Fog.CanSeeUnit(0, fresh[0].Index) && W.Fog.CanSeeUnit(0, fresh[1].Index), $"seed {seed} statuses: {fresh.Count} fresh Raiders, seen")) return;
        EntityHandle a = fresh[0], b = fresh[1];
        ApplyStatus.Invoke(U.Statuses, new object[] { a.Index, _slowed, 0.3f, 2400, 0 });
        ApplyStatus.Invoke(U.Statuses, new object[] { a.Index, _burning, 1f, 2400, 0 });
        ApplyStatus.Invoke(U.Statuses, new object[] { b.Index, _slowed, 0.3f, 2400, 0 });
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        int ka = -1, kb = -1;
        for (int k = 0; k < _views.ShownMarkers; k++)
        {
            Rts.Sim.ViewApi.StatusMark m = _views.MarkerAt(k);
            if (m.Unit == a.Index && m.Place == 0) ka = k;
            if (m.Unit == b.Index) kb = k;
        }
        if (Check(ka >= 0 && kb >= 0 && Marks(a.Index, _slowed) == 1 && Marks(a.Index, _burning) == 1 && Marks(b.Index, _slowed) == 1,
            $"seed {seed} statuses: markers a {Marks(a.Index, _slowed)}+{Marks(a.Index, _burning)}, b {Marks(b.Index, _slowed)}"))
        {
            Rts.Sim.ViewApi.StatusMark first = _views.MarkerAt(ka), second = _views.MarkerAt(ka + 1);
            Vector3 p0 = _views.MarkerPosition(ka), p1 = _views.MarkerPosition(ka + 1);
            Check(first.Status == _slowed && second.Status == _burning && first.Row == 2 && second.Place == 1, $"seed {seed} statuses: a's row {first} {second}, want Slowed then Burning (the order applied)");
            Check(_views.StatusColor(first.Status) == AbilityViews.SlowColor && _views.StatusColor(second.Status) == AbilityViews.DamageOverTimeColor, $"seed {seed} statuses: colours");
            Check(p1.X - p0.X > AbilityViews.MarkerSize * 0.99f && Math.Abs(p1.Y - p0.Y) < 1e-4f && Math.Abs(p1.Z - p0.Z) < 1e-4f,
                $"seed {seed} statuses: a's markers at {p0} and {p1}, not side by side");
        }
        await Shot($"ability-seed{seed}-statuses");
        // Every own unit walks 70 m west: the Raiders go under the fog, and their markers with them.
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0) continue;
            System.Numerics.Vector2 to = U.Position[i] - new System.Numerics.Vector2(70f, 0f);
            _sim.Enqueue(Command.Move(0, new EntityHandle(i, U.Generation[i]), new System.Numerics.Vector2(Math.Max(4f, to.X), to.Y)));
        }
        int ticks = 0, bad = 0;
        while (ticks < 900 && (W.Fog.CanSeeUnit(0, a.Index) || W.Fog.CanSeeUnit(0, b.Index)))
        {
            _sim.Tick();
            ticks++;
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            for (int k = 0; k < _views.ShownMarkers; k++)
                if (!W.Fog.CanSeeUnit(0, _views.MarkerAt(k).Unit)) bad++;
        }
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        Check(!W.Fog.CanSeeUnit(0, a.Index) && !W.Fog.CanSeeUnit(0, b.Index) && U.Statuses.Count[a.Index] == 2 && U.Statuses.Count[b.Index] == 1,
            $"seed {seed} statuses: after {ticks} ticks a seen {W.Fog.CanSeeUnit(0, a.Index)}, b seen {W.Fog.CanSeeUnit(0, b.Index)}; statuses {U.Statuses.Count[a.Index]}, {U.Statuses.Count[b.Index]}");
        Check(bad == 0 && Marks(a.Index, _slowed) + Marks(a.Index, _burning) + Marks(b.Index, _slowed) == 0,
            $"seed {seed} statuses: {bad} markers over hidden units; under the fog a {Marks(a.Index, _slowed) + Marks(a.Index, _burning)}, b {Marks(b.Index, _slowed)}");
        GD.Print($"seed {seed}: Slowed + Burning side by side on Raider {a.Index}, Slowed on {b.Index}; hidden with them under the fog after {ticks} ticks");
    }

    private async Task OneOnCooldownShift(ulong seed)
    {
        await Select(_near, _far);
        Check(_card.AbilitySecondsAt(0) == 0 && !_card.ButtonAt(0).Disabled, $"seed {seed} one ready: button {_card.AbilitySecondsAt(0)} s");
        if (!TryEnemyPixel(out EntityHandle enemy, out Vector2 px)) { Check(false, $"seed {seed} shift: no enemy pixel"); return; }
        System.Numerics.Vector2 point = U.Position[enemy.Index];
        Input.ActionPress("order_queue");
        Key(Godot.Key.Q);
        int from = PendingCount();
        LeftClick(px);
        Input.ActionRelease("order_queue");
        List<Command> sent = Pending(from);
        // The near mage is nearer (it walked in) but on cooldown: the far one, queued, and nothing for the near one.
        Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == _far && sent[0].IsQueued && sent[0].Position == point,
            $"seed {seed} shift: sent {Describe(sent)}, want one queued UseAbility for {_far}");
        // M4-V6b (BUG-0342): the Shift click left the ability armed; Esc ends it.
        Check(_sel.Targeting && _sel.TargetAbility == _telas, $"seed {seed} shift: the Shift click disarmed ({_sel.TargetKind})");
        Key(Godot.Key.Escape);
        Check(!_sel.Targeting, $"seed {seed} shift: Esc left it armed");
        int ticks = 0;
        while (!Resolved(_far) && ticks < 600) { _sim.Tick(); ticks++; }
        Check(Resolved(_far), $"seed {seed} shift: the far mage never resolved");
        Check(U.CastAbility[_near.Index] < 0 && U.QueueCount[_near.Index] == 0, $"seed {seed} shift: the near mage got an order");
        GD.Print($"seed {seed}: Shift + click with the near mage on cooldown: one queued UseAbility for the far mage, resolved after {ticks} ticks");
        await Gap();
    }

    private async Task BothOnCooldown(ulong seed)
    {
        await Select(_near, _far);
        int left = _sel.SoonestReady(_telas);
        int seconds = (left + 19) / 20;
        Check(left > 0 && _card.AbilitySecondsAt(0) == seconds && _card.ButtonAt(0).Disabled, $"seed {seed} both: button {_card.AbilitySecondsAt(0)} s, want {seconds} ({left} ticks)");
        Check(_card.CostAt(0).Text == $"{seconds} {_ui.Hud(HudText.Seconds)}" && _card.NameAt(0).Modulate.A == CommandCard.DimAlpha,
            $"seed {seed} both: line '{_card.CostAt(0).Text}', name alpha {_card.NameAt(0).Modulate.A}");
        int from = PendingCount(), sounds = _sfx.PlayCount(SfxEvent.Command);
        Key(Godot.Key.Q);
        Check(!_sel.Targeting, $"seed {seed} both: Q armed targeting");
        _card.Press(0);
        Check(!_sel.Targeting, $"seed {seed} both: the button armed targeting");
        Check(!_sel.AbilityOrder(_telas, U.Position[_raiders[0].Index], false) && PendingCount() == from && _sfx.PlayCount(SfxEvent.Command) == sounds,
            $"seed {seed} both: an order went out");
        GD.Print($"seed {seed}: both on cooldown: dimmed '{_card.CostAt(0).Text}', nothing sent");
        await Frame();
    }

    // ---- M4-V6d: the Frenzied colour and the no-target (selfAura) card ----

    // The largest per-channel difference of two colours (the marker colours must differ by at least 0.4 in some channel).
    private static float ChannelGap(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // FrenzyColor against the other three markers and the pale default; StatusColor(frenzied) checked in SelfAuraRun's match.
    private void FrenzyColourRow()
    {
        foreach ((string name, Color other) in new[] { ("Burning", AbilityViews.DamageOverTimeColor), ("Slowed", AbilityViews.SlowColor),
                     ("Blinded", AbilityViews.BlindColor), ("other", AbilityViews.OtherStatusColor) })
        {
            float gap = ChannelGap(AbilityViews.FrenzyColor, other);
            Check(gap >= 0.4f, $"frenzy colour: {AbilityViews.FrenzyColor} is only {gap:0.00} from {name} {other}");
            GD.Print($"frenzy colour: {gap:0.00} from {name}");
        }
    }

    // BUG-0431 (M4-VH3): an own zone's rim is far from every player colour (an enemy zone's rim), so a Whirlwind
    // mirror's own and enemy storms read apart: some channel at least 0.4 apart (the marker colours' rule; the old deeper
    // sand was 0.064 from #C8892E at most). Since M6-V3 the owner colour is ui.json's player colour table, not the faction's
    // primary (BUG-0631 item 2).
    private void OwnRimColourRow()
    {
        UiText ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
        for (int c = 0; c < UiText.ColourCount; c++)
        {
            Color enemy = UnitViews.ColorFromRgb(ui.ColourRgb(c));
            float gap = ChannelGap(AbilityViews.OwnZoneRimColor, enemy);
            Check(gap >= 0.4f, $"own rim colour: {AbilityViews.OwnZoneRimColor} is only {gap:0.000} from player colour {UiText.ColourKey(c)} {enemy}");
            GD.Print($"own rim colour: {gap:0.00} from player colour {UiText.ColourKey(c)}");
        }
    }

    private static GameData? _auraData;

    // The shipped data plus SelfAuraTests.AddTo's two test-only selfAura abilities on the Heavy Infantry (Requests 30: no shipped
    // ability uses the kind), written to a copy under the user data folder and loaded from there.
    // A fresh copy of the shipped data's JSON in the user data folder under `name`; returns its path.
    private static string CopyOfShippedData(string name)
    {
        string from = ProjectSettings.GlobalizePath("res://data"), to = System.IO.Path.Combine(OS.GetUserDataDir(), name);
        if (System.IO.Directory.Exists(to)) System.IO.Directory.Delete(to, recursive: true);
        foreach (string dir in System.IO.Directory.GetDirectories(from, "*", System.IO.SearchOption.AllDirectories))
            System.IO.Directory.CreateDirectory(dir.Replace(from, to));
        System.IO.Directory.CreateDirectory(to);
        foreach (string file in System.IO.Directory.GetFiles(from, "*.json", System.IO.SearchOption.AllDirectories))
            System.IO.File.Copy(file, file.Replace(from, to));
        return to;
    }

    private static GameData AuraData()
    {
        if (_auraData != null) return _auraData;
        string to = CopyOfShippedData("abilityview-selfaura-data");
        string abilities = System.IO.Path.Combine(to, "factions", "malazan", "abilities.json");
        var root = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(abilities))!;
        var list = root["abilities"]!.AsArray();
        list.Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "zz_test_rally", "displayName": "Test Rally", "description": "Test only.", "kind": "selfAura", "radius": 4,
              "castTime": 0.5, "cooldown": 10, "affects": "own_units", "autocast": false,
              "effects": [ { "kind": "applyStatus", "status": "frenzied", "magnitude": 0.5, "duration": 5 } ] }
            """));
        list.Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "zz_test_aura_zone", "displayName": "Test Aura Zone", "description": "Test only.", "kind": "selfAura", "radius": 3,
              "castTime": 0, "cooldown": 10, "duration": 4, "affects": "enemy_units", "autocast": false,
              "effects": [ { "kind": "createZone", "statuses": [ { "status": "slowed", "magnitude": 0.3, "duration": 1 } ] } ] }
            """));
        System.IO.File.WriteAllText(abilities, root.ToJsonString());
        string units = System.IO.Path.Combine(to, "factions", "malazan", "units.json");
        var unitRoot = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(units))!;
        foreach (System.Text.Json.Nodes.JsonNode? u in unitRoot["units"]!.AsArray())
            if ((string?)u!["id"] == "malazan_heavy_infantry") u["abilities"] = new System.Text.Json.Nodes.JsonArray("zz_test_rally", "zz_test_aura_zone");
        System.IO.File.WriteAllText(units, unitRoot.ToJsonString());
        DataLoadResult r = DataLoader.LoadAll(to);
        if (!r.Ok) throw new InvalidOperationException("selfAura test data: " + string.Join("; ", r.Errors));
        return _auraData = r.Data!;
    }

    // M4-V6d on a match whose Heavy Infantry carry the two test selfAura abilities: three Heavies (a lead, a second 10 m
    // east, a third 2 m from the lead) and a Raider 12 m off. Hovering the button rings the lead with the radius and arms
    // nothing; a click on the button through the viewport sends exactly one UseAbility at the lead's position (no targeting,
    // one sound), the resolve flash lands on the lead and the rally's Frenzied markers show in FrenzyColor; Shift + click
    // queues one for the second Heavy; the hotkey (W) casts the zone at once; 0 B a frame with the hover ring drawn; the twin.
    private async Task SelfAuraRun(ulong seed)
    {
        GameData shipped = _data;
        GameData data = AuraData();
        _data = data;
        try
        {
            StartMatch(seed, data, "--units", "0", "--no-bases", "--no-combat");
            int heavy = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider");
            int rally = data.FindAbility("zz_test_rally"), zone = data.FindAbility("zz_test_aura_zone");
            AbilityDef rallyDef = data.Abilities[rally];
            int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
            System.Numerics.Vector2 c = G.CellCenter(center % G.Width, center / G.Width);
            _sim.Enqueue(Command.SpawnUnit(0, heavy, c));
            _sim.Enqueue(Command.SpawnUnit(0, heavy, c + new System.Numerics.Vector2(10f, 0f)));
            _sim.Enqueue(Command.SpawnUnit(0, heavy, c + new System.Numerics.Vector2(0f, 2f)));
            _sim.Enqueue(Command.SpawnUnit(1, raider, c + new System.Numerics.Vector2(0f, -12f)));
            for (int t = 0; t < 8; t++) _sim.Tick();
            var heavies = new List<EntityHandle>();
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == heavy) heavies.Add(new EntityHandle(i, U.Generation[i]));
            heavies.Sort((a, b) => System.Numerics.Vector2.Distance(U.Position[a.Index], c).CompareTo(System.Numerics.Vector2.Distance(U.Position[b.Index], c)));
            if (!Check(heavies.Count == 3, $"seed {seed} aura: {heavies.Count} Heavies staged")) return;
            // Sorted by distance from c: the lead (0 m), the third (2 m), the second (10 m).
            EntityHandle lead = heavies[0], near = heavies[1], second = heavies[2];
            _camera.SetFocus(c.X + 4f, c.Y);
            Check(_views.StatusColor(data.FindStatus("frenzied")) == AbilityViews.FrenzyColor, $"seed {seed} aura: StatusColor(frenzied) {_views.StatusColor(data.FindStatus("frenzied"))}, want FrenzyColor");
            await Select(lead, second);

            // The card: both on the top row, the tooltip without a range.
            string m = _ui.Hud(HudText.Meters), s = _ui.Hud(HudText.Seconds);
            string tip = $"{rallyDef.Description}\n{_ui.Hud(HudText.Radius)} 4 {m}  {_ui.Hud(HudText.Cooldown)} 10 {s}";
            Check(_card.AbilityAt(0) == rally && _card.AbilityAt(1) == zone && _card.ButtonAt(0).TooltipText == tip,
                $"seed {seed} aura card: cells {_card.AbilityAt(0)} {_card.AbilityAt(1)}, tooltip '{_card.ButtonAt(0).TooltipText}', want '{tip}'");

            // Hover: the radius ring round the lead, no range ring, nothing armed.
            Vector2 button = _card.ButtonAt(0).GetGlobalRect().GetCenter();
            Push(new InputEventMouseMotion { Position = button });
            await Frames();
            _views.Sync(W, 0.5f, button, false);
            Vector3 leadAt = UnitViews.GroundPoint(W, lead.Index, 0.5f);
            Check(_card.HoveredAbility == rally && _views.SelfRingShown && _views.RadiusRing.Visible && !_views.RangeRing.Visible && _views.ShownRadius == rallyDef.Radius
                && _views.RangeCaster == lead.Index && Math.Abs(_views.RadiusRing.Position.X - leadAt.X) < 1e-3f && Math.Abs(_views.RadiusRing.Position.Z - leadAt.Z) < 1e-3f && !_sel.Targeting,
                $"seed {seed} aura hover: hovered {_card.HoveredAbility}, ring {_views.SelfRingShown} at {_views.RadiusRing.Position} round {_views.RangeCaster} (lead {lead.Index} at {leadAt}), radius {_views.ShownRadius}, range ring {_views.RangeRing.Visible}, targeting {_sel.Targeting}");
            (int hoverOff, int hoverPoints, _, _, string hoverFirst) = OnGround((x, z) => _views.RingSurfaceAt(false, x, z), _views.RadiusCenter,
                new[] { _views.ShownRadius - AbilityViews.RingWidth / 2f }, AbilityViews.Lift);
            Check(hoverOff == 0 && hoverPoints > 40, $"seed {seed} aura hover: {hoverOff} of {hoverPoints} ring points not on the ground, first {hoverFirst}");
            await Shot($"ability-seed{seed}-aura-hover");

            // 0 B a frame with the hover ring drawn (the card's hover read, the caster pick, the ring).
            long bytes = 0;
            int ringFrames = 0;
            for (int f = 0; f < 160; f++)
            {
                if (f % 3 == 0) _sim.Tick();
                long before = GC.GetAllocatedBytesForCurrentThread();
                _views.Sync(W, f % 3 / 3f, button, false);
                _card.Sync();
                long spent = GC.GetAllocatedBytesForCurrentThread() - before;
                if (f >= 40) bytes += spent;
                if (f >= 40 && _views.SelfRingShown) ringFrames++;
            }
            Check(bytes == 0 && ringFrames == 120, $"seed {seed} aura hover: {bytes} bytes over 120 frames, ring in {ringFrames}");
            GD.Print($"seed {seed}: selfAura hover ring round the lead in {ringFrames} of 120 frames, {bytes} bytes");

            // A click on the button: one UseAbility at the lead's position, no targeting, one sound.
            int from = PendingCount(), sounds = _sfx.PlayCount(SfxEvent.Command);
            await Gap();
            sounds = _sfx.PlayCount(SfxEvent.Command);
            LeftClick(button);
            List<Command> sent = Pending(from);
            System.Numerics.Vector2 leadPos = U.Position[lead.Index];
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == lead && sent[0].TypeId == 0 && sent[0].Position == leadPos && !sent[0].IsQueued,
                $"seed {seed} aura click: sent {Describe(sent)}, want one UseAbility for {lead} #0 at {leadPos}");
            Check(!_sel.Targeting && _sel.TargetAbility < 0 && _sfx.PlayCount(SfxEvent.Command) == sounds + 1,
                $"seed {seed} aura click: targeting {_sel.Targeting} ({_sel.TargetKind}), sounds {_sfx.PlayCount(SfxEvent.Command) - sounds}");
            long added = _views.Flashes.Added;
            int ticks = 0;
            while (!Resolved(lead) && ticks < 40) { _sim.Tick(); ticks++; }
            System.Numerics.Vector2 resolvedAt = ResolvePoint(lead);
            _views.Sync(W, 1f, Vector2.Zero, false);
            int flash = -1;
            for (int k = 0; k < _views.Flashes.Active.Length; k++) if (_views.Flashes.Active[k] && _views.Flashes.Ability[k] == rally) flash = k;
            Check(Resolved(lead) && resolvedAt == U.Position[lead.Index] && _views.Flashes.Added == added + 1 && flash >= 0 && _views.Flashes.Point[flash] == U.Position[lead.Index] && _views.ShownFlashes == 1,
                $"seed {seed} aura resolve: resolved {Resolved(lead)} after {ticks} ticks at {resolvedAt} (lead at {U.Position[lead.Index]}), flashes +{_views.Flashes.Added - added}, at {(flash >= 0 ? _views.Flashes.Point[flash] : default)}");
            // The rally made the lead and the Heavy beside it Frenzied: their markers in FrenzyColor; the far one has none.
            int frenzied = data.FindStatus("frenzied");
            Check(Marks(lead.Index, frenzied) == 1 && Marks(near.Index, frenzied) == 1 && Marks(second.Index, frenzied) == 0 && _views.MarkerAt(MarkIndex(lead.Index, frenzied)).Status == frenzied,
                $"seed {seed} aura: Frenzied markers lead {Marks(lead.Index, frenzied)}, beside {Marks(near.Index, frenzied)}, far {Marks(second.Index, frenzied)}");
            await Shot($"ability-seed{seed}-aura-frenzied");
            GD.Print($"seed {seed}: selfAura click: one UseAbility at the lead's position, resolved after {ticks} ticks with the flash on it; Frenzied on the two Heavies within 4 m");

            // Shift + click with the lead on cooldown: one queued UseAbility for the second Heavy at its own position.
            await Gap();
            from = PendingCount();
            sounds = _sfx.PlayCount(SfxEvent.Command);
            Input.ActionPress("order_queue");
            LeftClick(button);
            Input.ActionRelease("order_queue");
            sent = Pending(from);
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == second && sent[0].IsQueued && sent[0].Position == U.Position[second.Index] && !_sel.Targeting
                && _sfx.PlayCount(SfxEvent.Command) == sounds + 1,
                $"seed {seed} aura shift: sent {Describe(sent)}, want one queued UseAbility for {second}; targeting {_sel.Targeting}");
            ticks = 0;
            while (!Resolved(second) && ticks < 40) { _sim.Tick(); ticks++; }
            Check(Resolved(second), $"seed {seed} aura shift: the queued cast never resolved");

            // The hotkey of the zone ability (cell 1, W): it casts at once for the lead (its own cooldown), a zone on the lead.
            from = PendingCount();
            Push(new InputEventMouseMotion { Position = _screen / 2f });
            await Frame();
            Key(Godot.Key.W);
            sent = Pending(from);
            leadPos = U.Position[lead.Index];
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == lead && sent[0].TypeId == 1 && sent[0].Position == leadPos && !_sel.Targeting,
                $"seed {seed} aura hotkey: sent {Describe(sent)}, want one UseAbility for {lead} #1 at {leadPos}; targeting {_sel.Targeting}");
            _sim.Tick();
            _sim.Tick();
            bool zoneOnLead = false;
            for (int k = 0; k < W.Zones.Capacity; k++) if (W.Zones.Alive[k] && W.Zones.Owner[k] == 0 && W.Zones.Center[k] == leadPos) zoneOnLead = true;
            Check(zoneOnLead, $"seed {seed} aura hotkey: no own zone on the lead at {leadPos}");
            Twin(seed);

            // All four markers side by side on a Raider and on a Heavy at zoom 30 (test staging after the twin).
            await FourMarkersRow(seed, lead, data);
            GD.Print($"seed {seed}: selfAura Shift click queued one for the second Heavy; W cast the zone at once on the lead");
        }
        finally
        {
            await EndMatch();
            _data = shipped;
        }
    }

    // Burning, Slowed, Blinded and Frenzied written into the store on a Heavy and a Raider standing side by side: four markers
    // each, four distinct colours, the Frenzied one in FrenzyColor (the windowed still is the one the owner looks at).
    private async Task FourMarkersRow(ulong seed, EntityHandle heavyUnit, GameData data)
    {
        int raiderType = data.FindUnit("whirlwind_raider");
        var before = new HashSet<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(i);
        System.Numerics.Vector2 at = U.Position[heavyUnit.Index] + new System.Numerics.Vector2(2.5f, 0f);
        _sim.Enqueue(Command.SpawnUnit(1, raiderType, at));
        for (int t = 0; t < 6; t++) _sim.Tick();
        int raider = -1;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !before.Contains(i) && U.TypeId[i] == raiderType) raider = i;
        if (!Check(raider >= 0 && W.Fog.CanSeeUnit(0, raider), $"seed {seed} four markers: raider {raider}")) return;
        int frenzied = data.FindStatus("frenzied");
        foreach (int unit in new[] { heavyUnit.Index, raider })
        {
            ApplyStatus.Invoke(U.Statuses, new object[] { unit, _burning, 1f, 2400, 0 });
            ApplyStatus.Invoke(U.Statuses, new object[] { unit, _slowed, 0.3f, 2400, 0 });
            ApplyStatus.Invoke(U.Statuses, new object[] { unit, _blinded, 0f, 2400, 0 });
            ApplyStatus.Invoke(U.Statuses, new object[] { unit, frenzied, 0.5f, 2400, 0 });
        }
        System.Numerics.Vector2 mid = (U.Position[heavyUnit.Index] + U.Position[raider]) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        foreach (int unit in new[] { heavyUnit.Index, raider })
        {
            int kf = MarkIndex(unit, frenzied);
            var colours = new HashSet<Color>();
            foreach (int st in new[] { _burning, _slowed, _blinded, frenzied }) if (MarkIndex(unit, st) >= 0) colours.Add(_views.StatusColor(st));
            Check(kf >= 0 && colours.Count == 4 && _views.StatusColor(frenzied) == AbilityViews.FrenzyColor,
                $"seed {seed} four markers: unit {unit} Frenzied marker {kf}, {colours.Count} distinct colours");
        }
        await Shot($"ability-seed{seed}-frenzied");
        GD.Print($"seed {seed}: Burning, Slowed, Blinded and Frenzied side by side on Heavy {heavyUnit.Index} and Raider {raider}");
    }

    // ---- M4-V7b: the summon card, summoned views, and the hp bar's maximum (BUG-0450) ----

    private static GameData? _summonData;

    // The shipped data plus a test-only summon on the Cadre Mage (SummonTests.AddTo's pattern; no shipped ability is a
    // summon): zz_test_summon, 3 Heavy Infantry for 3 s, 0.5 s cast, 10 s cooldown, radius 0, as the mage's ability 1 (W).
    // Dryjhna's Prophecy loses its Age II requirement and takes 0.1 s, so the Prophecy row can research it by command.
    private static GameData SummonData()
    {
        if (_summonData != null) return _summonData;
        string to = CopyOfShippedData("abilityview-summon-data");
        string abilities = System.IO.Path.Combine(to, "factions", "malazan", "abilities.json");
        var root = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(abilities))!;
        root["abilities"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "zz_test_summon", "displayName": "Test Summon", "description": "Test only.", "kind": "summon",
              "castTime": 0.5, "cooldown": 10, "affects": "own_units", "autocast": false,
              "effects": [ { "kind": "spawn", "unit": "malazan_heavy_infantry", "count": 3, "lifetime": 3 } ] }
            """));
        System.IO.File.WriteAllText(abilities, root.ToJsonString());
        string units = System.IO.Path.Combine(to, "factions", "malazan", "units.json");
        var unitRoot = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(units))!;
        foreach (System.Text.Json.Nodes.JsonNode? u in unitRoot["units"]!.AsArray())
            if ((string?)u!["id"] == "malazan_cadre_mage") u["abilities"] = new System.Text.Json.Nodes.JsonArray("telas_fire", "zz_test_summon");
        System.IO.File.WriteAllText(units, unitRoot.ToJsonString());
        string techs = System.IO.Path.Combine(to, "factions", "whirlwind", "techs.json");
        var techRoot = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(techs))!;
        foreach (System.Text.Json.Nodes.JsonNode? t in techRoot["techs"]!.AsArray())
            if ((string?)t!["id"] == "dryjhnas_prophecy")
            {
                t["requires"] = new System.Text.Json.Nodes.JsonArray();
                t["researchTime"] = 0.1;
            }
        System.IO.File.WriteAllText(techs, techRoot.ToJsonString());
        DataLoadResult r = DataLoader.LoadAll(to);
        if (!r.Ok) throw new InvalidOperationException("summon test data: " + string.Join("; ", r.Errors));
        return _summonData = r.Data!;
    }

    // M4-V7b on a match whose Cadre Mages carry the test summon: two mages (a lead, a second 8 m east). The card (W, the
    // tooltip without range or radius); hovering the button draws no ring (radius 0) and arms nothing; a click on it sends
    // exactly one UseAbility at the lead's position (no targeting, one sound); the three Heavies appear at the resolve with
    // their views drawn; at their expiry the Deaths list has three Expired entries, the views go, no corpse marker is added
    // and K / L (the sim, the resource bar, the F12 overlay) stay; Shift + W (pushed through the viewport) queues one for the
    // second mage; the twin; then (staging after the twin) the BUG-0450 bar row.
    private async Task SummonRun(ulong seed)
    {
        GameData shipped = _data;
        GameData data = SummonData();
        _data = data;
        try
        {
            StartMatch(seed, data, "--units", "0", "--no-bases", "--no-combat");
            UnitViews units = _match.GetNode<UnitViews>("World3D/UnitViews");
            CombatViews combat = _match.GetNode<CombatViews>("World3D/CombatViews");
            ResourceBar bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
            int mage = data.FindUnit("malazan_cadre_mage"), heavy = data.FindUnit("malazan_heavy_infantry");
            int summon = data.FindAbility("zz_test_summon");
            AbilityDef def = data.Abilities[summon];
            int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
            System.Numerics.Vector2 c = G.CellCenter(center % G.Width, center / G.Width);
            _sim.Enqueue(Command.SpawnUnit(0, mage, c));
            _sim.Enqueue(Command.SpawnUnit(0, mage, c + new System.Numerics.Vector2(8f, 0f)));
            for (int t = 0; t < 8; t++) _sim.Tick();
            var mages = new List<EntityHandle>();
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == mage) mages.Add(new EntityHandle(i, U.Generation[i]));
            mages.Sort((a, b) => System.Numerics.Vector2.Distance(U.Position[a.Index], c).CompareTo(System.Numerics.Vector2.Distance(U.Position[b.Index], c)));
            if (!Check(mages.Count == 2 && def.Kind == AbilityKind.Summon && def.Radius == 0f && def.Range == 0f,
                $"seed {seed} summon: {mages.Count} mages staged, kind {def.Kind}, radius {def.Radius}, range {def.Range}")) return;
            EntityHandle lead = mages[0], second = mages[1];
            _camera.SetFocus(c.X + 4f, c.Y);
            await Select(lead, second);

            // The card: the summon on W, its tooltip without a range or a radius (a summon has neither).
            string tip = $"{def.Description}\n{_ui.Hud(HudText.Cooldown)} 10 {_ui.Hud(HudText.Seconds)}";
            Check(_card.AbilityAt(1) == summon && _card.GridKey(1) == "W" && _card.ButtonAt(1).TooltipText == tip,
                $"seed {seed} summon card: cell 1 {_card.AbilityAt(1)} key '{_card.GridKey(1)}', tooltip '{_card.ButtonAt(1).TooltipText}', want '{tip}'");

            // Hover: nothing armed, and no ring (radius 0).
            Vector2 button = _card.ButtonAt(1).GetGlobalRect().GetCenter();
            Push(new InputEventMouseMotion { Position = button });
            await Frames();
            _views.Sync(W, 0.5f, button, false);
            Check(_card.HoveredAbility == summon && !_views.SelfRingShown && !_views.RadiusRing.Visible && !_views.RangeRing.Visible && !_sel.Targeting,
                $"seed {seed} summon hover: hovered {_card.HoveredAbility}, ring {_views.SelfRingShown} radius {_views.RadiusRing.Visible} range {_views.RangeRing.Visible}, targeting {_sel.Targeting}");

            // A click on the button: one UseAbility for the lead at its own position, no targeting, one sound.
            await Gap();
            int from = PendingCount(), sounds = _sfx.PlayCount(SfxEvent.Command);
            LeftClick(button);
            List<Command> sent = Pending(from);
            System.Numerics.Vector2 leadPos = U.Position[lead.Index];
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == lead && sent[0].TypeId == 1 && sent[0].Position == leadPos && !sent[0].IsQueued,
                $"seed {seed} summon click: sent {Describe(sent)}, want one UseAbility for {lead} #1 at {leadPos}");
            Check(!_sel.Targeting && _sel.TargetAbility < 0 && _sfx.PlayCount(SfxEvent.Command) == sounds + 1,
                $"seed {seed} summon click: targeting {_sel.Targeting} ({_sel.TargetKind}), sounds {_sfx.PlayCount(SfxEvent.Command) - sounds}");

            // The resolve: nothing summoned before it; three Heavies with their views drawn after it.
            int[] kl = { W.Kills[0], W.Kills[1], W.Losses[0], W.Losses[1] };
            int ticks = 0, early = 0;
            bool resolved = false;
            while (ticks < 40)
            {
                _sim.Tick();
                ticks++;
                if (resolved = Resolved(lead)) break;
                if (SummonedCount() != 0) early++;
                await Frame();
            }
            int resolveTick = W.TickNumber - 1; // the tick that ran the resolve
            await Frames();
            var summoned = new List<EntityHandle>();
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.IsSummoned(i)) summoned.Add(new EntityHandle(i, U.Generation[i]));
            int expires = summoned.Count > 0 ? U.ExpiresAtTick[summoned[0].Index] : -1;
            bool drawn = summoned.All(h => units.IsShown(h.Index) && units.ViewOf(h.Index) is { Visible: true });
            if (!Check(resolved, $"seed {seed} summon: no resolve in {ticks} ticks")) return;
            Check(early == 0 && summoned.Count == 3 && summoned.All(h => U.TypeId[h.Index] == heavy && U.Owner[h.Index] == 0 && U.ExpiresAtTick[h.Index] == expires) && drawn
                && expires == resolveTick + 60,
                $"seed {seed} summon resolve (tick {resolveTick}): {early} ticks with a summon before it, {summoned.Count} summoned, views drawn {drawn}, expiry tick {expires} (want {resolveTick + 60}: 3 s)");
            GD.Print($"seed {seed}: summon click: one UseAbility at the lead's position; resolved on tick {resolveTick}, 3 Heavies drawn, expiring on {expires}");
            await Shot($"ability-seed{seed}-summoned");

            // The expiry: three Expired deaths, no killer; the views go; no corpse marker; K / L stay.
            long markersBefore = combat.Markers.Added;
            int shownBefore = 0, expired = 0, expiredOn = -1;
            while (summoned.Any(h => U.IsAlive(h)) && ticks++ < 200)
            {
                int running = W.TickNumber;
                _sim.Tick();
                int e = ExpiredAmong(summoned);
                if (e > 0) expiredOn = running;
                expired += e;
                await Frame();
                if (summoned.All(h => U.IsAlive(h) && units.IsShown(h.Index))) shownBefore++;
            }
            await Frames();
            bool gone = summoned.All(h => !U.IsAlive(h) && !units.IsShown(h.Index) && units.ViewOf(h.Index) is not { Visible: true });
            int corpses = 0;
            for (int k = 0; k < combat.Markers.Capacity; k++) if (combat.MarkerShown(k) != 0) corpses++;
            Check(expired == 3 && expiredOn == expires && gone && combat.Markers.Added == markersBefore && corpses == 0,
                $"seed {seed} summon expiry (tick {expiredOn}, want {expires}): {expired} Expired deaths, views gone {gone}, markers +{combat.Markers.Added - markersBefore}, {corpses} drawn");
            Check(shownBefore >= 50 && W.Kills[0] == kl[0] && W.Kills[1] == kl[1] && W.Losses[0] == kl[2] && W.Losses[1] == kl[3]
                && bar.ShownKills == W.Kills[0] && bar.ShownLosses == W.Losses[0] && _overlay.Kills0 == kl[0] && _overlay.Losses0 == kl[2] && _overlay.Kills1 == kl[1] && _overlay.Losses1 == kl[3],
                $"seed {seed} summon expiry: drawn on {shownBefore} frames before it; K / L sim {W.Kills[0]}/{W.Losses[0]} {W.Kills[1]}/{W.Losses[1]} (was {string.Join(",", kl)}), bar {bar.ShownKills}/{bar.ShownLosses}, overlay {_overlay.Kills0}/{_overlay.Losses0}");
            GD.Print($"seed {seed}: summon expiry on tick {expiredOn}: 3 Expired deaths, views gone, 0 corpse markers, K / L unchanged; drawn on {shownBefore} frames before it");

            // Shift + W through the viewport, the lead on cooldown: one queued UseAbility for the second mage at its position.
            Push(new InputEventMouseMotion { Position = _screen / 2f });
            await Gap();
            from = PendingCount();
            sounds = _sfx.PlayCount(SfxEvent.Command);
            Input.ActionPress("order_queue");
            PushKey(Godot.Key.W);
            Input.ActionRelease("order_queue");
            sent = Pending(from);
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && sent[0].Unit == second && sent[0].TypeId == 1 && sent[0].IsQueued
                && sent[0].Position == U.Position[second.Index] && !_sel.Targeting && _sfx.PlayCount(SfxEvent.Command) == sounds + 1,
                $"seed {seed} summon Shift + W: sent {Describe(sent)}, want one queued UseAbility for {second} #1; targeting {_sel.Targeting}");
            ticks = 0;
            while (!Resolved(second) && ticks < 40) { _sim.Tick(); ticks++; }
            await Frames();
            int second3 = 0;
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.IsSummoned(i) && units.IsShown(i)) second3++;
            Check(Resolved(second) && second3 == 3, $"seed {seed} summon Shift + W: resolved {Resolved(second)}, {second3} summoned drawn");
            Twin(seed);

            await ProphecyBarRow(seed, data, units, combat, c);
        }
        finally
        {
            await EndMatch();
            _data = shipped;
        }
    }

    // BUG-0450 on screen (staging after the twin): Whirlwind researches Dryjhna's Prophecy (a Smithy and a Research command,
    // the test data's 0.1 s), two fresh Zealots of theirs by our mages spawn at 83 / 83; one is set to 70 before its view's
    // first frame. It flashes on first sight and gets a bar of 70 / 83; the one at 83 / 83 neither flashes nor has a bar.
    private async Task ProphecyBarRow(ulong seed, GameData data, UnitViews units, CombatViews combat, System.Numerics.Vector2 c)
    {
        int zealot = data.FindUnit("whirlwind_zealot"), prophecy = data.FindTech("dryjhnas_prophecy");
        int smithy = StartBase.BuildingOfSlot(data, W.FactionOf(1), BuildingSlot.Forge);
        // BUG-0711: seat 1 owns no unit in this match, and a player that has built with no live unit and no unit-producing
        // building is out (M5-4e), its Research dropped. A Camp Follower on the passable cell farthest from the mage keeps
        // seat 1 in, out of the row's spots and the mages' sight.
        int keeperCell = -1;
        float keeperDist = -1f;
        for (int y = 0; y < G.Height; y++)
            for (int x = 0; x < G.Width; x++)
            {
                if (!G.IsPassable(x, y) || W.Buildings.SlotAt(x, y) >= 0) continue;
                float d = System.Numerics.Vector2.Distance(G.CellCenter(x, y), c);
                if (d > keeperDist) { keeperDist = d; keeperCell = y * G.Width + x; }
            }
        if (!Check(keeperCell >= 0, $"seed {seed} prophecy: no cell for seat 1's keeper")) return;
        int keepers = 0;
        _sim.Enqueue(Command.SpawnUnit(1, data.FindUnit("whirlwind_camp_follower"), G.CellCenter(keeperCell % G.Width, keeperCell / G.Width)));
        for (int t = 0; t < 3; t++) _sim.Tick();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 1) keepers++;
        if (!Check(keepers >= 1, $"seed {seed} prophecy: seat 1's keeper did not spawn")) return;
        int site = -1;
        for (int k = 0; k < 16 && site < 0; k++)
        {
            float a = k * MathF.PI / 8f;
            System.Numerics.Vector2 p = c + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * 24f;
            int cell = FlowField.NearestPassable(G, Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, G.Height - 1) * G.Width + Math.Clamp((int)(p.X / MapConstants.CellSize), 0, G.Width - 1));
            _sim.Enqueue(Command.SpawnBuilding(1, smithy, G.CellCenter(cell % G.Width, cell / G.Width)));
            _sim.Tick();
            _sim.Tick();
            for (int b = 0; b < W.Buildings.Capacity; b++) if (W.Buildings.Alive[b] && W.Buildings.Owner[b] == 1 && W.Buildings.TypeId[b] == smithy) site = b;
        }
        if (!Check(site >= 0, $"seed {seed} prophecy: no spot for the Smithy")) return;
        _sim.Enqueue(Command.Research(1, SelectionController.SiteCenter(W, site), prophecy));
        for (int t = 0; t < 20 && W.MaxHpOf(1, zealot) != 83; t++) _sim.Tick();
        if (!Check(W.MaxHpOf(1, zealot) == 83 && data.Units[zealot].Hp == 63, $"seed {seed} prophecy: Zealot max {W.MaxHpOf(1, zealot)}, type hp {data.Units[zealot].Hp}")) return;

        var before = new HashSet<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) before.Add(new EntityHandle(i, U.Generation[i]));
        // Two free cells 3-6 m from the mage at `c`, on its level (a Zealot below a rise, or above it, may be out of its sight).
        int level = G.LevelAt((int)(c.X / MapConstants.CellSize), (int)(c.Y / MapConstants.CellSize));
        var spots = new List<System.Numerics.Vector2>();
        for (int y = 0; y < G.Height; y++)
            for (int x = 0; x < G.Width; x++)
            {
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                float d = System.Numerics.Vector2.Distance(p, c);
                if (d < 3f || d > 6f || !G.IsPassable(x, y) || G.LevelAt(x, y) != level || W.Buildings.SlotAt(x, y) >= 0) continue;
                bool clear = true;
                for (int i = 0; i < U.Capacity && clear; i++) if (U.Alive[i] && System.Numerics.Vector2.Distance(U.Position[i], p) < 1.5f) clear = false;
                if (clear && spots.All(q => System.Numerics.Vector2.Distance(q, p) >= 2.5f)) spots.Add(p);
            }
        if (!Check(spots.Count >= 2, $"seed {seed} prophecy: {spots.Count} free spots by the mage")) return;
        _sim.Enqueue(Command.SpawnUnit(1, zealot, spots[0]));
        _sim.Enqueue(Command.SpawnUnit(1, zealot, spots[1]));
        _sim.Tick();
        _sim.Tick(); // a command applies on the tick after the one it was enqueued before
        var fresh = new List<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == zealot && !before.Contains(new EntityHandle(i, U.Generation[i]))) fresh.Add(i);
        if (!Check(fresh.Count == 2 && fresh.All(i => U.Hp[i] == 83), $"seed {seed} prophecy: {fresh.Count} fresh Zealots, hp {string.Join(",", fresh.Select(i => U.Hp[i]))}")) return;
        int hurt = fresh[0], full = fresh[1];
        U.Hp[hurt] = 70; // before any view frame saw it
        await Frame();
        Check(units.Flash.IsLit(hurt) && !units.Flash.IsLit(full), $"seed {seed} prophecy flash: 70 / 83 lit {units.Flash.IsLit(hurt)}, 83 / 83 lit {units.Flash.IsLit(full)}");
        // Seen (a fog update), then the bars: 70 / 83 has one, 83 / 83 none.
        for (int t = 0; t <= Rts.Sim.Vision.VisionConstants.UpdateInterval; t++) _sim.Tick();
        await Frames();
        int barHurt = -1, barFull = -1;
        for (int k = 0; k < combat.ShownBars; k++)
        {
            if (combat.BarSlot(k) == hurt) barHurt = k;
            if (combat.BarSlot(k) == full) barFull = k;
        }
        bool seen = W.Fog.CanSeeUnit(0, hurt) && W.Fog.CanSeeUnit(0, full);
        Check(seen && barHurt >= 0 && Math.Abs(combat.BarFill(barHurt) - 70f / 83f) < 1e-5f && barFull < 0 && U.Hp[full] == 83 && W.MaxHp(hurt) == 83,
            $"seed {seed} prophecy bars: seen {seen}, 70 / 83 bar {barHurt} fill {(barHurt >= 0 ? combat.BarFill(barHurt) : -1)} (want {70f / 83f}), 83 / 83 bar {barFull}");
        GD.Print($"seed {seed}: Prophecy Zealots: 70 / 83 flashed on first sight and has a bar of {(barHurt >= 0 ? combat.BarFill(barHurt) : -1):0.000}; 83 / 83 neither");
    }

    private int SummonedCount()
    {
        int n = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.IsSummoned(i)) n++;
        return n;
    }

    // The last tick's expiries (Expired, no killer) of units in `units`.
    private int ExpiredAmong(List<EntityHandle> units)
    {
        int n = 0;
        foreach (Rts.Sim.Combat.DeathEvent d in W.Deaths)
            if (d.Expired && d.KillerOwner == -1 && units.Contains(d.Victim)) n++;
        return n;
    }

    // A key pressed and released through the viewport (the player's path: the card's grid key, M4-V7b).
    private void PushKey(Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    // ---- 300 steady frames at --units 500 ----

    private async Task Steady(ulong seed)
    {
        StartMatch(seed, "--units", "500");
        _sim.Tick();
        _sim.Tick();
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
        _sim.Tick();
        int mageType = _data.FindUnit("malazan_cadre_mage");
        _sel.Selection.Clear();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == mageType) _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
        int mages = _sel.Selection.Count;
        await Frames();
        int casts = 0, armedFrames = 0, barFrames = 0, ringFrames = 0;
        // Warm-up: views, card and rings through a few hundred ticks (meshes, pools, the JIT); every 20 ticks two mages cast.
        for (int t = 0; t < 240; t++)
        {
            if (t % 20 == 0)
                for (int c = 0; c < 2; c++)
                    if (_sel.AbilityOrder(_telas, BesideMage(t / 20 * 2 + c), false)) casts++;
            _sim.Tick();
            if (!_sel.Targeting) _sel.BeginAbility(_telas);
            SyncAll(0.5f, t);
        }
        // The twin covers the commands so far; the 100 statuses below are written into the store (test staging).
        Twin(seed);
        int statused = 0;
        for (int i = 0; i < U.Capacity && statused < 100; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0) continue;
            ApplyStatus.Invoke(U.Statuses, new object[] { i, statused % 2 == 0 ? _burning : _slowed, statused % 2 == 0 ? 1f : 0.3f, 2400, 0 });
            statused++;
        }
        StageZones();
        for (int f = 0; f < 3; f++) SyncAll(0.5f, f);
        int firstMarkers = _views.ShownMarkers, markerFrames = 0, zoneFrames = 0, panelRebuilds = _panel.Rebuilds;
        long bytes = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            if (f % 30 == 0 && _sel.AbilityOrder(_telas, BesideMage(f), false)) casts++; // outside the measured span
            if (!_sel.Targeting) _sel.BeginAbility(_telas);
            long before = GC.GetAllocatedBytesForCurrentThread();
            SyncAll(f % 3 / 3f, f);
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            if (spent != 0) GD.Print($"steady frame {f}: {spent} bytes (views {_spent[0]}, card {_spent[1]}, panel {_spent[2]})");
            bytes += spent;
            if (_views.TargetingShown) armedFrames++;
            if (_views.ShownBars > 0) barFrames++;
            if (_views.ShownCircles > 0) ringFrames++;
            if (_views.ShownMarkers > 0) markerFrames++;
            if (_views.ShownZones == 8) zoneFrames++;
        }
        GD.Print($"steady (seed {seed}, {U.Count} units, {mages} mages selected, {casts} casts, {statused} statuses, {firstMarkers} markers at the start, {W.Zones.Count} zones ({W.Zones.BlockerCount} blocking), discs in {zoneFrames} frames): 300 frames, armed {armedFrames}, bars {barFrames}, cast rings {ringFrames}, markers {markerFrames}, panel rewrites {_panel.Rebuilds - panelRebuilds}, panel '{_panel.OverflowLabel.Text}': {bytes} bytes");
        Check(mages >= 2 && casts >= 4, $"steady: {mages} mages, {casts} casts");
        Check(armedFrames > 200 && ringFrames > 0 && barFrames > 0, $"steady: rings armed in {armedFrames} frames, cast rings in {ringFrames} (the 0 bytes must cover them)");
        Check(statused == 100 && firstMarkers >= 100 && markerFrames == 300, $"steady: {statused} statuses staged, {firstMarkers} markers drawn, markers in {markerFrames} frames");
        Check(zoneFrames == 300, $"steady: 8 zone discs drawn in {zoneFrames} of 300 frames ({W.Zones.Count} live, {W.Zones.BlockerCount} blocking)");
        Check(bytes == 0, $"steady: 300 frames at --units 500 allocated {bytes} bytes");
        PoolRow();
        StableKeysRow();
        ReclaimCostRow();
        MinimapRightClicks();
        await EndMatch();
    }

    // M5-V1 (BUG-0520): the cast-point rings and the resolve flashes come from GroundDisc pools. Two rounds in which 12 ready
    // selected mages each cast beside themselves in one frame (12 rings at once, then their flashes): the first warms the
    // pools; over the second's 120 frames the views allocate 0 B and neither pool makes a node.
    private void PoolRow()
    {
        _sel.CancelTargeting();
        var ready = new List<EntityHandle>();
        int[] sent = new int[2], rings = new int[2], flashes = new int[2];
        int ringNodes = 0, flashNodes = 0;
        long bytes = 0;
        for (int round = 0; round < 2; round++)
        {
            ready.Clear();
            foreach (EntityHandle h in _sel.Selection.Items) if (OffCooldown(h)) ready.Add(h);
            foreach (EntityHandle h in ready)
                if (sent[round] < 12 && _sel.AbilityOrder(_telas, U.Position[h.Index] + new System.Numerics.Vector2(3f, 0f), false)) sent[round]++;
            if (round == 1)
            {
                ringNodes = _views.CastRingPool.Nodes;
                flashNodes = _views.FlashPool.Nodes;
            }
            for (int f = 0; f < 120; f++)
            {
                if (f % 2 == 0) _sim.Tick();
                long before = GC.GetAllocatedBytesForCurrentThread();
                _views.Sync(W, f % 2 / 2f, Vector2.Zero, false);
                long spent = GC.GetAllocatedBytesForCurrentThread() - before;
                if (round == 1) bytes += spent;
                rings[round] = Math.Max(rings[round], _views.ShownCircles);
                flashes[round] = Math.Max(flashes[round], _views.ShownFlashes);
            }
        }
        GD.Print($"pool: rounds of {sent[0]} and {sent[1]} casts: at most {rings[0]} / {rings[1]} rings and {flashes[0]} / {flashes[1]} flashes at once; pools {_views.CastRingPool.Nodes} ring and {_views.FlashPool.Nodes} flash nodes (were {ringNodes} / {flashNodes}); {bytes} B over the second round's 120 frames");
        Check(sent[0] == 12 && sent[1] == 12 && rings[1] >= 10 && flashes[1] >= 10, $"pool: sent {sent[0]} / {sent[1]}, rings {rings[1]}, flashes {flashes[1]} at once in round 2");
        Check(bytes == 0 && _views.CastRingPool.Nodes == ringNodes && _views.FlashPool.Nodes == flashNodes,
            $"pool: {bytes} B over the second round; ring nodes {ringNodes} -> {_views.CastRingPool.Nodes}, flash nodes {flashNodes} -> {_views.FlashPool.Nodes}");
    }

    // BUG-0662: in a full pool a new low key takes a stale node (a skipped lower key's, else the highest untaken key's), so the
    // stable keys keep their nodes and only the new key's mesh is built. Frame 1: keys 10-13 in 4 nodes; frame 2: 5, 10, 11, 12
    // (13 gone): 5 takes 13's node, nobody moves. Frame 3: 3, 5, 10, 11, 12 (3 new, no stale node): 3 takes the node of the
    // highest key not taken yet (12, which comes last and then grows the pool), so 5, 10 and 11 keep theirs: one extra mesh,
    // not the old cascade of every stable key moving one node along. (Growing instead would break QaM5V1's 16-for-16 swap.)
    private void StableKeysRow()
    {
        var holder = new Node3D { Name = "StableKeysPool" };
        AddChild(holder);
        var pool = new GroundDiscPool(holder, "StableDisc", 64, 4);
        var mid = new System.Numerics.Vector2(W.Heightmap.Width * 0.5f, W.Heightmap.Height * 0.5f);
        int Frame(int[] keys)
        {
            int rebuilds = 0;
            for (int n = 0; n < pool.Nodes; n++) rebuilds -= pool.Node(n).Rebuilds;
            pool.Begin();
            foreach (int k in keys)
            {
                GroundDisc d = pool.Take(k);
                d.Place(W.Heightmap, mid + new System.Numerics.Vector2(k * 3f, 0f), 1.5f, 0f, 0.1f);
                d.Visible = true;
            }
            pool.End();
            for (int n = 0; n < pool.Nodes; n++) rebuilds += pool.Node(n).Rebuilds;
            return rebuilds;
        }
        Frame(new[] { 10, 11, 12, 13 });
        GroundDisc?[] held = { pool.NodeOf(10), pool.NodeOf(11), pool.NodeOf(12) };
        int second = Frame(new[] { 5, 10, 11, 12 });
        int kept2 = 0;
        for (int k = 0; k < 3; k++) if (ReferenceEquals(pool.NodeOf(10 + k), held[k])) kept2++;
        GroundDisc? five = pool.NodeOf(5);
        int third = Frame(new[] { 3, 5, 10, 11, 12 });
        int kept3 = ReferenceEquals(pool.NodeOf(5), five) ? 1 : 0;
        for (int k = 0; k < 2; k++) if (ReferenceEquals(pool.NodeOf(10 + k), held[k])) kept3++;
        GD.Print($"pool stable keys: a new key 5 kept {kept2} of 3 stable nodes ({second} rebuilds); a new key 3 next kept {kept3} of 3 below the highest ({third} rebuilds), pool {pool.Nodes} nodes");
        Check(kept2 == 3 && second == 1 && pool.NodeOf(13) == null, $"BUG-0662: a new key 5 kept {kept2} of 3 stable keys' nodes, {second} mesh rebuilds (want 3, 1)");
        Check(kept3 == 3 && third == 2 && pool.Nodes == 8, $"BUG-0662: a new key 3 in a full pool kept {kept3} of keys 5, 10, 11's nodes, {third} mesh rebuilds, {pool.Nodes} nodes (want 3, 2, 8)");
        pool.Free();
        RemoveChild(holder);
        holder.QueueFree();
    }

    // BUG-0684 item 4: a frame whose keys are all new in a full pool reclaims each node without scanning the whole pool per
    // key. 1,024 keys held, then 1,024 others below them (every key reclaims the highest untaken key's node), then 1,024 above
    // (every key reclaims a lower, skipped key's node): the node visits stay within a few per node and key, and the pool keeps
    // its 1,024 nodes (it was O(nodes x keys): about a million visits for the first swap).
    private void ReclaimCostRow()
    {
        var holder = new Node3D { Name = "ReclaimCostPool" };
        AddChild(holder);
        const int n = 1024;
        var pool = new GroundDiscPool(holder, "CostDisc", 4 * n, n);
        void Frame(int from)
        {
            pool.Begin();
            for (int k = from; k < from + n; k++) pool.Take(k);
            pool.End();
        }
        Frame(2 * n);
        long before = pool.Scanned;
        Frame(0);
        long below = pool.Scanned - before;
        before = pool.Scanned;
        Frame(3 * n);
        long above = pool.Scanned - before;
        GD.Print($"pool reclaim cost: {n} new keys below the held ones visited {below} nodes, {n} above {above}; pool {pool.Nodes} nodes");
        Check(below <= 4 * n && above <= 4 * n && pool.Nodes == n && pool.InUse == n,
            $"BUG-0684 item 4: a frame of {n} new keys visited {below} (below) and {above} (above) nodes, want at most {4 * n} each; pool {pool.Nodes} nodes, {pool.InUse} in use (want {n})");
        pool.Free();
        RemoveChild(holder);
        holder.QueueFree();
    }

    // Test staging after the twin (M4-V6c): 8 zones over the two armies, alternating owners, two of them Sandstorms (blocking)
    // and six a Sandstorm copy that does not block (no shipped zone ability is non-blocking), made through the sim's own
    // ZoneSystem.Create; each lasts 240 ticks, past the 100 ticks the measured frames run.
    private void StageZones()
    {
        MethodInfo create = typeof(Rts.Sim.Abilities.ZoneSystem).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
        AbilityDef storm = _data.Abilities[_sandstorm];
        var dust = (AbilityDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(storm, null)!;
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(dust,
            storm.Effects.SetItem(storm.ZoneEffect, storm.Effects[storm.ZoneEffect] with { BlocksVision = false }));
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1), mid = (west + east) / 2f;
        for (int k = 0; k < 8; k++)
        {
            // Own zones anywhere; the enemy's on player 0's army, where it sees them.
            System.Numerics.Vector2 at = (k & 1) == 0 ? mid + new System.Numerics.Vector2(0f, 14f * (k - 3)) : west + new System.Numerics.Vector2(8f * (k - 4), 6f);
            create.Invoke(null, new object[] { W, k & 1, k < 2 ? storm : dust, at });
        }
        for (int t = 0; t < 8; t++) _sim.Tick(); // a fog update or two with the zones in place
        Check(W.Zones.Count == 8 && W.Zones.BlockerCount == 2, $"steady: staged {W.Zones.Count} zones, {W.Zones.BlockerCount} blocking");
    }

    // BUG-0340: a right-click on the minimap (a Move for the 72 selected mages) allocates nothing once warm.
    private void MinimapRightClicks()
    {
        _sel.CancelTargeting();
        MinimapTransform fit = _mini.Fit;
        System.Numerics.Vector2 west = Centroid(0);
        var events = new InputEventMouseButton[12];
        for (int k = 0; k < events.Length; k++)
        {
            System.Numerics.Vector2 mp = fit.ToPixel(west + new System.Numerics.Vector2(-6f - k, 4f));
            events[k] = new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(mp.X, mp.Y) };
        }
        int moves = _sel.IssuedCount(CommandKind.Move);
        for (int k = 0; k < 4; k++) { _mini._GuiInput(events[k]); _sim.Tick(); } // warm-up
        long bytes = 0;
        for (int k = 4; k < events.Length; k++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _mini._GuiInput(events[k]);
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            _sim.Tick();
        }
        int sent = _sel.IssuedCount(CommandKind.Move) - moves;
        GD.Print($"minimap right-clicks: {events.Length} clicks, {sent} Moves issued, {bytes} bytes over the last {events.Length - 4}");
        Check(sent >= events.Length && bytes == 0, $"minimap right-click: {sent} Moves, {bytes} bytes");
    }

    // One frame of the ability views (cursor sweeping the play area), the card and the selection panel (back in the span
    // since its "+N" strings are cached, BUG-0340), as their _Process would run them.
    private void SyncAll(float alpha, int frame)
    {
        var mouse = new Vector2(_screen.X * (0.15f + 0.7f * (frame % 50) / 50f), _screen.Y * 0.4f);
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        _views.Sync(W, alpha, mouse, false);
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        _card.Sync();
        long b2 = GC.GetAllocatedBytesForCurrentThread();
        _panel.Sync();
        _spent[0] = b1 - b0;
        _spent[1] = b2 - b1;
        _spent[2] = GC.GetAllocatedBytesForCurrentThread() - b2;
    }

    // The last SyncAll's bytes per part (views, card, panel), for the failure line.
    private readonly long[] _spent = new long[3];

    // A point 3 m from the k-th live selected mage (so the nearest ready one casts at once, a bar and a ring drawn).
    private System.Numerics.Vector2 BesideMage(int k)
    {
        ReadOnlySpan<EntityHandle> sel = _sel.Selection.Items;
        for (int n = 0; n < sel.Length; n++)
        {
            EntityHandle h = sel[(k + n) % sel.Length];
            if (U.IsAlive(h)) return U.Position[h.Index] + new System.Numerics.Vector2(3f, 0f);
        }
        return Centroid(0);
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return sum / Math.Max(1, n);
    }

    // ---- helpers ----

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        int uses = replay.Commands.Count(c => c.Kind == CommandKind.UseAbility);
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands ({uses} UseAbility), {replay.Checkpoints.Length} checkpoints equal");
    }

    private System.Numerics.Vector2 ResolvePoint(EntityHandle caster)
    {
        foreach (Rts.Sim.Abilities.AbilityEvent e in W.AbilityEvents)
            if (e.Caster == caster && e.Resolved) return e.Point;
        return new System.Numerics.Vector2(float.NaN, float.NaN);
    }

    private bool Resolved(EntityHandle caster)
    {
        foreach (Rts.Sim.Abilities.AbilityEvent e in W.AbilityEvents)
            if (e.Caster == caster && e.Resolved) return true;
        return false;
    }

    private async Task Select(params EntityHandle[] units)
    {
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
    }

    private int PendingCount() => ((CommandQueue)CommandsField.GetValue(_sim)!).Count;

    private List<Command> Pending(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private static string Describe(List<Command> cmds) =>
        "[" + string.Join(", ", cmds.Select(c => $"{c.Kind} u{c.Unit.Index} #{c.TypeId} {c.Position}{(c.IsQueued ? " queued" : "")}")) + "]";

    // Away from the HUD panels (minimap bottom-left, panel and card along the bottom, resource bar top right).
    private bool InPlayArea(Vector2 p) => p.X > 30 && p.X < _screen.X - 30 && p.Y > 60 && p.Y < _screen.Y - 270;

    private int UnitUnder(Vector2 px)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        return UnitPicker.PickRay(U.Alive, U.PrevPosition, U.Position, U.Radius, W.Heightmap, (float)_runner.Alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out _);
    }

    // A Raider whose body centre pixel is in the play area and shows that Raider first (EnemyAt agrees).
    private bool TryEnemyPixel(out EntityHandle enemy, out Vector2 px)
    {
        foreach (EntityHandle r in _raiders)
        {
            if (!U.IsAlive(r) || !_sel.TryScreenPosition(r.Index, out px) || !InPlayArea(px) || UnitUnder(px) != r.Index) continue;
            if (!_sel.EnemyAt(px, out enemy, out bool b) || b || enemy != r) continue;
            return true;
        }
        enemy = default;
        px = default;
        return false;
    }

    private bool TryGroundPixel(out Vector2 px)
    {
        for (int k = 0; k < 200; k++)
        {
            px = new Vector2(_screen.X / 2 + (k % 20 - 10) * 18f, _screen.Y / 3 + (k / 20 - 5) * 14f);
            if (!InPlayArea(px) || UnitUnder(px) >= 0) continue;
            if (_sel.ContextTarget(px, out _, out int b) && b < 0) return true;
        }
        px = default;
        return false;
    }

    // Windowed only: the frame with the F12 layers off, saved as <name>.png in --shots.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        _overlay.SetEnabled(false);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        _overlay.SetEnabled(true);
        GD.Print($"screenshot {path}");
    }

    private string Label() => _match.GetNode<Label>("DebugOverlay/Label").Text;

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void LeftClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void RightClick(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    // The Command sound has a 50 ms gap (Sfx.MinGapMs): wait past it so the next row's sound counts.
    private async Task Gap()
    {
        await WallClock.Wait(this, 70);
        await Frame();
    }

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
