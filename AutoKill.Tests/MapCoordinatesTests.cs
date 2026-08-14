using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class MapCoordinatesTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void MapCentreIsTheWorldOrigin()
    {
        // At scale 100 with no offset, map coordinate 21.5 is world 0. This
        // anchor is what pins the whole projection down.
        Assert.Equal(0.0, MapCoordinates.ToWorld(21.5f, 100, 0), Tolerance);
    }

    [Fact]
    public void MapOneIsTheLeftEdge()
    {
        Assert.Equal(-1024.0, MapCoordinates.ToWorld(1.0f, 100, 0), Tolerance);
    }

    [Fact]
    public void OffsetShiftsTheResult()
    {
        var without = MapCoordinates.ToWorld(21.5f, 100, 0);
        var shifted = MapCoordinates.ToWorld(21.5f, 100, 200);
        Assert.Equal(without - 200.0, shifted, Tolerance);
    }

    [Fact]
    public void SizeFactorScalesTheSpan()
    {
        var big = MapCoordinates.ToWorld(1.0f, 100, 0);
        var small = MapCoordinates.ToWorld(1.0f, 200, 0);
        Assert.Equal(big / 2.0, small, Tolerance);
    }

    [Theory]
    [InlineData(15.9f, 100, 0)]
    [InlineData(23.5f, 100, 0)]
    [InlineData(30.2f, 200, 0)]
    [InlineData(12.4f, 95, -300)]
    [InlineData(1.0f, 400, 512)]
    public void RoundTripsBackToTheSameMapCoordinate(float value, ushort sizeFactor, short offset)
    {
        var world = MapCoordinates.ToWorld(value, sizeFactor, offset);
        Assert.Equal(value, MapCoordinates.ToMap(world, sizeFactor, offset), 1e-4);
    }

    [Fact]
    public void AKnownEasternThanalanSpawnLandsInsideTheZone()
    {
        // Myotragus Billy, Eastern Thanalan: map 22, size factor 100, no offset.
        var x = MapCoordinates.ToWorld(15.9f, 100, 0);
        Assert.InRange(x, -1024.0, 1024.0);
        Assert.Equal(-279.6, x, 1.0);
    }
}
