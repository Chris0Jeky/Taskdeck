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

        // Load appsettings.local.json for locally-generated secrets via the SAME hardened path the web
        // API uses: AddLocalConfigFile quarantines a corrupt file (optional:true only suppresses a
        // MISSING file, not a malformed one -- an MCP-only launch must self-heal, not crash), repairs
        // the file's permissions (#1241 forward remediation for installs upgraded from a pre-#1241
        // build that only ever launch in MCP mode), resolves the ABSOLUTE exe-adjacent path (MCP
        // servers are often launched from an arbitrary working directory), and inserts the source
        // BEFORE the env-var sources so operator-supplied environment config keeps priority.
        mcpHttpBuilder.AddLocalConfigFile();

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

        // MCP server: HTTP transport + all resources and tools.
        mcpHttpBuilder.Services.AddMcpServer()
            .WithHttpTransport()
            .AddMcpResourcesAndTools();

        var mcpHttpApp = mcpHttpBuilder.Build();

        // Apply EF Core migrations before starting, serialized across processes via a
        // cross-process file lock so the MCP HTTP host does not race the API/CLI (#1164).
        using (var scope = mcpHttpApp.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
            var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            Taskdeck.Infrastructure.Persistence.SerializedMigrator.Migrate(dbContext, migrationLogger);
        }

        // Correlation ID propagation: honours client X-Request-Id header.
        mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.CorrelationIdMiddleware>();

        // MCP telemetry middleware: structured logging, spans, and metrics for /mcp requests.
        // Runs before ApiKeyMiddleware so it captures all requests including those
        // rejected with 401 (missing/invalid/revoked API keys).
        mcpHttpApp.UseMiddleware<McpTelemetryMiddleware>();

        // API key authentication for MCP requests.
        mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.ApiKeyMiddleware>();

        // Apply rate limiting before endpoint routing.
        if (mcpRateLimitingSettings.Enabled)
        {
            mcpHttpApp.UseRateLimiter();
        }

        // Use the same authenticated route mapping as the co-hosted API.
        mcpHttpApp.MapTaskdeckMcpEndpoint(mcpRateLimitingSettings.Enabled);

        var mcpHttpLogger = mcpHttpApp.Services.GetRequiredService<ILogger<Program>>();
        mcpHttpLogger.LogInformation("Taskdeck MCP HTTP server starting on http://{Host}:{Port}", mcpBindHost, mcpPort);

        await mcpHttpApp.RunAsync();
        return 0;
    }

    // ── MCP stdio mode ──────────────────────────────────────────────────────
    // This path intentionally skips JWT, CORS, SignalR, rate limiting, and the
    // HTTP pipeline — none of those are meaningful over a local stdio connection.
    // Quarantine a corrupt persisted secrets file, then repair its permissions, before loading it
    // (#1241) -- mirroring AddLocalConfigFile on the web path (optional:true only suppresses a MISSING
    // file, not a malformed one, so an un-quarantined corrupt file would crash every stdio launch).
    // Load it by the same ABSOLUTE path: stdio MCP servers are typically launched by an MCP client from
    // the client's own working directory, so a relative "appsettings.local.json" could miss the
    // exe-adjacent file FirstRunBootstrapper writes -- and the repair must target the file being loaded.
    // Env-var precedence is preserved by the AddEnvironmentVariables() re-add AFTER the file source.
    FirstRunBootstrapper.QuarantineCorruptLocalConfig();
    FirstRunBootstrapper.RestrictExistingLocalConfigFile();
    var mcpHost = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);
            config.AddJsonFile(FirstRunBootstrapper.LocalConfigPath, optional: true);
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
    // serialized across processes via a cross-process file lock (#1164). In stdio mode logs
    // go to stderr, so the logger never corrupts the stdout JSON-RPC stream.
    using (var scope = mcpHost.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
        var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        Taskdeck.Infrastructure.Persistence.SerializedMigrator.Migrate(dbContext, migrationLogger);
    }

    await mcpHost.RunAsync();
    return 0;
}
// ── End MCP modes ───────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Taskdeck is a local-first app — the Windows EventLog provider added by
// CreateBuilder() causes ObjectDisposedException crashes in background
// workers when it is disposed before EF Core finishes logging.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ---- First-run bootstrap (must run before services are registered) ----------
// Registers appsettings.local.json so previously generated secrets are loaded,
// then generates a JWT secret if none is configured.
builder.AddLocalConfigFile();
using (var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole()))
{
    var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("FirstRun");
    builder.RunFirstRunChecks(bootstrapLogger);
    // Hard-fail if a placeholder JWT secret reaches Production (cloud containers).
    builder.ValidateProductionSecrets(bootstrapLogger);
}
// -----------------------------------------------------------------------------

// Add services to the container
builder.Services.AddControllers();

// SignalR with optional Redis backplane (see ADR-0023)
using (var signalRLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole()))
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
            Name = "MIT"
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

// Add LLM providers (quota, kill switch, OpenAI/Gemini/Mock selection)
builder.Services.AddLlmProviders(builder.Configuration);

// Add IUserContext for claim-based identity
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Taskdeck.Application.Interfaces.IUserContext, Taskdeck.Infrastructure.Identity.UserContext>();

// Register MCP HTTP transport (Streamable HTTP alongside REST on the same Kestrel instance).
// The HttpUserContextProvider resolves user identity from the API key set by ApiKeyMiddleware.
builder.Services.AddScoped<IUserContextProvider, Taskdeck.Infrastructure.Mcp.HttpUserContextProvider>();
builder.Services.AddMcpTelemetry();
builder.Services.AddMcpServer()
    .WithHttpTransport()
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
using (var corsLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole()))
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
    var browserUrl = addresses?.FirstOrDefault(u => u.Contains("://localhost"))
        ?? addresses?.FirstOrDefault()
        ?? $"http://localhost:{firstRunSettings.Port}";

    startupLogger.LogInformation("Taskdeck API is running at {Url}", browserUrl);
    startupLogger.LogInformation("Swagger UI available at {SwaggerUrl}", $"{browserUrl}/swagger");

    fr.TryOpenBrowser(browserUrl);
});

app.Run();
return 0;

public partial class Program
{
    internal const string StandaloneMcpDefaultBindHost = "127.0.0.1";
    internal const string StandaloneMcpLoopbackAllowedHosts = "localhost;127.0.0.1;[::1]";

    internal static void ApplyStandaloneMcpHostSecurity(IConfiguration configuration)
    {
        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts == "*")
        {
            configuration["AllowedHosts"] = StandaloneMcpLoopbackAllowedHosts;
        }
    }
}
