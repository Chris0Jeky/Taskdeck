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
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Deterministic regression coverage for the preload-to-execution race in batch proposal apply
/// (#2284). The barrier pauses the batch after its phase-one proposal/ACL reads, then a real second
/// HTTP request applies the same proposal before the batch reaches its per-item executor call.
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
        using var factory = _factory.WithWebHostBuilder(builder =>
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
        string title)
    {
        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Queue,
                userId,
                "Concurrent batch apply",
                RiskLevel.Low,
                Guid.NewGuid().ToString("N"),
                boardId,
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
        var proposal = (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await approveResponse.Content.ReadAsStringAsync());
        return (await approveResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
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
