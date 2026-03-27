using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
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
using Taskdeck.Api.RateLimiting;
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
var rateLimitingSettings = builder.Configuration
    .GetSection("RateLimiting")
    .Get<RateLimitingSettings>() ?? new RateLimitingSettings();
builder.Services.AddSingleton(rateLimitingSettings);

var securityHeadersSection = builder.Configuration.GetSection("SecurityHeaders");
var securityHeadersSettings = securityHeadersSection.Get<SecurityHeadersSettings>() ?? new SecurityHeadersSettings();
if (builder.Environment.IsDevelopment() && securityHeadersSection["EnableHsts"] is null)
{
    securityHeadersSettings.EnableHsts = false;
}
builder.Services.AddSingleton(securityHeadersSettings);

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
builder.Services.AddScoped<CardCommentService>();
builder.Services.AddScoped<LabelService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AuthorizationService>();
builder.Services.AddScoped<IAuthorizationService>(sp => sp.GetRequiredService<AuthorizationService>());
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BoardAccessService>();
builder.Services.AddScoped<IExportImportService, ExportImportService>();
builder.Services.AddScoped<IExternalImportService, ExternalImportService>();
builder.Services.AddScoped<IExternalImportAdapter, CsvExternalImportAdapter>();
builder.Services.AddScoped<LlmQueueService>();
builder.Services.AddScoped<ICaptureService, CaptureService>();
builder.Services.AddScoped<ICaptureTriageService, CaptureTriageService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<IAutomationProposalService, AutomationProposalService>();
builder.Services.AddScoped<IAutomationPolicyEngine, AutomationPolicyEngine>();
builder.Services.AddScoped<IAutomationPlannerService, AutomationPlannerService>();
builder.Services.AddScoped<IAutomationExecutorService, AutomationExecutorService>();
builder.Services.AddScoped<IArchiveRecoveryService, ArchiveRecoveryService>();
builder.Services.AddScoped<IOpsCliService, OpsCliService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
builder.Services.AddScoped<IStarterPackManifestValidator, StarterPackManifestValidator>();
builder.Services.AddScoped<IStarterPackApplyService, StarterPackApplyService>();
builder.Services.AddScoped<IStarterPackCatalogService, StarterPackCatalogService>();
builder.Services.AddScoped<IOutboundWebhookService, OutboundWebhookService>();
builder.Services.AddScoped<SignalRBoardRealtimeNotifier>();
builder.Services.AddScoped<WebhookBoardMutationNotifier>();
builder.Services.AddScoped<IBoardRealtimeNotifier, CompositeBoardRealtimeNotifier>();
builder.Services.AddSingleton<IBoardPresenceTracker, InMemoryBoardPresenceTracker>();

// LLM provider settings and deterministic provider selection policy
var llmProviderSettings = builder.Configuration.GetSection("Llm").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
builder.Services.AddSingleton(llmProviderSettings);

