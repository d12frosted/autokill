using AutoKill.Data;
using AutoKill.Farming;
using AutoKill.IPC;
using AutoKill.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AutoKill;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/autokill";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IToastGui Toast { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private readonly WindowSystem windows = new("AutoKill");
    private readonly MainWindow mainWindow;
    private readonly WrathIpc wrath;
    private readonly Notifier notifier;
    private readonly FarmController farming;

    private readonly Configuration config;

    private MobIndex? index;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var navmesh = new NavmeshIpc(PluginInterface);
        notifier = new Notifier(ChatGui, Toast) { Enabled = config.Notifications };
        wrath = new WrathIpc(PluginInterface, Log);
        farming = new FarmController(
            Framework, navmesh, wrath, ClientState, Objects, Targets, DataManager, Condition, notifier,
            itemId => index?.ItemName(itemId) ?? $"item {itemId}", config, Log);

        mainWindow = new MainWindow(() => index, farming, Textures, config, Save);
        windows.AddWindow(mainWindow);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AutoKill. Search for a mob, or for something you want it to drop.",
        });

        // Joining tens of thousands of spawn rows has no business blocking the
        // framework thread while the game is still loading in.
        Task.Run(() =>
        {
            try
            {
                index = MobIndex.Build(DataManager, Log);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to build the mob index.");
            }
        });
    }

    public void Dispose()
    {
        farming.Dispose();
        wrath.Stop();
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        windows.RemoveAllWindows();
    }

    private void Save()
    {
        PluginInterface.SavePluginConfig(config);
        notifier.Enabled = config.Notifications;
    }

    private void OpenMainUi() => mainWindow.IsOpen = true;

    private void OnCommand(string command, string args) => mainWindow.Toggle();
}
