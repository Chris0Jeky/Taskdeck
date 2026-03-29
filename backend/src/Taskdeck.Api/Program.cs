using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.FirstRun;
using Taskdeck.Infrastructure;

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
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
