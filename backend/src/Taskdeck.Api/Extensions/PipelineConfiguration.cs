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

        // Apply EF Core migrations serialized across processes via a cross-process file lock
        // so concurrent API/MCP/CLI startups apply the schema exactly once (#1164).
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            SerializedMigrator.Migrate(dbContext, app.Logger);
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

        // Response compression must run before the application middlewares that produce
        // compressible payloads (controllers, static files, SPA fallback, SignalR
        // negotiation JSON) so their responses are emitted with the negotiated
        // Content-Encoding when the client sends Accept-Encoding. Swagger UI
        // (Development-only, above) serves its own bundled assets before this point and
        // is intentionally left uncompressed — it is a dev-convenience surface and its
        // latency is not worth the extra CPU.
        //
        // Placed before CORS/security headers so downstream middlewares can still
        // append response headers: ASP.NET Core's response compression buffers the body
        // and flushes headers together, so later `Headers[...] = ...` calls on the
        // HttpResponse still take effect.
        app.UseResponseCompression();

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

        // MCP telemetry middleware: structured logging, spans, and metrics for /mcp requests.
        // Runs before ApiKeyMiddleware so it captures all requests including those
        // rejected with 401 (missing/invalid/revoked API keys).
        app.UseMiddleware<Taskdeck.Api.Mcp.McpTelemetryMiddleware>();

        // Bound the cost of MCP authentication FAILURES by trusted client address: reject before a
        // key parse or database lookup once the address's failure budget is spent, but let valid
        // requests through without consuming so they reach the per-key endpoint policy with
        // independent budgets. UseForwardedHeaders (above, when configured) has already corrected
        // Connection.RemoteIpAddress, so the budget keys on the real client behind a proxy.
        if (rateLimitingSettings.Enabled)
        {
            app.UseMiddleware<McpAuthenticationRateLimitingMiddleware>();
        }

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

        // Bare root "/": in production, UseDefaultFiles + UseStaticFiles (above) rewrites "/" to
        // "/index.html" and serves it before routing. When wwwroot is absent (dev/test without a
        // frontend build), that middleware no-ops and the request reaches routing. The {*path:nonfile}
        // catch-all in MapFallbackToFile does not match the empty path, so this explicit endpoint
        // prevents "/" from hitting the global RequireAuthenticatedUser FallbackPolicy (#1181).
        var webRootProvider = app.Environment.WebRootFileProvider;
        app.MapGet("/", async (HttpContext ctx) =>
        {
            var file = webRootProvider.GetFileInfo("index.html");
            if (!file.Exists)
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.SendFileAsync(file);
        }).AllowAnonymous();

        // MCP Streamable HTTP endpoint for external AI agent integration.
        // Authenticated via ApiKeyMiddleware (Bearer tdsk_... tokens), which also enforces the
        // per-key request budget (McpPerApiKey) before the user lookup and last-used write (#1384).
        app.MapTaskdeckMcpEndpoint();
        // MCP is authenticated by ApiKeyMiddleware (above): it 401s missing/invalid/revoked keys
        // before routing and sets an authenticated principal for valid keys, which satisfies the
        // global FallbackPolicy (#1132 AC4). The MCP SDK endpoint does not honor an
        // .AllowAnonymous() convention, so the principal — not endpoint metadata — is the opt-in.
        // SPA fallback: any route not matched by a controller or hub endpoint returns index.html,
        // enabling Vue Router's client-side navigation. API (/api/*) and hub (/hubs/*) routes
        // are matched above and never reach this fallback. AllowAnonymous so the app shell loads for
        // unauthenticated users (e.g. to reach the login page) under the global FallbackPolicy (#1132 AC4).
        app.MapFallbackToFile("index.html").AllowAnonymous();

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
