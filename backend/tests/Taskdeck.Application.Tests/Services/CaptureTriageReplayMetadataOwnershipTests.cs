using System.Text.Json;
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

public sealed class CaptureTriageReplayMetadataOwnershipTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<IProposalRevisionRepository> _revisions = new();
    private readonly Mock<IAutomationProposalService> _proposalService = new();
    private readonly Mock<IAutomationPolicyEngine> _policy = new();
    private readonly Mock<IProposalRevisionService> _revisionService = new();

    public CaptureTriageReplayMetadataOwnershipTests()
    {
        _unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(_proposals.Object);
        _unitOfWork.SetupGet(unit => unit.ProposalRevisions).Returns(_revisions.Object);
        _policy.Setup(service => service.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    [Fact]
    public async Task ReplayWithUnchangedCaptureMetadataDoesNotOverwriteLatestReviewerRevision()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = Proposal(userId, boardId, captureId,
            Parameters("Original title", new DateOnly(2026, 8, 28), ["capture-label"]));
        var operation = proposal.Operations.Single();
        var reviewerRevision = new ProposalRevision(
            proposal.Id,
            1,
            userId,
            RevisionPayload(operation,
                Parameters("Reviewer title", new DateOnly(2026, 8, 30), ["reviewer-label"])),
            "Reviewer changed the proposal metadata");
        SetupExisting(proposal, captureId, latestRevision: reviewerRevision);

        var result = await Service().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Original title",
                DueDate: new DateOnly(2026, 8, 28),
                Labels: ["capture-label"]));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ProposalId.Should().Be(proposal.Id);
        result.Value.OperationCount.Should().Be(1);
        _revisionService.Verify(service => service.CreateRevisionWithPendingCommitGuardAsync(
            It.IsAny<CreateProposalRevisionDto>(),
            It.IsAny<CancellationToken>()), Times.Never,
            "an unchanged capture replay does not own metadata the reviewer changed later");
        _policy.Verify(service => service.ValidatePermissionsAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<IEnumerable<ProposalOperationDto>>(),
            It.IsAny<BoardAccessBar>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApprovedReplayComparesAgainstThePinnedRevisionApplyWillExecute()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = Proposal(userId, boardId, captureId,
            Parameters("Original title", null, ["old-label"]));
        var operation = proposal.Operations.Single();
        var pinnedRevision = new ProposalRevision(
            proposal.Id,
            1,
            userId,
            RevisionPayload(operation, Parameters("Approved title", null, ["corrected-label"])),
            "Approved correction");
        proposal.Approve(userId, pinnedRevision.Id);
        SetupExisting(proposal, captureId, pinnedRevision: pinnedRevision);

        var result = await Service().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Approved title",
                Labels: ["corrected-label"]));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ProposalId.Should().Be(proposal.Id);
        result.Value.OperationCount.Should().Be(1);
        _revisions.Verify(repository => repository.GetByIdAsync(
            pinnedRevision.Id,
            It.IsAny<CancellationToken>()), Times.Once);
        _revisionService.Verify(service => service.CreateRevisionWithPendingCommitGuardAsync(
            It.IsAny<CreateProposalRevisionDto>(),
            It.IsAny<CancellationToken>()), Times.Never,
            "a decided proposal is comparison-only and its pinned revision already contains the correction");
    }

    [Fact]
    public async Task ReplayPatchesOnlyCaptureOwnedLabelsAndPreservesReviewerDueDateAndTitle()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = Proposal(userId, boardId, captureId,
            Parameters("Original title", new DateOnly(2026, 8, 28), ["old-label"]));
        var operation = proposal.Operations.Single();
        var reviewerRevision = new ProposalRevision(
            proposal.Id,
            1,
            userId,
            RevisionPayload(operation,
                Parameters("Reviewer title", new DateOnly(2026, 8, 30), ["reviewer-label"])),
            "Reviewer changed title, due date and labels");
        SetupExisting(proposal, captureId, latestRevision: reviewerRevision);

        CreateProposalRevisionDto? savedRevision = null;
        _revisionService
            .Setup(service => service.CreateRevisionWithPendingCommitGuardAsync(
                It.IsAny<CreateProposalRevisionDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateProposalRevisionDto, CancellationToken>((dto, _) => savedRevision = dto)
            .ReturnsAsync(Result.Success(new ProposalRevisionDto(
                Guid.NewGuid(),
                proposal.Id,
                2,
                userId,
                reviewerRevision.RevisedPayload,
                DateTimeOffset.UtcNow,
                "capture replay",
                DateTimeOffset.UtcNow)));

        var result = await Service().CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Original title",
                DueDate: new DateOnly(2026, 8, 28),
                Labels: ["corrected-label"]));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        savedRevision.Should().NotBeNull();
        using var revisionDocument = JsonDocument.Parse(savedRevision!.RevisedPayload);
        using var parametersDocument = JsonDocument.Parse(
            revisionDocument.RootElement.GetProperty("operations")[0]
                .GetProperty("parameters").GetString()!);
        parametersDocument.RootElement.GetProperty("title").GetString().Should().Be("Reviewer title");
        parametersDocument.RootElement.GetProperty("dueDate").GetDateTimeOffset().Should().Be(
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero));
        parametersDocument.RootElement.GetProperty("labels").EnumerateArray()
            .Select(label => label.GetString())
            .Should().Equal("corrected-label");
    }

    private CaptureTriageService Service() => new(
        _unitOfWork.Object,
        _proposalService.Object,
        _policy.Object,
        proposalRevisionService: _revisionService.Object);

    private void SetupExisting(
        AutomationProposal proposal,
        Guid captureId,
        ProposalRevision? latestRevision = null,
        ProposalRevision? pinnedRevision = null)
    {
        _proposals.Setup(repository => repository.GetBySourceReferenceAsync(
                ProposalSourceType.Queue,
                captureId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _revisions.Setup(repository => repository.GetLatestByProposalIdAsync(
                proposal.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestRevision);
        if (pinnedRevision is not null)
        {
            _revisions.Setup(repository => repository.GetByIdAsync(
                    pinnedRevision.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(pinnedRevision);
        }
    }

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

    private static string Parameters(string title, DateOnly? dueDate, IReadOnlyList<string> labels)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "evidence",
            ["boardId"] = Guid.NewGuid(),
            ["columnId"] = Guid.NewGuid(),
            ["labels"] = labels
        };
        if (dueDate.HasValue)
        {
            parameters["dueDate"] = new DateTimeOffset(
                dueDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }
        return JsonSerializer.Serialize(parameters);
    }

    private static string RevisionPayload(AutomationProposalOperation operation, string parameters) =>
        JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    id = operation.Id,
                    sequence = operation.Sequence,
                    actionType = operation.ActionType,
                    targetType = operation.TargetType,
                    targetId = operation.TargetId,
                    parameters,
                    idempotencyKey = operation.IdempotencyKey,
                    expectedVersion = operation.ExpectedVersion
                }
            }
        });
}
