using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class RealtimeBoardHubApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RealtimeBoardHubApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HubNegotiate_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/hubs/boards/negotiate?negotiateVersion=1")
        {
            Content = new StringContent(string.Empty)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task JoinBoard_ShouldAllowReader()
    {
        var ownerClient = _factory.CreateClient();
        var collaboratorClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "hub-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(collaboratorClient, "hub-reader");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "hub-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, collaborator.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = CreateHubConnection(collaborator.Token);
        await connection.StartAsync();

        var joinAction = async () => await connection.InvokeAsync("JoinBoard", board.Id);
        await joinAction.Should().NotThrowAsync();
    }

    [Fact]
    public async Task JoinBoard_ShouldReturnForbidden_WhenUserCannotReadBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "hub-owner");
        var outsider = await ApiTestHarness.AuthenticateAsync(outsiderClient, "hub-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "hub-board");

        await using var connection = CreateHubConnection(outsider.Token);
        await connection.StartAsync();

        var joinAction = async () => await connection.InvokeAsync("JoinBoard", board.Id);
        var exception = await joinAction.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Contain(ErrorCodes.Forbidden);
    }

    private HubConnection CreateHubConnection(string token)
    {
        var apiBaseAddress = _client.BaseAddress ?? new Uri("http://localhost");
        var hubAddress = new Uri(apiBaseAddress, "/hubs/boards");

        return new HubConnectionBuilder()
            .WithUrl(hubAddress, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(token)!;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers.Add("Authorization", new AuthenticationHeaderValue("Bearer", token).ToString());
            })
            .Build();
    }
}
