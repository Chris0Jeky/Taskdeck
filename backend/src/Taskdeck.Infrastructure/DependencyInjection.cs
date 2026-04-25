using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Connectors;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Connectors;
using Taskdeck.Infrastructure.Connectors;
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

        var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>()
            ?? new DatabaseSettings();

        // Enforce validation for all host modes (API, CLI, MCP).
        // ValidateOnStart causes an exception at startup if CommandTimeoutSeconds
        // is out of the [1, 300] range, regardless of which host runs AddInfrastructure.
        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection("Database"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<TaskdeckDbContext>(options =>
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                // Apply command timeout from configuration (default: 30s).
                // This applies to all EF Core commands including Database.Migrate().
                sqliteOptions.CommandTimeout(databaseSettings.CommandTimeoutSeconds);
            }));

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
        services.AddScoped<IOAuthAuthCodeRepository, OAuthAuthCodeRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IMfaCredentialRepository, MfaCredentialRepository>();
        services.AddScoped<IIntegrationConnectorRepository, IntegrationConnectorRepository>();
        services.AddScoped<IConnectorEventRepository, ConnectorEventRepository>();
        services.AddScoped<IConnectorCredentialRepository, ConnectorCredentialRepository>();
        services.AddScoped<Taskdeck.Infrastructure.Services.KnowledgeFtsSearchService>();
        services.AddScoped<IFtsKnowledgeSearchService>(sp =>
            sp.GetRequiredService<Taskdeck.Infrastructure.Services.KnowledgeFtsSearchService>());

        // Vector index is local; hash-based in-memory embeddings are development/test
        // oriented and stay disabled unless explicitly opted in.
        var enableInMemoryEmbeddings = configuration.GetValue<bool>("Knowledge:EnableInMemoryEmbeddings");
        services.AddSingleton<IVectorIndex, Taskdeck.Infrastructure.Services.InMemoryVectorIndex>();
        if (enableInMemoryEmbeddings)
        {
            services.AddSingleton<IEmbeddingGenerator, Taskdeck.Infrastructure.Services.InMemoryEmbeddingGenerator>();
        }
        else
        {
            services.AddSingleton<IEmbeddingGenerator, Taskdeck.Infrastructure.Services.DisabledEmbeddingGenerator>();
        }
        services.AddScoped<IEmbeddingBackfillService, Taskdeck.Infrastructure.Services.EmbeddingBackfillService>();
        services.AddScoped<Taskdeck.Infrastructure.Services.FallbackSemanticSearchService>();
        services.AddScoped<ISemanticSearchService>(sp =>
            sp.GetRequiredService<Taskdeck.Infrastructure.Services.FallbackSemanticSearchService>());
        services.AddScoped<IKnowledgeSearchService>(sp =>
            sp.GetRequiredService<Taskdeck.Infrastructure.Services.FallbackSemanticSearchService>());

        // Credential encryption — requires a configured AES-256 key.
        // Fail-fast: the service refuses to start without a valid encryption key.
        var credentialEncryptionKey = configuration["Connectors:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(credentialEncryptionKey))
        {
            throw new InvalidOperationException(
                "Connectors:EncryptionKey is not configured. " +
                "Set a base64-encoded 256-bit key via configuration or the " +
                "TASKDECK_CONNECTORS__ENCRYPTIONKEY environment variable. " +
                "Generate one with: openssl rand -base64 32");
        }
        services.AddSingleton<ICredentialEncryptionService>(
            new AesCredentialEncryptionService(credentialEncryptionKey));

        // Connector provider framework (concrete providers registered in Infrastructure).
        // Providers and registry are scoped to align with HttpClient lifetime from
        // AddHttpClient (which registers a transient typed client). Singleton registration
        // would capture a transient HttpClient, causing socket exhaustion.
        services.AddHttpClient<GitHubConnectorProvider>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Taskdeck-Connector/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IConnectorProvider>(sp =>
            sp.GetRequiredService<GitHubConnectorProvider>());
        services.AddScoped<IConnectorProviderRegistry>(sp =>
            new ConnectorProviderRegistry(sp.GetServices<IConnectorProvider>()));

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
                services.AddSingleton<ICacheService>(sp =>
                    new InMemoryCacheService(
                        sp.GetRequiredService<ILogger<InMemoryCacheService>>(),
                        cacheSettings.KeyPrefix));
                break;

            default:
                // Log a warning so operators notice configuration typos (e.g., "Rediss" or "inmem")
                // instead of silently falling back to InMemory.
                services.AddSingleton<ICacheService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<InMemoryCacheService>>();
                    logger.LogWarning(
                        "Unknown cache provider '{Provider}', falling back to InMemory. Valid values: Redis, InMemory, None",
                        cacheSettings.Provider);
                    return new InMemoryCacheService(logger, cacheSettings.KeyPrefix);
                });
                break;
        }

        services.AddSingleton(cacheSettings);
    }
}
