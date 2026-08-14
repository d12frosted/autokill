namespace AutoKill.Core;

/// <summary>Facts about the published spawn data that are not worth trusting.</summary>
public static class SpawnPositions
{
    // Dead centre of any map, with an elevation of -1. Roughly a quarter of the
    // published spawn rows carry this exact triple, and 1196 mobs have nothing
    // else, so reading it as a position marks them farmable and then walks you
    // into the middle of the zone.
    private const float UnknownMapX = 21.48f;
    private const float UnknownMapY = 21.48f;
    private const float UnknownElevation = -1f;

    public static bool IsUnknown(float mapX, float mapY, float elevation) =>
        mapX == UnknownMapX && mapY == UnknownMapY && elevation == UnknownElevation;
}
