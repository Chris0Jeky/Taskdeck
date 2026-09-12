using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class EstimateProposalsApiTests(HostedWorkerDisabledTestWebApplicationFactory factory) : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData(0, "0m")]
    [InlineData(90, "1h 30m")]
    [InlineData(1000000, "16666h 40m")]
    public async Task Create_PreviewAndApplyPreserveEstimateAndActor(int? estimate, string display)
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var cardId = Guid.NewGuid();
        var proposal = await CreateProposalAsync(client, userId, boardId,
            [Op(0, "create", cardId, new { boardId, columnId, title = "Estimated work", estimatedEffortMinutes = estimate })]);
        (await ReadDiffAsync(client, proposal.Id)).Should().Contain($"Effort estimate: (new card) -> {display}");
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards"))!.Should().BeEmpty();
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await ExecuteAsync(client, proposal.Id)).EnsureSuccessStatusCode();
        var card = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{cardId}"))!;
        card.EstimatedEffortMinutes.Should().Be(estimate);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var receipts = await db.AuditLogs.Where(log => log.EntityId == cardId).ToListAsync();
        // The service's proposal-lane row deliberately has no human actor; the
        // executor's provenance receipt owns authenticated proposal attribution.
        receipts.Where(log => log.Changes != null && log.Changes.Contains(proposal.Id.ToString()))
            .Should().ContainSingle().Which.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task OrderedUpdates_UseInitialPinAndPreviewWorkingState_AndReplayDoesNotWriteAgain()
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var card = await CreateCardAsync(client, boardId, columnId, 120);
        var proposal = await CreateProposalAsync(client, userId, boardId,
        [
            Op(4, "update", card.Id, new { cardId = card.Id, estimatedEffortMinutes = 0, expectedUpdatedAt = card.UpdatedAt }),
            Op(0, "update", card.Id, new { cardId = card.Id, estimatedEffortMinutes = 90, expectedUpdatedAt = card.UpdatedAt }),
            Op(1, "update", card.Id, new { cardId = card.Id, title = "Keep estimate", estimatedEffortMinutes = (int?)null, clearEstimatedEffort = false }),
            Op(2, "update", card.Id, new { cardId = card.Id, clearEstimatedEffort = true, expectedUpdatedAt = card.UpdatedAt }),
            Op(3, "update", card.Id, new { cardId = card.Id, clearEstimatedEffort = true, expectedUpdatedAt = card.UpdatedAt }),
            Op(5, "update", card.Id, new { cardId = card.Id, title = "Omitted estimate" })
        ]);
        var diff = await ReadDiffAsync(client, proposal.Id);
        var lines = diff.Split(Environment.NewLine);
        lines[0].Should().Contain("Effort estimate: 2h -> 1h 30m");
        lines[1].Should().NotContain("Effort estimate");
        lines[2].Should().Contain("Effort estimate: 1h 30m -> unknown");
        lines[3].Should().Contain("Effort estimate: unknown -> unknown");
        lines[4].Should().Contain("Effort estimate: unknown -> 0m");
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!.EstimatedEffortMinutes.Should().Be(120);
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        var applied = await ExecuteAsync(client, proposal.Id);
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var after = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!;
        after.EstimatedEffortMinutes.Should().Be(0);
        after.Title.Should().Be("Omitted estimate");
        var beforeReplay = await CountAuditsAsync(card.Id);
        (await ExecuteAsync(client, proposal.Id)).EnsureSuccessStatusCode();
        (await CountAuditsAsync(card.Id)).Should().Be(beforeReplay);
    }

    [Fact]
    public async Task PlannedCreateThenEstimateUpdates_NeedNoPersistedTimestamp()
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var cardId = Guid.NewGuid();
        var proposal = await CreateProposalAsync(client, userId, boardId,
        [
            Op(0, "create", cardId, new { boardId, columnId, title = "Planned card", estimatedEffortMinutes = 60 }),
            Op(1, "update", cardId, new { cardId, estimatedEffortMinutes = 0 }),
            Op(2, "update", cardId, new { cardId, clearEstimatedEffort = true })
        ]);
        var diff = await ReadDiffAsync(client, proposal.Id);
        diff.Should().Contain("Effort estimate: (new card) -> 1h").And.Contain("Effort estimate: 1h -> 0m").And.Contain("Effort estimate: 0m -> unknown");
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        var applied = await ExecuteAsync(client, proposal.Id);
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{cardId}"))!.EstimatedEffortMinutes.Should().BeNull();
    }

    [Fact]
    public async Task RevisedEstimate_PreviewAndApplyUseTheApprovedRevision()
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var card = await CreateCardAsync(client, boardId, columnId, 60);
        var proposal = await CreateProposalAsync(client, userId, boardId,
            [Op(0, "update", card.Id, new { cardId = card.Id, estimatedEffortMinutes = 90, expectedUpdatedAt = card.UpdatedAt })]);
        var revisedOperations = new[]
        {
            Op(0, "update", card.Id, new { cardId = card.Id, clearEstimatedEffort = true, expectedUpdatedAt = card.UpdatedAt }),
            Op(1, "update", card.Id, new { cardId = card.Id, estimatedEffortMinutes = 0, expectedUpdatedAt = card.UpdatedAt })
        };
        var revisedPayload = JsonSerializer.Serialize(new { operations = revisedOperations }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        (await client.PostAsJsonAsync($"/api/automation/proposals/{proposal.Id}/revisions", new { revisedPayload, reason = "Review the estimate" })).EnsureSuccessStatusCode();
        (await ReadDiffAsync(client, proposal.Id)).Should().Contain("Effort estimate: 1h -> unknown")
            .And.Contain("Effort estimate: unknown -> 0m").And.NotContain("1h 30m");
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await ExecuteAsync(client, proposal.Id)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!.EstimatedEffortMinutes.Should().Be(0);
    }

    [Theory]
    [InlineData("\"estimatedEffortMinutes\":true")]
    [InlineData("\"estimatedEffortMinutes\":1.5")]
    [InlineData("\"estimatedEffortMinutes\":1000001")]
    [InlineData("\"estimatedEffortMinutes\":0,\"clearEstimatedEffort\":true")]
    [InlineData("\"clearEstimatedEffort\":\"true\"")]
    public async Task RefusedEstimate_PreviewAndApplyRejectWithoutCardOrAuditWrites(string fields)
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var card = await CreateCardAsync(client, boardId, columnId, 90);
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, expectedUpdatedAt = card.UpdatedAt }).TrimEnd('}') + "," + fields + "}";
        var proposal = await CreateProposalAsync(client, userId, boardId,
            [new CreateProposalOperationDto(0, "update", "card", parameters, Guid.NewGuid().ToString(), card.Id.ToString())]);
        var auditCount = await CountAuditsAsync(card.Id);
        (await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Simulate a stored approved payload so the apply-time gate is exercised independently
        // of whichever create/approve surface recorded the validation issue.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var stored = await db.AutomationProposals.SingleAsync(p => p.Id == proposal.Id);
            stored.Approve(userId);
            await db.SaveChangesAsync();
        }
        (await ExecuteAsync(client, proposal.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unchanged = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!;
        unchanged.EstimatedEffortMinutes.Should().Be(90);
        unchanged.UpdatedAt.Should().Be(card.UpdatedAt);
        (await CountAuditsAsync(card.Id)).Should().Be(auditCount);
    }

    [Fact]
    public async Task StaleEstimatePin_RejectsWholeProposalBeforeFirstWrite()
    {
        using var client = factory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var first = await CreateCardAsync(client, boardId, columnId, null);
        var stale = await CreateCardAsync(client, boardId, columnId, 60);
        var proposal = await CreateProposalAsync(client, userId, boardId,
        [
            Op(0, "update", first.Id, new { cardId = first.Id, title = "Must not write" }),
            Op(1, "update", stale.Id, new { cardId = stale.Id, estimatedEffortMinutes = 90, expectedUpdatedAt = stale.UpdatedAt })
        ]);
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/boards/{boardId}/cards/{stale.Id}", new { estimatedEffortMinutes = 120, expectedUpdatedAt = stale.UpdatedAt })).EnsureSuccessStatusCode();
        var auditCount = await CountAuditsAsync(first.Id);
        (await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ExecuteAsync(client, proposal.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{first.Id}"))!.Title.Should().Be(first.Title);
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{stale.Id}"))!.EstimatedEffortMinutes.Should().Be(120);
        (await CountAuditsAsync(first.Id)).Should().Be(auditCount);
    }

    [Fact]
    public async Task ConcurrentWriterAfterValidation_UsesConcurrencyTokenAndRollsBackEarlierOperation()
    {
        var race = new EstimateRaceInterceptor();
        var saveObserver = new FirstOperationSaveObserver();
        using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
            services.AddDbContext<TaskdeckDbContext>((provider, options) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                options.UseTaskdeckSqlite(configuration.GetConnectionString("DefaultConnection")!,
                    configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings())
                    .AddInterceptors(race, saveObserver);
            });
        }));
        using var client = raceFactory.CreateClient();
        var (userId, boardId, columnId) = await SetupAsync(client);
        var first = await CreateCardAsync(client, boardId, columnId, null);
        var raced = await CreateCardAsync(client, boardId, columnId, 60);
        var proposal = await CreateProposalAsync(client, userId, boardId,
        [
            Op(0, "update", first.Id, new { cardId = first.Id, title = "Transactional first write" }),
            Op(1, "update", raced.Id, new { cardId = raced.Id, estimatedEffortMinutes = 90, expectedUpdatedAt = raced.UpdatedAt })
        ]);
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        saveObserver.CardId = first.Id;
        race.TargetId = raced.Id;
        race.BeforeTransaction = async () =>
        {
            using var writerScope = raceFactory.Services.CreateScope();
            var writer = writerScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var competing = await writer.Cards.SingleAsync(c => c.Id == raced.Id);
            competing.SetEstimatedEffortMinutes(120);
            await writer.SaveChangesAsync();
        };
        var applied = await ExecuteAsync(client, proposal.Id);
        applied.StatusCode.Should().Be(HttpStatusCode.Conflict, await applied.Content.ReadAsStringAsync());
        race.Fired.Should().BeTrue();
        saveObserver.SawFirstOperationSave.Should().BeTrue("the conflict must roll back an already-saved earlier operation");
        using var verifyScope = raceFactory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.SingleAsync(c => c.Id == first.Id)).Title.Should().Be(first.Title);
        (await db.Cards.SingleAsync(c => c.Id == raced.Id)).EstimatedEffortMinutes.Should().Be(120);
        (await db.AutomationProposals.SingleAsync(p => p.Id == proposal.Id)).Status.Should().NotBe(ProposalStatus.Applied);
        (await db.AuditLogs.Where(log => log.EntityId == first.Id || log.EntityId == raced.Id).ToListAsync())
            .Should().NotContain(log => log.Changes != null && log.Changes.Contains(proposal.Id.ToString()));
    }

    private sealed class EstimateRaceInterceptor : DbTransactionInterceptor
    {
        public Func<Task>? BeforeTransaction;
        public Guid TargetId { get; set; }
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Card>().Any(entry => entry.Entity.Id == TargetId) != true)
                return result;
            var callback = Interlocked.Exchange(ref BeforeTransaction, null);
            if (callback is not null)
            {
                await callback();
                Fired = true;
            }
            return result;
        }
    }

    private sealed class FirstOperationSaveObserver : SaveChangesInterceptor
    {
        public Guid CardId { get; set; }
        public bool SawFirstOperationSave { get; private set; }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Card>().Any(entry =>
                    entry.Entity.Id == CardId && entry.Entity.Title == "Transactional first write") == true)
                SawFirstOperationSave = true;
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<(Guid UserId, Guid BoardId, Guid ColumnId)> SetupAsync(HttpClient client)
    {
        var user = await ApiTestHarness.AuthenticateAsync(client, "estimate-proposal");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Estimate proposals");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        return (user.UserId, boardId, board.Columns.First().Id);
    }

    private static CreateProposalOperationDto Op(int sequence, string action, Guid id, object parameters) =>
        new(sequence, action, "card", JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), id.ToString());

    private static async Task<CardDto> CreateCardAsync(HttpClient client, Guid boardId, Guid columnId, int? estimate)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, "Original work", null, null, null, EstimatedEffortMinutes: estimate));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private static async Task<ProposalDto> CreateProposalAsync(HttpClient client, Guid userId, Guid boardId, List<CreateProposalOperationDto> operations)
    {
        var response = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, userId, "Estimate planned work", RiskLevel.Low, Guid.NewGuid().ToString(), boardId, Operations: operations));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static async Task<string> ReadDiffAsync(HttpClient client, Guid proposalId)
    {
        var response = await client.GetAsync($"/api/automation/proposals/{proposalId}/diff");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("diff").GetString()!;
    }

    private static async Task<HttpResponseMessage> ExecuteAsync(HttpClient client, Guid proposalId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private async Task<int> CountAuditsAsync(Guid cardId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().AuditLogs.CountAsync(log => log.EntityId == cardId);
    }
}
