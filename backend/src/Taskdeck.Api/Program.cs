using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Realtime;
using Taskdeck.Api.Telemetry;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var observabilitySettings = builder.Configuration
    .GetSection("Observability")
    .Get<ObservabilitySettings>() ?? new ObservabilitySettings();
builder.Services.AddSingleton(observabilitySettings);

// Add Infrastructure (DbContext, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

var databaseExportImportSettings = new DatabaseExportImportSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection"),
    MaxImportBytes = builder.Configuration.GetValue<int?>("ExportImport:MaxDatabaseImportBytes")
        ?? DatabaseExportImportSettings.DefaultMaxImportBytes
};
builder.Services.AddSingleton(databaseExportImportSettings);

// Add Application Services
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<LabelService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AuthorizationService>();
builder.Services.AddScoped<IAuthorizationService>(sp => sp.GetRequiredService<AuthorizationService>());
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BoardAccessService>();
builder.Services.AddScoped<IExportImportService, ExportImportService>();
builder.Services.AddScoped<LlmQueueService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<IAutomationProposalService, AutomationProposalService>();
builder.Services.AddScoped<IAutomationPolicyEngine, AutomationPolicyEngine>();
builder.Services.AddScoped<IAutomationPlannerService, AutomationPlannerService>();
builder.Services.AddScoped<IAutomationExecutorService, AutomationExecutorService>();
builder.Services.AddScoped<IArchiveRecoveryService, ArchiveRecoveryService>();
builder.Services.AddScoped<IOpsCliService, OpsCliService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddScoped<IStarterPackManifestValidator, StarterPackManifestValidator>();
builder.Services.AddScoped<IStarterPackApplyService, StarterPackApplyService>();
builder.Services.AddScoped<IStarterPackCatalogService, StarterPackCatalogService>();
builder.Services.AddSingleton<IBoardRealtimeNotifier, SignalRBoardRealtimeNotifier>();

// LLM provider settings and deterministic provider selection policy
var llmProviderSettings = builder.Configuration.GetSection("Llm").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
builder.Services.AddSingleton(llmProviderSettings);

builder.Services.AddHttpClient<OpenAiLlmProvider>((sp, client) =>
{
    var settings = sp.GetRequiredService<LlmProviderSettings>();
    var timeoutSeconds = settings.OpenAi.TimeoutSeconds <= 0 ? 30 : settings.OpenAi.TimeoutSeconds;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddScoped<MockLlmProvider>();
builder.Services.AddScoped<ILlmProvider>(sp =>
{
    var settings = sp.GetRequiredService<LlmProviderSettings>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var decision = LlmProviderSelectionPolicy.Evaluate(settings, environment.EnvironmentName);

    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Taskdeck.Api.LlmProviderSelection");
    logger.LogInformation(
        "Resolved ILlmProvider to {ProviderKind}. Reason: {Reason}",
        decision.ProviderKind,
        decision.Reason);

    return decision.ProviderKind == LlmProviderKind.OpenAi
        ? sp.GetRequiredService<OpenAiLlmProvider>()
        : sp.GetRequiredService<MockLlmProvider>();
});

// Add IUserContext for claim-based identity
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Taskdeck.Application.Interfaces.IUserContext, Taskdeck.Infrastructure.Identity.UserContext>();

// Add JwtSettings (required by AuthenticationService)
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddSingleton(jwtSettings);

// Development sandbox bypass settings (dev environment only)
var sandboxSettings = builder.Configuration.GetSection("DevelopmentSandbox").Get<DevelopmentSandboxSettings>() ?? new DevelopmentSandboxSettings();
sandboxSettings.Enabled = sandboxSettings.Enabled && builder.Environment.IsDevelopment();
builder.Services.AddSingleton(sandboxSettings);

// Worker settings and runtime services
var workerSettings = builder.Configuration.GetSection("Workers").Get<WorkerSettings>() ?? new WorkerSettings();
builder.Services.AddSingleton(workerSettings);
builder.Services.AddSingleton<WorkerHeartbeatRegistry>();
builder.Services.AddHostedService<LlmQueueToProposalWorker>();
builder.Services.AddHostedService<ProposalHousekeepingWorker>();

if (observabilitySettings.EnableOpenTelemetry)
{
    var openTelemetryBuilder = builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(observabilitySettings.ServiceName));

    openTelemetryBuilder.WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddSource(TaskdeckTelemetry.ActivitySourceName);

        if (!string.IsNullOrWhiteSpace(observabilitySettings.OtlpEndpoint) &&
            Uri.TryCreate(observabilitySettings.OtlpEndpoint, UriKind.Absolute, out var traceEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = traceEndpoint;
                options.Protocol = OtlpExportProtocol.Grpc;
            });
        }

        if (observabilitySettings.EnableConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }
    });

    openTelemetryBuilder.WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(TaskdeckTelemetry.MeterName);

        if (!string.IsNullOrWhiteSpace(observabilitySettings.OtlpEndpoint) &&
            Uri.TryCreate(observabilitySettings.OtlpEndpoint, UriKind.Absolute, out var metricEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = metricEndpoint;
                options.Protocol = OtlpExportProtocol.Grpc;
            });
        }

        if (observabilitySettings.EnableConsoleExporter)
        {
            metrics.AddConsoleExporter(
                (_, readerOptions) =>
                {
                    readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                        Math.Max(observabilitySettings.MetricExportIntervalSeconds, 5) * 1000;
                });
        }
    });
}

// Add JWT Authentication
if (!string.IsNullOrWhiteSpace(jwtSettings.SecretKey) &&
    jwtSettings.SecretKey.Length >= 32 &&
    !string.IsNullOrWhiteSpace(jwtSettings.Issuer) &&
    !string.IsNullOrWhiteSpace(jwtSettings.Audience))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/boards"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    if (context.Response.HasStarted)
                    {
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers[HeaderNames.WWWAuthenticate] =
                        BuildWwwAuthenticateHeaderValue(context.Error, context.ErrorDescription);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                        ErrorCodes.Unauthorized,
                        "Authentication is required to access this resource."));
                },
                OnForbidden = async context =>
                {
                    if (context.Response.HasStarted)
                    {
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                        ErrorCodes.Forbidden,
                        "You do not have permission to access this resource."));
                }
            };
        });
}

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<UnhandledExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<BoardsHub>("/hubs/boards");

app.Run();

static string BuildWwwAuthenticateHeaderValue(string? error, string? errorDescription)
{
    if (string.IsNullOrWhiteSpace(error))
    {
        return "Bearer";
    }

    var escapedError = EscapeAuthHeaderValue(error);
    if (string.IsNullOrWhiteSpace(errorDescription))
    {
        return $"Bearer error=\"{escapedError}\"";
    }

    return $"Bearer error=\"{escapedError}\", error_description=\"{EscapeAuthHeaderValue(errorDescription)}\"";
}

static string EscapeAuthHeaderValue(string value)
{
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

public partial class Program { }
