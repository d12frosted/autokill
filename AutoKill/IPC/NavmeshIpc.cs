using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AutoKill.IPC;

/// <summary>Movement, delegated to vnavmesh.</summary>
/// <remarks>
/// Every call is guarded. vnavmesh may not be installed, may be disabled, or may
/// still be building a mesh for the zone that was just loaded, and none of those
/// should be an exception escaping into the farming loop.
/// </remarks>
public sealed class NavmeshIpc(IDalamudPluginInterface plugin, IPluginLog log)
{
    private readonly ICallGateSubscriber<bool> isReady =
        plugin.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");

    private readonly ICallGateSubscriber<float> buildProgress =
        plugin.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");

    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo =
        plugin.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");

    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo =
        plugin.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");

    private readonly ICallGateSubscriber<bool> pathfindInProgress =
        plugin.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    private readonly ICallGateSubscriber<object> stop =
        plugin.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

    private readonly ICallGateSubscriber<bool> isRunning =
        plugin.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");

    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor =
        plugin.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");

    private readonly ICallGateSubscriber<object> cancelPathfinding =
        plugin.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");

    /// <summary>
    /// Whether vnavmesh answers at all, which is a different question from
    /// whether it has a mesh ready: it answers false all the while it is
    /// building one, and answers nothing when it is not there.
    /// </summary>
    public bool Responding
    {
        get
        {
            try
            {
                isReady.InvokeFunc();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>True once a mesh exists for the current zone.</summary>
    public bool Ready => Try(() => isReady.InvokeFunc(), false);

    public float BuildProgress => Try(() => buildProgress.InvokeFunc(), -1f);

    public bool PathfindInProgress => Try(() => pathfindInProgress.InvokeFunc(), false);

    public bool Moving => Try(() => isRunning.InvokeFunc(), false);

    public bool MoveTo(Vector3 destination, bool fly = false) =>
        Try(() => moveTo.InvokeFunc(destination, fly), false);

    public bool MoveCloseTo(Vector3 destination, float range, bool fly = false) =>
        Try(() => moveCloseTo.InvokeFunc(destination, fly, range), false);

    /// <summary>Drop the current path. Anything already being worked out still lands.</summary>
    public void Stop() => Try<object?>(() => { stop.InvokeAction(); return null; }, null);

    /// <summary>
    /// Stop for good: drop the path and throw away anything still being worked
    /// out.
    /// </summary>
    /// <remarks>
    /// Dropping the path alone is not enough to stop a character. A pathfind
    /// already in flight finishes afterwards and starts walking the result,
    /// which looks exactly like a stop button that does nothing.
    ///
    /// Cancelling costs vnavmesh a mesh reload, so it is only done when a
    /// pathfind is actually outstanding rather than on every ordinary halt.
    /// </remarks>
    public void StopCompletely()
    {
        Stop();

        if (!PathfindInProgress)
            return;

        Try<object?>(() => { cancelPathfinding.InvokeAction(); return null; }, null);
    }

    /// <summary>
    /// Drop a point onto the ground. Published spawn data carries no usable
    /// height, so an XZ position has to be resolved against the mesh before it
    /// is worth pathing to.
    /// </summary>
    public Vector3? PointOnFloor(Vector3 position, float halfExtentXZ = 5f) =>
        Try(() => pointOnFloor.InvokeFunc(position, false, halfExtentXZ), null);

    /// <summary>
    /// vnavmesh may be absent, disabled, or still loading a zone. None of those
    /// should reach the farming loop, but none of them should pass unnoticed
    /// either: a silently swallowed failure here is a control that appears to
    /// work and does nothing.
    /// </summary>
    private T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            log.Warning($"vnavmesh call failed: {ex.Message}");
            return fallback;
        }
    }
}
