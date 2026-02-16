using Microsoft.Extensions.Primitives;

namespace Taskdeck.Api.Middleware;

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";
    public const string ItemKey = "CorrelationId";
    private const int MaxCorrelationIdLength = 100;

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
        var requestedCorrelationId = incomingHeader.ToString().Trim();
        var correlationId = context.TraceIdentifier;

        if (!string.IsNullOrWhiteSpace(requestedCorrelationId))
        {
            if (IsValidCorrelationId(requestedCorrelationId))
            {
                correlationId = requestedCorrelationId;
            }
            else
            {
                _logger.LogWarning(
                    "Rejected invalid {HeaderName} value (length: {Length}); falling back to generated trace identifier",
                    HeaderName,
                    requestedCorrelationId.Length);
            }
        }

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

    private static bool IsValidCorrelationId(string correlationId)
    {
        if (correlationId.Length > MaxCorrelationIdLength)
            return false;

        foreach (var ch in correlationId)
        {
            if (char.IsLetterOrDigit(ch))
                continue;

            if (ch is '-' or '_' or '.' or ':' or '/')
                continue;

            return false;
        }

        return true;
    }
}
