using System.Collections.Immutable;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-2 criterion 2: per-player seats in <see cref="SimConfig"/> (faction, Human / AI, AI difficulty). The default is today's
/// (player p plays faction p mod factions, every seat human), so a config without seats builds the same match; the world's
/// faction of a player is its seat's; <see cref="SimConfig.Validate"/> refuses a bad seat.
/// </summary>
public class SeatConfigTests
{
    private static SimConfig Config(int players, params PlayerSeat[] seats) =>
        TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: 8, CommandCapacity: 8) with { Seats = seats.ToImmutableArray() };

    [Fact]
    public void NoSeats_AreTodaysDefaults()
    {
        SimConfig c = TestSim.Config(Seed: 1, PlayerCount: 5, UnitCapacity: 8, CommandCapacity: 8);
        Assert.True(c.Seats.IsEmpty);
        Assert.False(c.HasAiSeat);
        var w = new World(c);
        for (int p = 0; p < 5; p++)
        {
            Assert.Equal(new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal), c.SeatOf(p));
            Assert.Equal(p % TestSim.Data.Factions.Length, w.FactionOf(p));
        }
        Assert.Equal(-1, w.FactionOf(5));
        Assert.Equal(-1, w.FactionOf(-1));
    }

    [Fact]
    public void Seats_SetTheFactions_AndAnAiSeatGetsAnAi()
    {
        int malazan = TestSim.Data.FindFaction("malazan"), whirlwind = TestSim.Data.FindFaction("whirlwind");
        SimConfig c = Config(3, new(whirlwind, SeatController.Human, AiDifficulty.Normal), new(whirlwind, SeatController.Ai, AiDifficulty.Easy),
            new(malazan, SeatController.Human, AiDifficulty.Hard));
        var sim = new Simulation(c);
        Assert.Equal(new[] { whirlwind, whirlwind, malazan }, Enumerable.Range(0, 3).Select(sim.World.FactionOf));
        Assert.Null(sim.AiOf(0));
        Assert.NotNull(sim.AiOf(1));
        Assert.Null(sim.AiOf(2));
        Assert.Equal(TestSim.Data.Ai[whirlwind].Difficulty(AiDifficulty.Easy).ThinkTicks, sim.AiOf(1)!.ThinkTicks);
        Assert.True(c.HasAiSeat);
    }

    /// <summary>A seat's faction decides what the player may build: a whirlwind seat at player 0 may place a whirlwind house, not a malazan one.</summary>
    [Fact]
    public void ASeatsFaction_IsWhatThePlacementRuleReads()
    {
        int whirlwind = TestSim.Data.FindFaction("whirlwind");
        var sim = new Simulation(Config(1, new PlayerSeat(whirlwind, SeatController.Human, AiDifficulty.Normal)));
        sim.World.Fog.ExploreAllForTests();
        int cell = MoveScenario.CentralCell(sim.World.NavGrid);
        Assert.False(sim.World.CanPlace(0, BuildMaps.House, cell, out Economy.PlacementError why));
        Assert.Equal(Economy.PlacementError.WrongFaction, why);
        sim.World.CanPlace(0, BuildMaps.WhirlwindHouse, cell, out why);
        Assert.NotEqual(Economy.PlacementError.WrongFaction, why);
    }

    [Fact]
    public void Validate_RefusesBadSeats()
    {
        var human = new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal);
        Assert.Throws<ArgumentException>(() => Config(2, human).Validate()); // one seat for two players
        Assert.Throws<ArgumentException>(() => Config(1, human, human).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, human with { Faction = 2 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, human with { Faction = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, human with { Controller = (SeatController)2 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Config(1, human with { Difficulty = (AiDifficulty)3 }).Validate());
        Config(1, human).Validate();
        Config(1, human with { Controller = SeatController.Ai }).Validate();
    }

    /// <summary>An AI seat needs its faction's ai.json loaded: data without it (hand-built, no <c>Ai</c>) is refused, and a world isn't built.</summary>
    [Fact]
    public void Validate_RefusesAnAiSeat_WithoutALoadedAiJson()
    {
        GameData d = TestSim.Data;
        var noAi = new GameData { DamageTable = d.DamageTable, Rules = d.Rules, Factions = d.Factions, Units = d.Units, Resources = d.Resources, Buildings = d.Buildings };
        SimConfig c = new SimConfig(1, 1, 8, 8) { Data = noAi, Seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard)) };
        ArgumentException e = Assert.Throws<ArgumentException>(() => c.Validate());
        Assert.Contains("ai.json", e.Message);
        Assert.Throws<ArgumentException>(() => new Simulation(c));
        (c with { Seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Hard)) }).Validate();
    }
}
