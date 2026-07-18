using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
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

    [Fact]
    public void AddRegistrationGating_LeavesFreshDatabaseBootstrapUnclaimed()
    {
        _context.Database.Migrate();

        GetRegistrationBootstrapCount().Should().Be(0,
            "a fresh database must permit one operator-invite bootstrap transaction");
    }

    [Fact]
    public void AddRegistrationGating_MarksExistingInstallationAsAlreadyBootstrapped()
    {
        _context.GetService<IMigrator>()
            .Migrate("20260627003457_AddProposalFeedback");
        InsertLegacyUser("existing-user", "existing@example.test");

        _context.GetService<IMigrator>()
            .Migrate("20260713022601_AddRegistrationGating");

        GetRegistrationBootstrapCount().Should().Be(1,
            "upgrading an installation with users must not reopen first-user registration");
    }

    [Fact]
    public async Task AddRegistrationGating_CliActorOnlyDatabaseCanRedeemFirstOwnerInvite()
    {
        _context.GetService<IMigrator>()
            .Migrate("20260627003457_AddProposalFeedback");
        InsertLegacyUser("taskdeck_cli_actor", "cli-actor@system.taskdeck");

        _context.GetService<IMigrator>()
            .Migrate("20260713022601_AddRegistrationGating");

        GetRegistrationBootstrapCount().Should().Be(0,
            "a CLI-only database must still permit an operator-invite owner bootstrap");

        var code = RegistrationPolicyService.GenerateInviteCode();
        var invite = new RegistrationInvite(
            RegistrationPolicyService.HashInviteCode(code),
            code[..10],
            DateTimeOffset.UtcNow.AddHours(1));
        _context.RegistrationInvites.Add(invite);
        await _context.SaveChangesAsync();

        var policy = new RegistrationPolicyService(
            new RegistrationSettings { Mode = RegistrationMode.Closed },
            new RegistrationPolicyStore(_context),
            Mock.Of<IUnitOfWork>());

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var eligibility = await policy.CheckNewUserEligibilityAsync(code);
        var authorization = await policy.AuthorizeNewUserAsync(code);
        _context.Users.Add(new User(
            "first-owner",
            "first-owner@example.test",
            "test-password-hash"));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        eligibility.IsSuccess.Should().BeTrue();
        authorization.IsSuccess.Should().BeTrue();
        authorization.Value.ClaimedFirstUserBootstrap.Should().BeTrue();
        GetRegistrationBootstrapCount().Should().Be(1);
        _context.ChangeTracker.Clear();
        var persistedInvite = await _context.RegistrationInvites.SingleAsync(
            candidate => candidate.Id == invite.Id);
        persistedInvite.ConsumedAt.Should().NotBeNull();
        (await _context.Users.AnyAsync(user => user.Email == "first-owner@example.test"))
            .Should().BeTrue();
    }

    [Fact]
    public void ExtendProposalOutcomesForMetrics_preserves_legacy_outcome_type_decisions()
    {
        // Arrange — stop just before the metrics-extension migration, then seed rows
        // in the legacy ProposalOutcomes shape that only had OutcomeType.
        _context.GetService<IMigrator>()
            .Migrate("20260425105642_AddProposalRevisionsAndOutcomes");

        var rows = new Dictionary<Guid, OutcomeType>
        {
            [Guid.NewGuid()] = OutcomeType.Approved,
            [Guid.NewGuid()] = OutcomeType.EditedThenApproved,
            [Guid.NewGuid()] = OutcomeType.Rejected,
            [Guid.NewGuid()] = OutcomeType.Ignored
        };

        foreach (var (id, outcomeType) in rows)
        {
            InsertLegacyProposalOutcome(id, outcomeType);
        }

        // Act — apply the migration that adds Decision.
        _context.GetService<IMigrator>()
            .Migrate("20260425173300_ExtendProposalOutcomesForMetrics");

        // Assert — every legacy outcome receives the matching new decision value
        // instead of the non-null column default of Approved.
        foreach (var (id, outcomeType) in rows)
        {
            var decision = GetOutcomeDecision(id);
            decision.Should().Be((int)ToOutcomeDecision(outcomeType));
        }
    }

    [Fact]
    public void ExtendProposalOutcomesForMetrics_backfills_edit_counts_for_legacy_edited_rows()
    {
        // Arrange — stop just before the metrics-extension migration, then seed rows
        // in the legacy ProposalOutcomes shape.
        _context.GetService<IMigrator>()
            .Migrate("20260425105642_AddProposalRevisionsAndOutcomes");

        var approvedId = Guid.NewGuid();
        var editedId = Guid.NewGuid();
        var rejectedId = Guid.NewGuid();
        var ignoredId = Guid.NewGuid();

        InsertLegacyProposalOutcome(approvedId, OutcomeType.Approved);
        InsertLegacyProposalOutcome(editedId, OutcomeType.EditedThenApproved);
        InsertLegacyProposalOutcome(rejectedId, OutcomeType.Rejected);
        InsertLegacyProposalOutcome(ignoredId, OutcomeType.Ignored);

        // Act — apply the migration that adds the edit-count columns and backfills them.
        _context.GetService<IMigrator>()
            .Migrate("20260425173300_ExtendProposalOutcomesForMetrics");

        // Assert — EditedThenApproved rows get non-zero sentinel counts.
        var (editedField, fieldCount) = GetEditCounts(editedId);
        editedField.Should().Be(1, "legacy EditedThenApproved rows should have EditedFieldCount = 1");
        fieldCount.Should().Be(1, "legacy EditedThenApproved rows should have FieldCount = 1");

        // Assert — non-edited rows keep their default 0/0.
        foreach (var id in new[] { approvedId, rejectedId, ignoredId })
        {
            var (ef, fc) = GetEditCounts(id);
            ef.Should().Be(0, "non-edited legacy rows should keep EditedFieldCount = 0");
            fc.Should().Be(0, "non-edited legacy rows should keep FieldCount = 0");
        }
    }

    [Fact]
    public void AddApprovedRevisionId_backfills_latest_revision_pin_for_preexisting_approved_proposals()
    {
        // #1428 backfill: before the pinning migration, Apply materialized the LATEST revision for
        // an Approved proposal; after it, a null pin means "apply the ORIGINAL operations". The
        // migration must therefore pin already-Approved proposals with revisions to their latest
        // revision, or the deploy would silently revert them to content a reviewer edited out.
        // Arrange — stop just before the pinning migration and seed the pre-migration shape.
        _context.GetService<IMigrator>()
            .Migrate("20260713054422_AddArtefactExtractions");

        var approvedWithRevisions = Guid.NewGuid();
        var approvedWithoutRevisions = Guid.NewGuid();
        var pendingWithRevision = Guid.NewGuid();
        var latestRevisionId = Guid.NewGuid();

        InsertPreMigrationProposal(approvedWithRevisions, status: 1);    // Approved
        InsertPreMigrationProposal(approvedWithoutRevisions, status: 1); // Approved, no revisions
        InsertPreMigrationProposal(pendingWithRevision, status: 0);      // PendingReview

        InsertPreMigrationRevision(Guid.NewGuid(), approvedWithRevisions, revisionNumber: 1);
        InsertPreMigrationRevision(latestRevisionId, approvedWithRevisions, revisionNumber: 2);
        InsertPreMigrationRevision(Guid.NewGuid(), pendingWithRevision, revisionNumber: 1);

        // Act — apply the pinning migration (and any remainder of the chain).
        _context.Database.Migrate();

        // Assert — the approved-with-revisions proposal is pinned to its LATEST revision; the
        // revisionless approved proposal and the pending proposal stay unpinned (null pin =
        // original operations for the former; approve pins the latter going forward).
        GetApprovedRevisionId(approvedWithRevisions).Should().Be(latestRevisionId,
            "an already-approved proposal with revisions must be pinned to its latest revision " +
            "to preserve pre-migration Apply behavior");
        GetApprovedRevisionId(approvedWithoutRevisions).Should().BeNull(
            "an approved proposal without revisions applies its original operations");
        GetApprovedRevisionId(pendingWithRevision).Should().BeNull(
            "pending proposals are pinned at approve time, not by the backfill");
    }

    private void InsertPreMigrationProposal(Guid id, int status)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = DateTime.UtcNow.AddDays(1);

        _context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO AutomationProposals
                (Id, SourceType, SourceReferenceId, BoardId, RequestedByUserId, Status, RiskLevel, Summary,
                 DiffPreview, ValidationIssues, ExpiresAt, DecidedAt, DecidedByUserId, AppliedAt,
                 FailureReason, CorrelationId, CreatedAt, UpdatedAt)
            VALUES
                ({id}, 1, NULL, NULL, {Guid.NewGuid()}, {status}, 0, 'Pre-migration proposal',
                 NULL, NULL, {expiresAt}, NULL, NULL, NULL,
                 NULL, {Guid.NewGuid().ToString("N")}, {now}, {now})
            """);
    }

    private void InsertPreMigrationRevision(Guid id, Guid proposalId, int revisionNumber)
    {
        var now = DateTimeOffset.UtcNow;

        // The payload body is opaque to the backfill (it copies revision IDs, never parses
        // payloads), so a plain placeholder keeps the seed simple.
        _context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO ProposalRevisions
                (Id, ProposalId, RevisionNumber, EditorUserId, RevisedPayload, RevisedAt, Reason,
                 CreatedAt, UpdatedAt)
            VALUES
                ({id}, {proposalId}, {revisionNumber}, {Guid.NewGuid()}, 'pre-migration payload', {now},
                 'pre-migration revision', {now}, {now})
            """);
    }

    private Guid? GetApprovedRevisionId(Guid proposalId)
    {
        var connection = _context.Database.GetDbConnection();
        _context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ApprovedRevisionId FROM AutomationProposals WHERE Id = $id";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = proposalId;
            cmd.Parameters.Add(parameter);

            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? null : Guid.Parse((string)value);
        }
        finally
        {
            _context.Database.CloseConnection();
        }
    }

    private void InsertLegacyProposalOutcome(Guid id, OutcomeType outcomeType)
    {
        var proposalId = Guid.NewGuid();
        var decidedByUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = DateTime.UtcNow.AddDays(1);

        _context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO AutomationProposals
                (Id, SourceType, SourceReferenceId, BoardId, RequestedByUserId, Status, RiskLevel, Summary,
                 DiffPreview, ValidationIssues, ExpiresAt, DecidedAt, DecidedByUserId, AppliedAt,
                 FailureReason, CorrelationId, CreatedAt, UpdatedAt)
            VALUES
                ({proposalId}, 1, NULL, NULL, {decidedByUserId}, 0, 0, 'Legacy proposal',
                 NULL, NULL, {expiresAt}, NULL, NULL, NULL,
                 NULL, {Guid.NewGuid().ToString("N")}, {now}, {now})
            """);

        _context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO ProposalOutcomes
                (Id, ProposalId, OutcomeType, DecidedByUserId, DecidedAt, CreatedAt, UpdatedAt)
            VALUES
                ({id}, {proposalId}, {(int)outcomeType}, {decidedByUserId}, {now}, {now}, {now})
            """);
    }

    private int GetOutcomeDecision(Guid id)
    {
        var connection = _context.Database.GetDbConnection();
        _context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Decision FROM ProposalOutcomes WHERE Id = $id";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = id;
            cmd.Parameters.Add(parameter);

            var value = cmd.ExecuteScalar();
            value.Should().NotBeNull();
            return Convert.ToInt32(value);
        }
        finally
        {
            _context.Database.CloseConnection();
        }
    }

    private (int EditedFieldCount, int FieldCount) GetEditCounts(Guid id)
    {
        var connection = _context.Database.GetDbConnection();
        _context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT EditedFieldCount, FieldCount FROM ProposalOutcomes WHERE Id = $id";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = id;
            cmd.Parameters.Add(parameter);

            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();
            return (reader.GetInt32(0), reader.GetInt32(1));
        }
        finally
        {
            _context.Database.CloseConnection();
        }
    }

    private static OutcomeDecision ToOutcomeDecision(OutcomeType outcomeType)
    {
        return outcomeType switch
        {
            OutcomeType.Approved => OutcomeDecision.Approved,
            OutcomeType.EditedThenApproved => OutcomeDecision.EditedThenApproved,
            OutcomeType.Rejected => OutcomeDecision.Rejected,
            OutcomeType.Ignored => OutcomeDecision.Ignored,
            _ => throw new ArgumentOutOfRangeException(nameof(outcomeType), outcomeType, null)
        };
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

    private long GetRegistrationBootstrapCount()
    {
        var connection = _context.Database.GetDbConnection();
        _context.Database.OpenConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM RegistrationBootstraps";
            return Convert.ToInt64(command.ExecuteScalar());
        }
        finally
        {
            _context.Database.CloseConnection();
        }
    }

    private void InsertLegacyUser(string username, string email)
    {
        var now = DateTimeOffset.UtcNow;
        _context.Database.ExecuteSqlInterpolated($"""
            INSERT INTO "Users"
                ("Id", "Username", "Email", "PasswordHash", "DefaultRole", "IsActive",
                 "TokenInvalidatedAt", "MfaEnabled", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {username}, {email}, {"hash"},
                 {2}, {true}, {(DateTimeOffset?)null}, {false}, {now}, {now});
            """);
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
