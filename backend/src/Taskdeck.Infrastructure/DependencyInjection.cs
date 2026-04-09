using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Taskdeck.Infrastructure.Services;

namespace Taskdeck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=taskdeck.db";

        services.AddDbContext<TaskdeckDbContext>(options =>
            options.UseSqlite(connectionString));

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

        // Cache service registration
        services.AddCacheService(configuration);

        return services;
    }

    private static void AddCacheService(this IServiceCollection services, IConfiguration configuration)
    {
        var cacheSettings = configuration.GetSection("Cache").Get<CacheSettings>() ?? new CacheSettings();

        switch (cacheSettings.Provider.ToLowerInvariant())
        {
            case "redis":
                if (string.IsNullOrWhiteSpace(cacheSettings.RedisConnectionString))
                {
                    // Fallback to in-memory if Redis is configured but no connection string
                    services.AddSingleton<ICacheService>(sp =>
                        new InMemoryCacheService(
                            sp.GetRequiredService<ILogger<InMemoryCacheService>>(),
                            cacheSettings.KeyPrefix));
                }
                else
                {
                    services.AddSingleton<ICacheService>(sp =>
                        new RedisCacheService(
                            cacheSettings.RedisConnectionString,
                            sp.GetRequiredService<ILogger<RedisCacheService>>(),
                            cacheSettings.KeyPrefix));
                }
                break;

            case "none":
                services.AddSingleton<ICacheService>(NoOpCacheService.Instance);
                break;

            case "inmemory":
            default:
                services.AddSingleton<ICacheService>(sp =>
                    new InMemoryCacheService(
                        sp.GetRequiredService<ILogger<InMemoryCacheService>>(),
                        cacheSettings.KeyPrefix));
                break;
        }

        services.AddSingleton(cacheSettings);
    }
}
