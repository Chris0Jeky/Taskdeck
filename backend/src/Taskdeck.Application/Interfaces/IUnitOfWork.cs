namespace Taskdeck.Application.Interfaces;

public interface IUnitOfWork
{
    IBoardRepository Boards { get; }
    IColumnRepository Columns { get; }
    ICardRepository Cards { get; }
    ICardCommentRepository CardComments { get; }
    ILabelRepository Labels { get; }
    IUserRepository Users { get; }
    IBoardAccessRepository BoardAccesses { get; }
    IAuditLogRepository AuditLogs { get; }
    ILlmQueueRepository LlmQueue { get; }
    IAutomationProposalRepository AutomationProposals { get; }
    IArchiveItemRepository ArchiveItems { get; }
    IChatSessionRepository ChatSessions { get; }
    IChatMessageRepository ChatMessages { get; }
    ICommandRunRepository CommandRuns { get; }
    INotificationRepository Notifications { get; }
    INotificationPreferenceRepository NotificationPreferences { get; }
    IUserPreferenceRepository UserPreferences { get; }
    IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions { get; }
    IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries { get; }
    ILlmUsageRecordRepository LlmUsageRecords { get; }
    IAgentProfileRepository AgentProfiles { get; }
    IAgentRunRepository AgentRuns { get; }
    IKnowledgeDocumentRepository KnowledgeDocuments { get; }
    IKnowledgeChunkRepository KnowledgeChunks { get; }
    IExternalLoginRepository ExternalLogins { get; }
    IOAuthAuthCodeRepository OAuthAuthCodes { get; }
    IApiKeyRepository ApiKeys { get; }
    IMfaCredentialRepository MfaCredentials { get; }
    IIntegrationConnectorRepository IntegrationConnectors { get; }
    IConnectorEventRepository ConnectorEvents { get; }
    IConnectorCredentialRepository ConnectorCredentials { get; }
    IProposalRevisionRepository ProposalRevisions { get; }
    IProposalFeedbackRepository ProposalFeedbacks { get; }
    IDailySnapshotRepository DailySnapshots { get; }
    ITomorrowNoteRepository TomorrowNotes { get; }
    IMcpToolHashRepository McpToolHashes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Folds committed WAL pages back into the main database file so a file-level
    /// snapshot (e.g. the dev-sandbox DB export) is complete. No-op when the journal
    /// mode is not WAL (e.g. an in-memory database).
    /// </summary>
    Task CheckpointWalAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
