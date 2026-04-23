using Taskdeck.Domain.Connectors;

namespace Taskdeck.Application.Connectors;

/// <summary>
/// Service for registering, discovering, and resolving connector providers by their ID.
/// Populated at startup via DI-based discovery.
/// </summary>
public interface IConnectorProviderRegistry
{
    /// <summary>
    /// Get all registered providers.
    /// </summary>
    IReadOnlyList<IConnectorProvider> GetAll();

    /// <summary>
    /// Resolve a specific provider by its ID, or null if not found.
    /// </summary>
    IConnectorProvider? GetById(string providerId);

    /// <summary>
    /// Check whether a provider with the given ID is registered.
    /// </summary>
    bool IsRegistered(string providerId);
}
