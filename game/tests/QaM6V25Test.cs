using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V25: the new figures' casts and the Sapper's attack in a real match, driven by the match's own <see cref="SimRunner"/>
/// at a fixed frame delta (1/60 s), so the run is the same however loaded the machine is.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V25Test.tscn</c>; prints
/// "QA M6V25 TEST PASS". Rows, on the shipped data, for each type with an ability (the Cadre Mage's Telas Fire, the Priest's
/// Sandstorm, the Sapper's Cusser; found by id): (A) game speed 0.5, 1 and 2: the cast clip starts within one frame of 0, never
/// steps back, ends within one frame of its end, then idle; (B) the tree paused mid-cast for 30 frames: the clip holds still
/// and the sim does not tick; unpaused it carries on from there and ends at its end. (C) The Sapper attacking a held enemy:
/// it plays its attack clip (the library's throw) while it attacks, and every shot it fires is printed against the throw:
/// the clip's phase when the shot first exists, the release (the throwing hand's fastest frame, measured on the clip), and
/// the clip's loops per shot. (C) asserts only what the view promises (the attack clip while attacking, shots fired); the
/// timing is a measurement for the report (BUG-1601). (D) Each of the four new types' attack clip fits its data attack:
/// melee -> attack_melee, a magic bolt -> attack_cast, any other projectile -> a ranged clip.
/// </remarks>
public partial class QaM6V25Test : Node
{
    private const string Tag = "QA M6V25 TEST";
    private const double FrameDelta = 1.0 / 60.0;
    private readonly List<string> _failures = new();

