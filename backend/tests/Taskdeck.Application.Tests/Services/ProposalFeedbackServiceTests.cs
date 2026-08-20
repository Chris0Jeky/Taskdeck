using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProposalFeedbackServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock = new();
    private readonly Mock<IProposalFeedbackRepository> _feedbackRepoMock = new();
    private readonly ProposalFeedbackService _service;

    public ProposalFeedbackServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ProposalFeedbacks).Returns(_feedbackRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new ProposalFeedbackService(_unitOfWorkMock.Object);
    }

    private static AutomationProposal CreateProposal()
        => new(ProposalSourceType.Chat, Guid.NewGuid(), "summary", RiskLevel.Low, Guid.NewGuid().ToString());

    private void SetupProposal(AutomationProposal proposal)
        => _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(proposal);

    private void SetupExistingFeedback(Guid proposalId, Guid userId, ProposalFeedback? existing)
        => _feedbackRepoMock.Setup(r => r.GetByProposalAndUserAsync(proposalId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    [Fact]
    public async Task ReportBadSuggestion_ShouldRecordFeedback_OnFirstReport()
    {
        var proposal = CreateProposal();
        var userId = Guid.NewGuid();
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, null);

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Irrelevant);

        result.IsSuccess.Should().BeTrue();
        _feedbackRepoMock.Verify(r => r.AddAsync(It.IsAny<ProposalFeedback>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldBeNoOp_OnDuplicateSameReason()
    {
        var proposal = CreateProposal();
        var userId = Guid.NewGuid();
        var existing = new ProposalFeedback(proposal.Id, userId, ProposalFeedbackReason.Irrelevant);
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, existing);

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Irrelevant);

        result.IsSuccess.Should().BeTrue();
        _feedbackRepoMock.Verify(r => r.AddAsync(It.IsAny<ProposalFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldKeepFirstSpecificReason_WhenExistingUnspecified()
    {
        var proposal = CreateProposal();
        var userId = Guid.NewGuid();
        var existing = new ProposalFeedback(proposal.Id, userId, ProposalFeedbackReason.Unspecified);
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, existing);

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Incorrect);
        var repeatedResult = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.TooRisky);

        result.IsSuccess.Should().BeTrue();
        repeatedResult.IsSuccess.Should().BeTrue();
        existing.Reason.Should().Be(ProposalFeedbackReason.Incorrect, "the first specific reason wins after an Unspecified report");
        _feedbackRepoMock.Verify(r => r.AddAsync(It.IsAny<ProposalFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldNotDowngradeReason_WhenExistingSpecific()
    {
        var proposal = CreateProposal();
        var userId = Guid.NewGuid();
        var existing = new ProposalFeedback(proposal.Id, userId, ProposalFeedbackReason.Incorrect);
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, existing);

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Unspecified);

        result.IsSuccess.Should().BeTrue();
        existing.Reason.Should().Be(ProposalFeedbackReason.Incorrect);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldReturnNotFound_WhenProposalMissing()
    {
        var id = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((AutomationProposal?)null);

        var result = await _service.ReportBadSuggestionAsync(id, Guid.NewGuid(), ProposalFeedbackReason.Unspecified);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _feedbackRepoMock.Verify(r => r.AddAsync(It.IsAny<ProposalFeedback>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldBeAllowed_OnDecidedProposal()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid()); // orthogonal to status: feedback is allowed after a decision
        var userId = Guid.NewGuid();
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, null);

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Unspecified);

        result.IsSuccess.Should().BeTrue();
        _feedbackRepoMock.Verify(r => r.AddAsync(It.IsAny<ProposalFeedback>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportBadSuggestion_ShouldTreatRaceConflict_AsSuccess()
    {
        var proposal = CreateProposal();
        var userId = Guid.NewGuid();
        SetupProposal(proposal);
        SetupExistingFeedback(proposal.Id, userId, null);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "duplicate (proposal, user) feedback raced in"));

        var result = await _service.ReportBadSuggestionAsync(proposal.Id, userId, ProposalFeedbackReason.Unspecified);

        result.IsSuccess.Should().BeTrue("a racing duplicate is already recorded, so the Conflict is benign");
    }
}
