using System.Net;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UnhandledExceptionDiagnosticSinkTests
{
    [Fact]
    public void Record_ShouldBoundEntriesAndRejectUnsafeDiagnosticTokens()
    {
        var sink = new UnhandledExceptionDiagnosticSink(capacity: 2);

        sink.Record("first", "InvalidOperationException", "InvalidOperationException", null, null);
        sink.Record(new string('a', 150), "unsafe\nsecret", "SqliteException", 5, 517);
        sink.Record("third", "DbUpdateException", "SqliteException", 6, 262);

        sink.Snapshot().Should().Equal(
            new UnhandledExceptionDiagnostic(
                new string('a', UnhandledExceptionDiagnosticSink.MaxCorrelationIdLength),
                "UnknownException",
                "SqliteException",
                5,
                517),
            new UnhandledExceptionDiagnostic(
                "third",
                "DbUpdateException",
                "SqliteException",
                6,
                262));
    }

    [Fact]
    public void FormatFailures_ShouldFilterByCorrelationAndBoundSafeFieldsWithoutContent()
    {
        const string sensitiveContent = "board title and bearer secret";
        var sink = new UnhandledExceptionDiagnosticSink();
        sink.Record("unrelated", "ArgumentException", "ArgumentException", null, null);
        sink.Record("matched", "DbUpdateException", "SqliteException", 5, 517);

        var failures = Enumerable.Range(0, 30)
            .Select(index => new ConcurrentHttpResponseDiagnostic(
                index,
                HttpStatusCode.InternalServerError,
                index == 0 ? new string('a', 150) : $"unsafe {sensitiveContent}",
                "matched",
                index == 0 ? $"unexpected_error\n{sensitiveContent}" : "unexpected_error"))
            .ToList();

        var formatted = UnhandledExceptionDiagnosticFormatter.FormatFailures(
            failures,
            sink.Snapshot());

        formatted.Should().Contain(
            $"requestId={new string('a', UnhandledExceptionDiagnosticSink.MaxCorrelationIdLength)}");
        formatted.Should().Contain(
            "responseId=matched,errorCode=unreadable," +
            "middleware=DbUpdateException->SqliteException/sqlite=5/extended=517");
        formatted.Should().NotContain("ArgumentException");
        formatted.Should().NotContain(sensitiveContent);
        formatted.Should().HaveLength(UnhandledExceptionDiagnosticFormatter.MaxFormattedLength);
        formatted.Should().EndWith("...[truncated]");
    }
}
