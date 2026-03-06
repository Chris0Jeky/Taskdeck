using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Middleware;

public sealed class UnhandledExceptionMiddleware
{
    private const string GenericUnexpectedErrorMessage = "An unexpected error occurred.";

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
            _logger.LogError(
                "Unhandled exception while processing {Method} {Path} (CorrelationId: {CorrelationId}). {ExceptionSummary}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier,
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
}
