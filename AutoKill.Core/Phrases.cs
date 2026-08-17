namespace AutoKill.Core;

/// <summary>Turning several things into something a person would say.</summary>
public static class Phrases
{
    /// <summary>
    /// Words that only join two others, and are left down unless they start
    /// the name.
    /// </summary>
    private static readonly HashSet<string> Joining =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "at", "de", "for", "from", "in", "of", "on", "or",
            "the", "to", "with",
        };

    /// <summary>
    /// A name as a name: "kokkine petalouda" becomes "Kokkine Petalouda".
    /// </summary>
    /// <remarks>
    /// The game keeps mob names in lower case, which is fine in a target bar
    /// where there is one of them and it is the only thing there. In a list of
    /// twenty it is a wall of undifferentiated text, and the eye has nothing to
    /// catch on when looking for where one name ends and the next begins.
    ///
    /// Only the first letter of a word is touched. Numerals and names the game
    /// already shouts are how they are meant to be.
    /// </remarks>
    public static string Capitalise(string name)
    {
        var words = name.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0)
                continue;
            if (i > 0 && Joining.Contains(words[i]))
                continue;

            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }

        return string.Join(' ', words);
    }

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
