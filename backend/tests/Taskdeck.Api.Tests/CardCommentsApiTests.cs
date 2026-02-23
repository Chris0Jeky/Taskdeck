using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardCommentsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public CardCommentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CommentEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var commentId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/cards/{cardId}/comments"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}/comments",
                new CreateCardCommentDto("Unauthenticated comment")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PatchAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}/comments/{commentId}",
                new UpdateCardCommentDto("Updated")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/cards/{cardId}/comments/{commentId}"));
    }

    [Fact]
    public async Task CreateAndListComments_ShouldSupportReplyFlow_AndRejectNestedReplies()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do");
        var card = await CreateCardAsync(board.Id, column.Id, "Comment thread card");

        var topLevelResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments",
            new CreateCardCommentDto("Top-level comment"));
        topLevelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var topLevelComment = await topLevelResponse.Content.ReadFromJsonAsync<CardCommentDto>();
        topLevelComment.Should().NotBeNull();
        topLevelComment!.ParentCommentId.Should().BeNull();

        var replyResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments",
            new CreateCardCommentDto("Reply comment", topLevelComment.Id));
        replyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var replyComment = await replyResponse.Content.ReadFromJsonAsync<CardCommentDto>();
        replyComment.Should().NotBeNull();
        replyComment!.ParentCommentId.Should().Be(topLevelComment.Id);

        var nestedReplyResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments",
            new CreateCardCommentDto("Nested reply not allowed", replyComment.Id));
        nestedReplyResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var nestedError = await nestedReplyResponse.Content.ReadFromJsonAsync<JsonElement>();
        nestedError.GetProperty("errorCode").GetString().Should().Be("ValidationError");

        var listResponse = await _client.GetAsync($"/api/boards/{board.Id}/cards/{card.Id}/comments");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);

        var comments = await listResponse.Content.ReadFromJsonAsync<List<CardCommentDto>>();
        comments.Should().NotBeNull();
        comments!.Should().HaveCount(2);
        comments.Should().Contain(comment => comment.ParentCommentId == null);
        comments.Should().Contain(comment => comment.ParentCommentId == topLevelComment.Id);
    }

    [Fact]
    public async Task GetComments_ShouldReturnForbidden_WhenCallerCannotReadBoard()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "comment-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "comment-outsider");

        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "comment-board");
        var column = await CreateColumnAsync(ownerClient, board.Id, "To Do");
        var card = await CreateCardAsync(ownerClient, board.Id, column.Id, "Private card");

        var response = await outsiderClient.GetAsync($"/api/boards/{board.Id}/cards/{card.Id}/comments");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task UpdateComment_ShouldReturnForbidden_ForEditorWhoIsNotAuthorOrModerator()
    {
        using var ownerClient = _factory.CreateClient();
        using var editorClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "comment-owner");
        var editor = await ApiTestHarness.AuthenticateAsync(editorClient, "comment-editor");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "comment-board");
        var column = await CreateColumnAsync(ownerClient, board.Id, "To Do");
        var card = await CreateCardAsync(ownerClient, board.Id, column.Id, "Moderation card");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, editor.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createCommentResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments",
            new CreateCardCommentDto("Owner comment"));
        createCommentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var comment = await createCommentResponse.Content.ReadFromJsonAsync<CardCommentDto>();
        comment.Should().NotBeNull();

        var updateResponse = await editorClient.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments/{comment!.Id}",
            new UpdateCardCommentDto("Editor update attempt"));

        await ApiTestHarness.AssertForbiddenAsync(updateResponse);
    }

    [Fact]
    public async Task CreateComment_ShouldPublishMentionNotification_ForReadableMentionedUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var collaboratorClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "comment-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(collaboratorClient, "comment-collaborator");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "comment-board");
        var column = await CreateColumnAsync(ownerClient, board.Id, "To Do");
        var card = await CreateCardAsync(ownerClient, board.Id, column.Id, "Mention card");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, collaborator.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createCommentResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}/comments",
            new CreateCardCommentDto($"Please review this @{collaborator.Username}"));
        createCommentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createdComment = await createCommentResponse.Content.ReadFromJsonAsync<CardCommentDto>();
        createdComment.Should().NotBeNull();
        createdComment!.Mentions.Should().Contain(mention => mention.UserId == collaborator.UserId);

        var notificationsResponse = await collaboratorClient.GetAsync("/api/notifications");
        var notificationsBody = await notificationsResponse.Content.ReadAsStringAsync();
        notificationsResponse.StatusCode.Should().Be(HttpStatusCode.OK, notificationsBody);

        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<List<NotificationDto>>();
        notifications.Should().NotBeNull();
        notifications!
            .Should()
            .Contain(notification =>
                notification.Type == NotificationType.Mention &&
                notification.SourceEntityType == "card-comment" &&
                notification.SourceEntityId == createdComment.Id &&
                notification.BoardId == board.Id);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "card-comments-board");
    }

    private async Task<ColumnDto> CreateColumnAsync(Guid boardId, string name)
    {
        return await CreateColumnAsync(_client, boardId, name);
    }

    private static async Task<ColumnDto> CreateColumnAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<ColumnDto>();
        payload.Should().NotBeNull();
        return payload!;
    }

    private async Task<CardDto> CreateCardAsync(Guid boardId, Guid columnId, string title)
    {
        return await CreateCardAsync(_client, boardId, columnId, title);
    }

    private static async Task<CardDto> CreateCardAsync(HttpClient client, Guid boardId, Guid columnId, string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<CardDto>();
        payload.Should().NotBeNull();
        return payload!;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
            return;

        await ApiTestHarness.AuthenticateAsync(_client, "card-comments-suite");
        _isAuthenticated = true;
    }
}
