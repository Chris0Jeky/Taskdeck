using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AuthzRegressionMatrixApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AuthzRegressionMatrixApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedEndpoints_ShouldReturnUnauthorized_WhenNoToken_Matrix()
    {
        var boardId = Guid.NewGuid();
        var accessId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var cases = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>
        {
            ("boards.list", () => _client.GetAsync("/api/boards")),
            ("columns.list", () => _client.GetAsync($"/api/boards/{boardId}/columns")),
            ("cards.list", () => _client.GetAsync($"/api/boards/{boardId}/cards")),
            ("labels.list", () => _client.GetAsync($"/api/boards/{boardId}/labels")),
            ("boardAccess.list", () => _client.GetAsync($"/api/boards/{boardId}/access")),
            ("export.board", () => _client.GetAsync($"/api/export/boards/{boardId}")),
            ("audit.board", () => _client.GetAsync($"/api/audit/boards/{boardId}")),
            ("audit.entity", () => _client.GetAsync($"/api/audit/entities/Card/{entityId}")),
            ("audit.user.me", () => _client.GetAsync("/api/audit/users/me")),
            ("llmQueue.list", () => _client.GetAsync("/api/llm-queue/user")),
            ("users.list", () => _client.GetAsync("/api/users")),
            ("archive.items", () => _client.GetAsync("/api/archive/items")),
            ("chat.sessions", () => _client.GetAsync("/api/llm/chat/sessions")),
            ("automation.list", () => _client.GetAsync("/api/automation/proposals")),
            ("logs.query", () => _client.GetAsync("/api/logs?limit=10")),
            ("logs.stream", () => _client.GetAsync("/api/logs/stream")),
            ("logs.correlation", () => _client.GetAsync($"/api/logs/correlation/{Guid.NewGuid():N}")),
            ("notifications.list", () => _client.GetAsync("/api/notifications")),
            ("notifications.preferences.get", () => _client.GetAsync("/api/notifications/preferences")),
            ("notifications.markRead", () => _client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", content: null)),
            ("ops.run", () => _client.PostAsJsonAsync("/api/ops/cli/run", new RunCommandDto("health.check"))),
            ("ops.getRun", () => _client.GetAsync($"/api/ops/cli/runs/{runId}")),
            ("ops.getRunLogs", () => _client.GetAsync($"/api/ops/cli/runs/{runId}/logs")),
            ("automation.get", () => _client.GetAsync($"/api/automation/proposals/{proposalId}")),
            ("automation.diff", () => _client.GetAsync($"/api/automation/proposals/{proposalId}/diff")),
            ("automation.reject", () => _client.PostAsJsonAsync($"/api/automation/proposals/{proposalId}/reject", new UpdateProposalStatusDto("unauthorized"))),
            ("automation.execute", async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                return await _client.SendAsync(request);
            }),
            ("starterPacks.catalog", () => _client.GetAsync($"/api/boards/{boardId}/starter-packs/catalog")),
            ("starterPacks.apply", () => _client.PostAsJsonAsync(
                $"/api/boards/{boardId}/starter-packs/apply",
                new ApplyStarterPackDto(
                    new StarterPackManifestDto
                    {
                        SchemaVersion = "1.0",
                        PackId = "unauthorized-pack",
                        DisplayName = "Unauthorized Pack",
                        Compatibility = new StarterPackCompatibilityDto
                        {
                            MinTaskdeckVersion = "1.0.0",
                            RequiredFeatures = ["boards"]
                        },
                        Tags = ["starter"],
                        Labels = [],
                        Columns = [new StarterPackColumnDto { Name = "Backlog", Position = 0 }],
                        Templates = [],
                        SeedCards = []
                    },
                    false))),
            ("users.getById", () => _client.GetAsync($"/api/users/{userId}")),
            ("users.update", () => _client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserDto("Unauthorized", "unauthorized@example.com"))),
            ("users.activate", () => _client.PostAsync($"/api/users/{userId}/activate", content: null)),
            ("users.deactivate", () => _client.PostAsync($"/api/users/{userId}/deactivate", content: null)),
            ("boardAccess.update", () => _client.PutAsJsonAsync($"/api/boards/{boardId}/access/{accessId}", new UpdateAccessDto(UserRole.Editor)))
        };

        foreach (var testCase in cases)
        {
            var response = await testCase.Send();
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"endpoint '{testCase.Name}' should require authentication");
            await ApiTestHarness.AssertUnauthorizedAsync(response);
        }
    }

    [Fact]
    public async Task CrossUserEndpoints_ShouldReturnForbidden_WhenCallerIsAuthenticatedButUnauthorized_Matrix()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "matrix-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "matrix-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "matrix-board");
        var createColumnResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Matrix Column", null, null));
        createColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await createColumnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();

        var createCardResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column!.Id, "Matrix Card", null, null, null));
        createCardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await createCardResponse.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();

        var targetUser = await ApiTestHarness.AuthenticateAsync(_factory.CreateClient(), "matrix-target");
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, targetUser.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var grantedAccess = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();
        grantedAccess.Should().NotBeNull();

        var boardAccessUpdateResponse = await outsiderClient.PutAsJsonAsync(
            $"/api/boards/{board.Id}/access/{grantedAccess!.Id}",
            new UpdateAccessDto(UserRole.Editor));

        await ApiTestHarness.AssertForbiddenAsync(boardAccessUpdateResponse);

        var runResponse = await ownerClient.PostAsJsonAsync("/api/ops/cli/run", new RunCommandDto("health.check"));
        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var run = await runResponse.Content.ReadFromJsonAsync<CommandRunDto>();
        run.Should().NotBeNull();

        var getForeignRunLogsResponse = await outsiderClient.GetAsync($"/api/ops/cli/runs/{run!.Id}/logs");
        await ApiTestHarness.AssertForbiddenAsync(getForeignRunLogsResponse);

        var getForeignCorrelationLogsResponse = await outsiderClient.GetAsync($"/api/logs/correlation/{run.CorrelationId}");
        await ApiTestHarness.AssertForbiddenAsync(getForeignCorrelationLogsResponse);

        var queryForeignUserLogsResponse = await outsiderClient.GetAsync($"/api/logs?userId={owner.UserId}");
        await ApiTestHarness.AssertForbiddenAsync(queryForeignUserLogsResponse);

        var getForeignEntityAuditResponse = await outsiderClient.GetAsync($"/api/audit/entities/Card/{card!.Id}");
        await ApiTestHarness.AssertForbiddenAsync(getForeignEntityAuditResponse);

        var addForeignBoardQueueRequestResponse = await outsiderClient.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "cross-user queue payload", board.Id));
        await ApiTestHarness.AssertForbiddenAsync(addForeignBoardQueueRequestResponse);

        var getForeignUserResponse = await outsiderClient.GetAsync($"/api/users/{owner.UserId}");
        await ApiTestHarness.AssertForbiddenAsync(getForeignUserResponse);
    }

    [Fact]
    public async Task MissingResourceEndpoints_ShouldReturnNotFound_ForAuthorizedCallers_Matrix()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "matrix-notfound");
        var board = await ApiTestHarness.CreateBoardAsync(client, "matrix-notfound-board");

        var cases = new List<(string Name, Func<Task<HttpResponseMessage>> Send)>
        {
            ("archive.item.missing", () => client.GetAsync($"/api/archive/items/{Guid.NewGuid()}")),
            ("audit.entity.missing", () => client.GetAsync($"/api/audit/entities/Card/{Guid.NewGuid()}")),
            ("automation.get.missing", () => client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}")),
            ("automation.approve.missing", () => client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve", content: null)),
            ("ops.getRun.missing", () => client.GetAsync($"/api/ops/cli/runs/{Guid.NewGuid()}")),
            ("llmQueue.cancel.missing", () => client.PostAsync($"/api/llm-queue/{Guid.NewGuid()}/cancel", content: null)),
            ("boards.update.missing", () => client.PutAsJsonAsync($"/api/boards/{Guid.NewGuid()}", new UpdateBoardDto("missing", "missing", null))),
            ("boardAccess.update.missing", () => client.PutAsJsonAsync($"/api/boards/{board.Id}/access/{Guid.NewGuid()}", new UpdateAccessDto(UserRole.Editor)))
        };

        foreach (var testCase in cases)
        {
            var response = await testCase.Send();
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"endpoint '{testCase.Name}' should report true missing resources as not found");
            await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
        }

        var getSelfResponse = await client.GetAsync($"/api/users/{user.UserId}");
        getSelfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
