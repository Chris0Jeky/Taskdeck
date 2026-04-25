namespace Taskdeck.Application.Services;

/// <summary>
/// Registry of all known outbound data paths. Supports disclosure auditing
/// and host allowlisting. Every external destination Taskdeck connects to
/// must be registered here (GP-10: Explicit Egress And Telemetry Boundaries).
/// </summary>
public interface IEgressRegistry
{
    /// <summary>
    /// Returns all registered egress entries.
    /// </summary>
    IReadOnlyList<EgressEntry> GetAllEntries();

    /// <summary>
    /// Returns true if the given host is in the egress registry (i.e., is a known destination).
    /// Unknown hosts are not allowed.
    /// </summary>
    bool IsHostAllowed(string host);
}
