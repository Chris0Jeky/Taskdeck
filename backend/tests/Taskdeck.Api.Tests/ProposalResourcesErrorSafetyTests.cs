using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ProposalResourcesErrorSafetyTests
{
    private const string HostileError =
        "Bearer tdsk_test_secret C:\\Users\\alice\\taskdeck.db " +
        "SQLite UNIQUE constraint failed: Users.Email https://provider.example/v1/internal";

    private static readonly string[] HostileMarkers =
    [
        "tdsk_test_secret",
        "C:\\Users\\alice\\taskdeck.db",
        "UNIQUE constraint failed",
        "https://provider.example/v1/internal"
    ];

    [Fact]
    public async Task ListProposals_UnexpectedFailure_ThrowsGenericErrorForCurrentUserFilter()
    {
        var userId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalsAsync(
                It.Is<ProposalFilterDto?>(filter => filter != null && filter.UserId == userId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IEnumerable<ProposalDto>>(
                ErrorCodes.UnexpectedError,
                HostileError));

        var act = () => CreateResources(proposalService.Object, userId).ListProposals();

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        AssertGenericError(exception, "MCP: failed to list proposals: ");
        proposalService.VerifyAll();
    }

    [Fact]
    public async Task GetProposalDetail_UnexpectedLookupFailure_ThrowsGenericError()
    {
        var userId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, HostileError));

        var act = () => CreateResources(proposalService.Object, userId)
            .GetProposalDetail(proposalId.ToString());

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        AssertGenericError(exception, "MCP: failed to get proposal: ");
        proposalService.VerifyAll();
    }

    [Theory]
    [InlineData(ProposalStatus.PendingReview)]
    [InlineData(ProposalStatus.Applied)]
    public async Task GetProposalDetail_UnexpectedPreviewFailure_ThrowsGenericError(
        ProposalStatus status)
    {
        var userId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateProposal(proposalId, userId, status)));

        if (status == ProposalStatus.Applied)
        {
            proposalService
                .Setup(service => service.GetTerminalProposalStoredPreviewAsync(
                    proposalId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<string>(ErrorCodes.UnexpectedError, HostileError));
        }
        else
        {
            proposalService
                .Setup(service => service.GetProposalDiffAsync(
                    proposalId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<string>(ErrorCodes.UnexpectedError, HostileError));
        }

        var act = () => CreateResources(proposalService.Object, userId)
            .GetProposalDetail(proposalId.ToString());

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        AssertGenericError(exception, "MCP: ");
        proposalService.VerifyAll();
    }

    [Fact]
    public async Task GetProposalDetail_KnownDomainFailure_PreservesStableMessage()
    {
        const string stableMessage = "Proposal not found.";
        var userId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.NotFound, stableMessage));

        var act = () => CreateResources(proposalService.Object, userId)
            .GetProposalDetail(proposalId.ToString());

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be($"MCP: failed to get proposal: {stableMessage}");
        proposalService.VerifyAll();
    }

    private static ProposalResources CreateResources(
        IAutomationProposalService proposalService,
        Guid userId)
    {
        return new ProposalResources(
            proposalService,
            new McpBoardResourcesTests.FixedUserContextProvider(userId));
    }

    private static ProposalDto CreateProposal(
        Guid proposalId,
        Guid userId,
        ProposalStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProposalDto(
            proposalId,
            ProposalSourceType.Chat,
            null,
            null,
            userId,
            status,
            RiskLevel.Low,
            "Safe proposal",
            null,
            null,
            now,
            now,
            now.UtcDateTime.AddHours(1),
            status == ProposalStatus.Applied ? now.UtcDateTime : null,
            status == ProposalStatus.Applied ? userId : null,
            status == ProposalStatus.Applied ? now.UtcDateTime : null,
            null,
            "mcp-resource-error-safety-test",
            []);
    }

    private static void AssertGenericError(Exception exception, string prefix)
    {
        exception.Message.Should().Be(prefix + SensitiveDataRedactor.GenericUnexpectedFailureMessage);

        foreach (var marker in HostileMarkers)
            exception.Message.Should().NotContain(marker);
    }
}
