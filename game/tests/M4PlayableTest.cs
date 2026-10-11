using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;

namespace Rts.Game.Tests;

/// <summary>
/// M4-V7 slice 1: M4's "sandbox Playable" criterion (docs/05 M4 criterion 10) as a script, after <see cref="M3PlayableTest"/>'s
/// pattern. A real <see cref="Match"/> (shipped data, the fog of war on and drawn, 8x game speed, the sim ticked by its own
/// <see cref="SimRunner"/> from frame time) on seeds 1 and 6, staged with the dev spawn commands (they ignore requirements
/// and costs): each side's Town Hall (the match's start base) plus three buildings on its start plateau and an Age-II-style
/// army in front of it. Every action of the local player is injected input on the viewport (box drags, clicks, grid keys,
/// Tab, minimap clicks to move the camera), never a controller call; the enemy has no AI before M5, so its only orders
/// (the Zealot duel's two Attacks) are staging commands, like the spawns. Each step is checked against the sim and prints
/// the tick it completed at. Every frame of the whole run checks the fog rule: no enemy unit view, hp bar, status marker or
/// cast bar is drawn while <c>Fog.CanSeeUnit</c> is false for its unit, and no enemy building view while
/// <c>CanSeeBuilding</c> is false (the counters are printed). At the end the whole command stream is replayed into a bare
/// twin sim (<see cref="ReplayPlayer"/>, a checkpoint every tick): equal hashes prove the views and the HUD only read the sim.
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/M4PlayableTest.tscn</c> (both seeds) or
/// <c>... -- --seed 6</c> for one. Prints "M4 PLAYABLE TEST PASS" and exits 0, else "M4 PLAYABLE TEST FAIL &lt;step&gt;: &lt;what&gt;"
/// and exits 1. <c>-- --break &lt;step number&gt;</c> makes that step expect the wrong answer once, to show a failure names its
/// step. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>m4-seedN-first-contact.png</c>, <c>-storm.png</c>,
/// <c>-telas-fire.png</c>, <c>-cusser-rubble.png</c> and <c>-frenzied-zealot.png</c>. The own Priest of the Whirlwind in the
/// Malazan army is staging (a dev spawn for player 0): the local player is player 0 (Malazan) until the M6 lobby, so a
/// Sandstorm cast through the HUD needs a Priest of its own.
/// </remarks>
public partial class M4PlayableTest : Node
{
    /// <summary>Most sim ticks one seed may take (4 min 35 s of game time). Measured (2026-10-10, 46 seed-runs headless under a parallel <c>dotnet test</c>, after BUG-0463): seed 1 2,560-2,903, seed 6 3,424-3,878, so at least 1,600 ticks (40 %) of margin.</summary>
    public const int TickBudget = 5500;

    private sealed class StepFailed : Exception
    {
        public StepFailed(string message) : base(message) { }
    }

    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private CommandCard _card = null!;
    private ResourceBar _bar = null!;
    private Minimap _mini = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private CombatViews _combat = null!;
    private AbilityViews _views = null!;
    private FogOfWar _fog = null!;

    private ulong _seed;
    private int _stepNo, _breakStep = -1, _stepStart;
    private string _step = "start";
    private string? _shots;

    private int _hall0, _hall1;
    private int _mageType, _priestType, _sapperType, _zealotType, _telas, _sandstorm, _cusser, _burning, _blinded, _frenzied;
    private int _barracks0, _billet0;
    private System.Numerics.Vector2 _army1At;
    private readonly List<EntityHandle> _army = new();
    private readonly List<EntityHandle> _enemyArmy = new();
    private EntityHandle _sapper;

    // Per tick (SimRunner.Ticked): resolved casts by caster, and the duel's swing starts.
    private readonly List<(EntityHandle Caster, int Ability, System.Numerics.Vector2 Point, int Tick)> _resolves = new();
    // Swings are kept by the tick they started on, so a window is counted by tick, however far a frame runs past its end
    // (at 8x one frame spans several ticks; BUG-0463).
    private EntityHandle _duelA, _duelB;
    private readonly List<int> _swingTicksA = new(), _swingTicksB = new();
    private int _cooldownA, _cooldownB;

    // The fog rule, checked every frame.
    private long _fogFrames, _enemyShownFrames, _enemyHiddenFrames, _fogViolations, _approachFrames;
    private bool _approaching;
    private string _firstViolation = "";

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
    private NavGrid G => _sim.World.NavGrid;
    private int Tick => _sim.TickNumber;

