using System.Diagnostics;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Telemetry;
using Taskdeck.Infrastructure.Mcp;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// HTTP middleware that adds structured logging, tracing spans, and metrics
/// to MCP HTTP requests. Only activates on the /mcp path.
///
/// This middleware wraps the MCP SDK pipeline without modifying it. Failures
/// in telemetry are caught and logged — they never break MCP operations.
/// </summary>
public sealed class McpTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpTelemetryMiddleware> _logger;

    private const string McpPathPrefix = "/mcp";

    public McpTelemetryMiddleware(RequestDelegate next, ILogger<McpTelemetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(McpPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var corrId)
            ? corrId?.ToString()
            : context.TraceIdentifier;
        var userId = context.Items.TryGetValue(HttpUserContextProvider.UserIdItemKey, out var uid)
            ? uid?.ToString()
            : null;
        // Sanitize user-controlled method/path to prevent log injection (CWE-117).
        var method = LogSanitizer.SanitizeForLog(context.Request.Method);
        var sanitizedPath = LogSanitizer.SanitizeForLog(context.Request.Path.Value);

        using var activity = TaskdeckTelemetry.McpActivitySource.StartActivity(
            "mcp.request",
            ActivityKind.Server);

        try
        {
            activity?.SetTag(TaskdeckTelemetryTags.McpOperationType, "http_request");
            activity?.SetTag(TaskdeckTelemetryTags.McpTransport, "http");
            activity?.SetTag(TaskdeckTelemetryTags.CorrelationId, correlationId);
            if (userId is not null)
            {
                activity?.SetTag(TaskdeckTelemetryTags.UserId, userId);
            }

            _logger.LogInformation(
                "MCP HTTP request started: Method={Method} Path={Path} CorrelationId={CorrelationId} UserId={UserId}",
                method,
                sanitizedPath,
                correlationId,
                userId);

            await _next(context);

            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var success = statusCode < 400;

            activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, success);
            activity?.SetTag("http.status_code", statusCode);

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, success));

            TaskdeckTelemetry.McpRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"));

            _logger.LogInformation(
                "MCP HTTP request completed: Method={Method} StatusCode={StatusCode} DurationMs={DurationMs} CorrelationId={CorrelationId} UserId={UserId}",
                method,
                statusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                correlationId,
                userId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Use exception type name only to avoid leaking user content in trace exports.
            activity?.SetStatus(ActivityStatusCode.Error, LogSanitizer.SafeExceptionDescription(ex));
            activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, false);
            activity?.SetTag(TaskdeckTelemetryTags.McpErrorType, ex.GetType().Name);

            TaskdeckTelemetry.McpErrors.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpErrorType, ex.GetType().Name));

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, false));

            // Record duration for failed requests too, to avoid biasing latency metrics.
            TaskdeckTelemetry.McpRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"));

            _logger.LogError(ex,
                "MCP HTTP request failed: Method={Method} DurationMs={DurationMs} ErrorType={ErrorType} CorrelationId={CorrelationId} UserId={UserId}",
                method,
                stopwatch.Elapsed.TotalMilliseconds,
                ex.GetType().Name,
                correlationId,
                userId);

            throw;
        }
    }
}
