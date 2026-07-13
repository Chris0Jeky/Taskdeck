using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Provides JSON parameter parsing and extraction utilities for automation proposal operations.
/// </summary>
public static class OperationParameterParser
{
    public static bool TryDeserializeParameters(string rawParameters, out JsonElement parameters, out string error)
    {
        parameters = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawParameters))
        {
            error = "Operation parameters cannot be empty";
            return false;
        }

        try
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(rawParameters);
            if (parameters.ValueKind != JsonValueKind.Object)
            {
                error = "Operation parameters must be a JSON object";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid operation parameters JSON: {ex.Message}";
            return false;
        }
    }

    public static bool TryGetRequiredString(JsonElement parameters, string parameterName, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property))
        {
            error = $"Missing required parameter '{parameterName}'";
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Parameter '{parameterName}' must be a string";
            return false;
        }

        var parsed = property.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            error = $"Parameter '{parameterName}' cannot be empty";
            return false;
        }

        value = parsed;
        return true;
    }

    public static string? GetOptionalString(JsonElement parameters, string parameterName)
    {
        if (!parameters.TryGetProperty(parameterName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool? GetOptionalBoolean(JsonElement parameters, string parameterName)
    {
        if (!parameters.TryGetProperty(parameterName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static bool TryGetRequiredGuid(JsonElement parameters, string parameterName, out Guid value, out string error)
    {
        value = Guid.Empty;

        if (!TryGetRequiredString(parameters, parameterName, out var rawValue, out error))
            return false;

        if (!Guid.TryParse(rawValue, out value))
        {
            error = $"Invalid {parameterName}";
            return false;
        }

        return true;
    }

    public static bool TryGetRequiredInt32(JsonElement parameters, string parameterName, out int value, out string error)
    {
        value = 0;
        error = string.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property))
        {
            error = $"Missing required parameter '{parameterName}'";
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out value))
        {
            error = $"Parameter '{parameterName}' must be an integer";
            return false;
        }

        return true;
    }

    public static bool TryGetGuidFromParameters(JsonElement parameters, string parameterName, out Guid value)
    {
        value = Guid.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        var raw = property.GetString();
        return Guid.TryParse(raw, out value);
    }

    public static bool TryGetOptionalDateTimeOffset(
        JsonElement parameters,
        string parameterName,
        out bool wasProvided,
        out DateTimeOffset? value,
        out string error)
    {
        wasProvided = parameters.TryGetProperty(parameterName, out var property);
        value = null;
        error = string.Empty;

        if (!wasProvided || property.ValueKind == JsonValueKind.Null)
            return true;

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Parameter '{parameterName}' must be an ISO-8601 string or null";
            return false;
        }

        var raw = property.GetString();
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            value = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = $"Parameter '{parameterName}' must be a valid ISO-8601 date or timestamp";
            return false;
        }

        var hasUtcSuffix = raw.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        var hasOffsetSuffix = Regex.IsMatch(raw, @"[+-]\d{2}:\d{2}$", RegexOptions.CultureInvariant);
        if (!hasUtcSuffix && !hasOffsetSuffix)
        {
            error = $"Parameter '{parameterName}' must be a valid ISO-8601 timestamp with an explicit UTC or numeric offset";
            return false;
        }

        var formats = hasUtcSuffix
            ? new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" }
            : new[] { "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" };
        var styles = hasUtcSuffix
            ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
            : DateTimeStyles.None;

        if (!DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture, styles, out var parsed))
        {
            error = $"Parameter '{parameterName}' must be a valid ISO-8601 date or timestamp";
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }

    public static bool TryGetOptionalStringArray(
        JsonElement parameters,
        string parameterName,
        out bool wasProvided,
        out IReadOnlyList<string> values,
        out string error)
    {
        wasProvided = parameters.TryGetProperty(parameterName, out var property);
        values = Array.Empty<string>();
        error = string.Empty;

        if (!wasProvided)
            return true;

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = $"Parameter '{parameterName}' must be an array of non-empty strings";
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                error = $"Parameter '{parameterName}' must contain only non-empty strings";
                return false;
            }

            parsed.Add(item.GetString()!.Trim());
        }

        values = parsed;
        return true;
    }

    public static bool TryGetOptionalGuidArray(
        JsonElement parameters,
        string parameterName,
        out bool wasProvided,
        out IReadOnlyList<Guid> values,
        out string error)
    {
        values = Array.Empty<Guid>();
        if (!TryGetOptionalStringArray(parameters, parameterName, out wasProvided, out var rawValues, out error))
            return false;

        var parsed = new List<Guid>();
        foreach (var rawValue in rawValues)
        {
            if (!Guid.TryParse(rawValue, out var value) || value == Guid.Empty)
            {
                error = $"Parameter '{parameterName}' must contain only non-empty UUID strings";
                return false;
            }

            parsed.Add(value);
        }

        values = parsed;
        return true;
    }

    public static bool TryGetOptionalBoolean(
        JsonElement parameters,
        string parameterName,
        out bool wasProvided,
        out bool value,
        out string error)
    {
        wasProvided = parameters.TryGetProperty(parameterName, out var property);
        value = false;
        error = string.Empty;

        if (!wasProvided)
            return true;

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"Parameter '{parameterName}' must be a boolean";
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
