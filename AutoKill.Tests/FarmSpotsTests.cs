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
    public void ASpotKeepsWhichPointsMadeIt()
    {
        // The caller usually knows something about each point that the geometry
        // does not, such as the level it was recorded at, and folding points
        // into a centre would throw that away.
        var spots = FarmSpots.Cluster([At(0, 0), At(500, 0), At(505, 0)], 30f);

        Assert.Equal([1, 2], spots[0].Members);
        Assert.Equal([0], spots[1].Members);
    }

    [Fact]
    public void GroupingKeepsTheMembersOfEachGroup()
    {
        // Spots have to be grouped into areas without losing which spots went
        // where, because an area is patrolled by visiting its members.
        var groups = FarmSpots.GroupIndices([At(0, 0), At(10, 0), At(500, 0)], 30f);

        Assert.Equal(2, groups.Count);
        Assert.Equal([0, 1], groups[0]);
        Assert.Equal([2], groups[1]);
    }

    [Fact]
    public void GroupingReturnsNothingForNoPoints()
    {
        Assert.Empty(FarmSpots.GroupIndices([], 30f));
    }

    [Fact]
    public void GroupsAreSortedBySizeDescending()
    {
        var groups = FarmSpots.GroupIndices([At(0, 0), At(500, 0), At(505, 0), At(510, 0)], 30f);
        Assert.Equal([3, 1], groups.Select(g => g.Count));
    }

    /// <summary>
    /// Two things can stand in exactly the same place, which is ordinary once
    /// the spots of several mobs are put together. Neither may be lost.
    /// </summary>
    [Fact]
    public void PointsAtTheSamePositionAreBothInTheGroup()
    {
        var groups = FarmSpots.GroupIndices([At(10, 10), At(10, 10)], 30f);

        Assert.Equal([0, 1], Assert.Single(groups));
    }

    [Fact]
    public void OrderingIsStableForEqualCounts()
    {
        var a = FarmSpots.Cluster([At(500, 0), At(0, 0)], 30f);
        var b = FarmSpots.Cluster([At(0, 0), At(500, 0)], 30f);
        Assert.Equal(a.Select(s => s.Centre.X), b.Select(s => s.Centre.X));
    }
}
