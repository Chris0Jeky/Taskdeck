using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        // Assert — every entity type in the EF model should have a corresponding table.
        // Derived from the model so the test automatically adapts when DbSets change.
        var expectedTables = _context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(t => t != null)
            .Distinct()
            .ToList();

        expectedTables.Should().NotBeEmpty(
            "the EF model should define at least one entity type");

        foreach (var table in expectedTables)
        {
            tables.Should().Contain(table!,
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
    public void Model_has_no_pending_changes_after_migrations_apply()
    {
        // Arrange — apply all migrations
        _context.Database.Migrate();

        // Act — verify no unapplied migration files remain
        var pendingMigrations = _context.Database
            .GetPendingMigrations()
            .ToList();

        pendingMigrations.Should().BeEmpty(
            "all migrations should be applied; pending migrations indicate the snapshot is out of sync");

        // Act — verify the compiled C# model matches the last migration snapshot.
        // HasPendingModelChanges() diffs the model against the snapshot and detects
        // entity/property additions that lack a corresponding migration.
        var hasDrift = _context.Database.HasPendingModelChanges();

        hasDrift.Should().BeFalse(
            "the EF model should match the last migration snapshot; " +
            "if this fails, run 'dotnet ef migrations add <Name>' to capture the drift");
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

        // Assert — no duplicate timestamp prefixes (first 14 digits: YYYYMMDDHHmmss).
        // Two migrations with the same timestamp but different names would pass the
        // uniqueness check above but indicate a scaffolding collision.
        var timestamps = allMigrations
            .Select(m => m.Split('_')[0])
            .ToList();

        timestamps.Should().OnlyHaveUniqueItems(
            "each migration must have a unique timestamp prefix; " +
            "collisions indicate migrations were scaffolded in the same second");
    }

    private List<string> GetUserTables()
    {
        var tables = new List<string>();
        var connection = _context.Database.GetDbConnection();
        _context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }
        finally
        {
            _context.Database.CloseConnection();
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
