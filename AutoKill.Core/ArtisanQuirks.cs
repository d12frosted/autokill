namespace AutoKill.Core;

/// <summary>
/// Why a crafting list can be full in Artisan and empty here.
/// </summary>
/// <remarks>
/// Lists are read out of Artisan's configuration file, so anything Artisan has
/// not written out yet does not exist as far as this plugin is concerned.
/// Artisan does try: every path that edits a list ends in a save.
///
/// One path does not get there. Filling a list with "Add all visible" adds the
/// recipes in a background task and then, in the continuation, refreshes its own
/// table before saving. The refresh reads the local player, which Dalamud only
/// allows on the main thread, so the exception lands on the line before the save
/// and the recipes stay in memory until something else saves the config.
///
/// 4.0.5.18 is the current release and does this. Nothing later has been looked
/// at, so a newer Artisan is neither accused nor trusted: somebody staring at a
/// list that is full in one window and empty in the other is better served by
/// the symptom and their own version number than by a guess either way.
/// </remarks>
public static class ArtisanQuirks
{
    /// <summary>The latest Artisan known to lose list edits this way.</summary>
    public static readonly Version LastKnownToLoseListEdits = new(4, 0, 5, 18);

    /// <summary>
    /// Whether this Artisan is one of the versions that does it. An Artisan that
    /// is not answering has no version, and inventing one would be worse than
    /// saying so.
    /// </summary>
    public static bool LosesListEdits(Version? artisan) =>
        artisan is not null && artisan <= LastKnownToLoseListEdits;

    /// <summary>What to say about a list that reads as empty.</summary>
    public static string WhyEmpty(Version? artisan)
    {
        if (artisan is null)
        {
            return "Artisan is not answering, so this list is only as new as the "
                   + "last time Artisan wrote its file.";
        }

        return LosesListEdits(artisan)
            ? "Filling a list with \"Add all visible\" loses Artisan's save, so a "
              + "list can be full in Artisan and empty in its file. Ask it to save, "
              + "or add the recipes one at a time."
            : $"If this list is not empty in Artisan, then {artisan} still loses the "
              + "save when a list is filled with \"Add all visible\".";
    }
}
