using System.Text.RegularExpressions;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public static class SensitiveDataRedactor
{
    public const string RedactedValue = "[redacted]";

    /// <summary>
    /// The generic string returned to an end user over the wire: the HTTP 500 response
    /// body and the per-item message inside a batch execution receipt.
    /// <para>
    /// It differs from <see cref="GenericUnexpectedFailureMessage"/> deliberately. That
    /// one is written into persisted state, MCP output and CLI stderr, where the reader
    /// is an operator who can follow its instruction and look the correlation ID up in
    /// the server logs; this one is read by an end user for whom that instruction is
    /// noise. #2351 / R6 gives the two a single shared definition each so the surfaces
    /// that emit them cannot drift; neither text may change without also changing the
    /// contract tests and the frontend error mapper that pin it.
    /// </para>
    /// </summary>
    public const string GenericUnexpectedErrorMessage = "An unexpected error occurred.";

    /// <summary>
    /// The generic string written to persisted state, MCP output and CLI stderr. See
    /// <see cref="GenericUnexpectedErrorMessage"/> for why the two differ.
    /// </summary>
    public const string GenericUnexpectedFailureMessage =
        "Unexpected processing error. Check server logs with the correlation ID.";
    private const int MaxExceptionSummaryDepth = 5;
    private const int MaxExceptionSummaryLength = 1_024;

    private static readonly (Regex Pattern, string Replacement)[] ReplacementRules =
    {
        (
            new Regex(
                @"(?i)(authorization\s*[:=]\s*bearer\s+)([^\s,;]+)",
                RegexOptions.Compiled),
            $"$1{RedactedValue}"
        ),
        (
            new Regex(
                @"(?i)(bearer\s+)([^\s,;]+)",
                RegexOptions.Compiled),
            $"$1{RedactedValue}"
        ),
        (
            new Regex(
                @"(?i)(x-goog-api-key\s*[:=]\s*)([^\s,;]+)",
                RegexOptions.Compiled),
            $"$1{RedactedValue}"
        ),
        (
            new Regex(
                @"(?i)((""(?:authorization|apiKey|api_key|token|password|secret|x-goog-api-key)""\s*:\s*""))((?:\\.|[^""\\])*)("")",
                RegexOptions.Compiled),
            $"$1{RedactedValue}$4"
        ),
        (
            new Regex(
                @"(?i)((""(?:text|payload|rawText|content|titleHint|externalRef)""\s*:\s*""))((?:\\.|[^""\\])*)("")",
                RegexOptions.Compiled),
            $"$1{RedactedValue}$4"
        ),
        (
            new Regex(
                @"(?i)((?:api[_-]?key|token|password|secret)\s*[:=]\s*)([^\s,;]+)",
                RegexOptions.Compiled),
            $"$1{RedactedValue}"
        ),
        (
            new Regex(
                @"(?im)((?:capture\s+text|capture\s+payload|raw\s+text|payload|content)\s*[:=]\s*)(.+)$",
                RegexOptions.Compiled),
            $"$1{RedactedValue}"
        )
    };

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = value;
        foreach (var (pattern, replacement) in ReplacementRules)
        {
            redacted = pattern.Replace(redacted, replacement);
        }

        return redacted;
    }

    public static string SanitizeLlmFailureMessage(string? errorCode, string? errorMessage)
    {
        if (string.Equals(errorCode, ErrorCodes.UnexpectedError, StringComparison.Ordinal))
        {
            return GenericUnexpectedFailureMessage;
        }

        var redacted = Redact(errorMessage);
        return string.IsNullOrWhiteSpace(redacted)
            ? "Processing failed."
            : redacted;
    }

    public static string SummarizeException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var summaries = new List<string>();
        var depth = 0;
        Exception? current = exception;
        while (current is not null && depth < MaxExceptionSummaryDepth)
        {
            var message = string.IsNullOrWhiteSpace(current.Message)
                ? "(no message)"
                : Redact(current.Message);
            summaries.Add($"{current.GetType().Name}: {message}");
            current = current.InnerException;
            depth += 1;
        }

        if (current is not null)
        {
            summaries.Add($"... additional inner exceptions truncated after {MaxExceptionSummaryDepth} levels");
        }

        var summary = string.Join(" --> ", summaries);
        if (summary.Length <= MaxExceptionSummaryLength)
        {
            return summary;
        }

        return $"{summary[..MaxExceptionSummaryLength]}... [truncated]";
    }
}
