using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that manages a single PostgreSQL Testcontainer
/// for the lifetime of a test collection. Each test class that uses this
/// fixture gets its own <see cref="TaskdeckDbContext"/> with a fresh
/// database schema — no cross-test contamination.
///
/// Design notes:
///   - One container per collection (not per test) to keep startup cost manageable.
///   - Schema is created via <c>EnsureCreated()</c> from the EF Core model
///     rather than running SQLite-specific migrations, ensuring provider parity.
///   - Each test class gets an isolated database by using a unique database
///     name within the same PostgreSQL server instance.
///   - The container is torn down deterministically via <see cref="IAsyncLifetime"/>.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private string _baseConnectionString = string.Empty;
    private int _dbCounter;

    public PostgresContainerFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("taskdeck_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    /// <summary>
    /// Whether Docker was detected and the container started successfully.
    /// Tests should check this before accessing the container.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Starts the PostgreSQL container. Called once per test collection.
    /// If Docker is not available, the container is not started and
    /// <see cref="IsAvailable"/> remains false — tests should skip gracefully.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!DockerAvailableCheck.IsAvailable)
        {
            IsAvailable = false;
            return;
        }

        await _container.StartAsync();
        _baseConnectionString = _container.GetConnectionString();
        IsAvailable = true;
    }

    /// <summary>
    /// Stops and removes the PostgreSQL container. Called once per test collection.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a new <see cref="TaskdeckDbContext"/> backed by an isolated
    /// database within the shared PostgreSQL container. The schema is created
    /// from the EF Core model on first access.
    /// </summary>
    public TaskdeckDbContext CreateDbContext()
    {
        var dbName = $"taskdeck_test_{Interlocked.Increment(ref _dbCounter)}";

        // Create the database first using the default connection.
        // The database name is internally generated (counter-based, no user input)
        // so SQL injection is not a concern here. We use ExecuteSqlRaw because
        // CREATE DATABASE does not support parameterised identifiers in PostgreSQL.
        using (var adminContext = BuildContext(_baseConnectionString))
        {
            #pragma warning disable EF1002 // dbName is counter-based, not user input
            adminContext.Database.ExecuteSqlRaw($"CREATE DATABASE \"{dbName}\"");
            #pragma warning restore EF1002
        }

        var connectionString = ReplaceDatabase(_baseConnectionString, dbName);
        var context = BuildContext(connectionString);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Gets the base connection string (pointing to the default database).
    /// Useful for tests that need to manage connections manually.
    /// </summary>
    public string ConnectionString => _baseConnectionString;

    private static TaskdeckDbContext BuildContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TaskdeckDbContext(options);
    }

    private static string ReplaceDatabase(string connectionString, string newDbName)
    {
        // Npgsql connection strings use "Database=<name>" key
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = newDbName
        };
        return builder.ConnectionString;
    }
}
