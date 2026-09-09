using System.Net;
using System.Net.Http.Json;
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

public sealed class ThinkingStepApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task PromotionIsIdempotentAndRemovalKeepsChild_WithActorAuditAndPortableLinks()
    {
        using var client = factory.CreateClient();
        var (actor, board, parent, layer, item) = await Setup(client);
        var request = new PromoteThinkingStepDto(1, parent.ColumnId, "Do the first step");
        var first = await client.PostAsJsonAsync(Url(board.Id, parent.Id, layer.Id, item.Id), request);
        first.EnsureSuccessStatusCode();
        var deck = (await first.Content.ReadFromJsonAsync<ThinkingDeckDto>())!;
        var childId = deck.Layers[0].Items[0].LinkedCardId!.Value;
        deck.Revision.Should().Be(2);
        var retry = await client.PostAsJsonAsync(Url(board.Id, parent.Id, layer.Id, item.Id), request);
        retry.EnsureSuccessStatusCode();
        (await retry.Content.ReadFromJsonAsync<ThinkingDeckDto>())!.Layers[0].Items[0].LinkedCardId.Should().Be(childId);
        var cards = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!;
        cards.Should().HaveCount(2);
        cards.Single(card => card.Id == childId).Description.Should().Be(item.Text);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var audit = await db.Set<AuditLog>().SingleAsync(row => row.EntityId == childId);
            audit.UserId.Should().Be(actor);
            audit.Action.Should().Be(AuditAction.Created);
            audit.Changes.Should().Contain(parent.Id.ToString());
        }
        var exported = await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json");
        var importedResponse = await client.PostAsJsonAsync("/api/import/boards/json", exported);
        importedResponse.EnsureSuccessStatusCode();
        var imported = (await importedResponse.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var copies = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{imported.BoardId}/cards"))!;
        var copiedParent = copies.Single(card => card.Title == parent.Title);
        var copiedChild = copies.Single(card => card.Title == "Do the first step");
        var copiedDeck = (await client.GetFromJsonAsync<ThinkingDeckDto>(DeckUrl(imported.BoardId!.Value, copiedParent.Id)))!;
        copiedDeck.Layers[0].Items[0].LinkedCardId.Should().Be(copiedChild.Id).And.NotBe(childId);
        (await client.PutAsJsonAsync(DeckUrl(board.Id, parent.Id), new SaveThinkingDeckDto(2, []))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().HaveCount(2);
    }

    [Fact]
    public async Task RejectsUnauthenticatedForeignViewerWrongBoardAndSpoofedLinks()
    {
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var (_, board, parent, layer, item) = await Setup(owner);
        var request = new PromoteThinkingStepDto(1, parent.ColumnId, "A child");
        var url = Url(board.Id, parent.Id, layer.Id, item.Id);
        (await anonymous.PostAsJsonAsync(url, request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var viewer = await ApiTestHarness.AuthenticateAsync(other, "step-viewer");
        (await other.PostAsJsonAsync(url, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        (await other.PostAsJsonAsync(url, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var second = await ApiTestHarness.CreateBoardAsync(owner, "foreign step");
        (await owner.PostAsJsonAsync(Url(second.Id, parent.Id, layer.Id, item.Id), request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var forged = layer with { Items = [item with { LinkedCardId = Guid.NewGuid() }] };
        (await owner.PutAsJsonAsync(DeckUrl(board.Id, parent.Id), new SaveThinkingDeckDto(1, [forged]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConcurrentPromotionCreatesOnlyOneCard_AndDeletedLinksDoNotRecreateIt()
    {
        using var client = factory.CreateClient();
        var (_, board, parent, layer, item) = await Setup(client);
        var url = Url(board.Id, parent.Id, layer.Id, item.Id);
        var request = new PromoteThinkingStepDto(1, parent.ColumnId, "One child");
        var responses = await Task.WhenAll(client.PostAsJsonAsync(url, request), client.PostAsJsonAsync(url, request));
        responses.Should().Contain(response => response.IsSuccessStatusCode);
        responses.Should().OnlyContain(response => response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict);
        var deck = (await client.GetFromJsonAsync<ThinkingDeckDto>(DeckUrl(board.Id, parent.Id)))!;
        var childId = deck.Layers[0].Items[0].LinkedCardId!.Value;
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().HaveCount(2);
        (await client.DeleteAsync($"/api/boards/{board.Id}/cards/{childId}")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(url, request)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().HaveCount(1);
        var exported = (await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json"))!;
        exported.ThinkingDecks![0].Material.Layers[0].Items[0].LinkedCardId.Should().BeNull();
        // The source still has its tombstone; export normalization does not mutate it.
        (await client.GetFromJsonAsync<ThinkingDeckDto>(DeckUrl(board.Id, parent.Id)))!.Layers[0].Items[0].LinkedCardId.Should().Be(childId);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("wip")]
    [InlineData("archive")]
    [InlineData("column")]
    [InlineData("title")]
    public async Task RefusalLeavesDeckAndBoardUnchanged(string reason)
    {
        using var client = factory.CreateClient();
        var (_, board, parent, layer, item) = await Setup(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            if (reason == "archive") (await db.Boards.FindAsync(board.Id))!.Archive();
            if (reason == "wip") (await db.Columns.FindAsync(parent.ColumnId))!.Update(wipLimit: 1);
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync(Url(board.Id, parent.Id, layer.Id, item.Id),
            new PromoteThinkingStepDto(reason == "stale" ? 0 : 1, reason == "column" ? Guid.NewGuid() : parent.ColumnId,
                reason == "title" ? new string('x', 201) : "A child"));
        response.IsSuccessStatusCode.Should().BeFalse();
        var saved = (await client.GetFromJsonAsync<ThinkingDeckDto>(DeckUrl(board.Id, parent.Id)))!;
        saved.Revision.Should().Be(1);
        saved.Layers[0].Items[0].LinkedCardId.Should().BeNull();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportRejectsLinkedCardsOutsidePayloadWithoutCreatingBoard()
    {
        using var client = factory.CreateClient();
        var (_, board, parent, layer, item) = await Setup(client);
        var exported = (await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json"))!;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var before = await db.Boards.CountAsync();
        var invalid = exported with { ThinkingDecks = [new(parent.Id, new(1,
            [layer with { Items = [item with { LinkedCardId = Guid.NewGuid() }] }]))] };
        (await client.PostAsJsonAsync("/api/import/boards/json", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await db.Boards.CountAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentArchiveOrDeckSaveRollsBackChildAndAuditBeforeNotification(bool archive)
    {
        using var client = factory.CreateClient();
        var (actor, board, parent, layer, item) = await Setup(client);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TaskdeckDbContext>();
        var realDecks = new ThinkingDeckRepository(db);
        var interleaved = new Mock<IThinkingDeckRepository>();
        interleaved.Setup(x => x.GetAsync(parent.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => realDecks.GetAsync(id, ct));
        interleaved.Setup(x => x.SaveAsync(It.IsAny<ThinkingDeck>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(async (ThinkingDeck deck, long revision, CancellationToken ct) =>
            {
                using var otherScope = factory.Services.CreateScope();
                var otherDb = otherScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                if (archive) { (await otherDb.Boards.FindAsync(board.Id))!.Archive(); await otherDb.SaveChangesAsync(ct); }
                else
                {
                    var repository = new ThinkingDeckRepository(otherDb);
                    var winner = (await repository.GetAsync(parent.Id, ct))!;
                    winner.Replace([layer with { Title = "Concurrent edit" }]);
                    (await repository.SaveAsync(winner, 1, ct)).Should().BeTrue();
                }
                return await realDecks.SaveAsync(deck, revision, ct);
            });
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var service = new ThinkingStepService(services.GetRequiredService<ThinkingDeckService>(), interleaved.Object,
            services.GetRequiredService<IUnitOfWork>(), services.GetRequiredService<CardService>(), notifier.Object);
        var result = await service.PromoteAsync(actor, board.Id, parent.Id, layer.Id, item.Id,
            new PromoteThinkingStepDto(1, parent.ColumnId, "Never commit me"), default);
        result.IsSuccess.Should().BeFalse();
        notifier.Verify(x => x.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verify = factory.Services.CreateScope();
        var finalDb = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await finalDb.Cards.CountAsync(card => card.BoardId == board.Id)).Should().Be(1);
        (await finalDb.Set<AuditLog>().AnyAsync(row => row.Changes != null && row.Changes.Contains($"sourceCard={parent.Id}"))).Should().BeFalse();
    }

    private async Task<(Guid Actor, BoardDto Board, CardDto Parent, ThinkingLayer Layer, ThinkingItem Item)> Setup(HttpClient client)
    {
        var actor = await ApiTestHarness.AuthenticateAsync(client, "step-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Linked steps");
        var columnResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "To do", null, null));
        var column = (await columnResponse.Content.ReadFromJsonAsync<ColumnDto>())!;
        var cardResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column.Id, "Parent task", null, null, null));
        var card = (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!;
        var item = new ThinkingItem(Guid.NewGuid(), "Read source and implement");
        var layer = new ThinkingLayer(Guid.NewGuid(), "steps", "Next steps", "", [item]);
        (await client.PutAsJsonAsync(DeckUrl(board.Id, card.Id), new SaveThinkingDeckDto(0, [layer]))).EnsureSuccessStatusCode();
        return (actor.UserId, board, card, layer, item);
    }
    private static string DeckUrl(Guid board, Guid card) => $"/api/boards/{board}/cards/{card}/thinking";
    private static string Url(Guid board, Guid card, Guid layer, Guid item) => $"{DeckUrl(board, card)}/steps/{layer}/{item}/card";
}
