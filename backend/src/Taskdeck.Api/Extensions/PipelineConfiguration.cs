using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Routing;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Extensions;

public static class PipelineConfiguration
{
    /// <summary>
    /// Request-path prefixes owned by machine-facing surfaces — the REST API, SignalR hubs, health
    /// probes, and the MCP HTTP transport — rather than by the Vue SPA. An unmatched path under one
    /// of these is a wrong path, never a client-side route, so it is answered with a 404 error
    /// contract instead of the SPA shell (#1971).
    /// </summary>
    internal static readonly string[] NonSpaPathPrefixes = ["/api", "/hubs", "/health", "/mcp"];

    /// <summary>
    /// The HTTP methods <c>MapFallbackToFile</c> stamps on the SPA catch-all (measured: pattern
    /// <c>{*path:nonfile}</c>, methods <c>[GET, HEAD]</c>). The per-prefix machine-path fallbacks
    /// match the same set so that a wrong-verb request on a real route is still routed to the
    /// framework's 405 endpoint for every verb outside it (#1971).
    /// </summary>
    private static readonly string[] SpaFallbackHttpMethods = ["GET", "HEAD"];

    /// <summary>
    /// The display name ASP.NET Core's <c>HttpMethodMatcherPolicy</c> gives the metadata-less
    /// endpoint it synthesizes when every routing candidate is method-mismatched (pinned on .NET 8;
    /// the anonymous wrong-verb cases in <c>SpaFallbackRoutingApiTests</c> fail loudly if a
    /// framework update renames it, because the requests fall back to answering 401).
    /// </summary>
    private const string Http405EndpointDisplayName = "405 HTTP Method Not Supported";

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
        // so concurrent API/MCP/CLI startups apply the schema exactly once (#1164), taking a
        // fail-closed snapshot of the SQLite file first when migrations are pending (#1803).
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var backupSettings = scope.ServiceProvider
                .GetRequiredService<IOptions<DatabaseBackupSettings>>().Value;
            SerializedMigrator.Migrate(dbContext, backupSettings, app.Logger);
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

        // Routing (auto-inserted at the head of the pipeline) resolves a wrong-verb request whose
        // every candidate is method-mismatched to a synthetic 405 endpoint that carries NO metadata,
        // so the global FallbackPolicy would answer it 401 for anonymous callers before the
        // correction middleware below ever saw the 405 — an anonymous GET typo said 404 (the
        // machine-path catch-alls are AllowAnonymous) while the same PUT typo said 401. On machine
        // paths the endpoint is replaced with an equivalent one that opts out of the fallback
        // policy, so the 404/405 contract is verb-independent for exactly the unauthenticated
        // scripts and probes it exists for. The conjunction below can never match a real endpoint
        // (those are RouteEndpoints and carry metadata), and route existence is already disclosed
        // anonymously — see the fallback comment further down; this discloses nothing further.
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();
            if (endpoint is { RequestDelegate: not null } and not RouteEndpoint &&
                endpoint.Metadata.Count == 0 &&
                string.Equals(endpoint.DisplayName, Http405EndpointDisplayName, StringComparison.Ordinal) &&
                IsMachineFacingPath(context.Request.Path))
            {
                context.SetEndpoint(new Endpoint(
                    endpoint.RequestDelegate,
                    new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                    endpoint.DisplayName));
            }

