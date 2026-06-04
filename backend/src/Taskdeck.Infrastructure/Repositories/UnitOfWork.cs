using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private const int MaxSqliteWriteLockRetries = 5;
    private const int MaxWalCheckpointAttempts = 3;

    private readonly TaskdeckDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(
        TaskdeckDbContext context,
        IBoardRepository boards,
        IColumnRepository columns,
        ICardRepository cards,
        ICardCommentRepository cardComments,
        ILabelRepository labels,
        IUserRepository users,
        IBoardAccessRepository boardAccesses,
        IAuditLogRepository auditLogs,
        ILlmQueueRepository llmQueue,
        IAutomationProposalRepository automationProposals,
        IArchiveItemRepository archiveItems,
        IChatSessionRepository chatSessions,
        IChatMessageRepository chatMessages,
        ICommandRunRepository commandRuns,
        INotificationRepository notifications,
        INotificationPreferenceRepository notificationPreferences,
        IUserPreferenceRepository userPreferences,
        IOutboundWebhookSubscriptionRepository outboundWebhookSubscriptions,
        IOutboundWebhookDeliveryRepository outboundWebhookDeliveries,
        ILlmUsageRecordRepository llmUsageRecords,
        IAgentProfileRepository agentProfiles,
        IAgentRunRepository agentRuns,
        IKnowledgeDocumentRepository knowledgeDocuments,
        IKnowledgeChunkRepository knowledgeChunks,
        IExternalLoginRepository externalLogins,
        IOAuthAuthCodeRepository oauthAuthCodes,
        IApiKeyRepository apiKeys,
        IMfaCredentialRepository mfaCredentials,
        IIntegrationConnectorRepository integrationConnectors,
        IConnectorEventRepository connectorEvents,
        IConnectorCredentialRepository connectorCredentials,
        IProposalRevisionRepository proposalRevisions,
        IDailySnapshotRepository dailySnapshots,
        ITomorrowNoteRepository tomorrowNotes,
        IMcpToolHashRepository mcpToolHashes)
    {
        _context = context;
        Boards = boards;
        Columns = columns;
        Cards = cards;
        CardComments = cardComments;
        Labels = labels;
        Users = users;
        BoardAccesses = boardAccesses;
        AuditLogs = auditLogs;
        LlmQueue = llmQueue;
        AutomationProposals = automationProposals;
        ArchiveItems = archiveItems;
        ChatSessions = chatSessions;
        ChatMessages = chatMessages;
        CommandRuns = commandRuns;
        Notifications = notifications;
        NotificationPreferences = notificationPreferences;
        UserPreferences = userPreferences;
        OutboundWebhookSubscriptions = outboundWebhookSubscriptions;
        OutboundWebhookDeliveries = outboundWebhookDeliveries;
        LlmUsageRecords = llmUsageRecords;
        AgentProfiles = agentProfiles;
        AgentRuns = agentRuns;
        KnowledgeDocuments = knowledgeDocuments;
        KnowledgeChunks = knowledgeChunks;
        ExternalLogins = externalLogins;
        OAuthAuthCodes = oauthAuthCodes;
        ApiKeys = apiKeys;
        MfaCredentials = mfaCredentials;
        IntegrationConnectors = integrationConnectors;
        ConnectorEvents = connectorEvents;
        ConnectorCredentials = connectorCredentials;
        ProposalRevisions = proposalRevisions;
        DailySnapshots = dailySnapshots;
        TomorrowNotes = tomorrowNotes;
        McpToolHashes = mcpToolHashes;
    }

    public IBoardRepository Boards { get; }
    public IColumnRepository Columns { get; }
    public ICardRepository Cards { get; }
    public ICardCommentRepository CardComments { get; }
    public ILabelRepository Labels { get; }
    public IUserRepository Users { get; }
    public IBoardAccessRepository BoardAccesses { get; }
    public IAuditLogRepository AuditLogs { get; }
    public ILlmQueueRepository LlmQueue { get; }
    public IAutomationProposalRepository AutomationProposals { get; }
    public IArchiveItemRepository ArchiveItems { get; }
    public IChatSessionRepository ChatSessions { get; }
    public IChatMessageRepository ChatMessages { get; }
    public ICommandRunRepository CommandRuns { get; }
    public INotificationRepository Notifications { get; }
    public INotificationPreferenceRepository NotificationPreferences { get; }
    public IUserPreferenceRepository UserPreferences { get; }
    public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions { get; }
    public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries { get; }
    public ILlmUsageRecordRepository LlmUsageRecords { get; }
    public IAgentProfileRepository AgentProfiles { get; }
    public IAgentRunRepository AgentRuns { get; }
    public IKnowledgeDocumentRepository KnowledgeDocuments { get; }
    public IKnowledgeChunkRepository KnowledgeChunks { get; }
    public IExternalLoginRepository ExternalLogins { get; }
    public IOAuthAuthCodeRepository OAuthAuthCodes { get; }
    public IApiKeyRepository ApiKeys { get; }
    public IMfaCredentialRepository MfaCredentials { get; }
    public IIntegrationConnectorRepository IntegrationConnectors { get; }
    public IConnectorEventRepository ConnectorEvents { get; }
    public IConnectorCredentialRepository ConnectorCredentials { get; }
    public IProposalRevisionRepository ProposalRevisions { get; }
    public IDailySnapshotRepository DailySnapshots { get; }
    public ITomorrowNoteRepository TomorrowNotes { get; }
    public IMcpToolHashRepository McpToolHashes { get; }

    public async Task CheckpointWalAsync(CancellationToken cancellationToken = default)
    {
        // Fold committed WAL pages back into the main database file so a file-level
        // snapshot (DB export) is complete. PRAGMA wal_checkpoint(TRUNCATE) returns a row
        // (busy, log, checkpointed); busy=1 means a concurrent reader/writer prevented a
        // full checkpoint, so some committed frames may still live in <db>-wal. Retry a
        // few times, then fail loudly rather than hand back an incomplete snapshot. On a
        // non-WAL or in-memory database the row reports busy=0, so this is a no-op.
        // Open through EF (not the raw DbConnection) so SqlitePragmaConnectionInterceptor
        // installs busy_timeout (and WAL) on the connection before we checkpoint — otherwise
        // a checkpoint contended by another process could fail without the configured wait.
        // EF ref-counts opens, so this is safe whether or not the connection is already open.
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = _context.Database.GetDbConnection();
            for (var attempt = 1; attempt <= MaxWalCheckpointAttempts; attempt++)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return; // no result row -> nothing to checkpoint (non-WAL)

                var busy = reader.GetInt64(0);
                if (busy == 0)
                    return; // all committed frames are now in the main file
            }

            throw new InvalidOperationException(
                "Could not fully checkpoint the SQLite WAL after multiple attempts; another " +
                "Taskdeck process (UI/MCP/CLI) is holding the database. Stop other processes and retry.");
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var resolvedRecoverableUniqueConflict = false;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new DomainException(
                    ErrorCodes.Conflict,
                    "Record was updated by another session. Refresh and retry your action.",
                    ex);
            }
            catch (DbUpdateException ex) when (IsProposalRevisionUniqueViolation(ex))
            {
                throw new DomainException(
                    ErrorCodes.Conflict,
                    "Proposal revision was created by another session. Refresh and retry your edit.",
                    ex);
            }
            catch (DbUpdateException ex) when (IsOperationIdempotencyKeyUniqueViolation(ex))
            {
                throw new DomainException(
                    ErrorCodes.Conflict,
                    "An automation operation with this idempotency key already exists.",
                    ex);
            }
            catch (DbUpdateException ex) when (!resolvedRecoverableUniqueConflict && TryResolveRecoverableUniqueConflicts(ex))
            {
                resolvedRecoverableUniqueConflict = true;
            }
            catch (DbUpdateException ex) when (IsTransientSqliteWriteLock(ex) && attempt < MaxSqliteWriteLockRetries)
            {
                await Task.Delay(GetSqliteWriteLockRetryDelay(attempt), cancellationToken);
            }
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    private bool TryResolveRecoverableUniqueConflicts(DbUpdateException exception)
    {
        var resolvedNotificationConflict = TryResolveDuplicateNotificationDeduplicationConflicts(exception);
        var resolvedUserPreferenceConflict = TryResolveDuplicateUserPreferenceConflicts(exception);
        var resolvedDailySnapshotConflict = TryResolveDuplicateDailySnapshotConflicts(exception);
        var resolvedTomorrowNoteConflict = TryResolveDuplicateTomorrowNoteConflicts(exception);

        return resolvedNotificationConflict
            || resolvedUserPreferenceConflict
            || resolvedDailySnapshotConflict
            || resolvedTomorrowNoteConflict;
    }

    private bool TryResolveDuplicateNotificationDeduplicationConflicts(DbUpdateException exception)
    {
        if (!IsNotificationDeduplicationUniqueViolation(exception))
            return false;

        var duplicateNotificationFound = false;
        var pendingNotifications = _context.ChangeTracker
            .Entries<Notification>()
            .Where(entry => entry.State == EntityState.Added && !string.IsNullOrWhiteSpace(entry.Entity.DeduplicationKey))
            .ToList();

        foreach (var pendingNotification in pendingNotifications)
        {
            var deduplicationKey = pendingNotification.Entity.DeduplicationKey!;
            var duplicateExists = _context.Notifications
                .AsNoTracking()
                .Any(notification =>
                    notification.UserId == pendingNotification.Entity.UserId
                    && notification.DeduplicationKey == deduplicationKey);

            if (!duplicateExists)
                continue;

            pendingNotification.State = EntityState.Detached;
            duplicateNotificationFound = true;
        }

        return duplicateNotificationFound;
    }

    private bool TryResolveDuplicateUserPreferenceConflicts(DbUpdateException exception)
    {
        if (!IsUserPreferenceUniqueViolation(exception))
            return false;

        var duplicatePreferenceFound = false;
        var pendingPreferences = _context.ChangeTracker
            .Entries<UserPreference>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        foreach (var pendingPreference in pendingPreferences)
        {
            var duplicateExists = _context.UserPreferences
                .AsNoTracking()
                .Any(preference => preference.UserId == pendingPreference.Entity.UserId);

            if (!duplicateExists)
                continue;

            pendingPreference.State = EntityState.Detached;
            duplicatePreferenceFound = true;
        }

        return duplicatePreferenceFound;
    }

    private static bool IsNotificationDeduplicationUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        return exception.InnerException.Message.Contains(
            "Notifications.UserId, Notifications.DeduplicationKey",
            StringComparison.OrdinalIgnoreCase)
            || exception.InnerException.Message.Contains(
                "IX_Notifications_UserId_DeduplicationKey",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserPreferenceUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        return exception.InnerException.Message.Contains(
            "UserPreferences.UserId",
            StringComparison.OrdinalIgnoreCase)
            || exception.InnerException.Message.Contains(
                "IX_UserPreferences_UserId",
                StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveDuplicateTomorrowNoteConflicts(DbUpdateException exception)
    {
        if (!IsTomorrowNoteUniqueViolation(exception))
            return false;

        var duplicateNoteFound = false;
        var pendingNotes = _context.ChangeTracker
            .Entries<TomorrowNote>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        foreach (var pendingNote in pendingNotes)
        {
            var duplicateExists = _context.TomorrowNotes
                .AsNoTracking()
                .Any(note =>
                    note.UserId == pendingNote.Entity.UserId
                    && note.Date == pendingNote.Entity.Date);

            if (!duplicateExists)
                continue;

            pendingNote.State = EntityState.Detached;
            duplicateNoteFound = true;
        }

        return duplicateNoteFound;
    }

    private static bool IsTomorrowNoteUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        return exception.InnerException.Message.Contains(
            "TomorrowNotes.UserId, TomorrowNotes.Date",
            StringComparison.OrdinalIgnoreCase)
            || exception.InnerException.Message.Contains(
                "IX_TomorrowNotes_UserId_Date",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProposalRevisionUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        return exception.InnerException.Message.Contains(
            "ProposalRevisions.ProposalId, ProposalRevisions.RevisionNumber",
            StringComparison.OrdinalIgnoreCase)
            || exception.InnerException.Message.Contains(
                "IX_ProposalRevisions_ProposalId_RevisionNumber",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperationIdempotencyKeyUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        var message = exception.InnerException.Message;

        // Match UNIQUE violations only. The bare column name also appears in NOT NULL
        // violation messages ("NOT NULL constraint failed: AutomationProposalOperations.IdempotencyKey"),
        // so require the UNIQUE qualifier (SQLite) or the unique index name (other providers).
        return (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                && message.Contains("AutomationProposalOperations.IdempotencyKey", StringComparison.OrdinalIgnoreCase))
            || message.Contains("IX_AutomationProposalOperations_IdempotencyKey", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveDuplicateDailySnapshotConflicts(DbUpdateException exception)
    {
        if (!IsDailySnapshotUniqueViolation(exception))
            return false;

        var duplicateSnapshotFound = false;
        var pendingSnapshots = _context.ChangeTracker
            .Entries<DailySnapshot>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();

        foreach (var pendingSnapshot in pendingSnapshots)
        {
            var duplicateExists = _context.DailySnapshots
                .AsNoTracking()
                .Any(ds =>
                    ds.UserId == pendingSnapshot.Entity.UserId
                    && ds.Date == pendingSnapshot.Entity.Date);

            if (!duplicateExists)
                continue;

            pendingSnapshot.State = EntityState.Detached;
            duplicateSnapshotFound = true;
        }

        return duplicateSnapshotFound;
    }

    private static bool IsDailySnapshotUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is null)
            return false;

        return exception.InnerException.Message.Contains(
            "DailySnapshots.UserId, DailySnapshots.Date",
            StringComparison.OrdinalIgnoreCase)
            || exception.InnerException.Message.Contains(
                "IX_DailySnapshots_UserId_Date",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientSqliteWriteLock(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException
                && (sqliteException.SqliteErrorCode == 5 || sqliteException.SqliteErrorCode == 6))
            {
                return true;
            }

            if (current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan GetSqliteWriteLockRetryDelay(int attempt)
    {
        var multiplier = attempt + 1;
        return TimeSpan.FromMilliseconds(25 * multiplier * multiplier);
    }
}
