using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Create-time defensive validation for automation proposal operations (issue #1125).
///
/// Operations on a proposal are not executed at create time — they are persisted for
/// review-first approval and applied later by <c>OperationHandlerRegistry</c>. This validator
/// rejects clearly-malformed operation input at the create boundary so junk never persists
/// and never escapes as an unhandled 500:
/// <list type="bullet">
///   <item><description><c>actionType</c>/<c>targetType</c> must be short identifier-like tokens
///   (letters/digits and '. _ -' separators, starting with a letter). This rejects markup, SQL,
///   control characters, whitespace, and oversized strings while accepting every verb/target the
///   apply registry uses (create, update, move, archive, reorder, ...), namespaced forms
///   (card.create), and any future identifier-like verb — so it never rejects a legitimate
///   planner / chat / capture / MCP operation.</description></item>
///   <item><description><c>parameters</c> must be valid JSON, within a bounded size and
///   nesting depth.</description></item>
/// </list>
/// Returns <see cref="ErrorCodes.ValidationError"/> (HTTP 400) on the first violation.
/// </summary>
public static class ProposalOperationInputValidator
{
    /// <summary>Maximum length of an <c>actionType</c> or <c>targetType</c> token.</summary>
    public const int MaxTokenLength = 64;

    /// <summary>Maximum UTF-8 byte size of an operation's <c>parameters</c> JSON.</summary>
    public const int MaxParametersBytes = 64 * 1024;

    /// <summary>Maximum nesting depth of an operation's <c>parameters</c> JSON.</summary>
    public const int MaxParametersDepth = 32;

    // Identifier-like token: starts with a letter, then letters/digits and the '. _ -'
    // separators (covers plain verbs like "create" and namespaced ones like "card.create").
    // Deliberately permissive (not a fixed allowlist) so new legitimate verbs/targets are not
    // rejected, while markup, SQL, control/binary characters, and whitespace are.
    private static readonly Regex TokenPattern = new(
        "^[A-Za-z][A-Za-z0-9_.-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Result Validate(IReadOnlyList<CreateProposalOperationDto>? operations)
    {
        if (operations is null || operations.Count == 0)
            return Result.Success();

        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            var prefix = $"Operation {i} (sequence {op.Sequence})";

            if (!IsValidToken(op.ActionType))
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"{prefix}: actionType must be a short identifier-like token (letters, digits, and . _ - separators; start with a letter; max {MaxTokenLength} characters).");

            if (!IsValidToken(op.TargetType))
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"{prefix}: targetType must be a short identifier-like token (letters, digits, and . _ - separators; start with a letter; max {MaxTokenLength} characters).");

            var parametersError = ValidateParameters(op.Parameters, prefix);
            if (parametersError is not null)
                return Result.Failure(ErrorCodes.ValidationError, parametersError);
        }

        return Result.Success();
    }

    private static bool IsValidToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.Length <= MaxTokenLength && TokenPattern.IsMatch(trimmed);
    }

    private static string? ValidateParameters(string? rawParameters, string prefix)
    {
        if (string.IsNullOrWhiteSpace(rawParameters))
            return $"{prefix}: parameters cannot be empty.";

        // Bound size before parsing so an abusive payload is rejected cheaply.
        if (Encoding.UTF8.GetByteCount(rawParameters) > MaxParametersBytes)
            return $"{prefix}: parameters exceed the maximum size of {MaxParametersBytes} bytes.";

        try
        {
            // JsonDocument.Parse rejects input nested deeper than its default max depth (64),
            // which bounds the recursion in MeasureDepth below.
            using var document = JsonDocument.Parse(rawParameters);
            if (MeasureDepth(document.RootElement) > MaxParametersDepth)
                return $"{prefix}: parameters are nested too deeply (max depth {MaxParametersDepth}).";
        }
        catch (JsonException ex)
        {
            return $"{prefix}: parameters must be valid JSON ({ex.Message}).";
        }

        return null;
    }

    private static int MeasureDepth(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var maxObject = 0;
                foreach (var property in element.EnumerateObject())
                    maxObject = Math.Max(maxObject, MeasureDepth(property.Value));
                return maxObject + 1;

            case JsonValueKind.Array:
                var maxArray = 0;
                foreach (var item in element.EnumerateArray())
                    maxArray = Math.Max(maxArray, MeasureDepth(item));
                return maxArray + 1;

            default:
                return 1;
        }
    }
}
