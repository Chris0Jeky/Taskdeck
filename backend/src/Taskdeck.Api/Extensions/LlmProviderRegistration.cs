using System.Net.Sockets;
using Polly;
using Polly.Extensions.Http;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using CircuitState = Taskdeck.Application.Services.CircuitState;

namespace Taskdeck.Api.Extensions;

public static class LlmProviderRegistration
{
    internal const string OpenAiCompatibleHttpClientName = "OpenAiCompatibleLlmProvider";

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

        // LLM-backed transcript triage (REVIVAL-08 M1): the extraction leg CaptureTriageService
        // consults for transcript-source captures. Scoped because it depends on the scoped
        // ILlmProvider and ILlmQuotaService.
        var llmCaptureTriageSettings = configuration.GetSection("CaptureTriageLlm").Get<LlmCaptureTriageSettings>() ?? new LlmCaptureTriageSettings();
        services.AddSingleton(llmCaptureTriageSettings);
        services.AddScoped<ILlmCaptureTriageExtractor, LlmCaptureTriageExtractor>();

        // LLM provider settings and deterministic provider selection policy
        var llmProviderSettings = configuration.GetSection("Llm").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
        services.AddSingleton(llmProviderSettings);

        // Circuit breaker settings and shared state tracker (explicit instances
        // so Program.cs can look them up from the service descriptors before build).
        var circuitBreakerSettings = configuration.GetSection("CircuitBreaker").Get<CircuitBreakerSettings>() ?? new CircuitBreakerSettings();
        if (circuitBreakerSettings.FailureThreshold < 1)
        {
            throw new InvalidOperationException(
                $"CircuitBreaker:FailureThreshold must be at least 1 (got {circuitBreakerSettings.FailureThreshold}). " +
                "Polly requires a positive value for handledEventsAllowedBeforeBreaking.");
        }
        if (circuitBreakerSettings.BreakDurationSeconds < 1)
        {
            throw new InvalidOperationException(
                $"CircuitBreaker:BreakDurationSeconds must be at least 1 (got {circuitBreakerSettings.BreakDurationSeconds}).");
        }
        services.AddSingleton(circuitBreakerSettings);
        var circuitBreakerTracker = new CircuitBreakerStateTracker();
        services.AddSingleton(circuitBreakerTracker);

        // Create circuit breaker policies once so the same policy instance is reused
        // across all HTTP requests for each provider. This is critical — Polly tracks
        // consecutive failures per policy instance, so creating a new policy per
        // request would defeat circuit breaking entirely.
        var openAiCircuitBreakerPolicy = BuildCircuitBreakerPolicy(
            circuitBreakerTracker, "OpenAI", circuitBreakerSettings);
        var geminiCircuitBreakerPolicy = BuildCircuitBreakerPolicy(
            circuitBreakerTracker, "Gemini", circuitBreakerSettings);
        var ollamaCircuitBreakerPolicy = BuildCircuitBreakerPolicy(
            circuitBreakerTracker, "Ollama", circuitBreakerSettings);
        var openAiCompatibleCircuitBreakerPolicy = BuildCircuitBreakerPolicy(
            circuitBreakerTracker, "OpenAICompatible", circuitBreakerSettings);

