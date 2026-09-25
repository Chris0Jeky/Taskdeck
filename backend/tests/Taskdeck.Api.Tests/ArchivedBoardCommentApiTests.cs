using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ArchivedBoardCommentApiTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ArchivedBoard_RefusesCommentWritesAndRestorationReenablesThem(string operation)
    {
        using var factory = new HostedWorkerDisabledTestWebApplicationFactory();
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "comment-archive");
        var (card, comment) = await SeedAsync(client);
        var boardPath = $"/api/boards/{card.BoardId}";
        var path = $"{boardPath}/cards/{card.Id}/comments";
        var before = await client.GetFromJsonAsync<List<CardCommentDto>>(path);
        before.Should().NotBeNull();
        using var archive = await client.PutAsJsonAsync(boardPath, new UpdateBoardDto(null, null, true));
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        using var denied = await MutateAsync(client, operation, path, comment.Id);

        await ApiTestHarness.AssertErrorContractAsync(denied, HttpStatusCode.Conflict, "InvalidOperation");
        (await denied.Content.ReadAsStringAsync()).Should().Contain("Restore the board");
        var after = await client.GetFromJsonAsync<List<CardCommentDto>>(path);
        after.Should().BeEquivalentTo(before);
        using var anonymous = factory.CreateClient();
        using var anonymousResponse = await MutateAsync(anonymous, operation, path, comment.Id);
        await ApiTestHarness.AssertUnauthorizedAsync(anonymousResponse);
        using var outsider = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsider, "comment-outsider");
        using var forbidden = await MutateAsync(outsider, operation, path, comment.Id);
        await ApiTestHarness.AssertForbiddenAsync(forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().NotContain("archived");

        using var restore = await client.PutAsJsonAsync(boardPath, new UpdateBoardDto(null, null, false));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        using var allowed = await MutateAsync(client, operation, path, comment.Id);
        allowed.IsSuccessStatusCode.Should().BeTrue(await allowed.Content.ReadAsStringAsync());
        await AssertPersistedMutationAsync(client, path, operation, comment.Id);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task IndividuallyArchivedCard_OnActiveBoardStillAllowsDiscussion(string operation)
    {
        using var factory = new HostedWorkerDisabledTestWebApplicationFactory();
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "archived-card-comment");
        var (card, comment) = await SeedAsync(client);
        var cardPath = $"/api/boards/{card.BoardId}/cards/{card.Id}";
        var preview = await client.GetFromJsonAsync<CardDetachPreviewDto>($"{cardPath}/detach-preview");
        preview.Should().NotBeNull();
        using var archive = await client.PostAsJsonAsync($"{cardPath}/archive",
            new CardLifecycleDto(preview!.ExpectedUpdatedAt, preview.ExpectedChildrenFingerprint));
        archive.StatusCode.Should().Be(HttpStatusCode.OK);
        var path = $"{cardPath}/comments";

        using var response = await MutateAsync(client, operation, path, comment.Id);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        await AssertPersistedMutationAsync(client, path, operation, comment.Id);
        var archivedCards = await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{card.BoardId}/cards/archived");
        archivedCards.Should().Contain(item => item.Id == card.Id && item.IsArchived);
    }

    private static async Task<(CardDto Card, CardCommentDto Comment)> SeedAsync(HttpClient client)
    {
        var board = await ApiTestHarness.CreateBoardAsync(client, "Comment archive board");
        using var columnResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await columnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        using var cardResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column!.Id, "Discussion card", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResponse.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        using var commentResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{card!.Id}/comments",
            new CreateCardCommentDto("Before"));
        commentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var comment = await commentResponse.Content.ReadFromJsonAsync<CardCommentDto>();
        comment.Should().NotBeNull();
        return (card, comment!);
    }

    private static Task<HttpResponseMessage> MutateAsync(HttpClient client, string operation, string path, Guid commentId)
        => operation switch
        {
            "create" => client.PostAsJsonAsync(path, new CreateCardCommentDto("After")),
            "update" => client.PatchAsJsonAsync($"{path}/{commentId}", new UpdateCardCommentDto("After")),
            "delete" => client.DeleteAsync($"{path}/{commentId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static async Task AssertPersistedMutationAsync(HttpClient client, string path, string operation, Guid commentId)
    {
        var comments = await client.GetFromJsonAsync<List<CardCommentDto>>(path);
        comments.Should().NotBeNull();
        if (operation == "create")
        {
            comments.Should().HaveCount(2);
            comments.Should().Contain(item => item.Id != commentId && item.Content == "After");
        }
        else if (operation == "update")
        {
            comments!.Single(item => item.Id == commentId).Content.Should().Be("After");
        }
        else
        {
            var deleted = comments!.Single(item => item.Id == commentId);
            deleted.IsDeleted.Should().BeTrue();
            deleted.Content.Should().Be("[deleted]");
        }
    }
}
