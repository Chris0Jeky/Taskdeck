using Microsoft.Extensions.Options;
using Taskdeck.Api.Validation;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Wires <c>ValidateDataAnnotations()</c> and <c>ValidateOnStart()</c> for all
/// settings classes. This makes the application fail fast on startup if any
/// configuration value violates its data annotations or cross-property rules.
///
/// The existing singleton registrations (via <c>AddSingleton(instance)</c>) are
/// preserved for backward compatibility. This extension adds parallel
/// <c>IOptions&lt;T&gt;</c> registrations that trigger eager validation.
/// </summary>
public static class OptionsValidationRegistration
{
    public static IServiceCollection AddOptionsValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Settings from SettingsRegistration ──────────────────────────────

        services.RegisterValidatedOptions<JwtSettings>(configuration, "Jwt");
        services.RegisterValidatedOptions<RegistrationSettings>(configuration, "Auth:Registration");
        services.RegisterValidatedOptions<ObservabilitySettings>(configuration, "Observability");
        services.RegisterValidatedOptions<RateLimitingSettings>(configuration, "RateLimiting");
        services.RegisterValidatedOptions<SecurityHeadersSettings>(configuration, "SecurityHeaders");
        services.RegisterValidatedOptions<SentrySettings>(configuration, "Sentry");
        services.RegisterValidatedOptions<TelemetrySettings>(configuration, "Telemetry");
        services.RegisterValidatedOptions<AnalyticsSettings>(configuration, "Analytics");
        services.RegisterValidatedOptions<MfaPolicySettings>(configuration, "MfaPolicy");
        services.RegisterValidatedOptions<ArtefactStorageSettings>(configuration, "Artefacts");
        services.RegisterValidatedOptions<ContextFabricSettings>(configuration, "ContextFabric");

        // ── Settings from LlmProviderRegistration ──────────────────────────

        services.RegisterValidatedOptions<LlmProviderSettings>(configuration, "Llm");
        services.RegisterValidatedOptions<LlmQuotaSettings>(configuration, "LlmQuota");
        services.RegisterValidatedOptions<LlmToolCallingSettings>(configuration, "LlmToolCalling");
        services.RegisterValidatedOptions<AbuseDetectionSettings>(configuration, "AbuseDetection");
        services.RegisterValidatedOptions<LlmCaptureTriageSettings>(configuration, "CaptureTriageLlm");

        // ── Settings from WorkerRegistration ────────────────────────────────

        services.RegisterValidatedOptions<WorkerSettings>(configuration, "Workers");
        services.RegisterValidatedOptions<AuditRetentionSettings>(configuration, "AuditRetention");
        services.RegisterValidatedOptions<EmbeddingBackfillSettings>(configuration, "EmbeddingBackfill");

        // ── Settings from CorsRegistration (Cache is used in infrastructure) ─

        services.RegisterValidatedOptions<CacheSettings>(configuration, "Cache");

        // ── Cross-property validators (IValidateOptions<T>) ─────────────────

        services.AddSingleton<IValidateOptions<WorkerSettings>, WorkerSettingsValidator>();
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        services.AddSingleton<IValidateOptions<SentrySettings>, SentrySettingsValidator>();
        services.AddSingleton<IValidateOptions<RateLimitingSettings>, RateLimitingSettingsValidator>();

        return services;
    }

    private static void RegisterValidatedOptions<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName) where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
