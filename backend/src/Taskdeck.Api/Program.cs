using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.OpenApi;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.FirstRun;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.Interfaces;
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
        return;
    }

    if (transport == "http")
    {
        // ── MCP HTTP mode ───────────────────────────────────────────────────
        // Minimal web server exposing only the MCP endpoint with API key auth.
        // No controllers, no SignalR, no Swagger, no frontend — just MCP.
        var mcpPort = 5001;
        var mcpBindHost = "0.0.0.0";
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
                    return;
                }
            }
            else if (string.Equals(args[i], "--host", StringComparison.OrdinalIgnoreCase))
            {
                mcpBindHost = args[i + 1];
            }
        }

        var mcpHttpBuilder = WebApplication.CreateBuilder(args);

        // Load appsettings.local.json for locally-generated secrets (mirrors stdio mode).
        mcpHttpBuilder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        mcpHttpBuilder.WebHost.UseUrls($"http://{mcpBindHost}:{mcpPort}");

        // Infrastructure (DbContext, Repositories, UoW)
        mcpHttpBuilder.Services.AddInfrastructure(mcpHttpBuilder.Configuration);

        // Register Application services needed by MCP resources and tools.
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.AuthorizationService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.IAuthorizationService>(
            sp => sp.GetRequiredService<Taskdeck.Application.Services.AuthorizationService>());
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.BoardService>(sp =>
            new Taskdeck.Application.Services.BoardService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<Taskdeck.Application.Services.IAuthorizationService>()));
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.ColumnService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.CardService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.LabelService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.AutomationProposalService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.IAutomationProposalService>(
            sp => sp.GetRequiredService<Taskdeck.Application.Services.AutomationProposalService>());
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.CaptureService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.ICaptureService>(
            sp => sp.GetRequiredService<Taskdeck.Application.Services.CaptureService>());
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.NotificationService>();
        mcpHttpBuilder.Services.AddScoped<Taskdeck.Application.Services.INotificationService>(
            sp => sp.GetRequiredService<Taskdeck.Application.Services.NotificationService>());

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

        // MCP server: HTTP transport + all resources and tools.
        mcpHttpBuilder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithResources<BoardResources>()
            .WithResources<CaptureResources>()
            .WithResources<ProposalResources>()
            .WithTools<ReadTools>()
            .WithTools<WriteTools>()
            .WithTools<ProposalTools>();

        var mcpHttpApp = mcpHttpBuilder.Build();

        // Apply EF Core migrations before starting.
        using (var scope = mcpHttpApp.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
            dbContext.Database.Migrate();
        }

        // API key authentication for MCP requests.
        mcpHttpApp.UseMiddleware<Taskdeck.Api.Middleware.ApiKeyMiddleware>();

        // Apply rate limiting before endpoint routing.
        if (mcpRateLimitingSettings.Enabled)
        {
            mcpHttpApp.UseRateLimiter();
        }

        // Map the MCP endpoint with per-API-key rate limiting.
        var mcpEndpoint = mcpHttpApp.MapMcp();
        if (mcpRateLimitingSettings.Enabled)
        {
            mcpEndpoint.RequireRateLimiting(Taskdeck.Api.RateLimiting.RateLimitingPolicyNames.McpPerApiKey);
        }

        var mcpHttpLogger = mcpHttpApp.Services.GetRequiredService<ILogger<Program>>();
        mcpHttpLogger.LogInformation("Taskdeck MCP HTTP server starting on http://{Host}:{Port}", mcpBindHost, mcpPort);

        await mcpHttpApp.RunAsync();
        return;
    }

    // ── MCP stdio mode ──────────────────────────────────────────────────────
    // This path intentionally skips JWT, CORS, SignalR, rate limiting, and the
    // HTTP pipeline — none of those are meaningful over a local stdio connection.
    var mcpHost = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);
            config.AddJsonFile("appsettings.local.json", optional: true);
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

            // Register Application services needed by MCP resources and tools.
            // We deliberately skip web-only services (SignalR notifiers, workers,
            // LLM providers, rate limiting, etc.) to keep the MCP host minimal.
            services.AddScoped<Taskdeck.Application.Services.AuthorizationService>();
            services.AddScoped<Taskdeck.Application.Services.IAuthorizationService>(
                sp => sp.GetRequiredService<Taskdeck.Application.Services.AuthorizationService>());
            services.AddScoped<Taskdeck.Application.Services.BoardService>(sp =>
                new Taskdeck.Application.Services.BoardService(
                    sp.GetRequiredService<IUnitOfWork>(),
                    sp.GetRequiredService<Taskdeck.Application.Services.IAuthorizationService>()));
            services.AddScoped<Taskdeck.Application.Services.ColumnService>();
            services.AddScoped<Taskdeck.Application.Services.CardService>();
            services.AddScoped<Taskdeck.Application.Services.LabelService>();
            services.AddScoped<Taskdeck.Application.Services.AutomationProposalService>();
            services.AddScoped<Taskdeck.Application.Services.IAutomationProposalService>(
                sp => sp.GetRequiredService<Taskdeck.Application.Services.AutomationProposalService>());
            services.AddScoped<Taskdeck.Application.Services.CaptureService>();
            services.AddScoped<Taskdeck.Application.Services.ICaptureService>(
                sp => sp.GetRequiredService<Taskdeck.Application.Services.CaptureService>());
            services.AddScoped<Taskdeck.Application.Services.NotificationService>();
            services.AddScoped<Taskdeck.Application.Services.INotificationService>(
                sp => sp.GetRequiredService<Taskdeck.Application.Services.NotificationService>());

            // Stdio identity: maps the OS process owner to the local default user.
            services.AddScoped<IUserContextProvider, StdioUserContextProvider>();

            // MCP server: stdio transport + all resources and tools.
            services.AddMcpServer()
                .WithStdioServerTransport()
                .WithResources<BoardResources>()
                .WithResources<CaptureResources>()
                .WithResources<ProposalResources>()
                .WithTools<ReadTools>()
                .WithTools<WriteTools>()
                .WithTools<ProposalTools>();
        })
        .Build();

    // Apply EF Core migrations before starting the MCP host (mirrors web mode behaviour).
    using (var scope = mcpHost.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<Taskdeck.Infrastructure.Persistence.TaskdeckDbContext>();
        dbContext.Database.Migrate();
    }

    await mcpHost.RunAsync();
    return;
}
// ── End MCP modes ───────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// ---- First-run bootstrap (must run before services are registered) ----------
// Registers appsettings.local.json so previously generated secrets are loaded,
// then generates a JWT secret if none is configured.
builder.AddLocalConfigFile();
using (var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole()))
{
    var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("FirstRun");
    builder.RunFirstRunChecks(bootstrapLogger);
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

// Bind configuration settings (observability, rate limiting, security headers, JWT, etc.)
builder.Services.AddTaskdeckSettings(
    builder.Configuration,
    builder.Environment,
    out var observabilitySettings,
    out var rateLimitingSettings,
    out var jwtSettings,
    out var gitHubOAuthSettings);

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
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithResources<BoardResources>()
    .WithResources<CaptureResources>()
    .WithResources<ProposalResources>()
    .WithTools<ReadTools>()
    .WithTools<WriteTools>()
    .WithTools<ProposalTools>();

// Add JWT Authentication (with optional GitHub OAuth)
builder.Services.AddTaskdeckAuthentication(jwtSettings, gitHubOAuthSettings);

// Add OpenTelemetry observability
builder.Services.AddTaskdeckObservability(observabilitySettings);

// Add worker services (LLM queue, proposal housekeeping, outbound webhooks)
builder.Services.AddTaskdeckWorkers(builder.Configuration, builder.Environment);

// Add CORS
builder.Services.AddTaskdeckCors(builder.Configuration, builder.Environment.IsDevelopment());

// Add rate limiting
builder.Services.AddTaskdeckRateLimiting(rateLimitingSettings);

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

public partial class Program { }