    private static readonly (string Unit, string Ability)[] Casters =
    {
        ("malazan_cadre_mage", "telas_fire"), ("whirlwind_priest", "sandstorm"), ("malazan_sapper", "cusser"),
    };

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the pause row pauses the tree under it
        try
        {
            UnitViews.UseModels = true;
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            GameData data = loaded.Data!;
            foreach ((string unit, string ability) in Casters)
            {
                foreach (double speed in new[] { 0.5, 1.0, 2.0 }) await SpeedRow(data, unit, ability, speed);
                await PauseRow(data, unit, ability);
            }
            await ThrowRow(data);
            FitRow(data);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        GetTree().Paused = false;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private sealed class Stage
    {
        public Match Match = null!;
        public SimRunner Runner = null!;
        public Simulation Sim = null!;
        public UnitViews Views = null!;
        public GameData Data = null!;
        public string Id = "";
        public int Type, Ability = -1, Index = -1, Slot = -1;
        public UnitStore U => Sim.World.Units;
        public AnimationPlayer Anim => Views.AnimatorOf(Slot)!;
        public float CastLength => Views.Models!.ClipLength(Type, UnitModels.Clip.Cast);
        public int CastTicks => Data.Abilities[Ability].CastTicks;
    }

    // A match ticked by its own runner at a fixed frame delta; a Heavy Infantry first, so the type under test is in slot 1.
    private async Task<Stage> Begin(GameData data, string unitId, string? abilityId, double speed, bool combat)
    {
        var s = new Stage { Data = data, Id = unitId, Type = data.FindUnit(unitId), Ability = abilityId == null ? -1 : data.FindAbility(abilityId) };
        s.Match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(s.Match);
        s.Match.ProcessMode = ProcessModeEnum.Pausable;
        var flags = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute" };
        if (!combat) flags.Add("--no-combat");
        s.Match.Start(data, LaunchOptions.Parse(flags.ToArray()));
        s.Runner = s.Match.GetNode<SimRunner>("SimRunner");
        s.Runner.FixedFrameDelta = FrameDelta;
        s.Runner.GameSpeed = speed;
        s.Match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        s.Sim = s.Runner.Simulation!;
        s.Views = s.Match.GetNode<UnitViews>("World3D/UnitViews");
        World w = s.Sim.World;
        NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
        s.Sim.Enqueue(Command.SpawnUnit(0, data.FindUnit("malazan_heavy_infantry"), mid + new NVector2(-4f, 0f)));
        s.Sim.Enqueue(Command.SpawnUnit(0, s.Type, mid));
        for (int k = 0; k < 40; k++) await Frame();
        for (int i = 0; i < s.U.Capacity && s.Slot < 0; i++) if (s.U.Alive[i] && s.U.TypeId[i] == s.Type) s.Slot = i;
        if (s.Ability >= 0) s.Index = data.Units[s.Type].Abilities.IndexOf(s.Ability);
        if (!Check(s.Slot >= 0 && s.Views.AnimatorOf(s.Slot) != null && (abilityId == null || s.Index >= 0), $"{unitId}: no model / {abilityId}")) s.Slot = -1;
        return s;
    }

    // Aimed 5 m east: inside every one of the three abilities' ranges, and the Cusser's 3.5 m blast misses the Heavy Infantry
    // 4 m west of the Sapper and the Sapper itself.
    private static void Cast(Stage s) =>
        s.Sim.Enqueue(Command.UseAbility(0, new EntityHandle(s.Slot, s.U.Generation[s.Slot]), s.Index, s.U.Position[s.Slot] + new NVector2(5f, 0f)));

    private async Task End(Stage s)
    {
        GetTree().Paused = false;
        RemoveChild(s.Match);
        s.Match.QueueFree();
        await Frame();
    }

    private async Task<List<float>> RecordCast(Stage s, int limit = 600)
    {
        var positions = new List<float>();
        for (int f = 0; f < limit && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
        for (int f = 0; f < limit && s.U.State[s.Slot] == UnitState.Casting; f++)
        {
            if (s.Anim.CurrentAnimation == "attack_cast") positions.Add((float)s.Anim.CurrentAnimationPosition);
            await Frame();
        }
        return positions;
    }

    // (A) game speed: one frame's worth of clip at `speed` is length / (seen ticks * frames per tick).
    private async Task SpeedRow(GameData data, string unitId, string abilityId, double speed)
    {
        Stage s = await Begin(data, unitId, abilityId, speed, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            List<float> p = await RecordCast(s);
            await Frame();
            string after = s.Anim.CurrentAnimation;
            double framesPerTick = 3.0 / speed;
            float step = (float)(s.CastLength / ((s.CastTicks - 1) * framesPerTick));
            int back = 0;
            for (int k = 1; k < p.Count; k++) if (p[k] < p[k - 1] - 1e-5f) back++;
            float first = p.Count > 0 ? p[0] : -1f, last = p.Count > 0 ? p[^1] : -1f;
            string source = s.Views.Models!.ClipSource(s.Type, UnitModels.Clip.Cast) ?? "";
            GD.Print($"(A) {unitId} {abilityId} ({s.CastTicks} ticks, clip {source}) at speed {speed}: {p.Count} frames, first {first:F3} s, last {last:F3} s of {s.CastLength:F3}, {back} steps back, frame step {step:F3} s; then '{after}'");
            Check(p.Count > 0 && first <= step + 1e-3f, $"(A) {unitId} x{speed}: the clip starts at {first:F3} s, want 0 within one frame ({step:F3})");
            Check(back == 0, $"(A) {unitId} x{speed}: the clip stepped back {back} times");
            Check(last >= s.CastLength - step - 2e-3f && last < s.CastLength, $"(A) {unitId} x{speed}: last cast frame at {last:F3} s, want within one frame of {s.CastLength:F3}");
            Check(after == "idle", $"(A) {unitId} x{speed}: after the cast plays '{after}', want idle");
        }
        finally { await End(s); }
    }

    // (B) paused half way through the cast: the clip holds still; unpaused it carries on and ends at its end.
    private async Task PauseRow(GameData data, string unitId, string abilityId)
    {
        Stage s = await Begin(data, unitId, abilityId, 1.0, false);
        try
        {
            if (s.Slot < 0) return;
            Cast(s);
            for (int f = 0; f < 200 && s.U.State[s.Slot] != UnitState.Casting; f++) await Frame();
            for (int f = 0; f < (s.CastTicks - 1) * 3 / 2; f++) await Frame();
            if (!Check(s.U.State[s.Slot] == UnitState.Casting, $"(B) {unitId}: not casting half way through ({s.U.State[s.Slot]})")) return;
            float before = (float)s.Anim.CurrentAnimationPosition;
            long tick = s.Sim.TickNumber;
            GetTree().Paused = true;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int f = 0; f < 30; f++)
            {
                await Frame();
                float at = (float)s.Anim.CurrentAnimationPosition;
                lo = Math.Min(lo, at);
                hi = Math.Max(hi, at);
            }
            bool ticked = s.Sim.TickNumber != tick;
            GetTree().Paused = false;
            List<float> p = await RecordCast(s);
            float last = p.Count > 0 ? p[^1] : -1f;
            int back = 0;
            for (int k = 1; k < p.Count; k++) if (p[k] < p[k - 1] - 1e-5f) back++;
            GD.Print($"(B) {unitId} paused at {before:F3} s of {s.CastLength:F3} for 30 frames: clip {lo:F3}-{hi:F3} s, sim ticked {ticked}; unpaused: {p.Count} frames from {(p.Count > 0 ? p[0] : -1f):F3} to {last:F3} s, {back} steps back");
            Check(before > 0.1f * s.CastLength && before < 0.9f * s.CastLength, $"(B) {unitId}: paused at {before:F3} s, not mid-clip");
            Check(!ticked && Math.Abs(hi - before) < 1e-4f && Math.Abs(lo - before) < 1e-4f, $"(B) {unitId} paused: the clip moved {lo:F3}-{hi:F3} s from {before:F3} (sim ticked {ticked})");
            Check(p.Count > 0 && p[0] >= before - 1e-4f && back == 0, $"(B) {unitId} unpaused: the clip went back from {before:F3} or stepped back {back} times");
            Check(last >= s.CastLength - 0.05f && last < s.CastLength, $"(B) {unitId} unpaused: the cast ends at {last:F3} s of the clip, want its end");
        }
        finally { await End(s); }
    }

    // (C) The Sapper attacks an enemy Heavy Infantry told to hold 7 m away (the Sapper's range is 8, the enemy's 0.5: it never
    // reaches). Per frame: the Sapper's state and clip; per new shot of player 0: the clip's phase then.
    private async Task ThrowRow(GameData data)
    {
        Stage s = await Begin(data, "malazan_sapper", null, 1.0, true);
        try
        {
            if (s.Slot < 0) return;
            UnitStore u = s.U;
            // The Heavy Infantry spawned first belongs to player 0; free it, so only the Sapper shoots for player 0.
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && i != s.Slot) u.Free(new EntityHandle(i, u.Generation[i]));
            int heavy = data.FindUnit("malazan_heavy_infantry");
            s.Sim.Enqueue(Command.SpawnUnit(1, heavy, u.Position[s.Slot] + new NVector2(7f, 0f)));
            for (int f = 0; f < 6; f++) await Frame();
            int foe = -1;
            for (int i = 0; i < u.Capacity && foe < 0; i++) if (u.Alive[i] && u.Owner[i] == 1) foe = i;
            if (!Check(foe >= 0, "(C) no enemy spawned")) return;
            s.Sim.Enqueue(Command.HoldPosition(1, new EntityHandle(foe, u.Generation[foe])));
            s.Sim.Enqueue(Command.Attack(0, new EntityHandle(s.Slot, u.Generation[s.Slot]), new EntityHandle(foe, u.Generation[foe]), isBuilding: false));
            float length = s.Views.Models!.ClipLength(s.Type, UnitModels.Clip.Attack);
            string source = s.Views.Models.ClipSource(s.Type, UnitModels.Clip.Attack) ?? "";
            ProjectileStore shots = s.Sim.World.Projectiles;
            var seen = new bool[shots.Capacity];
            var phases = new List<float>();
            var shotFrames = new List<int>();
            int attackFrames = 0, wrongClip = 0, wraps = 0;
            float prev = -1f;
            for (int f = 0; f < 60 * 14 && u.Alive[foe] && u.Alive[s.Slot]; f++)
            {
                await Frame();
                bool attacking = u.State[s.Slot] == UnitState.Attacking;
                AnimationPlayer anim = s.Anim;
                float pos = (float)anim.CurrentAnimationPosition;
                if (attacking)
                {
                    attackFrames++;
                    if (anim.CurrentAnimation != "attack") wrongClip++;
                    if (prev >= 0f && pos < prev - 1e-4f) wraps++;
                    prev = pos;
                }
                for (int n = NewShots(shots, seen); n > 0; n--)
                {
                    phases.Add(length > 0f ? pos / length : -1f);
                    shotFrames.Add(f);
                }
            }
            float release = ReleasePhase(s);
            var gaps = new List<string>();
            for (int k = 1; k < shotFrames.Count; k++) gaps.Add((shotFrames[k] - shotFrames[k - 1]).ToString());
            float spreadLo = float.MaxValue, spreadHi = float.MinValue;
            foreach (float p in phases) { spreadLo = Math.Min(spreadLo, p); spreadHi = Math.Max(spreadHi, p); }
            UnitDef def = data.Units[s.Type];
            GD.Print($"(C) malazan_sapper attack clip '{source}' {length:F2} s; data windup {def.Attack.WindupTicks} ticks, cooldown {def.Attack.CooldownTicks} ticks; "
                     + $"{attackFrames} frames attacking, {wrongClip} not on 'attack', {wraps} clip loops, {phases.Count} shots");
            GD.Print($"(C) shot frames [{string.Join(' ', shotFrames)}], gaps [{string.Join(' ', gaps)}] frames; the throw's phase when each shot first exists "
                     + $"[{string.Join(' ', phases.ConvertAll(p => p.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}] "
                     + $"(spread {spreadLo:F2}-{spreadHi:F2}); the release (throwing hand fastest) at phase {release:F2}; "
                     + $"{(phases.Count > 0 ? (float)wraps / phases.Count : 0f):F2} throws per shot");
            Check(phases.Count >= 2, $"(C) the Sapper fired {phases.Count} shots in 14 s");
            Check(attackFrames > 0 && wrongClip == 0, $"(C) {wrongClip} of {attackFrames} attacking frames not on the attack clip");
        }
        finally { await End(s); }
    }