        // Determine once at startup whether localhost LLM endpoints are permitted.
        // This is true only in development-like environments with AllowLiveProvidersInDevelopment.
        var localhostPolicy = ResolveLocalhostPolicy(services, configuration);
        var egressRegistry = GetOrCreateEgressRegistry(services);
        RegisterOpenAiCompatibleEgress(
            egressRegistry,
            llmProviderSettings,
            localhostPolicy.AllowGeneralProviderLocalhost);

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
            return CreateProtectedSocketsHttpHandler(localhostPolicy.AllowGeneralProviderLocalhost);
        })
        .AddPolicyHandler(openAiCircuitBreakerPolicy);
        services.AddHttpClient(OpenAiCompatibleHttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.OpenAiCompatible?.TimeoutSeconds > 0 ? settings.OpenAiCompatible.TimeoutSeconds : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(_ =>
        {
            // An arbitrary compatible origin is validated directly; do not allow a
            // system proxy to replace that destination under ConnectCallback.
            return CreateProtectedSocketsHttpHandler(
                localhostPolicy.AllowGeneralProviderLocalhost,
                disableSystemProxy: true);
        })
        .AddHttpMessageHandler(sp => new EgressEnvelopeHandler(
            sp.GetRequiredService<IEgressRegistry>(),
            sp.GetRequiredService<ILogger<EgressEnvelopeHandler>>(),
            nameof(OpenAiCompatibleLlmProvider),
            followRedirects: false))
        .AddPolicyHandler(openAiCompatibleCircuitBreakerPolicy);
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
            return CreateProtectedSocketsHttpHandler(localhostPolicy.AllowGeneralProviderLocalhost);
        })
        .AddPolicyHandler(geminiCircuitBreakerPolicy);
        services.AddHttpClient<OllamaLlmProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LlmProviderSettings>();
            var timeoutSeconds = settings.Ollama?.TimeoutSeconds > 0 ? settings.Ollama.TimeoutSeconds : 120;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(_ =>
        {
            return CreateProtectedSocketsHttpHandler(localhostPolicy.AllowOllamaLocalhost);
        })
        .AddPolicyHandler(ollamaCircuitBreakerPolicy);

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
                LlmProviderKind.OpenAiCompatible => CreateOpenAiCompatibleProvider(
                    sp,
                    settings,
                    circuitBreakerTracker,
                    circuitBreakerSettings,
                    localhostPolicy.AllowGeneralProviderLocalhost),
                LlmProviderKind.Gemini => sp.GetRequiredService<GeminiLlmProvider>(),
                LlmProviderKind.Ollama => sp.GetRequiredService<OllamaLlmProvider>(),
                _ => sp.GetRequiredService<MockLlmProvider>()
            };
        });

        return services;
    }

    internal static SocketsHttpHandler CreateProtectedSocketsHttpHandler(
        bool allowLocalhostEndpoints,
        bool disableSystemProxy = false)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            // The compatible client opts out of system proxies because an arbitrary
            // configured origin must be the endpoint validated by ConnectCallback.
            // Existing fixed-origin providers retain their established proxy behavior.
            UseProxy = !disableSystemProxy,
            ConnectCallback = (context, cancellationToken) =>
                OutboundWebhookConnectCallback.ConnectAsync(
                    context,
                    allowLocalhostEndpoints,
                    cancellationToken)
        };
    }

    private static OpenAiCompatibleLlmProvider CreateOpenAiCompatibleProvider(
        IServiceProvider services,
        LlmProviderSettings settings,
        CircuitBreakerStateTracker circuitBreakerTracker,
        CircuitBreakerSettings circuitBreakerSettings,
        bool allowLocalhostEndpoints)
    {
        RegisterOpenAiCompatibleEgress(
            services.GetRequiredService<IEgressRegistry>(),
            settings,
            allowLocalhostEndpoints);
        return new OpenAiCompatibleLlmProvider(
            services.GetRequiredService<IHttpClientFactory>().CreateClient(OpenAiCompatibleHttpClientName),
            settings,
            services.GetRequiredService<ILogger<OpenAiCompatibleLlmProvider>>(),
            circuitBreakerTracker,
            circuitBreakerSettings,
            allowLocalhostEndpoints);
    }

    private static IEgressRegistry GetOrCreateEgressRegistry(IServiceCollection services)
    {
        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEgressRegistry));
        if (existing?.ImplementationInstance is IEgressRegistry registry)
            return registry;

        var created = new EgressRegistry();
        if (existing is null)
            services.AddSingleton<IEgressRegistry>(created);
        return created;
    }

    internal static void RegisterOpenAiCompatibleEgress(
        IEgressRegistry registry,
        LlmProviderSettings settings,
        bool allowLocalhostEndpoints)
    {
        if (!LlmProviderSelectionPolicy.TryValidateOpenAiCompatibleSettings(
                settings,
                out _,
                allowLocalhostEndpoints) ||
            !Uri.TryCreate(settings.OpenAiCompatible.BaseUrl, UriKind.Absolute, out var endpoint))
            return;

        if (registry.GetAllEntries().Any(entry =>
                string.Equals(entry.Host.TrimEnd('.'), endpoint.Host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.ToolOrAgentName, nameof(OpenAiCompatibleLlmProvider), StringComparison.Ordinal)))
            return;

        registry.Register(new EgressEntry(
            endpoint.Host,
            "LLM prompt with board context and user input",
            nameof(OpenAiCompatibleLlmProvider),
            EgressDataClassification.UserContent));
    }

    /// <summary>
    /// Builds a Polly circuit breaker policy for an external HTTP client.
    /// The circuit opens after <see cref="CircuitBreakerSettings.FailureThreshold"/>
    /// consecutive failures and stays open for
    /// <see cref="CircuitBreakerSettings.BreakDurationSeconds"/> seconds before
    /// transitioning to half-open. State transitions are recorded in
    /// <see cref="CircuitBreakerStateTracker"/> for health endpoint visibility.
    /// <para>
    /// Important: The returned policy must be reused across all requests for the
    /// same HTTP client. Polly tracks consecutive failures per policy instance,
    /// so creating a new policy per request would defeat circuit breaking.
    /// </para>
    /// </summary>
    internal static IAsyncPolicy<HttpResponseMessage> BuildCircuitBreakerPolicy(
        CircuitBreakerStateTracker tracker,
        string circuitName,
        CircuitBreakerSettings settings)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408, HttpRequestException
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: settings.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(settings.BreakDurationSeconds),
                onBreak: (outcome, breakDuration) =>
                {
                    var reason = outcome.Exception?.Message ?? $"HTTP {(int)(outcome.Result?.StatusCode ?? 0)}";
                    tracker.RecordState(circuitName, CircuitState.Open, reason);
                },
                onReset: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.Closed);
                },
                onHalfOpen: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.HalfOpen);
                });
    }

    /// <summary>
    /// Determines whether localhost LLM endpoints should be permitted based on the
    /// hosting environment and LLM provider configuration. Returns true only in
    /// development-like environments when AllowLiveProvidersInDevelopment is enabled,
    /// enabling developers to use local LLM gateways (Ollama, LM Studio, etc.).
    /// </summary>
    internal static LlmProviderLocalhostPolicy ResolveLocalhostPolicy(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var allowGeneralProviderLocalhost = IsLocalhostLlmAllowed(services, configuration);
        return new LlmProviderLocalhostPolicy(
            AllowGeneralProviderLocalhost: allowGeneralProviderLocalhost,
            AllowOllamaLocalhost: IsOllamaLocalhostLlmAllowed(configuration, allowGeneralProviderLocalhost));
    }

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

    private static bool IsOllamaLocalhostLlmAllowed(
        IConfiguration configuration,
        bool allowGeneralProviderLocalhost)
    {
        var llmSettings = configuration.GetSection("Llm").Get<LlmProviderSettings>();
        return llmSettings?.Ollama?.AllowLocalhostEndpoints == true &&
               allowGeneralProviderLocalhost;
    }
}

internal readonly record struct LlmProviderLocalhostPolicy(
    bool AllowGeneralProviderLocalhost,
    bool AllowOllamaLocalhost);
