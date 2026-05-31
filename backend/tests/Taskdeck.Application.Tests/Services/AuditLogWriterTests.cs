using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Tests for the shared safe audit-log writer (#1134): audit failures must surface at Warning
/// (a thrown exception OR a returned failed Result) without ever crashing the mutation.
/// </summary>
public class AuditLogWriterTests
{
    [Fact]
    public async Task SafeLogAsync_WhenLogActionThrows_LogsWarningWithExceptionAndDoesNotThrow()
    {
        var logger = new RecordingLogger();
        var history = new FakeHistoryService(FakeBehavior.Throw);

        await AuditLogWriter.SafeLogAsync(history, logger, "card", Guid.NewGuid(), AuditAction.Created);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.NotNull(warnings[0].Exception);
    }

    [Fact]
    public async Task SafeLogAsync_WhenLogActionReturnsFailure_LogsWarning()
    {
        var logger = new RecordingLogger();
        var history = new FakeHistoryService(FakeBehavior.Fail);

        await AuditLogWriter.SafeLogAsync(history, logger, "card", Guid.NewGuid(), AuditAction.Updated);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SafeLogAsync_WhenLogActionSucceeds_LogsNothing()
    {
        var logger = new RecordingLogger();
        var history = new FakeHistoryService(FakeBehavior.Succeed);

        await AuditLogWriter.SafeLogAsync(history, logger, "card", Guid.NewGuid(), AuditAction.Created);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task SafeLogAsync_WhenHistoryServiceNull_IsNoOp()
    {
        var logger = new RecordingLogger();

        await AuditLogWriter.SafeLogAsync(null, logger, "card", Guid.NewGuid(), AuditAction.Created);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task SafeLogAsync_WhenLoggerNull_StillNeverThrows()
    {
        var history = new FakeHistoryService(FakeBehavior.Throw);

        // Older/optional call sites may pass a null logger; failure must still not crash the mutation.
        var ex = await Record.ExceptionAsync(() =>
            AuditLogWriter.SafeLogAsync(history, logger: null, "card", Guid.NewGuid(), AuditAction.Created));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SafeLogAsync_WhenLogActionReturnsNullResult_LogsWarningWithoutException()
    {
        var logger = new RecordingLogger();
        var history = new FakeHistoryService(FakeBehavior.ReturnNull);

        await AuditLogWriter.SafeLogAsync(history, logger, "card", Guid.NewGuid(), AuditAction.Created);

        // A null result is classified as a failed write (Warning, no exception), not a throw/NRE.
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Null(warnings[0].Exception);
    }

    private enum FakeBehavior { Succeed, Fail, Throw, ReturnNull }

    private sealed class FakeHistoryService : IHistoryService
    {
        private readonly FakeBehavior _behavior;
        public FakeHistoryService(FakeBehavior behavior) => _behavior = behavior;

        public Task<Result> LogActionAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
            => _behavior switch
            {
                FakeBehavior.Throw => throw new InvalidOperationException("audit store unavailable"),
                FakeBehavior.Fail => Task.FromResult(Result.Failure("AuditError", "could not write audit log")),
                FakeBehavior.ReturnNull => Task.FromResult<Result>(null!),
                _ => Task.FromResult(Result.Success()),
            };

        public Task<Result<IEnumerable<AuditLogDto>>> GetBoardHistoryAsync(Guid boardId, int limit = 100) => throw new NotSupportedException();
        public Task<Result<IEnumerable<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, int limit = 100) => throw new NotSupportedException();
        public Task<Result<IEnumerable<AuditLogDto>>> GetUserHistoryAsync(Guid userId, int limit = 100) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger
    {
        public readonly List<(LogLevel Level, Exception? Exception)> Entries = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
