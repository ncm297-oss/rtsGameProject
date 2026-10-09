using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
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
/// </summary>
/// <remarks>Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/AbilityViewTest.tscn</c> (seeds 1 and 6; <c>-- --seed N</c> for one); prints "ABILITY VIEW TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves <c>ability-seedN-targeting.png</c> and <c>ability-seedN-casting.png</c>.</remarks>
public partial class AbilityViewTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;

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
    private int _telas;
    private AbilityDef _def = null!;
    private EntityHandle _near, _far, _spotter;
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
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
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
        return knot;
    }

    private async Task Run(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat");
        System.Numerics.Vector2 knot = Stage();
        for (int t = 0; t < 8; t++) _sim.Tick(); // spawned, and a fog update shows the Raiders
        _raiders.Clear();
        var mages = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.Owner[i] == 1) _raiders.Add(h);
            else if (U.TypeId[i] == _data.FindUnit("malazan_cadre_mage")) mages.Add(h);
            else _spotter = h;
        }
        if (!Check(_raiders.Count == 4 && mages.Count == 2, $"seed {seed}: staged {_raiders.Count} Raiders, {mages.Count} mages")) { await EndMatch(); return; }
        mages.Sort((a, b) => System.Numerics.Vector2.Distance(U.Position[a.Index], knot).CompareTo(System.Numerics.Vector2.Distance(U.Position[b.Index], knot)));
        (_near, _far) = (mages[0], mages[1]);
        System.Numerics.Vector2 mid = (U.Position[_near.Index] + knot) / 2f;
        _camera.SetFocus(mid.X, mid.Y);
        await Frames();

        await CardRows(seed);
        await CancelRows(seed);
        await NearestRows(seed);
        await CastRows(seed);
        await OneOnCooldownShift(seed);
        await BothOnCooldown(seed);
        Twin(seed);
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
        _views.Sync(W, 0.5f, px, false);
        Check(_views.ShownBars == 0 && _views.ShownCircles == 0, $"seed {seed} cast: {_views.ShownBars} bars, {_views.ShownCircles} rings after the resolve");
        await Frames();
        Check(_card.AbilitySecondsAt(0) == 25 && _card.ButtonAt(0).Disabled, $"seed {seed} cast: the caster's button shows {_card.AbilitySecondsAt(0)} s");
        GD.Print($"seed {seed}: one UseAbility for mage {_near.Index}, walked {walked} ticks, cast, {burning} Raiders burning, button 25 s");
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
        long bytes = 0;
        for (int f = 0; f < 300; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            if (f % 30 == 0 && _sel.AbilityOrder(_telas, BesideMage(f), false)) casts++; // outside the measured span
            if (!_sel.Targeting) _sel.BeginAbility(_telas);
            long before = GC.GetAllocatedBytesForCurrentThread();
            SyncAll(f % 3 / 3f, f);
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            if (spent != 0) GD.Print($"steady frame {f}: {spent} bytes (views {_spent[0]}, card {_spent[1]})");
            bytes += spent;
            if (_views.TargetingShown) armedFrames++;
            if (_views.ShownBars > 0) barFrames++;
            if (_views.ShownCircles > 0) ringFrames++;
        }
        GD.Print($"steady (seed {seed}, {U.Count} units, {mages} mages selected, {casts} casts): 300 frames, armed {armedFrames}, bars {barFrames}, cast rings {ringFrames}: {bytes} bytes");
        Check(mages >= 2 && casts >= 4, $"steady: {mages} mages, {casts} casts");
        Check(armedFrames > 200 && ringFrames > 0 && barFrames > 0, $"steady: rings armed in {armedFrames} frames, cast rings in {ringFrames} (the 0 bytes must cover them)");
        Check(bytes == 0, $"steady: 300 frames at --units 500 allocated {bytes} bytes");
        Twin(seed);
        await EndMatch();
    }

    // One frame of the ability views (cursor sweeping the play area) and the card, as their _Process would run them. The
    // selection panel is left out: its "+N" overflow line allocates when a selected unit dies (BUG-0340, not this task's).
    private void SyncAll(float alpha, int frame)
    {
        var mouse = new Vector2(_screen.X * (0.15f + 0.7f * (frame % 50) / 50f), _screen.Y * 0.4f);
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        _views.Sync(W, alpha, mouse, false);
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        _card.Sync();
        _spent[0] = b1 - b0;
        _spent[1] = GC.GetAllocatedBytesForCurrentThread() - b1;
    }

    // The last SyncAll's bytes per part (views, card), for the failure line.
    private readonly long[] _spent = new long[2];

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
