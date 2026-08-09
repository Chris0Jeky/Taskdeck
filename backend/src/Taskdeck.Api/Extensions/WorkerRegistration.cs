using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class WorkerRegistration
{
    public static IServiceCollection AddTaskdeckWorkers(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var workerSettings = configuration.GetSection("Workers").Get<WorkerSettings>() ?? new WorkerSettings();
        services.AddSingleton(workerSettings);

        var outboundWebhookSecuritySection = configuration.GetSection("OutboundWebhooks:Security");
        var outboundWebhookSecuritySettings = outboundWebhookSecuritySection.Get<OutboundWebhookSecuritySettings>() ?? new OutboundWebhookSecuritySettings();
        if (environment.IsDevelopment() && outboundWebhookSecuritySection["AllowLocalhostEndpoints"] is null)
        {
            outboundWebhookSecuritySettings.AllowLocalhostEndpoints = true;
        }
        services.AddSingleton(outboundWebhookSecuritySettings);
        services.TryAddTransient<ProtectedOutboundTelemetryHandler>();
        services.TryAddSingleton<ProtectedOutboundMeterFactory>();

        services.AddHttpClient("OutboundWebhookDelivery", (_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<OutboundWebhookSecuritySettings>();
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ActivityHeadersPropagator = null,
                MeterFactory = serviceProvider.GetRequiredService<ProtectedOutboundMeterFactory>(),
                ConnectCallback = (context, cancellationToken) =>
                    OutboundWebhookConnectCallback.ConnectAsync(
                        context,
                        settings.AllowLocalhostEndpoints,
                        cancellationToken)
            };
        })
        .RemoveAllLoggers()
        .AddHttpMessageHandler<ProtectedOutboundTelemetryHandler>();

        var auditRetentionSettings = configuration.GetSection("AuditRetention").Get<AuditRetentionSettings>() ?? new AuditRetentionSettings();
        services.AddSingleton(auditRetentionSettings);

        var embeddingBackfillSettings = configuration.GetSection("EmbeddingBackfill").Get<EmbeddingBackfillSettings>() ?? new EmbeddingBackfillSettings();
        services.AddSingleton(embeddingBackfillSettings);

        services.AddSingleton<WorkerHeartbeatRegistry>();
        services.AddSingleton<ILlmCaptureTriageProgressReporter>(serviceProvider =>
            serviceProvider.GetRequiredService<WorkerHeartbeatRegistry>());
        services.AddHostedService<LlmQueueToProposalWorker>();
        services.AddHostedService<TranscriptTriageWorker>();
        services.AddHostedService<ProposalHousekeepingWorker>();
        services.AddHostedService<OutboundWebhookDeliveryWorker>();
        services.AddHostedService<AuditRetentionWorker>();
        services.AddHostedService<EmbeddingBackfillWorker>();

        return services;
    }

}
