using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace AutoKill.UI;

/// <summary>Game icons drawn inline, at the size of the text beside them.</summary>
internal static class Icons
{
    /// <summary>
    /// Draw an item's icon inline, leaving the cursor where the text should go.
    /// Falls through silently when there is no icon, since a missing picture is
    /// not worth a gap in the row.
    /// </summary>
    public static void Draw(ITextureProvider textures, ushort icon)
    {
        if (icon == 0)
            return;

        if (textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault() is not { } texture)
            return;

        var size = ImGui.GetTextLineHeight() * 1.4f;
        ImGui.Image(texture.Handle, new Vector2(size, size));
        ImGui.SameLine();
    }
}
