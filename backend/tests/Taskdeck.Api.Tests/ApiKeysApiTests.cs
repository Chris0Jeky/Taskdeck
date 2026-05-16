using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ApiKeysApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiKeysApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiKeyEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.GetAsync("/api/apikeys"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("test-key")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.DeleteAsync($"/api/apikeys/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task List_ShouldReturnEmptyList_ForNewUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikeys-empty");

        var response = await client.GetAsync("/api/apikeys");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        result.Should().NotBeNull();
        result!.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturn201_WithPlaintextKey()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikeys-create");

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("my-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        result.Should().NotBeNull();
        result!.Key.Should().StartWith("tdsk_");
        result.Name.Should().Be("my-key");
        result.KeyPrefix.Should().NotBeNullOrWhiteSpace();
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_ShouldReturn400_WithEmptyName()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikeys-empty-name");

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FullLifecycle_CreateListRevoke()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikeys-lifecycle");

        var createResponse = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("lifecycle-key", 30));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        created.Should().NotBeNull();
        created!.ExpiresAt.Should().NotBeNull();

        var listResponse = await client.GetAsync("/api/apikeys");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        listed.Should().NotBeNull();
        listed!.Keys.Should().ContainSingle(k => k.Id == created.Id);
        listed.Keys[0].IsActive.Should().BeTrue();

        var revokeResponse = await client.DeleteAsync($"/api/apikeys/{created.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterRevoke = await client.GetAsync("/api/apikeys");
        var listedAfterRevoke = await listAfterRevoke.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        listedAfterRevoke.Should().NotBeNull();
        listedAfterRevoke!.Keys.Should().ContainSingle(k => k.Id == created.Id);
        listedAfterRevoke.Keys[0].IsActive.Should().BeFalse();
        listedAfterRevoke.Keys[0].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_ShouldReturn404_ForNonexistentKey()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikeys-revoke-404");

        var response = await client.DeleteAsync($"/api/apikeys/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CrossUserIsolation_ShouldNotSeeOtherUsersKeys()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "apikeys-user-a");

        var createResponse = await clientA.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("user-a-key"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdKey = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "apikeys-user-b");

        var listResponse = await clientB.GetAsync("/api/apikeys");
        var listed = await listResponse.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        listed.Should().NotBeNull();
        listed!.Keys.Should().NotContain(k => k.Id == createdKey!.Id);
    }

    [Fact]
    public async Task CrossUserIsolation_ShouldNotRevokeOtherUsersKeys()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "apikeys-revoke-a");

        var createResponse = await clientA.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("revoke-target"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdKey = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "apikeys-revoke-b");

        var revokeResponse = await clientB.DeleteAsync($"/api/apikeys/{createdKey!.Id}");

        revokeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }
}
