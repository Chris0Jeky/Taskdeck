using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class SettingsRegistration
{
    public static IServiceCollection AddTaskdeckSettings(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        out ObservabilitySettings observabilitySettings,
        out RateLimitingSettings rateLimitingSettings,
        out JwtSettings jwtSettings,
        out GitHubOAuthSettings gitHubOAuthSettings,
        out OidcSettings oidcSettings,
        out SentrySettings sentrySettings,
        out TelemetrySettings telemetrySettings,
        out AnalyticsSettings analyticsSettings)
    {
        observabilitySettings = configuration
            .GetSection("Observability")
            .Get<ObservabilitySettings>() ?? new ObservabilitySettings();
        services.AddSingleton(observabilitySettings);

        rateLimitingSettings = configuration
            .GetSection("RateLimiting")
            .Get<RateLimitingSettings>() ?? new RateLimitingSettings();
        services.AddSingleton(rateLimitingSettings);

        var securityHeadersSection = configuration.GetSection("SecurityHeaders");
        var securityHeadersSettings = securityHeadersSection.Get<SecurityHeadersSettings>() ?? new SecurityHeadersSettings();
        if (environment.IsDevelopment() && securityHeadersSection["EnableHsts"] is null)
        {
            securityHeadersSettings.EnableHsts = false;
        }
        services.AddSingleton(securityHeadersSettings);

        var databaseExportImportSettings = new DatabaseExportImportSettings
        {
            ConnectionString = configuration.GetConnectionString("DefaultConnection"),
            MaxImportBytes = configuration.GetValue<int?>("ExportImport:MaxDatabaseImportBytes")
                ?? DatabaseExportImportSettings.DefaultMaxImportBytes
        };
        services.AddSingleton(databaseExportImportSettings);

        jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        services.AddSingleton(jwtSettings);

        gitHubOAuthSettings = configuration.GetSection("GitHubOAuth").Get<GitHubOAuthSettings>() ?? new GitHubOAuthSettings();
        services.AddSingleton(gitHubOAuthSettings);

        oidcSettings = configuration.GetSection("Oidc").Get<OidcSettings>() ?? new OidcSettings();
        services.AddSingleton(oidcSettings);

        var mfaPolicySettings = configuration.GetSection("MfaPolicy").Get<MfaPolicySettings>() ?? new MfaPolicySettings();
        services.AddSingleton(mfaPolicySettings);

        var sandboxSettings = configuration.GetSection("DevelopmentSandbox").Get<DevelopmentSandboxSettings>() ?? new DevelopmentSandboxSettings();
        sandboxSettings.Enabled = sandboxSettings.Enabled && environment.IsDevelopment();
        services.AddSingleton(sandboxSettings);

        sentrySettings = configuration
            .GetSection("Sentry")
            .Get<SentrySettings>() ?? new SentrySettings();
        services.AddSingleton(sentrySettings);

        telemetrySettings = configuration
            .GetSection("Telemetry")
            .Get<TelemetrySettings>() ?? new TelemetrySettings();
        services.AddSingleton(telemetrySettings);

        analyticsSettings = configuration
            .GetSection("Analytics")
            .Get<AnalyticsSettings>() ?? new AnalyticsSettings();
        services.AddSingleton(analyticsSettings);

        // Register telemetry event service (opt-in guard is internal to the service)
        services.AddSingleton<ITelemetryEventService, TelemetryEventService>();

        return services;
    }
}
