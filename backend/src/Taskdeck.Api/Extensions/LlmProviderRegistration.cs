using System.Net.Sockets;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using CircuitState = Taskdeck.Application.Services.CircuitState;

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

        // Circuit breaker settings and shared state tracker (explicit instances
        // so Program.cs can look them up from the service descriptors before build).
        var circuitBreakerSettings = configuration.GetSection("CircuitBreaker").Get<CircuitBreakerSettings>() ?? new CircuitBreakerSettings();
        services.AddSingleton(circuitBreakerSettings);
        var circuitBreakerTracker = new CircuitBreakerStateTracker();
        services.AddSingleton(circuitBreakerTracker);

        // Determine once at startup whether localhost LLM endpoints are permitted.
        // This is true only in development-like environments with AllowLiveProvidersInDevelopment.
        var allowLocalhostLlm = IsLocalhostLlmAllowed(services, configuration);

        services.AddHttpClient<OpenAiLlmProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.OpenAi?.TimeoutSeconds > 0 ? settings.OpenAi.TimeoutSeconds : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(_ =>
        {
            // SSRF protection: DNS-level check prevents connections to private/internal IPs
            // even if the BaseUrl hostname resolves to a private address (DNS rebinding defense).
            // In development with AllowLiveProvidersInDevelopment, localhost is permitted
            // so developers can use local LLM gateways (Ollama, LM Studio, etc.).
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = (context, cancellationToken) =>
                    OutboundWebhookConnectCallback.ConnectAsync(
                        context,
                        allowLocalhostEndpoints: allowLocalhostLlm,
                        cancellationToken)
            };
        })
        .AddPolicyHandler((sp, _) => BuildCircuitBreakerPolicy(sp, "OpenAI", circuitBreakerSettings));
        services.AddHttpClient<GeminiLlmProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.Gemini?.TimeoutSeconds > 0 ? settings.Gemini.TimeoutSeconds : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(_ =>
        {
            // SSRF protection: DNS-level check prevents connections to private/internal IPs
            // even if the BaseUrl hostname resolves to a private address (DNS rebinding defense).
            // In development with AllowLiveProvidersInDevelopment, localhost is permitted.
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = (context, cancellationToken) =>
                    OutboundWebhookConnectCallback.ConnectAsync(
                        context,
                        allowLocalhostEndpoints: allowLocalhostLlm,
                        cancellationToken)
            };
        })
        .AddPolicyHandler((sp, _) => BuildCircuitBreakerPolicy(sp, "Gemini", circuitBreakerSettings));

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

    /// <summary>
    /// Builds a Polly advanced circuit breaker policy for an external HTTP client.
    /// The circuit opens after <see cref="CircuitBreakerSettings.FailureThreshold"/>
    /// consecutive failures and stays open for
    /// <see cref="CircuitBreakerSettings.BreakDurationSeconds"/> seconds before
    /// transitioning to half-open. State transitions are recorded in
    /// <see cref="CircuitBreakerStateTracker"/> for health endpoint visibility.
    /// </summary>
    internal static IAsyncPolicy<HttpResponseMessage> BuildCircuitBreakerPolicy(
        IServiceProvider serviceProvider,
        string circuitName,
        CircuitBreakerSettings settings)
    {
        var tracker = serviceProvider.GetRequiredService<CircuitBreakerStateTracker>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Taskdeck.CircuitBreaker.{circuitName}");

        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408, HttpRequestException
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: settings.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(settings.BreakDurationSeconds),
                onBreak: (outcome, breakDuration) =>
                {
                    var reason = outcome.Exception?.Message ?? $"HTTP {(int)(outcome.Result?.StatusCode ?? 0)}";
                    tracker.RecordState(circuitName, CircuitState.Open, reason);
                    logger.LogWarning(
                        "Circuit breaker '{CircuitName}' opened for {BreakDuration}s after {Threshold} consecutive failures. Last failure: {Reason}",
                        circuitName,
                        breakDuration.TotalSeconds,
                        settings.FailureThreshold,
                        reason);
                },
                onReset: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.Closed);
                    logger.LogInformation(
                        "Circuit breaker '{CircuitName}' closed (reset). Requests will flow normally.",
                        circuitName);
                },
                onHalfOpen: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.HalfOpen);
                    logger.LogInformation(
                        "Circuit breaker '{CircuitName}' half-open. Next request is a probe.",
                        circuitName);
                });
    }

    /// <summary>
    /// Determines whether localhost LLM endpoints should be permitted based on the
    /// hosting environment and LLM provider configuration. Returns true only in
    /// development-like environments when AllowLiveProvidersInDevelopment is enabled,
    /// enabling developers to use local LLM gateways (Ollama, LM Studio, etc.).
    /// </summary>
    private static bool IsLocalhostLlmAllowed(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var llmSettings = configuration.GetSection("Llm").Get<LlmProviderSettings>();
        if (llmSettings is null || !llmSettings.AllowLiveProvidersInDevelopment)
        {
            return false;
        }

        // Check for a registered IWebHostEnvironment to determine if we're in development.
        // During startup the environment is already registered as a singleton.
        var envDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IWebHostEnvironment));
        if (envDescriptor?.ImplementationInstance is IWebHostEnvironment env)
        {
            var name = env.EnvironmentName;
            return name.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Testing", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
