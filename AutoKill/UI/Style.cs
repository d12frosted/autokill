using System.Numerics;
using AutoKill.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AutoKill.UI;

/// <summary>
/// The window's visual language, in one place.
/// </summary>
/// <remarks>
/// Every screen here shows the same four things: somewhere to go, how much is
/// there, what it would cost you and a way to start. Drawn ad hoc they came out
/// four different ways, with numbers buried in the middle of sentences and
/// nothing lining up down the page, which is the difference between a window
/// you read and one you decipher.
///
/// So: names on the left, numbers on the right, one accent colour for the thing
/// you are choosing between, and everything else quieter than it. Detail that
/// is only sometimes wanted goes in a tooltip rather than on the line.
/// </remarks>
internal static class Style
{
    /// <summary>The thing being chosen between. Used for nothing else.</summary>
    public static readonly Vector4 Accent = new(0.88f, 0.75f, 0.48f, 1f);

    /// <summary>Anything that supports the accent rather than competing with it.</summary>
    public static readonly Vector4 Muted = new(0.60f, 0.61f, 0.66f, 1f);

    public static readonly Vector4 Good = new(0.56f, 0.75f, 0.47f, 1f);

    public static readonly Vector4 Bad = new(0.84f, 0.41f, 0.33f, 1f);

