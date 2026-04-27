using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProvenanceQueryServiceTests
{
    private readonly Mock<IProposalProvenanceRepository> _provenanceRepo = new();
    private readonly ProvenanceQueryService _service;

    public ProvenanceQueryServiceTests()
    {
        _service = new ProvenanceQueryService(_provenanceRepo.Object);
    }

    // ----- GetProvenanceRowsAsync -----

    [Fact]
    public async Task GetProvenanceRowsAsync_ReturnsEmptyList_WhenNoProvenanceExists()
    {
        var proposalId = Guid.NewGuid();
        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalProvenance?)null);

        var result = await _service.GetProvenanceRowsAsync(proposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_ReturnsValidationError_WhenProposalIdIsEmpty()
    {
        var result = await _service.GetProvenanceRowsAsync(Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MapsExtractiveFieldCorrectly()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "title",
            ProvenanceKind.Extractive,
            0.95,
            provenance.Id,
            "Fix the login bug");
        provenance.AddField(field);

        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var row = result.Value[0];
        row.Key.Should().Be("title");
        row.Weight.Should().Be("primary");
        row.Value.Should().Contain("Fix the login bug");
        row.Value.Should().Contain("95%");
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MapsInferredFieldCorrectly()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "due date",
            ProvenanceKind.Inferred,
            0.65,
            provenance.Id);
        provenance.AddField(field);

        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId);

        result.IsSuccess.Should().BeTrue();
        var row = result.Value[0];
        row.Key.Should().Be("due date");
        row.Weight.Should().Be("inferred");
        row.Value.Should().Contain("Inferred");
        row.Value.Should().Contain("65%");
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MapsMultipleFields()
    {
        var provenance = CreateProvenance();
        provenance.AddField(new ProvenanceField("title", ProvenanceKind.Extractive, 0.9, provenance.Id, "Task title"));
        provenance.AddField(new ProvenanceField("label", ProvenanceKind.Extractive, 0.5, provenance.Id, "bug"));
        provenance.AddField(new ProvenanceField("priority", ProvenanceKind.Inferred, 0.7, provenance.Id));

        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    // ----- Weight Mapping -----

    [Theory]
    [InlineData(ProvenanceKind.Extractive, 1.0, "primary")]
    [InlineData(ProvenanceKind.Extractive, 0.7, "primary")]
    [InlineData(ProvenanceKind.Extractive, 0.69, "contextual")]
    [InlineData(ProvenanceKind.Extractive, 0.5, "contextual")]
    [InlineData(ProvenanceKind.Extractive, 0.0, "contextual")]
    [InlineData(ProvenanceKind.Inferred, 1.0, "inferred")]
    [InlineData(ProvenanceKind.Inferred, 0.5, "inferred")]
    [InlineData(ProvenanceKind.Inferred, 0.0, "inferred")]
    public void MapWeight_CorrectlyBuckets(ProvenanceKind kind, double confidence, string expected)
    {
        var result = ProvenanceQueryService.MapWeight(kind, confidence);
        result.Should().Be(expected);
    }

    // ----- Icon Mapping -----

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("card body")]
    [InlineData("label")]
    [InlineData("column")]
    [InlineData("due date")]
    [InlineData("assignee")]
    [InlineData("priority")]
    [InlineData("board activity")]
    [InlineData("checklist")]
    [InlineData("comment")]
    [InlineData("attachment")]
    [InlineData("link")]
    [InlineData("capture")]
    [InlineData("not read")]
    [InlineData("inferred")]
    public void ResolveIcon_ReturnsExpectedIconForKnownFields(string fieldName)
    {
        var icon = ProvenanceQueryService.ResolveIcon(fieldName);
        icon.Should().NotBeNullOrWhiteSpace();
        // Every known field must be present in the icon map.
        ProvenanceQueryService.IconMap.Should().ContainKey(fieldName);
        icon.Should().Be(ProvenanceQueryService.IconMap[fieldName]);
    }

    [Fact]
    public void ResolveIcon_ReturnsFallbackForUnknownFields()
    {
        var icon = ProvenanceQueryService.ResolveIcon("completely_unknown_field");
        icon.Should().Be(ProvenanceQueryService.DefaultIcon);
    }

    [Fact]
    public void ResolveIcon_IsCaseInsensitive()
    {
        var lower = ProvenanceQueryService.ResolveIcon("title");
        var upper = ProvenanceQueryService.ResolveIcon("TITLE");
        var mixed = ProvenanceQueryService.ResolveIcon("Title");

        lower.Should().Be(upper).And.Be(mixed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveIcon_ReturnsFallbackForEmptyOrNull(string? fieldName)
    {
        var icon = ProvenanceQueryService.ResolveIcon(fieldName!);
        icon.Should().Be(ProvenanceQueryService.DefaultIcon);
    }

    // ----- Value Building -----

    [Fact]
    public void BuildValue_IncludesQuoteForExtractive()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "title",
            ProvenanceKind.Extractive,
            0.85,
            provenance.Id,
            "Fix authentication race condition");

        var value = ProvenanceQueryService.BuildValue(field);

        value.Should().Contain("Fix authentication race condition");
        value.Should().Contain("85%");
        value.Should().Contain("Extracted");
    }

    [Fact]
    public void BuildValue_TruncatesLongQuotes()
    {
        var provenance = CreateProvenance();
        var longQuote = new string('x', 200);
        var field = new ProvenanceField(
            "description",
            ProvenanceKind.Extractive,
            0.9,
            provenance.Id,
            longQuote);

        var value = ProvenanceQueryService.BuildValue(field);

        // The truncated value should end with "..." and not contain the full 200-char quote.
        value.Should().Contain("...");
        value.Length.Should().BeLessThan(longQuote.Length + 50);
    }

    [Fact]
    public void BuildValue_ShowsInferredLabel()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "priority",
            ProvenanceKind.Inferred,
            0.72,
            provenance.Id);

        var value = ProvenanceQueryService.BuildValue(field);

        value.Should().Contain("Inferred");
        value.Should().Contain("72%");
    }

    [Fact]
    public void BuildValue_RoundsConfidenceInsteadOfTruncating()
    {
        // 0.959 * 100 = 95.9 -- truncation would give "95%", rounding gives "96%"
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "title",
            ProvenanceKind.Extractive,
            0.959,
            provenance.Id,
            "Round me correctly");

        var value = ProvenanceQueryService.BuildValue(field);

        value.Should().Contain("96%");
        value.Should().NotContain("95%");
    }

    // ----- MapFieldToRow integration -----

    [Fact]
    public void MapFieldToRow_ProducesCompleteDto()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "label",
            ProvenanceKind.Extractive,
            0.88,
            provenance.Id,
            "bug");

        var row = ProvenanceQueryService.MapFieldToRow(field);

        row.Icon.Should().NotBeNullOrWhiteSpace();
        row.Key.Should().Be("label");
        row.Value.Should().Contain("bug");
        row.Weight.Should().Be("primary");
    }

    [Fact]
    public void MapFieldToRow_LowConfidenceExtractiveIsContextual()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "comment",
            ProvenanceKind.Extractive,
            0.45,
            provenance.Id,
            "maybe do this");

        var row = ProvenanceQueryService.MapFieldToRow(field);

        row.Weight.Should().Be("contextual");
    }

    // ----- Constructor validation -----

    [Fact]
    public void Constructor_ThrowsOnNullRepository()
    {
        var act = () => new ProvenanceQueryService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- Helpers -----

    private static ProposalProvenance CreateProvenance()
    {
        return new ProposalProvenance(
            proposalId: Guid.NewGuid(),
            correlationId: "test-correlation-001",
            modelId: "mock",
            totalTokens: 100);
    }
}