builder.Services.AddHttpClient<OpenAiLlmProvider>((sp, client) =>
{
    var settings = sp.GetRequiredService<LlmProviderSettings>();
    var timeoutSeconds = settings.OpenAi?.TimeoutSeconds > 0 ? settings.OpenAi.TimeoutSeconds : 30;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddHttpClient<GeminiLlmProvider>((sp, client) =>
{
    var settings = sp.GetRequiredService<LlmProviderSettings>();
    var timeoutSeconds = settings.Gemini?.TimeoutSeconds > 0 ? settings.Gemini.TimeoutSeconds : 30;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
});
builder.Services.AddHttpClient("OutboundWebhookDelivery", (_, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<OutboundWebhookSecuritySettings>();
    return new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = (context, cancellationToken) =>
            OutboundWebhookConnectCallback.ConnectAsync(
                context,
                settings.AllowLocalhostEndpoints,
                cancellationToken)
    };
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

    return decision.ProviderKind switch
    {
        LlmProviderKind.OpenAi => sp.GetRequiredService<OpenAiLlmProvider>(),
        LlmProviderKind.Gemini => sp.GetRequiredService<GeminiLlmProvider>(),
        _ => sp.GetRequiredService<MockLlmProvider>()
    };
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
var outboundWebhookSecuritySection = builder.Configuration.GetSection("OutboundWebhooks:Security");
var outboundWebhookSecuritySettings = outboundWebhookSecuritySection.Get<OutboundWebhookSecuritySettings>() ?? new OutboundWebhookSecuritySettings();
if (builder.Environment.IsDevelopment() && outboundWebhookSecuritySection["AllowLocalhostEndpoints"] is null)
{
    outboundWebhookSecuritySettings.AllowLocalhostEndpoints = true;
}
builder.Services.AddSingleton(outboundWebhookSecuritySettings);
builder.Services.AddSingleton<WorkerHeartbeatRegistry>();
builder.Services.AddHostedService<LlmQueueToProposalWorker>();
builder.Services.AddHostedService<ProposalHousekeepingWorker>();
builder.Services.AddHostedService<OutboundWebhookDeliveryWorker>();

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
                // Raw exception events can capture sensitive request/body data before our
                // sanitized logging path runs, so keep automatic exception recording off.
                options.RecordException = false;
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

var corsAllowedOrigins = ResolveCorsAllowedOrigins(builder.Configuration, builder.Environment.IsDevelopment());
var forwardedHeadersOptions = BuildForwardedHeadersOptions(builder.Configuration);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsAllowedOrigins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options => ConfigureRateLimiting(options, rateLimitingSettings));

var app = builder.Build();

if (rateLimitingSettings.Enabled &&
    !app.Environment.IsDevelopment() &&
    forwardedHeadersOptions is null)
{
    app.Logger.LogWarning(
        "Rate limiting is enabled without trusted forwarded-header configuration. " +
        "If Taskdeck runs behind a reverse proxy/load balancer, AuthPerIp may collapse users into shared buckets. " +
        "Configure ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks.");
}

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

if (forwardedHeadersOptions is not null)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseCors("AllowFrontend");
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<UnhandledExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseAuthentication();
if (rateLimitingSettings.Enabled)
{
    app.UseRateLimiter();
}
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

static IReadOnlyList<string> ResolveCorsAllowedOrigins(IConfiguration configuration, bool isDevelopment)
{
    var defaultAllowedOrigins = new[] { "http://localhost:5173", "http://localhost:5174" };
    if (!isDevelopment)
    {
        return defaultAllowedOrigins;
    }

    var configuredDevelopmentOrigins = configuration
        .GetSection("Cors:DevelopmentAllowedOrigins")
        .GetChildren()
        .Select(child => child.Value)
        .OfType<string>()
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

    if (configuredDevelopmentOrigins.Length == 0 &&
        !string.IsNullOrWhiteSpace(configuration["Cors:DevelopmentAllowedOrigins"]))
    {
        configuredDevelopmentOrigins = configuration["Cors:DevelopmentAllowedOrigins"]!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    var developmentFallbackOrigins = new[] { "http://localhost:4173", "http://localhost:5001" };

    return defaultAllowedOrigins
        .Concat(configuredDevelopmentOrigins)
        .Concat(developmentFallbackOrigins)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(NormalizeCorsOrigin)
        // Origin host matching is case-insensitive, so collapse mixed-case duplicates.
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string NormalizeCorsOrigin(string origin)
{
    var trimmedOrigin = origin.Trim();
    if (!Uri.TryCreate(trimmedOrigin, UriKind.Absolute, out var parsedOrigin))
    {
        throw new InvalidOperationException(
            $"Invalid Cors:DevelopmentAllowedOrigins value \"{trimmedOrigin}\". Provide an absolute http(s) origin.");
    }

    if (!string.Equals(parsedOrigin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(parsedOrigin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Invalid Cors:DevelopmentAllowedOrigins value \"{trimmedOrigin}\". Only http and https schemes are supported.");
    }

    return parsedOrigin.GetLeftPart(UriPartial.Authority);
}

static void ConfigureRateLimiting(RateLimiterOptions options, RateLimitingSettings settings)
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.HttpContext.Response.HasStarted)
        {
            return;
        }

        var retryAfterSeconds = 1;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);
        }

        var policyName = context.HttpContext
            .GetEndpoint()?
            .Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName;

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(policyName))
        {
            context.HttpContext.Response.Headers["X-RateLimit-Policy"] = policyName;
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                ErrorCodes.TooManyRequests,
                $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds."),
            cancellationToken);
    };

    options.AddPolicy(RateLimitingPolicyNames.AuthPerIp, httpContext =>
    {
        var partitionKey = $"auth-ip:{ResolveClientAddress(httpContext)}";
        return BuildFixedWindowPartition(partitionKey, settings.AuthPerIp);
    });

    options.AddPolicy(RateLimitingPolicyNames.HotPathPerUser, httpContext =>
    {
        var partitionKey = $"hot-user:{ResolveUserOrClientIdentifier(httpContext)}";
        return BuildFixedWindowPartition(partitionKey, settings.HotPathPerUser);
    });

    options.AddPolicy(RateLimitingPolicyNames.CaptureWritePerUser, httpContext =>
    {
        var partitionKey = $"capture-user:{ResolveUserOrClientIdentifier(httpContext)}";
        return BuildFixedWindowPartition(partitionKey, settings.CaptureWritePerUser);
    });
}

static RateLimitPartition<string> BuildFixedWindowPartition(string partitionKey, RateLimitPolicySettings policy)
{
    var permitLimit = Math.Max(policy.PermitLimit, 1);
    var windowSeconds = Math.Max(policy.WindowSeconds, 1);

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
}

static string ResolveUserOrClientIdentifier(HttpContext context)
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");
    return !string.IsNullOrWhiteSpace(userId)
        ? userId
        : ResolveClientAddress(context);
}

static string ResolveClientAddress(HttpContext context)
{
    // Trust only connection metadata here. Raw forwarded headers are caller-controlled unless
    // forwarded-header middleware is explicitly configured with trusted proxies/networks.
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static ForwardedHeadersOptions? BuildForwardedHeadersOptions(IConfiguration configuration)
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

static IReadOnlyList<string> ResolveConfigValues(IConfiguration configuration, string key)
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

static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseForwardedHeaderNetwork(string value)
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

public partial class Program { }
