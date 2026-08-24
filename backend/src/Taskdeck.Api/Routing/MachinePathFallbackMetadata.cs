namespace Taskdeck.Api.Routing;

/// <summary>
/// Marks the per-prefix catch-all endpoints that answer unmatched machine-facing paths, so
/// <see cref="MachineRouteMethodResolver"/> can exclude them when it asks whether a <em>real</em>
/// endpoint exists at a request path. Without the marker the catch-alls would match every path
/// under their own prefix and the resolver would report that every unknown path exists (#1992).
/// </summary>
internal sealed class MachinePathFallbackMetadata
{
    internal static MachinePathFallbackMetadata Instance { get; } = new();

    private MachinePathFallbackMetadata()
    {
    }
}
