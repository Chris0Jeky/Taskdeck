using Microsoft.Data.Sqlite;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Telemetry;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Middleware;

public sealed class UnhandledExceptionMiddleware
{
    private const string GenericUnexpectedErrorMessage = "An unexpected error occurred.";
    private const int MaxExceptionClassificationDepth = 8;
    private const int MaxExceptionTypeLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<UnhandledExceptionMiddleware> _logger;

    public UnhandledExceptionMiddleware(
        RequestDelegate next,
        ILogger<UnhandledExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException ex) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Request was canceled while processing {Method} {Path} (CorrelationId: {CorrelationId}; " +
                "ExceptionType: {ExceptionType}).",
                LogSanitizer.SanitizeForLog(context.Request.Method),
                LogSanitizer.SanitizeForLog(context.Request.Path.Value),
                LogSanitizer.SanitizeForLog(context.TraceIdentifier),
                BoundTypeName(ex.GetType()));
        }
        catch (Exception ex)
        {
            var classification = ClassifyException(ex);
            _logger.LogError(
                "Unhandled exception while processing {Method} {Path} (CorrelationId: {CorrelationId}). " +
                "ExceptionType: {ExceptionType}; LastInspectedExceptionType: {LastInspectedExceptionType}; " +
                "ClassificationTruncated: {ClassificationTruncated}; " +
                "SqliteErrorCode: {SqliteErrorCode}; " +
                "SqliteExtendedErrorCode: {SqliteExtendedErrorCode}.",
                LogSanitizer.SanitizeForLog(context.Request.Method),
                LogSanitizer.SanitizeForLog(context.Request.Path.Value),
                LogSanitizer.SanitizeForLog(context.TraceIdentifier),
                classification.ExceptionType,
                classification.LastInspectedExceptionType,
                classification.ClassificationTruncated,
                classification.SqliteErrorCode,
                classification.SqliteExtendedErrorCode);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                ErrorCodes.UnexpectedError,
                GenericUnexpectedErrorMessage));
        }
    }

    private static ExceptionClassification ClassifyException(Exception exception)
    {
        var lastInspectedException = exception;
        int? sqliteErrorCode = null;
        int? sqliteExtendedErrorCode = null;
        var classificationTruncated = false;
        var pending = new Queue<Exception>();
        var scheduled = new HashSet<Exception>(ReferenceEqualityComparer.Instance)
        {
            exception
        };
        pending.Enqueue(exception);

        while (pending.TryDequeue(out var current))
        {
            lastInspectedException = current;
            if (sqliteErrorCode is null && current is SqliteException sqliteException)
            {
                sqliteErrorCode = sqliteException.SqliteErrorCode;
                sqliteExtendedErrorCode = sqliteException.SqliteExtendedErrorCode;
            }

            IReadOnlyList<Exception> innerExceptions = current switch
            {
                AggregateException aggregateException => aggregateException.InnerExceptions,
                { InnerException: { } innerException } => [innerException],
                _ => []
            };

            foreach (var candidate in innerExceptions)
            {
                if (scheduled.Contains(candidate))
                {
                    continue;
                }

                if (scheduled.Count >= MaxExceptionClassificationDepth)
                {
                    classificationTruncated = true;
                    continue;
                }

                scheduled.Add(candidate);
                pending.Enqueue(candidate);
            }
        }

        return new ExceptionClassification(
            BoundTypeName(exception.GetType()),
            BoundTypeName(lastInspectedException.GetType()),
            classificationTruncated,
            sqliteErrorCode,
            sqliteExtendedErrorCode);
    }

    private static string BoundTypeName(Type exceptionType)
    {
        var name = exceptionType.Name;
        return name.Length <= MaxExceptionTypeLength
            ? name
            : name[..MaxExceptionTypeLength];
    }

    private sealed record ExceptionClassification(
        string ExceptionType,
        string LastInspectedExceptionType,
        bool ClassificationTruncated,
        int? SqliteErrorCode,
        int? SqliteExtendedErrorCode);
}
