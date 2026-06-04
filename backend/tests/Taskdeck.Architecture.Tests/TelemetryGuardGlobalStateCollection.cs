using Xunit;

namespace Taskdeck.Architecture.Tests;

/// <summary>
/// Anchors the "TelemetryGuardGlobalState" xUnit collection. Test classes that mutate
/// TelemetryGuard's process-wide static options (via <c>TelemetryGuard.Configure</c>) should
/// carry <c>[Collection("TelemetryGuardGlobalState")]</c> so xUnit runs them serially with
/// respect to one another rather than in parallel (the default for distinct test classes).
///
/// Today only <see cref="RoadmapInvariantTests"/> (INV-11) touches that state in this assembly,
/// so this definition exists primarily to give the next TelemetryGuard-touching test class a
/// documented, discoverable join point. Note: xUnit collections only serialize within a single
/// assembly — <c>TelemetryGuardTests</c> in Taskdeck.Application.Tests runs in a separate process.
/// </summary>
[CollectionDefinition("TelemetryGuardGlobalState")]
public sealed class TelemetryGuardGlobalStateCollection
{
}
