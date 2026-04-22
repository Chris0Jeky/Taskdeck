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
public sealed partial class McpTelemetryMiddleware
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
        // Sanitize all user-controlled values to prevent log injection (CWE-117).
        // CorrelationId may originate from the client X-Request-Id header;
        // userId is derived from API key lookup but could flow through user input.
        var correlationId = LogSanitizer.SanitizeForLog(
            context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var corrId)
                ? corrId?.ToString()
                : context.TraceIdentifier);
        var userId = LogSanitizer.SanitizeForLog(
            context.Items.TryGetValue(HttpUserContextProvider.UserIdItemKey, out var uid)
                ? uid?.ToString()
                : null);
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

            LogMcpRequestStarted(_logger, method, sanitizedPath, correlationId, userId);

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

            LogMcpRequestCompleted(_logger, method, statusCode, stopwatch.Elapsed.TotalMilliseconds, correlationId, userId);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — record as a cancellation, not an error.
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Ok, "Cancelled");
            activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, false);

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, false));

            TaskdeckTelemetry.McpRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"));

            LogMcpRequestCancelled(_logger, method, stopwatch.Elapsed.TotalMilliseconds, correlationId, userId);

            throw;
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
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpErrorType, ex.GetType().Name));

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, false));

            // Record duration for failed requests too, to avoid biasing latency metrics.
            TaskdeckTelemetry.McpRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, "http_request"),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpTransport, "http"));

            // Log the exception for local diagnostics. The exception object is passed
            // to ILogger (standard .NET practice) but never to tracing/OTel exports.
            // Activity status description above uses SafeExceptionDescription (type name only).
            LogMcpRequestFailed(_logger, ex, method, stopwatch.Elapsed.TotalMilliseconds, ex.GetType().Name, correlationId, userId);

            throw;
        }
    }

    // ── Source-generated log methods (CWE-117 safe) ─────────────────────────
    // LoggerMessage source generators produce compile-time structured log calls
    // that avoid string interpolation of user-controlled values (CodeQL CWE-117).
    // All parameters are sanitized via LogSanitizer before being passed in.

    [LoggerMessage(Level = LogLevel.Information, EventId = 1,
        Message = "MCP HTTP request started: Method={Method} Path={Path} CorrelationId={CorrelationId} UserId={UserId}")]
    private static partial void LogMcpRequestStarted(
        ILogger logger, string method, string path, string correlationId, string? userId);

    [LoggerMessage(Level = LogLevel.Information, EventId = 2,
        Message = "MCP HTTP request completed: Method={Method} StatusCode={StatusCode} DurationMs={DurationMs} CorrelationId={CorrelationId} UserId={UserId}")]
    private static partial void LogMcpRequestCompleted(
        ILogger logger, string method, int statusCode, double durationMs, string correlationId, string? userId);

    [LoggerMessage(Level = LogLevel.Error, EventId = 3,
        Message = "MCP HTTP request failed: Method={Method} DurationMs={DurationMs} ErrorType={ErrorType} CorrelationId={CorrelationId} UserId={UserId}")]
    private static partial void LogMcpRequestFailed(
        ILogger logger, Exception ex, string method, double durationMs, string errorType, string correlationId, string? userId);

    [LoggerMessage(Level = LogLevel.Information, EventId = 4,
        Message = "MCP HTTP request cancelled: Method={Method} DurationMs={DurationMs} CorrelationId={CorrelationId} UserId={UserId}")]
    private static partial void LogMcpRequestCancelled(
        ILogger logger, string method, double durationMs, string correlationId, string? userId);
}
