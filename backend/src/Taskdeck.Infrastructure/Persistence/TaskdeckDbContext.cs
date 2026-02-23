using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence;

public class TaskdeckDbContext : DbContext
{
    public TaskdeckDbContext(DbContextOptions<TaskdeckDbContext> options) : base(options)
    {
    }

    public DbSet<Board> Boards => Set<Board>();
    public DbSet<Column> Columns => Set<Column>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<CardComment> CardComments => Set<CardComment>();
    public DbSet<CardCommentMention> CardCommentMentions => Set<CardCommentMention>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BoardAccess> BoardAccesses => Set<BoardAccess>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LlmRequest> LlmRequests => Set<LlmRequest>();
    public DbSet<AutomationProposal> AutomationProposals => Set<AutomationProposal>();
    public DbSet<AutomationProposalOperation> AutomationProposalOperations => Set<AutomationProposalOperation>();
    public DbSet<ArchiveItem> ArchiveItems => Set<ArchiveItem>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<CommandRun> CommandRuns => Set<CommandRun>();
    public DbSet<CommandRunLog> CommandRunLogs => Set<CommandRunLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskdeckDbContext).Assembly);
    }
}
