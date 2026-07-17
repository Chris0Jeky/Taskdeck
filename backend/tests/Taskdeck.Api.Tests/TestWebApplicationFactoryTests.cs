using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TestWebApplicationFactoryTests
{
    [Fact]
    public void HostedWorkerDisabledFactory_RemovesTaskdeckWorkers_ButKeepsTheWebHostService()
    {
        // Guards the issue #1335 isolation mechanism against silent regression: if a future
        // change re-registers a background worker (or the removal stops working), the LLM queue
        // claim tests would again be pre-emptible and this test fails closed. The base factory
        // must still run workers so worker-dependent test classes keep their coverage, and the
        // framework web-host service must survive on the workerless host or the TestServer would
        // never start.
        static bool IsTaskdeckWorker(IHostedService service) =>
            service.GetType().Namespace?.StartsWith("Taskdeck.Api.Workers", StringComparison.Ordinal)
            ?? false;

        using var baseFactory = new TestWebApplicationFactory();
        baseFactory.Services.GetServices<IHostedService>().Where(IsTaskdeckWorker)
            .Should().NotBeEmpty("the base test host must keep production background workers");

        using var workerlessFactory = new HostedWorkerDisabledTestWebApplicationFactory();
        var workerlessHostedServices = workerlessFactory.Services.GetServices<IHostedService>().ToList();
        workerlessHostedServices.Where(IsTaskdeckWorker)
            .Should().BeEmpty("the repository-focused host must register no Taskdeck background worker");
        workerlessHostedServices
            .Should().NotBeEmpty("the framework web-host service must survive so the TestServer still runs");
    }


    [Fact]
    public void GetDatabaseCleanupTargets_ShouldIncludeSqliteSidecars()
    {
        var targets = TestWebApplicationFactory.GetDatabaseCleanupTargets("C:\\temp\\taskdeck-api-tests.db");

        targets.Should().Equal(
            "C:\\temp\\taskdeck-api-tests.db",
            "C:\\temp\\taskdeck-api-tests.db-wal",
            "C:\\temp\\taskdeck-api-tests.db-shm",
            "C:\\temp\\taskdeck-api-tests.db-journal");
    }

    [Fact]
    public void DatabaseRegistration_ShouldPreserveProductionSqliteSettings()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
        var connection = context.Database.GetDbConnection();

        connection.Should().BeOfType<SqliteConnection>();
        Path.GetFileName(connection.DataSource).Should()
            .MatchRegex("^taskdeck-api-tests-[0-9a-f]{32}\\.db$");
        context.Database.GetCommandTimeout().Should().Be(settings.CommandTimeoutSeconds);

        context.Database.OpenConnection();
        try
        {
            ScalarPragma<string>(connection, "journal_mode").Should().BeEquivalentTo("wal");
            ScalarPragma<long>(connection, "busy_timeout").Should()
                .Be(settings.BusyTimeoutMilliseconds);
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static T ScalarPragma<T>(System.Data.Common.DbConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        var result = command.ExecuteScalar();
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
