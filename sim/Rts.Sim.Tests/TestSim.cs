using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>Shared sim setup for tests: the shipped <c>game/data</c>, loaded once per test run.</summary>
public static class TestSim
{
    private static readonly Lazy<GameData> s_data = new(() =>
    {
        DataLoadResult result = DataLoader.LoadAll(TestDataDir.Shipped);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    });

    /// <summary>The shipped data (cached; GameData is immutable, so sharing it is safe).</summary>
    public static GameData Data => s_data.Value;

    /// <summary>Number of unit types in the shipped data; valid type ids are 0 to this minus 1.</summary>
    public static int UnitTypeCount => Data.Units.Length;

    /// <summary>A <see cref="SimConfig"/> carrying the shipped data.</summary>
    public static SimConfig Config(ulong Seed, int PlayerCount, int UnitCapacity, int CommandCapacity) =>
        new(Seed, PlayerCount, UnitCapacity, CommandCapacity) { Data = Data };
}
