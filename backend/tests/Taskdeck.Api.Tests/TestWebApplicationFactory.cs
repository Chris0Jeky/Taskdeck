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
    private static readonly object PristineDatabaseLock = new();
    private static Lazy<PristineDatabase>? s_pristineDatabase;
    private static int s_pristineDatabaseMigrationCount;

    private readonly ConcurrentBag<string> _dbPaths = new();

    static TestWebApplicationFactory()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            Lazy<PristineDatabase>? pristineDatabase;
            lock (PristineDatabaseLock)
            {
                pristineDatabase = s_pristineDatabase;
            }

            if (pristineDatabase is { IsValueCreated: true })
            {
                DeleteDatabaseFiles(pristineDatabase.Value.Path, includePrimaryDatabase: true);
            }
        };
    }

    internal UnhandledExceptionDiagnosticSink UnhandledExceptionDiagnostics { get; } = new();

    // Test-only observation seam. This counts real Database.Migrate calls on the process-local
    // template, never copies into individual factory databases.
    internal static int PristineDatabaseMigrationCount => Volatile.Read(ref s_pristineDatabaseMigrationCount);

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
            var diagnostics = TestAssemblyDiagnostics.ActivateIfConfigured();
            var configureServicesStarted = diagnostics?.BeginConfigureServices();
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

            File.Copy(GetPristineDatabasePath(databaseSettings), dbPath);
            if (configureServicesStarted is long configureServicesStartedTimestamp)
            {
                diagnostics!.CompleteConfigureServices(configureServicesStartedTimestamp);
            }
        });
    }

    private static string GetPristineDatabasePath(DatabaseSettings databaseSettings)
    {
        Lazy<PristineDatabase> pristineDatabase;
        lock (PristineDatabaseLock)
        {
            // Every factory has the same in-memory Database section. Capture the first instance so
            // the one real migration uses the exact production-equivalent options as the copied hosts.
            pristineDatabase = s_pristineDatabase ??= new Lazy<PristineDatabase>(
                () => CreatePristineDatabase(databaseSettings),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        return pristineDatabase.Value.Path;
    }

    private static PristineDatabase CreatePristineDatabase(DatabaseSettings databaseSettings)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-api-tests-template-{Guid.NewGuid():N}.db");
        var diagnostics = TestAssemblyDiagnostics.ActivateIfConfigured();
        var databaseMigrateStarted = diagnostics?.BeginDatabaseMigrate();

        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<TaskdeckDbContext>();
            optionsBuilder.UseTaskdeckSqlite(TestSqlite.ConnectionString(databasePath), databaseSettings);

            using (var dbContext = new TaskdeckDbContext(optionsBuilder.Options))
            {
                dbContext.Database.Migrate();
                CheckpointAndCloseTemplateDatabase(dbContext);
            }

            // The context is fully disposed before deleting WAL state, so a byte copy of the main
            // database contains the complete schema and never shares a live SQLite connection.
            DeleteDatabaseFiles(databasePath, includePrimaryDatabase: false);
            Interlocked.Increment(ref s_pristineDatabaseMigrationCount);
            if (databaseMigrateStarted is long migrationStarted)
            {
                diagnostics!.CompleteDatabaseMigrate(migrationStarted);
            }

            return new PristineDatabase(databasePath);
        }
        catch
        {
            DeleteDatabaseFiles(databasePath, includePrimaryDatabase: true);
            throw;
        }
    }

    private static void CheckpointAndCloseTemplateDatabase(TaskdeckDbContext dbContext)
    {
        dbContext.Database.OpenConnection();
        try
        {
            using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteScalar();
        }
        finally
        {
            dbContext.Database.CloseConnection();
        }
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
            DeleteDatabaseFiles(dbPath, includePrimaryDatabase: true);
    }

    private static void DeleteDatabaseFiles(string dbPath, bool includePrimaryDatabase)
    {
        var cleanupTargets = includePrimaryDatabase
            ? GetDatabaseCleanupTargets(dbPath)
            : GetDatabaseCleanupTargets(dbPath).Skip(1);

        foreach (var cleanupPath in cleanupTargets)
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

    private sealed record PristineDatabase(string Path);
}
