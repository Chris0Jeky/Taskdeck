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
    private const int MaxDecodeIterations = 16;

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

    private static readonly HashSet<Type> NumericValueTypes = new()
    {
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(float),
        typeof(decimal),
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
        if (options.AllowedKeys is null)
        {
            throw new ArgumentException("AllowedKeys must not be null.", nameof(options));
        }
        if (options.StringValueKeys is null)
        {
            throw new ArgumentException("StringValueKeys must not be null.", nameof(options));
        }
        if (options.StringValueAllowlists is null)
        {
            throw new ArgumentException("StringValueAllowlists must not be null.", nameof(options));
        }
        if (options.NumericValueKeys is null)
        {
            throw new ArgumentException("NumericValueKeys must not be null.", nameof(options));
        }
        if (options.BooleanValueKeys is null)
        {
            throw new ArgumentException("BooleanValueKeys must not be null.", nameof(options));
        }
        foreach (var key in options.StringValueKeys)
        {
            if (!options.StringValueAllowlists.TryGetValue(key, out var allowedValues) || allowedValues is null)
            {
                throw new ArgumentException(
                    $"String key '{key}' must define an allowed value set.",
                    nameof(options));
            }
        }

        _options = CloneOptions(options);
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
            var decoded = GetDecodedCandidates(s);
            if (decoded.HitDecodeLimit)
            {
                return TelemetryValidationResult.Rejected(
                    "String value is encoded too deeply to validate safely.");
            }

            foreach (var candidate in decoded.Candidates)
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

        if (!IsValueShapeAllowed(options, key, value))
        {
            return TelemetryValidationResult.Rejected(
                $"Key '{key}' does not allow values like '{value}' of type '{value.GetType().Name}'.");
        }

        return TelemetryValidationResult.Accepted();
    }

    /// <summary>
    /// Returns the raw string plus bounded URL-decoded and HTML-decoded variants
    /// for pattern matching. This prevents bypass via repeatedly or mixed-encoded
    /// characters such as <c>user%26%2364%3Bexample.com</c>.
    /// </summary>
    private static DecodedCandidateSet GetDecodedCandidates(string raw)
    {
        var candidates = new List<string> { raw };
        var seen = new HashSet<string>(StringComparer.Ordinal) { raw };
        var pending = new Queue<(string Value, int Depth)>();
        pending.Enqueue((raw, 0));
        var hitDecodeLimit = false;

        while (pending.Count > 0)
        {
            var (value, depth) = pending.Dequeue();
            if (depth >= MaxDecodeIterations)
            {
                hitDecodeLimit |= DecodeOneLayer(value).Any();
                continue;
            }

            foreach (var decoded in DecodeOneLayer(value))
            {
                if (seen.Add(decoded))
                {
                    candidates.Add(decoded);
                    pending.Enqueue((decoded, depth + 1));
                }
            }
        }

        return new DecodedCandidateSet(candidates, hitDecodeLimit);
    }

    private static IEnumerable<string> DecodeOneLayer(string value)
    {
        string? urlDecoded = null;

        try
        {
            urlDecoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            // Malformed percent-encoding -- skip URL decoding.
        }

        if (urlDecoded is not null && urlDecoded != value)
        {
            yield return urlDecoded;
        }

        var htmlDecoded = WebUtility.HtmlDecode(value);
        if (htmlDecoded != value)
        {
            yield return htmlDecoded;
        }
    }

    private static TelemetryGuardOptions CloneOptions(TelemetryGuardOptions options)
    {
        return new TelemetryGuardOptions
        {
            MaxStringLength = options.MaxStringLength,
            AllowedKeys = new HashSet<string>(options.AllowedKeys, options.AllowedKeys.Comparer),
            StringValueKeys = new HashSet<string>(options.StringValueKeys, options.StringValueKeys.Comparer),
            StringValueAllowlists = options.StringValueAllowlists.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(pair.Value, pair.Value.Comparer),
                options.StringValueAllowlists.Comparer),
            NumericValueKeys = new HashSet<string>(options.NumericValueKeys, options.NumericValueKeys.Comparer),
            BooleanValueKeys = new HashSet<string>(options.BooleanValueKeys, options.BooleanValueKeys.Comparer),
        };
    }

    private static bool IsValueShapeAllowed(TelemetryGuardOptions options, string key, object value)
    {
        var valueType = value.GetType();

        if (options.StringValueKeys.Contains(key) && value is string stringValue)
        {
            return options.StringValueAllowlists.TryGetValue(key, out var allowedValues) &&
                allowedValues.Contains(stringValue);
        }

        if (options.NumericValueKeys.Contains(key) && NumericValueTypes.Contains(valueType))
            return true;

        if (options.BooleanValueKeys.Contains(key) && value is bool)
            return true;

        return false;
    }

    private sealed record DecodedCandidateSet(
        IReadOnlyList<string> Candidates,
        bool HitDecodeLimit);
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
