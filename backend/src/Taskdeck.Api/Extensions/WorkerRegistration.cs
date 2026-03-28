using Taskdeck.Api.Workers;
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
                ConnectCallback = (context, cancellationToken) =>
                    OutboundWebhookConnectCallback.ConnectAsync(
                        context,
                        settings.AllowLocalhostEndpoints,
                        cancellationToken)
            };
        });

        services.AddSingleton<WorkerHeartbeatRegistry>();
        services.AddHostedService<LlmQueueToProposalWorker>();
        services.AddHostedService<ProposalHousekeepingWorker>();
        services.AddHostedService<OutboundWebhookDeliveryWorker>();

        return services;
    }
}
