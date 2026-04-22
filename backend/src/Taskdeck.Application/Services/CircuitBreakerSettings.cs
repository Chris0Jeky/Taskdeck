namespace Taskdeck.Application.Services;

/// <summary>
/// Configurable settings for the Polly circuit breaker applied to external
/// HTTP clients (LLM providers and OAuth). Bound from the
/// <c>CircuitBreaker</c> configuration section.
/// </summary>
public class CircuitBreakerSettings
{
    /// <summary>
    /// Number of consecutive failures before the circuit opens.
    /// Default: 5.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit stays open before transitioning
    /// to half-open. Default: 60 (1 minute).
    /// </summary>
    public int BreakDurationSeconds { get; set; } = 60;
}
