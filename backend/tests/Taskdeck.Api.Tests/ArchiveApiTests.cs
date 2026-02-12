using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ArchiveApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ArchiveApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetArchiveItems_ShouldReturnList()
    {
        var response = await _client.GetAsync("/api/archive/items");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task GetArchiveItem_ShouldReturnNotFound_WhenItemDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/archive/items/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task RestoreArchivedItem_WhenNotFound_ShouldReturnNotFound()
    {
        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: Guid.NewGuid(),
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail
        );

        var response = await _client.PostAsJsonAsync(
            $"/api/archive/Card/{Guid.NewGuid()}/restore?restoredByUserId={Guid.NewGuid()}",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetArchiveItems_WithLimit_ShouldRespectLimit()
    {
        var response = await _client.GetAsync("/api/archive/items?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Count.Should().BeLessOrEqualTo(5);
    }
}
