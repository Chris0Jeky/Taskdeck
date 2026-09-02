using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Taskdeck.Acceleration.Candidates.WorkModel;

public enum CandidateCustomFieldType
{
    Text,
    Number,
    Date,
    Boolean,
    SingleSelect,
    Url
}

public sealed record CandidateCustomFieldDefinition(
    Guid Id,
    CandidateCustomFieldType Type,
    bool IsRetired,
    int? MaximumTextLength = null,
    decimal? MinimumNumber = null,
    decimal? MaximumNumber = null,
    int? MaximumScale = null,
    IReadOnlySet<string>? AllowedOptionIds = null);

public sealed record FieldValidationResult(bool IsValid, string? ErrorCode = null)
{
    public static FieldValidationResult Valid() => new(true);
    public static FieldValidationResult Invalid(string code) => new(false, code);
}

public static class CustomFieldValueValidator
{
    public static FieldValidationResult Validate(
        CandidateCustomFieldDefinition definition,
        JsonElement value,
        bool allowRetiredValueWrite = false)
    {
        if (definition.IsRetired && !allowRetiredValueWrite)
        {
            return FieldValidationResult.Invalid("custom_field_retired");
        }

        return definition.Type switch
        {
            CandidateCustomFieldType.Text => ValidateText(definition, value),
            CandidateCustomFieldType.Number => ValidateNumber(definition, value),
            CandidateCustomFieldType.Date => ValidateDate(value),
            CandidateCustomFieldType.Boolean => value.ValueKind == JsonValueKind.True
                                                || value.ValueKind == JsonValueKind.False
                ? FieldValidationResult.Valid()
                : FieldValidationResult.Invalid("custom_field_boolean_invalid"),
            CandidateCustomFieldType.SingleSelect => ValidateSingleSelect(definition, value),
            CandidateCustomFieldType.Url => ValidateUrl(value),
            _ => FieldValidationResult.Invalid("custom_field_type_unknown")
        };
    }

    private static FieldValidationResult ValidateText(
        CandidateCustomFieldDefinition definition,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Invalid("custom_field_text_invalid");
        }

        var text = value.GetString() ?? string.Empty;
        return definition.MaximumTextLength is { } maximum && text.Length > maximum
            ? FieldValidationResult.Invalid("custom_field_text_too_long")
            : FieldValidationResult.Valid();
    }

    private static FieldValidationResult ValidateNumber(
        CandidateCustomFieldDefinition definition,
        JsonElement value)
    {
        decimal number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDecimal(out number))
            {
                return FieldValidationResult.Invalid("custom_field_number_invalid");
            }
        }
        else if (value.ValueKind == JsonValueKind.String
                 && decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                     CultureInfo.InvariantCulture, out var parsed))
        {
            number = parsed;
        }
        else
        {
            return FieldValidationResult.Invalid("custom_field_number_invalid");
        }

        if (definition.MinimumNumber is { } minimum && number < minimum)
        {
            return FieldValidationResult.Invalid("custom_field_number_below_minimum");
        }

        if (definition.MaximumNumber is { } maximum && number > maximum)
        {
            return FieldValidationResult.Invalid("custom_field_number_above_maximum");
        }

        if (definition.MaximumScale is { } maximumScale
            && GetScale(number) > maximumScale)
        {
            return FieldValidationResult.Invalid("custom_field_number_scale_exceeded");
        }

        return FieldValidationResult.Valid();
    }

    private static FieldValidationResult ValidateDate(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String
               && DateOnly.TryParseExact(
                   value.GetString(),
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out _)
            ? FieldValidationResult.Valid()
            : FieldValidationResult.Invalid("custom_field_date_invalid");
    }

    private static FieldValidationResult ValidateSingleSelect(
        CandidateCustomFieldDefinition definition,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return FieldValidationResult.Invalid("custom_field_option_invalid");
        }

        var optionId = value.GetString();
        return optionId is not null
               && definition.AllowedOptionIds is not null
               && definition.AllowedOptionIds.Contains(optionId)
            ? FieldValidationResult.Valid()
            : FieldValidationResult.Invalid("custom_field_option_not_allowed");
    }

    private static FieldValidationResult ValidateUrl(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri))
        {
            return FieldValidationResult.Invalid("custom_field_url_invalid");
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? FieldValidationResult.Valid()
            : FieldValidationResult.Invalid("custom_field_url_scheme_not_allowed");
    }

    private static int GetScale(decimal number)
    {
        var bits = decimal.GetBits(number);
        return (bits[3] >> 16) & 0x7F;
    }
}