    /// <summary>
    /// Softer corners and more air than the default. Pushed for the whole
    /// window rather than per control, so nothing is left looking sharper than
    /// what is next to it.
    /// </summary>
    public static IDisposable Window()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));

        return new Popped(8);
    }

    /// <summary>
    /// Undoing exactly what was pushed, whatever the drawing in between did.
    /// </summary>
    private sealed class Popped(int vars) : IDisposable
    {
        public void Dispose() => ImGui.PopStyleVar(vars);
    }

    /// <summary>What this part of the window is, said quietly and once.</summary>
    public static void Heading(string text)
    {
        Gap(2f);
        ImGui.TextColored(Muted, text.ToUpperInvariant());
    }

    public static void Muffled(string text) => ImGui.TextColored(Muted, text);

    /// <summary>
    /// Numbers flush to the right edge, on the line just drawn.
    /// </summary>
    /// <remarks>
    /// Left where they fell, counts sit at a different place on every row and
    /// cannot be compared without reading each one. Against the right edge they
    /// form a column, which is the whole reason to put them there.
    /// </remarks>
    public static void Trailing(string text) => Trailing(text, Muted);

    /// <summary>
    /// The same, in a colour that says something about the number: what is
    /// still to do reads differently from what is finished with, without
    /// anybody having to read the number to find out which it is.
    /// </summary>
    public static void Trailing(string text, Vector4 colour)
    {
        // Measured from the window's own right edge rather than from where the
        // last thing ended. A row picked out by a full width selectable ends at
        // that edge already, and stepping right from there has nowhere to go.
        var right = ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(text).X;

        ImGui.SameLine(right);
        ImGui.TextColored(colour, text);
    }

    /// <summary>
    /// Trailing numbers with a mark in front of them, for a state worth seeing
    /// before the number is read.
    /// </summary>
    public static void Trailing(FontAwesomeIcon mark, string text, Vector4 colour)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var marked = ImGui.CalcTextSize(mark.ToIconString()).X;
        ImGui.PopFont();

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var right = ImGui.GetWindowContentRegionMax().X
                    - (marked + gap + ImGui.CalcTextSize(text).X);

        ImGui.SameLine(right);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(colour, mark.ToIconString());
        ImGui.PopFont();

        ImGui.SameLine();
        ImGui.TextColored(colour, text);
    }

    /// <summary>Detail worth having but not worth a line of its own.</summary>
    public static void Explain(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    /// <summary>
    /// The name of somewhere to go, as the one thing on the row to look at.
    /// </summary>
    public static void Place(string text) => ImGui.TextColored(Accent, text);

    /// <summary>
    /// How hard something is, against the name it belongs to. Nothing is drawn
    /// when nobody recorded a level, which is better than a row reading "Lv0".
    /// </summary>
    /// <remarks>
    /// Counts go to the right edge because they are compared down the page. A
    /// level is not read that way: it answers whether this is worth walking
    /// into at all, which is a fact about the thing rather than about the row,
    /// so it sits against the name and closer to it than ordinary spacing.
    /// </remarks>
    public static void Level(LevelRange? level)
    {
        if (level is null)
            return;

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 0.75f);
        ImGui.TextColored(Muted, level.ToString());
    }

    /// <summary>
    /// A full width row picked by the name on it, with the level beside the
    /// name. Callers push their own id, since two mobs can share a name.
    /// </summary>
    /// <remarks>
    /// The row is drawn first and the name over it, the same way Pick works, so
    /// the whole width answers to the mouse and the level can still be a second
    /// piece of text in its own colour rather than part of the label.
    /// </remarks>
    public static bool Named(string name, LevelRange? level, bool current = false)
    {
        var start = ImGui.GetCursorPos();

        var picked = ImGui.Selectable("##named", current);
        var after = ImGui.GetCursorPos();

        ImGui.SetCursorPos(start);
        ImGui.TextUnformatted(name);
        Level(level);

        ImGui.SetCursorPos(after);

        return picked;
    }

    /// <summary>
    /// A full width row that can be picked, marked with what picking it does.
    /// </summary>
    /// <remarks>
    /// The label alone reads as a statement rather than as a control: text on a
    /// row looks like text on a row, and nothing about it says it can be
    /// pressed. A blade in front of it says both that it is live and what it is
    /// for, which no wording of the label can do without becoming a caption.
    ///
    /// The row is drawn first and the mark and label over it, so the whole
    /// width answers to the mouse rather than just the words.
    /// </remarks>
    public static bool Pick(
        string label,
        string? tip = null,
        FontAwesomeIcon mark = FontAwesomeIcon.Khanda,
        bool current = false,
        LevelRange? level = null)
    {
        var start = ImGui.GetCursorPos();

        var picked = ImGui.Selectable($"##{label}", current, ImGuiSelectableFlags.None);
        var hovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorPos();

        ImGui.SetCursorPos(start);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(hovered || current ? Accent : Muted, mark.ToIconString());
        ImGui.PopFont();

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        Level(level);

        ImGui.SetCursorPos(after);

        if (hovered && tip is not null)
            ImGui.SetTooltip(tip);

        return picked;
    }

    /// <summary>
    /// How wide a button that steers something is, at least. Longer labels
    /// grow past it; nothing sits narrower, so a row of them lines up.
    /// </summary>
    private const float ActionWidth = 120f;

    /// <summary>
    /// A button that steers something: start, stop, apply, go back.
    /// </summary>
    /// <remarks>
    /// Every one of them is the same height and no narrower than the next,
    /// because a row of buttons doing comparable jobs should not look like one
    /// important button and two afterthoughts. Drawn through here rather than
    /// by hand so that stays true without anybody remembering it.
    /// </remarks>
    public static bool Action(string label, string? tip = null)
    {
        var pressed = ImGui.Button(label, new Vector2(Wide(label), 0f));
        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    /// <summary>
    /// The one button on a screen worth pressing, coloured like it and sized
    /// like its neighbours.
    /// </summary>
    public static bool Primary(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Accent with { W = 0.85f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.12f, 0.10f, 0.08f, 1f));
        var pressed = ImGui.Button(label, new Vector2(Wide(label), 0f));
        ImGui.PopStyleColor(3);

        return pressed;
    }

    /// <summary>
    /// An action with a picture instead of a word, square and the same height
    /// as the words beside it.
    /// </summary>
    public static bool Action(FontAwesomeIcon icon, string? tip = null)
    {
        var glyph = icon.ToIconString();

        ImGui.PushFont(UiBuilder.IconFont);

        // Measured with the icon font in hand, since that is the font it will
        // be drawn in: a picture is wider than the letter it replaces, and a
        // square button sized from the text font clips it.
        var wide = ImGui.CalcTextSize(glyph).X + (ImGui.GetStyle().FramePadding.X * 2f);
        var pressed = ImGui.Button(
            glyph, new Vector2(Math.Max(ImGui.GetFrameHeight(), wide), ImGui.GetFrameHeight()));

        ImGui.PopFont();

        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    /// <summary>
    /// A button belonging to a row rather than to the screen: forget this
    /// entry, repeat this run, look at this on the map.
    /// </summary>
    /// <remarks>
    /// Deliberately the small kind. These sit inside a line of text or a table
    /// row, and drawn full height they would set the whole row's spacing and
    /// turn a list into a stack of controls.
    /// </remarks>
    public static bool Row(string label, string? tip = null)
    {
        var pressed = ImGui.SmallButton(label);
        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    private static float Wide(string label) =>
        Math.Max(ActionWidth, ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f) + 8f);

    public static void Gap(float y = 6f) => ImGui.Dummy(new Vector2(0f, y));

    /// <summary>
    /// How far along, as a bar rather than as two numbers with a slash. A run
    /// is watched more than it is read, and a bar is the one thing on the
    /// screen that can be understood without stopping to do arithmetic.
    /// </summary>
    public static void Progress(string label, int done, int target, string? reads = null)
    {
        var fraction = target > 0 ? Math.Clamp((float)done / target, 0f, 1f) : 0f;

        ImGui.TextUnformatted(label);
        Trailing(reads ?? $"{done} / {target}");

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, fraction >= 1f ? Good : Accent);
        ImGui.ProgressBar(fraction, new Vector2(-1f, ImGui.GetTextLineHeight() * 0.45f), string.Empty);
        ImGui.PopStyleColor();
    }
}
