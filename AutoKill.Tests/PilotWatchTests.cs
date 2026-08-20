using System.Numerics;
using AutoKill.Core;
using Xunit;

namespace AutoKill.Tests;

public class PilotWatchTests
{
    private const float Tolerance = 4f;

    private static PilotWatch NewWatch() => new(Tolerance);

    private static Vector3 At(float x, float z, float y = 0f) => new(x, y, z);

    [Fact]
    public void TheFirstLookOnlyAnchors()
    {
        // Wherever the character already is, is where it is. There is no ground
        // to have walked away from yet.
        Assert.False(NewWatch().Check(At(100f, 100f), steered: false, excused: false));
    }

    [Fact]
    public void SteeredMovementIsOurs()
    {
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);

        Assert.False(watch.Check(At(30f, 0f), steered: true, excused: false));
        Assert.False(watch.Check(At(60f, 0f), steered: true, excused: false));
    }

    [Fact]
    public void StandingStillIsNotInput()
    {
        // Hovering on a mount is not perfectly still, so a little drift has to
        // pass for standing.
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);

        Assert.False(watch.Check(At(0.4f, 0.3f), steered: false, excused: false));
        Assert.False(watch.Check(At(0.1f, 0.6f), steered: false, excused: false));
    }

    [Fact]
    public void AWalkAwayIsInput()
    {
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);

        Assert.True(watch.Check(At(6f, 0f), steered: false, excused: false));
    }

    [Fact]
    public void FallingIsNotInput()
    {
        // A dismount in the air is followed by a drop. Height changes without
        // anyone touching anything, so only the ground plane counts.
        var watch = NewWatch();
        watch.Check(At(0f, 0f, y: 30f), steered: false, excused: false);

        Assert.False(watch.Check(At(0f, 0f, y: 0f), steered: false, excused: false));
    }

    [Fact]
    public void SteeringMovesTheAnchor()
    {
        // The run walked the character somewhere. Standing there afterwards is
        // not walking away from where it used to be.
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);
        watch.Check(At(50f, 50f), steered: true, excused: false);

        Assert.False(watch.Check(At(50.5f, 50f), steered: false, excused: false));
    }

    [Fact]
    public void ExcusedMovementMovesTheAnchor()
    {
        // A knockback in combat moves the character without any input. Where it
        // lands is the new ground, and a real walk after the excuse lifts is
        // still noticed from there.
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);
        watch.Check(At(15f, 0f), steered: false, excused: true);

        Assert.False(watch.Check(At(15f, 0f), steered: false, excused: false));
        Assert.True(watch.Check(At(21f, 0f), steered: false, excused: false));
    }

    [Fact]
    public void ResetForgetsTheGround()
    {
        // A resume, or a new zone, starts from wherever the character stands.
        // Held over the gap, the old anchor would read the whole trip as input.
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);

        watch.Reset();
        Assert.False(watch.Check(At(80f, 80f), steered: false, excused: false));
    }

    [Fact]
    public void OneWalkKeepsBeingInputUntilReset()
    {
        // The caller pauses on the first true, but a tick that slips in before
        // the reset should not quietly re-anchor and lose the answer.
        var watch = NewWatch();
        watch.Check(At(0f, 0f), steered: false, excused: false);

        Assert.True(watch.Check(At(6f, 0f), steered: false, excused: false));
        Assert.True(watch.Check(At(6f, 0f), steered: false, excused: false));
    }
}
