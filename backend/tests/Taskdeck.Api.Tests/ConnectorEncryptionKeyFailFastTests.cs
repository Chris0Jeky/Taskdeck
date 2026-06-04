using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ConnectorEncryptionKeyFailFastTests
{
    [Fact]
    public void AddInfrastructure_WithoutEncryptionKey_ShouldThrowInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connectors:EncryptionKey*not configured*");
    }

    [Fact]
    public void AddInfrastructure_WithEmptyEncryptionKey_ShouldThrowInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // null-coalescing throws on null, but empty string passes that check
        // and hits the AesCredentialEncryptionService constructor validation.
        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void AddInfrastructure_WithValidEncryptionKey_ShouldNotThrow()
    {
        // Generate a valid base64-encoded 256-bit key.
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = validKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddInfrastructure_WiresWalAndBusyTimeoutInterceptorOnTheDbContext()
    {
        // Guards the production DI composition (AddDbContext + AddInterceptors): the
        // direct-construction interceptor unit test does not prove AddInfrastructure
        // actually registers it. Uses a file Data Source so WAL genuinely applies. #1130
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);

        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-di-wal-{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                ["Connectors:EncryptionKey"] = validKey,
                ["Database:BusyTimeoutMilliseconds"] = "5000",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);

        try
        {
            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            // Open through EF so the registered DbConnectionInterceptor fires.
            context.Database.OpenConnection();
            try
            {
                var connection = context.Database.GetDbConnection();
                using var journalCmd = connection.CreateCommand();
                journalCmd.CommandText = "PRAGMA journal_mode;";
                Convert.ToString(journalCmd.ExecuteScalar()).Should().BeEquivalentTo("wal");

                using var busyCmd = connection.CreateCommand();
                busyCmd.CommandText = "PRAGMA busy_timeout;";
                Convert.ToInt64(busyCmd.ExecuteScalar()).Should().Be(5000);
            }
            finally
            {
                context.Database.CloseConnection();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    // Best-effort: a still-locked file can throw IOException or
                    // UnauthorizedAccessException on Windows; neither should fail the test.
                    try { File.Delete(path); }
                    catch (Exception) { /* best-effort temp cleanup */ }
                }
            }
        }
    }

    [Fact]
    public void AddInfrastructure_ShouldResolveKnowledgeSearchThroughSemanticService()
    {
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = validKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IKnowledgeSearchService>()
            .Should().BeOfType<FallbackSemanticSearchService>();
        scope.ServiceProvider.GetRequiredService<ISemanticSearchService>()
            .Should().BeOfType<FallbackSemanticSearchService>();
        scope.ServiceProvider.GetRequiredService<IFtsKnowledgeSearchService>()
            .Should().BeOfType<KnowledgeFtsSearchService>();
        scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>()
            .Should().BeOfType<DisabledEmbeddingGenerator>();
    }

    [Fact]
    public void AddInfrastructure_ShouldResolveInMemoryEmbeddingGeneratorOnlyWhenExplicitlyEnabled()
    {
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = validKey,
                ["Knowledge:EnableInMemoryEmbeddings"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IEmbeddingGenerator>()
            .Should().BeOfType<InMemoryEmbeddingGenerator>();
    }
}