    public override async void _Ready()
    {
        string failure = "";
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new StepFailed("data failed to load");
            _data = loaded.Data!;
            if (UiText.Shared == null) throw new StepFailed("ui.json failed to load");
            var seeds = new List<ulong> { 1, 6 };
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--seed") seeds = new List<ulong> { ulong.Parse(args[i + 1]) };
                if (args[i] == "--break") _breakStep = int.Parse(args[i + 1]);
                if (args[i] == "--shots") _shots = args[i + 1];
            }
            foreach (ulong seed in seeds) await PlaySeed(seed);
        }
        catch (StepFailed f)
        {
            failure = $"{_step} (seed {_seed}, tick {(_sim != null ? Tick : -1)}): {f.Message}";
            SaveReplay("fail");
        }
        catch (Exception ex)
        {
            failure = $"{_step} (seed {_seed}, tick {(_sim != null ? Tick : -1)}): exception {ex}";
            try { SaveReplay("exception"); }
            catch (Exception save) { GD.Print($"  replay not saved: {save.Message}"); }
        }
        GD.Print(failure.Length == 0 ? "M4 PLAYABLE TEST PASS" : $"M4 PLAYABLE TEST FAIL {failure}");
        SceneExit.Quit(this, failure.Length == 0 ? 0 : 1);
    }

    // ---- the script ----

    private async Task PlaySeed(ulong seed)
    {
        _seed = seed;
        _stepNo = 0;
        _step = "start";
        await StartMatch(seed);
        await Stage();

        // 1. Fog: the map is black past own sight, the enemy base and army are not drawn.
        Step("fog: black past own sight, the enemy not drawn");
        await Ticks(VisionConstants.UpdateInterval + 1);
        byte[] vis = W.Fog.Visibility(0).ToArray();
        int unexplored = vis.Count(b => b == 0);
        int hallCell = FogView.CellOf(W.Fog.Width, W.Fog.Height, Centre(_hall1));
        byte[] texture = _fog.Image.GetData();
        Expect(unexplored > vis.Length / 2 && vis[hallCell] == 0 && texture[hallCell] == 0,
            $"{unexplored} of {vis.Length} cells unexplored; the enemy hall's cell {vis[hallCell]} (texture {texture[hallCell]})");
        int enemyShown = 0, enemySeen = 0, ownHidden = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            if (U.Owner[i] == 1) { if (_units.IsShown(i)) enemyShown++; if (W.Fog.CanSeeUnit(0, i)) enemySeen++; }
            else if (!_units.IsShown(i)) ownHidden++;
        }
        int enemyBuildingsShown = 0;
        for (int i = 0; i < B.Capacity; i++) if (B.Alive[i] && B.Owner[i] == 1 && _buildings.IsShown(i)) enemyBuildingsShown++;
        Expect(enemyShown == 0 && enemySeen == 0 && enemyBuildingsShown == 0 && ownHidden == 0,
            $"enemy units shown {enemyShown} (seen {enemySeen}), enemy buildings shown {enemyBuildingsShown}, own units hidden {ownHidden}");
        GD.Print($"  {unexplored * 100 / vis.Length}% of the map unexplored; {_enemyArmy.Count} enemy army units and the enemy base hidden");
        Done();

        // 2. The Sapper walks ahead to wait well short of the enemy (clicked, a right click on the ground: a plain Move, which
        // never fights). Then box-select the army, A, and a click on the ground where the enemy army stands (under the fog).
        Step("box-select the army, A + click toward the enemy");
        await SelectAlone(_sapper);
        System.Numerics.Vector2 toEnemy = System.Numerics.Vector2.Normalize(Centre(_hall1) - Centre(_hall0));
        int waitCell = FlowField.NearestPassable(G, CellOf(_army1At - toEnemy * 45f));
        System.Numerics.Vector2 sapperWait = G.CellCenter(waitCell % G.Width, waitCell / G.Width);
        await LookAt(sapperWait);
        await RightClick(Screen(sapperWait, 0f));
        await Ticks(2);
        Expect(U.State[_sapper.Index] == UnitState.Moving && U.Mode[_sapper.Index] == CombatMode.None, $"the Sapper is {U.State[_sapper.Index]} ({U.Mode[_sapper.Index]}) after the right click");
        // Shift + H: it holds where the walk ends (an idle unit would chase whatever comes into sight and wander off).
        Input.ActionPress("order_queue");
        Key(Godot.Key.H);
        Input.ActionRelease("order_queue");
        await Ticks(2);
        Expect(U.QueueCount[_sapper.Index] == 1 && U.QueueKind[_sapper.Index * Rts.Sim.Orders.OrderConstants.QueueCapacity] == CommandKind.HoldPosition,
            $"Shift + H queued {U.QueueCount[_sapper.Index]} orders on the Sapper");
        System.Numerics.Vector2 armyAt = Mean(_army);
        await LookAt(armyAt);
        await BoxDrag(_army.Select(h => ScreenOf(h.Index)).ToArray());
        Expect(_sel.Selection.Count == _army.Count && _army.All(h => _sel.Selection.Contains(h)), $"selection {_sel.Selection.Count}, army {_army.Count}");
        // Ctrl + 1: the army is control group 1.
        Input.ActionPress("group_assign");
        Key(Godot.Key.Key1);
        Input.ActionRelease("group_assign");
        Expect(_sel.Groups.Items(0).Length == _army.Count, $"group 1 holds {_sel.Groups.Items(0).Length} units");
        int attackMoves = _sel.IssuedCount(CommandKind.AttackMove);
        Key(Godot.Key.A);
        await Frames(1);
        Expect(_sel.TargetKind == CommandKind.AttackMove, $"A armed {_sel.TargetKind}");
        await LookAt(_army1At);
        await LeftClick(Screen(_army1At, 0f));
        await Ticks(2);
        Expect(_sel.IssuedCount(CommandKind.AttackMove) == attackMoves + _army.Count && !_sel.Targeting, $"{_sel.IssuedCount(CommandKind.AttackMove) - attackMoves} attack-moves for {_army.Count} units");
        Expect(_army.All(h => U.Mode[h.Index] == CombatMode.AttackMove), $"modes {string.Join(",", _army.Select(h => U.Mode[h.Index]))}");
        _approaching = true;
        Done();

        // 3. The approach under the fog: the enemy is drawn only from first contact on.
        Step("approach under fog: first contact");
        int contact = -1;
        await Until(() =>
        {
            for (int i = 0; i < U.Capacity && contact < 0; i++)
                if (U.Alive[i] && U.Owner[i] == 1 && _units.IsShown(i)) contact = i;
            return contact >= 0;
        }, "an enemy unit in sight");
        _approaching = false;
        Expect(W.Fog.CanSeeUnit(0, contact), $"unit {contact} drawn without CanSeeUnit");
        GD.Print($"  first contact: enemy unit {contact} ({_data.Units[U.TypeId[contact]].Key}) at {U.Position[contact]}; fog checked on {_approachFrames} frames of the approach, {_fogViolations} violations");
        await LookAt((Mean(_army) + U.Position[contact]) / 2f);
        await Shot("first-contact");
        Done();

        // 4. Tab to the own Priest (the most fragile caster, so first), Q, a click on a blob: the storm is drawn and the
        // Whirlwind inside are Blinded.
        Step("Sandstorm by the own Priest (Tab, Q, click)");
        System.Numerics.Vector2 stormAt = await CastOnBlob(_priestType, _sandstorm, 6f, 1, "Sandstorm");
        int zone = -1;
        for (int k = 0; k < W.Zones.Capacity; k++) if (W.Zones.Alive[k] && W.Zones.Owner[k] == 0) zone = k;
        Expect(zone >= 0, "no own zone after the resolve");
        // Bounded by the storm's life, so a storm that catches no one fails as that (its 45 s cooldown outlasts the fight).
        await Until(() => OwnDiscs() > 0 && BlindedEnemyMarkers() > 0 || !W.Zones.Alive[zone],
            "the storm drawn and a Blinded marker over an enemy", () => $"discs {_views.ShownZones}, blinded markers {BlindedEnemyMarkers()}");
        Expect(OwnDiscs() > 0 && BlindedEnemyMarkers() > 0, $"the storm at {stormAt} ended on tick {Tick} without a Blinded marker over a seen Whirlwind unit");
        GD.Print($"  storm at {W.Zones.Center[zone]} (cast at {stormAt}), {OwnDiscs()} own disc, {BlindedEnemyMarkers()} Blinded markers");
        await LookAt(W.Zones.Center[zone]);
        await Shot("storm");
        Done();

        // 5. Tab to the Cadre Mages, Q (the HUD picks the nearest ready one), a click on a Whirlwind blob: the cast resolves and
        // the Burning markers appear. A cast that burns no one (the clicked unit was killed in the fight, often by the mage's
        // own bolts, before the 0.8 s cast resolved; BUG-0463) is cast again, as a player would: at most twice more.
        Step("Telas Fire on a Whirlwind blob (Tab, Q, click)");
        for (int cast = 0; ; cast++)
        {
            await CastOnBlob(_mageType, _telas, 3f, 1, "Telas Fire", again: cast > 0);
            int resolvedAt = Tick;
            // A burning enemy out of sight may still walk into it while it burns; none burning at all is a miss.
            await Until(() => BurningEnemyMarkers() > 0 || Tick >= resolvedAt + 20 && !EnemyBurning(), "a Burning marker over an enemy",
                () => $"{EnemyBurning()} enemy burning");
            if (BurningEnemyMarkers() > 0) break;
            Expect(cast < 2, "Telas Fire resolved three times and burned no seen Whirlwind unit");
            GD.Print($"  Telas Fire burned no one (its blob died or walked off before the resolve); casting again on tick {Tick}");
        }
        GD.Print($"  {BurningEnemyMarkers()} Burning markers over Whirlwind units");
        await Shot("telas-fire");
        Done();

        // 6. Kills and losses move, the resource bar with them; then the fight ends with the army standing.
        Step("kills and losses move");
        await Until(() => W.Kills[0] > 0 && W.Losses[0] > 0, "a kill and a loss");
        await Frames(1);
        Expect(_bar.ShownKills == W.Kills[0] && _bar.ShownLosses == W.Losses[0], $"bar kills {_bar.ShownKills} losses {_bar.ShownLosses}, sim {W.Kills[0]} / {W.Losses[0]}");
        // Decided: our army stands, at most two of theirs live, and none of those has been in sight for 100 ticks (seed 6
        // leaves a Horse Raider idle on a rise above the army, which neither side engages).
        int lastSeen = Tick;
        await Until(() =>
        {
            if (_enemyArmy.Any(h => U.IsAlive(h) && W.Fog.CanSeeUnit(0, h.Index))) lastSeen = Tick;
            return _army.All(h => !U.IsAlive(h)) || _enemyArmy.Count(h => U.IsAlive(h)) <= 2 && Tick - lastSeen >= 100;
        }, "the fight decided",
            () => $"enemy army left {string.Join(", ", _enemyArmy.Where(h => U.IsAlive(h)).Select(h => $"{_data.Units[U.TypeId[h.Index]].Key} {U.State[h.Index]} at {U.Position[h.Index]} level {LevelAt(U.Position[h.Index])} seen {W.Fog.CanSeeUnit(0, h.Index)}"))}; ours {_army.Count(h => U.IsAlive(h))} round {Mean(_army)} ({string.Join(",", _army.Where(h => U.IsAlive(h)).Select(h => U.State[h.Index]).Distinct())})");
        Expect(_army.Any(h => U.IsAlive(h)), "the Malazan army was wiped out");
        string left = string.Join(", ", _enemyArmy.Where(h => U.IsAlive(h)).Select(h => $"a {_data.Units[U.TypeId[h.Index]].DisplayName} {U.State[h.Index]} out of sight at {U.Position[h.Index]} (level {LevelAt(U.Position[h.Index])})"));
        GD.Print($"  kills {W.Kills[0]}, losses {W.Losses[0]} (bar '{_bar.KillsLabel.Text}'); {_army.Count(h => U.IsAlive(h))} of {_army.Count} Malazan standing; enemy army left: {(left.Length == 0 ? "none" : left)}");
        Done();

        // 7. The Sapper walks up, Q, a click on an enemy building: the Cusser lands; the building falls and its rubble shows.
        Step("Cusser on a building, rubble shown");
        await CusserStep();
        Done();

        // 8. A Zealot under half hp shows the Frenzied marker and swings faster than an unhurt one.
        Step("a Frenzied Zealot swings faster");
        await ZealotDuel();
        Done();

        // 9. The hash twin: the command stream, replayed into a bare sim, hashes equal every tick.
        Step("hash twin");
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Expect(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"  {replay.Commands.Length} commands ({replay.Commands.Count(c => c.Kind == CommandKind.UseAbility)} UseAbility), {replay.Checkpoints.Length} checkpoints equal in a bare twin");
        Done();

        Expect(_fogViolations == 0, $"fog rule: {_fogViolations} violations, first {_firstViolation}");
        GD.Print($"seed {seed}: fog checked on {_fogFrames} frames ({_approachFrames} of the approach): enemy unit-frames drawn {_enemyShownFrames}, hidden {_enemyHiddenFrames}, {_fogViolations} drawn without sight; {Tick} of {TickBudget} ticks");
        _runner.Ticked -= OnTicked;
        _match.QueueFree();
        await Frames(2);
    }

    // Tab to the casters' subgroup (`casterType` in the army), wait for a visible enemy with `others` more visible enemies
    // within `within` m (a blob) within 40 m of a live caster, look at it, the ability's grid key, a click on the enemy:
    // exactly one UseAbility, for the caster the HUD picks (one of the live casters); waits for the resolve. A cast the sim
    // drops (the caster's walk in stopped short, as at a cliff edge) is clicked again on a fresh blob, as a player would, at
    // most twice more; each drop is printed. So is a caster killed in the fight before its cast resolves (it can die on the
    // very tick its command applies, BUG-0463): the click goes again, and the HUD picks the next live caster of the type.
    // `again`: a second cast of the step, so the button may first count down (the HUD's own SoonestReady) before it is pressed.
    private async Task<System.Numerics.Vector2> CastOnBlob(int casterType, int ability, float within, int others, string what, bool again = false)
    {
        string casterName = _data.Units[casterType].DisplayName;
        int cell = -1;
        int abilityIndex = AbilityCaster.IndexOf(_data.Units[casterType], ability);
        // What is in front of the casters: within 40 m (the walk in may be long; a drop is clicked again).
        const float reach = 40f;
        string dropped = "";
        int drops = 0, deaths = 0;
        while (drops < 3)
        {
            if (again && _sel.SoonestReady(ability) > 0)
            {
                GD.Print($"  {what}: every live {casterName} cooling down on tick {Tick}; waiting {_sel.SoonestReady(ability)} ticks for the button");
                await Until(() => _sel.SoonestReady(ability) <= 0, $"{what}'s button ready again");
            }
            List<EntityHandle> casters = _army.Where(h => U.IsAlive(h) && U.TypeId[h.Index] == casterType).ToList();
            Expect(casters.Count > 0, $"every {casterName} died before {what}{dropped}");
            // The biggest blob of standing units (a cast on a unit on the move lands where it was, and may catch no one:
            // BUG-0463); after 60 ticks without one of `others` + 1, any standing visible enemy in front of a caster will do,
            // and after 120 any visible enemy.
            int target = -1, since = Tick;
            await Until(() => (target = Blob(casters, within, Tick - since < 60 ? others : 0, reach, Tick - since < 120)) >= 0 || casters.All(h => !U.IsAlive(h)),
                $"a visible Whirlwind unit within {reach} m of a {casterName} for {what}");
            if (target < 0) continue; // every caster in the list died meanwhile: the top of the loop says so
            GD.Print($"  {what}: a blob round unit {target} ({_data.Units[U.TypeId[target]].Key}) at {U.Position[target]} on tick {Tick}, {casters.Count(h => U.IsAlive(h))} live {casterName}");
            await LookAt(U.Position[target]);
            // Tab now: a type dying out since an earlier Tab may have moved the active subgroup.
            await TabTo(casterType);
            for (int f = 0; f < 3 && (cell = Enumerable.Range(0, CommandCard.Cells).FirstOrDefault(i => _card.AbilityAt(i) == ability, -1)) < 0; f++) await Frame();
            Expect(cell >= 0, $"{what} not on the card (active subgroup {_sel.Subgroups.ActiveType}, cell 0 {_card.ActionAt(0)} {_card.AbilityAt(0)})");
            Expect(_card.AbilitySecondsAt(cell) == 0, $"{what}'s button shows {_card.AbilitySecondsAt(cell)} s");
            if (!U.Alive[target]) continue; // the blob's unit died on the way here: pick a fresh one
            Vector2 px = ScreenOf(target);
            int issued = _sel.IssuedCount(CommandKind.UseAbility), resolves = _resolves.Count;
            Key(KeyOf(cell));
            await Frames(1);
            Expect(_sel.TargetAbility == ability, $"{_card.GridKey(cell)} armed {_sel.TargetKind} {_sel.TargetAbility}");
            await LeftClick(px);
            EntityHandle caster = _sel.LastCaster;
            Expect(_sel.IssuedCount(CommandKind.UseAbility) == issued + 1 && _army.Contains(caster) && U.TypeId[caster.Index] == casterType && !_sel.Targeting,
                $"{_sel.IssuedCount(CommandKind.UseAbility) - issued} UseAbility, caster {caster} (want a {casterName} of the army), targeting {_sel.Targeting}");
            await Ticks(2);
            bool Resolved() => _resolves.Skip(resolves).Any(r => r.Caster == caster && r.Ability == ability);
            // A dead caster first: a unit killed in the fight holds no cast, and "took no cast" would name the wrong fault.
            if (!U.IsAlive(caster) && !Resolved())
            {
                deaths++;
                dropped += $"; a {casterName} ({caster.Index}) was killed in the fight before its cast resolved (by tick {Tick})";
                GD.Print($"  {what}: the {casterName} ({caster.Index}) the click picked was killed in the fight by tick {Tick}, before its cast resolved; clicking again");
                continue;
            }
            Expect(U.CastAbility[caster.Index] == abilityIndex || Resolved(), $"the {casterName} ({caster.Index}, alive) took no cast (cast ability {U.CastAbility[caster.Index]})");
            System.Numerics.Vector2 aim = U.CastPoint[caster.Index];
            await Until(() => Resolved() || !U.IsAlive(caster) || U.CastAbility[caster.Index] != abilityIndex, $"{what} resolved");
            if (!U.IsAlive(caster) && !Resolved())
            {
                deaths++;
                dropped += $"; a {casterName} ({caster.Index}) was killed mid-cast on tick {Tick}";
                GD.Print($"  {what}: the {casterName} ({caster.Index}) was killed mid-cast on tick {Tick}; clicking again");
                continue;
            }
            if (Resolved())
            {
                var r = _resolves.Skip(resolves).First(r => r.Caster == caster && r.Ability == ability);
                GD.Print($"  {what}: {_card.GridKey(cell)} + click on unit {target} ({_data.Units[U.TypeId[target]].Key}); cast by {caster.Index} at {r.Point} on tick {r.Tick}{dropped}");
                return r.Point;
            }
            drops++;
            System.Numerics.Vector2 at = U.Position[caster.Index];
            dropped += $"; an earlier click dropped by the sim: the caster stopped at {at} (level {LevelAt(at)}), {System.Numerics.Vector2.Distance(at, aim):0.0} m from {aim} (level {LevelAt(aim)})";
            GD.Print($"  {what} dropped on tick {Tick}: the caster stopped at {at} (level {LevelAt(at)}), {System.Numerics.Vector2.Distance(at, aim):0.0} m from the point {aim} (level {LevelAt(aim)}), range {_data.Abilities[ability].Range} m; clicking again");
        }
        throw new StepFailed($"{what} dropped three times ({deaths} casters killed besides){dropped}");
    }

    private int LevelAt(System.Numerics.Vector2 p) => G.LevelAt(CellOf(p) % G.Width, CellOf(p) / G.Width);

    // A visible enemy unit within `reach` m of a live caster with at least `others` other visible enemies within `within` m,
    // the one with the most; when `standing`, only a unit that did not move on the last tick and that no caster is shooting
    // (the caster's own bolts kill it before a 0.8 s cast resolves); -1 for none.
    private int Blob(List<EntityHandle> casters, float within, int others, float reach, bool standing)
    {
        int best = -1, bestN = others - 1;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 1 || !_units.IsShown(i)) continue;
            if (standing && U.Position[i] != U.PrevPosition[i]) continue;
            bool near = false, shot = false;
            foreach (EntityHandle c in casters)
            {
                if (!U.IsAlive(c)) continue;
                near |= System.Numerics.Vector2.Distance(U.Position[i], U.Position[c.Index]) <= reach;
                shot |= U.Target[c.Index].Index == i && !U.TargetIsBuilding[c.Index] && U.State[c.Index] == UnitState.Attacking;
            }
            if (!near || standing && shot) continue;
            int n = 0;
            for (int j = 0; j < U.Capacity; j++)
                if (j != i && U.Alive[j] && U.Owner[j] == 1 && _units.IsShown(j) && System.Numerics.Vector2.Distance(U.Position[i], U.Position[j]) <= within) n++;
            // Ties to the unit with the most hp: a unit about to die takes a cast with it (BUG-0463).
            if (n > bestN || n == bestN && best >= 0 && U.Hp[i] > U.Hp[best]) (best, bestN) = (i, n);
        }
        return best;
    }

    private async Task CusserStep()
    {
        Expect(U.IsAlive(_sapper), "the Sapper died on its way or while it waited");
        // 1 recalls the army; a right click on the ground 30 m back toward home pulls it off the base (attacking units stand
        // as walls, and on seed 6 they shut the Sapper's way down a rise).
        Key(Godot.Key.Key1);
        await Frames(1);
        int alive = _army.Count(h => U.IsAlive(h));
        Expect(_sel.Selection.Count == alive, $"1 recalled {_sel.Selection.Count} of {alive} live army units");
        System.Numerics.Vector2 toHome = System.Numerics.Vector2.Normalize(Centre(_hall0) - Centre(_hall1));
        int backCell = FlowField.NearestPassable(G, CellOf(Mean(_army) + toHome * 30f));
        System.Numerics.Vector2 back = G.CellCenter(backCell % G.Width, backCell / G.Width);
        await LookAt(back);
        await RightClick(Screen(back, 0f));
        await Ticks(2);
        await Until(() => _army.All(h => !U.IsAlive(h) || U.State[h.Index] != UnitState.Moving), "the army pulled back");
        GD.Print($"  1, right click: the army pulled back to {Mean(_army)} on tick {Tick}");
        await SelectAlone(_sapper);
        Expect(_card.AbilityAt(0) == _cusser, $"the Sapper's Q is {_card.AbilityAt(0)}");
        // The enemy building nearest the Sapper (the Town Hall last). Right-clicks on the ground (plain Moves) to a spot beside it, so the Cusser is
        // cast in range: the sim drops a long walk-in now and then (see docs/03 "Implementation (M4-V7)"). A Move that stops
        // short (the army's attackers stand as walls) is clicked again to the next spot round it; a building that falls
        // meanwhile (the army is razing the base) gives way to the next nearest.
        int building = -1;
        System.Numerics.Vector2 c = default;
        BuildingDef def = null!;
        float reach = _data.Abilities[_cusser].Range;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (building < 0 || !B.Alive[building])
            {
                building = NearestEnemyBuilding(U.Position[_sapper.Index]);
                Expect(building >= 0, "no enemy building left");
                c = Centre(building);
                def = _data.Buildings[B.TypeId[building]];
            }
            if (System.Numerics.Vector2.Distance(U.Position[_sapper.Index], c) <= reach) break;
            (System.Numerics.Vector2 stand, Vector2 px) = await StandBeside(c, def, U.Position[_sapper.Index],
                Math.Min(reach - 0.5f, Math.Max(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize / 2f + 1.5f), attempt);
            await RightClick(px);
            await Ticks(2);
            await Until(() => !U.IsAlive(_sapper) || U.State[_sapper.Index] != UnitState.Moving, "the Sapper's walk");
            Expect(U.IsAlive(_sapper), "the Sapper died on the way");
            float d = System.Numerics.Vector2.Distance(U.Position[_sapper.Index], c);
            GD.Print($"  right-clicked {stand}: the Sapper stopped at {U.Position[_sapper.Index]} (level {LevelAt(U.Position[_sapper.Index])}), {d:0.0} m from the {def.DisplayName} (level {LevelAt(c)}), on tick {Tick}");
        }
        float near = System.Numerics.Vector2.Distance(U.Position[_sapper.Index], c);
        Expect(near <= reach, $"the Sapper never got within {reach} m of the {def.DisplayName}: {near:0.0} m");
        Expect(B.Alive[building], $"the {def.DisplayName} fell before the Cusser");
        int hp0 = B.Hp[building], resolves = _resolves.Count, issued = _sel.IssuedCount(CommandKind.UseAbility);
        await LookAt(c);
        Key(Godot.Key.Q);
        await Frames(1);
        Expect(_sel.TargetAbility == _cusser, $"Q armed {_sel.TargetKind} {_sel.TargetAbility}");
        await LeftClick(Screen(c, 0f));
        Expect(_sel.IssuedCount(CommandKind.UseAbility) == issued + 1 && _sel.LastCaster == _sapper, "the click sent no Cusser");
        await Ticks(2);
        Expect(System.Numerics.Vector2.Distance(U.CastPoint[_sapper.Index], c) < 1f || _resolves.Skip(resolves).Any(r => r.Caster == _sapper),
            $"the Cusser aims at {U.CastPoint[_sapper.Index]}, the footprint's centre is {c}");
        await Until(() => _resolves.Skip(resolves).Any(r => r.Caster == _sapper) || !U.IsAlive(_sapper) || U.CastAbility[_sapper.Index] != 0, "the Cusser resolved");
        Expect(_resolves.Skip(resolves).Any(r => r.Caster == _sapper),
            $"the Cusser dropped: Sapper alive {U.IsAlive(_sapper)}, {U.State[_sapper.Index]} at {U.Position[_sapper.Index]} (level {LevelAt(U.Position[_sapper.Index])}), {System.Numerics.Vector2.Distance(U.Position[_sapper.Index], c):0.0} m from {c} (level {LevelAt(c)})");
        int dropped = B.Alive[building] ? hp0 - B.Hp[building] : hp0;
        Expect(dropped >= 300, $"{def.DisplayName}: hp {hp0} -> {(B.Alive[building] ? B.Hp[building] : 0)} at the Cusser's resolve");
        GD.Print($"  Cusser on the {def.DisplayName} ({building}) from {near:0.0} m: hp {hp0} -> {(B.Alive[building] ? B.Hp[building] : 0)}");
        if (B.Alive[building])
        {
            // The Sapper and the army (1) finish it: a right click on its box is an Attack for each.
            await RightClick(Screen(c, BuildingViews.BoxHeight * 0.5f));
            await Ticks(2);
            Expect(U.Target[_sapper.Index].Index == building && U.TargetIsBuilding[_sapper.Index] || !B.Alive[building], "the right click on the building gave no Attack");
            Key(Godot.Key.Key1);
            await Frames(1);
            await LookAt(c);
            await RightClick(Screen(c, BuildingViews.BoxHeight * 0.5f));
            await Ticks(2);
        }
        int gen = B.Generation[building], anchor = B.Cell[building];
        await Until(() => !B.Alive[building] || B.Generation[building] != gen, $"the {def.DisplayName} destroyed", () => $"hp {B.Hp[building]}");
        int rubble = -1;
        await Until(() => (rubble = RubbleAt(c)) >= 0, "rubble shown on its footprint");
        // In sight as the building was: any footprint cell (BUG-0460: the attackers stand at the footprint's edge, so its
        // centre cell can lie just past their sight).
        Expect(SeesFootprint(anchor, def), "the rubble is not in sight (no footprint cell visible)");
        GD.Print($"  {def.DisplayName} destroyed on tick {Tick}; rubble marker {rubble} drawn at {_combat.MarkerTransform(rubble).Origin}, in sight (centre cell {(W.Fog.IsVisible(0, CellOf(c)) ? "visible" : "not visible")})");
        await Shot("cusser-rubble");
    }

    // Two fresh Zealots of the enemy's attack two of our buildings (staging Attacks: the enemy has no AI yet); a fresh Cadre
    // Mage of ours burns one of them through the HUD (select, Q, click) and walks off (a Shift + right-click queued Move).
    // Burning takes the Zealot from 63 to 23 hp: under half, Frenzied (its marker in FrenzyColor), and over the next 60 ticks it
    // starts more swings than the unhurt one.
    private async Task ZealotDuel()
    {
        // Each Zealot on the camera's side of its building (+y is toward the camera), so the box never hides it in the still.
        System.Numerics.Vector2 billet = Centre(_billet0), barracks = Centre(_barracks0);
        float cs = MapConstants.CellSize;
        System.Numerics.Vector2 aAt = Free(billet + new System.Numerics.Vector2(0f, _data.Buildings[B.TypeId[_billet0]].FootprintHeight * cs / 2f + 1.5f));
        System.Numerics.Vector2 bAt = Free(barracks + new System.Numerics.Vector2(0f, _data.Buildings[B.TypeId[_barracks0]].FootprintHeight * cs / 2f + 1.5f));
        // The mage 20 m off, beyond both Zealots' and its own sight of them (it must not take one as a target by itself).
        System.Numerics.Vector2 mageAt = new[] { new System.Numerics.Vector2(20f, 0f), new System.Numerics.Vector2(-20f, 0f), new System.Numerics.Vector2(0f, 20f), new System.Numerics.Vector2(0f, -20f) }
            .Select(o => Free(aAt + o)).FirstOrDefault(p => System.Numerics.Vector2.Distance(p, aAt) >= 18f && System.Numerics.Vector2.Distance(p, bAt) >= 18f, new System.Numerics.Vector2(-1f, -1f));
        Expect(mageAt.X >= 0f, "no spot for the duel's mage 18 m from both Zealots");
        Expect(System.Numerics.Vector2.Distance(aAt, bAt) > 8f, $"the duel's Zealots stand {System.Numerics.Vector2.Distance(aAt, bAt):0.0} m apart");
        HashSet<EntityHandle> before = LiveHandles();
        _sim.Enqueue(Command.SpawnUnit(1, _zealotType, aAt));
        _sim.Enqueue(Command.SpawnUnit(1, _zealotType, bAt));
        _sim.Enqueue(Command.SpawnUnit(0, _mageType, mageAt));
        await Ticks(2);
        List<int> fresh = Fresh(before);
        var zealots = fresh.Where(i => U.TypeId[i] == _zealotType).OrderBy(i => System.Numerics.Vector2.Distance(U.Position[i], aAt)).ToList();
        int za = zealots.Count == 2 ? zealots[0] : -1, zb = zealots.Count == 2 ? zealots[1] : -1;
        int m = fresh.FirstOrDefault(i => U.TypeId[i] == _mageType, -1);
        Expect(za >= 0 && zb >= 0 && m >= 0, $"duel spawns: zealots {za} {zb}, mage {m}");
        _duelA = new EntityHandle(za, U.Generation[za]);
        _duelB = new EntityHandle(zb, U.Generation[zb]);
        var mage = new EntityHandle(m, U.Generation[m]);
        _sim.Enqueue(Command.Attack(1, _duelA, new EntityHandle(_billet0, B.Generation[_billet0]), true));
        _sim.Enqueue(Command.Attack(1, _duelB, new EntityHandle(_barracks0, B.Generation[_barracks0]), true));
        _swingTicksA.Clear();
        _swingTicksB.Clear();
        await Until(() => _swingTicksA.Count > 0 && _swingTicksB.Count > 0, "both Zealots swinging at the buildings");

        // The mage: clicked alone, Q, a click on Zealot A, then Shift + right-click back to where it stood.
        await SelectAlone(mage);
        System.Numerics.Vector2 mid = (U.Position[za] + mageAt) / 2f;
        await LookAt(mid);
        int resolves = _resolves.Count;
        Key(Godot.Key.Q);
        await Frames(1);
        Expect(_sel.TargetAbility == _telas, $"Q armed {_sel.TargetKind} {_sel.TargetAbility}");
        await LeftClick(ScreenOf(za));
        Input.ActionPress("order_queue");
        await RightClick(Screen(mageAt, 0f));
        Input.ActionRelease("order_queue");
        await Ticks(2);
        Expect(U.QueueCount[m] == 1 && U.CastAbility[m] == 0, $"the mage's cast {U.CastAbility[m]}, queue {U.QueueCount[m]}");
        await Until(() => _resolves.Skip(resolves).Any(r => r.Caster == mage), "the duel's Telas Fire resolved");
        float line = 0.5f * _data.Units[_zealotType].Hp;
        await Until(() => !U.IsAlive(_duelA) || U.Hp[za] < line && HasStatus(za, _frenzied), "Zealot A under half hp and Frenzied",
            () => $"hp {U.Hp[za]}, statuses {U.Statuses.Count[za]}");
        Expect(U.IsAlive(_duelA) && U.IsAlive(_duelB) && U.Hp[zb] == _data.Units[_zealotType].Hp && !HasStatus(zb, _frenzied),
            $"Zealot A alive {U.IsAlive(_duelA)} hp {U.Hp[za]}; B alive {U.IsAlive(_duelB)} hp {U.Hp[zb]} frenzied {HasStatus(zb, _frenzied)}");
        Expect(U.Target[za].Index == _billet0 && U.Target[zb].Index == _barracks0, "a Zealot left its building");
        await LookAt(U.Position[za]);
        await Until(() => Marks(za, _frenzied) == 1, "the Frenzied marker over Zealot A");
        Expect(_views.StatusColor(_frenzied) == AbilityViews.FrenzyColor && Marks(zb, _frenzied) == 0 && W.Fog.CanSeeUnit(0, za),
            $"colour {_views.StatusColor(_frenzied)}, B's Frenzied markers {Marks(zb, _frenzied)}");
        await Shot("frenzied-zealot");
        // Exactly the next 60 ticks of swings, counted by tick (the 60 Ticked calls after `start`), not by frame: in 60 ticks
        // a 13-tick cooldown starts at least 4 swings and a 20-tick one at most 3, but in 61 both can start 4 (BUG-0463).
        const int window = 60;
        int start = Tick;
        await Until(() => Tick >= start + window, $"{window} ticks of swings");
        Expect(U.IsAlive(_duelA), "Zealot A died during the swing count");
        int a = _swingTicksA.Count(t => t > start && t <= start + window), b = _swingTicksB.Count(t => t > start && t <= start + window);
        Expect(b >= 2 && a > b, $"over ticks {start + 1}-{start + window} Zealot A (hp {U.Hp[za]}, Frenzied) swung {a} times, unhurt B {b} (swing ticks A {string.Join(",", _swingTicksA.Where(t => t > start))}; B {string.Join(",", _swingTicksB.Where(t => t > start))})");
        GD.Print($"  Zealot A at {U.Hp[za]} / {_data.Units[_zealotType].Hp} hp, Frenzied (marker {AbilityViews.FrenzyColor}): {a} swings in ticks {start + 1}-{start + window} (the frame clock ran to {Tick}); unhurt Zealot B: {b}");
        _duelA = _duelB = default;
    }

    // ---- setup ----

    private async Task StartMatch(ulong seed)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(), "--units", "0", "--no-bases", "--mute", "--speed", "8" }));
        _sim = _runner.Simulation!;
        _runner.Ticked += OnTicked;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _mini = _match.GetNode<Minimap>("Hud/Minimap");
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _fog = _match.GetNode<FogOfWar>("World3D/FogOfWar");
        Expect(_runner.GameSpeed == 8.0 && _runner.Recorder != null && _fog.View is { Enabled: true }, $"speed {_runner.GameSpeed}, recorder {_runner.Recorder != null}, fog {_fog.View?.Enabled}");
        _mageType = _data.FindUnit("malazan_cadre_mage");
        _priestType = _data.FindUnit("whirlwind_priest");
        _sapperType = _data.FindUnit("malazan_sapper");
        _zealotType = _data.FindUnit("whirlwind_zealot");
        _telas = _data.FindAbility("telas_fire");
        _sandstorm = _data.FindAbility("sandstorm");
        _cusser = _data.FindAbility("cusser");
        _burning = _data.FindStatus("burning");
        _blinded = _data.FindStatus("blinded");
        _frenzied = _data.FindStatus("frenzied");
        _resolves.Clear();
        (_fogFrames, _enemyShownFrames, _enemyHiddenFrames, _fogViolations, _approachFrames) = (0, 0, 0, 0, 0);
        _firstViolation = "";
        _approaching = false;
        // The start bases through the match's own planner, but beside stand-in blocks a fifth of the map in from the west
        // and east edges: with no army the planner puts the two halls either side of the centre gap, in sight of each other.
        var blocks = new System.Numerics.Vector2[2][];
        for (int p = 0; p < 2; p++)
        {
            int cell = FlowField.NearestPassable(G, G.Height / 2 * G.Width + (p == 0 ? G.Width / 5 : G.Width - 1 - G.Width / 5));
            blocks[p] = new[] { G.CellCenter(cell % G.Width, cell / G.Width) };
        }
        Match.SpawnBases(_sim, blocks, _data.Rules.StartingWorkers, out int warnings);
        Expect(warnings == 0, $"{warnings} players got no start base");
        await Until(() => B.Count >= 2 && U.Count >= 2 * _data.Rules.StartingWorkers, "the start bases");
        _hall0 = _hall1 = -1;
        for (int k = 0; k < B.Capacity; k++)
        {
            if (!B.Alive[k] || _data.Buildings[B.TypeId[k]].Slot != BuildingSlot.TownHall) continue;
            if (B.Owner[k] == 0) _hall0 = k; else _hall1 = k;
        }
        Expect(_hall0 >= 0 && _hall1 >= 0, "no Town Hall for each side");
        GD.Print($"seed {seed}: match up at tick {Tick} (8x, shipped data, fog on, tick budget {TickBudget}); halls {System.Numerics.Vector2.Distance(Centre(_hall0), Centre(_hall1)):0} m apart at {Centre(_hall0)} and {Centre(_hall1)}");
    }

    // Dev spawns (requirements and costs ignored): three buildings per side on its hall's plateau, the two armies in front of
    // their halls (facing each other), and the Malazan Sapper behind its hall.
    private async Task Stage()
    {
        System.Numerics.Vector2 h0 = Centre(_hall0), h1 = Centre(_hall1);
        System.Numerics.Vector2 dir = System.Numerics.Vector2.Normalize(h1 - h0);
        var taken = new bool[G.Width * G.Height];
        foreach (int hall in new[] { _hall0, _hall1 }) StartBase.MarkTaken(G, _data.Buildings[B.TypeId[hall]], B.Cell[hall], taken);
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) taken[CellOf(U.Position[i])] = true;
        // Keep the armies' ground free of buildings.
        System.Numerics.Vector2 army0 = h0 + dir * 20f;
        _army1At = h1 - dir * 18f;
        foreach (System.Numerics.Vector2 a in new[] { army0, _army1At }) MarkDisc(taken, a, 9f);

        int f0 = W.FactionOf(0), f1 = W.FactionOf(1);
        int barracks = StartBase.BuildingOfSlot(_data, f0, BuildingSlot.InfantryHall), armory = StartBase.BuildingOfSlot(_data, f0, BuildingSlot.Forge);
        int billet = StartBase.BuildingOfSlot(_data, f0, BuildingSlot.House);
        int camp = StartBase.BuildingOfSlot(_data, f1, BuildingSlot.InfantryHall), smithy = StartBase.BuildingOfSlot(_data, f1, BuildingSlot.Forge);
        int tent = StartBase.BuildingOfSlot(_data, f1, BuildingSlot.House);
        int level0 = G.LevelAt(B.Cell[_hall0] % G.Width, B.Cell[_hall0] / G.Width), level1 = G.LevelAt(B.Cell[_hall1] % G.Width, B.Cell[_hall1] / G.Width);
        var plan = new List<(int Player, int Type, int Anchor)>
        {
            (0, barracks, BuildingSpot(barracks, h0 - dir * 4f, level0, taken)),
            (0, billet, BuildingSpot(billet, h0 - dir * 4f, level0, taken)),
            (0, armory, BuildingSpot(armory, h0, level0, taken)),
            (1, tent, BuildingSpot(tent, h1 + dir * 10f, level1, taken)), // behind the hall: the Cusser's target
            (1, camp, BuildingSpot(camp, h1, level1, taken)),
            (1, smithy, BuildingSpot(smithy, h1 + dir * 6f, level1, taken)),
        };
        foreach ((int p, int type, int anchor) in plan)
        {
            Expect(anchor >= 0, $"no spot on player {p}'s plateau for {_data.Buildings[type].DisplayName}");
            _sim.Enqueue(Command.SpawnBuilding(p, type, G.CellCenter(anchor % G.Width, anchor / G.Width)));
        }

        int heavy = _data.FindUnit("malazan_heavy_infantry"), crossbow = _data.FindUnit("malazan_crossbowman"), catapult = _data.FindUnit("malazan_catapult");
        int raider = _data.FindUnit("whirlwind_raider"), archer = _data.FindUnit("whirlwind_desert_archer"), horse = _data.FindUnit("whirlwind_horse_raider");
        // Front to back: the melee first, the casters and the catapult behind.
        var malazan = new List<int>();
        malazan.AddRange(Enumerable.Repeat(heavy, 8));
        malazan.AddRange(Enumerable.Repeat(crossbow, 5));
        // Two Cadre Mages: the one the HUD picks for Telas Fire can be killed in the fight on the tick its cast applies, and a
        // player would click again with the other (BUG-0463).
        malazan.AddRange(new[] { _mageType, _mageType, _priestType, catapult });
        var whirlwind = new List<int>();
        whirlwind.AddRange(Enumerable.Repeat(raider, 4));
        whirlwind.AddRange(Enumerable.Repeat(_zealotType, 3));
        whirlwind.AddRange(Enumerable.Repeat(horse, 2));
        whirlwind.AddRange(Enumerable.Repeat(archer, 3));
        whirlwind.Add(_priestType);
        SpawnArmy(0, malazan, army0, dir, taken);
        SpawnArmy(1, whirlwind, _army1At, -dir, taken);
        System.Numerics.Vector2 sapperAt = Free(h0 - dir * 9f);
        _sim.Enqueue(Command.SpawnUnit(0, _sapperType, sapperAt));
        HashSet<EntityHandle> before = LiveHandles();
        await Ticks(2);

        _army.Clear();
        _enemyArmy.Clear();
        _sapper = default;
        foreach (int i in Fresh(before))
        {
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.Owner[i] == 1) _enemyArmy.Add(h);
            else if (U.TypeId[i] == _sapperType) _sapper = h;
            else _army.Add(h);
        }
        Expect(_army.Count == malazan.Count && _enemyArmy.Count == whirlwind.Count && _sapper.Generation != 0,
            $"staged {_army.Count} / {malazan.Count} Malazan, {_enemyArmy.Count} / {whirlwind.Count} Whirlwind, sapper {_sapper}");
        _barracks0 = _billet0 = -1;
        int buildings = 0;
        foreach ((int p, int type, int anchor) in plan)
        {
            int slot = B.SlotAt(anchor % G.Width, anchor / G.Width);
            Expect(slot >= 0 && B.TypeId[slot] == type && B.Owner[slot] == p && !B.UnderConstruction[slot], $"{_data.Buildings[type].DisplayName} not spawned at {anchor}");
            if (p == 0 && type == barracks) _barracks0 = slot;
            if (p == 0 && type == billet) _billet0 = slot;
            buildings++;
        }
        GD.Print($"seed {_seed}: staged {buildings} buildings, {_army.Count} Malazan ({Census(_army)}) {System.Numerics.Vector2.Distance(army0, _army1At):0} m from {_enemyArmy.Count} Whirlwind ({Census(_enemyArmy)}), a Sapper at home; tick {Tick}");
    }

    // Live units by handle (slot and generation), not by slot: a unit that dies while spawns are pending frees its slot,
    // and a spawn may take that slot in the same tick (BUG-0460). The freed slot's generation moves on, so the spawn's handle
    // is new even though its slot is not.
    private HashSet<EntityHandle> LiveHandles()
    {
        var live = new HashSet<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) live.Add(new EntityHandle(i, U.Generation[i]));
        return live;
    }

    // The slots of live units whose handle was not live in `before`, in slot order.
    private List<int> Fresh(HashSet<EntityHandle> before)
    {
        var fresh = new List<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !before.Contains(new EntityHandle(i, U.Generation[i]))) fresh.Add(i);
        return fresh;
    }

    // Whether player 0 sees any cell of a footprint (CanSeeBuilding's rule, which still holds after the building is gone).
    private bool SeesFootprint(int anchor, BuildingDef def)
    {
        int x0 = anchor % G.Width, y0 = anchor / G.Width;
        for (int y = y0; y < y0 + def.FootprintHeight; y++)
            for (int x = x0; x < x0 + def.FootprintWidth; x++)
                if (x >= 0 && y >= 0 && x < G.Width && y < G.Height && W.Fog.IsVisible(0, y * G.Width + x)) return true;
        return false;
    }

    private string Census(List<EntityHandle> units) =>
        string.Join(", ", units.GroupBy(h => _data.Units[U.TypeId[h.Index]].DisplayName).Select(g => $"{g.Count()} {g.Key}"));

    private void SpawnArmy(int player, List<int> types, System.Numerics.Vector2 at, System.Numerics.Vector2 facing, bool[] taken)
    {
        int target = FlowField.NearestPassable(G, CellOf(at));
        FlowField field = FlowField.Build(G, target);
        System.Numerics.Vector2 c = G.CellCenter(target % G.Width, target / G.Width);
        var cells = new List<(float Key, int Cell)>();
        for (int y = Math.Max(0, target / G.Width - 8); y <= Math.Min(G.Height - 1, target / G.Width + 8); y++)
            for (int x = Math.Max(0, target % G.Width - 8); x <= Math.Min(G.Width - 1, target % G.Width + 8); x++)
            {
                int cell = y * G.Width + x;
                if (!G.IsPassable(x, y) || taken[cell] || !(field.CostAt(cell) <= 30f)) continue;
                System.Numerics.Vector2 p = G.CellCenter(x, y);
                // A block: rows across the facing, the front row first.
                float along = System.Numerics.Vector2.Dot(p - c, facing);
                cells.Add((System.Numerics.Vector2.DistanceSquared(p, c) - 0.5f * along, cell));
            }
        cells.Sort((a, b) => a.Key.CompareTo(b.Key));
        Expect(cells.Count >= types.Count, $"player {player}: {cells.Count} free cells for {types.Count} units");
        for (int k = 0; k < types.Count; k++)
        {
            int cell = cells[k].Cell;
            taken[cell] = true;
            _sim.Enqueue(Command.SpawnUnit(player, types[k], G.CellCenter(cell % G.Width, cell / G.Width)));
        }
    }

    // The anchor nearest `near` where a building of `type` is a start-base spot (fits, a free ring) on `level`, 8-28 m from it.
    private int BuildingSpot(int type, System.Numerics.Vector2 near, int level, bool[] taken)
    {
        BuildingDef def = _data.Buildings[type];
        int best = -1;
        float bestD = float.MaxValue;
        for (int cell = 0; cell < G.Width * G.Height; cell++)
        {
            System.Numerics.Vector2 c = StartBase.FootprintCenter(G, def, cell);
            float d = System.Numerics.Vector2.Distance(c, near);
            if (d < 8f || d > 28f || d >= bestD) continue;
            if (!StartBase.IsHallSpot(G, B, _data.Buildings, type, cell, level, taken)) continue;
            (best, bestD) = (cell, d);
        }
        if (best >= 0)
        {
            StartBase.MarkTaken(G, def, best, taken);
            // A ring of free cells round each staged building, so nothing seals a passage.
            int x0 = best % G.Width, y0 = best / G.Width;
            for (int y = y0 - 2; y < y0 + def.FootprintHeight + 2; y++)
                for (int x = x0 - 2; x < x0 + def.FootprintWidth + 2; x++)
                    if (x >= 0 && y >= 0 && x < G.Width && y < G.Height) taken[y * G.Width + x] = true;
        }
        return best;
    }

    private void MarkDisc(bool[] taken, System.Numerics.Vector2 at, float r)
    {
        for (int y = 0; y < G.Height; y++)
            for (int x = 0; x < G.Width; x++)
                if (System.Numerics.Vector2.Distance(G.CellCenter(x, y), at) <= r) taken[y * G.Width + x] = true;
    }

    // The centre of the passable cell nearest `p` that no unit stands in.
    private System.Numerics.Vector2 Free(System.Numerics.Vector2 p)
    {
        int best = -1;
        float bestD = float.MaxValue;
        int cx = Math.Clamp((int)(p.X / MapConstants.CellSize), 0, G.Width - 1), cy = Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, G.Height - 1);
        for (int y = Math.Max(0, cy - 8); y <= Math.Min(G.Height - 1, cy + 8); y++)
            for (int x = Math.Max(0, cx - 8); x <= Math.Min(G.Width - 1, cx + 8); x++)
            {
                if (!G.IsPassable(x, y) || B.SlotAt(x, y) >= 0) continue;
                System.Numerics.Vector2 c = G.CellCenter(x, y);
                float d = System.Numerics.Vector2.Distance(c, p);
                if (d >= bestD) continue;
                bool clear = true;
                for (int i = 0; i < U.Capacity && clear; i++) if (U.Alive[i] && System.Numerics.Vector2.Distance(U.Position[i], c) < 1.2f) clear = false;
                if (clear) (best, bestD) = (y * G.Width + x, d);
            }
        Expect(best >= 0, $"no free cell near {p}");
        return G.CellCenter(best % G.Width, best / G.Width);
    }

    // ---- per tick and per frame ----

    private void OnTicked(Simulation sim)
    {
        World w = sim.World;
        foreach (Rts.Sim.Abilities.AbilityEvent e in w.AbilityEvents)
            if (e.Resolved) _resolves.Add((e.Caster, e.Ability, e.Point, w.TickNumber));
        // A swing starts when the attack cooldown is set again (it counts down to 0 and waits there).
        if (_duelA.Generation != 0 && w.Units.IsAlive(_duelA))
        {
            int cd = w.Units.CooldownTicks[_duelA.Index];
            if (cd > _cooldownA) _swingTicksA.Add(w.TickNumber);
            _cooldownA = cd;
        }
        if (_duelB.Generation != 0 && w.Units.IsAlive(_duelB))
        {
            int cd = w.Units.CooldownTicks[_duelB.Index];
            if (cd > _cooldownB) _swingTicksB.Add(w.TickNumber);
            _cooldownB = cd;
        }
    }

    // The fog rule on the frame just drawn: what the views drew for every enemy unit and building against the sim's fog.
    private void CheckFog()
    {
        if (_sim == null || _units == null || !IsInstanceValid(_match) || _match.IsQueuedForDeletion()) return;
        _fogFrames++;
        if (_approaching) _approachFrames++;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (U.Owner[i] == 0 && U.Alive[i]) continue;
            bool drawn = _units.IsShown(i) || _units.ViewOf(i) is { Visible: true };
            if (!U.Alive[i])
            {
                if (drawn) Violation($"dead unit {i} drawn");
                continue;
            }
            bool sees = W.Fog.CanSeeUnit(0, i);
            if (drawn && !sees) Violation($"enemy unit {i} ({_data.Units[U.TypeId[i]].Key}) drawn without sight");
            if (drawn) _enemyShownFrames++; else _enemyHiddenFrames++;
        }
        for (int k = 0; k < _combat.ShownBars; k++)
            if (U.Owner[_combat.BarSlot(k)] != 0 && !W.Fog.CanSeeUnit(0, _combat.BarSlot(k))) Violation($"an hp bar over hidden unit {_combat.BarSlot(k)}");
        for (int k = 0; k < _views.ShownMarkers; k++)
            if (!W.Fog.CanSeeUnit(0, _views.MarkerAt(k).Unit)) Violation($"a status marker over hidden unit {_views.MarkerAt(k).Unit}");
        for (int k = 0; k < _views.ShownBars; k++)
            if (!W.Fog.CanSeeUnit(0, _views.BarSlot(k))) Violation($"a cast bar over hidden unit {_views.BarSlot(k)}");
        for (int i = 0; i < B.Capacity; i++)
            if (B.Alive[i] && B.Owner[i] != 0 && _buildings.IsShown(i) && !W.Fog.CanSeeBuilding(0, i)) Violation($"enemy building {i} drawn without sight");
    }

    private void Violation(string what)
    {
        if (_fogViolations++ == 0) _firstViolation = $"tick {Tick}: {what}";
    }

    private int BurningEnemyMarkers() => EnemyMarkers(_burning);

    // Whether any live enemy unit burns in the sim, seen or not.
    private bool EnemyBurning()
    {
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 1 && HasStatus(i, _burning)) return true;
        return false;
    }

    private int BlindedEnemyMarkers() => EnemyMarkers(_blinded);

    private int EnemyMarkers(int status)
    {
        int n = 0;
        for (int k = 0; k < _views.ShownMarkers; k++)
        {
            Rts.Sim.ViewApi.StatusMark m = _views.MarkerAt(k);
            if (m.Status == status && U.Owner[m.Unit] == 1 && W.Fog.CanSeeUnit(0, m.Unit)) n++;
        }
        return n;
    }

    private int Marks(int unit, int status)
    {
        int n = 0;
        for (int k = 0; k < _views.ShownMarkers; k++)
            if (_views.MarkerAt(k).Unit == unit && _views.MarkerAt(k).Status == status) n++;
        return n;
    }

    private int OwnDiscs()
    {
        int n = 0;
        for (int k = 0; k < _views.ShownZones; k++) if (_views.ZoneDiscAt(k).Own) n++;
        return n;
    }

    private bool HasStatus(int unit, int status)
    {
        for (int k = 0; k < U.Statuses.Count[unit]; k++)
            if (U.Statuses.StatusId[unit * Rts.Sim.Abilities.StatusStore.PerUnit + k] == status && U.Statuses.TicksRemaining[unit * Rts.Sim.Abilities.StatusStore.PerUnit + k] > 0) return true;
        return false;
    }

    // A rubble marker drawn within 0.6 m of `c`, or -1.
    private int RubbleAt(System.Numerics.Vector2 c)
    {
        DeathMarkers dm = _combat.Markers;
        for (int k = 0; k < dm.Capacity; k++)
            if (dm.Active[k] && dm.IsBuilding[k] && System.Numerics.Vector2.Distance(dm.Position[k], c) < 0.6f && _combat.MarkerShown(k) == 2) return k;
        return -1;
    }

    // A passable spot `d` m from the building's centre (eight directions, the one nearest `from` first) whose screen pixel,
    // with the camera on it, is ground and not the building's box (so the right click is a Move); the spot and its pixel.
    private async Task<(System.Numerics.Vector2, Vector2)> StandBeside(System.Numerics.Vector2 c, BuildingDef def, System.Numerics.Vector2 from, float d, int skip)
    {
        var spots = new List<System.Numerics.Vector2>();
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4f;
            System.Numerics.Vector2 p = c + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            int x = (int)(p.X / MapConstants.CellSize), y = (int)(p.Y / MapConstants.CellSize);
            if (G.IsPassable(x, y) && B.SlotAt(x, y) < 0) spots.Add(G.CellCenter(x, y));
        }
        var ordered = spots.OrderBy(p => System.Numerics.Vector2.Distance(p, from)).ToList();
        for (int k = 0; k < ordered.Count; k++)
        {
            System.Numerics.Vector2 p = ordered[(k + skip) % ordered.Count];
            await LookAt(p);
            Vector2 px = Screen(p, 0f);
            if (_sel.ContextTarget(px, out _, out int hitBuilding) && hitBuilding < 0 && !_sel.EnemyAt(px, out _, out _)) return (p, px);
        }
        throw new StepFailed($"no ground spot {d} m from the {def.DisplayName} at {c} to right-click");
    }

    private int NearestEnemyBuilding(System.Numerics.Vector2 from)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < B.Capacity; i++)
        {
            if (!B.Alive[i] || B.Owner[i] != 1) continue;
            // The Town Hall last: its 2,400 hit points would keep the rubble waiting.
            float d = System.Numerics.Vector2.Distance(Centre(i), from) + (_data.Buildings[B.TypeId[i]].Slot == BuildingSlot.TownHall ? 1000f : 0f);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    // ---- input (everything goes through the viewport) ----

    private void Push(InputEvent e) => GetViewport().PushInput(e);

    private void Key(Godot.Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task LeftClick(Vector2 at)
    {
        await Hover(at);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task RightClick(Vector2 at)
    {
        await Hover(at);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Right });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private async Task BoxDrag(Vector2[] points)
    {
        Vector2 lo = points.Aggregate((a, b) => a.Min(b)) - new Vector2(14f, 14f), hi = points.Aggregate((a, b) => a.Max(b)) + new Vector2(14f, 14f);
        Expect(lo.X > 0 && lo.Y > 0 && hi.X < 1152 && hi.Y < 648 - 200, $"the army does not fit the screen: box {lo} - {hi}");
        await Hover(lo);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = lo, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseMotion { Position = (lo + hi) / 2f, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseMotion { Position = hi, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = hi });
        await Frames(1);
    }

    private async Task Hover(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at });
        await Frames(1);
    }

    // The camera: a left click on the minimap at the map point (the player's jump), then a frame for the camera to move.
    private async Task LookAt(System.Numerics.Vector2 point)
    {
        System.Numerics.Vector2 px = _mini.Fit.ToPixel(point);
        Vector2 at = _mini.GetGlobalRect().Position + new Vector2(px.X, px.Y);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ButtonMask = MouseButtonMask.Left });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        await Frames(2);
        Expect(System.Numerics.Vector2.Distance(_camera.Focus, point) < 1f, $"minimap click: camera at {_camera.Focus}, wanted {point}");
    }

    // Tab until the active subgroup is `type` (its abilities on the card's top row).
    private async Task TabTo(int type)
    {
        for (int k = 0; k <= _sel.Subgroups.Count && _sel.Subgroups.ActiveType != type; k++)
        {
            Key(Godot.Key.Tab);
            await Frames(1);
        }
        Expect(_sel.Subgroups.ActiveType == type, $"Tab never reached {_data.Units[type].DisplayName} (active {_sel.Subgroups.ActiveType}, {_sel.Subgroups.Count} subgroups)");
    }

    // A left click on the unit wherever it is: it alone is selected.
    private async Task SelectAlone(EntityHandle unit)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Expect(U.IsAlive(unit), $"unit {unit.Index} died before it was selected");
            await LookAt(U.Position[unit.Index]);
            await LeftClick(ScreenOf(unit.Index));
            await Frames(1);
            if (_sel.Selection.Count == 1 && _sel.Selection.Contains(unit)) return;
        }
        Expect(false, $"unit {unit.Index} not selected alone ({_sel.Selection.Count} units)");
    }

    // ---- waiting and checking ----

    private void Step(string name)
    {
        _stepNo++;
        _step = $"{_stepNo}. {name}";
        _stepStart = Tick;
    }

    private void Done()
    {
        if (_breakStep == _stepNo)
        {
            _breakStep = -1;
            Expect(false, "deliberately broken by --break");
        }
        GD.Print($"seed {_seed} step {_step}: done at tick {Tick} ({Tick - _stepStart} ticks; kills {W.Kills[0]}, losses {W.Losses[0]}; fog frames {_fogFrames}, violations {_fogViolations})");
        Expect(_fogViolations == 0, $"fog rule: {_firstViolation}");
    }

    private void Expect(bool ok, string message)
    {
        if (!ok) throw new StepFailed(message);
    }

    private async Task Until(Func<bool> done, string what, Func<string>? detail = null)
    {
        while (!done())
        {
            if (Tick > TickBudget) throw new StepFailed($"{what}: not by tick {TickBudget}{(detail != null ? "; " + detail() : "")}");
            await Frame();
        }
    }

    private async Task Ticks(int n)
    {
        int until = Tick + n;
        await Until(() => Tick >= until, $"{n} ticks");
        await Frame();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    // Every wait goes through here, so the fog rule is checked on every frame of the run.
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        CheckFog();
    }

    // Windowed only: the frame saved as m4-seedN-<name>.png in --shots.
    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        // These frames are drawn too, so they take the fog check like every other (BUG-0462).
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            CheckFog();
        }
        string path = $"{_shots}/m4-seed{_seed}-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
    }

    // The recorded match so far as a replay file in the user data folder (a failure's repro: `Rts.Cli play <file>`).
    private void SaveReplay(string why)
    {
        if (_runner?.Recorder == null) return;
        string path = System.IO.Path.Combine(OS.GetUserDataDir(), $"m4playable-seed{_seed}-{why}-tick{Tick}.replay");
        ReplayFormat.WriteFile(_runner.Recorder.ToReplay(), path);
        GD.Print($"  replay saved: {path}");
    }

    // ---- geometry ----

    private System.Numerics.Vector2 Centre(int building) => SelectionController.SiteCenter(W, building);

    private System.Numerics.Vector2 Mean(List<EntityHandle> units)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        foreach (EntityHandle h in units) if (U.IsAlive(h)) { sum += U.Position[h.Index]; n++; }
        return sum / Math.Max(1, n);
    }

    private int CellOf(System.Numerics.Vector2 p) =>
        Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, G.Height - 1) * G.Width + Math.Clamp((int)(p.X / MapConstants.CellSize), 0, G.Width - 1);

    private Vector2 Screen(System.Numerics.Vector2 p, float up) =>
        _camera.UnprojectPosition(new Vector3(p.X, TerrainHeight.At(W.Heightmap, p.X, p.Y) + up, p.Y));

    private Vector2 ScreenOf(int unit) => _sel.TryScreenPosition(unit, out Vector2 s) ? s : throw new StepFailed($"unit {unit} behind the camera");

    private Godot.Key KeyOf(int cell) => Enum.Parse<Godot.Key>(_card.GridKey(cell));
}
