using System.Numerics;
using AutoKill.Core;
using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace AutoKill.UI;

/// <summary>
/// The run at a glance, in a window small enough to leave on screen.
/// </summary>
/// <remarks>
/// The main window is usually behind the game while a run is going, which is
/// the one time anything in it is happening. This one holds the little that is
/// watched rather than read: what is being farmed, how far along, and the
/// buttons worth pressing. It appears when a run starts and goes when the run
/// ends; the result stays in the main window, which has the room to say more.
///
/// While it is up, the main window steps aside rather than repeating it. The
/// cog is the way back to everything this window has no room for: the plan,
/// the tabs, the settings.
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
    private readonly ITextureProvider textures;
    private readonly PastRuns past;
    private readonly Action openMain;

    public RunOverlay(
        Func<MobIndex?> index,
        FarmController farming,
        Configuration config,
        ITextureProvider textures,
        PastRuns past,
        Action openMain)
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
        this.textures = textures;
        this.past = past;
        this.openMain = openMain;

        // Open for good; whether it draws is decided per frame below. The run
        // starting and ending is what shows and hides it, not a close box.
        IsOpen = true;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 0),
            MaximumSize = new Vector2(WrapAt + 40f, float.MaxValue),
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

        if (farming.Queued > 0)
            Style.Muffled($"then {farming.Queued} more stop(s)");

        var kills = session.Conditions.Conditions.OfType<KillCountCondition>().FirstOrDefault();
        if (kills is null)
        {
            ImGui.TextUnformatted("kills");
            Style.Trailing(progress.Kills.ToString());
        }
        else
        {
            Style.Progress(
                "kills", progress.Kills, kills.Target,
                Estimate.Reads(
                    progress.Kills, kills.Target, progress.Elapsed,
                    past.KillsPerHour(session.Target)));
        }

        foreach (var wanted in session.Conditions.Conditions.OfType<ItemCountCondition>())
        {
            var have = progress.CountOf(wanted.ItemId);

            // The icon is how an item is recognised at a glance, which is the
            // whole business of this window.
            Icons.Draw(textures, index()?.ItemIcon(wanted.ItemId) ?? 0);
            Style.Progress(
                ItemName(wanted.ItemId), have, wanted.Target,
                Estimate.Reads(
                    have, wanted.Target, progress.Elapsed,
                    past.PerHour(session.Target, wanted.ItemId)));
        }

        Style.Gap(2f);
        if (paused)
        {
            if (Style.Action("Resume"))
                farming.Resume();
        }
        else if (Style.Action("Pause"))
        {
            farming.Pause();
        }

        ImGui.SameLine();
        if (Style.Action("Stop"))
            farming.Stop();

        ImGui.SameLine();
        if (Style.Action(FontAwesomeIcon.Cog, "Open the main window: the tabs, the plan and the settings."))
            openMain();
    }

    private string ItemName(uint itemId) => index()?.ItemName(itemId) ?? $"item {itemId}";
}