    // (D) The attack clip fits the data's attack (the brief: "the clip that fits their attack in the data"), which FootModelTest
    // doesn't check (it reads the expectation from the manifest itself): no projectile -> the library's attack_melee; a magic
    // projectile -> attack_cast; any other projectile -> a ranged clip (attack_throw or attack_shoot), never the melee swing.
    private void FitRow(GameData data)
    {
        var models = new UnitModels(data, new uint[] { 0xC03030, 0x3060D0 });
        foreach (string id in new[] { "malazan_cadre_mage", "malazan_sapper", "whirlwind_priest", "whirlwind_zealot" })
        {
            int t = data.FindUnit(id);
            if (!Check(t >= 0 && models.Has(t), $"(D) {id}: no model")) continue;
            AttackDef a = data.Units[t].Attack;
            string source = models.ClipSource(t, UnitModels.Clip.Attack) ?? "";
            bool ranged = a.Projectile != null;
            string type = data.DamageTable.DamageTypeKeys[a.DamageType];
            bool magic = type == "magic";
            string want = !ranged ? "attack_melee" : magic ? "attack_cast" : "attack_throw|attack_shoot";
            bool ok = !ranged ? source == "attack_melee" : magic ? source == "attack_cast" : source is "attack_throw" or "attack_shoot";
            GD.Print($"(D) {id}: attack {type}, projectile {a.Projectile ?? "none"}: attack clip = library {source} (want {want})");
            Check(ok, $"(D) {id}: attack clip {source}, want {want}");
        }
    }

