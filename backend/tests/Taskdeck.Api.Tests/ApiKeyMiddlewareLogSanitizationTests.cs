using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Middleware;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression for #2519: the key-not-found branch of <see cref="ApiKeyMiddleware"/> logs a prefix of
/// the presented bearer token. That token comes straight from an unauthenticated caller's
/// Authorization header, so before the fix it could carry CR/LF, U+2028 and C1 controls into a
/// plain-text log sink and forge an extra log line (CWE-117). The prefix is now sliced to eight
/// characters FIRST and sanitized afterwards, so stripping can only shorten what is logged.
/// </summary>
public sealed class ApiKeyMiddlewareLogSanitizationTests : IDisposable
{
    private const string ForgedLine = "MCP API key authentication succeeded";
    private const string FailureMessage = "MCP API key authentication failed: key not found";

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskdeck-logsan-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task KeyNotFound_WithControlCharactersInToken_LogsOneSanitizedSingleLineEntry()
    {
        // The eight-character prefix window is 't','d','s','k','_' plus the three injected line
        // breakers, so every one of them is inside the slice the middleware logs.
        const string token = "tdsk_\r\u2028\n" + ForgedLine + " for key tdsk_admin";
        await using var db = await CreateMigratedContextAsync();
        var sink = new RecordingLoggerSink();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("an unauthenticated request must not reach the endpoint"),
            new RecordingLogger<ApiKeyMiddleware>(sink));

        var context = CreateMcpContext(token);
        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var failures = sink.Messages.Where(message => message.Contains(FailureMessage, StringComparison.Ordinal)).ToList();
        failures.Should().ContainSingle("the not-found branch logs exactly one warning per attempt");

        var entry = failures[0];
        entry.Should().NotContain("\r", "a carriage return would start a forged line in a plain-text sink");
        entry.Should().NotContain("\n", "a line feed would start a forged line in a plain-text sink");
        entry.Should().NotContain("\u2028", "U+2028 is a line break for many log viewers");
        entry.Should().Contain("tdsk_", "the printable part of the prefix is still useful for triage");

        sink.Messages.Should().NotContain(
            message => message.Contains(ForgedLine, StringComparison.Ordinal),
            "no caller-supplied text may reach the log as a forged authentication line");
    }

    [Fact]
    public async Task KeyNotFound_WithBidiAndZeroWidthCharactersInToken_LogsNoFormatCharacters()
    {
        // U+200B (zero width space), U+200F (right-to-left mark) and U+202E (right-to-left override)
        // are Unicode format characters: invisible in a log viewer and able to reverse the rendered
        // order of everything after them.
        const string token = "tdsk_\u200B\u200F\u202E" + ForgedLine;
        await using var db = await CreateMigratedContextAsync();
        var sink = new RecordingLoggerSink();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("an unauthenticated request must not reach the endpoint"),
            new RecordingLogger<ApiKeyMiddleware>(sink));

        var context = CreateMcpContext(token);
        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var entry = sink.Messages.Should()
            .ContainSingle(message => message.Contains(FailureMessage, StringComparison.Ordinal))
            .Subject;
        entry.Should().NotContain("\u200B").And.NotContain("\u200F").And.NotContain("\u202E");
        entry.Should().Contain("tdsk_");
    }

    [Fact]
    public async Task KeyNotFound_WithLongToken_LogsAtMostEightCharactersOfIt()
    {
        // The prefix is sliced before it is sanitized, so no amount of stripping can widen it.
        const string token = "tdsk_secret_material_that_must_never_be_logged";
        await using var db = await CreateMigratedContextAsync();
        var sink = new RecordingLoggerSink();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("an unauthenticated request must not reach the endpoint"),
            new RecordingLogger<ApiKeyMiddleware>(sink));

        var context = CreateMcpContext(token);
        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var entry = sink.Messages.Should()
            .ContainSingle(message => message.Contains(FailureMessage, StringComparison.Ordinal))
            .Subject;
        entry.Should().Contain(token[..8]);
        entry.Should().NotContain(token[..9], "at most eight characters of the presented token are logged");
    }

    [Fact]
    public async Task KeyNotFound_WithControlCharactersInsideTheWindow_DoesNotPullLaterTokenMaterialIntoTheLog()
    {
        // Discriminates the ordering: the first eight characters are "tdsk_" plus three carriage
        // returns, and the secret material starts at index 8. Slicing first and sanitizing afterwards
        // logs "tdsk_". Sanitizing first and slicing afterwards would log "tdsk_SEC", pulling three
        // characters of the secret past the window into the log.
        const string token = "tdsk_\r\r\rSECRETKEYMATERIAL";
        await using var db = await CreateMigratedContextAsync();
        var sink = new RecordingLoggerSink();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("an unauthenticated request must not reach the endpoint"),
            new RecordingLogger<ApiKeyMiddleware>(sink));

        var context = CreateMcpContext(token);
        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var entry = sink.Messages.Should()
            .ContainSingle(message => message.Contains(FailureMessage, StringComparison.Ordinal))
            .Subject;
        entry.Should().Contain("(prefix: tdsk_)", "the three stripped characters must not be replaced by later token material");
        entry.Should().NotContain("SEC", "nothing past the eight-character window may reach the log");
    }

    // ── Helpers ──

    private async Task<TaskdeckDbContext> CreateMigratedContextAsync()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_dbPath))
            .Options;
        var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static DefaultHttpContext CreateMcpContext(string bearerKey)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = $"Bearer {bearerKey}";
        context.Response.Body = new MemoryStream();
        return context;
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = _dbPath + suffix;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a leaked handle is not a test failure.
            }
        }
    }

    private sealed class RecordingLoggerSink
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public void Add(string message) => _messages.Enqueue(message);
    }

    private sealed class RecordingLogger<T>(RecordingLoggerSink sink) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Add(formatter(state, exception));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
