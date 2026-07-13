using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Taskdeck.Domain.Agents;
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
    public DbSet<IntegrationConnector> IntegrationConnectors => Set<IntegrationConnector>();
    public DbSet<ConnectorEvent> ConnectorEvents => Set<ConnectorEvent>();
    public DbSet<ConnectorCredential> ConnectorCredentials => Set<ConnectorCredential>();
    public DbSet<ProposalRevision> ProposalRevisions => Set<ProposalRevision>();
    public DbSet<ProposalOutcome> ProposalOutcomes => Set<ProposalOutcome>();
    public DbSet<ProposalFeedback> ProposalFeedbacks => Set<ProposalFeedback>();
    public DbSet<ProposalProvenance> ProposalProvenances => Set<ProposalProvenance>();
    public DbSet<ProvenanceField> ProvenanceFields => Set<ProvenanceField>();
    public DbSet<ProvenanceEvidenceLink> ProvenanceEvidenceLinks => Set<ProvenanceEvidenceLink>();
    public DbSet<DailySnapshot> DailySnapshots => Set<DailySnapshot>();
    public DbSet<TomorrowNote> TomorrowNotes => Set<TomorrowNote>();
    public DbSet<McpToolHash> McpToolHashes => Set<McpToolHash>();
    public DbSet<RegistrationBootstrap> RegistrationBootstraps => Set<RegistrationBootstrap>();
    public DbSet<RegistrationInvite> RegistrationInvites => Set<RegistrationInvite>();

    /// <summary>
    /// SQLite stores DateTime as TEXT without timezone info. EF Core materializes
    /// these as DateTimeKind.Unspecified, which causes incorrect comparisons with
    /// DateTime.UtcNow. Apply UTC normalization globally via conventions so every
    /// DateTime property is materialized with DateTimeKind.Utc, and a Local-kind value supplied
    /// on write is normalized to UTC. Raw-SQL paths (AuditLogRepository / OAuthAuthCodeRepository)
    /// bypass conventions and hand-format UTC instead.
    /// See: https://github.com/Chris0Jeky/Taskdeck/issues/1191 and
    /// docs/decisions/ADR-0040-utc-datetime-materialization-convention.md
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskdeckDbContext).Assembly);
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            // Write: the writer contract is "supply UTC". A Local-kind value (for example a
            // future System.Text.Json binding of an offset-bearing payload) would otherwise be
            // stored as local wall-time and re-read stamped Utc -- silently wrong. Normalize
            // Local to UTC on write; leave Utc/Unspecified untouched (behavior-preserving for
            // all current writers, which use DateTime.UtcNow).
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
            // Read: SQLite returns DateTimeKind.Unspecified; stamp it Utc.
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter() : base(
            v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
    }
}
