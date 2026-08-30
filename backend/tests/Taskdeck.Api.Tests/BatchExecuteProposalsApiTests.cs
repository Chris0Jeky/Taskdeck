using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// End-to-end coverage for <c>POST /api/automation/proposals/execute</c> (#1307, q-14 C): a bounded
/// per-proposal batch execute with partial success, per-item idempotency, and per-item board access.
/// </summary>
public class BatchExecuteProposalsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly TestWebApplicationFactory _factory;

    public BatchExecuteProposalsApiTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteProposals_AppliesEveryApprovedItemAndReportsItsOwnOutcome()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-happy");
        var firstBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-first");
        var secondBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-second");
        var first = await CreateApprovedProposalAsync(client, user.UserId, firstBoardId, "First batch card");
        var second = await CreateApprovedProposalAsync(client, user.UserId, secondBoardId, "Second batch card");

        var receipt = await PostBatchAsync(client, HttpStatusCode.OK, Select(first), Select(second));

        receipt.Results.Should().HaveCount(2);
        receipt.Results.Should().OnlyContain(item => item.Outcome == BatchExecuteOutcome.Applied);
        receipt.Results.Select(item => item.ProposalId).Should().Equal(first.Id, second.Id);
        receipt.Results.Should().OnlyContain(item => item.AppliedOperations == 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == firstBoardId && card.Title == "First batch card"))
            .Should().Be(1);
        (await db.Cards.CountAsync(card => card.BoardId == secondBoardId && card.Title == "Second batch card"))
            .Should().Be(1);
        var persisted = await db.AutomationProposals
            .Where(proposal => proposal.Id == first.Id || proposal.Id == second.Id)
            .ToListAsync();
        persisted.Should().OnlyContain(proposal => proposal.Status == ProposalStatus.Applied);
        // Audit belongs to execute, and a batch must write exactly what single execute writes. A
        // create-card operation keys its audit row on the target column (ExecutionAuditRecorder).
        var firstColumnId = await GetColumnIdAsync(firstBoardId);
        var secondColumnId = await GetColumnIdAsync(secondBoardId);
        (await db.AuditLogs.CountAsync(log => log.EntityId == firstColumnId)).Should().Be(1);
        (await db.AuditLogs.CountAsync(log => log.EntityId == secondColumnId)).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteProposals_WhenOneItemFails_AppliesTheHealthyItemsAnyway()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-partial");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-partial-board");
        var healthy = await CreateApprovedProposalAsync(client, user.UserId, boardId, "Healthy card");
        // Never approved, so execute must refuse it - but only it.
        var notApproved = await CreatePendingProposalAsync(client, user.UserId, boardId, "Not approved card");
        var alsoHealthy = await CreateApprovedProposalAsync(client, user.UserId, boardId, "Second healthy card");

        var receipt = await PostBatchAsync(
            client,
            HttpStatusCode.OK,
            Select(healthy),
            Select(notApproved),
            Select(alsoHealthy));

        receipt.Results[0].Outcome.Should().Be(BatchExecuteOutcome.Applied);
        receipt.Results[1].Outcome.Should().Be(BatchExecuteOutcome.Failed);
        receipt.Results[1].ErrorCode.Should().Be("InvalidOperation");
        receipt.Results[2].Outcome.Should().Be(BatchExecuteOutcome.Applied);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        // No whole-batch rollback: the failing item did not undo its neighbours' board writes.
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Healthy card"))
            .Should().Be(1);
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Second healthy card"))
            .Should().Be(1);
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Not approved card"))
            .Should().Be(0);
    }

    [Fact]
    public async Task ExecuteProposals_ReplayIsIdempotentAndWritesNothingASecondTime()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-replay");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-replay-board");
        var proposal = await CreateApprovedProposalAsync(client, user.UserId, boardId, "Replayed card");
        var selection = Select(proposal);

        var first = await PostBatchAsync(client, HttpStatusCode.OK, selection);
        first.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Applied);

        // Same idempotency key, same pin: the replay must not apply a second time.
        var second = await PostBatchAsync(client, HttpStatusCode.OK, selection);
        var third = await PostBatchAsync(client, HttpStatusCode.OK, selection);

        second.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Skipped);
        second.Results.Single().ErrorCode.Should().BeNull();
        third.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Skipped);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == boardId && card.Title == "Replayed card"))
            .Should().Be(1, "three batch calls with the same keys must produce exactly one card");
        var columnId = await GetColumnIdAsync(boardId);
        (await db.AuditLogs.CountAsync(log => log.EntityId == columnId))
            .Should().Be(1, "a replay writes no additional audit rows");
    }

    [Fact]
    public async Task ExecuteProposals_ProposalOnAnotherUsersBoard_FailsOnlyThatItem()
    {
        var ownerClient = _factory.CreateClient();
        var strangerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "batch-exec-owner");
        var stranger = await ApiTestHarness.AuthenticateAsync(strangerClient, "batch-exec-stranger");

        var ownerBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "batch-exec-owner-board");
        var strangerBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(strangerClient, "batch-exec-stranger-board");
        var ownersProposal = await CreateApprovedProposalAsync(ownerClient, owner.UserId, ownerBoardId, "Owner card");
        var strangersProposal = await CreateApprovedProposalAsync(
            strangerClient, stranger.UserId, strangerBoardId, "Stranger card");

        // The stranger asks for one proposal they may execute and one they may not.
        var receipt = await PostBatchAsync(
            strangerClient,
            HttpStatusCode.OK,
            Select(strangersProposal),
            Select(ownersProposal));

        receipt.Results[0].Outcome.Should().Be(BatchExecuteOutcome.Applied);
        receipt.Results[1].Outcome.Should().Be(BatchExecuteOutcome.Failed);
        // NotFound, not Forbidden: the stranger cannot read that board at all, so the receipt must
        // not confirm that the proposal exists. A made-up id in the same slot answers identically.
        receipt.Results[1].ErrorCode.Should().Be("NotFound");

        var probe = await PostBatchAsync(
            strangerClient,
            HttpStatusCode.OK,
            Select(strangersProposal),
            new ExecuteProposalSelectionRequest
            {
                ProposalId = Guid.NewGuid(),
                ApprovedRevisionId = null,
                IdempotencyKey = Guid.NewGuid().ToString("N")
            });
        probe.Results[1].ErrorCode.Should().Be(receipt.Results[1].ErrorCode);
        probe.Results[1].ErrorMessage.Should().Be(receipt.Results[1].ErrorMessage);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == ownerBoardId))
            .Should().Be(0, "an unauthorized item must never write to the board it targets");
    }

    [Fact]
    public async Task ExecuteProposals_OnABoardTheCallerCanReadButNotWrite_ReportsForbidden()
    {
        var ownerClient = _factory.CreateClient();
        var viewerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "batch-exec-viewer-owner");
        var viewer = await ApiTestHarness.AuthenticateAsync(viewerClient, "batch-exec-viewer");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "batch-exec-viewer-board");

        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, viewer.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposal = await CreateApprovedProposalAsync(ownerClient, owner.UserId, boardId, "Viewer visible card");

        // The viewer can SEE this board, so its proposals' existence is not news. Hiding behind
        // NotFound here would be a lie about a row they are looking at.
        var response = await viewerClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = [Select(proposal)] });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteProposals_WhenNothingIsVisible_CollapsesToNotFoundNotForbidden()
    {
        var ownerClient = _factory.CreateClient();
        var strangerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "batch-exec-all-forbidden-owner");
        await ApiTestHarness.AuthenticateAsync(strangerClient, "batch-exec-all-forbidden-stranger");
        var ownerBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "batch-exec-forbidden-board");
        var ownersProposal = await CreateApprovedProposalAsync(ownerClient, owner.UserId, ownerBoardId, "Owner only card");

        var real = await strangerClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = [Select(ownersProposal)] });

        // 404, matching what single execute answers for an id the caller cannot see. Answering 403
        // would re-leak precisely what the per-item rows were changed to hide: that the id is real.
        real.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invented = await strangerClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest
                    {
                        ProposalId = Guid.NewGuid(),
                        ApprovedRevisionId = null,
                        IdempotencyKey = Guid.NewGuid().ToString("N")
                    }
                ]
            });
        invented.StatusCode.Should().Be(real.StatusCode, "a real foreign id and an invented one must answer alike");
    }

    [Fact]
    public async Task ExecuteProposals_WhenEveryItemIsForbidden_ReturnsWholeRequestForbidden()
    {
        var ownerClient = _factory.CreateClient();
        var viewerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "batch-exec-403-owner");
        var viewer = await ApiTestHarness.AuthenticateAsync(viewerClient, "batch-exec-403-viewer");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "batch-exec-403-board");

        // Read access makes the rows visible, so Forbidden is the honest answer and the whole
        // request still collapses to 403 - the other half of the collapse rule.
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, viewer.UserId, UserRole.Viewer));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await CreateApprovedProposalAsync(ownerClient, owner.UserId, boardId, "403 card one");
        var second = await CreateApprovedProposalAsync(ownerClient, owner.UserId, boardId, "403 card two");

        var response = await viewerClient.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = [Select(first), Select(second)] });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExecuteProposals_MissingApprovedRevisionIdKey_IsRejectedWith400()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-missing-pin");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-missing-pin-board");
        var proposal = await CreateApprovedProposalAsync(client, user.UserId, boardId, "No pin echo card");

        // The key is absent entirely - not null. An omitted echo must be a 400, never a silent
        // "approved from the original operations" claim that skips the drift gate.
        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new
            {
                proposals = new[]
                {
                    new { proposalId = proposal.Id, idempotencyKey = Guid.NewGuid().ToString("N") }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("approvedRevisionId");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteProposals_ExplicitNullApprovedRevisionIdIsAcceptedForAnUnpinnedProposal()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-null-pin");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-null-pin-board");
        var proposal = await CreateApprovedProposalAsync(client, user.UserId, boardId, "Unpinned card");
        proposal.ApprovedRevisionId.Should().BeNull("approving without a revision pins nothing");

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new
            {
                proposals = new[]
                {
                    new
                    {
                        proposalId = proposal.Id,
                        approvedRevisionId = (Guid?)null,
                        idempotencyKey = Guid.NewGuid().ToString("N")
                    }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = Deserialize(await response.Content.ReadAsStringAsync());
        receipt.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Applied);
    }

    [Fact]
    public async Task ExecuteProposals_StaleApprovedRevisionEcho_FailsThatItemWithConflict()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-stale-pin");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-exec-stale-pin-board");
        var proposal = await CreateApprovedProposalAsync(client, user.UserId, boardId, "Drifted card");

        var receipt = await PostBatchAsync(
            client,
            HttpStatusCode.OK,
            new ExecuteProposalSelectionRequest
            {
                ProposalId = proposal.Id,
                ApprovedRevisionId = Guid.NewGuid(),
                IdempotencyKey = Guid.NewGuid().ToString("N")
            });

        receipt.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Failed);
        receipt.Results.Single().ErrorCode.Should().Be("Conflict");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteProposals_PreviewEqualsApplyForEveryItemInTheBatch()
    {
        var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "batch-exec-parity");
        var firstBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-parity-first");
        var secondBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "batch-parity-second");
        var first = await CreateRevisedApprovedProposalAsync(
            client, user.UserId, firstBoardId, "First original", "First edited");
        var second = await CreateRevisedApprovedProposalAsync(
            client, user.UserId, secondBoardId, "Second original", "Second edited");

        // The preview each item's approval gate shows, read per item.
        var firstDiff = await ReadDiffAsync(client, first.Id);
        var secondDiff = await ReadDiffAsync(client, second.Id);
        firstDiff.Should().Contain("First edited").And.NotContain("First original");
        secondDiff.Should().Contain("Second edited").And.NotContain("Second original");

        var receipt = await PostBatchAsync(client, HttpStatusCode.OK, Select(first), Select(second));
        receipt.Results.Should().OnlyContain(item => item.Outcome == BatchExecuteOutcome.Applied);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        // Apply materialized each item's own pinned revision - the same content its preview showed.
        (await db.Cards.Where(card => card.BoardId == firstBoardId).Select(card => card.Title).ToListAsync())
            .Should().Equal("First edited");
        (await db.Cards.Where(card => card.BoardId == secondBoardId).Select(card => card.Title).ToListAsync())
            .Should().Equal("Second edited");
    }

    [Fact]
    public async Task ExecuteProposals_EmptySelection_IsRejectedWith400()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "batch-exec-empty");

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = [] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteProposals_OverTheBound_IsRejectedWith400()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "batch-exec-bound");

        // 501 items: one past the 500 bound batch approve already declares.
        var selections = Enumerable.Range(0, 501)
            .Select(_ => new ExecuteProposalSelectionRequest
            {
                ProposalId = Guid.NewGuid(),
                ApprovedRevisionId = null,
                IdempotencyKey = Guid.NewGuid().ToString("N")
            })
            .ToList();

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = selections });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("500");
    }

    [Fact]
    public async Task ExecuteProposals_DuplicateIdsDuplicateKeysAndMissingKeys_AreRejectedWith400()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "batch-exec-shape");
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        // The same proposal listed twice: two outcome rows for one proposal is a contradiction.
        var duplicateIds = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest { ProposalId = id, ApprovedRevisionId = null, IdempotencyKey = "a" },
                    new ExecuteProposalSelectionRequest { ProposalId = id, ApprovedRevisionId = null, IdempotencyKey = "b" }
                ]
            });
        duplicateIds.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicateIds.Content.ReadAsStringAsync()).Should().Contain("Proposal IDs must be unique");

        // Two DIFFERENT proposals sharing one key: the caller has claimed one idempotent identity
        // for two distinct applies. Rejected as malformed rather than silently accepted.
        var duplicateKeys = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest { ProposalId = id, ApprovedRevisionId = null, IdempotencyKey = "shared" },
                    new ExecuteProposalSelectionRequest { ProposalId = otherId, ApprovedRevisionId = null, IdempotencyKey = "shared" }
                ]
            });
        duplicateKeys.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicateKeys.Content.ReadAsStringAsync()).Should().Contain("Idempotency keys must be unique");

        // Keys differing only by case are distinct: the key is an opaque token, not a name.
        var caseDistinctKeys = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest { ProposalId = id, ApprovedRevisionId = null, IdempotencyKey = "Key" },
                    new ExecuteProposalSelectionRequest { ProposalId = otherId, ApprovedRevisionId = null, IdempotencyKey = "key" }
                ]
            });
        // Both proposals are unknown, so this passes request validation, reaches the service, and
        // collapses to 404 on the all-not-found rule. The point is the 404: it is NOT the 400 a
        // duplicate-key request earns, so keys differing only by case were treated as distinct.
        caseDistinctKeys.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var missingIdempotencyKey = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest { ProposalId = id, ApprovedRevisionId = null, IdempotencyKey = "  " }
                ]
            });
        missingIdempotencyKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteProposals_WithoutAuthentication_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest
            {
                Proposals =
                [
                    new ExecuteProposalSelectionRequest
                    {
                        ProposalId = Guid.NewGuid(),
                        ApprovedRevisionId = null,
                        IdempotencyKey = "k"
                    }
                ]
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static ExecuteProposalSelectionRequest Select(ProposalDto proposal) => new()
    {
        ProposalId = proposal.Id,
        ApprovedRevisionId = proposal.ApprovedRevisionId,
        IdempotencyKey = Guid.NewGuid().ToString("N")
    };

    private static async Task<BatchExecuteProposalsResultDto> PostBatchAsync(
        HttpClient client,
        HttpStatusCode expected,
        params ExecuteProposalSelectionRequest[] selections)
    {
        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/execute",
            new ExecuteProposalsRequest { Proposals = selections.ToList() });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return Deserialize(body);
    }

    private static BatchExecuteProposalsResultDto Deserialize(string body)
    {
        var receipt = JsonSerializer.Deserialize<BatchExecuteProposalsResultDto>(body, Web);
        receipt.Should().NotBeNull();
        return receipt!;
    }

    private static async Task<string> ReadDiffAsync(HttpClient client, Guid proposalId)
    {
        var response = await client.GetAsync($"/api/automation/proposals/{proposalId}/diff");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("diff").GetString()!;
    }

    private async Task<Guid> GetColumnIdAsync(Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        return await db.Columns
            .Where(column => column.BoardId == boardId)
            .Select(column => column.Id)
            .FirstAsync();
    }

    private async Task<ProposalDto> CreatePendingProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        string title)
    {
        var columnId = await GetColumnIdAsync(boardId);
        var createRequest = new CreateProposalDto(
            ProposalSourceType.Queue,
            userId,
            $"Batch execute {title}",
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
            });

        var response = await client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private async Task<ProposalDto> CreateApprovedProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        string title)
    {
        var proposal = await CreatePendingProposalAsync(client, userId, boardId, title);
        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        var body = await approveResponse.Content.ReadAsStringAsync();
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await approveResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private async Task<ProposalDto> CreateRevisedApprovedProposalAsync(
        HttpClient client,
        Guid userId,
        Guid boardId,
        string originalTitle,
        string revisedTitle)
    {
        var proposal = await CreatePendingProposalAsync(client, userId, boardId, originalTitle);
        var columnId = await GetColumnIdAsync(boardId);
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
                    parameters = JsonSerializer.Serialize(new { title = revisedTitle, boardId, columnId }),
                    idempotencyKey = Guid.NewGuid().ToString("N")
                }
            }
        });

        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposal.Id}/revisions",
            new { revisedPayload, reason = "Reviewer edit before approval" });
        var revisionBody = await revisionResponse.Content.ReadAsStringAsync();
        revisionResponse.StatusCode.Should().Be(HttpStatusCode.Created, revisionBody);

        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
        var approveBody = await approveResponse.Content.ReadAsStringAsync();
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK, approveBody);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
        approved.ApprovedRevisionId.Should().NotBeNull("approve pins the revision Apply must materialize");
        return approved;
    }
}
