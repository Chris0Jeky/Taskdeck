using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// The standalone CLI's unknown-exception boundary (#2351).
///
/// Deliberate failures -- <see cref="DomainException"/> with an error code, validation,
/// parse/format errors, and the first-run bootstrapper's operator guidance about the
/// operator's own local paths -- keep their own messages. Everything else is an unknown
/// exception: its raw message, stack trace, file paths, SQL/constraint text, provider URLs
/// and tokens must never reach stdout or stderr. This boundary prints one stable generic
/// line instead, plus a bounded correlation reference: the startup-trace correlation when the
/// harness trace is enabled (that sink keeps the full exception exactly once), otherwise a
/// generated reference that names the redacted record the always-on <see cref="CliFailureSink"/>
/// writes under the data directory.
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
        "Full failure diagnostics were not captured: the local diagnostic sink could not write its " +
        "record under the data directory, and the startup trace is not enabled for this run.";

    /// <summary>
    /// Stable code for the fail-closed pre-migration snapshot failure (#1803), whose message is
    /// deliberately written to be actionable for a local-first operator.
    /// </summary>
    internal const string PreMigrationBackupErrorCode = "PRE_MIGRATION_BACKUP_FAILED";

    /// <summary>
    /// Prints the safe failure line and returns the standard failure exit code.
    /// </summary>
    /// <param name="failureSink">
    /// The always-on local diagnostic sink (#2468). When the harness trace is absent -- the
    /// ordinary operator run -- this is the only thing that retains the failure, and the reference
    /// it is filed under is what the generic stderr line shows.
    /// </param>
    /// <param name="arguments">
    /// The process arguments, recorded into the sink after redaction. Never printed.
    /// </param>
    internal static int Handle(
        Exception exception,
        CliStartupTrace? trace,
        TextWriter errorWriter,
        CliFailureSink? failureSink = null,
        IReadOnlyList<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(errorWriter);

        // Class (a): the pre-migration snapshot failure is a deliberate operator message. It keeps
        // its own text (redacted, never a stack trace) so a local-first user can act on it.
        if (exception is PreMigrationBackupException)
        {
            errorWriter.WriteLine(
                $"Error [{PreMigrationBackupErrorCode}]: {SensitiveDataRedactor.Redact(exception.Message)}");
            return ExitCodes.Failure;
        }

        // The harness trace stays the primary sink when it is enabled, so its correlation remains
        // the reference a harness run reports. Otherwise the always-on sink files the record under
        // a freshly generated short reference.
        var traceCaptured = trace?.TryRecordUnexpectedFailure(exception) ?? false;
        var reference = trace?.CorrelationId ?? CliFailureSink.CreateReference();
        var sinkCaptured = failureSink?.TryRecord(exception, reference, arguments) ?? false;
        var captured = traceCaptured || sinkCaptured;

        // Show the reference only when something actually kept the record: a reference to a record
        // that was never written would send an operator looking for a file that does not exist.
        errorWriter.WriteLine(captured
            ? $"Error [{ErrorCode}]: {Message} (trace correlation: {reference})"
            : $"Error [{ErrorCode}]: {Message}");

        if (!captured)
        {
            errorWriter.WriteLine(DiagnosticsUnavailableNotice);
        }

        return ExitCodes.Failure;
    }
}
