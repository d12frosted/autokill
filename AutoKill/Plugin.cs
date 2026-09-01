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
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IToastGui Toast { get; private set; } = null!;
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private readonly WindowSystem windows = new("AutoKill");
    private readonly MainWindow mainWindow;
    private readonly WrathIpc wrath;
    private readonly BossModIpc bossmod;
    private readonly Notifier notifier;
    private readonly FarmController farming;

    private readonly Configuration config;
    private readonly Observations observations;
    private readonly RunHistory history;

    private MobIndex? index;
    private HuntingLog? logbook;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        observations = new Observations(
            Path.Combine(PluginInterface.ConfigDirectory.FullName, "observations.json"), Log);

        history = new RunHistory(
            Path.Combine(PluginInterface.ConfigDirectory.FullName, "history.json"), Log);

        // Beside this plugin's own configuration file, where every plugin's is.
        var artisan = new ArtisanLists(
            Path.Combine(PluginInterface.ConfigFile.Directory!.FullName, "Artisan.json"), DataManager, Log,
            new ArtisanIpc(PluginInterface, Log));

        var hunts = new HuntBills(DataManager, Log);
        var fates = new Fates(FateTable, ClientState);

        var navmesh = new NavmeshIpc(PluginInterface, Log);
        notifier = new Notifier(ChatGui, Toast)
        {
            Enabled = config.Notifications,
            Chime = config.FinishSound,
        };
        wrath = new WrathIpc(PluginInterface, Log);
        bossmod = new BossModIpc(PluginInterface, Log);
        var lifestream = new LifestreamIpc(PluginInterface, Log);
        var requirements = new Requirements(PluginInterface, navmesh, wrath, bossmod, lifestream);
        var jobs = new Jobs(Objects, DataManager, Condition, config, Log);
        farming = new FarmController(
            Framework, navmesh, wrath, bossmod, lifestream, ClientState, Objects, Targets, DataManager, Condition, notifier,
            requirements,
            jobs, itemId => index?.ItemName(itemId) ?? $"item {itemId}", config, NewRecorder, observations, history, Log);

        // What past runs over a piece of ground came to, shared by the window
        // that plans one and the overlay that watches one.
        var past = new PastRuns(history);

        mainWindow = new MainWindow(
            () => index, farming, PlayerState, Textures, config, observations, history, artisan, hunts,
            () => logbook, fates, past, Save);
        windows.AddWindow(mainWindow);
        windows.AddWindow(new RunOverlay(
            () => index, farming, PlayerState, config, Textures, past, () => mainWindow.IsOpen = true));

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
                var built = MobIndex.Build(DataManager, Log);

                // The log needs somewhere to send a run, so it is built on top
                // of the index rather than beside it.
                logbook = new HuntingLog(DataManager, built, Log);
                index = built;
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
        observations.Save();
        wrath.Dispose();
        bossmod.Dispose();
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        windows.RemoveAllWindows();
    }

    /// <summary>
    /// A trace per run, named for when it started, next to the plugin's own
    /// configuration so it is easy to find and easy to delete.
    /// </summary>
    private RunRecorder? NewRecorder()
    {
        if (!config.RecordRuns)
            return null;

        var directory = Path.Combine(PluginInterface.ConfigDirectory.FullName, "traces");
        var name = $"run-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
        var recorder = new RunRecorder(directory, name, Log);
        if (recorder.Path is { } path)
            Log.Information($"Recording this run to {path}");
        return recorder;
    }

    private void Save()
    {
        PluginInterface.SavePluginConfig(config);
        notifier.Enabled = config.Notifications;
        notifier.Chime = config.FinishSound;
    }

    private void OpenMainUi() => mainWindow.IsOpen = true;

    private void OnCommand(string command, string args) => mainWindow.Toggle();
}
