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
    /// Applied to <c>SqliteDbContextOptionsBuilder.CommandTimeout</c>.
    /// This affects all EF Core commands including migrations
    /// (<c>Database.Migrate()</c>). Avoid setting very low values (e.g. 1s)
    /// if schema migrations are expected.
    /// </summary>
    [Range(1, 300, ErrorMessage = "CommandTimeoutSeconds must be between 1 and 300.")]
    public int CommandTimeoutSeconds { get; set; } = 30;
}
