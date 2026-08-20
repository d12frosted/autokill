using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

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

    /// <summary>
    /// Which of the game's chat sounds to play when a run ends, zero for none.
    /// The chat line and toast are silent, and a run ends precisely when nobody
    /// is looking at the screen.
    /// </summary>
    public int Chime { get; set; }

    /// <summary>
    /// One of the sixteen chat sounds, the same ones &lt;se.1&gt; through
    /// &lt;se.16&gt; play. Static so the settings screen can preview a sound
    /// without pretending a run finished.
    /// </summary>
    public static void Ring(int sound)
    {
        if (sound is >= 1 and <= 16)
            UIGlobals.PlayChatSoundEffect((uint)sound);
    }

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
        Ring(Chime);
    }

    /// <summary>
    /// The same, with a chat line that can carry item links. Toasts are plain
    /// text and cannot, so the two are written separately rather than one being
    /// flattened into the other.
    /// </summary>
    public void Alert(SeString chatMessage, string toastMessage)
    {
        if (!Enabled)
            return;

        chat.Print(new SeStringBuilder().AddText(Prefix).Append(chatMessage).Build());
        toast.ShowNormal(toastMessage);
        Ring(Chime);
    }
}
