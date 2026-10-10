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
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/AbilityViewTest.tscn</c> (seeds 1 and 6; <c>-- --seed N</c> for one); prints "ABILITY VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves <c>ability-seedN-targeting.png</c>, <c>ability-seedN-casting.png</c>, <c>ability-seedN-first-tick.png</c>, <c>ability-seedN-flash.png</c>, <c>ability-seedN-burning.png</c>, <c>ability-seedN-cusser.png</c> and <c>ability-seedN-statuses.png</c>.</remarks>
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
    private int _telas, _cusser, _burning, _slowed;
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
            UiRows();
            foreach (ulong seed in seeds) await Run(seed);
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

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--debug-overlay", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
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
        for (int f = 0; f < 3; f++) SyncAll(0.5f, f);
        int firstMarkers = _views.ShownMarkers, markerFrames = 0, panelRebuilds = _panel.Rebuilds;
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
        }
        GD.Print($"steady (seed {seed}, {U.Count} units, {mages} mages selected, {casts} casts, {statused} statuses, {firstMarkers} markers at the start): 300 frames, armed {armedFrames}, bars {barFrames}, cast rings {ringFrames}, markers {markerFrames}, panel rewrites {_panel.Rebuilds - panelRebuilds}, panel '{_panel.OverflowLabel.Text}': {bytes} bytes");
        Check(mages >= 2 && casts >= 4, $"steady: {mages} mages, {casts} casts");
        Check(armedFrames > 200 && ringFrames > 0 && barFrames > 0, $"steady: rings armed in {armedFrames} frames, cast rings in {ringFrames} (the 0 bytes must cover them)");
        Check(statused == 100 && firstMarkers >= 100 && markerFrames == 300, $"steady: {statused} statuses staged, {firstMarkers} markers drawn, markers in {markerFrames} frames");
        Check(bytes == 0, $"steady: 300 frames at --units 500 allocated {bytes} bytes");
        MinimapRightClicks();
        await EndMatch();
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
