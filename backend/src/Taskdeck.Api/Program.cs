using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.OpenApi;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.FirstRun;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Mcp;

// ── MCP modes ───────────────────────────────────────────────────────────────
// When launched with "--mcp", run as an MCP server instead of the full web API.
//   --mcp                          → stdio transport (default, for Claude Code / Cursor)
//   --mcp --transport http         → HTTP transport with API key auth (for cloud/remote)
//   --mcp --transport http --port 5001 → HTTP transport on a specific port
if (args.Contains("--mcp"))
{
    var transport = "stdio";
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase))
            transport = args[i + 1].ToLowerInvariant();
    }

    if (transport != "stdio" && transport != "http")
    {
        Console.Error.WriteLine($"Error: unknown transport '{transport}'. Supported values: stdio, http");
        return 1;
    }

    if (transport == "http")
    {
        // ── MCP HTTP mode ───────────────────────────────────────────────────
        // Minimal web server exposing only the MCP endpoint with API key auth.
        // No controllers, no SignalR, no Swagger, no frontend — just MCP.
        var mcpPort = 5001;
        var mcpBindHost = Program.StandaloneMcpDefaultBindHost;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args[i + 1], out var parsedPort)
                    && parsedPort is >= 1 and <= 65535)
                {
                    mcpPort = parsedPort;
                }
                else
                {
                    Console.Error.WriteLine($"Error: invalid --port value '{args[i + 1]}'. Must be an integer between 1 and 65535.");
                    return 1;
                }
            }
            else if (string.Equals(args[i], "--host", StringComparison.OrdinalIgnoreCase))
            {
                mcpBindHost = args[i + 1];
            }
        }

        var mcpHttpBuilder = WebApplication.CreateBuilder(args);
        var mcpHttpLocalConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath(
            mcpHttpBuilder.Environment.IsProduction(),
            FirstRunBootstrapper.IsHeadlessEnvironment());

        // Load appsettings.local.json for locally-generated secrets via the SAME hardened path the web
        // API uses: AddLocalConfigFile validates or securely quarantines a corrupt file (optional:true only
        // suppresses a MISSING file, not a malformed one), repairs the file's permissions (#1241 forward
        // remediation for installs upgraded from a pre-#1241
        // build that only ever launch in MCP mode), resolves the same absolute durable/compatibility path
        // policy as the web host (MCP servers often launch from an arbitrary working directory), and inserts
        // that source
        // BEFORE the env-var sources so operator-supplied environment config keeps priority.
        mcpHttpBuilder.AddLocalConfigFile(mcpHttpLocalConfigPath);

        // The standalone server is local-only by default. Replace the repository-wide wildcard
        // host setting with loopback hosts unless an operator supplied an exact allowlist.
        Program.ApplyStandaloneMcpHostSecurity(mcpHttpBuilder.Configuration);

        mcpHttpBuilder.WebHost.UseUrls($"http://{mcpBindHost}:{mcpPort}");

        // Infrastructure (DbContext, Repositories, UoW)
        mcpHttpBuilder.Services.AddInfrastructure(mcpHttpBuilder.Configuration);

        // Application services needed by MCP resources and tools (shared with stdio mode).
        mcpHttpBuilder.Services.AddMcpApplicationServices();

        // HTTP identity: maps API key to user via HttpUserContextProvider.
        mcpHttpBuilder.Services.AddHttpContextAccessor();
        mcpHttpBuilder.Services.AddScoped<IUserContextProvider, Taskdeck.Infrastructure.Mcp.HttpUserContextProvider>();

        // Rate limiting: register the McpPerApiKey policy for per-key throttling.
        var mcpRateLimitingSettings = mcpHttpBuilder.Configuration
            .GetSection("RateLimiting")
            .Get<Taskdeck.Application.Services.RateLimitingSettings>()
            ?? new Taskdeck.Application.Services.RateLimitingSettings();

        // Fail fast on invalid RateLimiting configuration BEFORE the pre-auth limiter can be
        // constructed (AddTaskdeckRateLimiting registers it via a lazy factory — constructed on
        // first resolution — and its constructor only lower-clamps, so an over-maximum value would
        // otherwise be silently accepted). The standalone host does not run the co-hosted
        // AddOptionsValidation / ValidateOnStart pipeline, so apply the same validator here with the
        // same semantics: skipped when RateLimiting:Enabled=false, fail-fast with the validation
        // message otherwise.
        var mcpRateLimitingValidation = new Taskdeck.Api.Validation.RateLimitingSettingsValidator()
            .Validate(null, mcpRateLimitingSettings);
        if (mcpRateLimitingValidation.Failed)
        {
            Console.Error.WriteLine(
                $"Error: invalid RateLimiting configuration. {mcpRateLimitingValidation.FailureMessage}");
            return 1;
        }

        mcpHttpBuilder.Services.AddSingleton(mcpRateLimitingSettings);
        if (mcpRateLimitingSettings.Enabled)
        {
            mcpHttpBuilder.Services.AddTaskdeckRateLimiting(mcpRateLimitingSettings);
        }

        // OpenTelemetry: export MCP activity source and meter so spans/metrics
        // are not silently dropped in standalone HTTP mode.
        var mcpObservabilitySettings = mcpHttpBuilder.Configuration
            .GetSection("Observability")
            .Get<Taskdeck.Application.Services.ObservabilitySettings>()
            ?? new Taskdeck.Application.Services.ObservabilitySettings();
        mcpHttpBuilder.Services.AddTaskdeckObservability(mcpObservabilitySettings);

        // MCP telemetry (operation logger, etc.).
        mcpHttpBuilder.Services.AddMcpTelemetry();

        // CORS services with NO policies registered at all -- deliberately not AddTaskdeckCors (#1602).
        // MapTaskdeckMcpEndpoint stamps the endpoint with DisableCorsAttribute, which is ICorsMetadata,
        // and ASP.NET Core's EndpointMiddleware refuses to execute an endpoint carrying CORS metadata
        // unless the CORS middleware ran first -- so without this pair (AddCors here, UseCors below) the
        // standalone host threw InvalidOperationException and returned a bare 500 on EVERY authenticated
        // request. Registering the services with an empty policy map satisfies that contract and adds no
        // cross-origin capability: there is no default policy to resolve, so CorsMiddleware never emits
        // an Access-Control-* header, and the endpoint's DisableCorsAttribute suppresses CORS handling
        // for it regardless. Browser-origin MCP clients stay unsupported here by construction.
        mcpHttpBuilder.Services.AddCors();

        // MCP server: HTTP transport + all resources and tools.
        // Stateless is pinned explicitly rather than left to the library default:
        // ModelContextProtocol 2.0.0 flipped that default from false to true, which drops
        // Mcp-Session-Id, standalone SSE GET/DELETE, and server-to-client requests. Taskdeck's
        // session-bound contract depends on those, so keep it false until a deliberate,
        // ADR-recorded decision says otherwise.
        mcpHttpBuilder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = false)
            .AddMcpResourcesAndTools();

        var mcpHttpApp = mcpHttpBuilder.Build();

        // Test seam: lets the standalone MCP host-filtering integration test observe the
        // built app when it drives this real entry point in-process, so it can await
        // startup and stop the host cleanly. Never set in production.
        Program.OnStandaloneMcpHttpAppBuilt?.Invoke(mcpHttpApp);

        // Apply EF Core migrations before starting, serialized across processes via a
        // cross-process file lock so the MCP HTTP host does not race the API/CLI (#1164), with a
        // fail-closed pre-migration snapshot of the SQLite file when migrations are pending (#1803).
        using (var scope = mcpHttpApp.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
            var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var backupSettings = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Taskdeck.Application.Services.DatabaseBackupSettings>>().Value;
            Taskdeck.Infrastructure.Persistence.SerializedMigrator.Migrate(dbContext, backupSettings, migrationLogger);
        }

        // Honour trusted forwarded headers (default OFF) so the pre-auth failure-budget limiter and
        // per-key partitioning key on the real client address behind a reverse proxy instead of
        // collapsing every client into the proxy's single socket address. Mirrors the co-hosted API
        // pipeline: only active when the operator configures ForwardedHeaders:KnownProxies /
        // KnownNetworks, and X-Forwarded-For is never trusted from an unknown peer. Runs before the
        // limiter and telemetry so all downstream see the corrected Connection.RemoteIpAddress.
        var mcpForwardedHeadersOptions = Taskdeck.Api.Extensions.PipelineConfiguration
            .BuildForwardedHeadersOptions(mcpHttpApp.Configuration);
        if (mcpForwardedHeadersOptions is not null)
        {
            mcpHttpApp.UseForwardedHeaders(mcpForwardedHeadersOptions);
        }

        // Correlation ID propagation: honours client X-Request-Id header.
        mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.CorrelationIdMiddleware>();

        // Fail-closed machine-path spelling (#1992, ADR-0064), the identical rule the co-hosted
        // pipeline enforces. This host builds its own pipeline rather than calling
        // ConfigureTaskdeckPipeline, so without this line /MCP answered 401 without a key and 200
        // WITH one — the real endpoint, reached by a spelling the reverse proxy and the service
        // worker both refuse to route. There is no SPA fallback here to leak, but one URL must not
        // mean two different things depending on which host is serving it.
        //
        // Ahead of ApiKeyMiddleware so a variant is rejected before a key parse, the failure budget
        // or the per-key budget, and so the answer is 404 rather than "authenticate for this".
        //
        // Ahead of UseCors for the same reason it precedes CORS in the co-hosted pipeline: routing
        // is case-insensitive, so OPTIONS /MCP carrying an Origin and Access-Control-Request-Method
        // selects the REAL MCP endpoint, and .NET 8's CorsMiddleware short-circuits a preflight for
        // a selected endpoint with 204 -- a success a browser reads as "this endpoint exists", on a
        // spelling that answers 404 everywhere else. Nothing here needs CORS to have run first: the
        // endpoint's DisableCorsAttribute is consulted by EndpointMiddleware when it executes the
        // endpoint, which is further down the pipeline than both of these.
        Taskdeck.Api.Extensions.PipelineConfiguration.UseMachinePathCanonicalGuard(mcpHttpApp);

        // CORS middleware with no named policy: required so EndpointMiddleware will execute the MCP
        // endpoint, which carries DisableCorsAttribute (ICorsMetadata) -- see AddCors above (#1602).
        // Deny-by-default: no policy is registered, so this emits no Access-Control-* headers and
        // grants no cross-origin access; it exists purely to satisfy the endpoint's metadata contract.
        // Positioned to mirror the co-hosted pipeline (correlation ID -> machine-path guard -> CORS),
        // and after the auto-inserted UseRouting so an endpoint is selected when it runs.
        mcpHttpApp.UseCors();

        // MCP telemetry middleware: structured logging, spans, and metrics for /mcp requests.
        // Runs before ApiKeyMiddleware so it captures all requests including those
        // rejected with 401 (missing/invalid/revoked API keys).
        mcpHttpApp.UseMiddleware<McpTelemetryMiddleware>();

        // Bound the cost of authentication FAILURES by client address: reject before a key parse or
        // database lookup once the address's failure budget is spent, but let valid requests through
        // without consuming so they reach the per-key policy with independent budgets.
        if (mcpRateLimitingSettings.Enabled)
        {
            mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.McpAuthenticationRateLimitingMiddleware>();
        }

        // API key authentication for MCP requests. ApiKeyMiddleware also enforces the per-key request
        // budget (McpPerApiKey) via McpPerApiKeyRateLimiter (#1384) before the user lookup and
        // last-used write. The standalone host serves only the /mcp endpoint, which no longer carries
        // an endpoint-stage rate-limiting policy, so the UseRateLimiter() middleware (which only
        // applies endpoint/global policies) is not needed here — the pre-auth failure budget and the
        // per-key budget are both enforced by dedicated middleware/components above.
        mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.ApiKeyMiddleware>();

        // Use the same authenticated route mapping as the co-hosted API. Per-key rate limiting is
        // enforced in ApiKeyMiddleware (#1384), shared identically by both pipelines.
        mcpHttpApp.MapTaskdeckMcpEndpoint();

        var mcpHttpLogger = mcpHttpApp.Services.GetRequiredService<ILogger<Program>>();
        mcpHttpLogger.LogInformation("Taskdeck MCP HTTP server starting on http://{Host}:{Port}", mcpBindHost, mcpPort);

        await mcpHttpApp.RunAsync();
        return 0;
    }

    // ── MCP stdio mode ──────────────────────────────────────────────────────
    // This path intentionally skips JWT, CORS, SignalR, rate limiting, and the
    // HTTP pipeline — none of those are meaningful over a local stdio connection.
    // Prepare the persisted secrets file before loading it (#1241/#1242), mirroring AddLocalConfigFile on
    // the web path. Production validates and fails closed on corrupt durable evidence; compatibility hosts
    // retain secure quarantine behavior (optional:true suppresses only a MISSING file, not malformed JSON).
    // Load it by the same ABSOLUTE path policy: stdio MCP servers are typically launched by an MCP client
    // from the client's own working directory, so a relative file could miss the durable desktop config
    // (or the executable-local compatibility config) being repaired.
    // Env-var precedence is preserved by the AddEnvironmentVariables() re-add AFTER the file source.
    var mcpStdioEnvironmentOverride = FirstRunBootstrapper.ResolveMcpStdioEnvironmentOverride(
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
    var mcpStdioHostBuilder = Host.CreateDefaultBuilder(args);
    if (mcpStdioEnvironmentOverride is not null)
    {
        // Apply before Build so the Generic Host environment, default appsettings.{Environment}.json source,
        // explicit source below, and local-config path policy all use one authoritative name.
        mcpStdioHostBuilder.UseEnvironment(mcpStdioEnvironmentOverride);
    }

    string? mcpStdioLocalConfigPath = null;
    var mcpHost = mcpStdioHostBuilder
        .ConfigureAppConfiguration((context, config) =>
        {
            var environmentName = context.HostingEnvironment.EnvironmentName;
            var isProduction = string.Equals(
                environmentName,
                Environments.Production,
                StringComparison.OrdinalIgnoreCase);
            var isHeadless = FirstRunBootstrapper.IsHeadlessEnvironment();
            mcpStdioLocalConfigPath ??= FirstRunBootstrapper.ResolveLocalConfigPath(
                isProduction,
                isHeadless);
            FirstRunBootstrapper.PrepareLocalConfigFile(
                mcpStdioLocalConfigPath,
                FirstRunBootstrapper.LegacyLocalConfigPath,
                requireOwnerOnly: isProduction && !isHeadless);
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddJsonFile($"appsettings.{environmentName}.json", optional: true);
            config.AddJsonFile(mcpStdioLocalConfigPath, optional: true);
            config.AddEnvironmentVariables();
        })
        .ConfigureLogging(logging =>
        {
            // In stdio mode stdout is the MCP transport channel.
            // Log to stderr only to avoid corrupting the JSON-RPC stream.
            logging.ClearProviders();
            logging.AddConsole(opts => opts.LogToStandardErrorThreshold = Microsoft.Extensions.Logging.LogLevel.Trace);
        })
        .ConfigureServices((ctx, services) =>
        {
            // Infrastructure (DbContext, Repositories, UoW)
            services.AddInfrastructure(ctx.Configuration);

            // Application services needed by MCP resources and tools (shared with HTTP mode).
            services.AddMcpApplicationServices();

            // Stdio identity: maps the OS process owner to the local default user.
            services.AddScoped<IUserContextProvider, StdioUserContextProvider>();

            // MCP telemetry (operation logger, etc.).
            services.AddMcpTelemetry();

            // MCP server: stdio transport + all resources and tools.
            services.AddMcpServer()
                .WithStdioServerTransport()
                .AddMcpResourcesAndTools();
        })
        .Build();

    // Apply EF Core migrations before starting the MCP host (mirrors web mode behaviour),
    // serialized across processes via a cross-process file lock (#1164), with a fail-closed
    // pre-migration snapshot of the SQLite file when migrations are pending (#1803). In stdio
    // mode logs go to stderr, so the logger never corrupts the stdout JSON-RPC stream.
    using (var scope = mcpHost.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
        var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var backupSettings = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Taskdeck.Application.Services.DatabaseBackupSettings>>().Value;
        Taskdeck.Infrastructure.Persistence.SerializedMigrator.Migrate(dbContext, backupSettings, migrationLogger);
    }

    await mcpHost.RunAsync();
    return 0;
}
// ── End MCP modes ───────────────────────────────────────────────────────────

