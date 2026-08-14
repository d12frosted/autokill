using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class SpawnPositionTests
{
    [Fact]
    public void TheMapCentreMarkerMeansThePositionIsUnknown()
    {
        // A quarter of the published spawn rows carry this exact triple: dead
        // centre of the map with an elevation of -1. Taken literally it sends
        // you to the middle of the zone to farm nothing.
        Assert.True(SpawnPositions.IsUnknown(21.48f, 21.48f, -1f));
    }

    [Fact]
    public void AnOrdinaryPositionIsNotUnknown()
    {
        Assert.False(SpawnPositions.IsUnknown(21f, 11f, -520.0123f));
    }

    [Fact]
    public void TheMarkerIsTheWholeTripleNotJustTheCoordinates()
    {
        // Matching on the pair alone would throw away a real spawn that happens
        // to sit near the middle of a map, so all three have to agree.
        Assert.False(SpawnPositions.IsUnknown(21.48f, 21.48f, 143.5f));
        Assert.False(SpawnPositions.IsUnknown(21.48f, 12.0f, -1f));
    }

    [Fact]
    public void ZeroIsNotTheMarker()
    {
        Assert.False(SpawnPositions.IsUnknown(0f, 0f, 0f));
    }
}
