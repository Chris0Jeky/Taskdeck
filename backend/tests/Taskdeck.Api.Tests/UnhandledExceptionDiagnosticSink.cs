using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Middleware;

namespace Taskdeck.Api.Tests;

internal sealed record UnhandledExceptionDiagnostic(
    string CorrelationId,
    string ExceptionType,
    string LastInspectedExceptionType,
    bool ClassificationTruncated,
    int? SqliteErrorCode,
    int? SqliteExtendedErrorCode);

internal sealed record ConcurrentHttpResponseDiagnostic(
    int RequestIndex,
    HttpStatusCode StatusCode,
    string RequestCorrelationId,
    string ResponseCorrelationId,
    string ErrorCode);

internal sealed class UnhandledExceptionDiagnosticSink
{
    internal const int DefaultCapacity = 64;
    internal const int MaxCorrelationIdLength = 100;
    internal const int MaxExceptionTypeLength = 128;

    private readonly ConcurrentQueue<UnhandledExceptionDiagnostic> _entries = new();
    private readonly int _capacity;

    public UnhandledExceptionDiagnosticSink(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Record(
        string? correlationId,
        string? exceptionType,
        string? lastInspectedExceptionType,
        bool classificationTruncated,
        int? sqliteErrorCode,
        int? sqliteExtendedErrorCode)
    {
        _entries.Enqueue(new UnhandledExceptionDiagnostic(
            DiagnosticToken.Normalize(correlationId, MaxCorrelationIdLength, "unknown-correlation"),
            DiagnosticToken.Normalize(exceptionType, MaxExceptionTypeLength, "UnknownException"),
            DiagnosticToken.Normalize(lastInspectedExceptionType, MaxExceptionTypeLength, "UnknownException"),
            classificationTruncated,
            sqliteErrorCode,
            sqliteExtendedErrorCode));

        while (_entries.Count > _capacity)
        {
            _entries.TryDequeue(out _);
        }
    }

    public IReadOnlyList<UnhandledExceptionDiagnostic> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

}

internal static class UnhandledExceptionDiagnosticFormatter
{
    internal const int MaxFormattedLength = 2_048;
    private const int MaxErrorCodeLength = 64;
    private const string TruncationSuffix = "...[truncated]";

    public static string FormatFailures(
        IReadOnlyCollection<ConcurrentHttpResponseDiagnostic> failures,
        IReadOnlyCollection<UnhandledExceptionDiagnostic> middlewareDiagnostics)
    {
        if (failures.Count == 0)
        {
            return "none";
        }

        var formatted = string.Join(" | ", failures.Select(failure =>
        {
            var requestCorrelationId = DiagnosticToken.Normalize(
                failure.RequestCorrelationId,
                UnhandledExceptionDiagnosticSink.MaxCorrelationIdLength,
                "missing");
            var responseCorrelationId = DiagnosticToken.Normalize(
                failure.ResponseCorrelationId,
                UnhandledExceptionDiagnosticSink.MaxCorrelationIdLength,
                "missing");
            var errorCode = DiagnosticToken.Normalize(
                failure.ErrorCode,
                MaxErrorCodeLength,
                "unreadable");
            var middleware = middlewareDiagnostics.FirstOrDefault(diagnostic =>
                string.Equals(
                    diagnostic.CorrelationId,
                    responseCorrelationId,
                    StringComparison.Ordinal));
            var classification = middleware is null
                ? "none"
                : $"{DiagnosticToken.Normalize(middleware.ExceptionType, UnhandledExceptionDiagnosticSink.MaxExceptionTypeLength, "UnknownException")}" +
                  $"->{DiagnosticToken.Normalize(middleware.LastInspectedExceptionType, UnhandledExceptionDiagnosticSink.MaxExceptionTypeLength, "UnknownException")}" +
                  $"/truncated={middleware.ClassificationTruncated.ToString().ToLowerInvariant()}" +
                  $"/sqlite={middleware.SqliteErrorCode?.ToString() ?? "none"}" +
                  $"/extended={middleware.SqliteExtendedErrorCode?.ToString() ?? "none"}";

            return $"index={failure.RequestIndex},status={(int)failure.StatusCode}," +
                   $"requestId={requestCorrelationId},responseId={responseCorrelationId}," +
                   $"errorCode={errorCode},middleware={classification}";
        }));

        return formatted.Length <= MaxFormattedLength
            ? formatted
            : $"{formatted[..(MaxFormattedLength - TruncationSuffix.Length)]}{TruncationSuffix}";
    }
}

internal static class DiagnosticToken
{
    public static string Normalize(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var bounded = value.Length <= maxLength ? value : value[..maxLength];
        foreach (var character in bounded)
        {
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or
                '-' or '_' or '.' or ':' or '/' or '+' or '`')
            {
                continue;
            }

            return fallback;
        }

        return bounded;
    }
}

internal sealed class UnhandledExceptionDiagnosticLoggerProvider : ILoggerProvider
{
    private static readonly string MiddlewareCategory = typeof(UnhandledExceptionMiddleware).FullName!;
    private readonly UnhandledExceptionDiagnosticSink _sink;

    public UnhandledExceptionDiagnosticLoggerProvider(UnhandledExceptionDiagnosticSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName, _sink);

    public void Dispose()
    {
    }

    private sealed class DiagnosticLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly UnhandledExceptionDiagnosticSink _sink;

        public DiagnosticLogger(string categoryName, UnhandledExceptionDiagnosticSink sink)
        {
            _categoryName = categoryName;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel == LogLevel.Error &&
            string.Equals(_categoryName, MiddlewareCategory, StringComparison.Ordinal);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) ||
                state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return;
            }

            string? correlationId = null;
            string? exceptionType = null;
            string? lastInspectedExceptionType = null;
            var classificationTruncated = false;
            int? sqliteErrorCode = null;
            int? sqliteExtendedErrorCode = null;

            foreach (var property in properties)
            {
                switch (property.Key)
                {
                    case "CorrelationId":
                        correlationId = property.Value as string;
                        break;
                    case "ExceptionType":
                        exceptionType = property.Value as string;
                        break;
                    case "LastInspectedExceptionType":
                        lastInspectedExceptionType = property.Value as string;
                        break;
                    case "ClassificationTruncated" when property.Value is bool value:
                        classificationTruncated = value;
                        break;
                    case "SqliteErrorCode":
                        sqliteErrorCode = property.Value as int?;
                        break;
                    case "SqliteExtendedErrorCode":
                        sqliteExtendedErrorCode = property.Value as int?;
                        break;
                }
            }

            _sink.Record(
                correlationId,
                exceptionType,
                lastInspectedExceptionType,
                classificationTruncated,
                sqliteErrorCode,
                sqliteExtendedErrorCode);
        }
    }
}
