using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ArchivedBoardArtifactVisibilityApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ArchivedBoardArtifactVisibilityApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RepeatedReset_KeepsExactlyOneActiveInboxAndReviewStory_WhileHistoryRemainsReadable()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archived-artifact-reset");

        var first = await CreateStoryAsync(client, user.UserId, "first");
        await QuarantineBoardAsync(client, first.Board.Id);

        var second = await CreateStoryAsync(client, user.UserId, "second");
        await AssertActiveStoryAsync(client, second);
        await AssertHistoricalStoryAsync(client, first);

        await QuarantineBoardAsync(client, second.Board.Id);

        var third = await CreateStoryAsync(client, user.UserId, "third");
        await AssertActiveStoryAsync(client, third);
        await AssertHistoricalStoryAsync(client, first);
        await AssertHistoricalStoryAsync(client, second);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Boards.SingleAsync(board => board.Id == first.Board.Id)).IsArchived.Should().BeTrue();
        (await db.Boards.SingleAsync(board => board.Id == second.Board.Id)).IsArchived.Should().BeTrue();
        (await db.Boards.SingleAsync(board => board.Id == third.Board.Id)).IsArchived.Should().BeFalse();
        (await db.LlmRequests.CountAsync(item =>
            item.Id == first.Capture.Id || item.Id == second.Capture.Id || item.Id == third.Capture.Id))
            .Should().Be(3);
        (await db.AutomationProposals.CountAsync(proposal =>
            proposal.Id == first.Proposal.Id || proposal.Id == second.Proposal.Id || proposal.Id == third.Proposal.Id))
            .Should().Be(3);
    }

    private static async Task<DemoStory> CreateStoryAsync(HttpClient client, Guid userId, string cycle)
    {
        var board = await ApiTestHarness.CreateBoardAsync(client, $"DEMO: Client Onboarding Demo {cycle}");

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, $"Synthetic capture story {cycle}", "Typed"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        var proposalResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Queue,
                userId,
                $"Synthetic review story {cycle}",
                RiskLevel.Low,
                $"reset-story-{cycle}-{Guid.NewGuid():N}",
                board.Id,
                capture!.Id.ToString(),
                Operations:
                [
                    new CreateProposalOperationDto(
                        1,
                        "update",
                        "board",
                        $"{{\"boardId\":\"{board.Id}\",\"name\":\"Synthetic {cycle}\"}}",
                        Guid.NewGuid().ToString(),
                        board.Id.ToString())
                ]));
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();

        return new DemoStory(board, capture, proposal!);
    }

    private static async Task QuarantineBoardAsync(HttpClient client, Guid boardId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/boards/{boardId}",
            new UpdateBoardDto($"RESET: Taskdeck demo board {boardId}", null, true));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        board!.IsArchived.Should().BeTrue();
    }

    private static async Task AssertActiveStoryAsync(HttpClient client, DemoStory expected)
    {
        var inboxResponse = await client.GetAsync("/api/capture/items?limit=200");
        inboxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<List<CaptureItemSummaryDto>>();
        inbox.Should().ContainSingle(item => item.Id == expected.Capture.Id);
        inbox.Should().ContainSingle();

        var reviewResponse = await client.GetAsync(
            $"/api/automation/proposals?status={ProposalStatus.PendingReview}&limit=200");
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var review = await reviewResponse.Content.ReadFromJsonAsync<List<ProposalDto>>();
        review.Should().ContainSingle(item => item.Id == expected.Proposal.Id);
        review.Should().ContainSingle();
    }

    private static async Task AssertHistoricalStoryAsync(HttpClient client, DemoStory expected)
    {
        var inboxResponse = await client.GetAsync(
            $"/api/capture/items?boardId={expected.Board.Id}&limit=200");
        inboxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<List<CaptureItemSummaryDto>>();
        inbox.Should().ContainSingle(item => item.Id == expected.Capture.Id);

        var captureDetailResponse = await client.GetAsync($"/api/capture/items/{expected.Capture.Id}");
        captureDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reviewResponse = await client.GetAsync(
            $"/api/automation/proposals?boardId={expected.Board.Id}&limit=200");
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var review = await reviewResponse.Content.ReadFromJsonAsync<List<ProposalDto>>();
        review.Should().ContainSingle(item => item.Id == expected.Proposal.Id);

        var proposalDetailResponse = await client.GetAsync(
            $"/api/automation/proposals/{expected.Proposal.Id}");
        proposalDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record DemoStory(BoardDto Board, CaptureItemDto Capture, ProposalDto Proposal);
}
