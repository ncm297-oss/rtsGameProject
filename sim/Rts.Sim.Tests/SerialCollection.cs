namespace Rts.Sim.Tests;

/// <summary>
/// The xUnit collection for every wall-clock (<c>Category=Perf</c>) and allocation-measuring test.
/// </summary>
/// <remarks>
/// Its tests run one at a time, after the parallel batch, with nothing else running: timings and
/// per-thread allocation counts measured next to the heavy fuzz rows rose 2-10x and flaked
/// (BUG-0017, BUG-0024). Classes that mix measured and heavy unmeasured tests keep the heavy ones
/// outside, in the parallel batch, through a nested <c>Serial</c> class.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialCollection
{
    /// <summary>The collection name for <c>[Collection(SerialCollection.Name)]</c>.</summary>
    public const string Name = "Serial measurements";
}
