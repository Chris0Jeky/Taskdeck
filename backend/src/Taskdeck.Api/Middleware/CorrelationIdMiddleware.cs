using Microsoft.Extensions.Primitives;

namespace Taskdeck.Api.Middleware;

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.Headers.TryGetValue(HeaderName, out StringValues incomingHeader);
        var correlationId = string.IsNullOrWhiteSpace(incomingHeader)
            ? context.TraceIdentifier
            : incomingHeader.ToString();

        context.TraceIdentifier = correlationId;
        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}
