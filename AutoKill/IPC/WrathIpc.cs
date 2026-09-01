using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>Fighting, delegated to Wrath Combo.</summary>
/// <remarks>
/// A lease is taken for every run, whether or not a rotation was already
/// running. A lease is Wrath's own mechanism for lending a plugin control:
/// everything set under one is put back when the lease ends, so nothing here is
/// a lasting change to somebody's configuration. That is what makes it fair to
/// set up a job that was never configured for auto-rotation, and to pin the
/// target mode for the length of a run.
///
/// Auto-rotation is only switched off at the end if this plugin was the one that
/// switched it on. Handing somebody's own rotation back off is the one thing a
/// lease does not cover.
///
/// Every set call answers with a result rather than throwing, and a refusal is
/// ordinary: the lease can be invalid, blacklisted, or the player simply not
/// ready. Those are reported and worked around, not treated as failures of the
/// farming loop.
/// </remarks>
public sealed class WrathIpc : IDisposable
{
    // Wrath's SetResult. Everything from ten up is a refusal.
    private const int Okay = 0;
    private const int OkayWorking = 1;
    private const int InvalidLease = 11;

    // Wrath's AutoRotationConfigOption. These are assigned numbers rather than
    // positions, so they stay put as the list grows.
    private const int InCombatOnly = 0;
    private const int DpsRotationMode = 1;
    private const int OnlyAttackInCombat = 13;
    private const int DpsAlwaysHardTarget = 19;
    private const int HealerAlwaysHardTarget = 20;

    // Wrath's DPSRotationMode. Manual is the one that picks nothing of its own.
    private const int Manual = 0;

    // Wrath's CancellationReason.
    private const int UserRevokedIt = 0;

    /// <summary>
    /// How often it is worth asking again after Wrath says no. Fighting is
    /// checked on every tick, and hammering a refusal sixty times a second helps
    /// nobody.
    /// </summary>
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Long enough to keep per-frame reads off the wire, short enough that the
    /// answer is still about now.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMilliseconds(250);

    private readonly IPluginLog log;

    private readonly ICallGateSubscriber<string, string, string?, Guid?> register;
    private readonly ICallGateSubscriber<bool> getAutoRotationState;
    private readonly ICallGateSubscriber<Guid, bool, int> setAutoRotationState;
    private readonly ICallGateSubscriber<bool> isCurrentJobReady;
    private readonly ICallGateSubscriber<Guid, int> setCurrentJobReady;
    private readonly ICallGateSubscriber<Guid, int, object, int> setConfig;
    private readonly ICallGateSubscriber<Guid, object> releaseControl;
    private readonly ICallGateProvider<int, string, object> cancelled;

    private Guid? lease;
    private bool weTurnedItOn;
    private bool configured;
    private bool surrendered;
    private DateTime lastAttempt = DateTime.MinValue;

    private bool running;
    private DateTime runningAsOf = DateTime.MinValue;
    private bool jobReady;
    private DateTime jobReadyAsOf = DateTime.MinValue;

    public WrathIpc(IDalamudPluginInterface plugin, IPluginLog log)
    {
        this.log = log;

        register = plugin.GetIpcSubscriber<string, string, string?, Guid?>(
            "WrathCombo.RegisterForLeaseWithCallback");
        getAutoRotationState = plugin.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");
        setAutoRotationState = plugin.GetIpcSubscriber<Guid, bool, int>("WrathCombo.SetAutoRotationState");
        isCurrentJobReady = plugin.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady");
        setCurrentJobReady = plugin.GetIpcSubscriber<Guid, int>("WrathCombo.SetCurrentJobAutoRotationReady");
        setConfig = plugin.GetIpcSubscriber<Guid, int, object, int>("WrathCombo.SetAutoRotationConfigState");
        releaseControl = plugin.GetIpcSubscriber<Guid, object>("WrathCombo.ReleaseControl");

        // Wrath calls this when a lease ends: the player revoked it, the job
        // changed, or Wrath itself is going away. Without it the lease dies in
        // silence and the character stands in a field swinging at nothing.
        cancelled = plugin.GetIpcProvider<int, string, object>("AutoKill.WrathComboCallback");
        cancelled.RegisterAction(LeaseCancelled);
    }

    /// <summary>True when something is going to be swinging, whoever arranged it.</summary>
    /// <remarks>
    /// Asked of Wrath rather than remembered. A lease can end without warning,
    /// and believing our own record of having switched it on is how a run spends
    /// twenty minutes watching a mob that nothing is hitting.
    /// </remarks>
    public bool Rotating
    {
        get
        {
            if (DateTime.UtcNow - runningAsOf < CacheFor)
                return running;

            runningAsOf = DateTime.UtcNow;
            return running = Call(() => getAutoRotationState.InvokeFunc(), false);
        }
    }

    /// <summary>
    /// Whether this job has anything enabled to actually rotate with. Auto
    /// rotation being on says nothing about that, and a job with nothing in
    /// auto-mode fights exactly as well as no rotation plugin at all.
    /// </summary>
    public bool JobReady
    {
        get
        {
            if (DateTime.UtcNow - jobReadyAsOf < CacheFor)
                return jobReady;

            jobReadyAsOf = DateTime.UtcNow;
            return jobReady = Call(() => isCurrentJobReady.InvokeFunc(), true);
        }
    }

