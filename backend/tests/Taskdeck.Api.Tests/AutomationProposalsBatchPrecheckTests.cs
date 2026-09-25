using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Controllers;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Confidence;
using Taskdeck.Domain.Common;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Pins the batched pre-check contract: batch approve/dismiss resolve every selection with ONE
/// header query and never fall back to per-id reads. HTTP error equivalence with the old per-row
/// loop is covered end to end in AutomationProposalsApiTests.
/// </summary>
public class AutomationProposalsBatchPrecheckTests
{
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationMock = new();
    private readonly Mock<IUserContext> _userContextMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly AutomationProposalsController _controller;

    public AutomationProposalsBatchPrecheckTests()
    {
        _userContextMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        _userContextMock.SetupGet(u => u.UserId).Returns(() => _userId.ToString());
        _controller = new AutomationProposalsController(
            _proposalServiceMock.Object,
            Mock.Of<IAutomationExecutorService>(),
            Mock.Of<ISimilarDecisionService>(),
            _authorizationMock.Object,
            Mock.Of<IProposalConflictDetector>(),
            Mock.Of<IProvenanceQueryService>(),
            Mock.Of<IConfidenceBreakdownService>(),
            Mock.Of<ICardHistoryService>(),
            Mock.Of<ISideEffectAnalyzer>(),
            Mock.Of<IProposalRevisionService>(),
            Mock.Of<IProposalFeedbackService>(),
            Mock.Of<IBatchProposalExecutionService>(),
            _userContextMock.Object);
    }

    [Fact]
    public async Task ApproveProposals_ShouldResolveAllSelectionsWithSingleHeaderQuery()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var headers = new Dictionary<Guid, ProposalHeaderDto>
        {
            [first] = new ProposalHeaderDto(first, _userId, null),
            [second] = new ProposalHeaderDto(second, _userId, null),
        };
        _proposalServiceMock
            .Setup(s => s.GetProposalHeadersByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(headers);
        _authorizationMock
            .Setup(a => a.GetWritableBoardIdsAsync(_userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        _proposalServiceMock
            .Setup(s => s.ApproveProposalsAsync(
                It.IsAny<IReadOnlyList<BatchApproveProposalSelectionDto>>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BatchApproveProposalsResultDto([first, second])));

        var result = await _controller.ApproveProposals(new ApproveProposalsRequest
        {
            Proposals =
            [
                new ApproveProposalSelectionRequest { Id = first, ExpectedProposalUpdatedAt = DateTimeOffset.UtcNow },
                new ApproveProposalSelectionRequest { Id = second, ExpectedProposalUpdatedAt = DateTimeOffset.UtcNow },
            ]
        });

        result.Should().BeOfType<OkObjectResult>();
        _proposalServiceMock.Verify(
            s => s.GetProposalHeadersByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _proposalServiceMock.Verify(
            s => s.GetProposalByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DismissProposals_ShouldResolveAllIdsWithSingleHeaderQuery()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var headers = new Dictionary<Guid, ProposalHeaderDto>
        {
            [first] = new ProposalHeaderDto(first, _userId, null),
            [second] = new ProposalHeaderDto(second, _userId, null),
        };
        _proposalServiceMock
            .Setup(s => s.GetProposalHeadersByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(headers);
        _proposalServiceMock
            .Setup(s => s.DismissProposalsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2));

        var result = await _controller.DismissProposals(new DismissProposalsRequest { Ids = [first, second] });

        result.Should().BeOfType<OkObjectResult>();
        _proposalServiceMock.Verify(
            s => s.GetProposalHeadersByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _proposalServiceMock.Verify(
            s => s.GetProposalByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
