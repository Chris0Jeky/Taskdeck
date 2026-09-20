using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ProposalConflictEntityScopeApiTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ForeignReferenceCreatedThroughHttp_CannotDiscloseOtherUsersEvidence(bool boardless, bool targetColumn)
    {
        using var ownerClient = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "evidence-owner");
        var foreignBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "foreign-board");
        var foreignBoard = (await ownerClient.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{foreignBoardId}"))!;
        var foreignColumn = foreignBoard.Columns.Single();
        var rename = await ownerClient.PatchAsJsonAsync($"/api/boards/{foreignBoardId}/columns/{foreignColumn.Id}",
            new UpdateColumnDto("foreign-column-secret", null, 10));
        rename.StatusCode.Should().Be(HttpStatusCode.OK, await rename.Content.ReadAsStringAsync());
        var cardResponse = await ownerClient.PostAsJsonAsync($"/api/boards/{foreignBoardId}/cards",
            new CreateCardDto(foreignBoardId, foreignColumn.Id, "foreign-card-secret", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created, await cardResponse.Content.ReadAsStringAsync());
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var commentResponse = await ownerClient.PostAsJsonAsync($"/api/boards/{foreignBoardId}/cards/{card.Id}/comments",
            new CreateCardCommentDto("foreign-comment-secret"));
        commentResponse.StatusCode.Should().Be(HttpStatusCode.OK, await commentResponse.Content.ReadAsStringAsync());

        using var readerClient = factory.CreateClient();
        var reader = await ApiTestHarness.AuthenticateAsync(readerClient, "evidence-reader");
        var ownBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(readerClient, "reader-board");
        Guid? proposalBoardId = boardless ? null : ownBoardId;
        var parameters = targetColumn
            ? JsonSerializer.Serialize(new { boardId = ownBoardId, columnId = foreignColumn.Id, title = "Proposed card" })
            : JsonSerializer.Serialize(new { cardId = card.Id, title = "Proposed title" });
        // Both actors and the reference are created through ordinary authenticated APIs.
        // No historical-row mutation or test-only persistence shortcut is involved.
        var create = await readerClient.PostAsJsonAsync("/api/automation/proposals",
            new CreateProposalDto(ProposalSourceType.Manual, reader.UserId, "Reference evidence", RiskLevel.High,
                Guid.NewGuid().ToString(), proposalBoardId, Operations:
                [
                    new CreateProposalOperationDto(0, targetColumn ? "create" : "update", "card", parameters,
                        Guid.NewGuid().ToString(), targetColumn ? null : card.Id.ToString()),
                ]));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var proposal = (await create.Content.ReadFromJsonAsync<ProposalDto>())!;

        var response = await readerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/conflicts");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var rows = (await response.Content.ReadFromJsonAsync<List<ConflictRowDto>>())!;
        rows.Should().Contain(row => row.Key == (targetColumn ? "missing-target-column" : "missing-target")
            && row.Tone == ConflictTone.Warn);
        rows.Should().NotContain(row => row.Key == "active-comments" || row.Key == "fresh-data" || row.Key == "capacity");
        body.Should().NotContain("foreign-card-secret");
        body.Should().NotContain("foreign-column-secret");
        body.Should().NotContain("foreign-comment-secret");
    }
}
