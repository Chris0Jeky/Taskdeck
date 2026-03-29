using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;

namespace Taskdeck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=taskdeck.db";

        services.AddDbContext<TaskdeckDbContext>(options =>
            options
                .UseSqlite(connectionString)
                // EF Core 9 introduced PendingModelChangesWarning which throws when upgrading
                // from EF Core 8 snapshots. EF tooling confirms no actual model changes are
                // pending; suppress to allow startup after the snapshot format migration.
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<IColumnRepository, ColumnRepository>();
        services.AddScoped<ICardRepository, CardRepository>();
        services.AddScoped<ICardCommentRepository, CardCommentRepository>();
        services.AddScoped<ILabelRepository, LabelRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBoardAccessRepository, BoardAccessRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ILlmQueueRepository, LlmQueueRepository>();
        services.AddScoped<IAutomationProposalRepository, AutomationProposalRepository>();
        services.AddScoped<IArchiveItemRepository, ArchiveItemRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
        services.AddScoped<ICommandRunRepository, CommandRunRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
        services.AddScoped<IOutboundWebhookSubscriptionRepository, OutboundWebhookSubscriptionRepository>();
        services.AddScoped<IOutboundWebhookDeliveryRepository, OutboundWebhookDeliveryRepository>();
        services.AddScoped<ILlmUsageRecordRepository, LlmUsageRecordRepository>();
        services.AddScoped<IAgentProfileRepository, AgentProfileRepository>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IKnowledgeChunkRepository, KnowledgeChunkRepository>();
        services.AddScoped<IExternalLoginRepository, ExternalLoginRepository>();
        services.AddScoped<IKnowledgeSearchService, Taskdeck.Infrastructure.Services.KnowledgeFtsSearchService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
