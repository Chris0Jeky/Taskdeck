using System.Text.RegularExpressions;

namespace Taskdeck.Api.Telemetry;

/// <summary>
/// Sanitizes values before they are included in log entries or trace attributes.
/// Prevents log injection (CWE-117) by stripping newlines and control characters
/// that could forge log entries in plain-text sinks.
/// </summary>
public static partial class LogSanitizer
{
    private const int MaxSanitizedLength = 200;

    /// <summary>
    /// Strips newlines, carriage returns, and non-printable control characters
    /// from user-controlled input before logging. Truncates to a safe length.
    /// </summary>
    public static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sanitized = ControlCharPattern().Replace(value, string.Empty);
        if (sanitized.Length > MaxSanitizedLength)
            sanitized = string.Concat(sanitized.AsSpan(0, MaxSanitizedLength), "...");

        return sanitized;
    }

    /// <summary>
    /// Returns a safe status description for OpenTelemetry Activity spans.
    /// Uses only the exception type name to avoid leaking user content
    /// from exception messages into tracing backends.
    /// </summary>
    public static string SafeExceptionDescription(Exception exception)
    {
        return exception.GetType().Name;
    }

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharPattern();
}
