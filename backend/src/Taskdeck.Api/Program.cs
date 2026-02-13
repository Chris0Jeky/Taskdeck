using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Services;
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
builder.Services.AddScoped<ILlmProvider, MockLlmProvider>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();

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

// Add Worker settings and background workers
var workerSettings = builder.Configuration.GetSection("Workers").Get<WorkerSettings>() ?? new WorkerSettings();
builder.Services.AddSingleton(workerSettings);
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
