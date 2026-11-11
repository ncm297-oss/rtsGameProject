using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M7-VH1 (session 2026-11-10-2116).
/// <list type="bullet">
/// <item>(1) <see cref="TooltipWrap"/> under BUG-1126's ruling, checked by an oracle written apart from the developer's
/// <c>TooltipWrapRule</c>: each data line's words land, in order, on one or more whole output lines (no output line without a
/// word unless its data line had none); only the text's own first lead / last trail spaces survive at a line edge; the gaps
/// between words on a line are the text's; a line over the width is one word; and every added break is greedy (the line,
/// the consumed gap and the next word would not have fit). 30,000 seeded strings at widths 1-80, plus every shipped
/// <c>description</c> at the shipped width.</item>
/// <item>(2) The difficulty tips' <c>{1}</c>: <see cref="UiText.DifficultyTip"/> at the seconds the loader keeps for
/// <c>thinkInterval</c> 0.333 / 2 / 7 (7 / 40 / 140 ticks) reads "every 0.35 / 2 / 7 seconds" in Easy and Hard; each change
/// of the interval changes the text; Normal's shipped tip is the M5-V2 sentence whatever the interval; the shipped data's
/// <see cref="SkirmishSetup.ThinkSeconds"/> is 2 / 1 / 0.5.</item>
/// <item>(3) Landing flashes: in a mixed-scale brawl (forged scales) with a watchtower per side and the view collecting
/// every tick, every new flash mark's height equals its shot's true victim height (the dead slot of the same aim point,
/// owner and type), the hash moving never.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM7Vh1Test.tscn</c>; prints "QA M7VH1 TEST PASS" or
/// "QA M7VH1 TEST FAIL: ..." lines.
/// </summary>
public partial class QaM7Vh1Test : Node
{
    private static readonly (string Faction, string Id, float Scale)[] Scales =
    {
        ("malazan", "malazan_heavy_infantry", 1.5f), ("malazan", "malazan_crossbowman", 0.5f),
        ("whirlwind", "whirlwind_desert_archer", 3.0f), ("whirlwind", "whirlwind_raider", 0.75f),
        ("malazan", "malazan_cadre_mage", 1.5f), ("whirlwind", "whirlwind_priest", 2.0f),
    };

