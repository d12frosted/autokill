namespace AutoKill.Core;

/// <summary>Why something of the right name is still not one to fight.</summary>
public enum Unfit
{
    None,
    Dead,
    NotACombatant,

    /// <summary>
    /// Spawned by a FATE. It wears the same name as the open world mobs around
    /// it, so nothing about the name gives it away.
    /// </summary>
    InAFate,

    /// <summary>Someone else's fight, which is not ours to steal.</summary>
    SomeoneElses,
}

/// <summary>A mob of the right name, as much of it as says whether to fight it.</summary>
/// <remarks>
/// A FATE mob is never fit, whatever it drops and whatever it counts for. Above
/// the FATE's level nothing lands on it until the character syncs down, and a
/// run that syncs down is slower and easier to kill than one that does not. So
/// it is passed over like a corpse, rather than picked and then stood beside
/// while every cast fails.
/// </remarks>
/// <param name="FateId">The FATE that spawned it, or zero for the open world.</param>
public readonly record struct Candidate(bool Dead, bool Combatant, ushort FateId, bool SomeoneElses)
{
    public Unfit Why =>
        Dead ? Unfit.Dead
        : !Combatant ? Unfit.NotACombatant
        : FateId != 0 ? Unfit.InAFate
        : SomeoneElses ? Unfit.SomeoneElses
        : Unfit.None;

    public bool Fit => Why == Unfit.None;

    /// <summary>The reason as a recording says it.</summary>
    public static string Say(Unfit why) => why switch
    {
        Unfit.Dead => "dead",
        Unfit.NotACombatant => "not a combatant",
        Unfit.InAFate => "in a FATE",
        Unfit.SomeoneElses => "someone else's",
        _ => "fit",
    };
}
