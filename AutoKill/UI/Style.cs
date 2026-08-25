using System.Numerics;
using AutoKill.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace AutoKill.UI;

/// <summary>
/// The window's visual language, in one place. The full account of it - what each
/// tone means, how a row is composed, which words get which case - lives in
/// docs/style.md; this file is where the guide is enforced.
/// </summary>
/// <remarks>
/// A ledger is read in one pass or it is not read at all. So: names on the left,
/// states and numbers flush right where they form a column, one accent for what still
/// wants doing, and everything finished quieter than everything open. Detail that is
/// only sometimes wanted lives in a tooltip rather than on the line, and a bar stands
/// in for any pair of numbers somebody would otherwise have to divide.
///
/// The window wears its own shell (<see cref="Shell"/>) rather than the user's
/// Dalamud theme: a ledger that changes clothes with every install has no signature,
/// and half this palette quietly assumes dark paper anyway.
/// </remarks>
internal static class Style
{
    /// <summary>What still wants doing. Used for nothing else.</summary>
    public static readonly Vector4 Accent = new(0.88f, 0.75f, 0.48f, 1f);

    /// <summary>
    /// The accent worn thin: the masthead's feather and rule. Brand, not state -
    /// the same gold so the window rhymes, faded so it never competes with the
    /// accent's one meaning.
    /// </summary>
    public static readonly Vector4 Brand = new(0.88f, 0.75f, 0.48f, 0.45f);

    /// <summary>Anything that supports the accent rather than competing with it.</summary>
    public static readonly Vector4 Muted = new(0.60f, 0.61f, 0.66f, 1f);

    public static readonly Vector4 Good = new(0.56f, 0.75f, 0.47f, 1f);

    public static readonly Vector4 Warn = new(0.85f, 0.78f, 0.35f, 1f);

    public static readonly Vector4 Bad = new(0.84f, 0.41f, 0.33f, 1f);

    public static readonly Vector4 Plain = new(0.92f, 0.92f, 0.94f, 1f);

    /// <summary>The paper everything is written on: warm near-black, faintly translucent.</summary>
    public static readonly Vector4 Paper = new(0.10f, 0.095f, 0.088f, 0.97f);

    /// <summary>
    /// The faintest wash of light over the paper: empty cells, idle chrome. One
    /// tone, so every quiet surface in the window is quiet in the same way.
    /// </summary>
    public static readonly Vector4 Veil = new(1f, 1f, 1f, 0.05f);

    /// <summary>A rule or an edge: barely more present than the veil.</summary>
    public static readonly Vector4 Rule = new(1f, 1f, 1f, 0.08f);

    /// <summary>
    /// The steel the shell is trimmed in: title bar and the active tab. Tataru
    /// wears bronze; this plugin's business is the blade, so its metal is colder,
    /// kept in the same quiet register.
    /// </summary>
    private static readonly Vector4 Trim = new(0.20f, 0.24f, 0.29f, 1f);

    /// <summary>Trim on a window that does not have the focus.</summary>
    private static readonly Vector4 TrimIdle = new(0.13f, 0.15f, 0.18f, 1f);

    /// <summary>A length in design pixels, at whatever scale the user runs Dalamud.</summary>
    public static float Px(float logical) => logical * ImGuiHelpers.GlobalScale;

