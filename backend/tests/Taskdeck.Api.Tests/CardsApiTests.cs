using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    [Theory]
    [InlineData("remove")]
    [InlineData("reparent")]
    [InlineData("restore-archived")]
    public async Task Hierarchy_ChildMembershipOrArchivedVersionChangeInvalidatesApproval(string change)
    {
        var board = await CreateBoardAsync(); var column = await CreateColumnAsync(board.Id, "Pins", null);
        var path = $"/api/boards/{board.Id}/cards";
        async Task<CardDto> Create(string title, Guid? parent = null)
        {
            var response = await _client.PostAsJsonAsync(path, new CreateCardDto(board.Id, column.Id, title, null, null, null, ParentCardId: parent));
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var parent = await Create("Parent"); var other = await Create("Other parent"); var child = await Create("Child", parent.Id);
        if (change == "restore-archived")
            child = (await (await _client.PostAsJsonAsync(path + $"/{child.Id}/archive", new { expectedUpdatedAt = child.UpdatedAt })).Content.ReadFromJsonAsync<CardDto>())!;
        var preview = (await _client.GetFromJsonAsync<CardDetachPreviewDto>(path + $"/{parent.Id}/detach-preview"))!;
        if (change == "remove") (await _client.DeleteAsync(path + $"/{child.Id}")).EnsureSuccessStatusCode();
        else if (change == "reparent") (await _client.PatchAsJsonAsync(path + $"/{child.Id}", new { parentCardId = other.Id, expectedUpdatedAt = child.UpdatedAt })).EnsureSuccessStatusCode();
        else (await _client.PostAsJsonAsync(path + $"/{child.Id}/restore", new { expectedUpdatedAt = child.UpdatedAt })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync(path + $"/{parent.Id}/archive", preview)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.GetFromJsonAsync<CardDto>(path + $"/{parent.Id}"))!.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Hierarchy_RejectsAnonymousViewerForeignParentsAndForeignPreview()
    {
        var board = await CreateBoardAsync(); var column = await CreateColumnAsync(board.Id, "Access", null);
        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column.Id, "Private", null, null, null));
        var card = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        var foreign = await CreateBoardAsync(); var foreignColumn = await CreateColumnAsync(foreign.Id, "Foreign", null);
        var foreignResponse = await _client.PostAsJsonAsync($"/api/boards/{foreign.Id}/cards", new CreateCardDto(foreign.Id, foreignColumn.Id, "Foreign", null, null, null));
        var foreignCard = (await foreignResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        (await _client.PatchAsJsonAsync(path, new { parentCardId = foreignCard.Id, expectedUpdatedAt = card.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(path + "/detach-preview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var outsider = _factory.CreateClient(); var viewer = await ApiTestHarness.AuthenticateAsync(outsider, "hierarchy-viewer");
        (await outsider.GetAsync(path + "/detach-preview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        (await outsider.PatchAsJsonAsync(path, new { clearParent = true, expectedUpdatedAt = card.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.GetAsync(path + "/detach-preview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hierarchy_AssignmentDepthAndRemovalKeepIdentityAndRequireVersion()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Hierarchy", null);
        var path = $"/api/boards/{board.Id}/cards";
        var cards = new List<CardDto>();
        for (var i = 0; i < 4; i++)
        {
            var response = await _client.PostAsJsonAsync(path, new { columnId = column.Id, title = $"Level {i}",
                parentCardId = cards.LastOrDefault()?.Id, workItemType = i % 2 == 0 ? "Spike" : "Epic" });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            cards.Add((await response.Content.ReadFromJsonAsync<CardDto>())!);
        }
        (await _client.PostAsJsonAsync(path, new { columnId = column.Id, title = "Too deep", parentCardId = cards[3].Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsJsonAsync(path + $"/{cards[0].Id}", new { parentCardId = cards[3].Id, expectedUpdatedAt = cards[0].UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsJsonAsync(path + $"/{cards[1].Id}", new { clearParent = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var removed = await _client.PatchAsJsonAsync(path + $"/{cards[1].Id}", new { clearParent = true, expectedUpdatedAt = cards[1].UpdatedAt });
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        var detached = (await removed.Content.ReadFromJsonAsync<CardDto>())!;
        detached.ParentCardId.Should().BeNull();
        detached.Id.Should().Be(cards[1].Id);
        detached.ColumnId.Should().Be(cards[1].ColumnId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hierarchy_ArchiveAndDeleteRequireExactVisibleChildrenIncludingArchived(bool delete)
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Detachment", null);
        var path = $"/api/boards/{board.Id}/cards";
        async Task<CardDto> Create(string title, Guid? parent = null)
        {
            var response = await _client.PostAsJsonAsync(path, new { columnId = column.Id, title, parentCardId = parent });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var parent = await Create("Parent");
        var child = await Create("Archived child", parent.Id);
        (await _client.PostAsJsonAsync(path + $"/{child.Id}/archive", new { expectedUpdatedAt = child.UpdatedAt })).EnsureSuccessStatusCode();
        var preview = (await _client.GetFromJsonAsync<CardDetachPreviewDto>(path + $"/{parent.Id}/detach-preview"))!;
        preview.Children.Should().ContainSingle().Which.IsArchived.Should().BeTrue();
        async Task<HttpResponseMessage> Apply(CardDetachPreviewDto pin) => delete
            ? await _client.DeleteAsync(path + $"/{parent.Id}?expectedUpdatedAt={Uri.EscapeDataString(pin.ExpectedUpdatedAt.ToString("O"))}&expectedChildrenFingerprint={pin.ExpectedChildrenFingerprint}")
            : await _client.PostAsJsonAsync(path + $"/{parent.Id}/archive", pin);
        var added = await Create("Added after preview", parent.Id);
        (await Apply(preview)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview = (await _client.GetFromJsonAsync<CardDetachPreviewDto>(path + $"/{parent.Id}/detach-preview"))!;
        (await _client.PatchAsJsonAsync(path + $"/{added.Id}", new { title = "Edited after preview", expectedUpdatedAt = added.UpdatedAt })).EnsureSuccessStatusCode();
        (await Apply(preview)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview = (await _client.GetFromJsonAsync<CardDetachPreviewDto>(path + $"/{parent.Id}/detach-preview"))!;
        preview.Children.Should().HaveCount(2);
        (await Apply(preview)).EnsureSuccessStatusCode();
        var archived = (await _client.GetFromJsonAsync<CardDto>(path + $"/{child.Id}"))!;
        archived.ParentCardId.Should().BeNull();
        archived.IsArchived.Should().BeTrue();
        archived.ColumnId.Should().Be(child.ColumnId);
        if (!delete)
        {
            var current = (await _client.GetFromJsonAsync<CardDto>(path + $"/{parent.Id}"))!;
            (await _client.PostAsJsonAsync(path + $"/{parent.Id}/restore", new { expectedUpdatedAt = current.UpdatedAt })).EnsureSuccessStatusCode();
            (await _client.GetFromJsonAsync<CardDto>(path + $"/{added.Id}"))!.ParentCardId.Should().BeNull();
        }
    }

    [Fact]
    public async Task WorkItemType_OmittedPayloadsPreserveDefaultsAndArchivedOrForeignCardsRejectWrites()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Type guards", null);
        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new { columnId = column.Id, title = "Default" });
        var card = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        card.WorkItemType.Should().Be("Task");
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        var changed = await _client.PatchAsJsonAsync(path, new { workItemType = "Epic", expectedUpdatedAt = card.UpdatedAt });
        card = (await changed.Content.ReadFromJsonAsync<CardDto>())!;
        var titleOnly = await _client.PatchAsJsonAsync(path, new { title = "Keep type" });
        card = (await titleOnly.Content.ReadFromJsonAsync<CardDto>())!;
        card.WorkItemType.Should().Be("Epic");
        var otherBoard = await CreateBoardAsync();
        (await _client.PatchAsJsonAsync($"/api/boards/{otherBoard.Id}/cards/{card.Id}",
            new { workItemType = "Spike", expectedUpdatedAt = card.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PatchAsJsonAsync(path, new { title = "Must not change", workItemType = "Question", expectedUpdatedAt = card.UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetFromJsonAsync<CardDto>(path))!.Title.Should().Be("Keep type");
        using var anonymous = _factory.CreateClient();
        await ApiTestHarness.AssertUnauthorizedAsync(await anonymous.PatchAsJsonAsync(path,
            new { workItemType = "Spike", expectedUpdatedAt = card.UpdatedAt }));
        var archivedResponse = await _client.PostAsJsonAsync(path + "/archive", new { expectedUpdatedAt = card.UpdatedAt });
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<CardDto>())!;
        (await _client.PatchAsJsonAsync(path, new { workItemType = "Spike", expectedUpdatedAt = archived.UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        archived.WorkItemType.Should().Be("Epic");
        await ApiTestHarness.AuthenticateAsync(_client, "type-outsider");
        await ApiTestHarness.AssertForbiddenAsync(await _client.PatchAsJsonAsync(path,
            new { workItemType = "Spike", expectedUpdatedAt = archived.UpdatedAt }));
    }

    [Fact]
    public async Task WorkItemType_RoundTripsAndRejectsUnknownOrUnversionedChanges()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Types", null);
        var path = $"/api/boards/{board.Id}/cards";
        var response = await _client.PostAsJsonAsync(path,
            new { columnId = column.Id, title = "An epic", workItemType = "Epic" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        card.WorkItemType.Should().Be("Epic");
        (await _client.PostAsJsonAsync(path, new { columnId = column.Id, title = "Invalid", workItemType = "Mystery" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsJsonAsync(path + $"/{card.Id}", new { workItemType = "Spike" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var changed = await _client.PatchAsJsonAsync(path + $"/{card.Id}",
            new { workItemType = "Spike", expectedUpdatedAt = card.UpdatedAt });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var spike = (await changed.Content.ReadFromJsonAsync<CardDto>())!;
        spike.WorkItemType.Should().Be("Spike");
        spike.Id.Should().Be(card.Id);
        spike.ColumnId.Should().Be(card.ColumnId);
        (await _client.PatchAsJsonAsync(path + $"/{card.Id}",
            new { workItemType = "Task", expectedUpdatedAt = card.UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.GetFromJsonAsync<CardDto>(path + $"/{card.Id}"))!.WorkItemType.Should().Be("Spike");
    }

    [Fact]
    public async Task CardLifecycle_RejectsMissingVersionArchivedWritesFullRestoreAndArchivedBoard()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "One active", wipLimit: 1);
        var created = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column.Id, "Archive guard", null, null, null));
        var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        (await _client.PostAsJsonAsync(path + "/archive", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var archivedResponse = await _client.PostAsJsonAsync(path + "/archive", new { expectedUpdatedAt = card.UpdatedAt });
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<CardDto>())!;
        (await _client.GetFromJsonAsync<CardDto>(path))!.IsArchived.Should().BeTrue();
        (await _client.PatchAsJsonAsync(path, new UpdateCardDto("No", null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.PatchAsJsonAsync(path, new UpdateCardDto(null, null, null, null, null, [])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.PostAsJsonAsync(path + "/move", new MoveCardDto(column.Id, 0)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.DeleteAsync($"/api/boards/{board.Id}/columns/{column.Id}"))
            .StatusCode.Should().NotBe(HttpStatusCode.NoContent, "archived placement must remain recoverable");
        (await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column.Id, "Active control", null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var beforeFailedRestore = (await _client.GetFromJsonAsync<BoardDependencyDto>($"/api/boards/{board.Id}/dependencies"))!;
        (await _client.PostAsJsonAsync(path + "/restore", new { expectedUpdatedAt = archived.UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetFromJsonAsync<CardDto>(path)).Should().BeEquivalentTo(archived);
        (await _client.GetFromJsonAsync<BoardDependencyDto>($"/api/boards/{board.Id}/dependencies"))
            .Should().BeEquivalentTo(beforeFailedRestore);
        await _client.PutAsJsonAsync($"/api/boards/{board.Id}", new UpdateBoardDto(null, null, true));
        (await _client.PostAsJsonAsync(path + "/restore", new { expectedUpdatedAt = archived.UpdatedAt }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CardLifecycle_EnforcesClaimsBoardAccessAndWrongBoardIsolation()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Protected", null);
        var created = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column.Id, "Private", null, null, null));
        var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;
        var secondBoard = await CreateBoardAsync();
        var payload = new { expectedUpdatedAt = card.UpdatedAt };
        foreach (var action in new[] { "archive", "restore" })
        {
            (await _client.PostAsJsonAsync($"/api/boards/{secondBoard.Id}/cards/{card.Id}/{action}", payload))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var anonymous = _factory.CreateClient();
            await ApiTestHarness.AssertUnauthorizedAsync(await anonymous.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards/{card.Id}/{action}", payload));
        }
        await ApiTestHarness.AuthenticateAsync(_client, "lifecycle-outsider");
        foreach (var action in new[] { "archive", "restore" })
            await ApiTestHarness.AssertForbiddenAsync(await _client.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards/{card.Id}/{action}", payload));
        await ApiTestHarness.AssertForbiddenAsync(await _client.GetAsync($"/api/boards/{board.Id}/cards/archived"));
        await ApiTestHarness.AssertForbiddenAsync(await _client.GetAsync($"/api/boards/{board.Id}/cards/{card.Id}"));
    }

    [Fact]
    public async Task CardLifecycle_ShouldArchiveAndRestoreInPlace_WithoutChangingLegacyBlockState()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Archive lifecycle", wipLimit: null);
        var created = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column.Id, "Keep my history", "Original", null, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;
        var response = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{card.Id}/archive",
            new { expectedUpdatedAt = card.UpdatedAt });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var archived = await response.Content.ReadFromJsonAsync<JsonElement>();
        archived.GetProperty("isArchived").GetBoolean().Should().BeTrue();
        archived.GetProperty("isBlocked").GetBoolean().Should().BeFalse();
        (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!
            .Should().NotContain(c => c.Id == card.Id);
        (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards/archived"))!
            .Should().ContainSingle(c => c.Id == card.Id);
        var staleRestore = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{card.Id}/restore",
            new { expectedUpdatedAt = card.UpdatedAt });
        staleRestore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var restored = await _client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{card.Id}/restore",
            new { expectedUpdatedAt = archived.GetProperty("updatedAt").GetDateTimeOffset() });
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoredCard = (await restored.Content.ReadFromJsonAsync<CardDto>())!;
        restoredCard.Id.Should().Be(card.Id);
        restoredCard.ColumnId.Should().Be(column.Id);
        restoredCard.Description.Should().Be("Original");
        (await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!
            .Should().ContainSingle(c => c.Id == card.Id);
    }

    public CardsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CardsEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/cards"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/boards/{boardId}/cards/{cardId}/provenance"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/cards",
                new CreateCardDto(boardId, columnId, "Unauthorized", null, null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PatchAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}",
                new UpdateCardDto("Updated", null, null, null, null, null)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/cards/{cardId}/move",
                new MoveCardDto(columnId, 0)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.DeleteAsync($"/api/boards/{boardId}/cards/{cardId}"));
    }

    [Fact]
    public async Task GetCards_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        var board = await CreateBoardAsync();

        await ApiTestHarness.AuthenticateAsync(_client, "cards-other-user");
        _isAuthenticated = true;

        var response = await _client.GetAsync($"/api/boards/{board.Id}/cards");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task CreateCard_ShouldReturnBadRequest_WhenTargetColumnWipLimitExceeded()
    {
        var board = await CreateBoardAsync();
        var limitedColumn = await CreateColumnAsync(board.Id, "In Progress", wipLimit: 1);

        var firstCreateResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, limitedColumn.Id, "Card 1", null, null, null));

        firstCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondCreateResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, limitedColumn.Id, "Card 2", null, null, null));

        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await secondCreateResponse.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("WipLimitExceeded");
    }

    [Fact]
    public async Task CreateCard_ShouldReturnConflictWithInvalidOperation_WhenBoardIsArchived()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var archiveResponse = await _client.PutAsJsonAsync(
            $"/api/boards/{board.Id}",
            new UpdateBoardDto(null, null, IsArchived: true));
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column.Id, "Blocked card", null, null, null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Conflict, "InvalidOperation");
    }

    [Fact]
    public async Task MoveCard_ShouldMoveCardAcrossColumns()
    {
        var board = await CreateBoardAsync();
        var sourceColumn = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var targetColumn = await CreateColumnAsync(board.Id, "Done", wipLimit: null);

        var createCardResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, sourceColumn.Id, "Move me", null, null, null));
        createCardResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var card = await createCardResponse.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();

        var moveResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card!.Id}/move",
            new MoveCardDto(targetColumn.Id, 0));

        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var movedCard = await moveResponse.Content.ReadFromJsonAsync<CardDto>();
        movedCard.Should().NotBeNull();
        movedCard!.ColumnId.Should().Be(targetColumn.Id);
        movedCard.Position.Should().Be(0);
    }

    [Fact]
    public async Task MoveCard_ShouldReturnBadRequest_WhenTargetColumnWipLimitExceeded()
    {
        var board = await CreateBoardAsync();
        var sourceColumn = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var limitedTargetColumn = await CreateColumnAsync(board.Id, "In Progress", wipLimit: 1);

        await CreateCardAsync(board.Id, limitedTargetColumn.Id, "Existing target card");
        var cardToMove = await CreateCardAsync(board.Id, sourceColumn.Id, "Card to move");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{cardToMove.Id}/move",
            new MoveCardDto(limitedTargetColumn.Id, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("WipLimitExceeded");
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnBadRequest_WhenTitleIsEmpty()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var card = await CreateCardAsync(board.Id, column.Id, "Valid title");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto(string.Empty, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnConflict_WhenExpectedUpdatedAtIsStale()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "To Do", wipLimit: null);
        var card = await CreateCardAsync(board.Id, column.Id, "Concurrency card");

        var firstUpdateResponse = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto("Updated by another session", null, null, null, null, null));
        firstUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleConflictResponse = await _client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto(
                "Stale write",
                null,
                null,
                null,
                null,
                null,
                card.UpdatedAt));

        staleConflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorPayload = await staleConflictResponse.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("Conflict");
        errorPayload.GetProperty("message").GetString().Should().Contain("updated by another session");
    }

    [Fact]
    public async Task DeleteCard_ShouldReturnNotFound_WhenCardDoesNotExist()
    {
        var board = await CreateBoardAsync();

        var response = await _client.DeleteAsync($"/api/boards/{board.Id}/cards/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateCard_ShouldReturnNotFound_WhenColumnDoesNotExist()
    {
        var board = await CreateBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, Guid.NewGuid(), "Missing column", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateCard_ShouldReturnNotFoundAndCreateNothing_WhenColumnBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "Other board column", wipLimit: null);

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards",
            new CreateCardDto(boardA.Id, boardBColumn.Id, "Cross-board card", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");

        var cardsOnBoardA = await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardA.Id}/cards");
        var cardsOnBoardB = await _client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardB.Id}/cards");
        cardsOnBoardA.Should().BeEmpty();
        cardsOnBoardB.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateCard_ShouldReturnNotFound_WhenCardBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "To Do", wipLimit: null);
        var boardBCard = await CreateCardAsync(boardB.Id, boardBColumn.Id, "Card in board B");

        var response = await _client.PatchAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards/{boardBCard.Id}",
            new UpdateCardDto("Updated", null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task MoveCard_ShouldReturnNotFound_WhenTargetColumnBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardAColumn = await CreateColumnAsync(boardA.Id, "To Do", wipLimit: null);
        var boardBColumn = await CreateColumnAsync(boardB.Id, "Other board", wipLimit: null);
        var boardACard = await CreateCardAsync(boardA.Id, boardAColumn.Id, "Card in board A");

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards/{boardACard.Id}/move",
            new MoveCardDto(boardBColumn.Id, 0));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteCard_ShouldReturnNotFound_WhenCardBelongsToDifferentBoard()
    {
        var boardA = await CreateBoardAsync();
        var boardB = await CreateBoardAsync();
        var boardBColumn = await CreateColumnAsync(boardB.Id, "To Do", wipLimit: null);
        var boardBCard = await CreateCardAsync(boardB.Id, boardBColumn.Id, "Card in board B");

        var response = await _client.DeleteAsync($"/api/boards/{boardA.Id}/cards/{boardBCard.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetCardProvenance_ShouldReturnCaptureMetadata_ForCaptureCreatedCard()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "cards-provenance");
        _isAuthenticated = true;
        var board = await ApiTestHarness.CreateBoardAsync(_client, "cards-provenance-board");
        await CreateColumnAsync(board.Id, "Inbox", wipLimit: null);

        var createCaptureResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Validate capture provenance endpoint
                """));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureItem = await createCaptureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        captureItem.Should().NotBeNull();

        var triageResponse = await _client.PostAsync($"/api/capture/items/{captureItem!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triagedItem = await WaitForCaptureStatusAsync(_client, captureItem.Id, CaptureStatus.ProposalCreated);
        triagedItem.Provenance.Should().NotBeNull();
        triagedItem.Provenance!.ProposalId.Should().NotBeNull();
        // #1273: triage is deterministic/offline — provenance names the extractor, not the LLM provider.
        triagedItem.Provenance.Provider.Should().Be(CaptureTriageService.TriageProviderName);
        triagedItem.Provenance.Model.Should().Be(CaptureTriageService.TriageModelName);

        var proposalId = triagedItem.Provenance.ProposalId!.Value;
        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await ExecuteProposalAsync(_client, proposalId);

        var createdCard = await WaitForSingleCardAsync(_client, board.Id);

        var provenanceResponse = await _client.GetAsync($"/api/boards/{board.Id}/cards/{createdCard.Id}/provenance");
        provenanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var provenance = await provenanceResponse.Content.ReadFromJsonAsync<CardCaptureProvenanceDto>();
        provenance.Should().NotBeNull();
        provenance!.CardId.Should().Be(createdCard.Id);
        provenance.CaptureItemId.Should().Be(captureItem.Id);
        provenance.ProposalId.Should().Be(proposalId);
        provenance.ProposalStatus.Should().Be(ProposalStatus.Applied);
        provenance.TriageRunId.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCardProvenance_ShouldReturnNotFound_WhenCardWasNotCreatedFromCapture()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "cards-provenance-manual");
        _isAuthenticated = true;

        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Inbox", wipLimit: null);
        var manualCard = await CreateCardAsync(board.Id, column.Id, "Manual capture-less card");

        var response = await _client.GetAsync($"/api/boards/{board.Id}/cards/{manualCard.Id}/provenance");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetCardProvenance_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "cards-provenance-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "cards-provenance-outsider");

        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "cards-provenance-owner-board");
        var createColumnResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Inbox", null, null));
        createColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createCaptureResponse = await ownerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Verify cross-user provenance restriction
                """));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var captureItem = await createCaptureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        captureItem.Should().NotBeNull();

        var triageResponse = await ownerClient.PostAsync($"/api/capture/items/{captureItem!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triagedItem = await WaitForCaptureStatusAsync(ownerClient, captureItem.Id, CaptureStatus.ProposalCreated);
        triagedItem.Provenance.Should().NotBeNull();
        triagedItem.Provenance!.ProposalId.Should().NotBeNull();
        var proposalId = triagedItem.Provenance.ProposalId!.Value;

        var approveResponse = await ownerClient.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await ExecuteProposalAsync(ownerClient, proposalId);

        var createdCard = await WaitForSingleCardAsync(ownerClient, board.Id);
        var response = await outsiderClient.GetAsync($"/api/boards/{board.Id}/cards/{createdCard.Id}/provenance");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "cards-board", "Card integration tests");
    }

    private async Task<ColumnDto> CreateColumnAsync(Guid boardId, string name, int? wipLimit)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, wipLimit));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await response.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        return column!;
    }

    private async Task<CardDto> CreateCardAsync(Guid boardId, Guid columnId, string title)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        return card!;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "cards-suite");
        _isAuthenticated = true;
    }

    private async Task<CardDto> WaitForSingleCardAsync(HttpClient client, Guid boardId)
    {
        var cardList = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var cardsResponse = await client.GetAsync($"/api/boards/{boardId}/cards");
                cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
                cards.Should().NotBeNull();
                return cards!;
            },
            cards => cards.Count == 1,
            $"single card to appear on board {boardId}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: cardList => cardList is null
                ? "cardList=null"
                : $"cardCount={cardList.Count}, cardIds=[{string.Join(",", cardList.Select(card => card.Id))}]");

        return cardList[0];
    }

    private async Task<CaptureItemDto> WaitForCaptureStatusAsync(Guid itemId, CaptureStatus expectedStatus)
    {
        return await WaitForCaptureStatusAsync(_client, itemId, expectedStatus);
    }

    private static async Task<CaptureItemDto> WaitForCaptureStatusAsync(
        HttpClient client,
        Guid itemId,
        CaptureStatus expectedStatus)
    {
        return await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/capture/items/{itemId}");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
                item.Should().NotBeNull();
                return item!;
            },
            item => item.Status == expectedStatus || (item.Status == CaptureStatus.Failed && expectedStatus != CaptureStatus.Failed),
            $"capture item {itemId} status to become {expectedStatus}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: item => item is null
                ? "item=null"
                : $"status={item.Status}, proposalId={item.Provenance?.ProposalId?.ToString() ?? "null"}, triageRunId={item.Provenance?.TriageRunId?.ToString() ?? "null"}");
    }

    private static async Task ExecuteProposalAsync(HttpClient client, Guid proposalId)
    {
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
