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
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var overrideSettings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                ["RateLimiting:Enabled"] = "false",
                ["Workers:QueuePollIntervalSeconds"] = "1",
                ["Workers:MaxBatchSize"] = "10",
                ["Workers:MaxConcurrency"] = "1",
                ["Workers:RetryBackoffSeconds:0"] = "0",
                // Keep API integration tests deterministic regardless of local machine env secrets.
                ["Llm:EnableLiveProviders"] = "false",
                ["Llm:AllowLiveProvidersInDevelopment"] = "false",
                ["Llm:Provider"] = "Mock"
            };

            configBuilder.AddInMemoryCollection(overrideSettings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
            services.RemoveAll<TaskdeckDbContext>();
            services.RemoveAll<LlmProviderSettings>();
            services.AddDbContext<TaskdeckDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));
            services.AddSingleton(new LlmProviderSettings
            {
                EnableLiveProviders = false,
                AllowLiveProvidersInDevelopment = false,
                Provider = "Mock"
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
            $"{dbPath}-journal"
        ];
    }
}
