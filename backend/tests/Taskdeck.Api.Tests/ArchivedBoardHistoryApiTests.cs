using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardHistoryApiTests
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public ArchivedBoardHistoryApiTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ArchiveBoard_ShouldKeepBoardScopedCaptureAndDecisionHistoryReadable()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archived-history");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archived-history-board");

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "Synthetic archived-board capture", "paste"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        var proposalResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                SourceType: ProposalSourceType.Chat,
                RequestedByUserId: user.UserId,
                Summary: "Synthetic archived-board decision",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString(),
                BoardId: board.Id,
                Operations:
                [
                    new CreateProposalOperationDto(
                        Sequence: 1,
                        ActionType: "update",
                        TargetType: "board",
                        Parameters: JsonSerializer.Serialize(new
                        {
                            boardId = board.Id,
                            name = "Board updated by archived-history test"
                        }),
                        IdempotencyKey: Guid.NewGuid().ToString(),
                        TargetId: board.Id.ToString())
                ]));
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();

        var approveResponse = await client.PostAsync(
            $"/api/automation/proposals/{proposal!.Id}/approve",
            content: null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var executeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var appliedProposal = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        appliedProposal.Should().NotBeNull();
        appliedProposal!.Status.Should().Be(ProposalStatus.Applied);

        var archiveResponse = await client.DeleteAsync($"/api/boards/{board.Id}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unscopedCaptureResponse = await client.GetAsync("/api/capture/items");
        unscopedCaptureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unscopedCaptures = await unscopedCaptureResponse.Content
            .ReadFromJsonAsync<List<CaptureItemSummaryDto>>();
        unscopedCaptures.Should().NotBeNull();
        unscopedCaptures!.Should().NotContain(item => item.Id == capture!.Id);

        var scopedCaptureResponse = await client.GetAsync($"/api/capture/items?boardId={board.Id}");
        scopedCaptureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var scopedCaptures = await scopedCaptureResponse.Content
            .ReadFromJsonAsync<List<CaptureItemSummaryDto>>();
        scopedCaptures.Should().NotBeNull();
        scopedCaptures!.Should().ContainSingle(item =>
            item.Id == capture!.Id && item.BoardId == board.Id);

        var unscopedProposalResponse = await client.GetAsync("/api/automation/proposals");
        unscopedProposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unscopedProposals = await unscopedProposalResponse.Content
            .ReadFromJsonAsync<List<ProposalDto>>();
        unscopedProposals.Should().NotBeNull();
        unscopedProposals!.Should().NotContain(item => item.Id == proposal.Id);

        var scopedProposalResponse = await client.GetAsync(
            $"/api/automation/proposals?boardId={board.Id}");
        scopedProposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var scopedProposals = await scopedProposalResponse.Content
            .ReadFromJsonAsync<List<ProposalDto>>();
        scopedProposals.Should().NotBeNull();
        scopedProposals!.Should().ContainSingle(item =>
            item.Id == proposal.Id &&
            item.BoardId == board.Id &&
            item.Status == ProposalStatus.Applied);
    }
}
