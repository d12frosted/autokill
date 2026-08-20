using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class RemainingTests
{
    private static FarmProgress After(
        int kills = 0, TimeSpan? elapsed = null, params (uint Item, int Count)[] gained) =>
        new(
            kills,
            elapsed ?? TimeSpan.Zero,
            50,
            InventoryFull: false,
            Died: true,
            gained.ToDictionary(g => g.Item, g => g.Count));

    [Fact]
    public void KillsAlreadyMadeComeOffTheTarget()
    {
        var rest = new StopConditions([new KillCountCondition(30)], StopMode.Any)
            .Remaining(After(kills: 12));

        var kills = Assert.IsType<KillCountCondition>(Assert.Single(rest.Conditions));
        Assert.Equal(18, kills.Target);
    }

    [Fact]
    public void ItemsAlreadyHeldComeOffTheirs()
    {
        // Loot survives a death, so what was gained stays gained.
        var rest = new StopConditions([new ItemCountCondition(7, 20)], StopMode.Any)
            .Remaining(After(gained: (7, 15)));

        var item = Assert.IsType<ItemCountCondition>(Assert.Single(rest.Conditions));
        Assert.Equal(5, item.Target);
    }

    [Fact]
    public void TimeSpentComesOffTheLimit()
    {
        var rest = new StopConditions(
                [new ElapsedCondition(TimeSpan.FromMinutes(45))], StopMode.Any)
            .Remaining(After(elapsed: TimeSpan.FromMinutes(20)));

        var time = Assert.IsType<ElapsedCondition>(Assert.Single(rest.Conditions));
        Assert.Equal(TimeSpan.FromMinutes(25), time.Limit);
    }

    [Fact]
    public void AMetTargetIsDropped()
    {
        // In an all-of set, whatever was finished before the death is finished.
        var rest = new StopConditions(
                [new KillCountCondition(30), new ItemCountCondition(7, 20)], StopMode.All)
            .Remaining(After(kills: 30, gained: (7, 2)));

        var item = Assert.IsType<ItemCountCondition>(Assert.Single(rest.Conditions));
        Assert.Equal(18, item.Target);
    }

    [Fact]
    public void SafetyRidesAlongUntouched()
    {
        var rest = new StopConditions(
                [new KillCountCondition(30), new DeathCondition(), new InventoryFullCondition()],
                StopMode.Any)
            .Remaining(After(kills: 10));

        Assert.Contains(rest.Conditions, c => c is DeathCondition);
        Assert.Contains(rest.Conditions, c => c is InventoryFullCondition);
    }

    [Fact]
    public void AbsoluteTargetsStayAbsolute()
    {
        // A level is where the character stands, not something accumulated by
        // this run, so there is nothing to subtract.
        var rest = new StopConditions([new LevelCondition(60)], StopMode.Any)
            .Remaining(After(kills: 100));

        var level = Assert.IsType<LevelCondition>(Assert.Single(rest.Conditions));
        Assert.Equal(60, level.Level);
    }

    [Fact]
    public void TheModeSurvives()
    {
        var rest = new StopConditions([new KillCountCondition(30)], StopMode.All)
            .Remaining(After());

        Assert.Equal(StopMode.All, rest.Mode);
    }
}
