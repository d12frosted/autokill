namespace AutoKill.Core;

/// <summary>
/// Converts between map coordinates and world coordinates.
/// </summary>
/// <remarks>
/// Spawn data is published as map coordinates, the numbers the game shows in
/// the coordinate readout. Navigation needs world coordinates. The projection
/// comes from the Map sheet's size factor and per-axis offset:
///
///     map = 41/c * (((world + offset) * c + 1024) / 2048) + 1,  c = sizeFactor / 100
///
/// Height is not converted here. Map elevation is recorded inconsistently and
/// is not worth trusting, so callers snap the resulting point onto the navmesh
/// floor instead.
/// </remarks>
public static class MapCoordinates
{
    private const double Tile = 2048.0;
    private const double HalfTile = 1024.0;
    private const double MapSpan = 41.0;

    public static double ToWorld(float value, ushort sizeFactor, short offset)
    {
        var c = sizeFactor / 100.0;
        var scaled = Tile * (value - 1.0) * c / MapSpan - HalfTile;
        return scaled / c - offset;
    }

    public static double ToMap(double value, ushort sizeFactor, short offset)
    {
        var c = sizeFactor / 100.0;
        var scaled = (value + offset) * c;
        return MapSpan / c * ((scaled + HalfTile) / Tile) + 1.0;
    }
}
