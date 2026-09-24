using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Access revocation must evict live SignalR board-group membership (#3420/#3407).
/// </summary>
public class BoardRevocationEvictionApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BoardRevocationEvictionApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RevokedMember_ShouldStopReceivingBoardMutations()
    {
        // #3420/#3407: revoke removes the BoardAccess row but leaves live
        // connections in the SignalR board group.
        var ownerClient = _factory.CreateClient();
        var memberClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "evict-owner");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "evict-member");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "evict-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, member.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();

        await using var connection = CreateHubConnection(member.Token);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);

        var revokeResponse = await ownerClient.DeleteAsync($"/api/boards/{board.Id}/access/{access!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mutationReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<object>("boardMutation", _ => mutationReceived.TrySetResult());

        var colResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "EvictCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();
        var cardResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "post-revoke card", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var completed = await Task.WhenAny(mutationReceived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().NotBe(mutationReceived.Task, "a revoked member must be evicted from the board group");
    }

    [Fact]
    public async Task LeaveBoard_ShouldSucceed_AfterAccessRevoked()
    {
        // #3420/#3407: LeaveBoard checks read access before removal, so a revoked
        // member can never voluntarily leave. Leaving is always safe.
        var ownerClient = _factory.CreateClient();
        var memberClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "evict-leave-owner");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "evict-leave-member");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "evict-leave-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, member.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();

        await using var connection = CreateHubConnection(member.Token);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);

        var revokeResponse = await ownerClient.DeleteAsync($"/api/boards/{board.Id}/access/{access!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var leaveAction = async () => await connection.InvokeAsync("LeaveBoard", board.Id);
        await leaveAction.Should().NotThrowAsync();
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
