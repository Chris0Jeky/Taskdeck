using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class BoardAccessApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardAccessApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BoardAccessEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var boardId = Guid.NewGuid();
        var accessId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.GetAsync($"/api/boards/{boardId}/access"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsJsonAsync($"/api/boards/{boardId}/access",
                new GrantAccessDto(boardId, Guid.NewGuid(), UserRole.Editor)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PutAsJsonAsync($"/api/boards/{boardId}/access/{accessId}",
                new UpdateAccessDto(UserRole.Viewer)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.DeleteAsync($"/api/boards/{boardId}/access/{accessId}"));
    }

    [Fact]
    public async Task GetBoardAccess_ShouldReturnOk_ForBoardOwner()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "access-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "access-test");

        var response = await client.GetAsync($"/api/boards/{board.Id}/access");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoardAccess_ShouldReturnForbiddenOrNotFound_ForNonOwner()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "access-owner-a");
        var board = await ApiTestHarness.CreateBoardAsync(clientA, "access-isolation");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "access-other-b");

        var response = await clientB.GetAsync($"/api/boards/{board.Id}/access");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GrantAccess_ShouldReturnOk_WhenOwnerGrantsAccess()
    {
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "access-grant-owner");
        var board = await ApiTestHarness.CreateBoardAsync(clientA, "access-grant");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "access-grant-target");

        var response = await clientA.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, userB.UserId, UserRole.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GrantAccess_ShouldReturnForbiddenOrNotFound_ForNonOwner()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "access-grant-crossuser-a");
        var board = await ApiTestHarness.CreateBoardAsync(clientA, "access-grant-cross");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "access-grant-crossuser-b");

        using var clientC = _factory.CreateClient();
        var userC = await ApiTestHarness.AuthenticateAsync(clientC, "access-grant-crossuser-c");

        var response = await clientB.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, userC.UserId, UserRole.Editor));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GrantAccess_ShouldReturnError_ForNonexistentBoard()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "access-grant-noboard");

        var response = await client.PostAsJsonAsync(
            $"/api/boards/{Guid.NewGuid()}/access",
            new GrantAccessDto(Guid.NewGuid(), Guid.NewGuid(), UserRole.Editor));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAccess_ShouldReturnError_ForNonexistentAccess()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "access-update-notfound");
        var board = await ApiTestHarness.CreateBoardAsync(client, "access-update-err");

        var response = await client.PutAsJsonAsync(
            $"/api/boards/{board.Id}/access/{Guid.NewGuid()}",
            new UpdateAccessDto(UserRole.Viewer));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAccess_ShouldReturnForbiddenOrNotFound_ForNonOwner()
    {
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "access-update-cross-a");
        var board = await ApiTestHarness.CreateBoardAsync(clientA, "access-update-cross");

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "access-update-cross-b");

        var response = await clientB.PutAsJsonAsync(
            $"/api/boards/{board.Id}/access/{Guid.NewGuid()}",
            new UpdateAccessDto(UserRole.Viewer));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeAccess_ShouldReturnError_ForNonexistentAccess()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "access-revoke-notfound");
        var board = await ApiTestHarness.CreateBoardAsync(client, "access-revoke");

        var response = await client.DeleteAsync($"/api/boards/{board.Id}/access/{Guid.NewGuid()}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, response.StatusCode);
    }
}
