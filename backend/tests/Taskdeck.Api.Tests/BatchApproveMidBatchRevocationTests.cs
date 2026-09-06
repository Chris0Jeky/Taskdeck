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
/// The batch-approve twin of <see cref="BatchExecuteMidBatchRevocationTests"/> (#1307).
///
/// Batch approve validates the caller's board-write bar in a preflight loop that runs BEFORE the
/// transaction opens. That snapshot is correct when it is taken and progressively less true
/// afterwards: the in-transaction decision guard bumps each board row, so it catches a concurrent
/// board-row mutation, but a grant revoked between the preflight and the commit changes no board
/// row and was therefore invisible. Batch execute rechecks the caller's bar inside each item's own
/// transaction; approve now rechecks it inside the batch's single transaction.
///
/// The race cannot be expressed over HTTP from the outside - there is no moment "after the
/// preflight, before the commit" that a second request can occupy deterministically - so it is
/// staged here with a policy-engine decorator that revokes the caller's access at exactly that
/// point, and then asserts through the ordinary public endpoint.
/// </summary>
public class BatchApproveMidBatchRevocationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BatchApproveMidBatchRevocationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ApproveProposals_WhenCallerAccessIsRevokedAfterPreflight_FailsWholeBatchAndApprovesNothing()
    {
        var revoker = new RevokeAfterPreflight();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(revoker);
                services.AddScoped<IAutomationPolicyEngine>(sp => new RevokingPolicyEngineDecorator(
                    new AutomationPolicyEngine(sp.GetRequiredService<IUnitOfWork>()),
                    sp.GetRequiredService<TaskdeckDbContext>(),
                    sp.GetRequiredService<RevokeAfterPreflight>()));
            }));

        var ownerClient = factory.CreateClient();
        var reviewerClient = factory.CreateClient();
        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "batchapprove-midbatch-owner");
        var reviewer = await ApiTestHarness.AuthenticateAsync(reviewerClient, "batchapprove-midbatch-reviewer");

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "batchapprove-midbatch-board");
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, reviewer.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Batch approve is limited to the caller's own proposals, so the reviewer is both the author
        // and the batch submitter here; the board belongs to someone else, which is what makes the
        // reviewer's write grant revocable at all.
        var first = await CreateBatchApprovalProposalAsync(reviewerClient, factory, reviewer.UserId, boardId);
        var second = await CreateBatchApprovalProposalAsync(reviewerClient, factory, reviewer.UserId, boardId);

        // Two proposals, one board: the service's preflight makes exactly two write-bar checks.
        revoker.Arm(reviewer.UserId, boardId, preflightCheckCount: 2);

        var response = await reviewerClient.PostAsJsonAsync(
            "/api/automation/proposals/approve",
            new ApproveProposalsRequest
            {
                Proposals =
                [
                    Select(first),
                    Select(second)
                ]
            });

        revoker.Revoked.Should().BeTrue("the test seam must actually have revoked access after the preflight");
        revoker.ObservedCallerWriteChecks.Should().Be(
            revoker.PreflightCheckCount,
            "the seam must have fired on the LAST preflight write-bar check; another caller write-bar check in the batch approve path would move the trigger earlier and let the request answer 403 from the preflight even with the in-transaction recheck deleted");
        await ApiTestHarness.AssertForbiddenAsync(response);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var statuses = await db.AutomationProposals
            .Where(proposal => proposal.Id == first.Id || proposal.Id == second.Id)
            .Select(proposal => proposal.Status)
            .ToListAsync();
        statuses.Should().HaveCount(2);
        statuses.Should().OnlyContain(
            status => status == ProposalStatus.PendingReview,
            "an all-or-none batch that loses the write bar before its commit must leave every proposal undecided");
        (await db.Notifications.CountAsync(notification =>
                notification.Type == NotificationType.ProposalOutcome &&
                (notification.SourceEntityId == first.Id || notification.SourceEntityId == second.Id)))
            .Should().Be(0, "the outcome notifications share the rolled-back transaction");
        (await db.Cards.CountAsync(card => card.BoardId == boardId))
            .Should().Be(0, "approval never applies operations");
    }

    private static ApproveProposalSelectionRequest Select(ProposalDto proposal) => new()
    {
        Id = proposal.Id,
        ExpectedProposalUpdatedAt = proposal.UpdatedAt,
        ExpectedLatestRevisionId = proposal.LatestRevisionId
    };

    private static async Task<ProposalDto> CreateBatchApprovalProposalAsync(
        HttpClient client,
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid boardId)
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
                $"Batch approve revocation {Guid.NewGuid():N}",
                RiskLevel.Low,
                Guid.NewGuid().ToString(),
                boardId,
                Operations: new List<CreateProposalOperationDto>
                {
                    new(
                        0,
                        "create",
                        "card",
                        JsonSerializer.Serialize(new { title = "Batch approve revocation task", boardId, columnId }),
                        Guid.NewGuid().ToString())
                }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        return (await createResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    /// <summary>Shared arming state; a singleton so it survives the per-request scopes.</summary>
    private sealed class RevokeAfterPreflight
    {
        private int _observedCallerWriteChecks;
        private int _fired;

        public Guid CallerId { get; private set; }

        public Guid BoardId { get; private set; }

        /// <summary>How many preflight write-bar checks the batch makes before its transaction.</summary>
        public int PreflightCheckCount { get; private set; }

        public bool Armed { get; private set; }

        public bool Revoked => Volatile.Read(ref _fired) == 1;

        /// <summary>
        /// Every caller write-bar <c>ValidatePermissionsAsync</c> observed since arming, counted
        /// regardless of its own outcome. The test asserts this equals
        /// <see cref="PreflightCheckCount"/>: if a future change adds another caller write-bar check
        /// to the batch approve path, the trigger would fire on an EARLIER check and the later one
        /// would then fail at the preflight, producing the same 403 with nothing approved even
        /// without the in-transaction recheck. Counting outcome-independently is what makes that
        /// drift visible - a check that fails because this seam already revoked access still counts.
        /// </summary>
        public int ObservedCallerWriteChecks => Volatile.Read(ref _observedCallerWriteChecks);

        public void Arm(Guid callerId, Guid boardId, int preflightCheckCount)
        {
            CallerId = callerId;
            BoardId = boardId;
            PreflightCheckCount = preflightCheckCount;
            Armed = true;
        }

        /// <summary>
        /// Records one observed caller write-bar check and reports whether it is the LAST preflight
        /// check - a call the service makes whether or not the in-transaction recheck exists, and
        /// one the earlier items have already passed. Hanging the trigger on a recheck-independent
        /// event is the point: it keeps this test honest as a mutation target, because deleting the
        /// recheck must make the assertions about the OUTCOME fail, not merely stop the seam from
        /// arming.
        /// </summary>
        public bool TryClaimOnLastPreflightCheck(bool succeeded)
        {
            if (!Armed)
                return false;

            var observed = Interlocked.Increment(ref _observedCallerWriteChecks);
            return succeeded &&
                observed == PreflightCheckCount &&
                Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
        }
    }

    /// <summary>
    /// Delegates every call, and immediately AFTER the batch's last preflight write-bar check
    /// deletes the reviewer's board access - the exact instant "after the preflight, before the
    /// commit" that a second HTTP request cannot occupy. The preflight runs before the transaction
    /// opens, so the deletion commits on its own. The policy engine reads board access from live
    /// database state, so the in-transaction recheck observes the revocation the way a real
    /// concurrent revoke would.
    ///
    /// Only <see cref="ValidatePermissionsAsync"/> carries the trigger. The recheck under test
    /// calls <see cref="ValidateBoardAccessAsync"/>, which passes straight through, so the trigger
    /// cannot be tripped by the code it is meant to observe.
    /// </summary>
    private sealed class RevokingPolicyEngineDecorator : IAutomationPolicyEngine
    {
        private readonly IAutomationPolicyEngine _inner;
        private readonly TaskdeckDbContext _db;
        private readonly RevokeAfterPreflight _revoker;

        public RevokingPolicyEngineDecorator(
            IAutomationPolicyEngine inner,
            TaskdeckDbContext db,
            RevokeAfterPreflight revoker)
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

            var isCallerWriteCheck = userId == _revoker.CallerId &&
                boardId == _revoker.BoardId &&
                accessBar == BoardAccessBar.Write;
            if (isCallerWriteCheck && _revoker.TryClaimOnLastPreflightCheck(result.IsSuccess))
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
