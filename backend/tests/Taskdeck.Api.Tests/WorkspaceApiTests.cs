using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkspaceApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WorkspaceApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WorkspaceEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync("/api/workspace/home"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync("/api/workspace/preferences"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.PutAsJsonAsync(
            "/api/workspace/preferences",
            new UpdateWorkspacePreferenceDto(WorkspaceModeContract.Workbench)));
    }

    [Fact]
    public async Task PreferencesEndpoints_ShouldGetAndUpdateCurrentUserPreferences()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "workspace-preferences");

        var getResponse = await client.GetAsync("/api/workspace/preferences");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var preferences = await getResponse.Content.ReadFromJsonAsync<WorkspacePreferenceDto>();
        preferences.Should().NotBeNull();
        preferences!.UserId.Should().Be(user.UserId);
        preferences.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/workspace/preferences",
            new UpdateWorkspacePreferenceDto(WorkspaceModeContract.Agent));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<WorkspacePreferenceDto>();
        updated.Should().NotBeNull();
        updated!.WorkspaceMode.Should().Be(WorkspaceModeContract.Agent);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persistedPreference = dbContext.UserPreferences.Single(preference => preference.UserId == user.UserId);
        persistedPreference.WorkspaceMode.Should().Be(WorkspaceMode.Agent);
    }

    [Fact]
    public async Task WorkspaceEndpoints_ShouldHandleConcurrentFirstLoadPreferenceCreation()
    {
        using var seedClient = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(seedClient, "workspace-first-load");

        using var homeClient = _factory.CreateClient();
        using var preferenceClient = _factory.CreateClient();
        homeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        preferenceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(index =>
                index % 2 == 0
                    ? homeClient.GetAsync("/api/workspace/home")
                    : preferenceClient.GetAsync("/api/workspace/preferences")));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.UserPreferences.Count(preference => preference.UserId == user.UserId).Should().Be(1);
    }

    [Fact]
    public async Task Home_ShouldReturnCurrentUserSummaryOnly()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "workspace-owner");
        await ApiTestHarness.AuthenticateAsync(otherClient, "workspace-other");

        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "workspace-home-owner");
        _ = await ApiTestHarness.CreateBoardAsync(otherClient, "workspace-home-other");

        await SeedWorkspaceDataAsync(owner.UserId, ownerBoard.Id);

        var response = await ownerClient.GetAsync("/api/workspace/home");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var home = await response.Content.ReadFromJsonAsync<WorkspaceHomeDto>();
        home.Should().NotBeNull();
        home!.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        home.IsFirstRun.Should().BeFalse();
        home.Workload.CapturesNeedingTriage.Should().Be(2);
        home.Workload.CapturesInProgress.Should().Be(1);
        home.Workload.CapturesReadyForFollowUp.Should().Be(1);
        home.Workload.ProposalsPendingReview.Should().Be(2);
        home.Boards.TotalBoards.Should().Be(1);
        home.Boards.RecentBoards.Should().ContainSingle(board => board.Id == ownerBoard.Id);
        home.RecommendedActions.Select(action => action.ActionId).Should().Contain("review-proposals");
        home.RecommendedActions.Select(action => action.ActionId).Should().Contain("triage-captures");
    }

    private async Task SeedWorkspaceDataAsync(Guid userId, Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        dbContext.LlmRequests.AddRange(
            CreateCaptureRequest(userId, boardId, RequestStatus.Pending),
            CreateCaptureRequest(userId, boardId, RequestStatus.Processing),
            CreateCaptureRequest(userId, boardId, RequestStatus.Completed),
            CreateCaptureRequest(userId, boardId, RequestStatus.Failed),
            CreateCaptureRequest(userId, boardId, RequestStatus.Completed, Guid.NewGuid()));

        dbContext.AutomationProposals.AddRange(
            new AutomationProposal(
                ProposalSourceType.Queue,
                userId,
                "Pending workspace proposal",
                RiskLevel.Low,
                Guid.NewGuid().ToString("N"),
                boardId),
            new AutomationProposal(
                ProposalSourceType.Chat,
                userId,
                "Second pending workspace proposal",
                RiskLevel.Medium,
                Guid.NewGuid().ToString("N"),
                boardId));

        await dbContext.SaveChangesAsync();
    }

    private static LlmRequest CreateCaptureRequest(Guid userId, Guid boardId, RequestStatus status, Guid? proposalId = null)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            $"Workspace capture for {status}");
        var request = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload),
            boardId);

        if (proposalId.HasValue)
        {
            var payloadWithProvenance = CaptureRequestContract.WithProvenance(
                payload,
                request.Id,
                proposalId: proposalId,
                requestedByUserId: userId,
                correlationId: Guid.NewGuid().ToString("N"),
                sourceSurface: "capture",
                boardId: boardId);
            request.UpdatePayload(CaptureRequestContract.SerializePayload(payloadWithProvenance));
        }

        switch (status)
        {
            case RequestStatus.Pending:
                break;
            case RequestStatus.Processing:
                request.MarkAsProcessing();
                break;
            case RequestStatus.Completed:
                request.MarkAsProcessing();
                request.MarkAsCompleted();
                break;
            case RequestStatus.Failed:
                request.MarkAsFailed("capture failed");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported capture status for workspace API test setup.");
        }

        return request;
    }
}
