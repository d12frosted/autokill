using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class LevelRangeTests
{
    private static FarmLocation Spot(ushort level, int spawns = 1, float x = 0f) =>
        new(100, "Elpis", new Vector3(x, 0f, 0f), new Vector2(x / 50f, 0f), spawns, level);

    [Fact]
    public void OneLevelReadsAsOneNumber()
    {
        Assert.Equal("Lv83", LevelRange.Of([83])!.ToString());
    }

    [Fact]
    public void SeveralLevelsReadAsTheRangeTheyCover()
    {
        Assert.Equal("Lv81-83", LevelRange.Of([83, 81, 82])!.ToString());
    }

    [Fact]
    public void NothingRecordedIsNoRangeRatherThanLevelZero()
    {
        Assert.Null(LevelRange.Of([]));
        Assert.Null(LevelRange.Of([0, 0]));
    }

    [Fact]
    public void UnrecordedLevelsDoNotDragTheRangeDown()
    {
        // Two sources feed the index and only one carries levels, so a spot
        // with none standing beside a spot with one is ordinary. Counting the
        // missing one as zero would read as "Lv0-83".
        var range = LevelRange.Of([0, 83, 0]);

        Assert.Equal("Lv83", range!.ToString());
    }

    [Fact]
    public void AnAreaIsAsHighAndAsLowAsWhatStandsInIt()
    {
        var area = FarmAreas.IntoAreas([Spot(41), Spot(44, x: 100f)], 250f)[0];

        Assert.Equal("Lv41-44", area.Level!.ToString());
    }

    [Fact]
    public void AnAreaNobodyRecordedALevelForHasNone()
    {
        var area = FarmAreas.IntoAreas([Spot(0), Spot(0, x: 100f)], 250f)[0];

        Assert.Null(area.Level);
    }

    [Fact]
    public void SeveralKindsOnOneKnotKeepTheHighestOfThem()
    {
        // Folding two mobs into one place to stand has to leave the level of
        // the harder one, since that is what walking in there means.
        var fields = FarmAreas.Share(
            [new MobSpots(1, [Spot(41)]), new MobSpots(2, [Spot(52, x: 5f)])],
            250f);

        Assert.Equal("Lv52", Assert.Single(fields).Area.Level!.ToString());
    }
}
