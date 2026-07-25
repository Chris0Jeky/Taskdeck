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
    public void HostedWorkerDisabledFactory_RemovesApplicationWorkers_ButKeepsTheWebHostService()
    {
        // Guards the issue #1335 isolation mechanism against silent regression: if a future
        // change re-registers a background worker (or the removal stops working), the
        // repository-focused test classes would again be pre-emptible and this test fails
        // closed. The base factory must still run workers so worker-dependent test classes keep
        // their coverage, and the framework web-host service must survive on the workerless
        // host or the TestServer would never start.
        //
        // Detection deliberately reuses the factory's own removal contract
        // (IsApplicationWorkerType) so the two predicates cannot drift — and, because this test
        // classifies LIVE resolved instances rather than service descriptors, it also catches an
        // app worker registered via an ImplementationFactory delegate, which the factory's
        // descriptor filter cannot classify (see the comment in
        // HostedWorkerDisabledTestWebApplicationFactory.ConfigureWebHost).
        static bool IsApplicationWorker(IHostedService service) =>
            HostedWorkerDisabledTestWebApplicationFactory.IsApplicationWorkerType(service.GetType());

        using var baseFactory = new TestWebApplicationFactory();
        baseFactory.Services.GetServices<IHostedService>().Where(IsApplicationWorker)
            .Should().NotBeEmpty(
                "the base test host must keep the application background workers so " +
                "worker-dependent test classes retain their coverage");

        using var workerlessFactory = new HostedWorkerDisabledTestWebApplicationFactory();
        var workerlessHostedServices = workerlessFactory.Services.GetServices<IHostedService>().ToList();
        workerlessHostedServices.Where(IsApplicationWorker)
            .Should().BeEmpty(
                "the workerless host must run no hosted service from the API assembly " +
                "(the IsApplicationWorkerType contract) — one present means a background worker " +
                "can again pre-empt repository-focused tests (issue #1335)");
        workerlessHostedServices
            .Should().NotBeEmpty(
                "framework hosted services (GenericWebHostService) must survive the removal " +
                "so the TestServer still starts and CreateClient() keeps working");
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
