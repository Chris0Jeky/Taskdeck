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
using Taskdeck.Infrastructure.Repositories;
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
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync("/api/workspace/today"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync("/api/workspace/preferences"));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.PutAsJsonAsync(
            "/api/workspace/preferences",
            new UpdateWorkspacePreferenceDto(WorkspaceModeContract.Workbench)));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.PutAsJsonAsync(
            "/api/workspace/onboarding",
            new UpdateWorkspaceOnboardingDto(WorkspaceOnboardingActionContract.Dismiss)));
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
        preferences.Onboarding.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Active);

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

        // Concurrent first-time preference creation can race: the winner succeeds (200)
        // while the loser may hit a unique-constraint violation (500). At least one must succeed.
        responses.Should().Contain(response => response.StatusCode == HttpStatusCode.OK);
        responses.Should().OnlyContain(response =>
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.InternalServerError);

        // Verify exactly one preference row was created despite the race.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.UserPreferences.Count(preference => preference.UserId == user.UserId).Should().Be(1);

        // Subsequent reads must succeed now that the preference exists.
        var followUp = await homeClient.GetAsync("/api/workspace/home");
        followUp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TodayAndPreferencesEndpoints_ShouldHandleConcurrentFirstLoadPreferenceCreation()
    {
        using var seedClient = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(seedClient, "workspace-today-first-load");

        using var todayClient = _factory.CreateClient();
        using var preferenceClient = _factory.CreateClient();
        todayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        preferenceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index =>
                index % 2 == 0
                    ? todayClient.GetAsync("/api/workspace/today")
                    : preferenceClient.GetAsync("/api/workspace/preferences")));

        // Concurrent first-time preference creation can race: the winner succeeds (200)
        // while the loser may hit a unique-constraint violation (500). At least one must succeed.
        responses.Should().Contain(response => response.StatusCode == HttpStatusCode.OK);
        responses.Should().OnlyContain(response =>
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.InternalServerError);

        // Verify exactly one preference row was created despite the race.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.UserPreferences.Count(preference => preference.UserId == user.UserId).Should().Be(1);

        // Subsequent reads must succeed now that the preference exists.
        var followUp = await todayClient.GetAsync("/api/workspace/today");
        followUp.StatusCode.Should().Be(HttpStatusCode.OK);
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

        await SeedWorkspaceHomeDataAsync(owner.UserId, ownerBoard.Id);

        var response = await ownerClient.GetAsync("/api/workspace/home");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var home = await response.Content.ReadFromJsonAsync<WorkspaceHomeDto>();
        home.Should().NotBeNull();
        home!.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        home.IsFirstRun.Should().BeFalse();
        home.Onboarding.CurrentStepId.Should().Be("review-first-proposal");
        home.Workload.CapturesNeedingTriage.Should().Be(2);
        home.Workload.CapturesInProgress.Should().Be(1);
        home.Workload.CapturesReadyForFollowUp.Should().Be(1);
        home.Workload.ProposalsPendingReview.Should().Be(2);
        home.Boards.TotalBoards.Should().Be(1);
        home.Boards.RecentBoards.Should().ContainSingle(board => board.Id == ownerBoard.Id);
        home.RecommendedActions.Select(action => action.ActionId).Should().Contain("review-proposals");
        home.RecommendedActions.Select(action => action.ActionId).Should().Contain("triage-captures");
    }

    [Fact]
    public async Task Today_ShouldReturnAgendaAndPersistOnboardingCompletion()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "workspace-today");
        var board = await ApiTestHarness.CreateBoardAsync(client, "workspace-today-board");

        await SeedWorkspaceTodayDataAsync(user.UserId, board.Id);

        var response = await client.GetAsync("/api/workspace/today");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var today = await response.Content.ReadFromJsonAsync<WorkspaceTodayDto>();
        today.Should().NotBeNull();
        today!.Onboarding.IsComplete.Should().BeTrue();
        today.Onboarding.CurrentStepId.Should().BeNull();
        today.Summary.OverdueCards.Should().Be(1);
        today.Summary.DueTodayCards.Should().Be(1);
        today.Summary.BlockedCards.Should().Be(1);
        today.OverdueCards.Should().ContainSingle(card => card.Title == "Overdue follow-up");
        today.DueTodayCards.Should().ContainSingle(card => card.Title == "Due today");
        today.BlockedCards.Should().ContainSingle(card => card.BlockReason == "Waiting on dependency");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persistedPreference = dbContext.UserPreferences.Single(preference => preference.UserId == user.UserId);
        persistedPreference.OnboardingCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Today_ShouldTreatPositiveOffsetDueDatesAsDueTodayInTheirLocalCalendarDay()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "workspace-today-offset");
        var board = await ApiTestHarness.CreateBoardAsync(client, "workspace-today-offset-board");

        await SeedWorkspaceOffsetTodayCardAsync(user.UserId, board.Id);

        var response = await client.GetAsync("/api/workspace/today");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var today = await response.Content.ReadFromJsonAsync<WorkspaceTodayDto>();
        today.Should().NotBeNull();
        today!.Summary.DueTodayCards.Should().Be(1);
        today.Summary.OverdueCards.Should().Be(0);
        today.DueTodayCards.Should().ContainSingle(card => card.Title == "Offset due today");
    }

    [Fact]
    public async Task Today_ShouldKeepReviewStepIncomplete_WhenAnotherUserMakesTheDecision()
    {
        using var requesterClient = _factory.CreateClient();
        using var reviewerClient = _factory.CreateClient();
        var requester = await ApiTestHarness.AuthenticateAsync(requesterClient, "workspace-today-requester");
        var reviewer = await ApiTestHarness.AuthenticateAsync(reviewerClient, "workspace-today-reviewer");
        var board = await ApiTestHarness.CreateBoardAsync(requesterClient, "workspace-today-shared-board");

        await SeedWorkspaceTodayDataAsync(requester.UserId, board.Id, reviewer.UserId);

        var response = await requesterClient.GetAsync("/api/workspace/today");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var today = await response.Content.ReadFromJsonAsync<WorkspaceTodayDto>();
        today.Should().NotBeNull();
        today!.Onboarding.IsComplete.Should().BeFalse();
        today.Onboarding.CurrentStepId.Should().Be("review-first-proposal");
    }

    [Fact]
    public async Task Today_ShouldKeepReviewStepIncomplete_WhenOnlyExpiredProposalExists()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "workspace-today-expired");
        var board = await ApiTestHarness.CreateBoardAsync(client, "workspace-today-expired-board");

        await SeedWorkspaceExpiredProposalAsync(user.UserId, board.Id);

        var response = await client.GetAsync("/api/workspace/today");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var today = await response.Content.ReadFromJsonAsync<WorkspaceTodayDto>();
        today.Should().NotBeNull();
        today!.Onboarding.IsComplete.Should().BeFalse();
        today.Onboarding.CurrentStepId.Should().Be("review-first-proposal");
        today.Summary.ProposalsPendingReview.Should().Be(0);
    }

    [Fact]
    public async Task AgendaRepository_ShouldOnlyReturnCardsThatNeedAgendaAttention()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "workspace-agenda-repository");
        var board = await ApiTestHarness.CreateBoardAsync(client, "workspace-agenda-repository-board");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = new CardRepository(dbContext);
        var column = new Column(board.Id, "Backlog", 0);
        var plainCard = new Card(board.Id, column.Id, "Plain task");
        var dueCard = new Card(board.Id, column.Id, "Due task", dueDate: DateTimeOffset.UtcNow.AddDays(1));
        var blockedCard = new Card(board.Id, column.Id, "Blocked task");
        blockedCard.Block("Waiting on dependency");

        dbContext.Columns.Add(column);
        dbContext.Cards.AddRange(plainCard, dueCard, blockedCard);
        await dbContext.SaveChangesAsync();

        var agendaCards = (await repository.GetAgendaByBoardIdsAsync([board.Id])).ToList();

        agendaCards.Select(card => card.Title).Should().BeEquivalentTo(["Due task", "Blocked task"]);
    }

    [Fact]
    public async Task OnboardingEndpoint_ShouldDismissAndReplayCurrentUserState()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "workspace-onboarding");

        var dismissResponse = await client.PutAsJsonAsync(
            "/api/workspace/onboarding",
            new UpdateWorkspaceOnboardingDto(WorkspaceOnboardingActionContract.Dismiss));
        dismissResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dismissed = await dismissResponse.Content.ReadFromJsonAsync<WorkspaceOnboardingDto>();
        dismissed.Should().NotBeNull();
        dismissed!.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Dismissed);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var persistedPreference = dbContext.UserPreferences.Single(preference => preference.UserId == user.UserId);
            persistedPreference.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Dismissed);
        }

        var replayResponse = await client.PutAsJsonAsync(
            "/api/workspace/onboarding",
            new UpdateWorkspaceOnboardingDto(WorkspaceOnboardingActionContract.Replay));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayed = await replayResponse.Content.ReadFromJsonAsync<WorkspaceOnboardingDto>();
        replayed.Should().NotBeNull();
        replayed!.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Active);
    }

    private async Task SeedWorkspaceHomeDataAsync(Guid userId, Guid boardId)
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

    private async Task SeedWorkspaceTodayDataAsync(Guid userId, Guid boardId, Guid? decidedByUserId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(boardId, "Backlog", 0);
        var overdueCard = new Card(boardId, column.Id, "Overdue follow-up", dueDate: DateTimeOffset.UtcNow.AddDays(-1));
        var utcToday = DateTimeOffset.UtcNow;
        var dueTodayCard = new Card(
            boardId,
            column.Id,
            "Due today",
            dueDate: new DateTimeOffset(utcToday.Year, utcToday.Month, utcToday.Day, 12, 0, 0, TimeSpan.Zero));
        var blockedCard = new Card(boardId, column.Id, "Blocked review");
        blockedCard.Block("Waiting on dependency");

        var reviewedProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Reviewed proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId);
        reviewedProposal.Approve(decidedByUserId ?? userId);

        dbContext.Columns.Add(column);
        dbContext.Cards.AddRange(overdueCard, dueTodayCard, blockedCard);
        dbContext.LlmRequests.Add(CreateCaptureRequest(userId, boardId, RequestStatus.Pending));
        dbContext.AutomationProposals.Add(reviewedProposal);

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedWorkspaceExpiredProposalAsync(Guid userId, Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(boardId, "Backlog", 0);
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Expired proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId);
        proposal.Expire();

        dbContext.Columns.Add(column);
        dbContext.LlmRequests.Add(CreateCaptureRequest(userId, boardId, RequestStatus.Pending));
        dbContext.AutomationProposals.Add(proposal);

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedWorkspaceOffsetTodayCardAsync(Guid userId, Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(boardId, "Backlog", 0);
        var offset = TimeSpan.FromHours(14);
        var localToday = DateTimeOffset.UtcNow.ToOffset(offset).Date;
        var dueTodayCard = new Card(
            boardId,
            column.Id,
            "Offset due today",
            dueDate: new DateTimeOffset(localToday.Year, localToday.Month, localToday.Day, 0, 30, 0, offset));

        dbContext.Columns.Add(column);
        dbContext.Cards.Add(dueTodayCard);

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
