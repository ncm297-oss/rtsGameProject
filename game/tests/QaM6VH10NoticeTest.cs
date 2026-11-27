using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH10 (BUG-1792), beside <c>ViewHardenVh10Test</c>'s (N) rows: the draw notice on a live match's HUD at 1152 x 648
/// and 1920 x 1080 with a short text ("Draw {0}"), the shipped text, 120 characters of words and 480 characters of words. Each
/// label: centred within 2 px, wholly on screen, its top at <c>DrawNotice.Top</c>, its width at most <c>DrawNotice.Width</c>,
/// and no other visible HUD control (the alert line, the resource bar, the dev line, the minimap) overlapping it.
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves each case as <c>qa-m6vh10-notice-&lt;w&gt;-&lt;case&gt;.png</c>.
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH10NoticeTest.tscn</c>; prints
/// "QA M6-VH10 NOTICE TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM6VH10NoticeTest : Node
{
    private readonly List<string> _failures = new();
    private string? _shots;
    private Match _match = null!;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            UiText shipped = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(_match);
            _match.Start(loaded.Data!, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-combat", "--no-fog", "--mute" }));
            _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Disabled;
            _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            string sentence = "Nobody can train a single new unit any more, so this match now ends in a draw unless any unit falls before then. ";
            var cases = new List<(string Name, UiText Ui)> { ("shipped", shipped) };
            foreach ((string name, string text) in new[] { ("short", "Draw {0}"), ("120", Fit(sentence, 120)), ("480", Fit(sentence + sentence + sentence + sentence + sentence, 480)) })
            {
                UiText? ui = UiWith(text);
                if (Check(ui != null, $"{name}: ui.json refused")) cases.Add((name, ui!));
            }
            foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1920, 1080) })
            {
                GetTree().Root.Size = size;
                await Frames(3);
                Rect2 screen = GetViewport().GetVisibleRect();
                foreach ((string name, UiText ui) in cases) await Measure(size, screen, name, ui);
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        foreach (string f in _failures) GD.Print($"QA M6-VH10 NOTICE TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-VH10 NOTICE TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Measure(Vector2I size, Rect2 screen, string name, UiText ui)
    {
        var extra = new DrawNotice { Name = "QaNotice" };
        _match.AddChild(extra);
        CanvasLayer hud = _match.GetNode<CanvasLayer>("Hud");
        extra.Init(_match.GetNode<SimRunner>("SimRunner"), hud, ui);
        Label label = extra.TextLabel!;
        label.Text = ui.DrawCountdown(300);
        label.Visible = true;
        await Frames(3);
        Rect2 r = label.GetGlobalRect();
        float off = Math.Abs(r.GetCenter().X - screen.Size.X / 2);
        var overlaps = new List<string>();
        Walk(hud, n =>
        {
            if (n == label || n is not Control c || !c.IsVisibleInTree() || c.Size.X <= 0 || c.Size.Y <= 0) return;
            if (c.GetChildCount() > 0 && c is not Label && c is not TextureRect) return; // containers: their leaves are looked at
            Rect2 o = c.GetGlobalRect();
            if (o.Size.X >= screen.Size.X - 1 && o.Size.Y >= screen.Size.Y - 1) return; // a full-screen layer
            if (o.Intersects(r) && !(c is Label l && string.IsNullOrEmpty(l.Text))) overlaps.Add($"{c.Name} {o}");
        });
        GD.Print($"notice {size.X} x {size.Y} {name} ({label.Text.Length} chars): {r}, centre {off:0.#} px off, on screen {screen.Encloses(r)}, overlaps [{string.Join(", ", overlaps)}]");
        Check(off <= 2f && screen.Encloses(r) && Math.Abs(r.Position.Y - DrawNotice.Top) <= 0.5f && r.Size.X <= DrawNotice.Width + 0.5f,
            $"{size.X} x {size.Y} {name}: label {r}, centre {off:0.#} px off, on screen {screen.Encloses(r)}");
        Check(overlaps.Count == 0, $"{size.X} x {size.Y} {name}: overlaps [{string.Join(", ", overlaps)}]");
        if (_shots != null)
        {
            for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_shots, $"qa-m6vh10-notice-{size.X}-{name}.png"));
        }
        label.QueueFree();
        _match.RemoveChild(extra);
        extra.QueueFree();
        await Frames(1);
    }

    // `text` cut or repeated to `length` characters once {0} is 5:00 (4 characters), with {0} at the end.
    private static string Fit(string text, int length)
    {
        string body = text.Length >= length - 4 ? text[..(length - 4)] : text.PadRight(length - 4, 'x');
        return body + "{0}";
    }

    private static UiText? UiWith(string drawCountdown)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(UiText.DefaultPath))!;
        root["hud"]!["draw_countdown"] = drawCountdown;
        return UiText.Parse(root.ToJsonString(), new List<string>());
    }

    private static void Walk(Node n, Action<Node> visit)
    {
        visit(n);
        foreach (Node c in n.GetChildren()) Walk(c, visit);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
