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
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<OutboundWebhookSubscription> OutboundWebhookSubscriptions => Set<OutboundWebhookSubscription>();
    public DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries => Set<OutboundWebhookDelivery>();
    public DbSet<LlmUsageRecord> LlmUsageRecords => Set<LlmUsageRecord>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentRunEvent> AgentRunEvents => Set<AgentRunEvent>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<OAuthAuthCode> OAuthAuthCodes => Set<OAuthAuthCode>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<MfaCredential> MfaCredentials => Set<MfaCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskdeckDbContext).Assembly);
    }
}
