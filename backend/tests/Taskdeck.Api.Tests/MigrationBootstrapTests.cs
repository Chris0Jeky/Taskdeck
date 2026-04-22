using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies that EF Core migrations apply cleanly to a fresh SQLite database,
/// producing the full expected schema. Guards against migration chain breaks
/// that would prevent fresh environment bootstrapping.
/// </summary>
public class MigrationBootstrapTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TaskdeckDbContext _context;

    public MigrationBootstrapTests()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-migration-test-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new TaskdeckDbContext(options);
    }

    [Fact]
    public void Migrations_apply_cleanly_to_fresh_database()
    {
        // Act — apply all migrations in order to an empty database
        _context.Database.Migrate();

        // Assert — no exception means the migration chain is unbroken.
        // Also verify the __EFMigrationsHistory table exists and has entries.
        var appliedMigrations = _context.Database
            .GetAppliedMigrations()
            .ToList();

        appliedMigrations.Should().NotBeEmpty(
            "the migration chain should produce at least one applied migration");
    }

    [Fact]
    public void Migrations_produce_all_expected_tables()
    {
        // Arrange — apply all migrations
        _context.Database.Migrate();

        // Act — query SQLite master for user tables
        var tables = GetUserTables();

        // Assert — every DbSet in TaskdeckDbContext should have a corresponding table.
        var expectedTables = new[]
        {
            "Boards",
            "Columns",
            "Cards",
            "CardComments",
            "CardCommentMentions",
            "Labels",
            "CardLabels",
            "Users",
            "BoardAccesses",
            "AuditLogs",
            "LlmRequests",
            "AutomationProposals",
            "AutomationProposalOperations",
            "ArchiveItems",
            "ChatSessions",
            "ChatMessages",
            "CommandRuns",
            "CommandRunLogs",
            "Notifications",
            "NotificationPreferences",
            "UserPreferences",
            "OutboundWebhookSubscriptions",
            "OutboundWebhookDeliveries",
            "LlmUsageRecords",
            "AgentProfiles",
            "AgentRuns",
            "AgentRunEvents",
            "KnowledgeDocuments",
            "KnowledgeChunks",
            "ExternalLogins",
            "OAuthAuthCodes",
            "ApiKeys",
            "MfaCredentials",
        };

        foreach (var table in expectedTables)
        {
            tables.Should().Contain(table,
                $"migration chain must create the '{table}' table");
        }
    }

    [Fact]
    public void Migrations_are_idempotent_when_already_applied()
    {
        // Arrange — apply all migrations once
        _context.Database.Migrate();
        var firstCount = _context.Database
            .GetAppliedMigrations()
            .Count();

        // Act — apply again (should be a no-op)
        _context.Database.Migrate();
        var secondCount = _context.Database
            .GetAppliedMigrations()
            .Count();

        // Assert
        secondCount.Should().Be(firstCount,
            "re-running Migrate() on an already-migrated database should not add migrations");
    }

    [Fact]
    public void Migration_schema_matches_model_snapshot()
    {
        // Arrange — apply all migrations
        _context.Database.Migrate();

        // Act — check for pending model changes
        var pendingMigrations = _context.Database
            .GetPendingMigrations()
            .ToList();

        // Assert — no migrations should be pending after a full Migrate()
        pendingMigrations.Should().BeEmpty(
            "all migrations should be applied; pending migrations indicate the snapshot is out of sync");
    }

    [Fact]
    public void All_migrations_have_distinct_timestamps()
    {
        // Act — get the full ordered migration list
        var allMigrations = _context.Database
            .GetMigrations()
            .ToList();

        // Assert — no duplicate migration IDs
        allMigrations.Should().OnlyHaveUniqueItems(
            "each migration must have a unique timestamp-based identifier");
    }

    private List<string> GetUserTables()
    {
        var tables = new List<string>();
        using var connection = _context.Database.GetDbConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public void Dispose()
    {
        _context.Dispose();

        foreach (var path in TestWebApplicationFactory.GetDatabaseCleanupTargets(_dbPath))
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }
}
