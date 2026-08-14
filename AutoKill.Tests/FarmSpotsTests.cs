using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class FarmSpotsTests
{
    private static Vector3 At(float x, float z, float y = 0f) => new(x, y, z);

    [Fact]
    public void NoPointsGivesNoSpots()
    {
        Assert.Empty(FarmSpots.Cluster([], 30f));
    }

    [Fact]
    public void ASinglePointBecomesASpotOfOne()
    {
        var spots = FarmSpots.Cluster([At(10, 20)], 30f);
        var spot = Assert.Single(spots);
        Assert.Equal(1, spot.Count);
        Assert.Equal(10f, spot.Centre.X, 1e-4);
        Assert.Equal(20f, spot.Centre.Z, 1e-4);
    }

    [Fact]
    public void NearbyPointsMergeAndTheCentreIsTheirAverage()
    {
        var spots = FarmSpots.Cluster([At(0, 0), At(10, 0), At(5, 0)], 30f);
        var spot = Assert.Single(spots);
        Assert.Equal(3, spot.Count);
        Assert.Equal(5f, spot.Centre.X, 1e-4);
    }

    [Fact]
    public void DistantPointsStaySeparate()
    {
        Assert.Equal(2, FarmSpots.Cluster([At(0, 0), At(500, 500)], 30f).Count);
    }

    [Fact]
    public void AChainOfPointsLinksIntoOneSpot()
    {
        // Single link: each point is within radius of the next, so the whole
        // chain is one patrol even though the ends are far apart.
        Vector3[] points = [At(0, 0), At(25, 0), At(50, 0), At(75, 0), At(100, 0)];
        var spot = Assert.Single(FarmSpots.Cluster(points, 30f));
        Assert.Equal(5, spot.Count);
    }

    [Fact]
    public void SpotsAreSortedByDensityDescending()
    {
        Vector3[] points = [At(0, 0), At(500, 0), At(505, 0), At(510, 0)];
        var spots = FarmSpots.Cluster(points, 30f);
        Assert.Equal([3, 1], spots.Select(s => s.Count));
    }

    [Fact]
    public void TheRadiusIsADistanceNotABoundingBox()
    {
        // (21, 21) is inside a 30 wide box around the origin but 29.7 away, so
        // it merges; (25, 25) is 35.4 away and must not.
        Assert.Single(FarmSpots.Cluster([At(0, 0), At(21, 21)], 30f));
        Assert.Equal(2, FarmSpots.Cluster([At(0, 0), At(25, 25)], 30f).Count);
    }

    [Fact]
    public void ElevationIsIgnoredWhenGrouping()
    {
        // Two points on top of each other on a cliff are still one place to
        // stand as far as picking a farm spot goes; the navmesh sorts out how
        // to get there.
        var spot = Assert.Single(FarmSpots.Cluster([At(0, 0, 0), At(5, 0, 200)], 30f));
        Assert.Equal(2, spot.Count);
    }

    [Fact]
    public void GroupingKeepsTheMembersOfEachGroup()
    {
        // Spots have to be grouped into areas without losing which spots went
        // where, because an area is patrolled by visiting its members.
        var groups = FarmSpots.Group([At(0, 0), At(10, 0), At(500, 0)], 30f);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Count);
        Assert.Single(groups[1]);
        Assert.Contains(At(10, 0), groups[0]);
    }

    [Fact]
    public void GroupingReturnsNothingForNoPoints()
    {
        Assert.Empty(FarmSpots.Group([], 30f));
    }

    [Fact]
    public void GroupsAreSortedBySizeDescending()
    {
        var groups = FarmSpots.Group([At(0, 0), At(500, 0), At(505, 0), At(510, 0)], 30f);
        Assert.Equal([3, 1], groups.Select(g => g.Count));
    }

    [Fact]
    public void OrderingIsStableForEqualCounts()
    {
        var a = FarmSpots.Cluster([At(500, 0), At(0, 0)], 30f);
        var b = FarmSpots.Cluster([At(0, 0), At(500, 0)], 30f);
        Assert.Equal(a.Select(s => s.Centre.X), b.Select(s => s.Centre.X));
    }
}
