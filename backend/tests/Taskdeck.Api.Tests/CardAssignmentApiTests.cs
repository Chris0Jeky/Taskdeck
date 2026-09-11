using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardAssignmentApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task LaterProposalFailureRollsBackEarlierAssignmentAndEmitsNoAssignmentEvent()
    {
        var notifier = new Mock<IBoardRealtimeNotifier>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Guid rejectedId = Guid.Empty;
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBoardRealtimeNotifier>(); services.AddSingleton(notifier.Object);
            services.RemoveAll<ICardAssignmentStore>();
            services.AddScoped<ICardAssignmentStore>(p => new RejectCardStore(new CardAssignmentStore(p.GetRequiredService<TaskdeckDbContext>()), rejectedId));
        }));
        using var client = isolated.CreateClient(); var actor = await ApiTestHarness.AuthenticateAsync(client, "assignment-proposal-rollback");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Atomic assignments");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var cards = new List<CardDto>();
        foreach (var title in new[] { "First", "Second" })
        {
            var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, title, null, null, null));
            response.EnsureSuccessStatusCode(); cards.Add((await response.Content.ReadFromJsonAsync<CardDto>())!);
        }
        var responseProposal = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(ProposalSourceType.Manual,
            actor.UserId, "Assign both", RiskLevel.Medium, Guid.NewGuid().ToString(), boardId,
            Operations: cards.Select((card, index) => new CreateProposalOperationDto(index, ProposalAssignmentContract.Action, "card",
                JsonSerializer.Serialize(new { cardId = card.Id, userIds = new[] { actor.UserId }, expectedUpdatedAt = card.UpdatedAt }),
                Guid.NewGuid().ToString(), card.Id.ToString())).ToList()));
        responseProposal.EnsureSuccessStatusCode(); var proposal = (await responseProposal.Content.ReadFromJsonAsync<ProposalDto>())!;
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        rejectedId = cards[1].Id; notifier.Invocations.Clear();
        using var execute = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        execute.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.SendAsync(execute)).IsSuccessStatusCode.Should().BeFalse();
        foreach (var card in cards)
            (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verification = isolated.Services.CreateScope(); var db = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var ids = cards.Select(c => c.Id).ToArray();
        (await db.AuditLogs.AnyAsync(a => ids.Contains(a.EntityId) && a.Changes != null && a.Changes.Contains("assignment-replace"))).Should().BeFalse();
    }

    private sealed class RejectCardStore(ICardAssignmentStore inner, Guid rejectedId) : ICardAssignmentStore
    {
        public Task RefreshAuthorityAsync(Guid boardId, Guid actorId, CancellationToken ct) => inner.RefreshAuthorityAsync(boardId, actorId, ct);
        public Task<Card?> ReadCardAsync(Guid boardId, Guid cardId, CancellationToken ct) => cardId == rejectedId
            ? throw new DomainException(ErrorCodes.Conflict, "Synthetic later-operation failure") : inner.ReadCardAsync(boardId, cardId, ct);
        public Task<IReadOnlyList<User>> ReadParticipantsAsync(Guid boardId, CancellationToken ct) => inner.ReadParticipantsAsync(boardId, ct);
        public Task<IReadOnlyList<Card>> ReadAssignedCardsAsync(Guid userId, Guid? boardId, CancellationToken ct) => inner.ReadAssignedCardsAsync(userId, boardId, ct);
    }

    [Fact]
    public async Task PreviewRollsBackAllStagedRowsAndPublishesNoMutation()
    {
        var notifier = new Mock<IBoardRealtimeNotifier>();
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBoardRealtimeNotifier>(); services.AddSingleton(notifier.Object);
        }));
        using var client = isolated.CreateClient(); var actor = await ApiTestHarness.AuthenticateAsync(client, "assignment-preview");
        async Task<int[]> Counts()
        {
            using var scope = isolated.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            return [await db.Boards.CountAsync(), await db.Columns.CountAsync(), await db.Cards.CountAsync(),
                await db.Set<CardAssignment>().CountAsync(), await db.AuditLogs.CountAsync()];
        }
        var before = await Counts();
        var input = new ImportBoardDto("Preview only", null, [new("Next", 0, null)],
            [new("Preview assignment", null, "Next", 0, null, [], SourceAssignees: [new("source", "Source person")])], [],
            AssigneeMappings: new Dictionary<string, Guid?> { ["source"] = actor.UserId });
        (await client.PostAsJsonAsync("/api/import/boards/preview", input)).EnsureSuccessStatusCode();
        (await Counts()).Should().Equal(before);
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        var malformed = input with { Cards = input.Cards.Concat([new ImportCardDto("Invalid card", null, "Missing column", 1, null, [])]) };
        (await client.PostAsJsonAsync("/api/import/boards", malformed)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Counts()).Should().Equal(before);
    }
    private async Task<(HttpClient Client, TestUserContext Actor, CardDto Card)> CreateAsync()
    {
        var client = factory.CreateClient();
        var actor = await ApiTestHarness.AuthenticateAsync(client, "assignment");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Assignment proof");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns[0].Id, "Responsibility", null, null, null));
        response.EnsureSuccessStatusCode();
        return (client, actor, (await response.Content.ReadFromJsonAsync<CardDto>())!);
    }

    [Fact]
    public async Task AuditFailureRollsBackAssignmentAndPublishesNothing()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var brokenAudit = new Mock<IAuditLogRepository>();
        brokenAudit.Setup(a => a.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "Synthetic audit failure"));
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(u => u.Boards).Returns(real.Boards);
        uow.SetupGet(u => u.Users).Returns(real.Users);
        uow.SetupGet(u => u.AuditLogs).Returns(brokenAudit.Object);
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.BeginTransactionAsync(ct));
        uow.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.RollbackTransactionAsync(ct));
        uow.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.CommitTransactionAsync(ct));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var service = new CardAssignmentService(uow.Object, scope.ServiceProvider.GetRequiredService<ICardAssignmentStore>(),
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), notifier.Object);
        var result = await service.ReplaceAsync(card.BoardId, card.Id, new ReplaceCardAssignmentsDto([actor.UserId], card.UpdatedAt), actor.UserId);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
        using var verification = factory.Services.CreateScope();
        (await verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>().AuditLogs
            .AnyAsync(a => a.EntityId == card.Id && a.Changes != null && a.Changes.Contains("assignment-replace"))).Should().BeFalse();
    }

    [Fact]
    public async Task RealtimeObserverSeesCommittedAssignmentAndAudit()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (BoardRealtimeEvent _, CancellationToken ct) =>
            {
                using var observer = factory.Services.CreateScope();
                var db = observer.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                (await db.Set<CardAssignment>().AnyAsync(a => a.CardId == card.Id, ct)).Should().BeTrue();
                (await db.AuditLogs.AnyAsync(a => a.EntityId == card.Id && a.Changes != null && a.Changes.Contains("assignment-replace"), ct)).Should().BeTrue();
            });
        var service = ActivatorUtilities.CreateInstance<CardAssignmentService>(scope.ServiceProvider, notifier.Object);
        (await service.ReplaceAsync(card.BoardId, card.Id, new ReplaceCardAssignmentsDto([actor.UserId], card.UpdatedAt), actor.UserId)).IsSuccess.Should().BeTrue();
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task McpAssignmentWriteOnlyCreatesProposalAndParticipantReadHasNoDirectoryFields()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var userContext = new Mock<IUserContextProvider>(); userContext.Setup(u => u.GetCurrentUserIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.UserId);
        var tools = ActivatorUtilities.CreateInstance<WriteTools>(scope.ServiceProvider, userContext.Object);
        var result = await tools.ReplaceCardAssignments(card.BoardId.ToString(), card.Id.ToString(), [actor.UserId.ToString()], card.UpdatedAt.ToString("O"));
        result.Should().Contain("proposal");
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
        var readers = ActivatorUtilities.CreateInstance<ReadTools>(scope.ServiceProvider, userContext.Object);
        var people = await readers.ListBoardParticipants(card.BoardId.ToString());
        people.Should().Contain(actor.Username).And.NotContain(actor.Email).And.NotContain("password");
    }

    [Fact]
    public async Task AccountExportsPreserveAssignmentsAndRespectBoardScope()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        var saved = await client.PutAsJsonAsync($"/api/boards/{card.BoardId}/cards/{card.Id}/assignments",
            new ReplaceCardAssignmentsDto([actor.UserId], card.UpdatedAt));
        saved.EnsureSuccessStatusCode(); card = (await saved.Content.ReadFromJsonAsync<CardDto>())!;
        (await client.PostAsJsonAsync($"/api/boards/{card.BoardId}/cards/{card.Id}/archive", new CardLifecycleDto(card.UpdatedAt))).EnsureSuccessStatusCode();
        using var outsider = factory.CreateClient(); await ApiTestHarness.AuthenticateAsync(outsider, "assignment-export-outsider");
        foreach (var path in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var export = await client.GetFromJsonAsync<JsonElement>(path);
            var exported = export.GetProperty("data").GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == card.Id);
            exported.GetProperty("assignments").GetArrayLength().Should().Be(1);
            exported.GetProperty("assignments")[0].GetProperty("userId").GetGuid().Should().Be(actor.UserId);
            exported.GetProperty("assignments")[0].GetProperty("assignedByUserId").GetGuid().Should().Be(actor.UserId);
            exported.GetProperty("isArchived").GetBoolean().Should().BeTrue();
            var other = await outsider.GetFromJsonAsync<JsonElement>(path);
            other.GetProperty("data").GetProperty("cards").EnumerateArray().Should().NotContain(c => c.GetProperty("id").GetGuid() == card.Id);
        }
        var deletion = await client.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));
        deletion.EnsureSuccessStatusCode();
        (await deletion.Content.ReadFromJsonAsync<AccountDeletionResultDto>())!.CardAssignmentsRemoved.Should().Be(1);
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.Set<CardAssignment>().AnyAsync(a => a.UserId == actor.UserId)).Should().BeFalse();
        (await database.Cards.SingleAsync(c => c.Id == card.Id)).IsArchived.Should().BeTrue();
        (await database.Users.SingleAsync(u => u.Id == actor.UserId)).IsActive.Should().BeFalse();
        (await database.AuditLogs.AnyAsync(a => a.EntityId == card.Id && a.Changes != null && a.Changes.Contains("account-erased"))).Should().BeTrue();
    }

    [Fact]
    public async Task UnauthorizedCrossBoardArchivedAndInactiveTargetsCannotChangeSet()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        var url = $"/api/boards/{card.BoardId}/cards/{card.Id}/assignments";
        using var anonymous = factory.CreateClient();
        (await anonymous.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([actor.UserId], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var otherBoard = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Other scope");
        (await client.PutAsJsonAsync($"/api/boards/{otherBoard}/cards/{card.Id}/assignments",
            new ReplaceCardAssignmentsDto([actor.UserId], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var inactiveClient = factory.CreateClient(); var inactive = await ApiTestHarness.AuthenticateAsync(inactiveClient, "inactive-assignee");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(card.BoardId, inactive.UserId, UserRole.Editor, actor.UserId));
            (await db.Users.FindAsync(inactive.UserId))!.Deactivate(); await db.SaveChangesAsync();
        }
        (await client.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([inactive.UserId], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var archived = await client.PostAsJsonAsync($"/api/boards/{card.BoardId}/cards/{card.Id}/archive", new CardLifecycleDto(card.UpdatedAt));
        archived.EnsureSuccessStatusCode(); var archivedCard = (await archived.Content.ReadFromJsonAsync<CardDto>())!;
        (await client.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([actor.UserId], archivedCard.UpdatedAt))).IsSuccessStatusCode.Should().BeFalse();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ProposalPreviewAndApplyUseExplicitSetAndExecutingActor(bool invalidate, bool batch)
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var other = factory.CreateClient(); var participant = await ApiTestHarness.AuthenticateAsync(other, "assignment-person");
        using var executingClient = factory.CreateClient(); var executing = await ApiTestHarness.AuthenticateAsync(executingClient, "assignment-executor");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(card.BoardId, participant.UserId, UserRole.Viewer, actor.UserId));
            db.BoardAccesses.Add(new BoardAccess(card.BoardId, executing.UserId, UserRole.Editor, actor.UserId));
            await db.SaveChangesAsync();
        }
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, userIds = new[] { participant.UserId }, expectedUpdatedAt = card.UpdatedAt });
        var created = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(ProposalSourceType.Manual,
            actor.UserId, "Assign responsibility", RiskLevel.Medium, Guid.NewGuid().ToString(), card.BoardId,
            Operations: [new(0, ProposalAssignmentContract.Action, "card", parameters, Guid.NewGuid().ToString(), card.Id.ToString())]));
        created.EnsureSuccessStatusCode(); var proposal = (await created.Content.ReadFromJsonAsync<ProposalDto>())!;
        var diff = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diff.EnsureSuccessStatusCode(); (await diff.Content.ReadAsStringAsync()).Should().Contain("Unassigned").And.Contain(participant.Username);
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
        if (invalidate)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Users.FindAsync(participant.UserId))!.Deactivate(); await db.SaveChangesAsync();
        }
        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        apply.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var applied = batch
            ? await executingClient.PostAsJsonAsync("/api/automation/proposals/execute", new { proposals = new[] {
                new { proposalId = proposal.Id, approvedRevisionId = proposal.ApprovedRevisionId, idempotencyKey = Guid.NewGuid().ToString() } } })
            : await executingClient.SendAsync(apply);
        if (!invalidate) applied.IsSuccessStatusCode.Should().BeTrue(await applied.Content.ReadAsStringAsync());
        var saved = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!;
        if (invalidate) saved.Assignments.Should().BeEmpty();
        else saved.Assignments.Should().ContainSingle(a => a.UserId == participant.UserId && a.AssignedByUserId == executing.UserId);
    }

    [Fact]
    public async Task AssignmentOperationWithoutExecutingPrincipalFailsClosed()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var registry = ActivatorUtilities.CreateInstance<OperationHandlerRegistry>(scope.ServiceProvider);
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, userIds = new[] { actor.UserId }, expectedUpdatedAt = card.UpdatedAt });
        var result = await registry.ExecuteOperationAsync(new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0,
            ProposalAssignmentContract.Action, "card", card.Id.ToString(), parameters, "assignment", null), default);
        result.IsSuccess.Should().BeFalse(); result.ErrorMessage.Should().Contain("authenticated actor");
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewRejectsMixedOperationsOnTheSameVersionedAssignmentCard()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id, userIds = new[] { actor.UserId }, expectedUpdatedAt = card.UpdatedAt });
        var result = await ProposalOperationContractValidator.ValidateAsync(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), card.BoardId,
            [new(Guid.NewGuid(), Guid.NewGuid(), 0, ProposalAssignmentContract.Action, "card", card.Id.ToString(), parameters, "assignment", null),
             new(Guid.NewGuid(), Guid.NewGuid(), 1, "update", "card", card.Id.ToString(), JsonSerializer.Serialize(new { cardId = card.Id, title = "Also edit" }), "edit", null)]);
        result.IsSuccess.Should().BeFalse(); result.ErrorMessage.Should().Contain("only operation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokeDetachesArchivedAssignmentsUnlessBoardOwnerRemainsEligible(bool owner)
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        using var other = factory.CreateClient(); var participant = await ApiTestHarness.AuthenticateAsync(other, "assignment-revoke");
        var target = owner ? actor.UserId : participant.UserId;
        Guid accessId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var access = new BoardAccess(card.BoardId, target, UserRole.Viewer, actor.UserId);
            accessId = access.Id; db.BoardAccesses.Add(access); await db.SaveChangesAsync();
        }
        var assigned = await client.PutAsJsonAsync($"/api/boards/{card.BoardId}/cards/{card.Id}/assignments",
            new ReplaceCardAssignmentsDto([target], card.UpdatedAt));
        assigned.EnsureSuccessStatusCode(); var saved = (await assigned.Content.ReadFromJsonAsync<CardDto>())!;
        var preview = (await client.GetFromJsonAsync<CardDetachPreviewDto>($"/api/boards/{card.BoardId}/cards/{card.Id}/detach-preview"))!;
        (await client.PostAsJsonAsync($"/api/boards/{card.BoardId}/cards/{card.Id}/archive",
            new CardLifecycleDto(saved.UpdatedAt, preview.ExpectedChildrenFingerprint))).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/boards/{card.BoardId}/access/{accessId}")).EnsureSuccessStatusCode();
        var after = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{card.BoardId}/cards/{card.Id}"))!;
        after.IsArchived.Should().BeTrue(); after.Assignments.Should().HaveCount(owner ? 1 : 0);
        using var verification = factory.Services.CreateScope();
        var database = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await database.AuditLogs.AnyAsync(a => a.EntityId == card.Id && a.Changes != null && a.Changes.Contains("access-revoked"))).Should().Be(!owner);
    }

    [Fact]
    public async Task ImportPreviewRequiresEverySourceMappingAndCollapsesMeWithFreshCardIdentity()
    {
        var (client, actor, card) = await CreateAsync(); using var owned = client;
        var dto = new ImportBoardDto("Mapped import", null, [new("Next", 0, null)],
            [new("Imported", null, "Next", 0, null, [], SourceId: card.Id, SourceAssignees: [
                new("source-A", "Alex"), new("source-B", "Blair")])], []);
        var previewResponse = await client.PostAsJsonAsync("/api/import/boards/preview", dto);
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<BoardImportPreviewDto>())!;
        preview.SourceAssignees.Should().HaveCount(2).And.OnlyContain(a => a.AffectedCardCount == 1);
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Boards.CountAsync(b => b.Name == dto.Name)).Should().Be(0);
        (await client.PostAsJsonAsync("/api/import/boards", dto)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/import/boards/json", dto)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/import/boards", dto with { AssigneeMappings = new Dictionary<string, Guid?> {
            ["source-A"] = actor.UserId, ["source-B"] = Guid.NewGuid() } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var mapped = dto with { AssigneeMappings = new Dictionary<string, Guid?> { ["source-A"] = actor.UserId, ["source-B"] = actor.UserId } };
        var imported = await client.PostAsJsonAsync("/api/import/boards", mapped);
        imported.EnsureSuccessStatusCode(); var result = (await imported.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var cards = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{result.BoardId}/cards"))!;
        cards.Should().ContainSingle(); cards[0].Id.Should().NotBe(card.Id);
        cards[0].Assignments.Should().ContainSingle(a => a.UserId == actor.UserId);
        var export = await client.GetStringAsync($"/api/export/boards/{result.BoardId}/json");
        using var exported = JsonDocument.Parse(export);
        exported.RootElement.GetProperty("version").GetInt32().Should().Be(4);
        (await client.PostAsJsonAsync("/api/import/boards/preview", exported.RootElement)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/import/boards/json", exported.RootElement)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = new { source = exported.RootElement, assigneeMappings = new Dictionary<string, Guid?> { [actor.UserId.ToString()] = null } };
        var unassigned = await client.PostAsJsonAsync("/api/import/boards/json", raw); unassigned.EnsureSuccessStatusCode();
        var empty = (await unassigned.Content.ReadFromJsonAsync<ImportResultDto>())!;
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{empty.BoardId}/cards"))![0].Assignments.Should().BeEmpty();
        foreach (var version in new[] { 2, 3 })
        {
            var legacyEmpty = new { format = "taskdeck-board", version, payload = new {
                board = new { id = Guid.NewGuid(), name = "Legacy empty board" }, cards = (object?)null,
                columns = Array.Empty<object>(), labels = Array.Empty<object>() } };
            (await client.PostAsJsonAsync("/api/import/boards/preview", legacyEmpty)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task OwnerWithoutAccessAndViewerAreEligibleButAssignmentGrantsNoAuthority()
    {
        using var owner = factory.CreateClient(); var actor = await ApiTestHarness.AuthenticateAsync(owner, "assignment-owner");
        using var viewer = factory.CreateClient(); var participant = await ApiTestHarness.AuthenticateAsync(viewer, "assignment-viewer");
        using var outsider = factory.CreateClient(); var stranger = await ApiTestHarness.AuthenticateAsync(outsider, "assignment-outsider");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(owner, "Assignments");
        var board = (await owner.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(boardId, participant.UserId, UserRole.Viewer, actor.UserId));
            await db.SaveChangesAsync();
            (await db.BoardAccesses.AnyAsync(a => a.BoardId == boardId && a.UserId == actor.UserId)).Should().BeFalse();
        }
        var created = await owner.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Assign me", null, null, null));
        created.EnsureSuccessStatusCode(); var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;
        var url = $"/api/boards/{boardId}/cards/{card.Id}/assignments";
        var participants = (await owner.GetFromJsonAsync<BoardParticipantDto[]>($"/api/boards/{boardId}/participants"))!;
        participants.Select(p => p.UserId).Should().BeEquivalentTo([actor.UserId, participant.UserId]);
        (await outsider.GetAsync($"/api/boards/{boardId}/participants")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([participant.UserId], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([actor.UserId, stranger.UserId], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var savedResponse = await owner.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([actor.UserId, participant.UserId, actor.UserId], card.UpdatedAt));
        savedResponse.EnsureSuccessStatusCode(); var saved = (await savedResponse.Content.ReadFromJsonAsync<CardDto>())!;
        saved.Assignments.Should().HaveCount(2);
        saved.Assignments!.Should().OnlyContain(a => a.AssignedByUserId == actor.UserId);
        var noop = await owner.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([participant.UserId, actor.UserId], saved.UpdatedAt));
        noop.EnsureSuccessStatusCode(); var unchanged = (await noop.Content.ReadFromJsonAsync<CardDto>())!;
        unchanged.UpdatedAt.Should().Be(saved.UpdatedAt);
        unchanged.Assignments.Should().BeEquivalentTo(saved.Assignments);
        (await owner.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([], card.UpdatedAt))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PutAsJsonAsync(url, new { expectedUpdatedAt = saved.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var cleared = await owner.PutAsJsonAsync(url, new ReplaceCardAssignmentsDto([], saved.UpdatedAt));
        cleared.EnsureSuccessStatusCode(); (await cleared.Content.ReadFromJsonAsync<CardDto>())!.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposalAppliedByACollaboratorAttributesEveryAuditRowToTheApplier()
    {
        // #2978: author A requests an assignment proposal, editor B approves and applies it. The
        // authoritative assignment-replace row always named B; the generic ExecutionAuditRecorder
        // row named A, so the same board change produced two rows with contradictory actors. Both
        // rows must now name B, with A preserved as provenance text on the execution-history row.
        using var author = factory.CreateClient();
        using var applier = factory.CreateClient();
        var requester = await ApiTestHarness.AuthenticateAsync(author, "assignment-actor-requester");
        var editor = await ApiTestHarness.AuthenticateAsync(applier, "assignment-actor-applier");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(author, "Assignment actor attribution");
        var board = (await author.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        (await author.PostAsJsonAsync($"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, editor.UserId, UserRole.Editor))).EnsureSuccessStatusCode();

        var created = await author.PostAsJsonAsync($"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, board.Columns[0].Id, "Attribution card", null, null, null));
        created.EnsureSuccessStatusCode();
        var card = (await created.Content.ReadFromJsonAsync<CardDto>())!;

        var proposalResponse = await author.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, requester.UserId, "Assign the collaborator", RiskLevel.Medium,
            Guid.NewGuid().ToString(), boardId,
            Operations: [new CreateProposalOperationDto(0, ProposalAssignmentContract.Action, "card",
                JsonSerializer.Serialize(new { cardId = card.Id, userIds = new[] { editor.UserId }, expectedUpdatedAt = card.UpdatedAt }),
                Guid.NewGuid().ToString(), card.Id.ToString())]));
        proposalResponse.EnsureSuccessStatusCode();
        var proposal = (await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>())!;
        proposal.RequestedByUserId.Should().Be(requester.UserId);

        (await applier.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        using var execute = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        execute.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await applier.SendAsync(execute)).EnsureSuccessStatusCode();
        (await author.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{card.Id}"))!
            .Assignments!.Select(a => a.UserId).Should().Equal(editor.UserId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var rows = await db.AuditLogs.Where(log => log.EntityId == card.Id).ToListAsync();

        // Unchanged: the authoritative assignment audit already identified the applier.
        rows.Single(log => log.Changes != null && log.Changes.Contains("assignment-replace"))
            .UserId.Should().Be(editor.UserId);

        var history = rows.Single(log => log.Changes != null && log.Changes.StartsWith("Automation proposal"));
        history.UserId.Should().Be(editor.UserId, "execution history answers who changed the board");
        history.Changes.Should().Contain($"requested by user {requester.UserId}");
        rows.Should().NotContain(log => log.UserId == requester.UserId && log.Changes != null
            && log.Changes.StartsWith("Automation proposal"));
    }
}
