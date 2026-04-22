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

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection("Jwt"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ObservabilitySettings>()
            .Bind(configuration.GetSection("Observability"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitingSettings>()
            .Bind(configuration.GetSection("RateLimiting"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityHeadersSettings>()
            .Bind(configuration.GetSection("SecurityHeaders"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SentrySettings>()
            .Bind(configuration.GetSection("Sentry"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TelemetrySettings>()
            .Bind(configuration.GetSection("Telemetry"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AnalyticsSettings>()
            .Bind(configuration.GetSection("Analytics"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MfaPolicySettings>()
            .Bind(configuration.GetSection("MfaPolicy"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Settings from LlmProviderRegistration ──────────────────────────

        services.AddOptions<LlmProviderSettings>()
            .Bind(configuration.GetSection("Llm"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LlmQuotaSettings>()
            .Bind(configuration.GetSection("LlmQuota"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LlmToolCallingSettings>()
            .Bind(configuration.GetSection("LlmToolCalling"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AbuseDetectionSettings>()
            .Bind(configuration.GetSection("AbuseDetection"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Settings from WorkerRegistration ────────────────────────────────

        services.AddOptions<WorkerSettings>()
            .Bind(configuration.GetSection("Workers"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Settings from CorsRegistration (Cache is used in infrastructure) ─

        services.AddOptions<CacheSettings>()
            .Bind(configuration.GetSection("Cache"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Cross-property validators (IValidateOptions<T>) ─────────────────

        services.AddSingleton<IValidateOptions<WorkerSettings>, WorkerSettingsValidator>();
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        services.AddSingleton<IValidateOptions<SentrySettings>, SentrySettingsValidator>();
        services.AddSingleton<IValidateOptions<RateLimitingSettings>, RateLimitingSettingsValidator>();

        return services;
    }
}
