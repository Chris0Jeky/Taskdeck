using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Connectors;

/// <summary>
/// Executes connector operations (e.g., health checks) with timeout, retry, and audit logging.
/// Uses source-generated LoggerMessage methods to avoid log injection (CWE-117).
/// </summary>
public sealed partial class ConnectorExecutionService
{
    private readonly IConnectorProviderRegistry _registry;
    private readonly ILogger<ConnectorExecutionService> _logger;

    /// <summary>
    /// Default timeout for provider operations.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of retries for transient failures.
    /// </summary>
    private const int MaxRetries = 2;

    public ConnectorExecutionService(
        IConnectorProviderRegistry registry,
        ILogger<ConnectorExecutionService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Execute a health check for a specific provider with timeout and retry.
    /// </summary>
    public async Task<Result<ConnectorHealthResult>> CheckProviderHealthAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = _registry.GetById(providerId);
        if (provider == null)
            return Result.Failure<ConnectorHealthResult>(ErrorCodes.NotFound, $"Provider '{providerId}' not found.");

        return await ExecuteWithRetryAsync(
            $"HealthCheck:{providerId}",
            async ct => await provider.CheckHealthAsync(ct),
            cancellationToken);
    }

    /// <summary>
    /// Get capabilities for a specific provider.
    /// </summary>
    public async Task<Result<ConnectorCapabilities>> GetProviderCapabilitiesAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = _registry.GetById(providerId);
        if (provider == null)
            return Result.Failure<ConnectorCapabilities>(ErrorCodes.NotFound, $"Provider '{providerId}' not found.");

        try
        {
            var capabilities = await provider.GetCapabilitiesAsync(cancellationToken);
            return Result.Success(capabilities);
        }
        catch (Exception ex)
        {
            LogGetCapabilitiesFailed(_logger, ex, providerId);
            return Result.Failure<ConnectorCapabilities>(
                ErrorCodes.UnexpectedError,
                "Failed to retrieve provider capabilities.");
        }
    }

    private async Task<Result<T>> ExecuteWithRetryAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(DefaultTimeout);

                var result = await operation(timeoutCts.Token);

                if (attempt > 0)
                {
                    LogOperationRetrySucceeded(_logger, operationName, attempt);
                }

                return Result.Success(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller cancelled — do not retry.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // Timeout — may be transient.
                lastException = ex;
                LogOperationTimedOut(_logger, operationName, attempt + 1, MaxRetries + 1);
            }
            catch (HttpRequestException ex)
            {
                // Network error — transient, retry.
                lastException = ex;
                LogOperationHttpError(_logger, ex, operationName, attempt + 1, MaxRetries + 1);
            }
            catch (Exception ex)
            {
                // Non-transient error — do not retry.
                LogOperationNonTransientError(_logger, ex, operationName);
                return Result.Failure<T>(ErrorCodes.UnexpectedError, "Provider operation failed.");
            }

            if (attempt < MaxRetries)
            {
                // Simple linear backoff.
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
            }
        }

        LogOperationRetriesExhausted(_logger, lastException, operationName, MaxRetries, MaxRetries + 1);

        return Result.Failure<T>(ErrorCodes.UnexpectedError, "Provider operation failed after retries.");
    }

    // ── Source-generated log methods (CWE-117 safe) ─────────────────────────

    [LoggerMessage(Level = LogLevel.Error, EventId = 1,
        Message = "Failed to get capabilities for provider {ProviderId}")]
    private static partial void LogGetCapabilitiesFailed(
        ILogger logger, Exception ex, string providerId);

    [LoggerMessage(Level = LogLevel.Information, EventId = 2,
        Message = "Operation {Operation} succeeded on retry attempt {Attempt}")]
    private static partial void LogOperationRetrySucceeded(
        ILogger logger, string operation, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 3,
        Message = "Operation {Operation} timed out (attempt {Attempt}/{TotalAttempts})")]
    private static partial void LogOperationTimedOut(
        ILogger logger, string operation, int attempt, int totalAttempts);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 4,
        Message = "Operation {Operation} failed with HTTP error (attempt {Attempt}/{TotalAttempts})")]
    private static partial void LogOperationHttpError(
        ILogger logger, Exception ex, string operation, int attempt, int totalAttempts);

    [LoggerMessage(Level = LogLevel.Error, EventId = 5,
        Message = "Operation {Operation} failed with non-transient error")]
    private static partial void LogOperationNonTransientError(
        ILogger logger, Exception ex, string operation);

    [LoggerMessage(Level = LogLevel.Error, EventId = 6,
        Message = "Operation {Operation} exhausted all {MaxRetries} retries ({TotalAttempts} total attempts)")]
    private static partial void LogOperationRetriesExhausted(
        ILogger logger, Exception? ex, string operation, int maxRetries, int totalAttempts);
}
