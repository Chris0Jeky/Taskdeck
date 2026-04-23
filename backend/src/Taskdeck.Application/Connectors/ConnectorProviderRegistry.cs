using Taskdeck.Domain.Connectors;

namespace Taskdeck.Application.Connectors;

/// <summary>
/// In-memory registry of connector providers, populated by DI-based discovery at startup.
/// Thread-safe for concurrent reads after construction.
/// </summary>
public sealed class ConnectorProviderRegistry : IConnectorProviderRegistry
{
    private readonly Dictionary<string, IConnectorProvider> _providers;

    public ConnectorProviderRegistry(IEnumerable<IConnectorProvider> providers)
    {
        _providers = new Dictionary<string, IConnectorProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
                continue;

            // Last-wins for duplicates; in practice DI should not register duplicates.
            _providers[provider.ProviderId] = provider;
        }
    }

    public IReadOnlyList<IConnectorProvider> GetAll()
        => _providers.Values.ToList().AsReadOnly();

    public IConnectorProvider? GetById(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        _providers.TryGetValue(providerId, out var provider);
        return provider;
    }

    public bool IsRegistered(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        return _providers.ContainsKey(providerId);
    }
}