            await next(context);
        });

        app.UseAuthorization();

        // Machine-path 404/405 contract (#1971, #1992). One resolver answers "is there a real endpoint
        // at this path, and with which verbs?" for both halves of it: this middleware, which owns every
        // verb routing itself rejects, and the per-prefix fallbacks below, which own GET/HEAD.
        var machineRouteMethods = new MachineRouteMethodResolver(
            ((IEndpointRouteBuilder)app).DataSources,
            app.Services.GetRequiredService<ParameterPolicyFactory>(),
            NonSpaPathPrefixes,
            app.Logger);

        // Registered before the endpoint middleware WebApplication appends, so awaiting next() here
        // resumes after the endpoint ran. Routing's own 405 endpoint only sets a status code, so the
        // response has not started and both the status and the body are still ours to correct.
        //
        // Two corrections, both caused by the GET/HEAD catch-alls below sharing a routing node with the
        // real endpoints:
        //   * Unknown machine path, non-GET verb -> routing answers a bodyless 405 with "Allow: GET,
        //     HEAD" because the catch-all made the node method-constrained. Nothing is routed there at
        //     all, so the honest answer is the same 404 error contract every other unknown machine path
        //     gets. #1971 recorded this as an accepted trade; it is the same "false success shape,
        //     different status" the issue is about, so it is corrected here rather than documented.
        //   * Real machine route, wrong verb -> routing's Allow header is the union of every method at
        //     the node, so it advertises the catch-all's GET/HEAD on routes that have neither. Replaced
        //     with the methods the route actually declares.
        app.Use(async (context, next) =>
        {
            await next(context);

            if (context.Response.HasStarted ||
                context.Response.StatusCode != StatusCodes.Status405MethodNotAllowed ||
                !IsMachineFacingPath(context.Request.Path))
            {
                return;
            }

            // Only correct a 405 this pipeline itself manufactured: routing's synthetic
            // method-mismatch endpoint (or its AllowAnonymous replacement above, which keeps the
            // framework display name) and the GET/HEAD machine fallbacks below. A real endpoint
            // that answers 405 on its own owns that answer. Measured (2026-08-25): the MCP
            // transport's wrong-verb 405 survives even without this guard, because that response
            // has started by the time this middleware resumes — but that immunity is incidental to
            // how the SDK writes its response, so the scope is pinned here rather than borrowed
            // from it, held by WrongVerbOnRealMcpEndpoint_KeepsItsOwn405_WithValidApiKey.
            var respondingEndpoint = context.GetEndpoint();
            var isSyntheticMethodMismatch =
                respondingEndpoint is not null &&
                respondingEndpoint is not RouteEndpoint &&
                string.Equals(respondingEndpoint.DisplayName, Http405EndpointDisplayName, StringComparison.Ordinal);
            var isMachineFallback =
                respondingEndpoint?.Metadata.GetMetadata<MachinePathFallbackMetadata>() is not null;
            if (!isSyntheticMethodMismatch && !isMachineFallback)
            {
                return;
            }

            var declaredMethods = machineRouteMethods.GetDeclaredMethods(context);
            if (declaredMethods.Count == 0)
            {
                context.Response.Headers.Allow = StringValues.Empty;
                await WriteUnknownEndpointAsync(context);
                return;
            }

            context.Response.Headers.Allow = string.Join(", ", declaredMethods);
        });

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

        // Machine-facing 404s (#1971). The SPA fallback below matches EVERY unmatched path, so
        // before this change a typo'd, renamed, or removed API route answered 200 + index.html and
        // any caller that checks the status code saw a false success. These per-prefix fallbacks
        // carry a literal first segment, which outranks the SPA catch-all in route precedence, so
        // an unmatched path under one of them terminates here instead of at the app shell — with the
        // API's JSON error contract (ApiErrorResponse, application/json) when no route exists there
        // at all, or a 405 when one exists under another verb (#1992).
        //
        // Only *unmatched* paths reach a fallback: every real controller, hub, and MCP endpoint is
        // a non-fallback endpoint and still wins the match, so an unauthenticated request to an
        // existing API route keeps returning 401 rather than 404 (#1132 AC4 ordering preserved).
        //
        // AllowAnonymous is deliberate: an unknown path has no resource to protect, and without it
        // the global FallbackPolicy would answer anonymous callers with 401 — re-hiding the "this
        // endpoint does not exist" signal behind an auth error for exactly the unauthenticated
        // scripts and probes this issue is about. The cost is that route existence is discoverable
        // (404 vs 401) without credentials, which the OpenAPI document already publishes.
        //
        // GET/HEAD only, mirroring the method metadata MapFallbackToFile puts on the SPA catch-all.
        // This is load-bearing, not decoration: routing only reaches its 405 endpoint when EVERY
        // candidate is method-mismatched, so a fallback that accepted every verb would be a valid
        // candidate for PUT /api/boards and would silently downgrade that 405 to a 404. Scoped to
        // GET/HEAD, every verb outside the pair still reaches routing's 405 endpoint, where the
        // middleware registered above corrects its Allow header (or converts it to the 404 contract
        // when the path matches no route at all).
        //
        // Inside the pair the same scoping is what BREAKS 405: a GET on a POST-only route is not
        // method-mismatched against this fallback, so it lands here instead of at routing's 405
        // endpoint and used to answer 404 (#1992). The handler therefore asks the resolver whether a
        // real endpoint exists at the path before choosing a status — routing cannot be asked, because
        // HttpMethodMatcherPolicy partitions the candidate set by verb inside the DFA, so the POST
        // endpoint is not visible from here.
        //
        // ExcludeFromDescription keeps them out of the OpenAPI document. Swashbuckle discovers
        // route-mapped endpoints, so without it these catch-alls are published as real GET operations
        // next to the genuine routes — in the very document consumers use to learn the surface, and
        // the one this comment block cites as already publishing route existence. Measured with
        // scripts/ci/generate-openapi-artifact.ps1 (2026-08-22): without the exclusion the document
        // carries 160 paths including "/api/{path}", "/hubs/{path}", "/health/{path}" and
        // "/mcp/{path}"; with it, 156 and no "{path}" template at all (#1971).
        foreach (var prefix in NonSpaPathPrefixes)
        {
            app.MapFallback(
                    $"{prefix}/{{**path}}",
                    (HttpContext context) => MachinePathFallbackAsync(context, machineRouteMethods))
                .WithMetadata(MachinePathFallbackMetadata.Instance)
                .WithMetadata(new HttpMethodMetadata(SpaFallbackHttpMethods))
                .ExcludeFromDescription()
                .AllowAnonymous();
        }

        // SPA fallback: any other unmatched route returns index.html, enabling Vue Router's
        // client-side navigation. Paths under NonSpaPathPrefixes cannot reach it — either a real
        // endpoint above matched them, or the per-prefix 404 fallbacks did. AllowAnonymous so the
        // app shell loads for unauthenticated users (e.g. to reach the login page) under the global
        // FallbackPolicy (#1132 AC4).
        app.MapFallbackToFile("index.html").AllowAnonymous();

        return app;
    }

    /// <summary>
    /// True when the path sits under one of <see cref="NonSpaPathPrefixes"/>. Uses
    /// <see cref="PathString.StartsWithSegments(PathString, StringComparison)"/> so the boundary is a
    /// segment boundary — <c>/api/x</c> and the bare <c>/api</c> are machine paths, <c>/apidocs</c> is
    /// not — matching the <c>^/api(?:/|$)</c> shape the reverse proxy uses for the same prefixes.
    /// </summary>
    private static bool IsMachineFacingPath(PathString path)
    {
        foreach (var prefix in NonSpaPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// GET/HEAD terminal handler for paths under <see cref="NonSpaPathPrefixes"/> that matched no
    /// endpoint for this verb. A path that a real endpoint declares under some OTHER verb exists — the
    /// verb is what is wrong — so it answers 405 the way routing answers every other wrong-verb
    /// request: status only, with the Allow header stamped by the middleware that owns it for all
    /// verbs. A path no endpoint declares at all is genuinely missing and gets the 404 contract.
    /// </summary>
    private static Task MachinePathFallbackAsync(HttpContext context, MachineRouteMethodResolver resolver)
    {
        if (resolver.GetDeclaredMethods(context).Count > 0)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return Task.CompletedTask;
        }

        return WriteUnknownEndpointAsync(context);
    }

    /// <summary>
    /// Writes the 404 answer for a machine-facing path that matches no route. Uses the same
    /// <see cref="ApiErrorResponse"/> shape (<c>errorCode</c>/<c>message</c>, application/json) that
    /// <see cref="ResultExtensions.ToErrorActionResult"/> produces for a controller 404, so a client
    /// parses one error contract regardless of whether the route existed.
    /// </summary>
    private static Task WriteUnknownEndpointAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(ErrorCodes.NotFound, "The requested endpoint does not exist."));
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
