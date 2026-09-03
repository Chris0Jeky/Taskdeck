using System.Text.Json;
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

public class ProposalToolsErrorSafetyTests
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
    public async Task GetProposalStatus_UnexpectedFailure_ReturnsGenericError()
    {
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, HostileError));

        var json = await CreateTools(proposalService.Object).GetProposalStatus(proposalId.ToString());

        AssertGenericError(json);
    }

    [Fact]
    public async Task ListProposals_UnexpectedFailure_ReturnsGenericError()
    {
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalsAsync(
                It.IsAny<ProposalFilterDto?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IEnumerable<ProposalDto>>(
                ErrorCodes.UnexpectedError,
                HostileError));

        var json = await CreateTools(proposalService.Object).ListProposals();

        AssertGenericError(json);
    }

    [Fact]
    public async Task DismissProposal_UnexpectedLookupFailure_ReturnsGenericError()
    {
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, HostileError));

        var json = await CreateTools(proposalService.Object).DismissProposal(proposalId.ToString());

        AssertGenericError(json);
    }

    [Fact]
    public async Task DismissProposal_UnexpectedDismissFailure_ReturnsGenericError()
    {
        var userId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateProposal(proposalId, userId)));
        proposalService
            .Setup(service => service.DismissProposalsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == proposalId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<int>(ErrorCodes.UnexpectedError, HostileError));

        var json = await CreateTools(proposalService.Object, userId).DismissProposal(proposalId.ToString());

        AssertGenericError(json);
    }

    [Fact]
    public async Task GetProposalStatus_KnownDomainFailure_PreservesStableMessage()
    {
        const string stableMessage = "Proposal not found.";
        var proposalId = Guid.NewGuid();
        var proposalService = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposalService
            .Setup(service => service.GetProposalByIdAsync(
                proposalId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.NotFound, stableMessage));

        var json = await CreateTools(proposalService.Object).GetProposalStatus(proposalId.ToString());

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetString().Should().Be(stableMessage);
    }

    private static ProposalTools CreateTools(
        IAutomationProposalService proposalService,
        Guid? userId = null)
    {
        return new ProposalTools(
            proposalService,
            new McpBoardResourcesTests.FixedUserContextProvider(userId ?? Guid.NewGuid()));
    }

    private static ProposalDto CreateProposal(Guid proposalId, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProposalDto(
            proposalId,
            ProposalSourceType.Chat,
            null,
            null,
            userId,
            ProposalStatus.Applied,
            RiskLevel.Low,
            "Safe proposal",
            null,
            null,
            now,
            now,
            now.UtcDateTime.AddHours(1),
            now.UtcDateTime,
            userId,
            now.UtcDateTime,
            null,
            "mcp-error-safety-test",
            []);
    }

    private static void AssertGenericError(string json)
    {
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetString()
            .Should().Be(SensitiveDataRedactor.GenericUnexpectedFailureMessage);

        foreach (var marker in HostileMarkers)
            json.Should().NotContain(marker);
    }
}
