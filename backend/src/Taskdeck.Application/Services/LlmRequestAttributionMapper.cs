using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Application.Services;

public static class LlmRequestAttributionMapper
{
    public const string CorrelationHeader = "x-taskdeck-correlation-id";
    public const string SourceSurfaceHeader = "x-taskdeck-source-surface";
    public const string UserTokenHeader = "x-taskdeck-user-token";
    public const string BoardTokenHeader = "x-taskdeck-board-token";
    public const string SessionTokenHeader = "x-taskdeck-session-token";
    private const int MaxCorrelationLength = 100;
    private const string CorrelationIdTagName = "taskdeck.correlation_id";

    public static string ResolveCorrelationId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Guid.NewGuid().ToString("N");
        }

        var trimmed = correlationId.Trim();
        var limited = trimmed.Length <= MaxCorrelationLength
            ? trimmed
            : trimmed[..MaxCorrelationLength];

        return IsValidCorrelationId(limited)
            ? limited
            : Guid.NewGuid().ToString("N");
    }

    public static string ResolveCorrelationIdFromActivity()
    {
        var correlationFromActivity = Activity.Current?.GetTagItem(CorrelationIdTagName)?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationFromActivity))
        {
            return ResolveCorrelationId(correlationFromActivity);
        }

        var traceId = Activity.Current?.TraceId.ToString();
        return ResolveCorrelationId(traceId);
    }

    public static string ResolveSourceSurface(LlmRequestSourceSurface sourceSurface)
    {
        return sourceSurface.ToString().ToLowerInvariant();
    }

    public static string BuildUserToken(Guid userId)
    {
        return BuildGuidToken("usr", userId);
    }

    public static string? BuildBoardToken(Guid? boardId)
    {
        return boardId.HasValue ? BuildGuidToken("brd", boardId.Value) : null;
    }

    public static string? BuildSessionToken(Guid? sessionId)
    {
        return sessionId.HasValue ? BuildGuidToken("ses", sessionId.Value) : null;
    }

    public static void AddAttributionHeaders(HttpRequestMessage message, LlmRequestAttribution? attribution)
    {
        if (attribution is null)
        {
            return;
        }

        message.Headers.TryAddWithoutValidation(CorrelationHeader, ResolveCorrelationId(attribution.CorrelationId));
        message.Headers.TryAddWithoutValidation(SourceSurfaceHeader, ResolveSourceSurface(attribution.SourceSurface));
        message.Headers.TryAddWithoutValidation(UserTokenHeader, BuildUserToken(attribution.UserId));

        var boardToken = BuildBoardToken(attribution.BoardId);
        if (!string.IsNullOrWhiteSpace(boardToken))
        {
            message.Headers.TryAddWithoutValidation(BoardTokenHeader, boardToken);
        }

        var sessionToken = BuildSessionToken(attribution.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            message.Headers.TryAddWithoutValidation(SessionTokenHeader, sessionToken);
        }
    }

    private static string BuildGuidToken(string prefix, Guid value)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString("N")));
        var tokenBody = Convert.ToHexString(hashBytes[..12]).ToLowerInvariant();
        return $"{prefix}_{tokenBody}";
    }

    private static bool IsValidCorrelationId(string correlationId)
    {
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
