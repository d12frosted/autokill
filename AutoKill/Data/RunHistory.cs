using System.Text.Json;
using Dalamud.Plugin.Services;

namespace AutoKill.Data;

/// <summary>What a finished run was, and what it managed.</summary>
/// <remarks>
/// The area is stored as a mob, a territory and a rough position rather than as
/// the area itself. Areas are derived from data that changes between versions,
/// so keeping a copy would preserve spots that no longer exist; looking the area
/// up again on repeat always gives whatever the current data thinks is right.
/// </remarks>
public sealed class RunRecord
{
    public DateTime When { get; set; }

    public uint MobId { get; set; }

    public string MobName { get; set; } = string.Empty;

    public uint TerritoryId { get; set; }

    public float AreaX { get; set; }

    public float AreaZ { get; set; }

    public int Kills { get; set; }

    public double ElapsedSeconds { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Dictionary<uint, int> Gained { get; set; } = [];

    // What was asked for, so repeating asks for the same thing.
    public int KillTarget { get; set; }

    public double MinuteTarget { get; set; }

    public Dictionary<uint, int> ItemTargets { get; set; } = [];

    public bool RequireAll { get; set; }
}

/// <summary>Finished runs, kept so they can be looked at and done again.</summary>
public sealed class RunHistory
{
    private const int Keep = 50;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string path;
    private readonly IPluginLog log;
    private List<RunRecord> records = [];

    public RunHistory(string path, IPluginLog log)
    {
        this.path = path;
        this.log = log;

        try
        {
            if (File.Exists(path))
                records = JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            log.Warning($"Could not read the run history: {ex.Message}");
            records = [];
        }
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<RunRecord> Records => records;

    public void Add(RunRecord record)
    {
        records.Insert(0, record);
        if (records.Count > Keep)
            records.RemoveRange(Keep, records.Count - Keep);

        Save();
    }

    public void Forget(RunRecord record)
    {
        if (records.Remove(record))
            Save();
    }

    public void ForgetEverything()
    {
        if (records.Count == 0)
            return;

        records = [];
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(records, Json));
        }
        catch (Exception ex)
        {
            log.Warning($"Could not save the run history: {ex.Message}");
        }
    }
}
