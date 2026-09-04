using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// End-to-end proof for issue #2351: an unknown failure whose text carries a secret-like token,
/// a Windows path, a SQLite constraint string, and a provider URL must not reach the HTTP
/// response (unhandled path or <see cref="ErrorCodes.UnexpectedError"/> Result-mapper path), must
/// stay correlated with the existing X-Request-Id, and must be logged exactly once rather than
/// once per layer. Deliberate domain and validation failures keep their stable messages and are
/// not logged as errors.
///
/// Note on scope: the shipped policy (docs/security/SECURITY_LOGGING_REDACTION.md) is that
/// <see cref="UnhandledExceptionMiddleware"/> logs a bounded, redacted classification instead of
/// the raw exception object. These tests therefore pin the correlated single log entry and the
/// absence of raw exception text everywhere; they do not assert raw exception text in the log.
/// </summary>
public class UnknownExceptionBoundaryProofTests
{
    private const string SecretMarker = "sk-live-ABC123XYZ";
    private const string WindowsPathMarker = @"C:\Users\alice\secret.db";
    private const string SqliteMarker = "SQLite Error 19: 'UNIQUE constraint failed: Cards.Id'";
    private const string ProviderUrlMarker = "https://api.openai.com/v1/chat/completions?key=abc";

    private static readonly string[] Markers =
    [
        SecretMarker,
        WindowsPathMarker,
        SqliteMarker,
        ProviderUrlMarker
    ];

    private static string UnsafeText =>
        $"token={SecretMarker} db={WindowsPathMarker} sqlite={SqliteMarker} provider={ProviderUrlMarker}";

    [Fact]
    public async Task UnhandledException_ShouldNotLeakMarkers_AndShouldLogOnceUnderTheRequestCorrelationId()
    {
        var correlationId = $"proof-unhandled-{Guid.NewGuid():N}";
        await using var factory = new ProofWebApplicationFactory(
            new ProofLogQueryService(() => throw new InvalidOperationException(UnsafeText)));

        var probe = await SendAsync(factory, correlationId);

        probe.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        AssertGenericUnexpectedBody(probe);
        AssertResponseCarriesNoMarkers(probe, correlationId);
        AssertNoCapturedLogContainsMarkers(factory);

        // Exactly one correlated error entry: the boundary logs once, no layer double-logs it.
        var errorEntries = factory.CapturedLogs.Snapshot()
            .Where(entry => entry.Level >= LogLevel.Error)
            .ToList();
        errorEntries.Should().ContainSingle(
            "the unknown exception must be logged exactly once, not once per layer");

        var boundaryEntry = errorEntries.Single();
        boundaryEntry.Category.Should().Be(typeof(UnhandledExceptionMiddleware).FullName);
        boundaryEntry.Properties.Should().ContainKey("CorrelationId");
        boundaryEntry.Properties["CorrelationId"].Should().Be(
            correlationId,
            "the protected log entry must carry the same reference the client received");
        boundaryEntry.Properties.Should().ContainKey("ExceptionType");
        boundaryEntry.Properties["ExceptionType"].Should().Be(nameof(InvalidOperationException));

        // The middleware's structured diagnostic sink saw the same single correlated failure.
        factory.UnhandledExceptionDiagnostics.Snapshot()
            .Should().ContainSingle()
            .Which.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task ThrownDomainExceptionCarryingMarkers_ShouldStillBeGeneralized()
    {
        var correlationId = $"proof-thrown-domain-{Guid.NewGuid():N}";
        await using var factory = new ProofWebApplicationFactory(
            new ProofLogQueryService(() => throw new DomainException(ErrorCodes.Conflict, UnsafeText)));

        var probe = await SendAsync(factory, correlationId);

        // There is no global DomainException-to-status filter: a thrown DomainException reaches the
        // unhandled boundary and is generalized. That is the safe direction; deliberate domain
        // failures are surfaced as Result failures (see the test below), not by throwing.
        probe.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        AssertGenericUnexpectedBody(probe);
        AssertResponseCarriesNoMarkers(probe, correlationId);
        AssertNoCapturedLogContainsMarkers(factory);
    }

    [Fact]
    public async Task UnexpectedErrorResult_ShouldNotLeakMarkersThroughTheResultMapper()
    {
        var correlationId = $"proof-result-{Guid.NewGuid():N}";
        await using var factory = new ProofWebApplicationFactory(
            new ProofLogQueryService(() =>
                Result.Failure<IEnumerable<LogEntryDto>>(ErrorCodes.UnexpectedError, UnsafeText)));

        var probe = await SendAsync(factory, correlationId);

        probe.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        AssertGenericUnexpectedBody(probe);
        AssertResponseCarriesNoMarkers(probe, correlationId);
        AssertNoCapturedLogContainsMarkers(factory);

        // ResultExtensions.ToErrorActionResult replaces the message but does not log. No second
        // correlation scheme is introduced here; correlation stays the X-Request-Id echoed above.
        factory.CapturedLogs.Snapshot().Should().NotContain(
            entry => entry.Level >= LogLevel.Error,
            "the Result mapper sanitizes without emitting its own error log");
        factory.UnhandledExceptionDiagnostics.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [InlineData(ErrorCodes.ValidationError, "Limit must be between 1 and 500.", HttpStatusCode.BadRequest)]
    [InlineData(ErrorCodes.NotFound, "No log entries exist for that board.", HttpStatusCode.NotFound)]
    [InlineData(ErrorCodes.Conflict, "Log export already in progress.", HttpStatusCode.Conflict)]
    public async Task DeliberateDomainAndValidationResults_ShouldKeepStableMessagesAndNotLogAsErrors(
        string errorCode,
        string message,
        HttpStatusCode expectedStatus)
    {
        var correlationId = $"proof-domain-{Guid.NewGuid():N}";
        await using var factory = new ProofWebApplicationFactory(
            new ProofLogQueryService(() =>
                Result.Failure<IEnumerable<LogEntryDto>>(errorCode, message)));

        var probe = await SendAsync(factory, correlationId);

        probe.StatusCode.Should().Be(expectedStatus);
        var payload = ParseBody(probe);
        payload.GetProperty("errorCode").GetString().Should().Be(errorCode);
        payload.GetProperty("message").GetString().Should().Be(
            message,
            "deliberate domain and validation messages must reach the caller unchanged");

        probe.CorrelationHeader.Should().Be(correlationId);
        factory.CapturedLogs.Snapshot().Should().NotContain(
            entry => entry.Level >= LogLevel.Error,
            "expected failures are not unknown-exception errors");
        factory.UnhandledExceptionDiagnostics.Snapshot().Should().BeEmpty();
    }

    private static async Task<ResponseProbe> SendAsync(ProofWebApplicationFactory factory, string correlationId)
    {
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "unknown-boundary");

        // Authentication traffic is not part of the proof; only the probe request is measured.
        factory.CapturedLogs.Clear();
        factory.UnhandledExceptionDiagnostics.Clear();
        factory.ServiceIsArmed = true;

        client.DefaultRequestHeaders.Remove(CorrelationIdMiddleware.HeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            correlationId);

        using var response = await client.GetAsync("/api/logs?limit=10");
        var body = await response.Content.ReadAsStringAsync();
        var headerText = string.Join(
            "\n",
            response.Headers
                .Concat(response.Content.Headers)
                .Select(header => $"{header.Key}: {string.Join(",", header.Value)}"));
        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var requestIds);

        return new ResponseProbe(
            response.StatusCode,
            body,
            headerText,
            requestIds?.SingleOrDefault());
    }

