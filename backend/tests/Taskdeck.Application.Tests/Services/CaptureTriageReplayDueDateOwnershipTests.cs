using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class CaptureTriageReplayDueDateOwnershipTests
{
    [Fact]
    public async Task ReplayPatchesOnlyCaptureOwnedDueDateAndPreservesReviewerTitleAndLabels()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var captureId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Capture triage",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId,
            captureId.ToString());
        var operation = new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters: Parameters(
                "Original title",
                new DateOnly(2026, 8, 28),
                ["capture-label"]),
            idempotencyKey: "capture-due-date-replay",
            targetId: Guid.NewGuid().ToString("D"));
        proposal.AddOperation(operation);

        var reviewerRevision = new ProposalRevision(
            proposal.Id,
            1,
            userId,
            RevisionPayload(
                operation,
                Parameters(
                    "Reviewer title",
                    new DateOnly(2026, 8, 30),
                    ["reviewer-label"])),
            "Reviewer changed title, due date and labels");

        var unitOfWork = new Mock<IUnitOfWork>();
        var proposals = new Mock<IAutomationProposalRepository>();
        var revisions = new Mock<IProposalRevisionRepository>();
        var proposalService = new Mock<IAutomationProposalService>();
        var policy = new Mock<IAutomationPolicyEngine>();
        var revisionService = new Mock<IProposalRevisionService>();
        unitOfWork.SetupGet(unit => unit.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(unit => unit.ProposalRevisions).Returns(revisions.Object);
        proposals.Setup(repository => repository.GetBySourceReferenceAsync(
                ProposalSourceType.Queue,
                captureId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        revisions.Setup(repository => repository.GetLatestByProposalIdAsync(
                proposal.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewerRevision);
        policy.Setup(service => service.ValidatePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<ProposalOperationDto>>(),
                BoardAccessBar.Write,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        CreateProposalRevisionDto? savedRevision = null;
        revisionService
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

        var service = new CaptureTriageService(
            unitOfWork.Object,
            proposalService.Object,
            policy.Object,
            proposalRevisionService: revisionService.Object);

        var result = await service.CreateProposalFromCaptureAsync(
            captureId,
            userId,
            boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "- [ ] Original title",
                DueDate: new DateOnly(2026, 9, 1),
                Labels: ["capture-label"]));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        savedRevision.Should().NotBeNull();
        using var revisionDocument = JsonDocument.Parse(savedRevision!.RevisedPayload);
        using var parametersDocument = JsonDocument.Parse(
            revisionDocument.RootElement.GetProperty("operations")[0]
                .GetProperty("parameters").GetString()!);

        parametersDocument.RootElement.GetProperty("title").GetString()
            .Should().Be("Reviewer title");
        parametersDocument.RootElement.GetProperty("dueDate").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        parametersDocument.RootElement.GetProperty("labels").EnumerateArray()
            .Select(label => label.GetString())
            .Should().Equal("reviewer-label");
    }

    private static string Parameters(
        string title,
        DateOnly? dueDate,
        IReadOnlyList<string> labels)
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

    private static string RevisionPayload(
        AutomationProposalOperation operation,
        string parameters) =>
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
