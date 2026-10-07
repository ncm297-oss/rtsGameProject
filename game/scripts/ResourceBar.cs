using System.Globalization;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>The HUD's resource bar, top right (docs/02 "HUD layout"; M3-V1): the local player's gold and wood.</summary>
/// <remarks>
/// Reads <see cref="World.Gold"/> and <see cref="World.Wood"/> every frame; the names are the faction's
/// <c>resources.gold / wood.displayName</c> from <c>faction.json</c>, passed in by <see cref="Match"/> (CLAUDE.md rule 8:
/// no player-facing literal here). Building the text allocates, so it is rebuilt only when a number changes (the M2-H2
/// overlay rule); a steady frame is two int compares. Population joins it in M3-V3.
/// </remarks>
public partial class ResourceBar : Label
{
    private SimRunner? _runner;
    private int _player;
    private string _goldName = "", _woodName = "";
    private int _gold, _wood;

    /// <summary>Times the text was rebuilt (only when a shown number changes).</summary>
    public int Builds { get; private set; }

    /// <summary>The gold amount on show.</summary>
    public int ShownGold => _gold;

    /// <summary>The wood amount on show.</summary>
    public int ShownWood => _wood;

    /// <summary>Connects the bar to the match; call once after the sim exists.</summary>
    /// <param name="goldName">The faction's display name for gold (data).</param>
    /// <param name="woodName">The faction's display name for wood (data).</param>
    public void Init(SimRunner runner, int player, string goldName, string woodName)
    {
        _runner = runner;
        _player = player;
        _goldName = goldName;
        _woodName = woodName;
        Builds = 0;
        if (runner.Simulation is Simulation sim) Sync(sim.World);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Simulation is Simulation sim) Sync(sim.World);
    }

    /// <summary>Shows the player's current totals, rebuilding the text only if one changed since the last build.</summary>
    public void Sync(World world)
    {
        if ((uint)_player >= (uint)world.Gold.Length) return;
        int gold = world.Gold[_player], wood = world.Wood[_player];
        if (Builds > 0 && gold == _gold && wood == _wood) return;
        _gold = gold;
        _wood = wood;
        Builds++;
        Text = string.Create(CultureInfo.InvariantCulture, $"{_goldName} {gold}  {_woodName} {wood}");
    }
}