    private static JsonElement ParseBody(ResponseProbe probe)
    {
        using var document = JsonDocument.Parse(probe.Body);
        return document.RootElement.Clone();
    }

    private static void AssertGenericUnexpectedBody(ResponseProbe probe)
    {
        var payload = ParseBody(probe);
        payload.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.UnexpectedError);
        payload.GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
    }

    private static void AssertResponseCarriesNoMarkers(ResponseProbe probe, string correlationId)
    {
        // The raw scan alone is not sufficient: JSON serialization escapes the markers (a Windows
        // path is written with doubled backslashes, and JavaScriptEncoder.Default renders the
        // SQLite marker's apostrophe as a \u0027 escape), so a regression that added a
        // detail/extensions/exceptionMessage field carrying the raw text would slip past it.
        // Decode the body and scan every string value and property name as well.
        var decodedStrings = CollectJsonStrings(probe.Body);
        decodedStrings.Should().NotBeEmpty("the error contract body must be readable JSON");

        foreach (var marker in Markers)
        {
            probe.Body.Should().NotContain(marker);
            probe.HeaderText.Should().NotContain(marker);

            decodedStrings.Should().NotContain(
                value => value.Contains(marker, StringComparison.Ordinal),
                $"no decoded JSON string or property name in the response body may carry '{marker}'");
        }

        probe.CorrelationHeader.Should().Be(
            correlationId,
            "the caller must get back the correlation reference to quote to an operator");
    }

    /// <summary>
    /// Every string value and property name in the response body, recursively, after JSON decoding.
    /// </summary>
    private static IReadOnlyList<string> CollectJsonStrings(string body)
    {
        var collected = new List<string>();
        using var document = JsonDocument.Parse(body);
        Walk(document.RootElement, collected);
        return collected;

        static void Walk(JsonElement element, List<string> collected)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        collected.Add(property.Name);
                        Walk(property.Value, collected);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, collected);
                    }

                    break;
                case JsonValueKind.String:
                    collected.Add(element.GetString() ?? string.Empty);
                    break;
            }
        }
    }

    private static void AssertNoCapturedLogContainsMarkers(ProofWebApplicationFactory factory)
    {
        var entries = factory.CapturedLogs.Snapshot();
        foreach (var marker in Markers)
        {
            // Search the rendered message, every structured property value, and every enclosing
            // log scope: exception text smuggled into a scope or a property is still a leak.
            var occurrences = entries.Count(entry =>
                entry.Text.Contains(marker, StringComparison.Ordinal) ||
                entry.Properties.Values.Any(value => value.Contains(marker, StringComparison.Ordinal)) ||
                entry.Scopes.Any(scope => scope.Contains(marker, StringComparison.Ordinal)));
            occurrences.Should().Be(
                0,
                $"no captured log entry, property, or scope may render the raw marker '{marker}'");
        }
    }

    private sealed record ResponseProbe(
        HttpStatusCode StatusCode,
        string Body,
        string HeaderText,
        string? CorrelationHeader);

    private sealed class ProofWebApplicationFactory : TestWebApplicationFactory
    {
        private readonly ProofLogQueryService _logQueryService;

        public ProofWebApplicationFactory(ProofLogQueryService logQueryService)
        {
            _logQueryService = logQueryService;
        }

        internal CapturedLogSink CapturedLogs { get; } = new();

        internal bool ServiceIsArmed
        {
            get => _logQueryService.IsArmed;
            set => _logQueryService.IsArmed = value;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturedLogProvider(CapturedLogs)));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILogQueryService>();
                services.AddScoped<ILogQueryService>(_ => _logQueryService);
            });
        }
    }

    private sealed class ProofLogQueryService : ILogQueryService
    {
        private readonly Func<Result<IEnumerable<LogEntryDto>>> _behavior;

        public ProofLogQueryService(Func<Result<IEnumerable<LogEntryDto>>> behavior)
        {
            _behavior = behavior;
        }

        public bool IsArmed { get; set; }

        public Task<Result<IEnumerable<LogEntryDto>>> QueryLogsAsync(
            LogQueryDto query,
            CancellationToken ct = default)
        {
            return Task.FromResult(IsArmed
                ? _behavior()
                : Result.Success<IEnumerable<LogEntryDto>>([]));
        }

        public Task<Result<IEnumerable<LogEntryDto>>> GetByCorrelationIdAsync(
            string correlationId,
            CancellationToken ct = default)
            => QueryLogsAsync(new LogQueryDto(null, null, null, null, correlationId, null, null, 100), ct);

        public IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(
            LogQueryDto? filter = null,
            CancellationToken ct = default)
            => throw new NotSupportedException("Streaming is not part of this proof.");
    }

    internal sealed record CapturedLogEntry(
        string Category,
        LogLevel Level,
        string Text,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyList<string> Scopes);

    internal sealed class CapturedLogSink
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public void Record(CapturedLogEntry entry) => _entries.Enqueue(entry);

        public IReadOnlyList<CapturedLogEntry> Snapshot() => _entries.ToArray();

        public void Clear()
        {
            while (_entries.TryDequeue(out _))
            {
            }
        }
    }


    /// <summary>
    /// Ambient stack of rendered logging scopes, so scope-carried text is searchable too.
    /// </summary>
    private static class CapturedScopeStack
    {
        private static readonly AsyncLocal<ImmutableStack<string>?> Scopes = new();

        public static IReadOnlyList<string> Current =>
            Scopes.Value is null ? Array.Empty<string>() : Scopes.Value.ToArray();

        public static IDisposable Push(string rendered)
        {
            var previous = Scopes.Value ?? ImmutableStack<string>.Empty;
            Scopes.Value = previous.Push(rendered);
            return new PopOnDispose(previous);
        }

        private sealed class PopOnDispose : IDisposable
        {
            private readonly ImmutableStack<string> _previous;

            public PopOnDispose(ImmutableStack<string> previous)
            {
                _previous = previous;
            }

            public void Dispose() => Scopes.Value = _previous;
        }
    }

    private sealed class CapturedLogProvider : ILoggerProvider
    {
        private readonly CapturedLogSink _sink;

        public CapturedLogProvider(CapturedLogSink sink)
        {
            _sink = sink;
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly CapturedLogSink _sink;

            public CapturingLogger(string categoryName, CapturedLogSink sink)
            {
                _categoryName = categoryName;
                _sink = sink;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                => CapturedScopeStack.Push(RenderScope(state));

            private static string RenderScope<TState>(TState state)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> structured)
                {
                    return string.Join("; ", structured.Select(pair => $"{pair.Key}={pair.Value}"));
                }

                return state.ToString() ?? string.Empty;
            }

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> structured)
                {
                    foreach (var property in structured)
                    {
                        properties[property.Key] = property.Value?.ToString() ?? string.Empty;
                    }
                }

                var text = formatter(state, exception);
                if (exception is not null)
                {
                    text = $"{text}\n{exception}";
                }

                _sink.Record(new CapturedLogEntry(
                    _categoryName,
                    logLevel,
                    text,
                    properties,
                    CapturedScopeStack.Current));
            }
        }
    }
}
