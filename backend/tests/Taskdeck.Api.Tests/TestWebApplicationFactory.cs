using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly ConcurrentBag<string> _dbPaths = new();

    internal UnhandledExceptionDiagnosticSink UnhandledExceptionDiagnostics { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-api-tests-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);

        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new UnhandledExceptionDiagnosticLoggerProvider(UnhandledExceptionDiagnostics));
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var overrideSettings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestSqlite.ConnectionString(dbPath),
                // Provide a stable test JWT secret so tests do not depend on
                // appsettings.Development.json or FirstRunBootstrapper side-effects.
                ["Jwt:SecretKey"] = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
                ["RateLimiting:Enabled"] = "false",
                ["Workers:QueuePollIntervalSeconds"] = "1",
                ["Workers:MaxBatchSize"] = "10",
                ["Workers:MaxConcurrency"] = "1",
                ["Workers:RetryBackoffSeconds:0"] = "0",
                // Keep API integration tests deterministic regardless of local machine env secrets.
                ["Llm:EnableLiveProviders"] = "false",
                ["Llm:AllowLiveProvidersInDevelopment"] = "false",
                ["Llm:Provider"] = "Mock",
                ["Artefacts:MaxBytesPerArtefact"] = "1024",
                ["Artefacts:MaxBytesPerUser"] = "1024",
                // Test-only 256-bit encryption key for connector credentials.
                ["Connectors:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
            };

            configBuilder.AddInMemoryCollection(overrideSettings);
        });

        builder.ConfigureServices((context, services) =>
        {
            var databaseSettings = context.Configuration.GetSection("Database").Get<DatabaseSettings>()
                ?? new DatabaseSettings();

            services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
            services.RemoveAll<TaskdeckDbContext>();
            services.RemoveAll<LlmProviderSettings>();
            services.RemoveAll<ArtefactStorageSettings>();
            // Same shared helper as production DependencyInjection.AddInfrastructure —
            // only the connection string differs (isolated per-factory temp file), so
            // the test registration is structurally unable to drift from production (#1282).
            services.AddDbContext<TaskdeckDbContext>(options =>
                options.UseTaskdeckSqlite(TestSqlite.ConnectionString(dbPath), databaseSettings));
            services.AddSingleton(new LlmProviderSettings
            {
                EnableLiveProviders = false,
                AllowLiveProvidersInDevelopment = false,
                Provider = "Mock"
            });
            services.AddSingleton(new ArtefactStorageSettings
            {
                MaxBytesPerArtefact = 1024,
                MaxBytesPerUser = 1024
            });

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            dbContext.Database.Migrate();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // The host's connections use Pooling=False (TestSqlite, #1609), so disposing it above
        // closes the -wal/-shm handles outright and the delete loop below can succeed. This
        // previously called the process-global pool-clearing API, which released other test
        // classes' handles too and raced their concurrent opens.

        foreach (var dbPath in _dbPaths)
        {
            foreach (var cleanupPath in GetDatabaseCleanupTargets(dbPath))
            {
                if (!File.Exists(cleanupPath))
                {
                    continue;
                }

                try
                {
                    File.Delete(cleanupPath);
                }
                catch (IOException)
                {
                    // Cleanup failure should not fail test teardown.
                }
            }
        }
    }

    internal static IReadOnlyList<string> GetDatabaseCleanupTargets(string dbPath)
    {
        return
        [
            dbPath,
            $"{dbPath}-wal",
            $"{dbPath}-shm",
            $"{dbPath}-journal",
            $"{dbPath}.migrate.lock"
        ];
    }
}
