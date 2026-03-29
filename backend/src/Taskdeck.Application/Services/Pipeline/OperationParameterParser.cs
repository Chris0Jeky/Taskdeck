using System.Text.Json;

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
}
