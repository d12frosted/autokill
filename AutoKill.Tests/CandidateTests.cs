using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class CandidateTests
{
    private static Candidate Open() =>
        new(Dead: false, Combatant: true, FateId: 0, SomeoneElses: false);

    [Fact]
    public void AnOpenWorldMobIsFit()
    {
        Assert.Equal(Unfit.None, Open().Why);
        Assert.True(Open().Fit);
    }

    /// <summary>
    /// A FATE mob shares its name with the open world ones around it, so the
    /// name says nothing. Above the FATE's level it cannot even be hit without
    /// syncing down, and syncing down is slow and dangerous, so it is never ours.
    /// </summary>
    [Fact]
    public void AMobInAFateIsNeverFit()
    {
        var fated = Open() with { FateId = 1234 };

        Assert.Equal(Unfit.InAFate, fated.Why);
        Assert.False(fated.Fit);
    }

    [Fact]
    public void SomeoneElsesFightIsNotOurs()
    {
        Assert.Equal(Unfit.SomeoneElses, (Open() with { SomeoneElses = true }).Why);
    }

    [Fact]
    public void WhatIsNotACombatantIsNotFought()
    {
        Assert.Equal(Unfit.NotACombatant, (Open() with { Combatant = false }).Why);
    }

    /// <summary>Dead says the most, so it wins over everything else.</summary>
    [Fact]
    public void DeadComesFirst()
    {
        var gone = new Candidate(Dead: true, Combatant: false, FateId: 1234, SomeoneElses: true);

        Assert.Equal(Unfit.Dead, gone.Why);
    }

    [Theory]
    [InlineData(Unfit.Dead, "dead")]
    [InlineData(Unfit.NotACombatant, "not a combatant")]
    [InlineData(Unfit.InAFate, "in a FATE")]
    [InlineData(Unfit.SomeoneElses, "someone else's")]
    public void EachReasonReadsPlainly(Unfit why, string said)
    {
        Assert.Equal(said, Candidate.Say(why));
    }
}
