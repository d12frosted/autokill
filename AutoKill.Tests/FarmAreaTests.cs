using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class FarmAreaTests
{
    private static FarmLocation Spot(float x, float z, int spawns = 1, uint territory = 100) =>
        new(territory, "Elpis", new Vector3(x, 0f, z), new Vector2(x / 50f, z / 50f), spawns, 0);

    [Fact]
    public void NoSpotsGiveNoAreas()
    {
        Assert.Empty(FarmAreas.IntoAreas([], 250f));
    }

    [Fact]
    public void SpotsWithinReachOfEachOtherAreOneArea()
    {
        var areas = FarmAreas.IntoAreas([Spot(0, 0, 5), Spot(100, 0, 3)], 250f);

        var area = Assert.Single(areas);
        Assert.Equal(8, area.SpawnCount);
        Assert.Equal(2, area.Spots.Count);
    }

    [Fact]
    public void DistantSpotsAreSeparateAreas()
    {
        var areas = FarmAreas.IntoAreas([Spot(0, 0, 3), Spot(900, 0, 5)], 250f);

        Assert.Equal(2, areas.Count);

        // Thickest first, since that is the one worth offering.
        Assert.Equal(5, areas[0].SpawnCount);
    }

    [Fact]
    public void AreasNeverCrossATerritory()
    {
        var areas = FarmAreas.IntoAreas(
            [Spot(0, 0, 3, territory: 100), Spot(0, 0, 4, territory: 200)], 250f);

        Assert.Equal(2, areas.Count);
        Assert.Equal([100u, 200u], areas.Select(a => a.TerritoryTypeId).Order());
    }

    /// <summary>
    /// Two mobs can stand in exactly the same place, and after merging their
    /// spots that is ordinary rather than freakish. Matching spots up by position
    /// would quietly drop one of them.
    /// </summary>
    [Fact]
    public void SpotsAtTheSamePositionBothSurvive()
    {
        var areas = FarmAreas.IntoAreas([Spot(10, 10, 3), Spot(10, 10, 4)], 250f);

        var area = Assert.Single(areas);
        Assert.Equal(2, area.Spots.Count);
        Assert.Equal(7, area.SpawnCount);
    }
}