    /// <summary>
    /// The whole shell: paper, trim, chrome and spacing, pushed around the window
    /// rather than inside it so the frame itself is ours. One signature on every
    /// install, whatever theme the rest of Dalamud wears.
    /// </summary>
    public static IDisposable Shell()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Paper);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Paper with { W = 0.99f });
        ImGui.PushStyleColor(ImGuiCol.Border, Rule);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, TrimIdle);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Trim);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, TrimIdle);
        ImGui.PushStyleColor(ImGuiCol.Text, Plain);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, Muted);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Veil);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Veil with { W = 0.10f });
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Veil with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.Tab, Veil with { W = 0.04f });
        ImGui.PushStyleColor(ImGuiCol.TabHovered, Veil with { W = 0.10f });
        ImGui.PushStyleColor(ImGuiCol.TabActive, Trim);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, Veil with { W = 0.04f });
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, TrimIdle);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent);
        ImGui.PushStyleColor(ImGuiCol.Button, Veil with { W = 0.07f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Veil with { W = 0.12f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Veil with { W = 0.18f });
        ImGui.PushStyleColor(ImGuiCol.Header, Veil);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Veil with { W = 0.10f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Veil with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Veil with { W = 0.12f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Veil with { W = 0.20f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Veil with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.Separator, Rule);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Veil);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Veil with { W = 0.15f });
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Veil with { W = 0.25f });

        return new Popped(vars: 9, colours: 31);
    }

    private sealed class Popped(int vars, int colours) : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopStyleColor(colours);
            ImGui.PopStyleVar(vars);
        }
    }

    /// <summary>
    /// The signature strip a window opens with: the feather, the name, whatever
    /// context the window carries against the right edge - and the brand as a rule
    /// beneath, so every tab hangs from the same anchor.
    /// </summary>
    public static void Masthead(string name, string context)
    {
        Mark(FontAwesomeIcon.Feather, Brand);
        ImGui.SameLine();
        ImGui.TextColored(Plain, name);

        if (context.Length > 0)
            Trailing(context, Muted);

        ImGui.PushStyleColor(ImGuiCol.Separator, Brand);
        ImGui.Separator();
        ImGui.PopStyleColor();
    }

    /// <summary>What this part of the window is, said quietly and once.</summary>
    public static void Heading(string text)
    {
        Gap(2f);
        ImGui.TextColored(Muted, text.ToUpperInvariant());
    }

    /// <summary>A heading with its own number or clock against the right edge.</summary>
    public static void Heading(string text, string trailing)
    {
        Heading(text);
        Trailing(trailing);
    }

    /// <summary>A row's own words, in the plain ink.</summary>
    public static void Line(string text) => ImGui.TextColored(Plain, text);

    /// <summary>
    /// A kind's mark and then its name: how a row on a list opens, so the eye can
    /// sort the list by shape before reading a word.
    /// </summary>
    public static void Lead(FontAwesomeIcon mark, string name, Vector4? ink = null)
    {
        Mark(mark, Muted);
        ImGui.SameLine();
        ImGui.TextColored(ink ?? Plain, name);
    }

    public static void Muffled(string text) => ImGui.TextColored(Muted, text);

    /// <summary>Quiet text that folds rather than running off its column.</summary>
    public static void MuffledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A warning that folds rather than running off its column.</summary>
    public static void WarnWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Warn);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>Nothing to list: the fact said quietly, with air around it.</summary>
    public static void Nothing(string text)
    {
        Gap();
        Muffled(text);
        Gap();
    }

    /// <summary>
    /// The small x that forgets a line, against the row's right edge: content on the
    /// left, the destructive thing as far from it as the row allows. Nearly invisible
    /// until the mouse comes near - several rows in a column each carry one, and a
    /// stack of bright x's reads as a feature rather than as the exits they are.
    /// </summary>
    public static bool TrailingRemove(string tip)
    {
        var width = ImGui.CalcTextSize("x").X + (ImGui.GetStyle().FramePadding.X * 2f);

        ImGui.SameLine();
        var slack = ImGui.GetContentRegionAvail().X - width;
        if (slack > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + slack);

        ImGui.PushStyleColor(ImGuiCol.Text, Muted with { W = 0.35f });
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        var pressed = ImGui.SmallButton("x");
        ImGui.PopStyleColor(2);

        Explain(tip);
        return pressed;
    }

    /// <summary>
    /// Numbers flush to the right edge, on the line just drawn. Left where they fell,
    /// counts sit at a different place on every row; against the edge they form a
    /// column, which is the whole reason to put them there.
    /// </summary>
    public static void Trailing(string text) => Trailing(text, Muted);

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
    /// Trailing text with a mark in front of it, for a state worth seeing before the
    /// words are read.
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

    /// <summary>A mark in the icon font, inline, in a colour.</summary>
    public static void Mark(FontAwesomeIcon icon, Vector4 colour)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(colour, icon.ToIconString());
        ImGui.PopFont();
    }

    /// <summary>Detail worth having but not worth a line of its own.</summary>
    public static void Explain(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    /// <summary>
    /// A button belonging to a row rather than to the screen: forget this entry, try
    /// this on, stop chasing this. Deliberately the small kind, so a list stays a
    /// list rather than a stack of controls.
    /// </summary>
    public static bool Row(string label, string? tip = null)
    {
        var pressed = ImGui.SmallButton(label);
        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    /// <summary>
    /// The button that commits: starts a run, farms a whole list, applies new
    /// targets. Full height so it stands level with the inputs beside it, the accent
    /// for its word because it is the one control on a form that moves the ledger.
    /// There is exactly one way to say "do it" in this window, and this is it.
    /// </summary>
    public static bool Commit(string label, string? tip = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        var pressed = ImGui.Button(label);
        ImGui.PopStyleColor();

        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    /// <summary>
    /// A button that reads as part of the text until the mouse comes near: muted, no
    /// box, a faint hover to say it answers. For the actions a row carries every day
    /// - repeated down a ledger, chips become a wall of chrome, and the information
    /// is what the eye should land on. Anything loud or rare stays a real button.
    /// </summary>
    public static bool Quiet(string label, string? tip = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rule);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Veil with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        var pressed = ImGui.SmallButton(label);
        ImGui.PopStyleColor(4);

        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    /// <summary>The quiet tier with a picture for a label, for the row that has no room for a word.</summary>
    public static bool Quiet(FontAwesomeIcon icon, string? tip = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rule);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Veil with { W = 0.14f });
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);

        ImGui.PushFont(UiBuilder.IconFont);
        var pressed = ImGui.SmallButton(icon.ToIconString());
        ImGui.PopFont();

        ImGui.PopStyleColor(4);

        if (tip is not null)
            Explain(tip);

        return pressed;
    }

    public static void Gap(float y = 6f) => ImGui.Dummy(new Vector2(0f, y));

    /// <summary>
    /// How far along, as a bar rather than as two numbers with a slash. Full turns
    /// quiet green: a finished goal is a fact, not a call to action. Full width
    /// unless a width is asked for, because most bars belong to their column.
    /// </summary>
    public static void Progress(int done, int target, Vector4? full = null, float width = -1f)
    {
        var fraction = target > 0 ? Math.Clamp((float)done / target, 0f, 1f) : 0f;

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, fraction >= 1f ? full ?? Good : Accent);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Veil);
        ImGui.ProgressBar(fraction, new Vector2(width, ImGui.GetTextLineHeight() * 0.45f), string.Empty);
        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// A goal being watched: its name, its numbers against the right edge, and the
    /// bar that saves anybody dividing them.
    /// </summary>
    public static void Progress(string label, int done, int target, string? reads = null)
    {
        Line(label);
        Trailing(reads ?? $"{done} / {target}");
        Progress(done, target);
    }

    /// <summary>
    /// The name of somewhere still to go, in the accent: on a browse screen the
    /// choice is the thing that wants doing, and these are the choices.
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
        Line(name);
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

        Mark(mark, hovered || current ? Accent : Muted);

        ImGui.SameLine();
        Line(label);
        Level(level);

        ImGui.SetCursorPos(after);

        if (hovered && tip is not null)
            ImGui.SetTooltip(tip);

        return picked;
    }
}