    // Player 0's shots alive now that weren't last frame (a freed slot forgets it was seen).
    private static int NewShots(ProjectileStore shots, bool[] seen)
    {
        ReadOnlySpan<bool> alive = shots.Alive;
        ReadOnlySpan<int> owner = shots.Owner;
        int added = 0;
        for (int k = 0; k < alive.Length; k++)
        {
            if (!alive[k]) { seen[k] = false; continue; }
            if (seen[k] || owner[k] != 0) continue;
            seen[k] = true;
            added++;
        }
        return added;
    }

    // The phase (0-1) of the Sapper's attack clip where its right hand moves fastest (the throw's release), sampled on its
    // own skeleton at 120 points. -1 when there is no skeleton or hand bone.
    private static float ReleasePhase(Stage s)
    {
        if (s.Slot < 0 || !s.U.Alive[s.Slot]) return -1f;
        AnimationPlayer anim = s.Anim;
        Node? model = anim.GetParent();
        var bones = model?.FindChildren("*", "Skeleton3D", true, false);
        if (bones == null || bones.Count == 0 || bones[0] is not Skeleton3D skeleton) return -1f;
        int hand = skeleton.FindBone("hand_r");
        if (hand < 0) return -1f;
        float length = s.Views.Models!.ClipLength(s.Type, UnitModels.Clip.Attack);
        anim.Play("attack", 0.0); // no blend from the clip before, so each sample is the clip's own pose
        const int n = 120;
        var at = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            anim.Seek(length * k / n, true);
            at[k] = skeleton.GetBoneGlobalPose(hand).Origin;
        }
        int best = 0;
        float fastest = -1f;
        for (int k = 1; k < n; k++)
        {
            float v = (at[k] - at[k - 1]).Length();
            if (v > fastest) { fastest = v; best = k; }
        }
        return (float)best / n;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
