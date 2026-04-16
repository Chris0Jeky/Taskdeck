using System.Diagnostics;
using Taskdeck.Api.Telemetry;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// Provides structured logging, tracing spans, and metrics for individual
/// MCP tool and resource operations. Injected into MCP handler classes via DI.
///
/// Design principles:
/// - Never throws: all telemetry failures are caught and logged at Debug level.
/// - Never logs sensitive data: API key values, full request/response bodies, or
///   user content are excluded. Tool arguments are logged at Debug level only,
///   truncated to <see cref="MaxArgumentLogLength"/> characters.
/// - Non-intrusive: callers use <see cref="BeginOperation"/> to start tracking
///   and <see cref="McpOperationScope.Complete"/> / <see cref="McpOperationScope.Fail(Exception)"/>
///   to record the outcome.
/// </summary>
public sealed class McpOperationLogger
{
    private readonly ILogger<McpOperationLogger> _logger;

    /// <summary>Maximum characters to log for tool arguments at Debug level.</summary>
    internal const int MaxArgumentLogLength = 200;

    public McpOperationLogger(ILogger<McpOperationLogger> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Begins tracking an MCP operation (tool call or resource read).
    /// Returns a scope that the caller disposes when the operation finishes.
    /// </summary>
    /// <param name="operationType">"tool" or "resource"</param>
    /// <param name="operationName">The tool or resource name (e.g. "search_cards", "boards")</param>
    /// <param name="userId">Authenticated user ID, or null for stdio.</param>
    /// <param name="transport">"http" or "stdio"</param>
    /// <param name="arguments">Optional tool arguments, logged at Debug level only and truncated.</param>
    public McpOperationScope BeginOperation(
        string operationType,
        string operationName,
        Guid? userId = null,
        string transport = "http",
        string? arguments = null)
    {
        var activity = TaskdeckTelemetry.McpActivitySource.StartActivity(
            $"mcp.{operationType}.{operationName}",
            ActivityKind.Internal);

        activity?.SetTag(TaskdeckTelemetryTags.McpOperationType, operationType);
        activity?.SetTag(TaskdeckTelemetryTags.McpOperationName, operationName);
        activity?.SetTag(TaskdeckTelemetryTags.McpTransport, transport);
        if (userId.HasValue)
        {
            activity?.SetTag(TaskdeckTelemetryTags.UserId, userId.Value.ToString());
        }

        _logger.LogInformation(
            "MCP {OperationType} started: Name={OperationName} UserId={UserId} Transport={Transport}",
            operationType,
            operationName,
            userId,
            transport);

        if (arguments is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            var truncated = arguments.Length > MaxArgumentLogLength
                ? string.Concat(arguments.AsSpan(0, MaxArgumentLogLength), "...[truncated]")
                : arguments;
            _logger.LogDebug(
                "MCP {OperationType} arguments: Name={OperationName} Args={Arguments}",
                operationType,
                operationName,
                truncated);
        }

        return new McpOperationScope(_logger, activity, operationType, operationName, userId, transport);
    }
}

/// <summary>
/// Tracks a single MCP operation's lifecycle. Records duration, outcome,
/// and error details on completion.
/// </summary>
public sealed class McpOperationScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly Activity? _activity;
    private readonly string _operationType;
    private readonly string _operationName;
    private readonly Guid? _userId;
    private readonly string _transport;
    private readonly Stopwatch _stopwatch;
    private bool _completed;

    internal McpOperationScope(
        ILogger logger,
        Activity? activity,
        string operationType,
        string operationName,
        Guid? userId,
        string transport)
    {
        _logger = logger;
        _activity = activity;
        _operationType = operationType;
        _operationName = operationName;
        _userId = userId;
        _transport = transport;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Records a successful completion with optional response size.
    /// </summary>
    public void Complete(int? responseSizeBytes = null)
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();

        try
        {
            _activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, true);
            if (responseSizeBytes.HasValue)
            {
                _activity?.SetTag("taskdeck.mcp.response_size", responseSizeBytes.Value);
            }

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, true));

            TaskdeckTelemetry.McpRequestDurationMs.Record(_stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName));

            _logger.LogInformation(
                "MCP {OperationType} completed: Name={OperationName} DurationMs={DurationMs} UserId={UserId} Transport={Transport}",
                _operationType,
                _operationName,
                _stopwatch.Elapsed.TotalMilliseconds,
                _userId,
                _transport);
        }
        catch (Exception ex)
        {
            // Telemetry failures must never break MCP operations.
            _logger.LogDebug(ex, "Failed to record MCP operation completion telemetry");
        }
    }

    /// <summary>
    /// Records a failed operation with error details.
    /// </summary>
    public void Fail(Exception exception)
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();

        try
        {
            // Use exception type name only to avoid leaking user content in trace exports.
            _activity?.SetStatus(ActivityStatusCode.Error, LogSanitizer.SafeExceptionDescription(exception));
            _activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, false);
            _activity?.SetTag(TaskdeckTelemetryTags.McpErrorType, exception.GetType().Name);

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, false));

            TaskdeckTelemetry.McpErrors.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpErrorType, exception.GetType().Name));

            TaskdeckTelemetry.McpRequestDurationMs.Record(_stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName));

            _logger.LogError(exception,
                "MCP {OperationType} failed: Name={OperationName} DurationMs={DurationMs} ErrorType={ErrorType} UserId={UserId} Transport={Transport}",
                _operationType,
                _operationName,
                _stopwatch.Elapsed.TotalMilliseconds,
                exception.GetType().Name,
                _userId,
                _transport);
        }
        catch (Exception ex)
        {
            // Telemetry failures must never break MCP operations.
            _logger.LogDebug(ex, "Failed to record MCP operation failure telemetry");
        }
    }

    /// <summary>
    /// Records a failed operation with an error message (when no exception is available).
    /// </summary>
    public void Fail(string errorMessage)
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();

        try
        {
            // Use a fixed description to avoid leaking user content in trace exports.
            _activity?.SetStatus(ActivityStatusCode.Error, "ApplicationError");
            _activity?.SetTag(TaskdeckTelemetryTags.McpSuccess, false);
            _activity?.SetTag(TaskdeckTelemetryTags.McpErrorType, "ApplicationError");

            TaskdeckTelemetry.McpRequests.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpSuccess, false));

            TaskdeckTelemetry.McpErrors.Add(1,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpErrorType, "ApplicationError"));

            TaskdeckTelemetry.McpRequestDurationMs.Record(_stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationType, _operationType),
                new KeyValuePair<string, object?>(TaskdeckTelemetryTags.McpOperationName, _operationName));

            _logger.LogWarning(
                "MCP {OperationType} failed: Name={OperationName} DurationMs={DurationMs} Error={ErrorMessage} UserId={UserId} Transport={Transport}",
                _operationType,
                _operationName,
                _stopwatch.Elapsed.TotalMilliseconds,
                errorMessage,
                _userId,
                _transport);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record MCP operation failure telemetry");
        }
    }

    public void Dispose()
    {
        if (!_completed)
        {
            // If neither Complete nor Fail was called, record as completed
            // (defensive — callers should always call one explicitly).
            Complete();
        }

        _activity?.Dispose();
    }
}
