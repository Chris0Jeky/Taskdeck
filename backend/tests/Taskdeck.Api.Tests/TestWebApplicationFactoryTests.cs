using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TestWebApplicationFactoryTests
{
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
