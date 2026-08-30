using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// The mid-batch revocation guard (#1307, review round 1 P1-A).
///
/// A batch reads board authorization ONCE before its loop. That snapshot is correct at the instant
/// it is taken and progressively less true afterwards, so a write revoked while the batch is
/// running was, before this guard, invisible to every remaining item. The window is real: a
/// 500-item batch of board writes is not instantaneous.
///
/// The race cannot be expressed over HTTP from the outside - there is no moment "between items" a
/// second request can occupy deterministically - so it is staged here with a policy-engine
/// decorator that revokes the collaborator's access at exactly that point, and then asserts through
/// the ordinary public endpoint.
/// </summary>
public class BatchExecuteMidBatchRevocationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly TestWebApplicationFactory _factory;

    public BatchExecuteMidBatchRevocationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteProposals_WhenCallerAccessIsRevokedMidBatch_LaterItemIsForbiddenAndWritesNothing()
    {
        var revoker = new RevokeAfterFirstItem();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(revoker);
                services.AddScoped<IAutomationPolicyEngine>(sp => new RevokingPolicyEngineDecorator(
                    new AutomationPolicyEngine(sp.GetRequiredService<IUnitOfWork>()),
                    sp.GetRequiredService<TaskdeckDbContext>(),
                    sp.GetRequiredService<RevokeAfterFirstItem>()));
            }));

        var ownerClient = factory.CreateClient();
        var collaboratorClient = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "midbatch-owner");
        var collaborator = await ApiTestHarness.AuthenticateAsync(collaboratorClient, "midbatch-collaborator");

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "midbatch-board");
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, collaborator.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both proposals are authored by the OWNER. That isolates the new guard: the executor's
        // pre-existing permission check validates the proposal's REQUESTER, who is the owner and
        // never loses access, so anything the second item refuses can only have been refused by the
        // caller-side recheck this test exists for.
        var first = await CreateApprovedProposalAsync(ownerClient, factory, owner.UserId, boardId, "Mid-batch first");
        var second = await CreateApprovedProposalAsync(ownerClient, factory, owner.UserId, boardId, "Mid-batch second");

        revoker.Arm(owner.UserId, collaborator.UserId, boardId);

        // The COLLABORATOR submits the owner's two approved proposals in one batch.
        var response = await collaboratorClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    Select(first),
                    Select(second)
                ]
            });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var receipt = JsonSerializer.Deserialize<BatchExecuteProposalsResultDto>(body, Web)!;

        revoker.Revoked.Should().BeTrue("the test seam must actually have revoked access mid-batch");

        receipt.Results[0].Outcome.Should().Be(
            BatchExecuteOutcome.Applied,
            "the first item was authorized when it ran and must keep its apply");
        receipt.Results[1].Outcome.Should().Be(
            BatchExecuteOutcome.Failed,
            "the caller lost write access before the second item executed");
        receipt.Results[1].ErrorCode.Should().Be("Forbidden");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Mid-batch first"))
            .Should().Be(1);
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Mid-batch second"))
            .Should().Be(0, "the refused item must write nothing to the board");
        (await db.AutomationProposals.Where(p => p.Id == second.Id).Select(p => p.Status).SingleAsync())
            .Should().NotBe(ProposalStatus.Applied);
    }

    private static ExecuteProposalSelectionRequest Select(ProposalDto proposal) => new()
    {
        ProposalId = proposal.Id,
        ApprovedRevisionId = proposal.ApprovedRevisionId,
        IdempotencyKey = Guid.NewGuid().ToString("N")
    };

    private static async Task<ProposalDto> CreateApprovedProposalAsync(
        HttpClient client,
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid boardId,
        string title)
    {
        Guid columnId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            columnId = await db.Columns
                .Where(column => column.BoardId == boardId)
                .Select(column => column.Id)
                .FirstAsync();
        }

        var createResponse = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Queue,
                userId,
                $"Mid-batch {title}",
                RiskLevel.Low,
                Guid.NewGuid().ToString(),
                boardId,
                Operations: new List<CreateProposalOperationDto>
                {
                    new(
                        0,
                        "create",
                        "card",
                        JsonSerializer.Serialize(new { title, boardId, columnId }),
                        Guid.NewGuid().ToString())
                }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var proposal = (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, await approveResponse.Content.ReadAsStringAsync());
        return (await approveResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    /// <summary>Shared arming state; a singleton so it survives the per-request scopes.</summary>
    private sealed class RevokeAfterFirstItem
    {
        private int _requesterChecks;
        private int _fired;

        /// <summary>The proposals' author, whose access is never revoked.</summary>
        public Guid RequesterId { get; private set; }

        /// <summary>The batch submitter, whose access is revoked between items.</summary>
        public Guid CallerId { get; private set; }

        public Guid BoardId { get; private set; }
        public bool Armed { get; private set; }
        public bool Revoked => Volatile.Read(ref _fired) == 1;

        public void Arm(Guid requesterId, Guid callerId, Guid boardId)
        {
            RequesterId = requesterId;
            CallerId = callerId;
            BoardId = boardId;
            Armed = true;
        }

        /// <summary>
        /// Fires on the SECOND item's requester check - a call the executor makes whether or not
        /// the caller-side guard exists. Hanging the trigger on a guard-independent event is the
        /// point: it keeps this test honest as a mutation target, because deleting the guard must
        /// make the assertions about the OUTCOME fail, not merely stop the seam from arming.
        /// </summary>
        public bool TryClaimOnSecondRequesterCheck() =>
            Armed &&
            Interlocked.Increment(ref _requesterChecks) == 2 &&
            Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
    }

    /// <summary>
    /// Delegates every call, and immediately AFTER the first successful caller-side write check
    /// deletes the collaborator's board access - the exact instant "between items" that a second
    /// HTTP request cannot occupy. The policy engine reads board access from live database state,
    /// so the next item's recheck observes the revocation the way a real concurrent revoke would.
    /// </summary>
    private sealed class RevokingPolicyEngineDecorator : IAutomationPolicyEngine
    {
        private readonly IAutomationPolicyEngine _inner;
        private readonly TaskdeckDbContext _db;
        private readonly RevokeAfterFirstItem _revoker;

        public RevokingPolicyEngineDecorator(
            IAutomationPolicyEngine inner,
            TaskdeckDbContext db,
            RevokeAfterFirstItem revoker)
        {
            _inner = inner;
            _db = db;
            _revoker = revoker;
        }

        public RiskLevel ClassifyRisk(IEnumerable<ProposalOperationDto> operations) =>
            _inner.ClassifyRisk(operations);

        public Task<Result> ValidateBoardAccessAsync(
            Guid requesterUserId,
            Guid? boardId,
            BoardAccessBar accessBar,
            CancellationToken cancellationToken = default) =>
            _inner.ValidateBoardAccessAsync(requesterUserId, boardId, accessBar, cancellationToken);

        public Task<Result> GuardProposalDecisionWritesAsync(
            IEnumerable<Guid?> boardIds,
            CancellationToken cancellationToken = default) =>
            _inner.GuardProposalDecisionWritesAsync(boardIds, cancellationToken);

        public Result ValidateOperationStructure(IReadOnlyCollection<ProposalOperationDto> operations) =>
            _inner.ValidateOperationStructure(operations);

        public Result ValidatePolicy(ProposalDto proposal) => _inner.ValidatePolicy(proposal);

        public async Task<Result> ValidatePermissionsAsync(
            Guid userId,
            Guid? boardId,
            IEnumerable<ProposalOperationDto> operations,
            BoardAccessBar accessBar,
            CancellationToken cancellationToken = default)
        {
            var result = await _inner.ValidatePermissionsAsync(userId, boardId, operations, accessBar, cancellationToken);

            var isRequesterWriteCheck = userId == _revoker.RequesterId &&
                boardId == _revoker.BoardId &&
                accessBar == BoardAccessBar.Write &&
                result.IsSuccess;
            if (isRequesterWriteCheck && _revoker.TryClaimOnSecondRequesterCheck())
            {
                var access = await _db.BoardAccesses
                    .Where(row => row.BoardId == _revoker.BoardId && row.UserId == _revoker.CallerId)
                    .ToListAsync(cancellationToken);
                _db.BoardAccesses.RemoveRange(access);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
