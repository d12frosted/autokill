using AutoKill.Core;

namespace AutoKill.Data;

/// <summary>
/// What a run goes after: a stretch of ground, and every kind of mob standing
/// in it that is worth killing.
/// </summary>
/// <remarks>
/// Usually one kind. But an item search asks for an item rather than a mob, and
/// several mobs sharing a field all drop it, so killing only the one that was
/// picked means flying past the rest. A run that carries the whole set treats
/// the field as what it is.
/// </remarks>
public sealed record FarmTarget(IReadOnlyList<MobEntry> Mobs, FarmArea Area)
{
    public FarmTarget(MobEntry mob, FarmArea area)
        : this([mob], area)
    {
    }

    public bool Shared => Mobs.Count > 1;

    public IReadOnlyList<uint> BNpcNameIds => Mobs.Select(mob => mob.BNpcNameId).ToList();

    /// <summary>Everything any of them drops, each item once.</summary>
    public IReadOnlyList<uint> Drops => Mobs.SelectMany(mob => mob.Drops).Distinct().ToList();

    /// <summary>
    /// All of them in one phrase, for saying what a run is doing. Read out in
    /// full rather than counted, because "three kinds of mob" tells nobody
    /// whether the right three were picked.
    /// </summary>
    public string Name => Phrases.Kinds(Mobs.Select(mob => mob.Name).ToList());

    /// <summary>
    /// What tells them apart, for putting each one on a control of its own.
    /// Three buttons reading "petalouda" would not be a choice.
    /// </summary>
    public IReadOnlyList<string> Distinct => Phrases.Split(Mobs.Select(mob => mob.Name).ToList()).Distinct;

    /// <summary>Which of them this is, for talking about the one in front of you.</summary>
    public string NameOf(uint bNpcNameId) =>
        Mobs.FirstOrDefault(mob => mob.BNpcNameId == bNpcNameId)?.Name ?? Mobs[0].Name;
}
