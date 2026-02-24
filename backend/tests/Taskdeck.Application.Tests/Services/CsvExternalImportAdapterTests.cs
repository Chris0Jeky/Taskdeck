using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CsvExternalImportAdapterTests
{
    private readonly CsvExternalImportAdapter _adapter = new();

    [Fact]
    public void Parse_ShouldApplyDeterministicDedupeKeyOrder_LinkedInThenEmailThenNameCompany()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,LinkedIn URL
                     Alice Example,Acme,alice@example.com,https://linkedin.com/in/alice
                     Bob Example,Acme,bob@example.com,
                     Carol Example,Acme,,
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Select(candidate => candidate.DedupeKey).Should().ContainInOrder(
            "linkedin:https://linkedin.com/in/alice",
            "email:bobexamplecom",
            "name-company:carolexample|acme");
    }

    [Fact]
    public void Parse_ShouldReturnValidationError_WhenCsvHasUnclosedQuote()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company
                     "Alice,Acme
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("unclosed quoted field");
    }

    [Fact]
    public void Parse_ShouldEmitConflict_WhenInputContainsDuplicateDedupeKeys()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Example,Acme,alice@example.com
                     Alice Duplicate,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().HaveCount(1);
        result.Value.Conflicts.Should().ContainSingle(conflict => conflict.Code == "DuplicateInputRecord");
    }

    [Fact]
    public void Parse_ShouldEmitConflict_WhenLastTouchDateCannotBeParsed()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,last_touch_at
                     Alice Example,Acme,not-a-date
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        result.Value.Conflicts.Should().ContainSingle(conflict =>
            conflict.Code == "InvalidDate" &&
            conflict.Path == "$.rows[2].last_touch_at");
    }
}
