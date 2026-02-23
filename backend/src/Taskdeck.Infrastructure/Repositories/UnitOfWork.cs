using Microsoft.EntityFrameworkCore.Storage;
using Taskdeck.Application.Interfaces;
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
        INotificationPreferenceRepository notificationPreferences)
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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
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
}
