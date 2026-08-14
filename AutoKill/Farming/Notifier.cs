using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>Telling you what happened while you were not looking.</summary>
/// <remarks>
/// The whole point of a run with a target is that you go and do something else,
/// which makes a status line in a window nobody is watching worthless. Chat is
/// the reliable half: it is local only, like /echo, it persists in the log, and
/// it is still there when you come back. The toast is the half that catches the
/// eye of someone still at the keyboard.
/// </remarks>
public sealed class Notifier(IChatGui chat, IToastGui toast)
{
    private const string Prefix = "[AutoKill] ";

    public bool Enabled { get; set; } = true;

    /// <summary>Worth a line in the log, not worth interrupting anyone.</summary>
    public void Info(string message)
    {
        if (Enabled)
            chat.Print(Prefix + message);
    }

    /// <summary>The run is over, or wants attention. Say it twice.</summary>
    public void Alert(string message)
    {
        if (!Enabled)
            return;

        chat.Print(Prefix + message);
        toast.ShowNormal(message);
    }
}
