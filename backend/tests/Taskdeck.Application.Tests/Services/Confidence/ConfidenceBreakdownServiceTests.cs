using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Confidence;

public class ConfidenceBreakdownServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepository = new();
    private readonly Mock<IProposalProvenanceRepository> _provenanceRepository = new();
    private readonly ConfidenceBreakdownService _sut;

    public ConfidenceBreakdownServiceTests()
    {
        _unitOfWork.Setup(unit => unit.AutomationProposals).Returns(_proposalRepository.Object);
        _sut = new ConfidenceBreakdownService(_unitOfWork.Object, _provenanceRepository.Object);
    }

    [Fact]
    public async Task GetBreakdownAsync_ReturnsNotFound_WhenProposalDoesNotExist()
    {
        var proposalId = Guid.NewGuid();
        _proposalRepository
            .Setup(repository => repository.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _sut.GetBreakdownAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _provenanceRepository.Verify(
            repository => repository.GetByProposalIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBreakdownAsync_ReturnsNoNumber_WhenProvenanceWasNotRecorded()
    {
        var proposal = CreateProposal();
        SetupProposal(proposal);
        _provenanceRepository
            .Setup(repository => repository.GetByProposalIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalProvenance?)null);

        var result = await _sut.GetBreakdownAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be("not-reported");
        result.Value.Overall.Should().BeNull();
        result.Value.Components.Should().BeEmpty();
        result.Value.Threshold.Should().BeNull();
        result.Value.MeetsThreshold.Should().BeNull();
    }

    [Fact]
    public async Task GetBreakdownAsync_ReturnsDeterministicWithoutNumericConfidence()
    {
        var proposal = CreateProposal();
        var provenance = CreateProvenance(proposal);
        provenance.AddField(new ProvenanceField(
            "Operation 1: create card",
            ProvenanceKind.Inferred,
            confidence: null,
            provenance.Id,
            ProvenanceConfidenceSource.Deterministic));
        SetupProposal(proposal, provenance);

        var result = await _sut.GetBreakdownAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be("deterministic");
        result.Value.Overall.Should().BeNull();
        result.Value.Components.Should().BeEmpty();
        result.Value.Note.Should().Contain("no model confidence");
    }

    [Fact]
    public async Task GetBreakdownAsync_RoundTripsExactModelReportedPerItemConfidence()
    {
        var proposal = CreateProposal();
        var provenance = CreateProvenance(proposal);
        provenance.AddField(new ProvenanceField(
            "Operation 2: create card",
            ProvenanceKind.Inferred,
            0.63,
            provenance.Id,
            ProvenanceConfidenceSource.ModelReported));
        provenance.AddField(new ProvenanceField(
            "Operation 1: create card",
            ProvenanceKind.Inferred,
            0.81,
            provenance.Id,
            ProvenanceConfidenceSource.ModelReported));
        SetupProposal(proposal, provenance);

        var result = await _sut.GetBreakdownAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be("model-reported");
        result.Value.Components.Should().Equal(
            new ConfidenceComponentDto("Operation 1: create card", 0.81),
            new ConfidenceComponentDto("Operation 2: create card", 0.63));
        result.Value.Overall.Should().BeApproximately(0.72, 0.0000001);
        result.Value.Threshold.Should().BeNull();
        result.Value.MeetsThreshold.Should().BeNull();
        result.Value.Note.Should().Contain("approval").And.Contain("Apply");
    }

    [Fact]
    public async Task GetBreakdownAsync_LabelsDerivedValuesAsNotModelReported()
    {
        var proposal = CreateProposal();
        var provenance = CreateProvenance(proposal);
        provenance.AddField(new ProvenanceField(
            "Title match",
            ProvenanceKind.Extractive,
            0.92,
            provenance.Id,
            ProvenanceConfidenceSource.Derived,
            "Ship the fix"));
        SetupProposal(proposal, provenance);

        var result = await _sut.GetBreakdownAsync(proposal.Id);

        result.Value.Source.Should().Be("derived");
        result.Value.Components.Should().ContainSingle().Which.Value.Should().Be(0.92);
        result.Value.Note.Should().Contain("not reported by a model");
    }

    private static AutomationProposal CreateProposal() => new(
        ProposalSourceType.Queue,
        Guid.NewGuid(),
        "Test proposal",
        RiskLevel.Low,
        Guid.NewGuid().ToString(),
        Guid.NewGuid());

    private static ProposalProvenance CreateProvenance(AutomationProposal proposal) => new(
        proposal.Id,
        proposal.CorrelationId,
        "gpt-test");

    private void SetupProposal(AutomationProposal proposal, ProposalProvenance? provenance = null)
    {
        _proposalRepository
            .Setup(repository => repository.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _provenanceRepository
            .Setup(repository => repository.GetByProposalIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provenance);
    }
}
