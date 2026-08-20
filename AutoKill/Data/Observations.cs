using System.Text.Json;
using AutoKill.Core;
using Dalamud.Plugin.Services;

namespace AutoKill.Data;

/// <summary>What farming a mob has actually taught us about it.</summary>
public sealed class MobObservations
{
    /// <summary>
    /// Seconds between one thing dying at a spawn point and the next standing
    /// on it, watched from close enough to see both ends.
    /// </summary>
    /// <remarks>
    /// The respawn itself, near enough: no travel in it and no waiting on a
    /// circuit, so it is the number the rotation wants. It is also the only one
    /// a busy field ever produces, since a field that is never empty and never
    /// left gives the other two measurements nothing to work with.
    /// </remarks>
    public List<double> Respawned { get; set; } = [];

    /// <summary>
    /// Seconds between emptying a spot and finding it populated again.
    /// </summary>
    /// <remarks>
    /// Not a respawn timer, and not pretending to be one. It is how long it took
    /// to find things standing there again, which includes however long it took
    /// to come back, so it always reads a little long. That is still worth
    /// having: the question a circuit asks is when it is worth returning, not
    /// when the server ticked.
    /// </remarks>
    public List<double> Repopulated { get; set; } = [];

    /// <summary>Where this mob has been seen, rounded, with how often.</summary>
    public Dictionary<string, int> Seen { get; set; } = [];

    public int Kills { get; set; }

    /// <summary>What to expect of this mob, and what says so.</summary>
    public Repopulation? Typical() => Repopulation.From(Respawned, Repopulated);
}

/// <summary>
/// The plugin's memory of places it has already farmed, kept between sessions.
/// </summary>
/// <remarks>
/// Shipped data says where a mob lives. Only farming it says how quickly it
/// comes back, and that is what decides when a circuit should return rather than
/// stand waiting.
///
/// Nothing here is required. A missing or unreadable file simply means nothing
/// has been learnt yet, which is where every character starts.
/// </remarks>
public sealed class Observations
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // How many measurements of one kind are kept for one mob. Enough for the
    // middle of them to mean something, few enough that a zone reworked in a
    // patch is forgotten within an evening of farming it.
    private const int Kept = 50;

    private readonly string path;
    private readonly IPluginLog log;
    private Dictionary<string, MobObservations> entries = [];
    private bool dirty;

    public Observations(string path, IPluginLog log)
    {
        this.path = path;
        this.log = log;

        try
        {
            if (File.Exists(path))
                entries = JsonSerializer.Deserialize<Dictionary<string, MobObservations>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            log.Warning($"Could not read what was learnt previously: {ex.Message}");
            entries = [];
        }
    }

    /// <summary>Everything learnt so far, for showing and for forgetting.</summary>
    public IEnumerable<(uint MobId, uint TerritoryId, MobObservations What)> Entries =>
        entries
            .Select(pair =>
            {
                var parts = pair.Key.Split(':');
                return uint.TryParse(parts.ElementAtOrDefault(0), out var mob)
                       && uint.TryParse(parts.ElementAtOrDefault(1), out var territory)
                    ? ((uint MobId, uint TerritoryId, MobObservations What)?)(mob, territory, pair.Value)
                    : null;
            })
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .OrderByDescending(entry => entry.What.Kills);

    public void Forget(uint bNpcNameId, uint territoryId)
    {
        if (!entries.Remove($"{bNpcNameId}:{territoryId}"))
            return;

        dirty = true;
        Save();
    }

    public void ForgetEverything()
    {
        if (entries.Count == 0)
            return;

        entries = [];
        dirty = true;
        Save();
    }

    /// <summary>
    /// What has been learnt about a mob, or nothing. Asking never invents an
    /// entry, so a run that goes after several kinds and only ever meets one
    /// does not fill the Learned tab with rows about mobs it never saw.
    /// </summary>
    public MobObservations? Known(uint bNpcNameId, uint territoryId) =>
        entries.GetValueOrDefault($"{bNpcNameId}:{territoryId}");

    private MobObservations For(uint bNpcNameId, uint territoryId)
    {
        var key = $"{bNpcNameId}:{territoryId}";
        if (entries.TryGetValue(key, out var found))
            return found;

        return entries[key] = new MobObservations();
    }

    /// <summary>One spawn point timed from a death to what stood there next.</summary>
    public void RecordRespawn(uint bNpcNameId, uint territoryId, TimeSpan taken) =>
        Record(For(bNpcNameId, territoryId).Respawned, taken);

    /// <summary>One spot found populated again on coming back to it.</summary>
    public void RecordRepopulation(uint bNpcNameId, uint territoryId, TimeSpan taken) =>
        Record(For(bNpcNameId, territoryId).Repopulated, taken);

    private void Record(List<double> seconds, TimeSpan taken)
    {
        // A gap measured across a logout or a trip to another zone says nothing
        // about how fast anything respawns.
        if (taken <= TimeSpan.Zero || taken > TimeSpan.FromMinutes(10))
            return;

        seconds.Add(Math.Round(taken.TotalSeconds, 1));

        // Recent behaviour is the useful kind, and the file has no business
        // growing forever.
        if (seconds.Count > Kept)
            seconds.RemoveRange(0, seconds.Count - Kept);

        dirty = true;
    }

    public void RecordSighting(uint bNpcNameId, uint territoryId, float x, float z)
    {
        // Rounded, so standing in roughly the same field counts as the same
        // place and the file stays small however long the farming goes on.
        var key = $"{Math.Round(x / 5f) * 5:F0},{Math.Round(z / 5f) * 5:F0}";
        var mob = For(bNpcNameId, territoryId);
        mob.Seen[key] = mob.Seen.GetValueOrDefault(key) + 1;
        dirty = true;
    }

    public void RecordKill(uint bNpcNameId, uint territoryId)
    {
        For(bNpcNameId, territoryId).Kills++;
        dirty = true;
    }

    public void Save()
    {
        if (!dirty)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, Json));
            dirty = false;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not save what was learnt: {ex.Message}");
        }
    }
}
