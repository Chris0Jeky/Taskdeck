using System.Text;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Round-trip and edge-case tests for CSV external import parsing.
/// Covers RFC 4180 edge cases, special characters, large files,
/// missing fields, deduplication, and format validation.
/// </summary>
public class CsvImportRoundTripTests
{
    private readonly CsvExternalImportAdapter _adapter = new();

    [Fact]
    public void Parse_StandardCsv_ProducesCorrectCandidates()
    {
        var csv = "display_name,email,company,role,linkedin_url\n" +
                  "Alice Smith,alice@example.com,Acme Corp,Engineer,https://linkedin.com/in/alice\n" +
                  "Bob Jones,bob@example.com,Widget Inc,Manager,https://linkedin.com/in/bob\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsReceived.Should().Be(2);
        result.Value.RowsParsed.Should().Be(2);
        result.Value.Candidates.Should().HaveCount(2);
        result.Value.Conflicts.Should().BeEmpty();

        result.Value.Candidates[0].Title.Should().Be("Alice Smith");
        result.Value.Candidates[1].Title.Should().Be("Bob Jones");
    }

    [Fact]
    public void Parse_CommasInQuotedFields_ParsedCorrectly()
    {
        var csv = "display_name,email,company\n" +
                  "\"Smith, Alice\",alice@example.com,\"Acme, Corp\"\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
        result.Value.Candidates[0].Title.Should().Be("Smith, Alice");
    }

    [Fact]
    public void Parse_QuotedStringsWithEscapedQuotes_ParsedCorrectly()
    {
        var csv = "display_name,email,company\n" +
                  "\"Alice \"\"The Great\"\" Smith\",alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
        // The title should contain the unescaped quotes
        result.Value.Candidates[0].Title.Should().Contain("Alice \"The Great\" Smith");
    }

    [Fact]
    public void Parse_NewlinesInQuotedFields_ParsedCorrectly()
    {
        var csv = "display_name,email,company\n" +
                  "\"Alice\nSmith\",alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
        result.Value.Candidates[0].Title.Should().Contain("Alice\nSmith");
    }

    [Fact]
    public void Parse_UnicodeAndSpecialCharacters_PreservedInOutput()
    {
        var csv = "display_name,email,company\n" +
                  "\u00c9milie Br\u00f6nte,emilie@example.com,Caf\u00e9 Corp\n" +
                  "\u5c71\u7530\u592a\u90ce,yamada@example.com,\u682a\u5f0f\u4f1a\u793e\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().HaveCount(2);
        result.Value.Candidates[0].Title.Should().Be("\u00c9milie Br\u00f6nte");
        result.Value.Candidates[1].Title.Should().Be("\u5c71\u7530\u592a\u90ce");
    }

    [Fact]
    public void Parse_MissingRequiredDedupeFields_ReportsConflicts()
    {
        // Row with no email, no linkedin, only a display_name but no company (can't form dedupe key)
        var csv = "display_name,email,company\n" +
                  "JustAName,,\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty();
        result.Value.Conflicts.Should().ContainSingle();
        result.Value.Conflicts[0].Code.Should().Be("MissingDedupeKey");
    }

    [Fact]
    public void Parse_DuplicateEntries_DeduplicationApplied()
    {
        var csv = "display_name,email,company\n" +
                  "Alice Smith,alice@example.com,Acme\n" +
                  "Alice Smith,alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle("duplicate should be reported as conflict");
        result.Value.Conflicts.Should().ContainSingle();
        result.Value.Conflicts[0].Code.Should().Be("DuplicateInputRecord");
    }

    [Fact]
    public void Parse_LargeCsv_1000Rows_CompletesWithoutError()
    {
        var sb = new StringBuilder();
        sb.AppendLine("display_name,email,company,role,linkedin_url");
        for (var i = 0; i < 1000; i++)
        {
            sb.AppendLine($"Contact {i},contact{i}@example.com,Company {i},Role {i},https://linkedin.com/in/contact{i}");
        }

        var request = CreateRequest(sb.ToString());
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsReceived.Should().Be(1000);
        result.Value.RowsParsed.Should().Be(1000);
        result.Value.Candidates.Should().HaveCount(1000);
    }

