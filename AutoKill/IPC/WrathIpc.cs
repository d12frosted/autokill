using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>Fighting, delegated to Wrath Combo.</summary>
/// <remarks>
/// Wrath is left alone if auto-rotation is already running. Somebody who plays
/// with it on has their own settings, and quietly reaching in to change them and
/// then switching it off at the end of a run is not a favour.
///
/// Every set call answers with a result rather than throwing, and a refusal is
/// ordinary: the lease can be invalid, blacklisted, or the player simply not
/// ready. Those are reported and worked around, not treated as failures of the
/// farming loop.
/// </remarks>
public sealed class WrathIpc(IDalamudPluginInterface plugin, IPluginLog log)
{
    // Wrath's SetResult. Everything from ten up is a refusal.
    private const int Okay = 0;
    private const int OkayWorking = 1;
    private const int InvalidLease = 11;

    // Wrath's AutoRotationConfigOption.
    private const int InCombatOnly = 0;
    private const int OnlyAttackInCombat = 13;

    private readonly ICallGateSubscriber<string, string, string?, Guid?> register =
        plugin.GetIpcSubscriber<string, string, string?, Guid?>("WrathCombo.RegisterForLeaseWithCallback");

    private readonly ICallGateSubscriber<bool> getAutoRotationState =
        plugin.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");

    private readonly ICallGateSubscriber<Guid, bool, int> setAutoRotationState =
        plugin.GetIpcSubscriber<Guid, bool, int>("WrathCombo.SetAutoRotationState");

    private readonly ICallGateSubscriber<bool> isCurrentJobReady =
        plugin.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady");

    private readonly ICallGateSubscriber<Guid, int> setCurrentJobReady =
        plugin.GetIpcSubscriber<Guid, int>("WrathCombo.SetCurrentJobAutoRotationReady");

    private readonly ICallGateSubscriber<Guid, int, object, int> setConfig =
        plugin.GetIpcSubscriber<Guid, int, object, int>("WrathCombo.SetAutoRotationConfigState");

    private readonly ICallGateSubscriber<Guid, object> releaseControl =
        plugin.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");

    private Guid? lease;
    private bool weTurnedItOn;

    /// <summary>True when something is going to be swinging, whoever arranged it.</summary>
    public bool Rotating => weTurnedItOn || AlreadyRunning;

    private bool AlreadyRunning => Call(() => getAutoRotationState.InvokeFunc(), false);

    /// <summary>
    /// Make sure a rotation is running, taking control only if one is not.
    /// </summary>
    public bool Start()
    {
        if (AlreadyRunning)
        {
            // Someone is already driving. Leave every setting exactly as found.
            return true;
        }

        if (lease is null)
        {
            // No callback prefix: the lease being revoked shows up as a refusal
            // on the next set call, which has to be handled anyway.
            lease = Call<Guid?>(() => register.InvokeFunc("AutoKill", "AutoKill", null), null);
            if (lease is null)
            {
                log.Warning("Wrath Combo would not grant a lease, so no rotation will run.");
                return false;
            }
        }

        if (!Succeeded(Call(() => setAutoRotationState.InvokeFunc(lease.Value, true), InvalidLease), "enable auto-rotation"))
            return false;

        weTurnedItOn = true;

        if (!Call(() => isCurrentJobReady.InvokeFunc(), true))
            Succeeded(Call(() => setCurrentJobReady.InvokeFunc(lease.Value), InvalidLease), "ready this job");

        // Both of these otherwise wait for a fight to have started already,
        // which is exactly what nothing else here is going to do.
        Configure(InCombatOnly, false);
        Configure(OnlyAttackInCombat, false);
        return true;
    }

    /// <summary>Put Wrath back exactly as it was found.</summary>
    public void Stop()
    {
        if (lease is not { } id)
            return;

        lease = null;

        if (weTurnedItOn)
        {
            Succeeded(Call(() => setAutoRotationState.InvokeFunc(id, false), InvalidLease), "disable auto-rotation");
            weTurnedItOn = false;
        }

        Call<object?>(() => { releaseControl.InvokeAction(id); return null; }, null);
    }

    private void Configure(int option, bool value) =>
        Succeeded(Call(() => setConfig.InvokeFunc(lease!.Value, option, value), InvalidLease), $"set option {option}");

    private bool Succeeded(int result, string what)
    {
        if (result is Okay or OkayWorking)
            return true;

        // A refused lease is spent. Dropping it means the next attempt registers
        // afresh rather than repeating a call that can no longer work.
        if (result == InvalidLease)
            lease = null;

        log.Warning($"Wrath Combo would not {what} (result {result}).");
        return false;
    }

    /// <summary>
    /// Wrath may be absent, disabled, or mid-reload, and any of those throw
    /// rather than answer. None of them are worth taking the farming loop down.
    /// </summary>
    private T Call<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            log.Warning($"Wrath Combo call failed: {ex.Message}");
            return fallback;
        }
    }
}
