using System.Text.Json;
using Dalamud.Plugin.Services;

namespace AutoKill.Data;

/// <summary>What farming a mob has actually taught us about it.</summary>
public sealed class MobObservations
{
    /// <summary>
    /// Seconds between emptying a spot and finding it populated again.
    /// </summary>
    /// <remarks>
    /// Not a respawn timer, and not pretending to be one. It is how long it took
    /// to find things standing there again, which includes however long it took
    /// to come back, so it always reads a little long. That is the number worth
    /// having anyway: the question a circuit asks is when it is worth returning,
    /// not when the server ticked.
    /// </remarks>
    public List<double> Repopulated { get; set; } = [];

    /// <summary>Where this mob has been seen, rounded, with how often.</summary>
    public Dictionary<string, int> Seen { get; set; } = [];

    public int Kills { get; set; }

    /// <summary>
    /// The middle of what has been seen, ignoring the long tail of one-off
    /// sightings: a single mob wandering does not move a farm spot.
    /// </summary>
    public TimeSpan? TypicalRepopulation()
    {
        if (Repopulated.Count < 3)
            return null;

        var sorted = Repopulated.Order().ToList();
        return TimeSpan.FromSeconds(sorted[sorted.Count / 2]);
    }
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

    public void RecordRepopulation(uint bNpcNameId, uint territoryId, TimeSpan taken)
    {
        // A gap measured across a logout or a trip to another zone says nothing
        // about how fast anything respawns.
        if (taken <= TimeSpan.Zero || taken > TimeSpan.FromMinutes(10))
            return;

        var mob = For(bNpcNameId, territoryId);
        mob.Repopulated.Add(Math.Round(taken.TotalSeconds, 1));

        // Recent behaviour is the useful kind, and the file has no business
        // growing forever.
        if (mob.Repopulated.Count > 50)
            mob.Repopulated.RemoveRange(0, mob.Repopulated.Count - 50);

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
