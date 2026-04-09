using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Middleware;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Extensions;

public static class PipelineConfiguration
{
    public static WebApplication ConfigureTaskdeckPipeline(
        this WebApplication app,
        RateLimitingSettings rateLimitingSettings)
    {
        var forwardedHeadersOptions = BuildForwardedHeadersOptions(app.Configuration);

        if (rateLimitingSettings.Enabled &&
            !app.Environment.IsDevelopment() &&
            forwardedHeadersOptions is null)
        {
            app.Logger.LogWarning(
                "Rate limiting is enabled without trusted forwarded-header configuration. " +
                "If Taskdeck runs behind a reverse proxy/load balancer, AuthPerIp may collapse users into shared buckets. " +
                "Configure ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks.");
        }

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            dbContext.Database.Migrate();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (forwardedHeadersOptions is not null)
        {
            app.UseForwardedHeaders(forwardedHeadersOptions);
        }

        app.UseCors("AllowFrontend");
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<UnhandledExceptionMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // SPA static file serving: serve Vue build output from wwwroot/.
        // Placed after security-headers middleware so that the OnStarting callback registered by
        // SecurityHeadersMiddleware is already in scope when UseStaticFiles short-circuits the pipeline,
        // ensuring CSP, X-Frame-Options, and other headers are applied to SPA assets including index.html.
        // UseDefaultFiles must precede UseStaticFiles so that requests to "/" map to index.html.
        // Directory listing is disabled by default (no DirectoryBrowser registered).
        //
        // Cache-control strategy (PKG-01 AC: "SPA assets served with appropriate cache headers"):
        //   - Versioned/hashed assets (Vite output under /assets/): max-age=1 year + immutable.
        //     Safe because Vite appends a content hash to each filename — stale content is impossible.
        //   - All other files (including index.html): no-cache so the browser always revalidates,
        //     ensuring users pick up new deployments immediately.
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path.Value ?? string.Empty;
                var headers = ctx.Context.Response.Headers;

                if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    // Vite hashes these filenames — safe to cache indefinitely.
                    headers["Cache-Control"] = "public, max-age=31536000, immutable";
                }
                else
                {
                    // index.html and other non-versioned files: revalidate on every request.
                    headers["Cache-Control"] = "no-cache";
                }
            }
        });

        // API key authentication for MCP HTTP transport (/mcp path).
        // Must run before UseAuthentication so MCP requests are handled by API key auth,
        // not JWT auth. Non-MCP requests pass through unaffected.
        app.UseMiddleware<ApiKeyMiddleware>();

        app.UseAuthentication();
        // Reject tokens for deleted/deactivated users or tokens issued before invalidation.
        // Must run after UseAuthentication (so JWT is parsed) and before UseAuthorization.
        app.UseMiddleware<TokenValidationMiddleware>();
        if (rateLimitingSettings.Enabled)
        {
            app.UseRateLimiter();
        }
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<BoardsHub>("/hubs/boards");

        // MCP Streamable HTTP endpoint for external AI agent integration.
        // Authenticated via ApiKeyMiddleware (Bearer tdsk_... tokens).
        // Rate limiting applied per API key user identity.
        var mcpEndpoint = app.MapMcp();
        if (rateLimitingSettings.Enabled)
        {
            mcpEndpoint.RequireRateLimiting(RateLimiting.RateLimitingPolicyNames.McpPerApiKey);
        }

        // SPA fallback: any route not matched by a controller or hub endpoint returns index.html,
        // enabling Vue Router's client-side navigation. API (/api/*) and hub (/hubs/*) routes
        // are matched above and never reach this fallback.
        app.MapFallbackToFile("index.html");

        return app;
    }

    internal static ForwardedHeadersOptions? BuildForwardedHeadersOptions(IConfiguration configuration)
    {
        var knownProxyValues = ResolveConfigValues(configuration, "ForwardedHeaders:KnownProxies");
        var knownNetworkValues = ResolveConfigValues(configuration, "ForwardedHeaders:KnownNetworks");
        if (knownProxyValues.Count == 0 && knownNetworkValues.Count == 0)
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        var configuredForwardLimit = configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit");
        options.ForwardLimit = configuredForwardLimit switch
        {
            null => 1,
            > 0 => configuredForwardLimit,
            _ => throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:ForwardLimit value \"{configuredForwardLimit}\". Expected a positive integer.")
        };

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxyValue in knownProxyValues)
        {
            if (!IPAddress.TryParse(proxyValue, out var proxyAddress))
            {
                throw new InvalidOperationException(
                    $"Invalid ForwardedHeaders:KnownProxies value \"{proxyValue}\". Provide a valid IP address.");
            }

            options.KnownProxies.Add(proxyAddress);
        }

        foreach (var networkValue in knownNetworkValues)
        {
            options.KnownNetworks.Add(ParseForwardedHeaderNetwork(networkValue));
        }

        return options;
    }

    private static IReadOnlyList<string> ResolveConfigValues(IConfiguration configuration, string key)
    {
        var sectionValues = configuration
            .GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (sectionValues.Length > 0)
        {
            return sectionValues;
        }

        var singleValue = configuration[key];
        if (string.IsNullOrWhiteSpace(singleValue))
        {
            return Array.Empty<string>();
        }

        return singleValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseForwardedHeaderNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefixAddress) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefixLength))
        {
            throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownNetworks value \"{value}\". Use CIDR format (for example, 10.0.0.0/24).");
        }

        var maxPrefixLength = prefixAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownNetworks prefix length in \"{value}\". Expected 0-{maxPrefixLength}.");
        }

        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefixAddress, prefixLength);
    }
}
