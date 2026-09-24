using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class LlmQueueProcessNextAuthorizationTests : IDisposable
{
    // Each test owns its database; no live worker can consume its pending row.
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory = new();

    [Fact]
    public async Task ProcessNext_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync("/api/llm-queue/process-next", null);
        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Theory]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.Viewer)]
    public async Task ProcessNext_OrdinaryUser_CannotClaimOrReadAnotherUsersRequest(UserRole role)
    {
        var queued = await SeedOtherUsersRequestAsync();
        using var client = _factory.CreateClient();
        await AuthenticateWithRoleAsync(client, role);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.PostAsync("/api/llm-queue/process-next", null);
            await ApiTestHarness.AssertForbiddenAsync(response);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(queued.Id.ToString());
            body.Should().NotContain(queued.UserId.ToString());
            body.Should().NotContain(queued.BoardId!.Value.ToString());
        }

        await AssertStoredStatusAsync(queued.Id, RequestStatus.Pending);
    }

    [Fact]
    public async Task ProcessNext_BoardOwnerWithoutGlobalOperatorRole_ReturnsForbidden()
    {
        var queued = await SeedOtherUsersRequestAsync();
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "claim-board-owner");
        await ApiTestHarness.CreateBoardAsync(client, "unrelated-owned-board");

        using var response = await client.PostAsync("/api/llm-queue/process-next", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
        await AssertStoredStatusAsync(queued.Id, RequestStatus.Pending);
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    public async Task ProcessNext_GlobalOperator_CanClaimPendingRequest(UserRole role)
    {
        var queued = await SeedOtherUsersRequestAsync();
        using var client = _factory.CreateClient();
        await AuthenticateWithRoleAsync(client, role);

        using var response = await client.PostAsync("/api/llm-queue/process-next", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var claimed = await response.Content.ReadFromJsonAsync<LlmRequestDto>();
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(queued.Id);
        claimed.UserId.Should().Be(queued.UserId);
        claimed.Status.Should().Be(RequestStatus.Processing);
        await AssertStoredStatusAsync(queued.Id, RequestStatus.Processing);
    }

    private async Task<LlmRequestDto> SeedOtherUsersRequestAsync()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "claim-request-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "private-queue-board");
        using var response = await client.PostAsJsonAsync(
            "/api/llm-queue", new CreateLlmRequestDto("summarize", "private payload", board.Id));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var queued = await response.Content.ReadFromJsonAsync<LlmRequestDto>();
        queued.Should().NotBeNull();
        return queued!;
    }

    private async Task AuthenticateWithRoleAsync(HttpClient client, UserRole role)
    {
        var identity = await ApiTestHarness.AuthenticateAsync(client, "claim-operator");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == identity.UserId);
            user.UpdateDefaultRole(role);
            await db.SaveChangesAsync();
        }

        // The policy uses the signed global role, not a mutable board membership.
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginDto(identity.Username, "password123"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        login.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
    }

    private async Task AssertStoredStatusAsync(Guid requestId, RequestStatus expected)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var stored = await db.LlmRequests.AsNoTracking().SingleAsync(request => request.Id == requestId);
        stored.Status.Should().Be(expected);
    }

    public void Dispose() => _factory.Dispose();
}
