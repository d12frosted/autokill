using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;

namespace AutoKill.Farming;

/// <summary>
/// Which FATEs are running, for the hunt targets that only exist inside one.
/// </summary>
/// <remarks>
/// The table only carries FATEs in the zone the character is standing in, so
/// "not running" and "cannot tell from here" are different answers and are worth
/// keeping apart. Telling someone a FATE is not up when nothing was ever asked
/// is how a plugin loses trust.
/// </remarks>
public sealed class Fates(IFateTable fates, IClientState state)
{
    public bool InZone(uint territoryId) => state.TerritoryType == territoryId;

    public IFate? Running(ushort fateId) =>
        fateId == 0 ? null : fates.FirstOrDefault(fate => fate.FateId == fateId);
}
