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
            "email:bob@example.com",
            "name-company:carolexample|acme");
    }

    [Fact]
    public void Parse_ShouldPreserveEmailPunctuation_WhenBuildingDedupeKey()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Dot,Acme,a.b@example.com
                     Alice Plain,Acme,ab@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().HaveCount(2);
        var dedupeKeys = result.Value.Candidates.Select(candidate => candidate.DedupeKey).ToList();
        dedupeKeys.Should().Contain("email:a.b@example.com");
        dedupeKeys.Should().Contain("email:ab@example.com");
        result.Value.Conflicts.Should().NotContain(conflict => conflict.Code == "DuplicateInputRecord");
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
    public void Parse_ShouldReturnValidationError_WhenExplicitMappedHeaderDoesNotExist()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address
                     Alice Example,Acme,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true,
            Csv: new ExternalImportCsvOptionsDto(EmailColumn: "Email Typo"));

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("emailColumn");
        result.ErrorMessage.Should().Contain("Email Typo");
    }

    [Fact]
    public void Parse_ShouldReturnValidationError_WhenPayloadContainsOnlyEmptyRows()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: ",,\r\n,,\r\n",
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("at least one non-empty header row");
    }

    [Fact]
    public void Parse_ShouldReturnValidationError_WhenHeaderNamesNormalizeToDuplicateValue()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name, Display Name ,Email Address
                     Alice Example,Alice Alias,alice@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("duplicate column names after normalization");
        result.ErrorMessage.Should().ContainEquivalentOf("Display Name");
    }

    [Fact]
    public void Parse_ShouldReturnValidationError_WhenHeadersDifferOnlyByCaseOrWhitespace()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Email Address,email address
                     alice@example.com,alice+alt@example.com
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("duplicate column names after normalization");
        result.ErrorMessage.Should().ContainEquivalentOf("email address");
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

    [Fact]
    public void Parse_ShouldTreatNonIsoLastTouchDateAsInvalid()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,last_touch_at
                     Alice Example,Acme,01/02/2024
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        result.Value.Conflicts.Should().ContainSingle(conflict =>
            conflict.Code == "InvalidDate" &&
            conflict.IncomingValue == "01/02/2024");
    }

    [Fact]
    public void Parse_ShouldHandleUtf8BomInFirstHeaderCell()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: $"""
                      {"\uFEFF"}Display Name,Company,Email Address
                      Alice Example,Acme,
                      """,
            TargetColumnName: "Imported",
            DryRun: true,
            Csv: new ExternalImportCsvOptionsDto(
                DisplayNameColumn: "Display Name",
                CompanyColumn: "Company",
                EmailColumn: "Email Address"));

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle(candidate =>
            candidate.DedupeKey == "name-company:aliceexample|acme" &&
            candidate.Title == "Alice Example");
    }

    [Fact]
    public void Parse_ShouldEmitConflict_WhenCardTitleExceedsDomainLimit()
    {
        var overlyLongName = new string('A', 250);
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: $"""
                      Display Name,Company,Email Address
                      {overlyLongName},Acme,alice@example.com
                      """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        result.Value.Conflicts.Should().ContainSingle(conflict =>
            conflict.Code == "TitleTooLong" &&
            conflict.Path == "$.rows[2].title" &&
            conflict.IncomingValue == "length=250");
    }

    [Fact]
    public void Parse_ShouldPopulateIncomingValues_ForActionableConflicts()
    {
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: """
                     Display Name,Company,Email Address,last_touch_at
                     Alice Example,Acme,alice@example.com,not-a-date
                     Alice Duplicate,Acme,alice@example.com,2024-01-01T00:00:00Z
                     Charlie,,,
                     """,
            TargetColumnName: "Imported",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Conflicts.Should().Contain(conflict =>
            conflict.Code == "InvalidDate" &&
            conflict.IncomingValue == "not-a-date");
        result.Value.Conflicts.Should().Contain(conflict =>
            conflict.Code == "DuplicateInputRecord" &&
            conflict.IncomingValue == "email:alice@example.com");
        result.Value.Conflicts.Should().Contain(conflict =>
            conflict.Code == "MissingDedupeKey" &&
            conflict.IncomingValue != null &&
            conflict.IncomingValue.Contains("display_name='Charlie'"));
    }
}
