using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class StopConditionTests
{
    private static FarmProgress Progress(
        int kills = 0,
        double minutes = 0,
        int level = 1,
        bool inventoryFull = false,
        bool died = false,
        (uint Item, int Count)[]? items = null)
    {
        var gained = new Dictionary<uint, int>();
        foreach (var (item, count) in items ?? [])
            gained[item] = count;

        return new FarmProgress(kills, TimeSpan.FromMinutes(minutes), level, inventoryFull, died, gained);
    }

    [Fact]
    public void KillCountIsMetOnceTheTargetIsReached()
    {
        var condition = new KillCountCondition(200);
        Assert.False(condition.IsMet(Progress(kills: 199)));
        Assert.True(condition.IsMet(Progress(kills: 200)));
        Assert.True(condition.IsMet(Progress(kills: 201)));
    }

    [Fact]
    public void ItemCountCountsOnlyTheItemAskedFor()
    {
        var condition = new ItemCountCondition(36203, 30);
        Assert.False(condition.IsMet(Progress(items: [(36203, 29), (5296, 999)])));
        Assert.True(condition.IsMet(Progress(items: [(36203, 30)])));
    }

    [Fact]
    public void ItemCountIsUnmetWhenNothingHasDropped()
    {
        Assert.False(new ItemCountCondition(36203, 1).IsMet(Progress()));
    }

    [Fact]
    public void ElapsedIsMetOnceTheLimitPasses()
    {
        var condition = new ElapsedCondition(TimeSpan.FromMinutes(45));
        Assert.False(condition.IsMet(Progress(minutes: 44)));
        Assert.True(condition.IsMet(Progress(minutes: 45)));
    }

    [Fact]
    public void SafetyConditionsReadTheirFlags()
    {
        Assert.True(new InventoryFullCondition().IsMet(Progress(inventoryFull: true)));
        Assert.False(new InventoryFullCondition().IsMet(Progress()));
        Assert.True(new DeathCondition().IsMet(Progress(died: true)));
        Assert.True(new LevelCondition(90).IsMet(Progress(level: 90)));
        Assert.False(new LevelCondition(90).IsMet(Progress(level: 89)));
    }

    [Fact]
    public void AnEmptySetNeverStops()
    {
        // Matters most for RequireAll, where an empty set would otherwise be
        // vacuously true and end the run before it started.
        Assert.False(new StopConditions([], StopMode.Any).ShouldStop(Progress(kills: 5000)));
        Assert.False(new StopConditions([], StopMode.All).ShouldStop(Progress(kills: 5000)));
    }

    [Fact]
    public void AnyStopsAsSoonAsOneConditionIsMet()
    {
        var conditions = new StopConditions(
            [new KillCountCondition(200), new ElapsedCondition(TimeSpan.FromMinutes(45))],
            StopMode.Any);

        Assert.False(conditions.ShouldStop(Progress(kills: 10, minutes: 5)));
        Assert.True(conditions.ShouldStop(Progress(kills: 200, minutes: 5)));
        Assert.True(conditions.ShouldStop(Progress(kills: 10, minutes: 45)));
    }

    [Fact]
    public void AllStopsOnlyWhenEveryConditionIsMet()
    {
        var conditions = new StopConditions(
            [new KillCountCondition(200), new ItemCountCondition(36203, 30)],
            StopMode.All);

        Assert.False(conditions.ShouldStop(Progress(kills: 200, items: [(36203, 29)])));
        Assert.True(conditions.ShouldStop(Progress(kills: 200, items: [(36203, 30)])));
    }

    [Fact]
    public void SafetyConditionsStopTheRunRegardlessOfMode()
    {
        // Dying does not become acceptable because the kill target is unmet.
        var conditions = new StopConditions(
            [new KillCountCondition(200), new DeathCondition()],
            StopMode.All);

        Assert.True(conditions.ShouldStop(Progress(kills: 3, died: true)));
    }

    [Fact]
    public void ReportsWhichConditionsEndedTheRun()
    {
        var kills = new KillCountCondition(200);
        var conditions = new StopConditions([kills, new ElapsedCondition(TimeSpan.FromMinutes(45))], StopMode.Any);

        var met = conditions.Met(Progress(kills: 200, minutes: 5));

        Assert.Same(kills, Assert.Single(met));
    }

    [Fact]
    public void DescribesProgressTowardsEachCondition()
    {
        Assert.Equal("kills 37/200", new KillCountCondition(200).Describe(Progress(kills: 37)));
        Assert.Equal("item 36203 12/30", new ItemCountCondition(36203, 30).Describe(Progress(items: [(36203, 12)])));
    }
}
