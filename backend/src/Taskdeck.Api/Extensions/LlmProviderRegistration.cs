using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class LlmProviderRegistration
{
    public static IServiceCollection AddLlmProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // LLM quota and kill switch settings
        var llmQuotaSettings = configuration.GetSection("LlmQuota").Get<LlmQuotaSettings>() ?? new LlmQuotaSettings();
        services.AddSingleton(llmQuotaSettings);
        var llmKillSwitchSettings = configuration.GetSection("LlmKillSwitch").Get<LlmKillSwitchSettings>() ?? new LlmKillSwitchSettings();
        services.AddSingleton(llmKillSwitchSettings);
        services.AddScoped<ILlmQuotaService, LlmQuotaService>();
        services.AddSingleton<ILlmKillSwitchService, LlmKillSwitchService>();

        // Abuse detection settings, shared state (singleton), and service (scoped to access ILlmUsageRecordRepository)
        var abuseDetectionSettings = configuration.GetSection("AbuseDetection").Get<AbuseDetectionSettings>() ?? new AbuseDetectionSettings();
        services.AddSingleton(abuseDetectionSettings);
        services.AddSingleton<AbuseDetectionState>();
        services.AddScoped<IAbuseDetectionService>(sp =>
        {
            var settings = sp.GetRequiredService<AbuseDetectionSettings>();
            var state = sp.GetRequiredService<AbuseDetectionState>();
            var usageRecords = sp.GetService<ILlmUsageRecordRepository>();
            return new AbuseDetectionService(settings, state, usageRecords);
        });

        // Tool-calling feature flag and budget settings
        var llmToolCallingSettings = configuration.GetSection("LlmToolCalling").Get<LlmToolCallingSettings>() ?? new LlmToolCallingSettings();
        services.AddSingleton(llmToolCallingSettings);

        // LLM provider settings and deterministic provider selection policy
        var llmProviderSettings = configuration.GetSection("Llm").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
        services.AddSingleton(llmProviderSettings);

        services.AddHttpClient<OpenAiLlmProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.OpenAi?.TimeoutSeconds > 0 ? settings.OpenAi.TimeoutSeconds : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddHttpClient<GeminiLlmProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.Gemini?.TimeoutSeconds > 0 ? settings.Gemini.TimeoutSeconds : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        services.AddScoped<MockLlmProvider>();
        services.AddScoped<ILlmProvider>(sp =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var environment = sp.GetRequiredService<IWebHostEnvironment>();
            var decision = LlmProviderSelectionPolicy.Evaluate(settings, environment.EnvironmentName);

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Taskdeck.Api.LlmProviderSelection");
            logger.LogInformation(
                "Resolved ILlmProvider to {ProviderKind}. Reason: {Reason}",
                decision.ProviderKind,
                decision.Reason);

            return decision.ProviderKind switch
            {
                LlmProviderKind.OpenAi => sp.GetRequiredService<OpenAiLlmProvider>(),
                LlmProviderKind.Gemini => sp.GetRequiredService<GeminiLlmProvider>(),
                _ => sp.GetRequiredService<MockLlmProvider>()
            };
        });

        return services;
    }
}
