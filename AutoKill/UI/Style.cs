using System.Numerics;
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
    public static void Trailing(string text)
    {
        // Measured from the window's own right edge rather than from where the
        // last thing ended. A row picked out by a full width selectable ends at
        // that edge already, and stepping right from there has nowhere to go.
        var right = ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(text).X;

        ImGui.SameLine(right);
        ImGui.TextColored(Muted, text);
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
    public static bool Pick(string label, string? tip = null, FontAwesomeIcon mark = FontAwesomeIcon.Khanda)
    {
        var start = ImGui.GetCursorPos();

        var picked = ImGui.Selectable($"##{label}", false, ImGuiSelectableFlags.None);
        var hovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorPos();

        ImGui.SetCursorPos(start);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(hovered ? Accent : Muted, mark.ToIconString());
        ImGui.PopFont();

        ImGui.SameLine();
        ImGui.TextUnformatted(label);

        ImGui.SetCursorPos(after);

        if (hovered && tip is not null)
            ImGui.SetTooltip(tip);

        return picked;
    }

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
