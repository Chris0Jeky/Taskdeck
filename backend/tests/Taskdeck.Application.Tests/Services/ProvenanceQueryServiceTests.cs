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
    /// <summary>The authenticated caller the controller resolves from claims.</summary>
    private static readonly Guid CallerUserId = Guid.Parse("7a1d0a1b-4c9e-4d47-a2c3-1f5b6c8d9e01");

    private static readonly IReadOnlySet<Guid> NoOwnedTranscripts = new HashSet<Guid>();

    private readonly Mock<IProposalProvenanceRepository> _provenanceRepo = new();
    private readonly Mock<ITranscriptRepository> _transcriptRepo = new();
    private readonly ProvenanceQueryService _service;

    public ProvenanceQueryServiceTests()
    {
        // Default: the caller owns none of the referenced transcripts.
        _transcriptRepo
            .Setup(r => r.FilterOwnedIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        _service = new ProvenanceQueryService(_provenanceRepo.Object, _transcriptRepo.Object);
    }

    // ----- GetProvenanceRowsAsync -----

    [Fact]
    public async Task GetProvenanceRowsAsync_ReturnsEmptyList_WhenNoProvenanceExists()
    {
        var proposalId = Guid.NewGuid();
        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalProvenance?)null);

        var result = await _service.GetProvenanceRowsAsync(proposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_ReturnsValidationError_WhenProposalIdIsEmpty()
    {
        var result = await _service.GetProvenanceRowsAsync(Guid.Empty, CallerUserId);

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

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var row = result.Value[0];
        row.Key.Should().Be("title");
        row.Weight.Should().Be("primary");
        row.Value.Should().Contain("Fix the login bug");
        row.Value.Should().Contain("95%");
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MapsModelReportedInferredFieldCorrectly()
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

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        var row = result.Value[0];
        row.Key.Should().Be("due date");
        row.Weight.Should().Be("inferred");
        row.Value.Should().Contain("Model reported");
        row.Value.Should().Contain("65%");
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MapsOpaqueEvidenceMetadataWithoutQuoteText()
    {
        var provenance = CreateProvenance();
        var transcriptId = Guid.NewGuid();
        var field = new ProvenanceField("Operation 1: create card", ProvenanceKind.Inferred, 0.75, provenance.Id);
        field.AddEvidenceLink(new ProvenanceEvidenceLink(
            ProvenanceEvidenceLink.TranscriptSourceType,
            transcriptId.ToString("D"),
            field.Id,
            "Transcript evidence",
            12,
            23,
            transcriptId));
        provenance.AddField(field);
        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        var link = result.Value.Single().EvidenceLinks.Should().ContainSingle().Subject;
        link.SourceType.Should().Be("Transcript");
        link.SourceId.Should().Be(transcriptId.ToString("D"));
        link.Label.Should().Be("Transcript evidence");
        link.SpanStart.Should().Be(12);
        link.SpanEnd.Should().Be(23);
        result.Value.Single().Value.Should().NotContain(transcriptId.ToString("D"));
    }

    // ----- Viewable flag (issue #1837 item 1) -----

    [Fact]
    public async Task GetProvenanceRowsAsync_MarksTranscriptEvidenceViewable_WhenCallerOwnsTheTranscript()
    {
        var transcriptId = Guid.NewGuid();
        var provenance = CreateProvenanceWithTranscriptEvidence(transcriptId);
        _transcriptRepo
            .Setup(r => r.FilterOwnedIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(transcriptId)),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transcriptId });

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().EvidenceLinks!.Single().Viewable.Should().BeTrue();
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_MarksTranscriptEvidenceNotViewable_WhenCallerDoesNotOwnTheTranscript()
    {
        var transcriptId = Guid.NewGuid();
        var provenance = CreateProvenanceWithTranscriptEvidence(transcriptId);
        // A board collaborator: authorized for the proposal, not for the owner's transcript.
        _transcriptRepo
            .Setup(r => r.FilterOwnedIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        var link = result.Value.Single().EvidenceLinks!.Single();
        link.Viewable.Should().BeFalse();
        // The link itself is still returned: the collaborator keeps the evidence metadata.
        link.SourceId.Should().Be(transcriptId.ToString("D"));
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_ComputesViewabilityFromTheCallerClaim_NotFromThePayload()
    {
        var transcriptId = Guid.NewGuid();
        var provenance = CreateProvenanceWithTranscriptEvidence(transcriptId);
        var otherUserId = Guid.NewGuid();
        // Ownership resolved for the OTHER user must not colour this caller's answer.
        _transcriptRepo
            .Setup(r => r.FilterOwnedIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                otherUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transcriptId });

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().EvidenceLinks!.Single().Viewable.Should().BeFalse();
        _transcriptRepo.Verify(
            r => r.FilterOwnedIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                CallerUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_LeavesNonTranscriptEvidenceNotViewable_WithoutQueryingTranscripts()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField("title", ProvenanceKind.Extractive, 0.9, provenance.Id, "Ship it");
        field.AddEvidenceLink(new ProvenanceEvidenceLink(
            "Capture",
            "capture-42",
            field.Id,
            "Capture evidence",
            0,
            9));
        provenance.AddField(field);
        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().EvidenceLinks!.Single().Viewable.Should().BeFalse();
        _transcriptRepo.Verify(
            r => r.FilterOwnedIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProvenanceRowsAsync_ReturnsValidationError_WhenCallerUserIdIsEmpty()
    {
        var result = await _service.GetProvenanceRowsAsync(Guid.NewGuid(), Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _provenanceRepo.Verify(
            r => r.GetByProposalIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        var result = await _service.GetProvenanceRowsAsync(provenance.ProposalId, CallerUserId);

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
    public void BuildValue_LabelsModelReportedConfidence()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "priority",
            ProvenanceKind.Inferred,
            0.72,
            provenance.Id);

        var value = ProvenanceQueryService.BuildValue(field);

        value.Should().Contain("Model reported");
        value.Should().Contain("72%");
    }

    [Fact]
    public void BuildValue_DeterministicFieldContainsNoNumericConfidence()
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField(
            "title",
            ProvenanceKind.Inferred,
            confidence: null,
            provenance.Id,
            ProvenanceConfidenceSource.Deterministic);

        var row = ProvenanceQueryService.MapFieldToRow(field, NoOwnedTranscripts);

        row.Value.Should().Be("Deterministic extraction (no model confidence)");
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

        var row = ProvenanceQueryService.MapFieldToRow(field, NoOwnedTranscripts);

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

        var row = ProvenanceQueryService.MapFieldToRow(field, NoOwnedTranscripts);

        row.Weight.Should().Be("contextual");
    }

    // ----- Constructor validation -----

    [Fact]
    public void Constructor_ThrowsOnNullRepository()
    {
        var act = () => new ProvenanceQueryService(null!, _transcriptRepo.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullTranscriptRepository()
    {
        var act = () => new ProvenanceQueryService(_provenanceRepo.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- Helpers -----

    private ProposalProvenance CreateProvenanceWithTranscriptEvidence(Guid transcriptId)
    {
        var provenance = CreateProvenance();
        var field = new ProvenanceField("title", ProvenanceKind.Extractive, 0.9, provenance.Id, "Ship the export fix");
        field.AddEvidenceLink(new ProvenanceEvidenceLink(
            ProvenanceEvidenceLink.TranscriptSourceType,
            transcriptId.ToString("D"),
            field.Id,
            "Transcript evidence",
            8,
            23,
            transcriptId));
        provenance.AddField(field);
        _provenanceRepo
            .Setup(r => r.GetByProposalIdAsync(provenance.ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);
        return provenance;
    }

    private static ProposalProvenance CreateProvenance()
    {
        return new ProposalProvenance(
            proposalId: Guid.NewGuid(),
            correlationId: "test-correlation-001",
            modelId: "mock",
            totalTokens: 100);
    }
}
