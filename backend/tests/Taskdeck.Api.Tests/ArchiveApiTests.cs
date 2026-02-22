using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ArchiveApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ArchiveApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetArchiveItems_ShouldReturnList()
    {
        await AuthenticateAsync("archive-list");

        var response = await _client.GetAsync("/api/archive/items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task GetArchiveItem_ShouldReturnNotFound_WhenItemDoesNotExist()
    {
        await AuthenticateAsync("archive-item-notfound");

        var response = await _client.GetAsync($"/api/archive/items/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task RestoreArchivedItem_WhenNotFound_ShouldReturnNotFound()
    {
        await AuthenticateAsync("archive-restore-notfound");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: Guid.NewGuid(),
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail
        );

        var response = await _client.PostAsJsonAsync(
            $"/api/archive/Card/{Guid.NewGuid()}/restore",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task RestoreArchivedItem_WithInvalidEntityType_ShouldReturnBadRequest()
    {
        await AuthenticateAsync("archive-restore-invalid-type");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: Guid.NewGuid(),
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail
        );

        var response = await _client.PostAsJsonAsync(
            $"/api/archive/not-a-type/{Guid.NewGuid()}/restore",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task GetArchiveItems_WithLimit_ShouldRespectLimit()
    {
        await AuthenticateAsync("archive-limit");

        var response = await _client.GetAsync("/api/archive/items?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Count.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task GetArchiveItems_ShouldReturnForbidden_WhenFilteringForeignBoard()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "archive-filter-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "archive-filter-owner-board");

        _ = await SeedArchiveItemAsync(ownerBoard.Id, owner.UserId);

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "archive-filter-other");

        var response = await otherClient.GetAsync($"/api/archive/items?boardId={ownerBoard.Id}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetArchiveItem_ShouldReturnForbidden_WhenItemBelongsToDifferentBoard()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "archive-item-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "archive-item-owner-board");

        var archiveItem = await SeedArchiveItemAsync(ownerBoard.Id, owner.UserId);

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "archive-item-other");

        var response = await otherClient.GetAsync($"/api/archive/items/{archiveItem.Id}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task RestoreArchivedItem_ShouldReturnForbidden_WhenItemBelongsToDifferentBoard()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "archive-restore-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "archive-restore-owner-board");

        var archiveItem = await SeedArchiveItemAsync(ownerBoard.Id, owner.UserId, entityType: "card");

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "archive-restore-other");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: ownerBoard.Id,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var response = await otherClient.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
        return payload.User.Id;
    }

    private async Task<ArchiveItem> SeedArchiveItemAsync(
        Guid boardId,
        Guid archivedByUserId,
        string entityType = "board",
        Guid? entityId = null)
    {
        var resolvedEntityId = entityId ?? Guid.NewGuid();
        var archiveItem = new ArchiveItem(
            entityType,
            resolvedEntityId,
            boardId,
            $"archive-{Guid.NewGuid():N}",
            archivedByUserId,
            "{\"name\":\"Seeded Archive Item\"}");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.ArchiveItems.Add(archiveItem);
        await dbContext.SaveChangesAsync();

        return archiveItem;
    }
}
