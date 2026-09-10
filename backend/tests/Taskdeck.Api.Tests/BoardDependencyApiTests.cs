using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;
namespace Taskdeck.Api.Tests;

public sealed class BoardDependencyApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestoringCard_InvalidatesPreviouslyReadDependencyProjection(bool hasStoredGraph)
    {
        using var client = factory.CreateClient();
        var (_, board, a, b) = await Setup(client);
        var edge = new CardDependency(a.Id, b.Id);
        if (hasStoredGraph)
            (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, [edge]))).EnsureSuccessStatusCode();
        var archivedResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{a.Id}/archive", new CardLifecycleDto(a.UpdatedAt));
        archivedResponse.EnsureSuccessStatusCode();
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var oldProjection = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!;
        oldProjection.Edges.Should().BeEmpty();
        (await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{a.Id}/restore", new CardLifecycleDto(archived.UpdatedAt))).EnsureSuccessStatusCode();
        var staleSave = await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(oldProjection.Revision, oldProjection.Edges));
        staleSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var fresh = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!;
        fresh.Revision.Should().BeGreaterThan(oldProjection.Revision);
        fresh.Edges.Should().BeEquivalentTo(hasStoredGraph ? new[] { edge } : []);
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(fresh.Revision, []))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!.Edges.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingActiveDependencies_PreservesArchivedEdges_AndAllowsActiveRemoval()
    {
        using var client = factory.CreateClient();
        var (_, board, a, b) = await Setup(client);
        async Task<CardDto> Create(string title)
        {
            var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
                new CreateCardDto(board.Id, a.ColumnId, title, null, null, null));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var c = await Create("Active prerequisite");
        var d = await Create("Active dependency");
        var retained = new CardDependency(a.Id, b.Id);
        var active = new CardDependency(c.Id, d.Id);
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, [retained]))).EnsureSuccessStatusCode();
        var archivedResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{a.Id}/archive", new CardLifecycleDto(a.UpdatedAt));
        archivedResponse.EnsureSuccessStatusCode();
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var visible = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!;
        visible.Edges.Should().BeEmpty();
        var saved = await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(visible.Revision, [.. visible.Edges, active]));
        saved.EnsureSuccessStatusCode();
        (await saved.Content.ReadFromJsonAsync<BoardDependencyDto>())!.Edges.Should().Equal(active);
        (await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{a.Id}/restore", new CardLifecycleDto(archived.UpdatedAt))).EnsureSuccessStatusCode();
        var restored = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!;
        restored.Edges.Should().BeEquivalentTo(new[] { retained, active });
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(restored.Revision, [retained]))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!.Edges.Should().Equal(retained);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterleavedGraphWrite_RollsBackLifecycleRevisionCardAndAudit(bool hasStoredGraph)
    {
        using var client = factory.CreateClient();
        var (actor, board, a, b) = await Setup(client);
        if (hasStoredGraph)
            (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, []))).EnsureSuccessStatusCode();
        using var loser = factory.Services.CreateScope();
        var unit = loser.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var card = (await unit.Cards.GetByIdAsync(a.Id))!;
        await unit.Cards.StageDependencyProjectionInvalidationAsync(board.Id);
        card.Archive();
        await unit.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Archived, actor, "losing lifecycle"));
        using (var winner = factory.Services.CreateScope())
        {
            var db = winner.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var graph = await db.Set<BoardDependencies>().FindAsync(board.Id);
            if (graph is null) { graph = new BoardDependencies(board.Id); db.Add(graph); }
            graph.Replace([new(a.Id, b.Id)]);
            await db.SaveChangesAsync();
        }
        var save = () => unit.SaveChangesAsync();
        (await save.Should().ThrowAsync<Taskdeck.Domain.Exceptions.DomainException>()).Which.ErrorCode
            .Should().Be(Taskdeck.Domain.Exceptions.ErrorCodes.Conflict);
        using var verify = factory.Services.CreateScope();
        var final = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await final.Cards.FindAsync(a.Id))!.IsArchived.Should().BeFalse();
        var savedGraph = (await final.Set<BoardDependencies>().FindAsync(board.Id))!;
        savedGraph.Revision.Should().Be(hasStoredGraph ? 2 : 1);
        savedGraph.ReadEdges().Should().Equal(new CardDependency(a.Id, b.Id));
        (await final.AuditLogs.AnyAsync(log => log.EntityId == a.Id && log.Action == AuditAction.Archived)).Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitLinksPersistWithoutChangingCards_AndExportRemapsThem()
    {
        using var client = factory.CreateClient();
        var (actor, board, a, b) = await Setup(client);
        var before = await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards");
        (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!.Edges.Should().BeEmpty();
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, [new(a.Id, b.Id)]))).EnsureSuccessStatusCode();
        var graph = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!;
        graph.Revision.Should().Be(1);
        graph.Edges.Should().Equal(new CardDependency(a.Id, b.Id));
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards")).Should().BeEquivalentTo(before);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Set<AuditLog>().SingleAsync(row => row.EntityId == board.Id && row.Changes != null && row.Changes.StartsWith("Updated explicit"))).UserId.Should().Be(actor);
        foreach (var suffix in new[] { "", "/json" })
        {
            var json = await client.GetStringAsync($"/api/export/boards/{board.Id}{suffix}");
            using var document = JsonDocument.Parse(json);
            document.RootElement.GetProperty("version").GetInt32().Should().Be(2);
            // Both legacy readers require a top-level name or board; neither can silently import this file.
            document.RootElement.TryGetProperty("name", out _).Should().BeFalse();
            document.RootElement.TryGetProperty("board", out _).Should().BeFalse();
            var importedResponse = await client.PostAsJsonAsync("/api/import/boards/json", document.RootElement);
            importedResponse.EnsureSuccessStatusCode();
            var imported = (await importedResponse.Content.ReadFromJsonAsync<ImportResultDto>())!;
            var cards = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{imported.BoardId}/cards"))!;
            var copy = (await client.GetFromJsonAsync<BoardDependencyDto>(Url(imported.BoardId!.Value)))!;
            copy.Edges.Should().Equal(new CardDependency(cards.Single(c => c.Title == a.Title).Id, cards.Single(c => c.Title == b.Title).Id));
            copy.Edges[0].CardId.Should().NotBe(a.Id);
        }
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(1, []))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards")).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task RejectsAnonymousForeignViewerAndCrossBoardWrites()
    {
        using var owner = factory.CreateClient(); using var other = factory.CreateClient(); using var anonymous = factory.CreateClient();
        var (_, board, a, b) = await Setup(owner);
        var request = new SaveBoardDependenciesDto(0, [new(a.Id, b.Id)]);
        (await anonymous.GetAsync(Url(board.Id))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(Url(board.Id), request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var viewer = await ApiTestHarness.AuthenticateAsync(other, "dependency-viewer");
        (await other.GetAsync(Url(board.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PutAsJsonAsync(Url(board.Id), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        (await other.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!.CanWrite.Should().BeFalse();
        (await other.PutAsJsonAsync(Url(board.Id), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, foreign, foreignCard, _) = await Setup(other);
        (await owner.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, [new(a.Id, foreignCard.Id)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync(Url(foreign.Id), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RejectsCycleAndStaleSave_DeletedCardsDisappearWithoutRecreation()
    {
        using var client = factory.CreateClient();
        var (_, board, a, b) = await Setup(client);
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, [new(a.Id, b.Id)]))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(1, [new(a.Id, b.Id), new(b.Id, a.Id)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(0, []))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync($"/api/boards/{board.Id}/cards/{b.Id}")).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<BoardDependencyDto>(Url(board.Id)))!.Edges.Should().BeEmpty();
        var exported = (await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json"))!;
        exported.Dependencies.Should().BeEmpty();
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(1, [new(a.Id, b.Id)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(Url(board.Id), new SaveBoardDependenciesDto(1, []))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("delete")]
    [InlineData("graph")]
    public async Task InterleavedMutationRollsBackGraphAndAudit(string change)
    {
        using var client = factory.CreateClient();
        var (actor, board, a, b) = await Setup(client);
        using var scope = factory.Services.CreateScope(); var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TaskdeckDbContext>();
        var real = new BoardDependencyRepository(db);
        var repository = new Mock<IBoardDependencyRepository>();
        repository.Setup(x => x.GetAsync(board.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.GetAsync(id, ct));
        repository.Setup(x => x.SaveAsync(It.IsAny<BoardDependencies>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(async (BoardDependencies graph, long revision, CancellationToken ct) =>
            {
                using var other = factory.Services.CreateScope();
                var otherDb = other.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                if (change == "archive") { (await otherDb.Boards.FindAsync(board.Id))!.Archive(); await otherDb.SaveChangesAsync(ct); }
                else if (change == "delete") (await client.DeleteAsync($"/api/boards/{board.Id}/cards/{b.Id}")).EnsureSuccessStatusCode();
                else { var winner = new BoardDependencies(board.Id); winner.Replace([]); await new BoardDependencyRepository(otherDb).SaveAsync(winner, 0, ct); }
                return await real.SaveAsync(graph, revision, ct);
            });
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var service = new BoardDependencyService(repository.Object, services.GetRequiredService<IUnitOfWork>(), services.GetRequiredService<IAuthorizationService>(), notifier.Object);
        var result = await service.SaveAsync(actor, board.Id, new SaveBoardDependenciesDto(0, [new(a.Id, b.Id)]), default);
        result.IsSuccess.Should().BeFalse();
        notifier.Verify(x => x.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verify = factory.Services.CreateScope(); var final = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var saved = await final.Set<BoardDependencies>().SingleOrDefaultAsync(g => g.BoardId == board.Id);
        (saved?.ReadEdges() ?? []).Should().BeEmpty();
        (await final.Set<AuditLog>().AnyAsync(row => row.EntityId == board.Id && row.Changes != null && row.Changes.StartsWith("Updated explicit"))).Should().BeFalse();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("cycle")]
    [InlineData("version")]
    public async Task InvalidImportIsAtomic(string failure)
    {
        using var client = factory.CreateClient(); var (_, board, a, b) = await Setup(client);
        var exported = (await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json"))!;
        var payload = exported with { Dependencies = failure == "cycle" ? [new(a.Id, b.Id), new(b.Id, a.Id)] : [new(a.Id, Guid.NewGuid())] };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var before = await db.Boards.CountAsync();
        var response = await client.PostAsJsonAsync("/api/import/boards/json", new BoardExportEnvelope("taskdeck-board", failure == "version" ? 99 : 2, payload));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await db.Boards.CountAsync()).Should().Be(before);
    }

    private async Task<(Guid Actor, BoardDto Board, CardDto A, CardDto B)> Setup(HttpClient client)
    {
        var actor = await ApiTestHarness.AuthenticateAsync(client, "dependency-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Explicit dependencies");
        var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "Next", null, null));
        response.EnsureSuccessStatusCode(); var column = (await response.Content.ReadFromJsonAsync<ColumnDto>())!;
        async Task<CardDto> Create(string title) { var r = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column.Id, title, null, null, null)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<CardDto>())!; }
        return (actor.UserId, board, await Create("Ship result"), await Create("Prepare source"));
    }
    private static string Url(Guid board) => $"/api/boards/{board}/dependencies";
}
