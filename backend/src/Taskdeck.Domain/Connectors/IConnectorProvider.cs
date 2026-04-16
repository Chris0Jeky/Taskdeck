using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Connectors;

/// <summary>
/// Pluggable provider interface for connector implementations.
/// Each provider represents a specific integration target (e.g., GitHub, Slack).
/// </summary>
public interface IConnectorProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "github", "slack").
    /// Must be lowercase, alphanumeric with hyphens only.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// The connector type this provider implements.
    /// </summary>
    ConnectorType ConnectorType { get; }

    /// <summary>
    /// The direction of data flow for this provider.
    /// </summary>
    ConnectorDirection Direction { get; }

    /// <summary>
    /// Check the health of the external service this provider connects to.
    /// Implementations MUST enforce a timeout and not block indefinitely.
    /// </summary>
    Task<ConnectorHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Describe this provider's capabilities.
    /// </summary>
    Task<ConnectorCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
