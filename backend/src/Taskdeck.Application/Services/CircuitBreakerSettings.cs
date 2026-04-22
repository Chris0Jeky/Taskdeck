using System.ComponentModel.DataAnnotations;

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
    /// Must be at least 1 (Polly throws <see cref="ArgumentOutOfRangeException"/>
    /// if the value is not positive). Default: 5.
    /// </summary>
    [Range(1, 1000, ErrorMessage = "FailureThreshold must be between 1 and 1000.")]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit stays open before transitioning
    /// to half-open. Must be at least 1 second. Default: 60 (1 minute).
    /// </summary>
    [Range(1, 3600, ErrorMessage = "BreakDurationSeconds must be between 1 and 3600.")]
    public int BreakDurationSeconds { get; set; } = 60;
}
