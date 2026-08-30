using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class JobFitnessTests
{
    // Real ClassJob rows, so the ids read as the jobs they are.
    private const uint Paladin = 19;
    private const uint WhiteMage = 24;
    private const uint Samurai = 34;
    private const uint Machinist = 31;
    private const uint Weaver = 12;

    private static JobStanding Tank(int level) => new(Paladin, "Paladin", JobRole.Tank, level);

    private static JobStanding Melee(int level) => new(Samurai, "Samurai", JobRole.Melee, level);

    private static JobStanding Ranged(int level) => new(Machinist, "Machinist", JobRole.Ranged, level);

    private static JobStanding Healer(int level) => new(WhiteMage, "White Mage", JobRole.Healer, level);

    private static JobStanding Crafter(int level = 100) => new(Weaver, "Weaver", JobRole.None, level);

    private static LevelRange Level(ushort lowest, ushort highest) => new(lowest, highest);

    [Fact]
    public void ABattleJobHighEnoughIsFine()
    {
        Assert.Equal(JobProblem.None, JobFitness.Problem(Tank(95), Level(90, 95)));
    }

    [Fact]
    public void MeetingTheTopOfTheRangeExactlyIsEnough()
    {
        Assert.Equal(JobProblem.None, JobFitness.Problem(Tank(43), Level(41, 43)));
    }

    [Fact]
    public void ShortOfTheTopOfTheRangeIsNotEnough()
    {
        // The top rather than the bottom: a field is patrolled whole, and being
        // able to kill the easiest thing in it is not the question.
        Assert.Equal(JobProblem.Underlevelled, JobFitness.Problem(Tank(42), Level(41, 43)));
    }

    [Fact]
    public void ACrafterCannotFightHoweverHighItIs()
    {
        Assert.Equal(JobProblem.NotABattleJob, JobFitness.Problem(Crafter(), Level(1, 1)));
    }

    [Fact]
    public void AnUnrecordedLevelBlocksNothing()
    {
        // Three percent of the points carry no level and 302 mobs carry none
        // anywhere. Refusing to go there would be a guess dressed as a fact.
        Assert.Equal(JobProblem.None, JobFitness.Problem(Tank(1), null));
    }

    [Fact]
    public void ACrafterIsStillWrongWhenNothingIsKnownAboutTheLevel()
    {
        Assert.Equal(JobProblem.NotABattleJob, JobFitness.Problem(Crafter(), null));
    }

    [Fact]
    public void SomethingThatKillsThingsBeatsSomethingThatSurvivesThem()
    {
        // A tank clears a field eventually and a healer barely clears it at all.
        // Farming is damage, so the one that does damage goes, even when it is
        // the lower of the two.
        var best = JobFitness.Best(
            [
                new JobSwitch(0, "PLD", Tank(100)),
                new JobSwitch(1, "WHM", Healer(100)),
                new JobSwitch(2, "SAM", Melee(90)),
            ],
            Level(40, 45));

        Assert.Equal(2, best!.GearsetId);
    }

    [Fact]
    public void MeleeAndRangedAreTheSameKindOfAnswer()
    {
        // Both kill things at a reasonable rate, which is the whole test. The
        // higher one wins, as it would within either.
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(70)), new JobSwitch(1, "MCH", Ranged(90))],
            Level(40, 45));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void ATankStillBeatsAHealerWhenThereIsNoDamageToBeHad()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "WHM", Healer(100)), new JobSwitch(1, "PLD", Tank(90))],
            Level(40, 45));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void TheHighestOfEqualStandingWins()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(50)), new JobSwitch(1, "MCH", Ranged(90))],
            Level(40, 45));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void GearsetsThatCannotFightAreNeverPicked()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "WVR", Crafter()), new JobSwitch(1, "SAM", Melee(50))],
            Level(40, 45));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void GearsetsTooLowForItAreNeverPicked()
    {
        Assert.Null(JobFitness.Best([new JobSwitch(0, "SAM", Melee(44))], Level(40, 45)));
    }

    [Fact]
    public void ATankThatCanManageBeatsADamageJobThatCannot()
    {
        // Preferring damage is about which of the ones that will do, not about
        // sending something that will die.
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(40)), new JobSwitch(1, "PLD", Tank(90))],
            Level(85, 90));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void NothingToSwitchIntoIsNoSwitch()
    {
        Assert.Null(JobFitness.Best([], Level(40, 45)));
    }

    [Fact]
    public void TwoGearsetsOfEqualStandingPickTheEarlierOne()
    {
        // Whichever is chosen has to be the same one every time, or a run that
        // was refused once starts the next time for no visible reason.
        var best = JobFitness.Best(
            [new JobSwitch(3, "SAM b", Melee(90)), new JobSwitch(1, "SAM a", Melee(90))],
            Level(40, 45));

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void AnyBattleGearsetWillDoWhenTheLevelIsUnrecorded()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "WVR", Crafter()), new JobSwitch(1, "SAM", Melee(12))],
            null);

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void TheJobYouAskedForWinsOverEverything()
    {
        // Including over the ordering that would otherwise pick the damage job.
        // Somebody who named a job named it on purpose.
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(100)), new JobSwitch(1, "PLD", Tank(90))],
            Level(40, 45),
            Paladin);

        Assert.Equal(1, best!.GearsetId);
    }

    [Fact]
    public void TheJobYouAskedForIsSkippedWhenItCannotManage()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(100)), new JobSwitch(1, "PLD", Tank(40))],
            Level(85, 90),
            Paladin);

        Assert.Equal(0, best!.GearsetId);
    }

    [Fact]
    public void TheJobYouAskedForIsSkippedWhenYouHaveNoGearsetForIt()
    {
        var best = JobFitness.Best(
            [new JobSwitch(0, "SAM", Melee(100))], Level(40, 45), Paladin);

        Assert.Equal(0, best!.GearsetId);
    }

    [Fact]
    public void TwoGearsetsForTheJobYouAskedForPickTheEarlierOne()
    {
        var best = JobFitness.Best(
            [new JobSwitch(4, "PLD b", Tank(90)), new JobSwitch(2, "PLD a", Tank(90))],
            Level(40, 45),
            Paladin);

        Assert.Equal(2, best!.GearsetId);
    }

    [Fact]
    public void TroubleSaysWhichJobAndWhy()
    {
        Assert.Equal("Weaver cannot fight.", JobFitness.Trouble(Crafter(), Level(1, 1)));
        Assert.Equal(
            "Paladin is level 42, and this is Lv41-43.",
            JobFitness.Trouble(Tank(42), Level(41, 43)));
    }

    [Fact]
    public void NothingWrongIsSaidAsNothing()
    {
        Assert.Null(JobFitness.Trouble(Tank(90), Level(41, 43)));
    }

    private static readonly IReadOnlyList<JobSwitch> Wardrobe =
    [
        new JobSwitch(0, "WVR", Crafter()),
        new JobSwitch(1, "PLD", Tank(42)),
        new JobSwitch(2, "SAM", Melee(90)),
    ];

    [Fact]
    public void AJobThatFitsNeedsNoPlanAtAll()
    {
        var plan = JobFitness.Plan(Melee(90), Level(41, 43), Wardrobe, JobPolicy.Switch);

        Assert.False(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Null(plan.Says);
    }

    [Fact]
    public void IgnoringItStartsAnywayAndSaysSo()
    {
        // Mobs a few levels up are killable, and somebody who wants to try is
        // entitled to. Saying nothing at all would leave a death looking like a
        // bug in the run.
        var plan = JobFitness.Plan(Tank(42), Level(41, 43), Wardrobe, JobPolicy.Ignore);

        Assert.False(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Equal("Paladin is level 42, and this is Lv41-43. Going anyway.", plan.Says);
    }

    [Fact]
    public void RefusingBlocksAndNamesTheReason()
    {
        var plan = JobFitness.Plan(Crafter(), Level(41, 43), Wardrobe, JobPolicy.Refuse);

        Assert.True(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Equal("Weaver cannot fight.", plan.Says);
    }

    [Fact]
    public void RefusingNeverLooksAtTheGearsetsEvenWhenOneWouldDo()
    {
        var plan = JobFitness.Plan(Crafter(), Level(41, 43), Wardrobe, JobPolicy.Refuse);

        Assert.Null(plan.Change);
    }

    [Fact]
    public void SwitchingPicksAGearsetAndDoesNotBlock()
    {
        var plan = JobFitness.Plan(Crafter(), Level(41, 43), Wardrobe, JobPolicy.Switch);

        Assert.False(plan.Blocked);
        Assert.Equal(2, plan.Change!.GearsetId);
        Assert.Equal("Weaver cannot fight. Switching to Samurai first.", plan.Says);
    }

    [Fact]
    public void SwitchingHonoursTheJobYouAskedFor()
    {
        // A field the level 42 Paladin can manage, so the only thing keeping the
        // level 90 Samurai out of it is having been asked for the Paladin.
        var plan = JobFitness.Plan(Crafter(), Level(40, 42), Wardrobe, JobPolicy.Switch, Paladin);

        Assert.Equal(1, plan.Change!.GearsetId);
        Assert.Equal("Weaver cannot fight. Switching to Paladin first.", plan.Says);
    }

    [Fact]
    public void SwitchingBlocksWhenNothingInTheWardrobeGetsThere()
    {
        var plan = JobFitness.Plan(Crafter(), Level(95, 100), Wardrobe, JobPolicy.Switch);

        Assert.True(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Equal(
            "Weaver cannot fight. Nothing you can switch to gets there.",
            plan.Says);
    }

    [Fact]
    public void SwitchingAwayFromAJobThatIsMerelyTooLowWorksTheSameWay()
    {
        var plan = JobFitness.Plan(Tank(42), Level(85, 90), Wardrobe, JobPolicy.Switch);

        Assert.Equal(2, plan.Change!.GearsetId);
        Assert.Equal(
            "Paladin is level 42, and this is Lv85-90. Switching to Samurai first.",
            plan.Says);
    }

    // The hunting log names who has to land the kill, so the class stops being
    // a preference and becomes the requirement.

    [Fact]
    public void AlreadyInTheClassTheLogNamesIsNothingToDo()
    {
        var plan = JobFitness.PlanAs(
            Tank(42), Level(41, 42), Wardrobe, Paladin, "Paladin", JobPolicy.Switch);

        Assert.False(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Null(plan.Says);
    }

    [Fact]
    public void AnotherClassMeansPuttingOnTheGearsetForTheOneNamed()
    {
        var plan = JobFitness.PlanAs(
            Melee(90), Level(41, 42), Wardrobe, Paladin, "Paladin", JobPolicy.Switch);

        Assert.Equal(1, plan.Change!.GearsetId);
        Assert.Equal("The log is Paladin's. Switching to it first.", plan.Says);
    }

    [Fact]
    public void NoGearsetForTheClassBlocksTheRun()
    {
        // The game offers no way to equip a bare class, so a log for a class
        // nobody has kitted out cannot be farmed at all.
        var plan = JobFitness.PlanAs(
            Melee(90), Level(1, 5), Wardrobe, WhiteMage, "White Mage", JobPolicy.Switch);

        Assert.True(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Equal(JobProblem.NoGearset, plan.Problem);
        Assert.Equal("You have no White Mage gearset to put on.", plan.Says);
    }

    [Fact]
    public void GroundAboveTheClassIsSaidRatherThanSwappedAwayFrom()
    {
        // Switching is exactly what must not happen here, so the standing
        // instruction narrows to going anyway or refusing.
        var plan = JobFitness.PlanAs(
            Tank(42), Level(50, 55), Wardrobe, Paladin, "Paladin", JobPolicy.Switch);

        Assert.False(plan.Blocked);
        Assert.Null(plan.Change);
        Assert.Equal("Paladin is level 42, and this is Lv50-55. Going anyway.", plan.Says);
    }

    [Fact]
    public void RefusingStillRefusesWhenTheClassIsTooLow()
    {
        var plan = JobFitness.PlanAs(
            Tank(42), Level(50, 55), Wardrobe, Paladin, "Paladin", JobPolicy.Refuse);

        Assert.True(plan.Blocked);
        Assert.Equal("Paladin is level 42, and this is Lv50-55.", plan.Says);
    }

    [Fact]
    public void TheHighestGearsetForTheClassIsTheOneWorn()
    {
        IReadOnlyList<JobSwitch> two =
        [
            new JobSwitch(3, "PLD old", new JobStanding(Paladin, "Paladin", JobRole.Tank, 42)),
            new JobSwitch(4, "PLD", new JobStanding(Paladin, "Paladin", JobRole.Tank, 42)),
        ];

        var plan = JobFitness.PlanAs(
            Melee(90), Level(41, 42), two, Paladin, "Paladin", JobPolicy.Switch);

        Assert.Equal(3, plan.Change!.GearsetId);
    }
}
