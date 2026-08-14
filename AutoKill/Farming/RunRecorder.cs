using System.Text.Json;
using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>
/// Writes what a run actually did to a file, one JSON object per line.
/// </summary>
/// <remarks>
/// Watching a run and thinking it looked wasteful is not the same as knowing
/// where the waste is. A trace makes the question answerable afterwards: how
/// long was spent travelling versus fighting, whether a mob was standing next to
/// the character while it walked somewhere else, whether a mount was summoned
/// for a distance that did not need one.
///
/// Nothing here is allowed to disturb the run. Every write is guarded, and a
/// recorder that cannot write simply stops recording.
/// </remarks>
public sealed class RunRecorder : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IPluginLog log;
    private readonly DateTime started = DateTime.UtcNow;
    private StreamWriter? writer;
    private int sinceFlush;

    public RunRecorder(string directory, string name, IPluginLog log)
    {
        this.log = log;

        try
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, name);
            writer = new StreamWriter(Path, append: false) { AutoFlush = false };
        }
        catch (Exception ex)
        {
            log.Warning($"Could not start a run trace: {ex.Message}");
            writer = null;
        }
    }

    public string? Path { get; }

    public void Write(string kind, object? data = null)
    {
        if (writer is null)
            return;

        try
        {
            var line = JsonSerializer.Serialize(
                new
                {
                    t = Math.Round((DateTime.UtcNow - started).TotalSeconds, 2),
                    kind,
                    data,
                },
                Json);

            writer.WriteLine(line);

            // Flushing every line would be a write per frame; never flushing
            // would lose the tail of any run that ends in a crash.
            if (++sinceFlush < 50)
                return;
            sinceFlush = 0;
            writer.Flush();
        }
        catch (Exception ex)
        {
            log.Warning($"Run trace stopped: {ex.Message}");
            Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            writer?.Flush();
            writer?.Dispose();
        }
        catch (Exception)
        {
            // Nothing useful to do about a trace that will not close.
        }

        writer = null;
    }
}
