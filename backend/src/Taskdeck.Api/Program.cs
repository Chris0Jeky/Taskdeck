using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Infrastructure (DbContext, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

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
builder.Services.AddScoped<ExportImportService>();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
