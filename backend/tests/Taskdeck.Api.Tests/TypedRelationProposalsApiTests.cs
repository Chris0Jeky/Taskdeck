using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class TypedRelationProposalsApiTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Fact]
    public async Task RelationProposal_AppliesCanonicalEdgeAndPreservesRequesterAndApplierProvenance()
    {
        using var requesterClient = factory.CreateClient();
        var (requester, boardId, columnId) = await SetupAsync(requesterClient, "relation-requester");
        var source = await CreateCardAsync(requesterClient, boardId, columnId, "Depends on target");
        var target = await CreateCardAsync(requesterClient, boardId, columnId, "Target");
        using var applierClient = factory.CreateClient();
        var applier = await ApiTestHarness.AuthenticateAsync(applierClient, "relation-applier");
        (await requesterClient.PostAsJsonAsync($"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, applier.UserId, UserRole.Editor))).EnsureSuccessStatusCode();
        var observed = await GetRelationsAsync(requesterClient, boardId);
        var proposal = await CreateProposalAsync(requesterClient, requester.UserId, boardId,
            [RelationOp(0, "add-relation", boardId, source.Id, target.Id, "depends-on", observed.Revision)]);

        (await applierClient.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await ExecuteAsync(applierClient, proposal.Id)).EnsureSuccessStatusCode();

        var applied = await GetRelationsAsync(requesterClient, boardId);
        applied.Revision.Should().Be(observed.Revision + 1);
        applied.Relations.Should().Equal(new CardRelationEdge(target.Id, source.Id, "blocks"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var receipt = (await db.AuditLogs.Where(log => log.Changes != null && log.Changes.Contains(proposal.Id.ToString()))
            .ToListAsync()).Should().ContainSingle().Subject;
        receipt.UserId.Should().Be(applier.UserId);
        receipt.Changes.Should().Contain($"requested by user {requester.UserId}");
    }

    [Fact]
    public async Task PlannedCreatesThenRelation_ApplyInOrderWithoutPersistedEndpoints()
    {
        using var client = factory.CreateClient();
        var (user, boardId, columnId) = await SetupAsync(client, "relation-planned-create");
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var proposal = await CreateProposalAsync(client, user.UserId, boardId,
        [
            CardOp(0, "create", sourceId, new { boardId, columnId, title = "Planned source" }),
            CardOp(1, "create", targetId, new { boardId, columnId, title = "Planned target" }),
            RelationOp(2, "add-relation", boardId, sourceId, targetId, "relates-to", expectedRevision: 0)
        ]);

        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await ExecuteAsync(client, proposal.Id)).EnsureSuccessStatusCode();

        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{sourceId}"))!.Title.Should().Be("Planned source");
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{targetId}"))!.Title.Should().Be("Planned target");
        (await GetRelationsAsync(client, boardId)).Relations.Should()
            .Equal(CardRelationRules.Normalize(new CardRelationEdge(sourceId, targetId, "relates-to")));
    }

    [Fact]
    public async Task CompetingRelationAfterInitialValidation_RollsBackEarlierSavedEditWithoutAuditOrRealtime()
    {
        var realtime = new Mock<IBoardRealtimeNotifier>();
        var relationRace = new RelationRaceInterceptor();
        var firstSave = new FirstOperationSaveObserver();
        realtime.Setup(notifier => notifier.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var raceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TaskdeckDbContext>>();
            services.AddDbContext<TaskdeckDbContext>((provider, options) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                options.UseTaskdeckSqlite(configuration.GetConnectionString("DefaultConnection")!,
                    configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings())
                    .AddInterceptors(relationRace, firstSave);
            });
            services.RemoveAll<IBoardRealtimeNotifier>();
            services.AddSingleton(realtime.Object);
        }));
        using var client = raceFactory.CreateClient();
        var (user, boardId, columnId) = await SetupAsync(client, "relation-stale-rollback");
        var unrelated = await CreateCardAsync(client, boardId, columnId, "Unchanged unrelated");
        var source = await CreateCardAsync(client, boardId, columnId, "Source");
        var target = await CreateCardAsync(client, boardId, columnId, "Stale target");
        var competingTarget = await CreateCardAsync(client, boardId, columnId, "Competing target");
        var observed = await GetRelationsAsync(client, boardId);
        var staleProposal = await CreateProposalAsync(client, user.UserId, boardId,
        [
            CardOp(0, "update", unrelated.Id, new { cardId = unrelated.Id, title = "Must roll back" }),
            RelationOp(1, "add-relation", boardId, source.Id, target.Id, "blocks", observed.Revision)
        ]);
        (await client.PostAsync($"/api/automation/proposals/{staleProposal.Id}/approve", null)).EnsureSuccessStatusCode();
        relationRace.TargetCardId = unrelated.Id;
        relationRace.BeforeTransaction = async () =>
        {
            using var competingScope = raceFactory.Services.CreateScope();
            var repository = competingScope.ServiceProvider.GetRequiredService<IBoardDependencyRepository>();
            var graph = await repository.GetAsync(boardId, CancellationToken.None) ?? new BoardDependencies(boardId);
            var expectedRevision = graph.Revision;
            graph.ReplaceRelations([new CardRelationEdge(source.Id, competingTarget.Id, "blocks")]);
            (await repository.SaveAsync(graph, expectedRevision, CancellationToken.None)).Should().BeTrue();
        };
        realtime.Invocations.Clear();

        var failed = await ExecuteAsync(client, staleProposal.Id);
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict, await failed.Content.ReadAsStringAsync());
        relationRace.Fired.Should().BeTrue("the competing relation must land after proposal validation and before its handlers");
        firstSave.SawFirstOperationSave.Should().BeTrue("the earlier card update must be saved inside the outer transaction before the relation conflict");
        realtime.Verify(notifier => notifier.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{unrelated.Id}"))!.Title.Should().Be(unrelated.Title);
        (await GetRelationsAsync(client, boardId)).Relations.Should()
            .Equal(new CardRelationEdge(source.Id, competingTarget.Id, "blocks"));
        using var scope = raceFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.AutomationProposals.SingleAsync(proposal => proposal.Id == staleProposal.Id)).Status.Should().NotBe(ProposalStatus.Applied);
        (await db.AuditLogs.Where(log => log.Changes != null && log.Changes.Contains(staleProposal.Id.ToString())).ToListAsync())
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("archive-lifecycle")]
    [InlineData("delete")]
    public async Task RelationProposal_RefusesMixedLifecycleOrDeleteOperations(string conflictingAction)
    {
        using var client = factory.CreateClient();
        var (user, boardId, columnId) = await SetupAsync(client, $"relation-mixed-{conflictingAction}");
        var source = await CreateCardAsync(client, boardId, columnId, "Source");
        var target = await CreateCardAsync(client, boardId, columnId, "Target");
        var relation = RelationOp(0, "add-relation", boardId, source.Id, target.Id, "blocks", expectedRevision: 0);
        object conflictParameters = conflictingAction == "archive-lifecycle"
            ? new { cardId = source.Id, expectedUpdatedAt = source.UpdatedAt }
            : new { cardId = source.Id };
        var conflict = CardOp(1, conflictingAction, source.Id, conflictParameters);
        var response = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, user.UserId, "Invalid mixed relation", RiskLevel.Medium,
            Guid.NewGuid().ToString(), boardId, Operations: [relation, conflict]));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var proposal = await response.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        var rejected = await client.PostAsync($"/api/automation/proposals/{proposal!.Id}/approve", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, await rejected.Content.ReadAsStringAsync());
        (await GetRelationsAsync(client, boardId)).Relations.Should().BeEmpty();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{source.Id}"))!.IsArchived.Should().BeFalse();
    }

    private static async Task<(TestUserContext User, Guid BoardId, Guid ColumnId)> SetupAsync(HttpClient client, string userName)
    {
        var user = await ApiTestHarness.AuthenticateAsync(client, userName);
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, $"{userName} board");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        return (user, boardId, board.Columns.Single().Id);
    }

    private static async Task<CardDto> CreateCardAsync(HttpClient client, Guid boardId, Guid columnId, string title)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private static CreateProposalOperationDto RelationOp(
        int sequence, string action, Guid boardId, Guid sourceId, Guid targetId, string relationType, long expectedRevision) =>
        new(sequence, action, "card", JsonSerializer.Serialize(new
        {
            boardId,
            cardId = sourceId,
            relatedCardId = targetId,
            relationType,
            expectedRevision
        }), Guid.NewGuid().ToString(), sourceId.ToString());

    private static CreateProposalOperationDto CardOp(int sequence, string action, Guid cardId, object parameters) =>
        new(sequence, action, "card", JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), cardId.ToString());

    private static async Task<ProposalDto> CreateProposalAsync(
        HttpClient client, Guid userId, Guid boardId, List<CreateProposalOperationDto> operations)
    {
        var response = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, userId, "Typed relation proposal", RiskLevel.Medium,
            Guid.NewGuid().ToString(), boardId, Operations: operations));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private static async Task<BoardRelationsDto> GetRelationsAsync(HttpClient client, Guid boardId) =>
        (await client.GetFromJsonAsync<BoardRelationsDto>($"/api/boards/{boardId}/relations"))!;

    private static async Task<HttpResponseMessage> ExecuteAsync(HttpClient client, Guid proposalId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private static async Task<int> CountProposalsAsync(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().AutomationProposals.CountAsync();
    }

    private sealed class RelationRaceInterceptor : DbTransactionInterceptor
    {
        public Func<Task>? BeforeTransaction;
        public Guid TargetCardId { get; set; }
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Card>().Any(entry => entry.Entity.Id == TargetCardId) != true)
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
        public bool SawFirstOperationSave { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Card>().Any(entry => entry.Entity.Title == "Must roll back") == true)
                SawFirstOperationSave = true;
            return ValueTask.FromResult(result);
        }
    }
}
