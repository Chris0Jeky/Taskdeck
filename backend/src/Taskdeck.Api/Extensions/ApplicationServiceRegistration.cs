using Taskdeck.Api.Realtime;
using Taskdeck.Api.Services;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Api.Extensions;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<BoardService>(sp =>
            new BoardService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetService<IAuthorizationService>(),
                sp.GetService<IBoardRealtimeNotifier>(),
                sp.GetService<IHistoryService>(),
                sp.GetService<ICacheService>(),
                sp.GetService<CacheSettings>()));
        services.AddScoped<ColumnService>();
        services.AddScoped<CardService>();
        services.AddScoped<CardCommentService>();
        services.AddScoped<LabelService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<AuthorizationService>();
        services.AddScoped<IAuthorizationService>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<UserService>();
        services.AddScoped<BoardAccessService>();
        services.AddScoped<IBoardJsonExportImportService, BoardJsonExportImportService>();
        services.AddScoped<IDatabaseFileExportImportService, DatabaseFileExportImportService>();
        services.AddScoped<IExportImportService>(sp =>
            new ExportImportService(
                sp.GetRequiredService<IBoardJsonExportImportService>(),
                sp.GetRequiredService<IDatabaseFileExportImportService>()));
        services.AddScoped<IExternalImportService, ExternalImportService>();
        services.AddScoped<IExternalImportAdapter, CsvExternalImportAdapter>();
        services.AddScoped<LlmQueueService>();
        services.AddScoped<ICaptureService, CaptureService>();
        services.AddScoped<ICaptureTriageService, CaptureTriageService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<IHistoryService>(sp => sp.GetRequiredService<HistoryService>());
        services.AddScoped<IAutomationProposalService, AutomationProposalService>();
        services.AddScoped<IAutomationPolicyEngine, AutomationPolicyEngine>();
        services.AddScoped<IAutomationPlannerService, AutomationPlannerService>();
        services.AddScoped<IAutomationExecutorService, AutomationExecutorService>();
        services.AddScoped<IArchiveRecoveryService, ArchiveRecoveryService>();
        services.AddScoped<IOpsCliService, OpsCliService>();
        services.AddScoped<IBoardContextBuilder, BoardContextBuilder>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<ILogQueryService, LogQueryService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IStarterPackManifestValidator, StarterPackManifestValidator>();
        services.AddScoped<IStarterPackApplyService, StarterPackApplyService>();
        services.AddScoped<IStarterPackCatalogService, StarterPackCatalogService>();
        services.AddScoped<IOutboundWebhookService, OutboundWebhookService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddSingleton<InMemoryActiveUserCache>();
        services.AddSingleton<IActiveUserCache>(sp => sp.GetRequiredService<InMemoryActiveUserCache>());
        services.AddScoped<IBoardMetricsService>(sp =>
            new BoardMetricsService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IAuthorizationService>()));
        services.AddScoped<ApiKeyService>();
        services.AddScoped<IForecastingService>(sp =>
            new ForecastingService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IAuthorizationService>()));
        services.AddScoped<IMetricsExportService>(sp =>
            new MetricsExportService(
                sp.GetRequiredService<IBoardMetricsService>()));
        services.AddScoped<AgentProfileService>();
        services.AddScoped<AgentRunService>();
        services.AddScoped<SignalRBoardRealtimeNotifier>();
        services.AddScoped<WebhookBoardMutationNotifier>();
        services.AddScoped<IBoardRealtimeNotifier, CompositeBoardRealtimeNotifier>();
        services.AddSingleton<IBoardPresenceTracker, InMemoryBoardPresenceTracker>();

        // Agent tool registry (singleton — populated once at startup, read concurrently)
        var toolRegistry = new TaskdeckToolRegistry();
        toolRegistry.RegisterTool(InboxTriageAssistant.GetToolDefinition());
        services.AddSingleton<ITaskdeckToolRegistry>(toolRegistry);

        // Agent policy evaluator and inbox triage assistant
        services.AddScoped<IAgentPolicyEvaluator, AgentPolicyEvaluator>();
        services.AddScoped<InboxTriageAssistant>();

        // Tool-calling infrastructure (read tools)
        services.AddScoped<IToolExecutor, ListBoardColumnsExecutor>();
        services.AddScoped<IToolExecutor, ListCardsInColumnExecutor>();
        services.AddScoped<IToolExecutor, GetCardDetailsExecutor>();
        services.AddScoped<IToolExecutor, SearchCardsExecutor>();
        services.AddScoped<IToolExecutor, GetBoardLabelsExecutor>();

        // Tool-calling infrastructure (write tools — always produce proposals, GP-06)
        services.AddScoped<IToolExecutor, ProposeCreateCardExecutor>();
        services.AddScoped<IToolExecutor, ProposeMoveCardExecutor>();
        services.AddScoped<IToolExecutor, ProposeArchiveCardExecutor>();
        services.AddScoped<IToolExecutor, ProposeUpdateCardExecutor>();
        services.AddScoped<IToolExecutor, ProposeBulkMoveExecutor>();
        services.AddScoped<IToolExecutor, ProposeCreateColumnExecutor>();

        services.AddScoped<ToolExecutorRegistry>(sp =>
            new ToolExecutorRegistry(sp.GetServices<IToolExecutor>()));
        services.AddScoped<IToolStatusNotifier, SignalRToolStatusNotifier>();
        services.AddScoped<ToolCallingChatOrchestrator>();

        return services;
    }
}
