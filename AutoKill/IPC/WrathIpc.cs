using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>Fighting, delegated to Wrath Combo.</summary>
/// <remarks>
/// Wrath hands out a lease and can revoke it at any time, so the lease is the
/// thing to hold onto and the callback is how we learn it is gone. Losing the
/// lease silently would leave the loop walking up to mobs and never attacking.
///
/// Wrath's own enums are not shared across assemblies, but Dalamud's call gates
/// convert return values when the declared type differs, so the integer values
/// below travel correctly.
/// </remarks>
public sealed class WrathIpc : IDisposable
{
    private const string CallbackPrefix = "AutoKillWrathCallback";

    // Values of Wrath's AutoRotationConfigOption.
    private const int InCombatOnly = 0;
    private const int OnlyAttackInCombat = 13;

    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, string, string, Guid?> registerWithCallback;
    private readonly ICallGateSubscriber<Guid, bool, int> setAutoRotationState;
    private readonly ICallGateSubscriber<Guid, int> setCurrentJobReady;
    private readonly ICallGateSubscriber<Guid, int, object, int> setConfigState;
    private readonly ICallGateSubscriber<Guid, object> releaseControl;
    private readonly ICallGateProvider<int, string, object> callback;

    private Guid? lease;

    public WrathIpc(IDalamudPluginInterface plugin, IPluginLog log)
    {
        this.log = log;

        registerWithCallback = plugin.GetIpcSubscriber<string, string, string, Guid?>(
            "WrathCombo.RegisterForLeaseWithCallback");
        setAutoRotationState = plugin.GetIpcSubscriber<Guid, bool, int>("WrathCombo.SetAutoRotationState");
        setCurrentJobReady = plugin.GetIpcSubscriber<Guid, int>("WrathCombo.SetCurrentJobAutoRotationReady");
        setConfigState = plugin.GetIpcSubscriber<Guid, int, object, int>("WrathCombo.SetAutoRotationConfigState");
        releaseControl = plugin.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");

        callback = plugin.GetIpcProvider<int, string, object>($"{CallbackPrefix}.WrathComboCallback");
        callback.RegisterFunc(OnLeaseCancelled);
    }

    public bool Leased => lease.HasValue;

    /// <summary>Take control of the rotation and start swinging.</summary>
    public bool Start()
    {
        if (lease.HasValue)
            return true;

        try
        {
            lease = registerWithCallback.InvokeFunc("AutoKill", "AutoKill", CallbackPrefix);
        }
        catch (Exception ex)
        {
            log.Warning($"Wrath Combo is not answering, no rotation will run: {ex.Message}");
            return false;
        }

        if (lease is not { } id)
        {
            log.Warning("Wrath Combo refused a lease, no rotation will run.");
            return false;
        }

        try
        {
            setCurrentJobReady.InvokeFunc(id);
            // Both of these otherwise wait for something else to start the
            // fight, which is exactly what nothing else is going to do here.
            setConfigState.InvokeFunc(id, InCombatOnly, false);
            setConfigState.InvokeFunc(id, OnlyAttackInCombat, false);
            setAutoRotationState.InvokeFunc(id, true);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Could not configure Wrath Combo: {ex.Message}");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        if (lease is not { } id)
            return;

        lease = null;
        try
        {
            setAutoRotationState.InvokeFunc(id, false);
            releaseControl.InvokeAction(id);
        }
        catch (Exception ex)
        {
            log.Warning($"Could not hand the Wrath Combo lease back: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        callback.UnregisterFunc();
    }

    private object OnLeaseCancelled(int reason, string message)
    {
        log.Warning($"Wrath Combo took its lease back (reason {reason}): {message}");
        lease = null;
        return null!;
    }
}
