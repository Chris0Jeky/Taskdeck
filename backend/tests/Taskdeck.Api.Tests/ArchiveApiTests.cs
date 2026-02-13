using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
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
    public async Task GetArchiveItems_WithLimit_ShouldRespectLimit()
    {
        await AuthenticateAsync("archive-limit");

        var response = await _client.GetAsync("/api/archive/items?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Count.Should().BeLessOrEqualTo(5);
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
}
