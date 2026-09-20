using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class CaptureTriageNonObjectParametersTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<IProposalRevisionRepository> _revisions = new();
    private readonly Mock<IAutomationProposalService> _proposalService = new();
    private readonly Mock<IAutomationPolicyEngine> _policy = new();

    public CaptureTriageNonObjectParametersTests()
    {
        _unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _unitOfWork.SetupGet(unit => unit.ProposalRevisions).Returns(_revisions.Object);
        _revisions.Setup(repository => repository.GetLatestByProposalIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision?)null);
        _policy.Setup(service => service.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{not-valid-json")]
    public async Task ReplayWithNonObjectCreateCardParametersReturnsInvalidOperationInsteadOfThrowing(
        string parameters)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = Proposal(userId, boardId, captureId, parameters);
        _proposals.Setup(repository => repository.GetBySourceReferenceAsync(
                ProposalSourceType.Queue,
                captureId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        Result<CaptureTriageProposalResultDto>? result = null;
        Exception? thrown = null;
        try
        {
            result = await Service().CreateProposalFromCaptureAsync(
                captureId,
                userId,
                boardId,
                new CapturePayloadV1(
                    CaptureRequestContract.CurrentSchemaVersion,
                    CaptureSource.Typed,
                    "- [ ] Original title",
                    Labels: ["capture-label"]));
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.Should().BeNull("the replay path must return failures as outcomes, never throw");
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    private CaptureTriageService Service() => new(
        _unitOfWork.Object,
        _proposalService.Object,
        _policy.Object);

    private static AutomationProposal Proposal(
        Guid userId,
        Guid boardId,
        Guid captureId,
        string parameters)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId,
            captureId.ToString());
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters,
            idempotencyKey: "capture-replay-operation",
            targetId: Guid.NewGuid().ToString()));
        return proposal;
    }
}
