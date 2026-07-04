using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProposalRevisionServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposals = new();
    private readonly Mock<IProposalRevisionRepository> _revisions = new();
    private readonly ProposalRevisionService _service;

    public ProposalRevisionServiceTests()
    {
        _unitOfWork.SetupGet(unitOfWork => unitOfWork.AutomationProposals).Returns(_proposals.Object);
        _unitOfWork.SetupGet(unitOfWork => unitOfWork.ProposalRevisions).Returns(_revisions.Object);

        // Use the real policy engine so the structure invariants (#1281) are exercised end-to-end
        // through the save path; ValidateOperationStructure is pure and never touches the unit of work.
        _service = new ProposalRevisionService(_unitOfWork.Object, new AutomationPolicyEngine(_unitOfWork.Object));
    }

    [Fact]
    public async Task CreateRevisionAsync_ReturnsValidationError_WhenSequencesAreDuplicated()
    {
        var proposal = CreatePendingProposal();
        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload((sequence: 0, parameters: "{}"), (sequence: 0, parameters: "{}")),
            "Duplicate sequences");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("sequences must be unique");
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRevisionAsync_ReturnsValidationError_WhenOperationCountExceedsMaximum()
    {
        var proposal = CreatePendingProposal();
        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var operations = Enumerable.Range(0, 51)
            .Select(i => (sequence: i, parameters: "{}"))
            .ToArray();
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload(operations),
            "Too many operations");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum operation count");
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRevisionAsync_ReturnsValidationError_WhenParametersExceedMaximumLength()
    {
        var proposal = CreatePendingProposal();
        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var oversizedParameters = "{\"blob\":\"" + new string('a', 10_001) + "\"}";
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload((sequence: 0, parameters: oversizedParameters)),
            "Oversized parameters");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum length");
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRevisionAsync_Succeeds_WhenOperationsAreStructurallyValid()
    {
        var proposal = CreatePendingProposal();
        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _revisions
            .Setup(repo => repo.GetNextRevisionNumberAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _revisions
            .Setup(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision revision, CancellationToken _) => revision);
        _unitOfWork
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload((sequence: 0, parameters: "{}"), (sequence: 1, parameters: "{}")),
            "Valid multi-op revision");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeTrue();
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRevisionAsync_Succeeds_AtExactlyMaxOperationCount()
    {
        // Boundary: exactly 50 operations is allowed (only >50 is rejected). Pins the allowed
        // side of the count guard so a `>` -> `>=` mutation is caught.
        var proposal = CreatePendingProposal();
        SetupSuccessfulSave(proposal);

        var operations = Enumerable.Range(0, 50)
            .Select(i => (sequence: i, parameters: "{}"))
            .ToArray();
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload(operations),
            "Exactly max operations");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeTrue();
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRevisionAsync_Succeeds_AtExactlyMaxParametersLength()
    {
        // Boundary: a parameters string of exactly 10000 chars is allowed (only >10000 is rejected).
        var proposal = CreatePendingProposal();
        SetupSuccessfulSave(proposal);

        // {"blob":"<a...>"} — the 11-char JSON wrapper + 9989 'a' = exactly 10000 chars.
        var exactlyMaxParameters = "{\"blob\":\"" + new string('a', 9_989) + "\"}";
        exactlyMaxParameters.Length.Should().Be(10_000);
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildPayload((sequence: 0, parameters: exactlyMaxParameters)),
            "Exactly max parameters length");

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeTrue();
        _revisions.Verify(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupSuccessfulSave(AutomationProposal proposal)
    {
        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _revisions
            .Setup(repo => repo.GetNextRevisionNumberAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _revisions
            .Setup(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision revision, CancellationToken _) => revision);
        _unitOfWork
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task CreateRevisionAsync_ReturnsConflict_WhenSaveChangesDetectsRevisionNumberRace()
    {
        var proposal = CreatePendingProposal();
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            BuildRevisionPayload(proposal.BoardId!.Value, Guid.NewGuid()),
            "Edited before approval");

        _proposals
            .Setup(repo => repo.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _revisions
            .Setup(repo => repo.GetNextRevisionNumberAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _revisions
            .Setup(repo => repo.AddAsync(It.IsAny<ProposalRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision revision, CancellationToken _) => revision);
        _unitOfWork
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(
                ErrorCodes.Conflict,
                "Proposal revision was created by another session. Refresh and retry your edit."));

        var result = await _service.CreateRevisionAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("another session");
    }

    private static AutomationProposal CreatePendingProposal()
    {
        return new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Draft proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid());
    }

    private static string BuildRevisionPayload(Guid boardId, Guid columnId)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 1,
                    actionType = "create",
                    targetType = "card",
                    parameters = JsonSerializer.Serialize(new
                    {
                        title = "Edited Card",
                        boardId,
                        columnId
                    }),
                    idempotencyKey = Guid.NewGuid().ToString()
                }
            }
        });
    }

    private static string BuildPayload(params (int sequence, string parameters)[] operations)
    {
        return JsonSerializer.Serialize(new
        {
            operations = operations.Select(op => new
            {
                sequence = op.sequence,
                actionType = "create",
                targetType = "card",
                parameters = op.parameters,
                idempotencyKey = Guid.NewGuid().ToString()
            }).ToArray()
        });
    }
}
