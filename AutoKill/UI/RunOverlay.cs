using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoKill.UI;

/// <summary>
/// The run at a glance, in a window small enough to leave on screen.
/// </summary>
/// <remarks>
/// The main window is usually behind the game while a run is going, which is
/// the one time anything in it is happening. This one holds the little that is
/// watched rather than read: what is being farmed, how far along, and the two
/// buttons worth pressing. It appears when a run starts and goes when the run
/// ends; the result stays in the main window, which has the room to say more.
///
/// No title bar, because the name of the mob is the title, and dragging works
/// from anywhere on it.
/// </remarks>
public sealed class RunOverlay : Window
{
    // Wide enough for a name and a column of numbers, narrow enough to sit in
    // a corner. The status wraps just inside the cap instead of stretching the
    // window to fit one long sentence.
    private const float WrapAt = 320f;

    private readonly Func<MobIndex?> index;
    private readonly FarmController farming;
    private readonly Configuration config;

    public RunOverlay(Func<MobIndex?> index, FarmController farming, Configuration config)
        : base(
            "AutoKill###AutoKillOverlay",
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.index = index;
        this.farming = farming;
        this.config = config;

        // Open for good; whether it draws is decided per frame below. The run
        // starting and ending is what shows and hides it, not a close box.
        IsOpen = true;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 0),
            MaximumSize = new Vector2(WrapAt + 20f, float.MaxValue),
        };
    }

    public override bool DrawConditions() =>
        config.ShowOverlay && farming.Current is { Phase: not FarmPhase.Finished };

    public override void Draw()
    {
        if (farming.Current is not { } session || session.Phase == FarmPhase.Finished)
            return;

        using var style = Style.Window();

        var progress = session.Progress;
        var paused = session.Phase == FarmPhase.Paused;

        Style.Place(session.Target.Name);
        Style.Trailing($"{progress.Elapsed:hh\\:mm\\:ss}");

        ImGui.PushTextWrapPos(WrapAt);
        ImGui.TextColored(
            paused ? Style.Accent : Style.Muted,
            paused ? $"Paused: {session.Status}" : session.Status);
        ImGui.PopTextWrapPos();

        var kills = session.Conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault();
        if (kills is null)
        {
            ImGui.TextUnformatted("kills");
            Style.Trailing(progress.Kills.ToString());
        }
        else
        {
            Style.Progress("kills", progress.Kills, kills.Target);
        }

        foreach (var wanted in session.Conditions.Conditions.OfType<ItemCountCondition>())
        {
            Style.Progress(
                ItemName(wanted.ItemId), progress.CountOf(wanted.ItemId), wanted.Target);
        }

        Style.Gap(2f);
        if (paused)
        {
            if (ImGui.SmallButton("Resume"))
                farming.Resume();
        }
        else if (ImGui.SmallButton("Pause"))
        {
            farming.Pause();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Stop"))
            farming.Stop();
    }

    private string ItemName(uint itemId) => index()?.ItemName(itemId) ?? $"item {itemId}";
}
