namespace AutoKill.Core;

/// <summary>Turning several things into something a person would say.</summary>
public static class Phrases
{
    /// <summary>
    /// Names read out in full: "a", "a and b", "a, b and c".
    /// </summary>
    /// <remarks>
    /// Counted instead ("three kinds of mob") it says nothing about whether the
    /// right three were picked, which is the only question a reader has.
    /// </remarks>
    public static string List(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + $" and {names[^1]}",
    };
}
