using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for DbContext connection resilience.
/// Bound from appsettings.json "Database" section.
/// </summary>
public sealed class DatabaseSettings
{
    /// <summary>
    /// Command timeout in seconds for database operations.
    /// Applied to <c>DbContextOptionsBuilder.CommandTimeout</c>.
    /// </summary>
    [Range(1, 300, ErrorMessage = "CommandTimeoutSeconds must be between 1 and 300.")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of automatic retries on transient failures.
    /// Note: SQLite does not support <c>EnableRetryOnFailure</c>; this setting
    /// will take effect when the project migrates to PostgreSQL or another
    /// provider that supports execution strategies with retry.
    /// </summary>
    [Range(0, 10, ErrorMessage = "MaxRetryCount must be between 0 and 10.")]
    public int MaxRetryCount { get; set; } = 3;
}
