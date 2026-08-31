using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>The aethernet, delegated to Lifestream.</summary>
/// <remarks>
/// Teleporting between aetherytes is a single game call this plugin makes
/// itself. The aethernet is not: it is targeting the crystal, interacting with
/// it, and picking a line out of the menu that opens, which is a good deal of
/// interface driving for the one zone that needs it. Lifestream already does
/// exactly that and Questionable already relies on it for the same hop, so the
/// work is borrowed rather than repeated.
///
/// Guarded the same way vnavmesh is. Lifestream is optional: without it the
/// only thing lost is the Dravanian Hinterlands, and a missing plugin should
/// read as that rather than as an exception out of the farming loop.
/// </remarks>
public sealed class LifestreamIpc(IDalamudPluginInterface plugin, IPluginLog log)
{
    private readonly ICallGateSubscriber<bool> isBusy =
        plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy");

    private readonly ICallGateSubscriber<uint, bool> aethernetTeleportById =
        plugin.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");

    /// <summary>Whether Lifestream answers at all.</summary>
    public bool Responding
    {
        get
        {
            try
            {
                isBusy.InvokeFunc();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Whether it is already in the middle of something of its own.</summary>
    public bool Busy => Try(() => isBusy.InvokeFunc(), false);

    /// <summary>
    /// Ask for an aethernet hop to a shard, by its row in the Aetheryte sheet.
    /// The character has to be standing in range of a crystal for it to be
    /// accepted, which after a teleport it is.
    /// </summary>
    /// <returns>Whether the ask was taken, which is not whether it arrives.</returns>
    public bool AethernetTeleport(uint shardId) =>
        Try(() => aethernetTeleportById.InvokeFunc(shardId), false);

    private T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            log.Warning($"Lifestream call failed: {ex.Message}");
            return fallback;
        }
    }
}
