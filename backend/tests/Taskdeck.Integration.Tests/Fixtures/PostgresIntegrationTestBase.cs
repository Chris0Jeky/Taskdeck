using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// Base class for PostgreSQL-backed integration tests. Each test method
/// receives a fresh, isolated <see cref="TaskdeckDbContext"/> that is
/// disposed after the test completes (xUnit 2.x creates a new class
/// instance per test method, so each test gets its own database).
///
/// When Docker is not available, tests are skipped gracefully via
/// <see cref="SkipIfDockerUnavailable"/> rather than failing — this allows
/// <c>dotnet test backend/Taskdeck.sln</c> to pass on machines without Docker.
///
/// Usage:
/// <code>
/// [Collection(PostgresTestCollection.Name)]
/// public class MyTests : PostgresIntegrationTestBase
/// {
///     public MyTests(PostgresContainerFixture fixture) : base(fixture) { }
///
///     [SkippableFact]
///     public async Task MyTest()
///     {
///         SkipIfDockerUnavailable();
///         Db.Boards.Add(new Board("test", "desc"));
///         await Db.SaveChangesAsync();
///     }
/// }
/// </code>
///
/// Thread safety: Each test method gets its own DbContext instance
/// (backed by its own database), so parallel test execution is safe.
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
    /// Whether the PostgreSQL container is available for this test class.
    /// </summary>
    protected bool IsDockerAvailable => _fixture.IsAvailable;

    /// <summary>
    /// The isolated <see cref="TaskdeckDbContext"/> for this test class.
    /// Backed by a unique PostgreSQL database within the shared container.
    /// </summary>
    protected TaskdeckDbContext Db => _db ?? throw new InvalidOperationException(
        "DbContext is not available. Ensure Docker is running and InitializeAsync has completed.");

    /// <summary>
    /// Called by xUnit before each test method runs (xUnit 2.x creates a new
    /// instance per test). Creates an isolated database and schema.
    /// Skips if Docker is not available.
    /// </summary>
    public Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return Task.CompletedTask;
        }

        _db = _fixture.CreateDbContext();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by xUnit after each test method completes.
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

    /// <summary>
    /// Skips the current test if Docker is not available on the host.
    /// Call at the start of each test method. Uses SkippableFact's
    /// <c>Skip.If</c> to report the test as skipped rather than failed.
    /// </summary>
    protected static void SkipIfDockerUnavailable()
    {
        Skip.If(!DockerAvailableCheck.IsAvailable, DockerAvailableCheck.SkipReason);
    }
}
