namespace Taskdeck.Domain.Connectors;

/// <summary>
/// Value object representing the health status of a connector provider.
/// </summary>
public sealed class ConnectorHealthResult
{
    public ConnectorHealthStatus Status { get; }
    public string Message { get; }
    public DateTimeOffset CheckedAt { get; }

    private ConnectorHealthResult(ConnectorHealthStatus status, string message)
    {
        Status = status;
        Message = message;
        CheckedAt = DateTimeOffset.UtcNow;
    }

    public static ConnectorHealthResult Healthy(string message = "Provider is healthy.")
        => new(ConnectorHealthStatus.Healthy, message);

    public static ConnectorHealthResult Degraded(string message)
        => new(ConnectorHealthStatus.Degraded, message);

    public static ConnectorHealthResult Unhealthy(string message)
        => new(ConnectorHealthStatus.Unhealthy, message);
}
