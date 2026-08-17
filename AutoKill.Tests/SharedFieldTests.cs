using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class SharedFieldTests
{
    private const float AreaRadius = 250f;

    private static FarmLocation Spot(float x, float z, int spawns = 1, uint territory = 100) =>
        new(territory, "Elpis", new Vector3(x, 0f, z), new Vector2(x / 50f, z / 50f), spawns, 0);

    private static MobSpots Mob(uint nameId, params FarmLocation[] spots) => new(nameId, spots);

    [Fact]
    public void NothingToShareGivesNoFields()
    {
        Assert.Empty(FarmAreas.Share([], AreaRadius));
    }

    [Fact]
    public void OneMobOnItsOwnStillGetsItsAreas()
    {
        var fields = FarmAreas.Share([Mob(1, Spot(0, 0, 5), Spot(900, 0, 3))], AreaRadius);

        Assert.Equal(2, fields.Count);
        Assert.All(fields, field => Assert.Equal([1u], field.BNpcNameIds));
        Assert.Equal(5, fields[0].Area.SpawnCount);
    }

    [Fact]
    public void MobsInTheSameFieldBecomeOneChoice()
    {
        var fields = FarmAreas.Share(
            [
                Mob(1, Spot(0, 0, 29)),
                Mob(2, Spot(120, 0, 27)),
                Mob(3, Spot(0, 120, 13)),
            ],
            AreaRadius);

        var field = Assert.Single(fields);
        Assert.Equal(69, field.Area.SpawnCount);
        Assert.Equal(3, field.Area.Spots.Count);

        // Named by how much each contributes, so the one you will mostly be
        // killing is read first.
        Assert.Equal([1u, 2u, 3u], field.BNpcNameIds);
    }

    [Fact]
    public void MobsInDifferentFieldsStaySeparate()
    {
        var fields = FarmAreas.Share([Mob(1, Spot(0, 0, 5)), Mob(2, Spot(900, 0, 9))], AreaRadius);

        Assert.Equal(2, fields.Count);
        Assert.Equal([2u], fields[0].BNpcNameIds);
        Assert.Equal([1u], fields[1].BNpcNameIds);
    }

    [Fact]
    public void AMobPresentInBothFieldsIsNamedInBoth()
    {
        var fields = FarmAreas.Share(
            [Mob(1, Spot(0, 0, 5), Spot(900, 0, 4)), Mob(2, Spot(900, 30, 9))],
            AreaRadius);

        Assert.Equal(2, fields.Count);
        Assert.Equal([2u, 1u], fields[0].BNpcNameIds);
        Assert.Equal([1u], fields[1].BNpcNameIds);
    }

    [Fact]
    public void FieldsNeverCrossATerritory()
    {
        var fields = FarmAreas.Share(
            [Mob(1, Spot(0, 0, 5, territory: 100)), Mob(2, Spot(0, 0, 9, territory: 200))],
            AreaRadius);

        Assert.Equal(2, fields.Count);
        Assert.All(fields, field => Assert.Single(field.BNpcNameIds));
    }

    /// <summary>
    /// Two mobs standing on the same knot are one place to go, not two. Left
    /// apart, a circuit would fly the same twenty yalms twice under two names.
    /// </summary>
    [Fact]
    public void MobsOnTheSameKnotShareOneWaypoint()
    {
        var fields = FarmAreas.Share([Mob(1, Spot(0, 0, 12)), Mob(2, Spot(4, 3, 8))], AreaRadius);

        var field = Assert.Single(fields);
        var spot = Assert.Single(field.Area.Spots);
        Assert.Equal(20, spot.SpawnCount);

        // Weighted towards where the mobs actually are, rather than the midpoint
        // between two counts that are nothing alike.
        Assert.Equal(1.6f, spot.Position.X, 0.01);
        Assert.Equal(1.2f, spot.Position.Z, 0.01);
    }

    [Fact]
    public void KnotsFarEnoughApartStayApart()
    {
        var fields = FarmAreas.Share([Mob(1, Spot(0, 0, 12)), Mob(2, Spot(100, 0, 8))], AreaRadius);

        var field = Assert.Single(fields);
        Assert.Equal(2, field.Area.Spots.Count);

        // Thickest first, the same order a single mob's spots come in.
        Assert.Equal(12, field.Area.Spots[0].SpawnCount);
    }

    [Fact]
    public void TheSameMobTwiceIsNamedOnlyOnce()
    {
        var fields = FarmAreas.Share([Mob(1, Spot(0, 0, 5), Spot(100, 0, 4))], AreaRadius);

        var field = Assert.Single(fields);
        Assert.Equal([1u], field.BNpcNameIds);
    }
}
