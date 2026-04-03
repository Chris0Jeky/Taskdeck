using System.Reflection;
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

/// <summary>
/// Edge-case tests for AutomationProposalService covering expiry service flow,
/// approve-after-expiry race, dismiss batch behavior, and double-apply via service.
/// Addresses issue #708 (TST-41).
/// </summary>
public class AutomationProposalServiceEdgeCaseTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly AutomationProposalService _service;

    public AutomationProposalServiceEdgeCaseTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _notificationServiceMock = new Mock<INotificationService>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new AutomationProposalService(_unitOfWorkMock.Object, _notificationServiceMock.Object);
    }

    #region Approve After Expiry Race

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnFailure_WhenProposalIsExpired()
    {
        // Arrange: proposal whose ExpiresAt is in the past
        var proposal = CreatePendingProposal();
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-10));

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Act
        var result = await _service.ApproveProposalAsync(proposal.Id, Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnFailure_WhenProposalAlreadyApproved()
    {
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _service.ApproveProposalAsync(proposal.Id, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Approved");
    }

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.ApproveProposalAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region ExpireProposalsAsync (Batch Expiry)

    [Fact]
    public async Task ExpireProposalsAsync_ShouldExpireAll_WhenMultipleExpired()
    {
        var expired1 = CreatePendingProposal();
        var expired2 = CreatePendingProposal();

        _proposalRepoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal> { expired1, expired2 });

        var result = await _service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        expired1.Status.Should().Be(ProposalStatus.Expired);
        expired2.Status.Should().Be(ProposalStatus.Expired);
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldReturnZero_WhenNoneExpired()
    {
        _proposalRepoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal>());

        var result = await _service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region MarkAsApplied — Double Apply Prevention

    [Fact]
    public async Task MarkAsAppliedAsync_ShouldReturnFailure_WhenAlreadyApplied()
    {
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _service.MarkAsAppliedAsync(proposal.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only approved proposals");
    }

    [Fact]
    public async Task MarkAsFailedAsync_ShouldReturnFailure_WhenAlreadyFailed()
    {
        var proposal = CreatePendingProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("First failure");

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _service.MarkAsFailedAsync(proposal.Id, "Second failure");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Only approved proposals");
    }

    #endregion

    #region DismissProposalsAsync — Edge Cases

    [Fact]
    public async Task DismissProposalsAsync_ShouldReturnZero_WhenEmptyIdsList()
    {
        var result = await _service.DismissProposalsAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task DismissProposalsAsync_ShouldSkipNonDismissable_AndCountOnlyDismissed()
    {
        // One expired (dismissable), one pending (not dismissable)
        var expired = CreatePendingProposal();
        expired.Expire();

        var pending = CreatePendingProposal();

        _proposalRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal> { expired, pending });

        var result = await _service.DismissProposalsAsync([expired.Id, pending.Id]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        expired.Status.Should().Be(ProposalStatus.Dismissed);
        pending.Status.Should().Be(ProposalStatus.PendingReview);
    }

    [Fact]
    public async Task DismissProposalsAsync_ShouldDismissAll_WhenAllDismissable()
    {
        var proposals = new[]
        {
            CreateExpiredProposal(),
            CreateRejectedProposal(),
            CreateFailedProposal()
        };

        _proposalRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposals.ToList());

        var result = await _service.DismissProposalsAsync(proposals.Select(p => p.Id).ToList());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
        proposals.Should().AllSatisfy(p => p.Status.Should().Be(ProposalStatus.Dismissed));
    }

    #endregion

    #region Reject Edge Cases

    [Fact]
    public async Task RejectProposalAsync_ShouldReturnFailure_WhenAlreadyExpired()
    {
        var proposal = CreatePendingProposal();
        proposal.Expire();

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _service.RejectProposalAsync(
            proposal.Id, Guid.NewGuid(), new UpdateProposalStatusDto("Reason"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Expired");
    }

    #endregion

    #region Helpers

    private static AutomationProposal CreatePendingProposal()
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Test proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            Guid.NewGuid());
    }

    private static AutomationProposal CreateExpiredProposal()
    {
        var p = CreatePendingProposal();
        p.Expire();
        return p;
    }

    private static AutomationProposal CreateRejectedProposal()
    {
        var p = CreatePendingProposal();
        p.Reject(Guid.NewGuid());
        return p;
    }

    private static AutomationProposal CreateFailedProposal()
    {
        var p = CreatePendingProposal();
        p.Approve(Guid.NewGuid());
        p.MarkAsFailed("Error");
        return p;
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(proposal, expiresAt);
    }

    #endregion
}
