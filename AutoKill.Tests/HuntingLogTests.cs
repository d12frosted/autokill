using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class HuntingLogTests
{
    private static HuntingLogEntry Entry(
        uint rowId, int index, params HuntingLogKill[] kills) =>
        new(rowId, index, (index - 1) / 10 + 1, index, kills);

    private static HuntingLogKill Kill(uint mob, int needed, int killed) =>
        new(mob, $"mob {mob}", needed, killed);

    [Fact]
    public void AnEntryIsDoneWhenEveryMobOnItIs()
    {
        var entry = Entry(40011, 11, Kill(1, 3, 3), Kill(2, 3, 3));

        Assert.True(entry.Done);
        Assert.Equal(0, entry.Remaining);
    }

    [Fact]
    public void OneMobShortIsNotDone()
    {
        var entry = Entry(40011, 11, Kill(1, 3, 3), Kill(2, 3, 1));

        Assert.False(entry.Done);
        Assert.Equal(2, entry.Remaining);
    }

    [Fact]
    public void MoreKillsThanAskedForDoNotCountBackwards()
    {
        var entry = Entry(40011, 11, Kill(1, 3, 5));

        Assert.Equal(0, entry.Remaining);
    }

    [Fact]
    public void AnEntryAtTheLevelYouAreIsWithinReach()
    {
        Assert.True(HuntingLogPlan.WithinReach(11, classLevel: 11, allowance: 0));
    }

    [Fact]
    public void AnEntryAboveYouIsReachableOnlyUpToTheAllowance()
    {
        Assert.True(HuntingLogPlan.WithinReach(14, classLevel: 11, allowance: 3));
        Assert.False(HuntingLogPlan.WithinReach(15, classLevel: 11, allowance: 3));
    }

    [Fact]
    public void AnEntryWithNoLevelBlocksNothing()
    {
        // The Grand Company logs have no ordering to read a level out of, and
        // a quarter of the mobs have no recorded level either.
        Assert.True(HuntingLogPlan.WithinReach(null, classLevel: 1, allowance: 0));
    }

    [Fact]
    public void OneZonePerMobIsOneStopEach()
    {
        var stops = HuntingLogPlan.Stops(
        [
            new HuntingLogPlacement(1, [148]),
            new HuntingLogPlacement(2, [152]),
        ]);

        Assert.Equal([148u, 152u], stops.Select(stop => stop.TerritoryTypeId));
    }

    [Fact]
    public void MobsSharingAZoneShareTheStop()
    {
        var stops = HuntingLogPlan.Stops(
        [
            new HuntingLogPlacement(1, [148]),
            new HuntingLogPlacement(2, [148]),
        ]);

        var stop = Assert.Single(stops);
        Assert.Equal(148u, stop.TerritoryTypeId);
        Assert.Equal([1u, 2u], stop.BNpcNameIds);
    }

    [Fact]
    public void AZoneThatCoversMoreMobsIsTakenOverOneThatCoversFewer()
    {
        // Mobs 1 and 2 stand in 148 as well as somewhere else, and mob 3 only
        // in 148. Going there once does the work of three trips.
        var stops = HuntingLogPlan.Stops(
        [
            new HuntingLogPlacement(1, [152, 148]),
            new HuntingLogPlacement(2, [153, 148]),
            new HuntingLogPlacement(3, [148]),
        ]);

        var stop = Assert.Single(stops);
        Assert.Equal(148u, stop.TerritoryTypeId);
        Assert.Equal([1u, 2u, 3u], stop.BNpcNameIds);
    }

    [Fact]
    public void WhatIsLeftOverGetsAStopOfItsOwn()
    {
        var stops = HuntingLogPlan.Stops(
        [
            new HuntingLogPlacement(1, [148]),
            new HuntingLogPlacement(2, [148]),
            new HuntingLogPlacement(3, [155]),
        ]);

        Assert.Equal([148u, 155u], stops.Select(stop => stop.TerritoryTypeId));
        Assert.Equal([3u], stops[1].BNpcNameIds);
    }

    [Fact]
    public void TwoZonesThatWouldDoEquallyWellAlwaysComeOutTheSameWay()
    {
        // Asked twice with the zones in either order, the answer has to be the
        // same one: a plan that shuffles is a plan nobody can check.
        var one = HuntingLogPlan.Stops([new HuntingLogPlacement(1, [155, 148])]);
        var other = HuntingLogPlan.Stops([new HuntingLogPlacement(1, [148, 155])]);

        Assert.Equal(one[0].TerritoryTypeId, other[0].TerritoryTypeId);
    }

    [Fact]
    public void AMobWithNowhereToGoIsNotAStop()
    {
        var stops = HuntingLogPlan.Stops(
        [
            new HuntingLogPlacement(1, []),
            new HuntingLogPlacement(2, [148]),
        ]);

        var stop = Assert.Single(stops);
        Assert.Equal([2u], stop.BNpcNameIds);
    }

    [Fact]
    public void NothingToDoIsNoStops()
    {
        Assert.Empty(HuntingLogPlan.Stops([]));
    }
}
