using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Tests.Support;
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
    private readonly Mock<IProposalRevisionRepository> _revisionRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly AutomationProposalService _service;

    public AutomationProposalServiceEdgeCaseTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _revisionRepoMock = new Mock<IProposalRevisionRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _notificationServiceMock = new Mock<INotificationService>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ProposalRevisions).Returns(_revisionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        // Default: no saved revision, so the approve-time gates (#1416) validate the proposal's
        // original operations rather than an effective revision.
        _revisionRepoMock
            .Setup(r => r.GetLatestByProposalIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalRevision?)null);
        // Approve now also runs Apply's permission/contract gate (#1416): requester exists,
        // board exists, requester has board access. Default them to PASS so these edge-case
        // tests reach their intended seam (expiry guard, SaveChanges concurrency collision).
        _userRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("edgetester", "edge@example.com", "hashedPassword"));
        _boardRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestDataBuilder.CreateBoard() });
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notificationServiceMock
            .Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new AutomationProposalService(_unitOfWorkMock.Object, _notificationServiceMock.Object);
    }

    #region Approve After Expiry Race

    [Fact]
    public async Task ApproveProposalAsync_ShouldReturnFailure_WhenProposalIsExpired()
    {
        // Arrange: proposal whose ExpiresAt is in the past. It carries an operation so it clears
        // the approve-time structure gate (#1416) and the domain expiry guard is what rejects it.
        var proposal = CreatePendingProposal();
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{\"title\":\"Test\"}", Guid.NewGuid().ToString()));
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
    public async Task ApproveProposalAsync_ShouldReturnConflict_WhenSaveChangesDetectsConcurrency()
    {
        var proposal = CreatePendingProposal();
        // Carries an in-scope board-update operation so approve clears the structure AND
        // permission/contract gates (#1416) and reaches SaveChanges, where the simulated
        // concurrency collision surfaces.
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "board",
            $"{{\"boardId\":\"{proposal.BoardId}\",\"name\":\"Renamed\"}}",
            Guid.NewGuid().ToString(),
            targetId: proposal.BoardId!.Value.ToString()));
        var deciderId = Guid.NewGuid();

        _proposalRepoMock
            .Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "Record was updated by another session."));

        var result = await _service.ApproveProposalAsync(proposal.Id, deciderId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _notificationServiceMock.Verify(
            s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
            .ReturnsAsync(new ExpiredProposalSweep(new List<AutomationProposal> { expired1, expired2 }, 0));

        var result = await _service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        expired1.Status.Should().Be(ProposalStatus.Expired);
        expired2.Status.Should().Be(ProposalStatus.Expired);
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldExpireAndNotifyOnlyTheExpirableHalf_WhenArchivedBoardProposalsAreWithheld()
    {
        // #2197: this path used to call Expire() on everything the expiry query returned, with no
        // archived-board guard — so a pending proposal on an archived board was silently decided.
        // The query now withholds those and reports a count. What this test pins is the SERVICE's
        // half of that contract: the withheld ones must not be counted, must not be saved, and must
        // not pick up an "expired" notification, and the operator must be told how many were held
        // back. Both loops in the method read the same list, which is what makes that hold.
        var expirable = CreatePendingProposal();
        var logger = new InMemoryLogger<AutomationProposalService>();
        var service = new AutomationProposalService(
            _unitOfWorkMock.Object,
            _notificationServiceMock.Object,
            provenanceRepository: null,
            policyEngine: null,
            logger: logger);

        _proposalRepoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiredProposalSweep(
                new List<AutomationProposal> { expirable },
                SkippedArchivedBoardCount: 3));

        var result = await service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(
            1,
            "the returned count is how many proposals were actually expired, not how many were considered");
        expirable.Status.Should().Be(ProposalStatus.Expired);

        // Exactly one notification: a withheld proposal that got an "expired" notification without
        // being expired would be a user-visible lie about archived history.
        _notificationServiceMock.Verify(
            s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.AtMostOnce());

        var skipEntry = logger.Entries.Should()
            .ContainSingle(entry => entry.Message.Contains("Skipped expiring"))
            .Subject;
        skipEntry.Level.Should().Be(LogLevel.Information);
        skipEntry.Message.Should().Contain("3");
        skipEntry.Message.Should().Contain("board is archived");
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldNotSaveOrLog_WhenEveryExpiredProposalIsWithheld()
    {
        // The all-withheld cycle is the exact shape of the defect: a sweep that finds only
        // archived-board proposals must be a no-op write, not a silent batch of decisions.
        var logger = new InMemoryLogger<AutomationProposalService>();
        var service = new AutomationProposalService(
            _unitOfWorkMock.Object,
            _notificationServiceMock.Object,
            provenanceRepository: null,
            policyEngine: null,
            logger: logger);

        _proposalRepoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiredProposalSweep(Array.Empty<AutomationProposal>(), SkippedArchivedBoardCount: 2));

        var result = await service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _unitOfWorkMock.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never(),
            "nothing was expired, so nothing may be written — no audit row for a withheld proposal");
        _notificationServiceMock.Verify(
            s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never());
        logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("Skipped expiring"));
    }

    [Fact]
    public async Task ExpireProposalsAsync_ShouldReturnZero_WhenNoneExpired()
    {
        _proposalRepoMock
            .Setup(r => r.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExpiredProposalSweep.Empty);

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
