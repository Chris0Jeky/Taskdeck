namespace Taskdeck.Api.Extensions;

public static class CorsRegistration
{
    public static IServiceCollection AddTaskdeckCors(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var corsAllowedOrigins = ResolveCorsAllowedOrigins(configuration, isDevelopment);

        if (!isDevelopment && corsAllowedOrigins.Count == 0)
        {
            // Fail-closed posture: no production origins configured. We deliberately do NOT fall
            // back to localhost (that would authorize credentialed cross-origin requests from
            // localhost on a real deployment). Emit a loud, guaranteed-visible bootstrap warning —
            // the application logger is not built yet at service-registration time, and this
            // matches the Console.Error bootstrap diagnostics already used in Program.cs.
            Console.Error.WriteLine(
                "[WARN] CORS fail-closed: no production origins configured (Cors:AllowedOrigins). " +
                "All cross-origin browser requests will be denied. Set Cors:AllowedOrigins to your " +
                "frontend origin(s) for a hosted deployment. Same-origin / desktop single-exe is unaffected.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(corsAllowedOrigins.ToArray())
                      .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "X-Request-Id", "X-SignalR-User-Agent", "Idempotency-Key")
                      .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                      .AllowCredentials();
            });
        });

        return services;
    }

    internal static IReadOnlyList<string> ResolveCorsAllowedOrigins(IConfiguration configuration, bool isDevelopment)
    {
        var defaultAllowedOrigins = new[] { "http://localhost:5173", "http://localhost:5174" };

        if (!isDevelopment)
        {
            var productionOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .GetChildren()
                .Select(child => child.Value)
                .OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (productionOrigins.Length == 0 &&
                !string.IsNullOrWhiteSpace(configuration["Cors:AllowedOrigins"]))
            {
                productionOrigins = configuration["Cors:AllowedOrigins"]!
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            // Fail closed: when no production origins are configured we do NOT fall back to the
            // localhost defaults. Falling back would authorize credentialed cross-origin requests
            // (the "AllowFrontend" policy calls AllowCredentials()) from localhost on a real
            // deployment that simply forgot to set Cors:AllowedOrigins. An empty set denies all
            // cross-origin requests until origins are configured explicitly. Same-origin requests
            // (the desktop single-exe serving the SPA from wwwroot) never hit CORS, so this is safe
            // for the local-first posture.
            return productionOrigins
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(NormalizeCorsOrigin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var configuredDevelopmentOrigins = configuration
            .GetSection("Cors:DevelopmentAllowedOrigins")
            .GetChildren()
            .Select(child => child.Value)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (configuredDevelopmentOrigins.Length == 0 &&
            !string.IsNullOrWhiteSpace(configuration["Cors:DevelopmentAllowedOrigins"]))
        {
            configuredDevelopmentOrigins = configuration["Cors:DevelopmentAllowedOrigins"]!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var developmentFallbackOrigins = new[] { "http://localhost:4173", "http://localhost:5001" };

        return defaultAllowedOrigins
            .Concat(configuredDevelopmentOrigins)
            .Concat(developmentFallbackOrigins)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(NormalizeCorsOrigin)
            // Origin host matching is case-insensitive, so collapse mixed-case duplicates.
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeCorsOrigin(string origin)
    {
        var trimmedOrigin = origin.Trim();
        if (!Uri.TryCreate(trimmedOrigin, UriKind.Absolute, out var parsedOrigin))
        {
            throw new InvalidOperationException(
                $"Invalid CORS allowed origin value \"{trimmedOrigin}\". Provide an absolute http(s) origin.");
        }

        if (!string.Equals(parsedOrigin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsedOrigin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Invalid CORS allowed origin value \"{trimmedOrigin}\". Only http and https schemes are supported.");
        }

        return parsedOrigin.GetLeftPart(UriPartial.Authority);
    }
}