    private static readonly PropertyInfo VictimP = typeof(ProjectileStore).GetProperty("Victim", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo VictimIsBuildingP = typeof(ProjectileStore).GetProperty("VictimIsBuilding", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7vh1-{OS.GetProcessId()}"));
        try
        {
            await Frame();
            WrapOracleFuzz();
            ShippedDescriptions();
            Tips();
            await Flashes(root);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7VH1 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7VH1 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) the wrap oracle ----

    private void WrapOracleFuzz()
    {
        var rng = new Random(20261110);
        int failures = 0, overWidth = 0, added = 0;
        for (int n = 0; n < 30000; n++)
        {
            var sb = new StringBuilder();
            if (rng.Next(4) == 0) sb.Append(' ', 1 + rng.Next(3));
            int words = rng.Next(0, 14);
            for (int k = 0; k < words; k++)
            {
                if (k > 0)
                {
                    int r = rng.Next(10);
                    if (r < 2)
                    {
                        // A data line break, with or without spaces beside it, sometimes two in a row.
                        if (rng.Next(2) == 0) sb.Append(' ', 1 + rng.Next(2));
                        sb.Append('\n', 1 + (rng.Next(4) == 0 ? 1 : 0));
                        if (rng.Next(2) == 0) sb.Append(' ', 1 + rng.Next(2));
                    }
                    else sb.Append(' ', r < 7 ? 1 : r < 9 ? 2 : 3);
                }
                int len = rng.Next(8) == 0 ? 1 + rng.Next(90) : 1 + rng.Next(12);
                for (int c = 0; c < len; c++) sb.Append((char)('a' + rng.Next(26)));
            }
            if (rng.Next(4) == 0) sb.Append(' ', 1 + rng.Next(3));
            if (rng.Next(8) == 0) sb.Append('\n');
            if (rng.Next(8) == 0) sb.Insert(0, "\n");
            string text = sb.ToString();
            int width = 1 + rng.Next(80);
            string w = TooltipWrap.Wrap(text, width);
            string? why = Oracle(text, w, width, ref overWidth, ref added);
            if (why != null && failures++ < 8) Check(false, $"wrap oracle {n} width {width}: {why}: in '{Esc(text)}' out '{Esc(w)}'");
        }
        GD.Print($"  wrap oracle fuzz: 30000 strings, {failures} failures, {added} added breaks, {overWidth} single-word lines over the width");
        Check(added > 10000 && overWidth > 100, $"the fuzz exercised too little: {added} added breaks, {overWidth} long words");
    }

    private void ShippedDescriptions()
    {
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        Check(loaded.Ok, "shipped data failed to load");
        if (!loaded.Ok) return;
        GameData d = loaded.Data!;
        int n = 0, o = 0, a = 0;
        IEnumerable<string> texts = d.Units.Select(u => u.Description).Concat(d.Buildings.Select(b => b.Description));
        foreach (string t in texts)
        {
            if (string.IsNullOrEmpty(t)) continue;
            n++;
            string w = TooltipWrap.Wrap(t);
            string? why = Oracle(t, w, TooltipWrap.Width, ref o, ref a);
            Check(why == null, $"shipped description: {why}: '{Esc(w)}'");
            Check(w.Replace('\n', ' ') == t, $"shipped description changed beyond its breaks: '{Esc(w)}'");
        }
        GD.Print($"  shipped descriptions: {n} wrapped under the oracle ({a} added breaks)");
        Check(n > 10, $"only {n} shipped descriptions");
    }

    // Null when `w` is a valid wrap of `text` at `width` under the ruling (see the class summary).
    private static string? Oracle(string text, string w, int width, ref int overWidth, ref int addedBreaks)
    {
        if (text.Length == 0) return w.Length == 0 ? null : "empty text, non-empty output";
        string[] data = text.Split('\n');
        string[] lines = w.Split('\n');
        if (data.Length == 1 && data[0].Trim(' ').Length == 0) return w == text ? null : "an all-space text changed";
        int li = 0;
        for (int di = 0; di < data.Length; di++)
        {
            string d = data[di];
            string[] dw = d.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (dw.Length == 0)
            {
                if (li >= lines.Length) return $"data line {di} (no word) has no output line";
                if (lines[li].Length != 0) return $"data line {di} (spaces only, beside a break) is output line {li} '{Esc(lines[li])}', not empty";
                li++;
                continue;
            }
            int lead = d.Length - d.TrimStart(' ').Length, trail = d.Length - d.TrimEnd(' ').Length;
            // Gaps between this data line's words, in order.
            var gaps = new List<int>();
            {
                int i = lead;
                while (i < d.Length - trail)
                {
                    while (i < d.Length && d[i] != ' ') i++;
                    int g = 0;
                    while (i < d.Length - trail && d[i] == ' ') { g++; i++; }
                    if (g > 0) gaps.Add(g);
                }
            }
            int taken = 0, first = li;
            while (taken < dw.Length)
            {
                if (li >= lines.Length) return $"data line {di}: output ran out after {taken} of {dw.Length} words";
                string l = lines[li];
                bool firstOut = li == first, keepLead = di == 0 && firstOut;
                int lLead = l.Length - l.TrimStart(' ').Length, lTrail = l.Length - l.TrimEnd(' ').Length;
                string core = l.Trim(' ');
                if (core.Length == 0) return $"output line {li} is blank inside data line {di}";
                if (lLead > 0 && !(keepLead && lLead == lead)) return $"output line {li} starts with {lLead} spaces (text edge {lead}, kept here {keepLead})";
                string[] lw = core.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int k = 0; k < lw.Length; k++)
                    if (taken + k >= dw.Length || lw[k] != dw[taken + k]) return $"output line {li} word {k} '{lw[k]}' is not data line {di}'s next";
                // Gaps on the line are the text's.
                int pos = 0;
                for (int k = 0; k + 1 < lw.Length; k++)
                {
                    pos += lw[k].Length;
                    int g = 0;
                    while (core[pos + g] == ' ') g++;
                    if (g != gaps[taken + k]) return $"output line {li}: gap {g} after '{lw[k]}', the text's is {gaps[taken + k]}";
                    pos += g;
                }
                taken += lw.Length;
                bool lastOut = taken == dw.Length, keepTrail = di == data.Length - 1 && lastOut;
                if (lTrail > 0 && !(keepTrail && lTrail == trail)) return $"output line {li} ends with {lTrail} spaces (text edge {trail}, kept here {keepTrail})";
                if (core.Length > width)
                {
                    if (lw.Length > 1) return $"output line {li} of {core.Length} (> {width}) holds {lw.Length} words";
                    overWidth++;
                }
                if (!lastOut)
                {
                    // An added break: greedy (the gap the break consumed and the next word would not have fit).
                    addedBreaks++;
                    int need = core.Length + gaps[taken - 1] + dw[taken].Length;
                    if (need <= width) return $"a break after output line {li} ({core.Length}) though the next word fit ({need} <= {width})";
                }
                li++;
            }
        }
        return li == lines.Length ? null : $"output has {lines.Length - li} lines past the text";
    }

    private static string Esc(string s) => s.Replace("\n", "\\n");

    // ---- (2) the tips' {1} ----

    private void Tips()
    {
        UiText ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
        string normalShipped = "Fair play at a relaxed pace: follows its build order on time and uses abilities, but doesn't focus fire or pull back wounded units.";
        var seen = new HashSet<string>();
        foreach ((double interval, string want) in new[] { (0.333, "0.35"), (2.0, "2"), (7.0, "7") })
        {
            int ticks = (int)Math.Round(interval * SimConstants.TicksPerSecond, MidpointRounding.AwayFromZero);
            float s = ticks / (float)SimConstants.TicksPerSecond;
            string easy = ui.DifficultyTip(AiDifficulty.Easy, 0f, s), hard = ui.DifficultyTip(AiDifficulty.Hard, 0.25f, s);
            string normal = ui.DifficultyTip(AiDifficulty.Normal, 0f, s);
            Check(easy.StartsWith($"Thinks every {want} seconds,", StringComparison.Ordinal), $"Easy at {interval} s: '{easy}'");
            Check(hard.Contains($"thinks every {want} seconds,", StringComparison.Ordinal) && hard.Contains("gathers 25% faster", StringComparison.Ordinal), $"Hard at {interval} s: '{hard}'");
            Check(normal == normalShipped, $"Normal at {interval} s: '{normal}'");
            Check(seen.Add(easy) && seen.Add(hard), $"the {interval} s tips equal another interval's");
            GD.Print($"  thinkInterval {interval}: Easy '{easy[..30]}...', Hard '...{hard.Substring(hard.IndexOf("thinks", StringComparison.Ordinal), 26)}...'");
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) return;
        for (int f = 0; f < loaded.Data!.Factions.Length; f++)
        {
            float e = SkirmishSetup.ThinkSeconds(loaded.Data, f, AiDifficulty.Easy), n = SkirmishSetup.ThinkSeconds(loaded.Data, f, AiDifficulty.Normal),
                h = SkirmishSetup.ThinkSeconds(loaded.Data, f, AiDifficulty.Hard);
            Check(e == 2f && n == 1f && h == 0.5f, $"shipped faction {f} think seconds {e} / {n} / {h}, want 2 / 1 / 0.5");
            Check(ui.DifficultyTip(AiDifficulty.Easy, 0f, e).StartsWith("Thinks every 2 seconds, follows its build order slowly", StringComparison.Ordinal), $"shipped faction {f} Easy tip");
        }
        // M7-V3a (BUG-1200): an interval of exactly 1 s reads "1 second" (setup.seconds_one); it was "1 seconds".
        string one = ui.DifficultyTip(AiDifficulty.Easy, 0f, 1f);
        Check(one.StartsWith("Thinks every 1 second, ", StringComparison.Ordinal), $"an Easy interval of exactly 1 s reads '{one}'");
        GD.Print($"  an Easy interval of exactly 1 s reads: '{one[..24]}...'");
    }

    // ---- (3) landing flashes ----

    private async Task Flashes(string root)
    {
        GameData data = Forge(ProjectSettings.GlobalizePath("res://data"), root);
        GetTree().Root.Size = new Vector2I(1152, 648);
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        match.Start(data, LaunchOptions.Parse(new[] { "--seed", "9", "--units", "0", "--mute", "--no-fog" }));
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Simulation sim = runner.Simulation!;
        ProjectileViews shots = match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        void Tick() { sim.Tick(); shots.CollectTick(w); }
        Tick();
        Tick();

        string[][] mix =
        {
            new[] { "malazan_crossbowman", "malazan_heavy_infantry", "malazan_crossbowman", "malazan_cadre_mage", "malazan_catapult", "malazan_crossbowman" },
            new[] { "whirlwind_desert_archer", "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_desert_archer" },
        };
        int center = FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        int cx = center % g.Width, cy = center / g.Width;
        FlowField field = FlowField.Build(g, center);
        for (int pl = 0; pl < 2; pl++)
        {
            int dir = pl == 0 ? -1 : 1, placed = 0;
            for (int dx = 3; dx < g.Width && placed < 120; dx++)
                for (int dy = 0; dy < 41 && placed < 120; dy++)
                {
                    int x = cx + dir * dx, y = cy + (dy % 2 == 0 ? dy / 2 : -(dy + 1) / 2);
                    if ((uint)x >= (uint)g.Width || (uint)y >= (uint)g.Height || !(field.CostAt(y * g.Width + x) < 200f)) continue;
                    sim.Enqueue(Command.SpawnUnit(pl, data.FindUnit(mix[pl][placed % mix[pl].Length]), g.CellCenter(x, y)));
                    placed++;
                }
        }
        int tower = data.FindBuilding("malazan_watchtower");
        for (int pl = 0; pl < 2 && tower >= 0; pl++)
            for (int r = 0; r < 20; r++)
            {
                int x = cx + (pl == 0 ? -3 : 3), y = cy + 24 + r;
                if ((uint)y < (uint)g.Height && w.Buildings.Fits(tower, y * g.Width + x))
                {
                    sim.Enqueue(Command.SpawnBuilding(pl, tower, g.CellCenter(x, y)));
                    break;
                }
            }
        Tick();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] < 2 && data.Units[u.TypeId[i]].Slot != UnitSlot.Worker)
                sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(cx + (u.Owner[i] == 0 ? 30 : -30), cy)));

        ProjectileStore p = w.Projectiles;
        var victim = (EntityHandle[])VictimP.GetValue(p)!;
        var victimIsBuilding = (bool[])VictimIsBuildingP.GetValue(p)!;
        int cap = p.Capacity;
        var was = new bool[cap];
        var want = new float[cap];
        var aim = new System.Numerics.Vector2[cap];
        var owner = new int[cap];
        var type = new int[cap];
        var dead = new List<int>();
        int flashes = 0, checkedFlashes = 0, wrong = 0, ambiguous = 0, unmatched = 0, offDefault = 0, hashMoved = 0;
        for (int t = 0; t < 900; t++)
        {
            ulong h0 = sim.StateHash();
            Tick();
            shots.Sync(w, 0.5f);
            // CollectTick ran after this tick: compare the hash with a second read after the views.
            ulong h1 = sim.StateHash();
            shots.Sync(w, 0.7f);
            if (sim.StateHash() != h1) hashMoved++;
            _ = h0;
            dead.Clear();
            for (int i = 0; i < cap; i++)
            {
                if (was[i] && !p.Alive[i]) dead.Add(i);
                if (p.Alive[i] && !was[i])
                {
                    EntityHandle v = victim[i];
                    bool corpse = !victimIsBuilding[i] && !u.Alive[v.Index] && u.Generation[v.Index] == v.Generation + 1;
                    want[i] = !victimIsBuilding[i] && (u.IsAlive(v) || corpse) ? ProjectileViews.HeightFor(data.Units[u.TypeId[v.Index]].Scale) : ProjectileViews.LaunchHeight;
                    owner[i] = p.Owner[i];
                    type[i] = p.ProjectileTypeId[i];
                }
                if (p.Alive[i]) aim[i] = p.Target[i];
                was[i] = p.Alive[i];
            }
            ImpactMarks m = shots.Marks;
            for (int k = 0; k < m.Capacity; k++)
            {
                if (!m.Active[k] || m.StartTick[k] != w.TickNumber || m.Kind[k] != ImpactMarkKind.Flash) continue;
                flashes++;
                float? truth = null;
                bool clash = false;
                foreach (int i in dead)
                {
                    if (aim[i] != m.Position[k] || owner[i] != m.Owner[k] || type[i] != m.ProjectileType[k]) continue;
                    if (truth != null && truth != want[i]) clash = true;
                    truth = want[i];
                }
                if (truth == null) { unmatched++; continue; }
                if (clash) { ambiguous++; continue; }
                checkedFlashes++;
                if (shots.MarkFlashHeight(k) != truth.Value) wrong++;
                if (truth.Value != ProjectileViews.LaunchHeight) offDefault++;
            }
            if (u.Count < 40) break;
        }
        GD.Print($"  landing flashes: {flashes} new, {checkedFlashes} checked ({offDefault} above or below 1.2 m), {wrong} wrong, {ambiguous} ambiguous, {unmatched} unmatched; hash moved by a Sync {hashMoved}");
        Check(checkedFlashes > 200 && offDefault > 100, $"too few flashes checked: {checkedFlashes} ({offDefault} off the default)");
        Check(wrong == 0, $"{wrong} of {checkedFlashes} landing flashes not at their shot's victim height");
        Check(unmatched == 0, $"{unmatched} flashes with no dead slot to match");
        Check(hashMoved == 0, $"a Sync moved the hash on {hashMoved} ticks");
        RemoveChild(match);
        match.QueueFree();
        await Frame();
    }

    private static GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        foreach (string faction in Scales.Select(s => s.Faction).Distinct())
        {
            string path = Path.Combine(copy, "factions", faction, "units.json");
            JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
            JsonArray list = units["units"]!.AsArray();
            foreach ((string f, string id, float scale) in Scales)
                if (f == faction) list.First(n => (string?)n!["id"] == id)!["scale"] = (double)(decimal)scale;
            File.WriteAllText(path, units.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
