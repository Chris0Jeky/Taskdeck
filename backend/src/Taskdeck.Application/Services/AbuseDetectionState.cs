using System.Collections.Concurrent;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Shared in-memory state for abuse detection.
/// Registered as a singleton to maintain actor state across scoped service instances.
/// Runtime modifications are held in memory (not persisted to config file), consistent
/// with the LlmKillSwitchSettings approach.
/// </summary>
public class AbuseDetectionState
{
    internal readonly ConcurrentDictionary<Guid, AbuseActor> Actors = new();
    internal readonly ConcurrentBag<AbuseEvent> Events = new();
    internal readonly object Lock = new();
}