    [Fact]
    public void Parse_EmptyPayload_ReturnsValidationError()
    {
        var request = CreateRequest("");
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void Parse_HeaderOnly_NoCandidates()
    {
        var csv = "display_name,email,company\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsReceived.Should().Be(0);
        result.Value.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WrongProvider_ReturnsValidationError()
    {
        var request = new ExternalImportRequestDto(
            Provider: "json",
            Payload: "display_name,email\nAlice,alice@test.com",
            TargetColumnName: "Todo",
            DryRun: true);

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("CSV adapter cannot parse");
    }

    [Fact]
    public void Parse_UnclosedQuotedField_ReturnsValidationError()
    {
        var csv = "display_name,email\n" +
                  "\"Alice Smith,alice@example.com\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("unclosed quoted field");
    }

    [Fact]
    public void Parse_DuplicateHeaderColumns_ReturnsValidationError()
    {
        var csv = "display_name,email,display_name\n" +
                  "Alice,alice@test.com,Bob\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("duplicate column names");
    }

    [Fact]
    public void Parse_ExceedsMaxPayloadSize_ReturnsValidationError()
    {
        // Create a payload larger than 1MB
        var largePayload = "display_name,email\n" + new string('A', 1024 * 1024 + 1);

        var request = CreateRequest(largePayload);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("exceeds max size");
    }

    [Fact]
    public void Parse_InvalidDateFormat_ReportsConflict()
    {
        var csv = "display_name,email,last_touch_at\n" +
                  "Alice,alice@example.com,not-a-date\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().BeEmpty("row with invalid date should be skipped");
        result.Value.Conflicts.Should().ContainSingle();
        result.Value.Conflicts[0].Code.Should().Be("InvalidDate");
    }

    [Fact]
    public void Parse_ValidDateFormats_AcceptedCorrectly()
    {
        var csv = "display_name,email,last_touch_at\n" +
                  "Alice,alice1@example.com,2025-06-15\n" +
                  "Bob,bob1@example.com,2025-06-15T14:30:00\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().HaveCount(2, "both date formats should parse");
    }

    [Fact]
    public void Parse_CrLfLineEndings_ParsedCorrectly()
    {
        var csv = "display_name,email,company\r\n" +
                  "Alice,alice@example.com,Acme\r\n" +
                  "Bob,bob@example.com,Widget\r\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_BomPrefix_HandledGracefully()
    {
        // UTF-8 BOM character at start of file
        var csv = "\uFEFFdisplay_name,email,company\n" +
                  "Alice,alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void Parse_ExplicitColumnMappingToNonexistentHeader_ReturnsError()
    {
        var csv = "display_name,email\nAlice,alice@test.com\n";

        var request = new ExternalImportRequestDto(
            Provider: "csv",
            Payload: csv,
            TargetColumnName: "Todo",
            DryRun: true,
            Csv: new ExternalImportCsvOptionsDto(DisplayNameColumn: "nonexistent_column"));

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("nonexistent_column");
    }

    [Fact]
    public void Parse_UnsupportedProfile_ReturnsValidationError()
    {
        var csv = "display_name,email\nAlice,alice@test.com\n";

        var request = new ExternalImportRequestDto(
            Provider: "csv",
            Payload: csv,
            TargetColumnName: "Todo",
            DryRun: true,
            Profile: "unsupported.profile.v99");

        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported import profile");
    }

    [Fact]
    public void Parse_PartialData_FirstAndLastNameFallback()
    {
        // No display_name, but first_name + last_name should be combined
        var csv = "first_name,last_name,email,company\n" +
                  "Alice,Smith,alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
        result.Value.Candidates[0].Title.Should().Be("Alice Smith");
    }

    [Fact]
    public void Parse_EmailOnlyForTitle_WhenNameAndLinkedInMissing()
    {
        var csv = "email,company\n" +
                  "alice@example.com,Acme\n";

        var request = CreateRequest(csv);
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Candidates.Should().ContainSingle();
        result.Value.Candidates[0].Title.Should().Be("alice@example.com");
    }

    [Fact]
    public void Parse_ExceedsMaxRowCount_ReturnsValidationError()
    {
        var sb = new StringBuilder();
        sb.AppendLine("display_name,email,company");
        // Max is 5001 including header = 5000 data rows. We try 5001 data rows.
        for (var i = 0; i < 5001; i++)
        {
            sb.AppendLine($"Contact {i},contact{i}@example.com,Company {i}");
        }

        var request = CreateRequest(sb.ToString());
        var result = _adapter.Parse(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("max row count");
    }

    private static ExternalImportRequestDto CreateRequest(string payload)
    {
        return new ExternalImportRequestDto(
            Provider: "csv",
            Payload: payload,
            TargetColumnName: "Todo",
            DryRun: true);
    }
}
