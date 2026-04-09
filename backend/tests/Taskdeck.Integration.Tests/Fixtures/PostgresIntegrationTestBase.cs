using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// Base class for PostgreSQL-backed integration tests. Each derived class
/// receives a fresh, isolated <see cref="TaskdeckDbContext"/> that is
/// disposed after all tests in the class complete.
///
/// Usage:
/// <code>
/// [Collection(PostgresTestCollection.Name)]
/// public class MyTests : PostgresIntegrationTestBase
/// {
///     public MyTests(PostgresContainerFixture fixture) : base(fixture) { }
///
///     [Fact]
///     public async Task MyTest()
///     {
///         // Use Db property for database operations
///         Db.Boards.Add(new Board("test", "desc"));
///         await Db.SaveChangesAsync();
///     }
/// }
/// </code>
///
/// Thread safety: Each test class instance gets its own DbContext
/// (backed by its own database), so parallel test classes are safe.
/// Tests within the same class share the DbContext and must not
/// run in parallel (xUnit default for a single class).
/// </summary>
public abstract class PostgresIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private TaskdeckDbContext? _db;

    protected PostgresIntegrationTestBase(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The isolated <see cref="TaskdeckDbContext"/> for this test class.
    /// Backed by a unique PostgreSQL database within the shared container.
    /// </summary>
    protected TaskdeckDbContext Db => _db ?? throw new InvalidOperationException(
        "DbContext is not available. Ensure InitializeAsync has completed.");

    /// <summary>
    /// Called by xUnit before the first test in the class runs.
    /// Creates the isolated database and schema.
    /// </summary>
    public Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by xUnit after the last test in the class completes.
    /// Disposes the DbContext (the database remains in the container
    /// until the container itself is torn down).
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
