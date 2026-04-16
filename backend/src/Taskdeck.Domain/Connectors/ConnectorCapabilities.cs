using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Connectors;

/// <summary>
/// Value object describing what a connector provider can do.
/// </summary>
public sealed class ConnectorCapabilities
{
    /// <summary>
    /// Human-readable display name for the provider.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// The connector direction this provider supports.
    /// </summary>
    public ConnectorDirection Direction { get; }

    /// <summary>
    /// Event types this provider can produce or consume.
    /// </summary>
    public IReadOnlyList<string> SupportedEventTypes { get; }

    /// <summary>
    /// Authentication methods the provider supports.
    /// </summary>
    public IReadOnlyList<ConnectorAuthMethod> AuthMethods { get; }

    /// <summary>
    /// Maximum requests per minute, or null for no explicit limit.
    /// </summary>
    public int? RateLimitPerMinute { get; }

    /// <summary>
    /// Brief description of the provider for UI display.
    /// </summary>
    public string Description { get; }

    public ConnectorCapabilities(
        string displayName,
        ConnectorDirection direction,
        IReadOnlyList<string> supportedEventTypes,
        IReadOnlyList<ConnectorAuthMethod> authMethods,
        int? rateLimitPerMinute = null,
        string description = "")
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Direction = direction;
        SupportedEventTypes = supportedEventTypes ?? throw new ArgumentNullException(nameof(supportedEventTypes));
        AuthMethods = authMethods ?? throw new ArgumentNullException(nameof(authMethods));
        RateLimitPerMinute = rateLimitPerMinute;
        Description = description ?? string.Empty;
    }
}
