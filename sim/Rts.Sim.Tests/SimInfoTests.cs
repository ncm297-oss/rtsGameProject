using Rts.Sim;

namespace Rts.Sim.Tests;

public class SimInfoTests
{
    [Fact]
    public void Version_IsCurrentSkeletonVersion()
    {
        Assert.Equal("0.0.1", SimInfo.Version);
    }
}
