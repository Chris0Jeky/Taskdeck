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
        out GitHubOAuthSettings gitHubOAuthSettings)
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

        var sandboxSettings = configuration.GetSection("DevelopmentSandbox").Get<DevelopmentSandboxSettings>() ?? new DevelopmentSandboxSettings();
        sandboxSettings.Enabled = sandboxSettings.Enabled && environment.IsDevelopment();
        services.AddSingleton(sandboxSettings);

        return services;
    }
}
