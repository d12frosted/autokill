using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace AutoKill.IPC;

/// <summary>Movement, delegated to vnavmesh.</summary>
/// <remarks>
/// Every call is guarded. vnavmesh may not be installed, may be disabled, or may
/// still be building a mesh for the zone that was just loaded, and none of those
/// should be an exception escaping into the farming loop.
/// </remarks>
public sealed class NavmeshIpc(IDalamudPluginInterface plugin)
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

    public bool Available => Try(() => isReady.InvokeFunc(), false);

    /// <summary>True once a mesh exists for the current zone.</summary>
    public bool Ready => Try(() => isReady.InvokeFunc(), false);

    public float BuildProgress => Try(() => buildProgress.InvokeFunc(), -1f);

    public bool PathfindInProgress => Try(() => pathfindInProgress.InvokeFunc(), false);

    public bool Moving => Try(() => isRunning.InvokeFunc(), false);

    public bool MoveTo(Vector3 destination) => Try(() => moveTo.InvokeFunc(destination, false), false);

    public bool MoveCloseTo(Vector3 destination, float range) =>
        Try(() => moveCloseTo.InvokeFunc(destination, false, range), false);

    public void Stop() => Try<object?>(() => { stop.InvokeAction(); return null; }, null);

    /// <summary>
    /// Drop a point onto the ground. Published spawn data carries no usable
    /// height, so an XZ position has to be resolved against the mesh before it
    /// is worth pathing to.
    /// </summary>
    public Vector3? PointOnFloor(Vector3 position, float halfExtentXZ = 5f) =>
        Try(() => pointOnFloor.InvokeFunc(position, false, halfExtentXZ), null);

    private static T Try<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
