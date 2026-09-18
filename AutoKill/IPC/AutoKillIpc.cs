using AutoKill.Data;
using AutoKill.Farming;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>
/// The door other plugins knock on: farm a thing until the bags hold so many.
/// </summary>
/// <remarks>
/// Everything this offers is what the window's By drop tab does with a click, and
/// nothing more: the caller names an item and a count, AutoKill picks the field the
/// tab would list first and runs the same leg the crafting lists run, with the same
/// stops - the count reached, a death, full bags. The caller reads whether a run is
/// on and can stop it, which is all a neighbour needs to hand a material over and
/// take it back.
///
/// Gates:
/// - <c>AutoKill.IsRunning</c> () -> bool
/// - <c>AutoKill.CanFarm</c> (uint itemId) -> bool: the index knows a farmable field
///   for it and nothing required is missing
/// - <c>AutoKill.FarmItem</c> (uint itemId, int count) -> bool: started
/// - <c>AutoKill.Status</c> () -> string: the run's own words, empty when idle
/// - <c>AutoKill.Stop</c> () -> void
/// </remarks>
public sealed class AutoKillIpc : IDisposable
{
    private readonly ICallGateProvider<bool> isRunning;
    private readonly ICallGateProvider<uint, bool> canFarm;
    private readonly ICallGateProvider<uint, int, bool> farmItem;
    private readonly ICallGateProvider<string> status;
    private readonly ICallGateProvider<object> stop;

    public AutoKillIpc(IDalamudPluginInterface plugin, Func<MobIndex?> index, FarmController farming, IPluginLog log)
    {
        isRunning = plugin.GetIpcProvider<bool>("AutoKill.IsRunning");
        canFarm = plugin.GetIpcProvider<uint, bool>("AutoKill.CanFarm");
        farmItem = plugin.GetIpcProvider<uint, int, bool>("AutoKill.FarmItem");
        status = plugin.GetIpcProvider<string>("AutoKill.Status");
        stop = plugin.GetIpcProvider<object>("AutoKill.Stop");

        isRunning.RegisterFunc(() => farming.Running);
        status.RegisterFunc(() => farming.Current is { Phase: not FarmPhase.Finished } run ? run.Status : "");
        canFarm.RegisterFunc(itemId =>
            farming.Blocker is null && index()?.FieldsDropping(itemId) is { Count: > 0 });

        farmItem.RegisterFunc((itemId, count) =>
        {
            if (farming.Running || count <= 0 || index()?.FieldsDropping(itemId) is not { Count: > 0 } fields)
                return false;

            log.Information($"Asked over IPC to farm {count}x item {itemId}; going to {fields[0].Area.ZoneName}.");
            farming.StartMany([FarmController.Gathering(fields[0], itemId, count)]);
            return farming.Running;
        });

        stop.RegisterAction(() =>
        {
            if (farming.Running)
                farming.Stop("stopped over IPC");
        });
    }

    public void Dispose()
    {
        isRunning.UnregisterFunc();
        canFarm.UnregisterFunc();
        farmItem.UnregisterFunc();
        status.UnregisterFunc();
        stop.UnregisterAction();
    }
}
