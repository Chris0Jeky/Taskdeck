using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Single source of truth for Taskdeck's SQLite <see cref="DbContextOptionsBuilder"/>
/// configuration. Both the production registration (<c>DependencyInjection.AddInfrastructure</c>)
/// and the API integration-test factory (<c>TestWebApplicationFactory</c>) MUST build their
/// options through this helper so the two registrations cannot drift apart — a hand-copied
/// factory block silently losing the pragma interceptor (and with it the per-connection
/// <c>busy_timeout</c>) is exactly how the #1282 transient <c>SQLITE_BUSY</c> 500s happened.
/// </summary>
public static class TaskdeckSqliteDbContextOptionsExtensions
{
    /// <summary>
    /// Applies Taskdeck's standard SQLite options:
    /// <list type="bullet">
    ///   <item>the configured command timeout (<see cref="DatabaseSettings.CommandTimeoutSeconds"/>),
    ///   which applies to all EF Core commands including <c>Database.Migrate()</c>;</item>
    ///   <item><see cref="SqlitePragmaConnectionInterceptor"/> (WAL +
    ///   <see cref="DatabaseSettings.BusyTimeoutMilliseconds"/>) so processes sharing one
    ///   SQLite file don't hit <c>SQLITE_BUSY</c> ("database is locked") under normal
    ///   concurrency.</item>
    /// </list>
    /// Only the connection string may vary between callers.
    /// </summary>
    public static DbContextOptionsBuilder UseTaskdeckSqlite(
        this DbContextOptionsBuilder options,
        string connectionString,
        DatabaseSettings databaseSettings)
    {
        return options
            .UseSqlite(connectionString, sqliteOptions =>
                sqliteOptions.CommandTimeout(databaseSettings.CommandTimeoutSeconds))
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(
                databaseSettings.BusyTimeoutMilliseconds));
    }
}
