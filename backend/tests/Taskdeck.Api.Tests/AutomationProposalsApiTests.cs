using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AutomationProposalsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AutomationProposalsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateProposal_ThenGetProposal_ShouldReturnCreatedProposal()
    {
        var userId = await AuthenticateAsync("automation-create");
        var boardId = await CreateOwnedBoardAsync(userId);
        var correlationId = Guid.NewGuid().ToString();

        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: "Test automation proposal",
            RiskLevel: RiskLevel.Low,
            CorrelationId: correlationId,
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "CreateCard",
                    TargetType: "Card",
                    Parameters: "{\"title\":\"Test Card\"}",
                    IdempotencyKey: Guid.NewGuid().ToString()
                )
            }
        );

        var createResponse = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProposal = await createResponse.Content.ReadFromJsonAsync<ProposalDto>();
        createdProposal.Should().NotBeNull();
        createdProposal!.Summary.Should().Be(createRequest.Summary);
        createdProposal.Status.Should().Be(ProposalStatus.PendingReview);
        createdProposal.RiskLevel.Should().Be(RiskLevel.Low);
        createdProposal.Operations.Should().HaveCount(1);
        createdProposal.Presentation.PlainSummary.Should().Contain("This would");
        createdProposal.Presentation.SourceCue.Should().Be("Created from an automation chat session.");
        createdProposal.Presentation.OperationHeadlines.Should().ContainSingle()
            .Which.Should().Contain("Create card");

        var getResponse = await _client.GetAsync($"/api/automation/proposals/{createdProposal.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrievedProposal = await getResponse.Content.ReadFromJsonAsync<ProposalDto>();
        retrievedProposal.Should().NotBeNull();
        retrievedProposal!.Id.Should().Be(createdProposal.Id);
        retrievedProposal.Summary.Should().Be(createRequest.Summary);
        retrievedProposal.Presentation.AffectedEntities.Should().ContainSingle(entity =>
            entity.EntityType == "Card" &&
            entity.ChangeCount == 1);
    }

    [Fact]
    public async Task GetProposals_WithFilters_ShouldReturnFilteredResults()
    {
        var userId = await AuthenticateAsync("automation-filters");
        var boardId = await CreateOwnedBoardAsync(userId);

        var proposal1 = await CreateTestProposal(userId, boardId, RiskLevel.Low);
        var proposal2 = await CreateTestProposal(userId, boardId, RiskLevel.High);

        var response = await _client.GetAsync($"/api/automation/proposals?boardId={boardId}&status={ProposalStatus.PendingReview}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposals = await response.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();
        proposals.Should().NotBeEmpty();
        proposals.Should().Contain(p => p.Id == proposal1.Id);
        proposals.Should().Contain(p => p.Id == proposal2.Id);
    }

    [Fact]
    public async Task GetProposals_WithStatusAndLimit_ShouldReturnCallerScopedResults()
    {
        var callerClient = _factory.CreateClient();
        var otherClient = _factory.CreateClient();

        var caller = await ApiTestHarness.AuthenticateAsync(callerClient, "automation-list-caller");
        var other = await ApiTestHarness.AuthenticateAsync(otherClient, "automation-list-other");
        var callerBoard = await ApiTestHarness.CreateBoardAsync(callerClient, "automation-list-caller-board");
        var callerProposal = await CreateTestProposalAsync(callerClient, caller.UserId, callerBoard.Id, RiskLevel.Low);

        var otherBoard = await ApiTestHarness.CreateBoardAsync(otherClient, "automation-list-other-board");
        _ = await CreateTestProposalAsync(otherClient, other.UserId, otherBoard.Id, RiskLevel.Low);

        var response = await callerClient.GetAsync("/api/automation/proposals?status=PendingReview&limit=1");
        var errorBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {errorBody}");
        var proposals = await response.Content.ReadFromJsonAsync<List<ProposalDto>>();
        var scopedProposals = proposals ?? throw new InvalidOperationException("Proposal list should not be null.");
        scopedProposals.Should().ContainSingle();
        scopedProposals[0].Id.Should().Be(callerProposal.Id);
    }

    [Fact]
    public async Task GetProposals_ShouldExcludeBoardScopedProposals_WhenCallerNoLongerHasBoardReadAccess()
    {
        var ownerClient = _factory.CreateClient();
        var collaboratorClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-list-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(collaboratorClient, "automation-list-collaborator");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-list-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, collaborator.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();
        access.Should().NotBeNull();

        var boardScopedProposal = await CreateTestProposalAsync(collaboratorClient, collaborator.UserId, board.Id, RiskLevel.Low);
        var userScopedCreateResponse = await collaboratorClient.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                SourceType: ProposalSourceType.Chat,
                RequestedByUserId: collaborator.UserId,
                Summary: "User scoped proposal",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString()));
        userScopedCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var userScopedProposal = await userScopedCreateResponse.Content.ReadFromJsonAsync<ProposalDto>();
        userScopedProposal.Should().NotBeNull();

        var revokeResponse = await ownerClient.DeleteAsync($"/api/boards/{board.Id}/access/{access!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await collaboratorClient.GetAsync("/api/automation/proposals?status=PendingReview&limit=10");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = await listResponse.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();
        proposals!.Should().Contain(p => p.Id == userScopedProposal!.Id);
        proposals.Should().NotContain(p => p.Id == boardScopedProposal.Id);
    }

    [Fact]
    public async Task GetProposals_WithSmallLimit_ShouldReturnReadableProposalAfterAuthorizationFilter()
    {
        var ownerClient = _factory.CreateClient();
        var collaboratorClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-limit-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(collaboratorClient, "automation-limit-collaborator");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-limit-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, collaborator.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();
        access.Should().NotBeNull();

        var userScopedCreateResponse = await collaboratorClient.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                SourceType: ProposalSourceType.Chat,
                RequestedByUserId: collaborator.UserId,
                Summary: "Oldest readable proposal",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString()));
        userScopedCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var userScopedProposal = await userScopedCreateResponse.Content.ReadFromJsonAsync<ProposalDto>();
        userScopedProposal.Should().NotBeNull();

        _ = await CreateTestProposalAsync(collaboratorClient, collaborator.UserId, board.Id, RiskLevel.Low);
        _ = await CreateTestProposalAsync(collaboratorClient, collaborator.UserId, board.Id, RiskLevel.Low);
        _ = await CreateTestProposalAsync(collaboratorClient, collaborator.UserId, board.Id, RiskLevel.Low);

        var revokeResponse = await ownerClient.DeleteAsync($"/api/boards/{board.Id}/access/{access!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await collaboratorClient.GetAsync("/api/automation/proposals?status=PendingReview&limit=1");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = await listResponse.Content.ReadFromJsonAsync<List<ProposalDto>>();
        var scopedProposals = proposals ?? throw new InvalidOperationException("Proposal list should not be null.");
        scopedProposals.Should().ContainSingle();
        scopedProposals[0].Id.Should().Be(userScopedProposal!.Id);
    }

    [Fact]
    public async Task ApproveProposal_ShouldUpdateStatus()
    {
        var userId = await AuthenticateAsync("automation-approve");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedProposal = await approveResponse.Content.ReadFromJsonAsync<ProposalDto>();
        approvedProposal.Should().NotBeNull();
        approvedProposal!.Status.Should().Be(ProposalStatus.Approved);
        approvedProposal.DecidedByUserId.Should().Be(userId);
        approvedProposal.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectProposal_ShouldUpdateStatus()
    {
        var userId = await AuthenticateAsync("automation-reject");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var rejectDto = new UpdateProposalStatusDto(Reason: "Not needed");
        var rejectResponse = await _client.PostAsJsonAsync($"/api/automation/proposals/{proposal.Id}/reject", rejectDto);
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectedProposal = await rejectResponse.Content.ReadFromJsonAsync<ProposalDto>();
        rejectedProposal.Should().NotBeNull();
        rejectedProposal!.Status.Should().Be(ProposalStatus.Rejected);
        rejectedProposal.DecidedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task ExecuteProposal_WhenApproved_ShouldMarkAsApplied()
    {
        var userId = await AuthenticateAsync("automation-exec-applied");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await _client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executedProposal = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        executedProposal.Should().NotBeNull();
        executedProposal!.Status.Should().Be(ProposalStatus.Applied);
        executedProposal.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteProposal_ShouldNotMutateUnrelatedCapture_WhenQueueSourceReferenceIsCallerSupplied()
    {
        var proposalClient = _factory.CreateClient();
        var captureOwnerClient = _factory.CreateClient();

        var proposalUser = await ApiTestHarness.AuthenticateAsync(proposalClient, "automation-queue-source-proposal");
        var captureOwner = await ApiTestHarness.AuthenticateAsync(captureOwnerClient, "automation-queue-source-capture");
        var proposalBoard = await ApiTestHarness.CreateBoardAsync(proposalClient, "automation-queue-source-board");
        var captureBoard = await ApiTestHarness.CreateBoardAsync(captureOwnerClient, "automation-queue-source-capture-board");

        var createCaptureResponse = await captureOwnerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(captureBoard.Id, "capture payload that should stay unattached"));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdCapture = await createCaptureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        createdCapture.Should().NotBeNull();

        using var initialScope = _factory.Services.CreateScope();
        var initialDb = initialScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var initialPersistedCapture = await initialDb.LlmRequests.FindAsync(createdCapture!.Id);
        initialPersistedCapture.Should().NotBeNull();
        var initialPayload = CaptureRequestContract.ParsePayload(initialPersistedCapture!.Payload, allowServerAttributionFields: true);
        initialPayload.IsSuccess.Should().BeTrue();
        initialPayload.Value.Provenance.Should().NotBeNull();
        var initialProvenance = initialPayload.Value.Provenance!;

        var createProposalResponse = await proposalClient.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                SourceType: ProposalSourceType.Queue,
                RequestedByUserId: proposalUser.UserId,
                Summary: "Caller supplied queue reference",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString(),
                BoardId: proposalBoard.Id,
                SourceReferenceId: createdCapture!.Id.ToString(),
                Operations: new List<CreateProposalOperationDto>
                {
                    new(
                        Sequence: 1,
                        ActionType: "update",
                        TargetType: "board",
                        Parameters: $"{{\"boardId\":\"{proposalBoard.Id}\",\"name\":\"Queue source guardrail {Guid.NewGuid():N}\"}}",
                        IdempotencyKey: Guid.NewGuid().ToString(),
                        TargetId: proposalBoard.Id.ToString())
                }));
        createProposalResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProposal = await createProposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        createdProposal.Should().NotBeNull();

        var approveResponse = await proposalClient.PostAsync($"/api/automation/proposals/{createdProposal!.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{createdProposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await proposalClient.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persistedCapture = await db.LlmRequests.FindAsync(createdCapture.Id);
        persistedCapture.Should().NotBeNull();
        persistedCapture!.UserId.Should().Be(captureOwner.UserId);
        persistedCapture.BoardId.Should().Be(captureBoard.Id);
        var payload = CaptureRequestContract.ParsePayload(persistedCapture.Payload, allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.CaptureItemId.Should().Be(initialProvenance.CaptureItemId);
        payload.Value.Provenance.RequestedByUserId.Should().Be(initialProvenance.RequestedByUserId);
        payload.Value.Provenance.CorrelationId.Should().Be(initialProvenance.CorrelationId);
        payload.Value.Provenance.SourceSurface.Should().Be(initialProvenance.SourceSurface);
        payload.Value.Provenance.BoardId.Should().Be(initialProvenance.BoardId);
        payload.Value.Provenance.ProposalId.Should().BeNull();
        payload.Value.Provenance.ConvertedAt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteProposal_WhenNotApproved_ShouldReturnConflict()
    {
        var userId = await AuthenticateAsync("automation-exec-conflict");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await _client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("InvalidOperation");
    }

    [Fact]
    public async Task ExecuteProposal_WithoutIdempotencyKey_ShouldReturnBadRequest()
    {
        var userId = await AuthenticateAsync("automation-exec-no-idempotency");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var executeResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/execute", null);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProposal_ShouldReturnForbidden_WhenCallerCannotReadProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-access-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-access-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-access-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.GetAsync($"/api/automation/proposals/{proposal.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-approve-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-approve-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-approve-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-exec-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-exec-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-exec-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var approveResponse = await ownerClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await outsiderClient.SendAsync(executeRequest);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task RejectProposal_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-reject-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-reject-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-reject-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/reject",
            new UpdateProposalStatusDto("reject-forbidden"));

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetProposal_ShouldReturnForbidden_WhenProposalIsUserScopedToAnotherUser()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-user-scope-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-user-scope-outsider");

        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: owner.UserId,
            Summary: "User-scoped proposal",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString());

        var createResponse = await ownerClient.PostAsJsonAsync("/api/automation/proposals", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProposal = await createResponse.Content.ReadFromJsonAsync<ProposalDto>();
        createdProposal.Should().NotBeNull();

        var response = await outsiderClient.GetAsync($"/api/automation/proposals/{createdProposal!.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetProposalDiff_ShouldReturnDiffPreview()
    {
        var userId = await AuthenticateAsync("automation-diff");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var diffResponse = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var diffResult = await diffResponse.Content.ReadFromJsonAsync<JsonElement>();
        diffResult.TryGetProperty("diff", out var diff).Should().BeTrue();
    }

    [Fact]
    public async Task GetProposalDiff_ShouldReturnForbidden_WhenCallerCannotReadProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-diff-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-diff-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-diff-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        await AuthenticateAsync("automation-get-notfound");

        var response = await _client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        await AuthenticateAsync("automation-approve-notfound");

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateProposal_WithEmptySummary_ShouldReturnBadRequest()
    {
        var userId = await AuthenticateAsync("automation-create-invalid");

        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: string.Empty,
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString()
        );

        var response = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    private async Task<ProposalDto> CreateTestProposal(Guid userId, Guid boardId, RiskLevel riskLevel)
    {
        return await CreateTestProposalAsync(_client, userId, boardId, riskLevel);
    }

    private static async Task<ProposalDto> CreateTestProposalAsync(HttpClient client, Guid userId, Guid boardId, RiskLevel riskLevel)
    {
        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: $"Test proposal {Guid.NewGuid()}",
            RiskLevel: riskLevel,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "update",
                    TargetType: "board",
                    Parameters: $"{{\"boardId\":\"{boardId}\",\"name\":\"Automated update {Guid.NewGuid():N}\"}}",
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: boardId.ToString()
                )
            }
        );

        var response = await client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
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

    private async Task<Guid> CreateOwnedBoardAsync(Guid ownerId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/import/boards",
            new ImportBoardDto(
                $"automation-board-{Guid.NewGuid():N}",
                null,
                Array.Empty<ImportColumnDto>(),
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.BoardId.Should().NotBeNull();

        return result.BoardId!.Value;
    }
}
