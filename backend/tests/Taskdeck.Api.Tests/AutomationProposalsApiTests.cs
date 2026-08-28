using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
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
    public async Task CreateProposal_ExternalTrustedConfidence_IsIgnoredAndSerializesWithoutANumber()
    {
        var userId = await AuthenticateAsync("automation-confidence-untrusted");
        var boardId = await CreateOwnedBoardAsync(userId);
        var request = new
        {
            sourceType = ProposalSourceType.Chat,
            requestedByUserId = userId,
            summary = "Untrusted confidence must not cross the API boundary",
            riskLevel = RiskLevel.Low,
            correlationId = Guid.NewGuid().ToString(),
            boardId,
            operations = new[]
            {
                new
                {
                    sequence = 1,
                    actionType = "update",
                    targetType = "board",
                    parameters = $"{{\"boardId\":\"{boardId}\",\"name\":\"External confidence ignored\"}}",
                    idempotencyKey = Guid.NewGuid().ToString(),
                    targetId = boardId.ToString(),
                },
            },
            trustedConfidence = new
            {
                source = ProvenanceConfidenceSource.ModelReported,
                operations = new[] { new { operationSequence = 1, value = 0.99 } },
            },
        };

        var createResponse = await _client.PostAsJsonAsync("/api/automation/proposals", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var proposal = await createResponse.Content.ReadFromJsonAsync<ProposalDto>();

        var response = await _client.GetAsync($"/api/automation/proposals/{proposal!.Id}/confidence");
        var rawJson = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {rawJson}");
        using var document = JsonDocument.Parse(rawJson);
        document.RootElement.GetProperty("source").GetString().Should().Be("not-reported");
        document.RootElement.GetProperty("overall").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("components").GetArrayLength().Should().Be(0);
        document.RootElement.GetProperty("threshold").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("meetsThreshold").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetProposalConfidence_SerializesExactStoredModelReportedValue()
    {
        var userId = await AuthenticateAsync("automation-confidence-model");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var provenance = await db.ProposalProvenances
                .Include(item => item.Fields)
                .SingleAsync(item => item.ProposalId == proposal.Id);
            var operationField = provenance.Fields.Single(field =>
                field.FieldName.StartsWith("Operation ", StringComparison.Ordinal));
            db.ProvenanceFields.Remove(operationField);
            provenance.AddField(new ProvenanceField(
                operationField.FieldName,
                operationField.Kind,
                0.81,
                provenance.Id,
                ProvenanceConfidenceSource.ModelReported));
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}/confidence");
        var rawJson = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {rawJson}");
        var breakdown = JsonSerializer.Deserialize<ConfidenceBreakdownDto>(
            rawJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        breakdown.Should().NotBeNull();
        breakdown!.Source.Should().Be(ConfidenceBreakdownDto.ModelReportedSource);
        breakdown.Overall.Should().Be(0.81);
        breakdown.Components.Should().ContainSingle().Which.Should().Be(
            new ConfidenceComponentDto("Operation 1: update board", 0.81));
        breakdown.Threshold.Should().BeNull();
        breakdown.MeetsThreshold.Should().BeNull();
        rawJson.Should().NotContain("Reversibility").And.NotContain("Recency").And.NotContain("Pattern match");
    }

    [Fact]
    public async Task GetProposalProvenance_AsBoardViewer_ReturnsOpaqueTranscriptEvidenceWithoutText()
    {
        var ownerClient = _factory.CreateClient();
        var viewerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-provenance-owner");
        var viewer = await ApiTestHarness.AuthenticateAsync(viewerClient, "automation-provenance-viewer");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-provenance-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        const string privateTranscriptText = "PRIVATE_TRANSCRIPT_TEXT_never_return_this";
        Guid transcriptId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var transcript = new Transcript(
                owner.UserId,
                CaptureSource.TranscriptPaste,
                privateTranscriptText,
                [new TranscriptSegment(0, 0, "Speaker", 0)]);
            var provenance = await db.ProposalProvenances
                .Include(item => item.Fields)
                .SingleAsync(item => item.ProposalId == proposal.Id);
            var field = provenance.Fields
                .OrderBy(item => item.Id)
                .First();
            field.AddEvidenceLink(new ProvenanceEvidenceLink(
                ProvenanceEvidenceLink.TranscriptSourceType,
                transcript.Id.ToString("D"),
                field.Id,
                "Transcript evidence",
                8,
                23,
                transcript.Id));
            db.Transcripts.Add(transcript);
            await db.SaveChangesAsync();
            transcriptId = transcript.Id;
        }

        var response = await viewerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/provenance");
        var rawJson = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {rawJson}");
        rawJson.Should().NotContain(privateTranscriptText);
        var rows = JsonSerializer.Deserialize<List<ProvenanceRowDto>>(
            rawJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        rows.Should().NotBeNull();
        var evidence = rows!
            .SelectMany(row => row.EvidenceLinks ?? Array.Empty<ProvenanceEvidenceLinkDto>())
            .Should()
            .ContainSingle()
            .Subject;
        evidence.SourceType.Should().Be("Transcript");
        evidence.SourceId.Should().Be(transcriptId.ToString("D"));
        evidence.Label.Should().Be("Transcript evidence");
        evidence.SpanStart.Should().Be(8);
        evidence.SpanEnd.Should().Be(23);

        // Issue #1837 item 1: the board collaborator is authorized for the proposal but not for
        // the owner's transcript, so the evidence must not advertise a followable link.
        evidence.Viewable.Should().BeFalse();

        // The flag tells the truth: the same caller's direct read is a 404, and that 404 is
        // indistinguishable from a nonexistent transcript (parity unchanged by this flag).
        var viewerTranscriptResponse = await viewerClient.GetAsync($"/api/transcripts/{transcriptId}");
        viewerTranscriptResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The owner sees the same row marked viewable, computed from their own claims.
        var ownerResponse = await ownerClient.GetAsync($"/api/automation/proposals/{proposal.Id}/provenance");
        var ownerJson = await ownerResponse.Content.ReadAsStringAsync();
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {ownerJson}");
        var ownerRows = JsonSerializer.Deserialize<List<ProvenanceRowDto>>(
            ownerJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var ownerEvidence = ownerRows!
            .SelectMany(row => row.EvidenceLinks ?? Array.Empty<ProvenanceEvidenceLinkDto>())
            .Should()
            .ContainSingle()
            .Subject;
        ownerEvidence.Viewable.Should().BeTrue();
        // Still opaque for the owner too: the flag never carries transcript text.
        ownerJson.Should().NotContain(privateTranscriptText);
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
        var initialStatus = initialPersistedCapture!.Status;
        var initialErrorMessage = initialPersistedCapture.ErrorMessage;
        var initialProcessedAt = initialPersistedCapture.ProcessedAt;
        var initialRetryCount = initialPersistedCapture.RetryCount;
        var initialUpdatedAt = initialPersistedCapture.UpdatedAt;
        var initialPayloadRaw = initialPersistedCapture.Payload;
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
        persistedCapture.Status.Should().Be(initialStatus);
        persistedCapture.ErrorMessage.Should().Be(initialErrorMessage);
        persistedCapture.ProcessedAt.Should().Be(initialProcessedAt);
        persistedCapture.RetryCount.Should().Be(initialRetryCount);
        persistedCapture.UpdatedAt.Should().Be(initialUpdatedAt);
        persistedCapture.Payload.Should().Be(initialPayloadRaw);
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

    [Fact]
    public async Task DeferProposal_WithEmptyBody_ShouldSnoozeDefaultWindow_AndKeepPending()
    {
        var userId = await AuthenticateAsync("automation-defer-default");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var before = DateTime.UtcNow;
        var deferResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/defer", null);
        deferResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deferred = await deferResponse.Content.ReadFromJsonAsync<ProposalDto>();
        deferred.Should().NotBeNull();
        deferred!.Status.Should().Be(ProposalStatus.PendingReview);
        deferred.DecidedByUserId.Should().BeNull();
        deferred.DeferredUntil.Should().NotBeNull();
        // Default window is ~60 minutes.
        deferred.DeferredUntil!.Value.Should().BeOnOrAfter(before.AddMinutes(60));
        deferred.DeferredUntil!.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(60).AddSeconds(5));

        // Still reachable by id (deep-link) even while snoozed.
        var getResponse = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeferProposal_WithExplicitDuration_ShouldHonorIt()
    {
        var userId = await AuthenticateAsync("automation-defer-explicit");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var before = DateTime.UtcNow;
        var deferResponse = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/defer",
            new DeferProposalRequestDto(DurationMinutes: 10));
        deferResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deferred = await deferResponse.Content.ReadFromJsonAsync<ProposalDto>();
        deferred!.DeferredUntil.Should().NotBeNull();
        deferred.DeferredUntil!.Value.Should().BeOnOrAfter(before.AddMinutes(10));
        deferred.DeferredUntil!.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(10).AddSeconds(5));
    }

    [Fact]
    public async Task DeferProposal_WithOverlongDuration_ShouldClampToMax()
    {
        var userId = await AuthenticateAsync("automation-defer-clamp");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var before = DateTime.UtcNow;
        var deferResponse = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/defer",
            new DeferProposalRequestDto(DurationMinutes: 100000));
        deferResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deferred = await deferResponse.Content.ReadFromJsonAsync<ProposalDto>();
        deferred!.DeferredUntil.Should().NotBeNull();
        // Clamped to the 1440-minute (24h) maximum, not the requested 100000.
        deferred.DeferredUntil!.Value.Should().BeOnOrAfter(before.AddMinutes(1440));
        deferred.DeferredUntil!.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(1440).AddSeconds(5));
    }

    [Fact]
    public async Task DeferProposal_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/defer", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeferProposal_ShouldReturnForbidden_WhenCallerCannotWriteProposalBoard()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-defer-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-defer-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-defer-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.PostAsync($"/api/automation/proposals/{proposal.Id}/defer", null);
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task DeferProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        await AuthenticateAsync("automation-defer-notfound");

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/defer", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeferProposal_ShouldReturnConflict_WhenProposalAlreadyApproved()
    {
        var userId = await AuthenticateAsync("automation-defer-approved");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deferResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/defer", null);
        deferResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeferProposal_ShouldDisappearFromList_ThenReappearAfterWindow()
    {
        var userId = await AuthenticateAsync("automation-defer-list");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var deferResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/defer", null);
        deferResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Snoozed: it leaves the board-scoped list.
        var hiddenList = await GetBoardProposalsAsync(boardId);
        hiddenList.Should().NotContain(p => p.Id == proposal.Id);

        // Elapse the snooze window directly in the database, then it reappears.
        var pastDeferredUntil = DateTime.UtcNow.AddMinutes(-1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AutomationProposals SET DeferredUntil = {pastDeferredUntil} WHERE Id = {proposal.Id}");
        }

        var visibleList = await GetBoardProposalsAsync(boardId);
        visibleList.Should().Contain(p => p.Id == proposal.Id);
    }

    [Fact]
    public async Task ReportFeedback_ShouldReturn204_WithEmptyBody()
    {
        var userId = await AuthenticateAsync("automation-feedback-basic");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var response = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/feedback", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ReportFeedback_ShouldBeIdempotent_AndRecordOneRow()
    {
        var userId = await AuthenticateAsync("automation-feedback-idem");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var first = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/feedback", null);
        var second = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/feedback", null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var count = await db.ProposalFeedbacks.CountAsync(f => f.ProposalId == proposal.Id);
        count.Should().Be(1);
    }

    [Fact]
    public async Task ReportFeedback_ShouldPersistReason_AndReporterFromClaims()
    {
        var userId = await AuthenticateAsync("automation-feedback-reason");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var response = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/feedback",
            new ReportProposalFeedbackDto("TooRisky"));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var feedback = await db.ProposalFeedbacks.SingleAsync(f => f.ProposalId == proposal.Id);
        feedback.Reason.Should().Be(ProposalFeedbackReason.TooRisky);
        feedback.ReportedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetAllByUserIdAsync_ReturnsUserFeedbackNewestFirst_OnSqlite()
    {
        // Regression guard (#1245 review): GetAllByUserIdAsync orders by a DateTimeOffset column,
        // which SQLite's EF provider can't ORDER BY in LINQ -- it must run without throwing and
        // return newest-first. This is the real-SQLite read path the GDPR export depends on.
        var userId = await AuthenticateAsync("automation-feedback-getall");
        var boardId = await CreateOwnedBoardAsync(userId);
        var older = await CreateTestProposal(userId, boardId, RiskLevel.Low);
        var newer = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        (await _client.PostAsJsonAsync($"/api/automation/proposals/{older.Id}/feedback",
            new ReportProposalFeedbackDto("Irrelevant"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsJsonAsync($"/api/automation/proposals/{newer.Id}/feedback",
            new ReportProposalFeedbackDto("Incorrect"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProposalFeedbackRepository>();
        var all = await repo.GetAllByUserIdAsync(userId);

        all.Should().HaveCount(2);
        all.Select(f => f.ProposalId).Should().Equal(newer.Id, older.Id); // newest-first
    }

    [Fact]
    public async Task GetAllByUserIdForExportAsync_ReturnsAllRowsUncapped_WhileCohortReadCaps()
    {
        // #1245 Codex P2: the data-portability export must return the COMPLETE feedback set, not the
        // capped 1000-row cohort sample (a heavy reporter would otherwise lose older rows from the
        // export). Seed just past the cap and assert the two reads diverge: the export read returns
        // everything; the cohort read stays bounded. Seed directly via the DbContext -- 1005 API
        // round-trips would be needlessly slow, and AutomationProposal has no required parent FK.
        const int cohortCap = 1000; // mirrors ProposalFeedbackRepository.MaxLimit
        const int seedCount = cohortCap + 5;
        var userId = Guid.NewGuid();

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            for (var i = 0; i < seedCount; i++)
            {
                var proposal = new AutomationProposal(
                    ProposalSourceType.Chat, userId, $"export-cap proposal {i}",
                    RiskLevel.Low, Guid.NewGuid().ToString());
                db.AutomationProposals.Add(proposal);
                db.ProposalFeedbacks.Add(new ProposalFeedback(proposal.Id, userId, ProposalFeedbackReason.Unspecified));
            }
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProposalFeedbackRepository>();

        var exported = await repo.GetAllByUserIdForExportAsync(userId);
        var cohort = await repo.GetAllByUserIdAsync(userId);

        exported.Should().HaveCount(seedCount);   // uncapped — the complete export
        cohort.Should().HaveCount(cohortCap);     // cohort sample stays bounded
    }

    [Fact]
    public async Task ReportFeedback_ShouldReturn404_ForUnknownProposal()
    {
        await AuthenticateAsync("automation-feedback-404");

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/feedback", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReportFeedback_ShouldReturn400_ForUnknownReason()
    {
        var userId = await AuthenticateAsync("automation-feedback-badreason");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        // Exact-name validation rejects every non-member form. Beyond the obvious junk/out-of-range
        // numeric, the subtle ones (#1245 Codex P2) are inputs Enum.TryParse would coerce to a
        // *defined* member and silently mis-record: a comma-composite flags combination
        // ("Irrelevant, Incorrect" -> 1|2 == 3 == Duplicate) and an in-range numeric string
        // ("3" == Duplicate). All four must be 400, never a recorded Duplicate.
        var numeric = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/feedback",
            new ReportProposalFeedbackDto("999"));
        var junk = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/feedback",
            new ReportProposalFeedbackDto("definitely-not-a-reason"));
        var composite = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/feedback",
            new ReportProposalFeedbackDto("Irrelevant, Incorrect"));
        var inRangeNumeric = await _client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/feedback",
            new ReportProposalFeedbackDto("3"));

        numeric.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        junk.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        composite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        inRangeNumeric.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And nothing was persisted by the rejected coercions.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.ProposalFeedbacks.AnyAsync(f => f.ProposalId == proposal.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ReportFeedback_ShouldReturn403_ForUserWithoutBoardAccess()
    {
        var ownerClient = _factory.CreateClient();
        var outsiderClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "automation-feedback-owner");
        _ = await ApiTestHarness.AuthenticateAsync(outsiderClient, "automation-feedback-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "automation-feedback-board");
        var proposal = await CreateTestProposalAsync(ownerClient, owner.UserId, board.Id, RiskLevel.Low);

        var response = await outsiderClient.PostAsync($"/api/automation/proposals/{proposal.Id}/feedback", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ReportFeedback_ShouldReturn401_WhenUnauthenticated()
    {
        var anon = _factory.CreateClient();

        var response = await anon.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/feedback", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSimilarPast_ForPendingCaptureProposalWithoutHistory_ReturnsEmptyResult()
    {
        var userId = await AuthenticateAsync("automation-similar-past-empty");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposalAsync(
            _client,
            userId,
            boardId,
            RiskLevel.Low,
            ProposalSourceType.Queue);

        var response = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}/similar-past");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SimilarPastResultDto>();
        result.Should().NotBeNull();
        result!.Decisions.Should().BeEmpty();
        result.ApplyRate.Should().Be(0);
    }

    private async Task<List<ProposalDto>> GetBoardProposalsAsync(Guid boardId)
    {
        var response = await _client.GetAsync($"/api/automation/proposals?boardId={boardId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ProposalDto>>())!;
    }

    private async Task<ProposalDto> CreateTestProposal(Guid userId, Guid boardId, RiskLevel riskLevel)
    {
        return await CreateTestProposalAsync(_client, userId, boardId, riskLevel);
    }

    private static async Task<ProposalDto> CreateTestProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        RiskLevel riskLevel,
        ProposalSourceType sourceType = ProposalSourceType.Chat)
    {
        var createRequest = new CreateProposalDto(
            SourceType: sourceType,
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
