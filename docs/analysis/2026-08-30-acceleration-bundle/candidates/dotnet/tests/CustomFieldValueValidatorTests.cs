using System;
using System.Text.Json;
using Taskdeck.Acceleration.Candidates.WorkModel;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.WorkModel;

public sealed class CustomFieldValueValidatorTests
{
    [Fact]
    public void Invariant_number_does_not_accept_thousands_separator()
    {
        using var document = JsonDocument.Parse("\"1,000\"");
        var definition = new CandidateCustomFieldDefinition(Guid.NewGuid(), CandidateCustomFieldType.Number, false);
        var result = CustomFieldValueValidator.Validate(definition, document.RootElement);
        Assert.False(result.IsValid);
        Assert.Equal("custom_field_number_invalid", result.ErrorCode);
    }

    [Fact]
    public void Retired_definition_rejects_new_write()
    {
        using var document = JsonDocument.Parse("true");
        var definition = new CandidateCustomFieldDefinition(Guid.NewGuid(), CandidateCustomFieldType.Boolean, true);
        var result = CustomFieldValueValidator.Validate(definition, document.RootElement);
        Assert.Equal("custom_field_retired", result.ErrorCode);
    }
}
