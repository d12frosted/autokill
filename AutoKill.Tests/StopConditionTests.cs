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
        (uint Item, int Count)[]? items = null,
        (uint Mob, int Count)[]? mobs = null)
    {
        var gained = new Dictionary<uint, int>();
        foreach (var (item, count) in items ?? [])
            gained[item] = count;

        var killed = new Dictionary<uint, int>();
        foreach (var (mob, count) in mobs ?? [])
            killed[mob] = count;

        return new FarmProgress(
            kills, TimeSpan.FromMinutes(minutes), level, inventoryFull, died, gained, killed);
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

    [Fact]
    public void AMobKillTargetCountsOnlyThatMob()
    {
        var condition = new MobKillCondition(49, "wharf rat", 3);

        Assert.False(condition.IsMet(Progress(kills: 30, mobs: [(50, 12)])));
        Assert.False(condition.IsMet(Progress(mobs: [(49, 2)])));
        Assert.True(condition.IsMet(Progress(mobs: [(49, 3)])));
    }

    [Fact]
    public void AMobKillTargetSaysWhichMobItIsWaitingOn()
    {
        var condition = new MobKillCondition(49, "wharf rat", 3);

        Assert.Equal("wharf rat 2/3", condition.Describe(Progress(mobs: [(49, 2)])));
    }

    [Fact]
    public void MobsWhoseCountIsInAreNotWorthAnotherSwing()
    {
        var conditions = new StopConditions(
            [
                new MobKillCondition(49, "lemur", 3),
                new MobKillCondition(50, "mandragora", 3),
                new DeathCondition(),
            ],
            StopMode.All);

        Assert.Equal([49u], conditions.MobsDone(Progress(mobs: [(49, 8), (50, 1)])));
    }

    [Fact]
    public void NothingIsDoneWhenNothingCountsMobsSeparately()
    {
        // An item run kills everything in the field on purpose: no mob on it
        // has a count of its own to fill.
        var conditions = new StopConditions(
            [new ItemCountCondition(7, 20), new KillCountCondition(30)], StopMode.Any);

        Assert.Empty(conditions.MobsDone(Progress(kills: 30, mobs: [(49, 30)])));
    }

    [Fact]
    public void OnlyMobsWithACountOfTheirOwnAreCounted()
    {
        var conditions = new StopConditions(
            [new MobKillCondition(49, "lemur", 3), new KillCountCondition(30)], StopMode.All);

        Assert.Equal([49u], conditions.MobsCounted);
    }

    [Fact]
    public void PickingARunBackUpLeavesNothingCountingAMobItNoLongerWants()
    {
        // What is left after a death drops the mobs already done, and a run
        // that still went after them would be killing them for nothing: there
        // is no target left to fill and no reason to stop.
        var rest = new StopConditions(
                [new MobKillCondition(49, "lemur", 3), new MobKillCondition(50, "mandragora", 3)],
                StopMode.All)
            .Remaining(Progress(mobs: [(49, 3)]));

        Assert.Equal([50u], rest.MobsCounted);
    }

    [Fact]
    public void ASetWithSomethingToReachSaysSo()
    {
        var conditions = new StopConditions(
            [new KillCountCondition(30), new DeathCondition()], StopMode.Any);

        Assert.True(conditions.Asking);
    }

    [Fact]
    public void ASetOfNothingButSafetyIsAskingForNothing()
    {
        // What is left after a death can come to this: everything the run was
        // told to reach was reached, and dying is what ended it. Starting again
        // on that would be a run with no number to reach.
        var conditions = new StopConditions(
            [new DeathCondition(), new InventoryFullCondition()], StopMode.All);

        Assert.False(conditions.Asking);
    }
}
