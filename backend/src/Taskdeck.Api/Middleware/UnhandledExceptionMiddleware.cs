using Microsoft.Data.Sqlite;
using Taskdeck.Api.Contracts;
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
            var exceptionSummary = SensitiveDataRedactor.SummarizeException(ex);
            _logger.LogInformation(
                "Request was canceled while processing {Method} {Path} (CorrelationId: {CorrelationId}). {ExceptionSummary}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier,
                exceptionSummary);
        }
        catch (Exception ex)
        {
            var exceptionSummary = SensitiveDataRedactor.SummarizeException(ex);
            var classification = ClassifyException(ex);
            _logger.LogError(
                "Unhandled exception while processing {Method} {Path} (CorrelationId: {CorrelationId}). " +
                "ExceptionType: {ExceptionType}; RootExceptionType: {RootExceptionType}; " +
                "SqliteErrorCode: {SqliteErrorCode}; " +
                "SqliteExtendedErrorCode: {SqliteExtendedErrorCode}. {ExceptionSummary}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier,
                classification.ExceptionType,
                classification.RootExceptionType,
                classification.SqliteErrorCode,
                classification.SqliteExtendedErrorCode,
                exceptionSummary);

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
        var rootException = exception;
        int? sqliteErrorCode = null;
        int? sqliteExtendedErrorCode = null;
        var depth = 0;

        for (Exception? current = exception;
             current is not null && depth < MaxExceptionClassificationDepth;
             current = current.InnerException)
        {
            rootException = current;
            if (sqliteErrorCode is null && current is SqliteException sqliteException)
            {
                sqliteErrorCode = sqliteException.SqliteErrorCode;
                sqliteExtendedErrorCode = sqliteException.SqliteExtendedErrorCode;
            }

            depth += 1;
        }

        return new ExceptionClassification(
            BoundTypeName(exception.GetType()),
            BoundTypeName(rootException.GetType()),
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
        string RootExceptionType,
        int? SqliteErrorCode,
        int? SqliteExtendedErrorCode);
}
