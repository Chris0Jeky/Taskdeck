using Taskdeck.Api.Extensions;
using Taskdeck.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Configure the HTTP request pipeline
app.ConfigureTaskdeckPipeline(rateLimitingSettings);

app.Run();

public partial class Program { }
