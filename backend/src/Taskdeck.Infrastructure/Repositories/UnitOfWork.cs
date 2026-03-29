using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
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
        IExternalLoginRepository externalLogins)
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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (TryResolveRecoverableUniqueConflicts(ex))
        {
            return await _context.SaveChangesAsync(cancellationToken);
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

        return resolvedNotificationConflict || resolvedUserPreferenceConflict;
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
}