    /// <summary>
    /// Make sure a rotation is running and pointed at what the run targeted.
    /// </summary>
    /// <remarks>
    /// Called on every tick of a fight, so the ordinary answer is the first one:
    /// a lease in hand, settings applied, something swinging.
    /// </remarks>
    public bool Start()
    {
        // The player took control back by hand. Asking again every few seconds
        // would be arguing with them, so whatever they are running is what the
        // run gets.
        if (surrendered)
            return Rotating;

        if (lease is not null && configured && Rotating)
            return true;

        if (DateTime.UtcNow - lastAttempt < RetryEvery)
            return Rotating;

        lastAttempt = DateTime.UtcNow;

        if (Lease() is null)
            return Rotating;

        // Only when it was off, because the flag this sets is what decides
        // whether the end of the run switches it back off, and switching off
        // somebody else's rotation is the one thing the lease does not undo.
        if (!Rotating)
        {
            if (!Set(id => setAutoRotationState.InvokeFunc(id, true), "enable auto-rotation"))
                return false;

            weTurnedItOn = true;
            running = true;
            runningAsOf = DateTime.UtcNow;
        }

        // Sets up a job that was never configured for auto-rotation, keeping
        // whatever the player already chose where they chose anything. It is
        // done under the lease, so it is undone when the lease ends, and it
        // takes a few seconds to come into effect.
        if (!JobReady)
        {
            Set(id => setCurrentJobReady.InvokeFunc(id), "set this job up");
            jobReadyAsOf = DateTime.MinValue;
        }

        // The first two otherwise wait for a fight to have started already,
        // which is exactly what nothing else here is going to do.
        Configure(InCombatOnly, false);
        Configure(OnlyAttackInCombat, false);

        // The rest keep Wrath on the mob this plugin picked. Manual is the only
        // mode that never chooses a target of its own, and the hard target
        // override is not enough on its own: it applies only while there is a
        // hard target in range, and between one kill and reaching the next one
        // there is not. Every other mode fills that gap with whatever stands
        // nearby, and Nearest, the usual choice, prefers exactly the mobs that
        // are not already fighting you.
        Configure(DpsRotationMode, Manual);
        Configure(DpsAlwaysHardTarget, true);
        Configure(HealerAlwaysHardTarget, true);

        configured = true;
        return true;
    }

    /// <summary>Put Wrath back exactly as it was found.</summary>
    public void Stop()
    {
        surrendered = false;
        configured = false;
        lastAttempt = DateTime.MinValue;

        if (lease is not { } id)
            return;

        lease = null;

        if (weTurnedItOn)
        {
            Set(_ => setAutoRotationState.InvokeFunc(id, false), "disable auto-rotation", retry: false);
            weTurnedItOn = false;
            running = false;
            runningAsOf = DateTime.UtcNow;
        }

        Call<object?>(() => { releaseControl.InvokeAction(id); return null; }, null);
    }

    public void Dispose()
    {
        Stop();
        cancelled.UnregisterAction();
    }

    /// <summary>
    /// Wrath telling us the lease is over. Which reason it gives decides whether
    /// trying again is repair or nagging.
    /// </summary>
    private void LeaseCancelled(int reason, string detail)
    {
        lease = null;
        weTurnedItOn = false;
        configured = false;
        runningAsOf = DateTime.MinValue;
        jobReadyAsOf = DateTime.MinValue;

        // Every other reason, a job change most of all, is worth recovering
        // from: the next tick registers again and sets the new job up.
        surrendered = reason == UserRevokedIt;

        log.Information(
            surrendered
                ? "Wrath Combo control was taken back by hand, so the fighting is yours from here."
                : $"Wrath Combo ended the lease (reason {reason}), so it will be taken again. {detail}");
    }

    /// <summary>
    /// The lease held, taking one if there is none, and null if Wrath would not
    /// grant one. Handed back rather than read off the field afterwards: the
    /// callback that ends a lease can clear the field at any point, including
    /// while asking for this one.
    /// </summary>
    private Guid? Lease()
    {
        if (lease is { } held)
            return held;

        // The prefix names the IPC Wrath calls when the lease ends.
        lease = Call<Guid?>(() => register.InvokeFunc("AutoKill", "AutoKill", "AutoKill"), null);
        if (lease is { } granted)
            return granted;

        log.Warning("Wrath Combo would not grant a lease, so no rotation will run.");
        return null;
    }

    private void Configure(int option, object value) =>
        Set(id => setConfig.InvokeFunc(id, option, value), $"set option {option}");

    /// <summary>
    /// Run something that needs a lease, once more with a fresh lease if the one
    /// held turned out to be spent.
    /// </summary>
    private bool Set(Func<Guid, int> call, string what, bool retry = true)
    {
        if (lease is not { } id)
            return false;

        var result = Call(() => call(id), InvalidLease);
        if (result is Okay or OkayWorking)
            return true;

        if (result != InvalidLease)
        {
            log.Warning($"Wrath Combo would not {what} (result {result}).");
            return false;
        }

        // A refused lease is spent, so the one thing worth doing is asking for
        // another and trying once. Twice would be a loop.
        lease = null;
        if (!retry || Lease() is not { } fresh)
            return false;

        var second = Call(() => call(fresh), InvalidLease);
        if (second is Okay or OkayWorking)
            return true;

        log.Warning($"Wrath Combo would not {what}, even with a new lease (result {second}).");
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
