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

    /// <summary>
    /// The same, with whatever every name ends in said once:
    /// "ianthine, kokkine and kyane petalouda".
    /// </summary>
    /// <remarks>
    /// Mobs sharing a field usually share most of their name, and the shared
    /// part is the half that carries no information. Repeated three times it
    /// fills a line and pushes the words that tell them apart off the end of it.
    ///
    /// Whole words only, and never so far that a name is left as nothing but
    /// the part it shares: "and petalouda" names no mob.
    /// </remarks>
    public static string Kinds(IReadOnlyList<string> names)
    {
        var (distinct, shared) = Split(names);
        return shared.Length == 0 ? List(names) : $"{List(distinct)} {shared}";
    }

    /// <summary>
    /// The same names as what tells them apart, and the ending they share.
    /// Nothing is shared when there are fewer than two names, or when folding
    /// would leave one of them empty.
    /// </summary>
    public static (IReadOnlyList<string> Distinct, string Shared) Split(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return (names, string.Empty);

        var words = names.Select(name => name.Split(' ')).ToList();
        var shared = 0;

        while (words.All(name => name.Length > shared + 1)
               && words.All(name => name[^(shared + 1)] == words[0][^(shared + 1)]))
        {
            shared++;
        }

        return shared == 0
            ? (names, string.Empty)
            : (words.Select(name => string.Join(' ', name[..^shared])).ToList(),
               string.Join(' ', words[0][^shared..]));
    }
}
