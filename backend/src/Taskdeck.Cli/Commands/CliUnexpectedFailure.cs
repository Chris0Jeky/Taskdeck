using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// The standalone CLI's unknown-exception boundary (#2351).
///
/// Deliberate failures -- <see cref="DomainException"/> with an error code, validation,
/// parse/format errors, and the first-run bootstrapper's operator guidance about the
/// operator's own local paths -- keep their own messages. Everything else is an unknown
/// exception: its raw message, stack trace, file paths, SQL/constraint text, provider URLs
/// and tokens must never reach stdout or stderr. This boundary prints one stable generic
/// line instead, plus the bounded startup-trace correlation reference when the run has one,
/// and keeps the full exception exactly once in the protected trace sink.
/// </summary>
internal static class CliUnexpectedFailure
{
    /// <summary>Stable error code, shared with the HTTP surface's unexpected-error code.</summary>
    internal const string ErrorCode = ErrorCodes.UnexpectedError;

    /// <summary>
    /// Stable generic failure text. Deliberately the canonical redactor constant so the CLI
    /// and the API cannot drift apart.
    /// </summary>
    internal const string Message = SensitiveDataRedactor.GenericUnexpectedFailureMessage;

    /// <summary>
    /// Printed when no protected diagnostic sink was available for this run, so the operator
    /// knows diagnostics were absent rather than suppressed. Contains no exception detail.
    /// </summary>
    internal const string DiagnosticsUnavailableNotice =
        "Full failure diagnostics were not captured: the CLI startup trace is not enabled for this run.";

    /// <summary>
    /// Prints the safe failure line and returns the standard failure exit code.
    /// </summary>
    internal static int Handle(Exception exception, CliStartupTrace? trace, TextWriter errorWriter)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(errorWriter);

        var captured = trace?.TryRecordUnexpectedFailure(exception) ?? false;
        var correlationId = trace?.CorrelationId;

        errorWriter.WriteLine(correlationId is null
            ? $"Error [{ErrorCode}]: {Message}"
            : $"Error [{ErrorCode}]: {Message} (trace correlation: {correlationId})");

        if (!captured)
        {
            errorWriter.WriteLine(DiagnosticsUnavailableNotice);
        }

        return ExitCodes.Failure;
    }
}
