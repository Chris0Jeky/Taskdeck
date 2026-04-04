using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Cross-format integrity tests: verifies that feeding the output of one
/// export format into another import format produces appropriate errors,
/// and that format-specific validation detects mismatches.
/// </summary>
public class CrossFormatImportIntegrityTests
{
    private readonly CsvExternalImportAdapter _csvAdapter = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void BoardJsonExport_FedAsCsvPayload_ProducesZeroCandidates()
    {
        // Create a board JSON export payload
        var now = DateTimeOffset.UtcNow;
        var exportDto = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Test Board", "Description", false, now, now),
            new[] { new ColumnDto(Guid.NewGuid(), Guid.NewGuid(), "Todo", 0, null, 0, now, now) },
            Array.Empty<CardDto>(),
            Array.Empty<LabelDto>(),
            new List<BoardAccessDto>(),
            now, "tester");

        var jsonPayload = JsonSerializer.Serialize(exportDto, JsonOptions);

        // Try to import this JSON as CSV
        var request = new ExternalImportRequestDto(
            Provider: "csv",
            Payload: jsonPayload,
            TargetColumnName: "Todo",
            DryRun: true);

        var result = _csvAdapter.Parse(request);

        // JSON is technically parseable as single-column CSV (the "{" line becomes the header),
        // but no recognized column aliases exist, so zero candidates are produced.
        result.IsSuccess.Should().BeTrue("JSON is syntactically parseable as degenerate CSV");
        result.Value.Candidates.Should().BeEmpty(
            "JSON payload should not produce valid CSV candidates because no column aliases match");
    }

    [Fact]
    public void CsvPayload_FedAsBoardJson_FailsDeserialization()
    {
        var csvPayload = "display_name,email,company\nAlice,alice@test.com,Acme\n";

        // Try to deserialize CSV as ImportBoardDto
        var result = BoardJsonExportImportService.TryDeserializeImportDto(csvPayload);
        result.Should().BeNull("CSV text is not valid JSON and should not deserialize as board import");
    }

    [Fact]
    public void RandomBinaryData_FedAsBoardJson_FailsGracefully()
    {
        var binaryGarbage = Convert.ToBase64String(new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0xAB, 0xCD });

        var result = BoardJsonExportImportService.TryDeserializeImportDto(binaryGarbage);
        result.Should().BeNull("binary garbage should not parse as board import JSON");
    }

    [Fact]
    public void ArrayJson_FedAsBoardImport_FailsGracefully()
    {
        // Valid JSON but wrong shape (array instead of object)
        var arrayJson = "[{\"name\":\"test\"}]";

        var result = BoardJsonExportImportService.TryDeserializeImportDto(arrayJson);
        result.Should().BeNull("JSON array should not parse as a board import DTO");
    }

    [Fact]
    public void NumericJson_FedAsBoardImport_FailsGracefully()
    {
        var result = BoardJsonExportImportService.TryDeserializeImportDto("42");
        result.Should().BeNull("numeric JSON should not parse as board import");
    }

    [Fact]
    public void NullJson_FedAsBoardImport_FailsGracefully()
    {
        var result = BoardJsonExportImportService.TryDeserializeImportDto("null");
        result.Should().BeNull("JSON null should not parse as board import");
    }

    [Fact]
    public void StringJson_FedAsBoardImport_FailsGracefully()
    {
        var result = BoardJsonExportImportService.TryDeserializeImportDto("\"just a string\"");
        result.Should().BeNull("JSON string should not parse as board import");
    }

    [Fact]
    public void ValidImportJson_AcceptedByTryDeserialize()
    {
        var importJson = JsonSerializer.Serialize(new ImportBoardDto(
            "Test Board",
            "Description",
            new[] { new ImportColumnDto("Todo", 0, null) },
            new[] { new ImportCardDto("Card 1", "Desc", "Todo", 0, null, null) },
            new[] { new ImportLabelDto("Bug", "#FF0000") }
        ), JsonOptions);

        var result = BoardJsonExportImportService.TryDeserializeImportDto(importJson);
        result.Should().NotBeNull("valid import JSON should be accepted");
        result!.Name.Should().Be("Test Board");
    }

    [Fact]
    public void ValidExportJson_ConvertedToImportByTryDeserialize()
    {
        var now = DateTimeOffset.UtcNow;
        var colId = Guid.NewGuid();
        var exportDto = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Exported Board", "Desc", false, now, now),
            new[] { new ColumnDto(colId, Guid.NewGuid(), "Backlog", 0, null, 1, now, now) },
            new[] { new CardDto(Guid.NewGuid(), Guid.NewGuid(), colId, "Card A", null, null, false, null, 0, new List<LabelDto>(), now, now) },
            Array.Empty<LabelDto>(),
            new List<BoardAccessDto>(),
            now, "tester");

        var json = JsonSerializer.Serialize(exportDto, JsonOptions);

        var result = BoardJsonExportImportService.TryDeserializeImportDto(json);
        result.Should().NotBeNull("export JSON should be auto-converted to import shape");
        result!.Name.Should().Be("Exported Board");
        result.Columns.Should().ContainSingle(c => c.Name == "Backlog");
        result.Cards.Should().ContainSingle(c => c.Title == "Card A");
    }

    [Fact]
    public void JsonWithTrailingComma_RejectedGracefully()
    {
        var badJson = "{\"name\":\"Test\",\"columns\":[],\"cards\":[],\"labels\":[],}";

        var result = BoardJsonExportImportService.TryDeserializeImportDto(badJson);
        // System.Text.Json rejects trailing commas by default
        result.Should().BeNull("JSON with trailing commas should not parse");
    }

    [Fact]
    public void JsonWithComments_RejectedGracefully()
    {
        var commentJson = "{ /* comment */ \"name\":\"Test\",\"columns\":[],\"cards\":[],\"labels\":[] }";

        var result = BoardJsonExportImportService.TryDeserializeImportDto(commentJson);
        // System.Text.Json rejects comments by default
        result.Should().BeNull("JSON with comments should not parse");
    }
}
