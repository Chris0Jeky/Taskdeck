using System.Net;
using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates telemetry metric keys and values to prevent PII leakage.
/// All validation is allowlist-based: unknown keys are rejected.
/// String values are checked for URLs, email patterns, and length limits.
/// Numeric values are checked for non-finite doubles (NaN, Infinity).
///
/// All regex patterns use RegexOptions.Compiled and are designed to avoid
/// catastrophic backtracking (ReDoS). Patterns are anchored or bounded
/// and avoid nested quantifiers.
/// </summary>
public static class TelemetryGuard
{
    /// <summary>
    /// Matches URLs (http/https/ftp with ://). Anchored to avoid backtracking.
    /// Pattern: protocol :// followed by non-whitespace characters.
    /// No nested quantifiers -- linear scan only.
    /// </summary>
    private static readonly Regex UrlPattern = new(
        @"https?://\S+|ftp://\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Matches email-like strings: word chars/dots/hyphens @ word chars/dots/hyphens.
    /// No nested quantifiers -- each character class matches exactly one char.
    /// </summary>
    private static readonly Regex EmailPattern = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Allowed value types for telemetry properties. Only primitives are accepted;
    /// complex objects (dictionaries, DTOs, arrays) could smuggle user content or PII.
    /// </summary>
    private static readonly HashSet<Type> AllowedValueTypes = new()
    {
        typeof(string),
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(float),
        typeof(decimal),
        typeof(bool),
        typeof(byte),
        typeof(short),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(sbyte),
    };

    private static volatile TelemetryGuardOptions _options = new();

    /// <summary>
    /// Configures the guard with custom options. Call once at startup.
    /// Thread-safe: uses volatile write semantics via reference assignment.
    /// </summary>
    public static void Configure(TelemetryGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Resets options to defaults. Intended for test cleanup only.
    /// </summary>
    internal static void ResetToDefaults()
    {
        _options = new TelemetryGuardOptions();
    }

    /// <summary>
    /// Validates a telemetry metric key-value pair.
    /// Returns a <see cref="TelemetryValidationResult"/> indicating pass/fail with reason.
    /// </summary>
    public static TelemetryValidationResult Validate(string key, object? value)
    {
        var options = _options;

        // Reject null values
        if (value is null)
        {
            return TelemetryValidationResult.Rejected("Value must not be null.");
        }

        // Reject unknown keys
        if (string.IsNullOrWhiteSpace(key))
        {
            return TelemetryValidationResult.Rejected("Key must not be null or empty.");
        }

        if (!options.AllowedKeys.Contains(key))
        {
            return TelemetryValidationResult.Rejected($"Key '{key}' is not in the allowlist.");
        }

        // Reject unsupported value types -- only primitives are allowed.
        // Complex objects (dictionaries, DTOs, arrays, etc.) could carry user
        // content or PII that bypasses string/numeric validation below.
        if (!AllowedValueTypes.Contains(value.GetType()))
        {
            return TelemetryValidationResult.Rejected(
                $"Value type '{value.GetType().Name}' is not supported. Only primitive types (string, int, long, double, float, decimal, bool) are allowed.");
        }

        // Validate numeric values
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                return TelemetryValidationResult.Rejected("Double value must be finite (not NaN or Infinity).");
            }
        }

        if (value is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                return TelemetryValidationResult.Rejected("Float value must be finite (not NaN or Infinity).");
            }
        }

        // Validate string values
        if (value is string s)
        {
            if (s.Length > options.MaxStringLength)
            {
                return TelemetryValidationResult.Rejected(
                    $"String value exceeds maximum length of {options.MaxStringLength} characters.");
            }

            // Decode URL-encoded and HTML-encoded forms before pattern matching
            // to prevent bypass via encoded characters (e.g., user%40example.com).
            var candidates = GetDecodedCandidates(s);

            foreach (var candidate in candidates)
            {
                try
                {
                    if (UrlPattern.IsMatch(candidate))
                    {
                        return TelemetryValidationResult.Rejected("String value must not contain URLs.");
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return TelemetryValidationResult.Rejected("String value triggered regex timeout (potential ReDoS).");
                }

                try
                {
                    if (EmailPattern.IsMatch(candidate))
                    {
                        return TelemetryValidationResult.Rejected("String value must not contain email addresses.");
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return TelemetryValidationResult.Rejected("String value triggered regex timeout (potential ReDoS).");
                }
            }
        }

        return TelemetryValidationResult.Accepted();
    }

    /// <summary>
    /// Returns the raw string plus URL-decoded and HTML-decoded variants for
    /// pattern matching. This prevents bypass via encoded characters such as
    /// <c>user%40example.com</c> (URL-encoded @) or <c>https&#58;//evil.com</c>
    /// (HTML-encoded colon). Deduplicates to avoid redundant regex passes.
    /// </summary>
    private static List<string> GetDecodedCandidates(string raw)
    {
        var candidates = new List<string>(3) { raw };

        try
        {
            var urlDecoded = Uri.UnescapeDataString(raw);
            if (urlDecoded != raw)
            {
                candidates.Add(urlDecoded);
            }
        }
        catch (UriFormatException)
        {
            // Malformed percent-encoding -- skip URL decoding
        }

        var htmlDecoded = WebUtility.HtmlDecode(raw);
        if (htmlDecoded != raw && !candidates.Contains(htmlDecoded))
        {
            candidates.Add(htmlDecoded);
        }

        return candidates;
    }
}

/// <summary>
/// Result of a telemetry guard validation check.
/// </summary>
public sealed class TelemetryValidationResult
{
    public bool IsValid { get; }
    public string? Reason { get; }

    private TelemetryValidationResult(bool isValid, string? reason)
    {
        IsValid = isValid;
        Reason = reason;
    }

    public static TelemetryValidationResult Accepted() => new(true, null);
    public static TelemetryValidationResult Rejected(string reason) => new(false, reason);
}