DesktopRuntime.InstallPackagedFatalHandler();
if (DesktopRuntime.IsPackagedDesktop)
{
    DesktopRuntime.WriteStarting();
}

try
{
var builder = DesktopRuntime.CreateWebApplicationBuilder(args);

// #2233: a Windows profile that once ran a Gemini-era Taskdeck keeps its user-scope retired
// provider variables forever, and the packaged double-click inherits them. Drop those
// retired names before any reader sees them so the packaged app still starts on its default
// provider. Only environment sources are filtered — retired settings the user wrote into
// Taskdeck's own appsettings files remain fatal — and only in the packaged desktop host, so the
// container / dotnet run / CI fail-closed contract is untouched.
var retiredProviderNotice = new RetiredLlmProviderConfigurationNotice();
if (DesktopRuntime.IsPackagedDesktop)
{
    RetiredProviderEnvironmentConfiguration.IgnoreInheritedRetiredProviderConfiguration(
        builder.Configuration,
        retiredProviderNotice);
    if (retiredProviderNotice.IgnoredEnvironmentConfiguration)
    {
        DesktopRuntime.WriteRetiredProviderConfigurationIgnored();
    }
}

builder.Services.AddSingleton(retiredProviderNotice);
DesktopRuntime.ConfigurePackagedListenUrl(builder);
var bootstrapHeadless = DesktopRuntime.IsBootstrapHeadlessEnvironment(DesktopRuntime.IsPackagedDesktop);
var localConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath(
    builder.Environment.IsProduction(),
    bootstrapHeadless);

if (DesktopRuntime.IsPackagedDesktop)
{
    DesktopRuntime.WriteDataLocation(Path.GetDirectoryName(localConfigPath)!);
}

// Taskdeck is a local-first app — the Windows EventLog provider added by
// CreateBuilder() causes ObjectDisposedException crashes in background
// workers when it is disposed before EF Core finishes logging.
builder.Logging.ClearProviders();
if (!DesktopRuntime.IsPackagedDesktop)
{
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

// ---- First-run bootstrap (must run before services are registered) ----------
// Registers appsettings.local.json so previously generated secrets are loaded,
// then generates a JWT secret if none is configured.
builder.AddLocalConfigFile(localConfigPath, bootstrapHeadless);
using (var bootstrapLoggerFactory = LoggerFactory.Create(lb =>
{
    if (!DesktopRuntime.IsPackagedDesktop)
    {
        lb.AddConsole();
    }
}))
{
    var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("FirstRun");
    Action<BootstrapIdentityLifecycle>? bootstrapIdentityObserver = DesktopRuntime.IsPackagedDesktop
        ? DesktopRuntime.WriteBootstrapIdentity
        : null;
    builder.RunFirstRunChecks(
        bootstrapLogger,
        localConfigPath,
        bootstrapHeadless,
        bootstrapIdentityObserver);
    // Hard-fail if a placeholder JWT secret reaches Production (cloud containers).
    builder.ValidateProductionSecrets(bootstrapLogger, localConfigPath);
}
// -----------------------------------------------------------------------------

// Add services to the container
builder.Services.AddControllers();

// SignalR with optional Redis backplane (see ADR-0023)
using (var signalRLoggerFactory = LoggerFactory.Create(lb =>
{
    if (!DesktopRuntime.IsPackagedDesktop)
    {
        lb.AddConsole();
    }
}))
{
    var signalRLogger = signalRLoggerFactory.CreateLogger("SignalR");
    builder.Services.AddTaskdeckSignalR(builder.Configuration, signalRLogger);
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Taskdeck API",
        Version = "v1",
        Description = "Local-first execution workspace API. Provides board management, capture pipeline, "
            + "chat-to-proposal automation, webhook integrations, and review-first governance.",
        Contact = new OpenApiContact
        {
            Name = "Taskdeck Contributors",
            Url = new Uri("https://github.com/Chris0Jeky/Taskdeck")
        },
        License = new OpenApiLicense
        {
            Name = "GPL-3.0-only"
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. "
            + "Enter your token in the text input below. Example: 'eyJhbGci...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });

    // Include XML comments from the API project
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// Bind configuration settings (observability, rate limiting, security headers, JWT, Sentry, telemetry, analytics)
builder.Services.AddTaskdeckSettings(
    builder.Configuration,
    builder.Environment,
    out var observabilitySettings,
    out var rateLimitingSettings,
    out var jwtSettings,
    out var gitHubOAuthSettings,
    out var oidcSettings,
    out var sentrySettings,
    out _,  // telemetrySettings — registered in DI by AddTaskdeckSettings
    out _); // analyticsSettings — registered in DI by AddTaskdeckSettings

// Wire ValidateDataAnnotations() + ValidateOnStart() for all settings classes.
// The app will fail fast on startup if any configuration value is invalid.
builder.Services.AddOptionsValidation(builder.Configuration);

// Add Infrastructure (DbContext, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

// Add Application Services
builder.Services.AddApplicationServices();

// Add LLM providers (quota, kill switch, OpenAI/compatible/Ollama/Mock selection)
builder.Services.AddLlmProviders(builder.Configuration);

// Add IUserContext for claim-based identity
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Taskdeck.Application.Interfaces.IUserContext, Taskdeck.Infrastructure.Identity.UserContext>();

// Register MCP HTTP transport (Streamable HTTP alongside REST on the same Kestrel instance).
// The HttpUserContextProvider resolves user identity from the API key set by ApiKeyMiddleware.
builder.Services.AddScoped<IUserContextProvider, Taskdeck.Infrastructure.Mcp.HttpUserContextProvider>();
builder.Services.AddMcpTelemetry();
// Stateless is pinned explicitly — see the standalone MCP host above for why the
// ModelContextProtocol 2.0.0 default flip must not be inherited silently.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = false)
    .AddMcpResourcesAndTools();

// Add JWT Authentication (with optional GitHub OAuth and OIDC providers, circuit-breaker-protected backchannel)
// CircuitBreakerStateTracker is already registered as a singleton by AddLlmProviders above.
var circuitBreakerTracker = builder.Services
    .Where(d => d.ServiceType == typeof(CircuitBreakerStateTracker))
    .Select(d => d.ImplementationInstance as CircuitBreakerStateTracker)
    .FirstOrDefault();
var circuitBreakerSettings = builder.Services
    .Where(d => d.ServiceType == typeof(CircuitBreakerSettings))
    .Select(d => d.ImplementationInstance as CircuitBreakerSettings)
    .FirstOrDefault();
builder.Services.AddTaskdeckAuthentication(jwtSettings, gitHubOAuthSettings, oidcSettings, circuitBreakerTracker, circuitBreakerSettings);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Owner", "Admin"))
    // Defense-in-depth (#1132 AC4): any endpoint without explicit authorization metadata requires
    // an authenticated user. Anonymous endpoints opt out explicitly — login/register/health/OAuth
    // controllers carry [AllowAnonymous]; the SPA fallback calls .AllowAnonymous(); and /mcp is
    // satisfied because ApiKeyMiddleware sets an authenticated principal for valid API keys (and
    // 401s invalid ones). The standalone --mcp HTTP host has no UseAuthorization and is unaffected.
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// Add OpenTelemetry observability
builder.Services.AddTaskdeckObservability(observabilitySettings);

// Add Sentry error tracking (config-gated, disabled by default)
builder.AddTaskdeckSentry(sentrySettings);

// Add worker services (LLM queue, proposal housekeeping, outbound webhooks)
builder.Services.AddTaskdeckWorkers(builder.Configuration, builder.Environment);

// Add CORS (bootstrap logger threaded in so the fail-closed warning is structured + filterable,
// matching the AddTaskdeckSignalR pattern above).
using (var corsLoggerFactory = LoggerFactory.Create(lb =>
{
    if (!DesktopRuntime.IsPackagedDesktop)
    {
        lb.AddConsole();
    }
}))
{
    var corsLogger = corsLoggerFactory.CreateLogger("Cors");
    builder.Services.AddTaskdeckCors(builder.Configuration, builder.Environment.IsDevelopment(), corsLogger);
}

// Add rate limiting
builder.Services.AddTaskdeckRateLimiting(rateLimitingSettings);

// Add response compression (Brotli + Gzip, Optimal level, enabled over HTTPS).
// See ResponseCompressionRegistration for BREACH/SignalR considerations.
builder.Services.AddTaskdeckResponseCompression();

// Register first-run settings and service
var firstRunSettings = builder.Configuration
    .GetSection("FirstRun")
    .Get<FirstRunSettings>() ?? new FirstRunSettings();
builder.Services.AddSingleton(firstRunSettings);
builder.Services.AddSingleton<FirstRunService>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.ConfigureTaskdeckPipeline(rateLimitingSettings);

// Resolve DB path and log startup info, then optionally open the browser
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
appLifetime.ApplicationStarted.Register(() =>
{
    var fr = app.Services.GetRequiredService<FirstRunService>();
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var browserUrl = DesktopRuntime.IsPackagedDesktop
        ? DesktopRuntime.ResolveUserFacingUrl(addresses)
        : addresses?.FirstOrDefault(u => u.Contains("://localhost"))
            ?? addresses?.FirstOrDefault()
            ?? $"http://localhost:{firstRunSettings.Port}";

    startupLogger.LogInformation("Taskdeck API is running at {Url}", browserUrl);
    startupLogger.LogInformation("Swagger UI available at {SwaggerUrl}", $"{browserUrl}/swagger");

    if (DesktopRuntime.IsPackagedDesktop)
    {
        _ = fr.ReportPackagedReadyAndOpenBrowserAsync(browserUrl, appLifetime.ApplicationStopping);
    }
    else
    {
        fr.TryOpenBrowser(browserUrl);
    }
});

if (DesktopRuntime.IsPackagedDesktop)
{
    appLifetime.ApplicationStopping.Register(DesktopRuntime.WriteStopping);
    appLifetime.ApplicationStopped.Register(DesktopRuntime.WriteStopped);
}

app.Run();
return 0;
}
catch (Exception ex) when (DesktopRuntime.IsPackagedDesktop)
{
    DesktopRuntime.WriteFatalStartup(ex);
    DesktopRuntime.WaitForFailureAcknowledgement();
    return 1;
}

public partial class Program
{
    internal const string StandaloneMcpDefaultBindHost = "127.0.0.1";
    internal const string StandaloneMcpLoopbackAllowedHosts = "localhost;127.0.0.1;[::1]";

    /// <summary>
    /// Test seam for the standalone MCP HTTP integration test (see
    /// <c>StandaloneMcpHostFilteringTests</c>): invoked with the built app just before it
    /// runs, so the test can await startup and stop the host. Never set in production.
    /// </summary>
    internal static Action<WebApplication>? OnStandaloneMcpHttpAppBuilt;

    internal static void ApplyStandaloneMcpHostSecurity(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Single rule: rewrite exactly when HostFilteringMiddleware itself would DISABLE
        // filtering; every other value is preserved because the middleware fails closed on
        // it. To decide that, mirror the middleware's parse EXACTLY:
        //   1. Split(';', RemoveEmptyEntries) with NO trimming -- so null/""/";"/";;" parse
        //      to zero entries (the middleware then falls back to allow-all: the true #1367
        //      fail-open), while whitespace-bearing values like " ; " or " * " parse to
        //      literal whitespace entries, an ACTIVE filter that rejects every real host.
        //   2. Normalize each entry via new HostString(entry).ToUriComponent() -- which
        //      RETAINS any :port suffix -- before the ordinal top-level-wildcard test for
        //      "*" / "0.0.0.0" / "[::]". Port-suffixed pseudo-wildcards ("0.0.0.0:5001")
        //      are therefore literals no real Host header matches, i.e. deny-all.
        // Rewriting any of those fail-closed literals to the loopback allowlist would be
        // strictly WEAKER (spoofed loopback Host headers would pass on a non-loopback
        // bind), so they are preserved. Do not reintroduce TrimEntries or HostString.Host
        // (port-stripping) here without re-reading the middleware source.
        var configuredHosts = configuration["AllowedHosts"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();
        var containsAnyHost = configuredHosts
            .Any(host => new HostString(host).ToUriComponent() is "*" or "0.0.0.0" or "[::]");
        if (configuredHosts.Length == 0 || containsAnyHost)
        {
            configuration["AllowedHosts"] = StandaloneMcpLoopbackAllowedHosts;
        }
    }
}
