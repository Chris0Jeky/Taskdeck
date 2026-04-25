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

        _service = new ProposalRevisionService(_unitOfWork.Object);
    }

    [Fact]
    public async Task CreateRevisionAsync_ReturnsConflict_WhenSaveChangesDetectsRevisionNumberRace()
    {
        var proposal = CreatePendingProposal();
        var dto = new CreateProposalRevisionDto(
            proposal.Id,
            Guid.NewGuid(),
            """{"operations":[]}""",
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
}
