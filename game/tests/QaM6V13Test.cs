using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V13 (2026-10-18-0516): the AlertHud half of BUG-0954 item 1 on the real Match, which no dev scene drives. A raider
/// hits a worker beside the hall (a Units alert), then raiders hit the hall within 10 s / 30 m (the alert turns Base). Off
/// screen: the base text goes up (UpgradeTexts + 1) and stays the base text, with no second sound or ping. On screen: the
/// alert turns Base, no text, UpgradeTexts unchanged.
/// </summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V13Test.tscn</c>; prints "QA M6-V13 TEST PASS"
/// and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class QaM6V13Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            await Upgrade(offScreen: true);
            await Upgrade(offScreen: false);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M6-V13 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V13 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Upgrade(bool offScreen)
    {
        string how = offScreen ? "off screen" : "on screen";
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        m.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--seed", "1", "--mute" }));
        var runner = m.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        var camera = m.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        var alerts = m.GetNode<AlertHud>("Alerts");
        var sfx = m.GetNode<Sfx>("Sfx");
        var minimap = m.GetNode<Minimap>("Hud/Minimap");
        while (sim.TickNumber < 3) await Frame();
        World w = sim.World;
        NavGrid g = w.NavGrid;
        StartBasePlan bases = m.Bases!;
        System.Numerics.Vector2 hall = StartBase.FootprintCenter(g, _data.Buildings[bases.HallType[0]], bases.HallAnchor[0]);
        EntityHandle hallHandle = DefeatStaging.BuildingAt(w, 0, bases.HallAnchor[0]);
        // The worker nearest the hall: its first hit raises a Units alert well inside 30 m of the hall's centre.
        EntityHandle worker = default;
        float best = float.MaxValue;
        foreach (EntityHandle h in DefeatStaging.UnitsOf(w, 0))
        {
            float d = System.Numerics.Vector2.Distance(w.Units.Position[h.Index], hall);
            if (d < best) { best = d; worker = h; }
        }
        if (!Check(worker != default && hallHandle != default && best < 20f, $"{how}: worker {best:F1} m from the hall")) { await End(m); return; }

        System.Numerics.Vector2 centre = new System.Numerics.Vector2(g.Width, g.Height) * MapConstants.CellSize / 2;
        System.Numerics.Vector2 away = hall + System.Numerics.Vector2.Normalize(centre - hall) * 80f;
        if (offScreen) camera.SetFocus(away.X, away.Y);
        else camera.SetFocus(hall.X, hall.Y);
        await Frame();
        Check(ScreenCheck.OnScreen(camera, w.Heightmap, hall) != offScreen, $"{how}: hall on screen {ScreenCheck.OnScreen(camera, w.Heightmap, hall)}");

        int raider = _data.FindUnit("whirlwind_raider");
        int sounds0 = sfx.PlayCount(SfxEvent.Alert), pings0 = minimap.PingsAdded, texts0 = alerts.TextsShown, up0 = alerts.UpgradeTexts;
        // (1) one raider on the worker
        System.Numerics.Vector2 wp = w.Units.Position[worker.Index];
        sim.Enqueue(Command.SpawnUnit(1, raider, wp + System.Numerics.Vector2.Normalize(wp - hall) * 1.5f));
        await Frame();
        await Frame();
        OrderAll(sim, u => Command.Attack(1, u, worker, isBuilding: false));
        await Until(() => alerts.Alerts.Raised >= 1, 20);
        if (!Check(alerts.Alerts.Raised == 1, $"{how}: {alerts.Alerts.Raised} alerts after the worker raid")) { await End(m); return; }
        int first = alerts.Alerts.LastIndex;
        int firstTick = sim.TickNumber;
        if (!Check(alerts.Alerts.Kind(first) == AttackAlertKind.Units, $"{how}: the first alert is {alerts.Alerts.Kind(first)} (the raider hit the hall first?)")) { await End(m); return; }
        Check(System.Numerics.Vector2.Distance(alerts.Alerts.Position(first), hall) <= AttackAlerts.DefaultRadius - 5f, $"{how}: units alert {System.Numerics.Vector2.Distance(alerts.Alerts.Position(first), hall):F1} m from the hall");
        Check(offScreen ? alerts.TextShown && alerts.TextLabel!.Text == _ui.Alert(AlertText.UnitsUnderAttack) : !alerts.TextShown,
            $"{how}: after the units alert text shown {alerts.TextShown} '{alerts.TextLabel?.Text}'");
        // (2) off screen: let the units text go first (4 s), so the upgrade must put text up, not merely relabel it.
        if (offScreen) await Until(() => !alerts.TextShown, 6);
        Check(sim.TickNumber - firstTick < 150, $"{how}: {sim.TickNumber - firstTick} ticks went by before the hall raid (want well under 200)");
        BuildingDef def = _data.Buildings[w.Buildings.TypeId[hallHandle.Index]];
        float reach = Math.Max(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize / 2f + 1.5f;
        for (int k = 0; k < 3; k++)
        {
            float a = k * MathF.Tau / 3 + 0.4f;
            sim.Enqueue(Command.SpawnUnit(1, raider, hall + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * reach));
        }
        await Frame();
        await Frame();
        OrderAll(sim, u => Command.Attack(1, u, hallHandle, isBuilding: true));
        int hp0 = w.Buildings.Hp[hallHandle.Index];
        await Until(() => alerts.Alerts.Kind(first) == AttackAlertKind.Base || sim.TickNumber - firstTick >= 200, 20);
        GD.Print($"{how}: hall hp {hp0} -> {w.Buildings.Hp[hallHandle.Index]}, alert {alerts.Alerts.Kind(first)} {sim.TickNumber - firstTick} ticks after the first; " +
            $"alerts {alerts.Alerts.Raised}, upgrade texts {alerts.UpgradeTexts - up0}, texts {alerts.TextsShown - texts0}, sounds {sfx.PlayCount(SfxEvent.Alert) - sounds0}, pings {minimap.PingsAdded - pings0}, label '{alerts.TextLabel?.Text}' {alerts.TextShown}");
        Check(alerts.Alerts.Kind(first) == AttackAlertKind.Base && alerts.Alerts.Raised == 1, $"{how}: alert {alerts.Alerts.Kind(first)}, {alerts.Alerts.Raised} raised (want one, upgraded to Base)");
        Check(sfx.PlayCount(SfxEvent.Alert) == sounds0 + 1 && minimap.PingsAdded == pings0 + 1, $"{how}: {sfx.PlayCount(SfxEvent.Alert) - sounds0} sounds, {minimap.PingsAdded - pings0} pings (want one each: the upgrade is the same alert)");
        if (offScreen)
        {
            Check(alerts.UpgradeTexts == up0 + 1, $"{how}: {alerts.UpgradeTexts - up0} upgrade texts (want 1)");
            Check(alerts.TextShown && alerts.TextLabel!.Text == _ui.Alert(AlertText.BaseUnderAttack), $"{how}: label '{alerts.TextLabel?.Text}' shown {alerts.TextShown} (want the base text)");
            // More unit and building hits join the Base alert: the label stays base, no second upgrade.
            await Until(() => sim.TickNumber - firstTick >= 190, 15);
            Check(alerts.UpgradeTexts == up0 + 1 && alerts.Alerts.Kind(first) == AttackAlertKind.Base, $"{how}: {alerts.UpgradeTexts - up0} upgrade texts by the 10 s mark");
            Check(!alerts.TextShown || alerts.TextLabel!.Text == _ui.Alert(AlertText.BaseUnderAttack), $"{how}: label went back to '{alerts.TextLabel?.Text}'");
        }
        else
        {
            Check(alerts.UpgradeTexts == up0 && !alerts.TextShown && alerts.TextsShown == texts0, $"{how}: {alerts.UpgradeTexts - up0} upgrade texts, text shown {alerts.TextShown} ({alerts.TextsShown - texts0} times; want none)");
        }
        await End(m);
    }

    private static void OrderAll(Simulation sim, Func<EntityHandle, Command> order)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) sim.Enqueue(order(new EntityHandle(i, u.Generation[i])));
    }

    private async Task End(Match m)
    {
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    private async Task Until(Func<bool> done, double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (!done() && Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000)) await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
