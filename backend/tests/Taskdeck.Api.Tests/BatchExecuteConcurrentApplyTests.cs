using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Deterministic regression coverage for preload-to-execution races in batch proposal apply
/// (#2284). The barrier pauses the batch after its phase-one proposal/ACL reads, then real second
/// requests change persisted proposal, revision, and authorization state before the batch reaches
/// its per-item executor call.
/// </summary>
public class BatchExecuteConcurrentApplyTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly TestWebApplicationFactory _factory;

    public BatchExecuteConcurrentApplyTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteProposals_WhenAnotherRequestAppliesAfterPreload_ReturnsSkippedWithoutTransientMutation()
    {
        var barrier = new BatchPreloadBarrier();
        var realtime = new RecordingBoardRealtimeNotifier();
        using var factory = CreateRaceFactory(barrier, realtime);

        var batchClient = factory.CreateClient();
        var winningClient = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(batchClient, "batch-exec-concurrent-apply");
        winningClient.DefaultRequestHeaders.Authorization = batchClient.DefaultRequestHeaders.Authorization;

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(batchClient, "batch-exec-concurrent-board");
        var columnId = await GetColumnIdAsync(factory, boardId);
        var proposal = await CreateApprovedProposalAsync(
            batchClient,
            user.UserId,
            boardId,
            columnId,
            "Concurrent winner card");

        realtime.Clear();
        barrier.Arm(user.UserId, boardId);

        var batchRequest = batchClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest
                    {
                        ProposalId = proposal.Id,
                        ApprovedRevisionId = proposal.ApprovedRevisionId,
                        IdempotencyKey = "losing-batch-turn"
                    }
                ]
            });

        IReadOnlyList<BoardRealtimeEvent>? realtimeAfterWinningRequest = null;
        try
        {
            await barrier.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            using var winningRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/automation/proposals/{proposal.Id}/execute");
            winningRequest.Headers.Add("Idempotency-Key", "concurrent-winner");
            using var winningResponse = await winningClient.SendAsync(winningRequest);
            winningResponse.StatusCode.Should().Be(
                HttpStatusCode.OK,
                await winningResponse.Content.ReadAsStringAsync());
            realtimeAfterWinningRequest = realtime.Events;
        }
        finally
        {
            barrier.Release();
        }

        var batchResponse = await batchRequest.WaitAsync(TimeSpan.FromSeconds(10));
        var batchBody = await batchResponse.Content.ReadAsStringAsync();
        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK, batchBody);
        var receipt = JsonSerializer.Deserialize<BatchExecuteProposalsResultDto>(batchBody, Web)!;
        receipt.Results.Should().ContainSingle();
        receipt.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Skipped);
        receipt.Results.Single().ErrorCode.Should().BeNull();
        receipt.Results.Single().AppliedOperations.Should().BeNull();

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card =>
                card.BoardId == boardId && card.Title == "Concurrent winner card"))
            .Should().Be(1, "only the concurrent winning request may mutate the board");
        (await db.AuditLogs.CountAsync(log => log.EntityId == columnId))
            .Should().Be(1, "the losing batch turn must not emit a duplicate execution audit row");
        (await db.AutomationProposals
                .Where(item => item.Id == proposal.Id)
                .Select(item => item.Status)
                .SingleAsync())
            .Should().Be(ProposalStatus.Applied);

        realtimeAfterWinningRequest.Should().NotBeNull();
        realtimeAfterWinningRequest!.Should().ContainSingle(evt =>
            evt.BoardId == boardId &&
            evt.EntityType == "card" &&
            evt.Operation == "created");
        realtime.Events.Should().Equal(
            realtimeAfterWinningRequest,
            "the losing batch turn must emit no transient realtime change before it skips");
    }

    [Fact]
    public async Task ExecuteProposals_WhenAnotherRequestAppliesThenCallerIsRevoked_RefusesWithoutLosingSyncOrMutation()
    {
        var barrier = new BatchPreloadBarrier();
        var realtime = new RecordingBoardRealtimeNotifier();
        using var factory = CreateRaceFactory(barrier, realtime);

        var ownerClient = factory.CreateClient();
        var collaboratorClient = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "batch-exec-revoked-replay-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(
            collaboratorClient,
            "batch-exec-revoked-replay-collaborator");

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(
            ownerClient,
            "batch-exec-revoked-replay-board");
        var columnId = await GetColumnIdAsync(factory, boardId);
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, collaborator.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grantResponse.Content.ReadFromJsonAsync<BoardAccessDto>();
        access.Should().NotBeNull();

        var captureId = await CreateLinkedCaptureShellAsync(factory, owner.UserId, boardId);
        var proposal = await CreateApprovedProposalAsync(
            ownerClient,
            owner.UserId,
            boardId,
            columnId,
            "Concurrent revoked winner card",
            captureId.ToString());
        await LinkCaptureToProposalAsync(factory, captureId, proposal.Id);

        realtime.Clear();
        barrier.Arm(collaborator.UserId, boardId);
        var batchRequest = collaboratorClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest
                    {
                        ProposalId = proposal.Id,
                        ApprovedRevisionId = proposal.ApprovedRevisionId,
                        IdempotencyKey = "revoked-losing-batch-turn"
                    }
                ]
            });

        string? capturePayloadBeforeRelease = null;
        IReadOnlyList<BoardRealtimeEvent>? realtimeBeforeRelease = null;
        try
        {
            await barrier.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            using var winningRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/automation/proposals/{proposal.Id}/execute");
            winningRequest.Headers.Add("Idempotency-Key", "revoked-concurrent-winner");
            using var winningResponse = await ownerClient.SendAsync(winningRequest);
            winningResponse.StatusCode.Should().Be(
                HttpStatusCode.OK,
                await winningResponse.Content.ReadAsStringAsync());

            capturePayloadBeforeRelease = await ResetCaptureConversionAsync(factory, captureId);

            using var revokeResponse = await ownerClient.DeleteAsync(
                $"/api/boards/{boardId}/access/{access!.Id}");
            revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            realtimeBeforeRelease = realtime.Events;
        }
        finally
        {
            barrier.Release();
        }

        using var batchResponse = await batchRequest.WaitAsync(TimeSpan.FromSeconds(10));
        var batchBody = await batchResponse.Content.ReadAsStringAsync();
        batchResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, batchBody);
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(batchBody, Web)!;
        error.ErrorCode.Should().Be(ErrorCodes.Forbidden);

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card =>
                card.BoardId == boardId && card.Title == "Concurrent revoked winner card"))
            .Should().Be(1, "only the owner request may mutate the board");
        (await db.AuditLogs.CountAsync(log => log.EntityId == columnId))
            .Should().Be(1, "the revoked losing turn must not emit a duplicate execution audit");
        var persistedCapture = await db.LlmRequests.SingleAsync(item => item.Id == captureId);
        persistedCapture.Payload.Should().Be(
            capturePayloadBeforeRelease,
            "the revoked losing turn must not repair linked-capture conversion metadata");
        var capturePayload = CaptureRequestContract.ParsePayload(
            persistedCapture.Payload,
            allowServerAttributionFields: true);
        capturePayload.IsSuccess.Should().BeTrue();
        capturePayload.Value.Provenance!.ConvertedAt.Should().BeNull();

        realtimeBeforeRelease.Should().NotBeNull();
        realtime.Events.Should().Equal(
            realtimeBeforeRelease,
            "the revoked losing turn must emit no realtime board change");
    }

    [Fact]
    public async Task ExecuteProposals_WhenNullPinBecomesApprovedRevisionAfterPreload_ConflictsWithoutMutation()
    {
        var barrier = new BatchPreloadBarrier();
        var realtime = new RecordingBoardRealtimeNotifier();
        using var factory = CreateRaceFactory(barrier, realtime);

        var batchClient = factory.CreateClient();
        var transitionClient = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(batchClient, "batch-exec-null-pin-race");
        transitionClient.DefaultRequestHeaders.Authorization = batchClient.DefaultRequestHeaders.Authorization;

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(
            batchClient,
            "batch-exec-null-pin-race-board");
        var columnId = await GetColumnIdAsync(factory, boardId);
        var proposal = await CreatePendingProposalAsync(
            batchClient,
            user.UserId,
            boardId,
            columnId,
            "Unreviewed original card");
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.ApprovedRevisionId.Should().BeNull();

        realtime.Clear();
        barrier.Arm(user.UserId, boardId);
        var batchRequest = batchClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest
                    {
                        ProposalId = proposal.Id,
                        ApprovedRevisionId = null,
                        IdempotencyKey = "stale-explicit-null-pin"
                    }
                ]
            });

        ProposalRevisionDto? revision = null;
        IReadOnlyList<BoardRealtimeEvent>? realtimeBeforeRelease = null;
        try
        {
            await barrier.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            var revisedPayload = JsonSerializer.Serialize(new
            {
                operations = new[]
                {
                    new
                    {
                        sequence = 0,
                        actionType = "create",
                        targetType = "card",
                        targetId = (string?)null,
                        parameters = JsonSerializer.Serialize(new
                        {
                            title = "Freshly approved revision card",
                            boardId,
                            columnId
                        }),
                        idempotencyKey = "fresh-approved-revision-operation"
                    }
                }
            });
            using var revisionResponse = await transitionClient.PostAsJsonAsync(
                $"/api/automation/proposals/{proposal.Id}/revisions",
                new { revisedPayload, reason = "approved after batch preload" });
            var revisionBody = await revisionResponse.Content.ReadAsStringAsync();
            revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created, revisionBody);
            revision = JsonSerializer.Deserialize<ProposalRevisionDto>(revisionBody, Web)!;

            using var approveResponse = await transitionClient.PostAsync(
                $"/api/automation/proposals/{proposal.Id}/approve",
                null);
            var approveBody = await approveResponse.Content.ReadAsStringAsync();
            approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, approveBody);
            var approved = JsonSerializer.Deserialize<ProposalDto>(approveBody, Web)!;
            approved.ApprovedRevisionId.Should().Be(revision.Id);
            realtimeBeforeRelease = realtime.Events;
        }
        finally
        {
            barrier.Release();
        }

        using var batchResponse = await batchRequest.WaitAsync(TimeSpan.FromSeconds(10));
        var batchBody = await batchResponse.Content.ReadAsStringAsync();
        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK, batchBody);
        var receipt = JsonSerializer.Deserialize<BatchExecuteProposalsResultDto>(batchBody, Web)!;
        receipt.Results.Should().ContainSingle();
        receipt.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Failed);
        receipt.Results.Single().ErrorCode.Should().Be(ErrorCodes.Conflict);

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card =>
                card.BoardId == boardId && card.Title == "Freshly approved revision card"))
            .Should().Be(0, "the batch never consented to the revision created after preload");
        (await db.AuditLogs.CountAsync(log => log.EntityId == columnId))
            .Should().Be(0, "the pin mismatch must fail before operation audit");
        var persistedProposal = await db.AutomationProposals.SingleAsync(item => item.Id == proposal.Id);
        persistedProposal.Status.Should().Be(ProposalStatus.Approved);
        persistedProposal.ApprovedRevisionId.Should().Be(revision!.Id);

        realtimeBeforeRelease.Should().NotBeNull();
        realtime.Events.Should().Equal(
            realtimeBeforeRelease,
            "the unconsented revision must not emit a realtime board change");
    }

    private WebApplicationFactory<Program> CreateRaceFactory(
        BatchPreloadBarrier barrier,
        RecordingBoardRealtimeNotifier realtime) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(barrier);
                services.RemoveAll<IAuthorizationService>();
                services.AddScoped<IAuthorizationService>(sp =>
                    new BlockingAuthorizationService(
                        sp.GetRequiredService<AuthorizationService>(),
                        sp.GetRequiredService<BatchPreloadBarrier>()));

                services.RemoveAll<IBoardRealtimeNotifier>();
                services.AddSingleton<IBoardRealtimeNotifier>(realtime);
            }));

    private static async Task<Guid> GetColumnIdAsync(
        WebApplicationFactory<Program> factory,
        Guid boardId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        return await db.Columns
            .Where(column => column.BoardId == boardId)
            .Select(column => column.Id)
            .SingleAsync();
    }

    private static async Task<ProposalDto> CreateApprovedProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        Guid columnId,
        string title,
        string? sourceReferenceId = null)
    {
        var proposal = await CreatePendingProposalAsync(
            client,
            userId,
            boardId,
            columnId,
            title,
            sourceReferenceId);

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await approveResponse.Content.ReadAsStringAsync());
        return (await approveResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static async Task<ProposalDto> CreatePendingProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        Guid columnId,
        string title,
        string? sourceReferenceId = null)
    {
        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                SourceType: ProposalSourceType.Queue,
                RequestedByUserId: userId,
                Summary: "Concurrent batch apply",
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString("N"),
                BoardId: boardId,
                SourceReferenceId: sourceReferenceId,
                Operations:
                [
                    new CreateProposalOperationDto(
                        0,
                        "create",
                        "card",
                        JsonSerializer.Serialize(new { title, boardId, columnId }),
                        Guid.NewGuid().ToString("N"))
                ]));
        createResponse.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await createResponse.Content.ReadAsStringAsync());
        return (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static async Task<Guid> CreateLinkedCaptureShellAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid boardId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var capture = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.Typed,
                "Concurrent capture sync guard")),
            boardId);
        capture.MarkAsProcessing();
        capture.MarkAsCompleted();
        db.LlmRequests.Add(capture);
        await db.SaveChangesAsync();
        return capture.Id;
    }

    private static async Task LinkCaptureToProposalAsync(
        WebApplicationFactory<Program> factory,
        Guid captureId,
        Guid proposalId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var capture = await db.LlmRequests.SingleAsync(item => item.Id == captureId);
        var payload = CaptureRequestContract.ParsePayload(
            capture.Payload,
            allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        var linked = CaptureRequestContract.WithProvenance(
            payload.Value,
            capture.Id,
            proposalId: proposalId);
        capture.UpdatePayload(CaptureRequestContract.SerializePayload(linked));
        await db.SaveChangesAsync();
    }

    private static async Task<string> ResetCaptureConversionAsync(
        WebApplicationFactory<Program> factory,
        Guid captureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var capture = await db.LlmRequests.SingleAsync(item => item.Id == captureId);
        var payload = CaptureRequestContract.ParsePayload(
            capture.Payload,
            allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.ConvertedAt.Should().NotBeNull(
            "the winning request must have completed the linked-capture sync before the test resets it");
        var unsynchronized = payload.Value with
        {
            Provenance = payload.Value.Provenance! with { ConvertedAt = null }
        };
        capture.UpdatePayload(CaptureRequestContract.SerializePayload(unsynchronized));
        await db.SaveChangesAsync();
        return capture.Payload;
    }

    private sealed class BatchPreloadBarrier
    {
        private readonly TaskCompletionSource<bool> _blocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _claimed;
        private Guid _callerUserId;
        private Guid _boardId;

        public void Arm(Guid callerUserId, Guid boardId)
        {
            _callerUserId = callerUserId;
            _boardId = boardId;
            Volatile.Write(ref _armed, 1);
        }

        public async Task BlockAfterReadableAclAsync(
            Guid callerUserId,
            IReadOnlyCollection<Guid> boardIds,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _armed) != 1 ||
                callerUserId != _callerUserId ||
                !boardIds.Contains(_boardId) ||
                Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
            {
                return;
            }

            _blocked.TrySetResult(true);
            await _released.Task.WaitAsync(cancellationToken);
        }

        public async Task WaitUntilBlockedAsync(TimeSpan timeout) =>
            await _blocked.Task.WaitAsync(timeout);

        public void Release() => _released.TrySetResult(true);
    }

    private sealed class BlockingAuthorizationService : IAuthorizationService
    {
        private readonly AuthorizationService _inner;
        private readonly BatchPreloadBarrier _barrier;

        public BlockingAuthorizationService(
            AuthorizationService inner,
            BatchPreloadBarrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public async Task<Result<IReadOnlySet<Guid>>> GetReadableBoardIdsAsync(
            Guid userId,
            IEnumerable<Guid> boardIds,
            CancellationToken cancellationToken = default)
        {
            var materializedBoardIds = boardIds.ToList();
            var result = await _inner.GetReadableBoardIdsAsync(userId, materializedBoardIds, cancellationToken);
            if (result.IsSuccess)
            {
                await _barrier.BlockAfterReadableAclAsync(userId, materializedBoardIds, cancellationToken);
            }

            return result;
        }

        public Task<Result<IReadOnlySet<Guid>>> GetWritableBoardIdsAsync(
            Guid userId,
            IEnumerable<Guid> boardIds,
            CancellationToken cancellationToken = default) =>
            _inner.GetWritableBoardIdsAsync(userId, boardIds, cancellationToken);

        public Task<Result<bool>> CanReadBoardAsync(Guid userId, Guid boardId) =>
            _inner.CanReadBoardAsync(userId, boardId);

        public Task<Result<bool>> CanWriteBoardAsync(Guid userId, Guid boardId) =>
            _inner.CanWriteBoardAsync(userId, boardId);

        public Task<Result<bool>> CanManageBoardAccessAsync(Guid userId, Guid boardId) =>
            _inner.CanManageBoardAccessAsync(userId, boardId);

        public Task<Result<bool>> CanDeleteBoardAsync(Guid userId, Guid boardId) =>
            _inner.CanDeleteBoardAsync(userId, boardId);

        public Task<Result<UserRole?>> GetUserRoleForBoardAsync(Guid userId, Guid boardId) =>
            _inner.GetUserRoleForBoardAsync(userId, boardId);
    }

    private sealed class RecordingBoardRealtimeNotifier : IBoardRealtimeNotifier
    {
        private readonly object _gate = new();
        private readonly List<BoardRealtimeEvent> _events = [];

        public IReadOnlyList<BoardRealtimeEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToList();
                }
            }
        }

        public Task NotifyBoardMutationAsync(
            BoardRealtimeEvent mutation,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _events.Add(mutation);
            }

            return Task.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _events.Clear();
            }
        }
    }
}
