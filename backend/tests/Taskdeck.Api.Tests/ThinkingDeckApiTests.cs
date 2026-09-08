using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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

public sealed class ThinkingDeckApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task ArchivedBoardThinkingIsReadOnlyForOwnerAndEditor()
    {
        using var owner = factory.CreateClient();
        using var editor = factory.CreateClient();
        var (board, card) = await Setup(owner);
        var editorUser = await ApiTestHarness.AuthenticateAsync(editor, "thinking-archive-editor");
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, editorUser.UserId, UserRole.Editor))).EnsureSuccessStatusCode();
        var url = Url(board.Id, card.Id);
        ThinkingLayer[] original = [new(Guid.NewGuid(), "note", "Retain this", "Original material", [])];
        (await owner.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, original))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.FindAsync(board.Id))!.Archive();
            await db.SaveChangesAsync();
        }
        foreach (var client in new[] { owner, editor })
        {
            var read = (await client.GetFromJsonAsync<ThinkingDeckDto>(url))!;
            read.CanWrite.Should().BeFalse();
            var rejected = await client.PutAsJsonAsync(url, new SaveThinkingDeckDto(1, []));
            rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await rejected.Content.ReadAsStringAsync()).Should().Contain("InvalidOperation");
            var retained = (await client.GetFromJsonAsync<ThinkingDeckDto>(url))!;
            retained.Revision.Should().Be(1);
            retained.Layers.Should().BeEquivalentTo(original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveBetweenCheckAndSaveRejectsThinkingAtomically(bool existingDeck)
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        ThinkingLayer[] original = [new(Guid.NewGuid(), "note", "Original", "Keep", [])];
        if (existingDeck)
            (await client.PutAsJsonAsync(Url(board.Id, card.Id), new SaveThinkingDeckDto(0, original))).EnsureSuccessStatusCode();
        using var writingScope = factory.Services.CreateScope();
        var db = writingScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var boardBefore = (await db.Boards.FindAsync(board.Id))!;
        var marker = boardBefore.CardMutationMarker;
        var realDecks = new ThinkingDeckRepository(db);
        var interleavedDecks = new Mock<IThinkingDeckRepository>();
        interleavedDecks.Setup(x => x.GetAsync(card.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken ct) => realDecks.GetAsync(id, ct));
        interleavedDecks.Setup(x => x.SaveAsync(It.IsAny<ThinkingDeck>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(async (ThinkingDeck deck, long revision, CancellationToken ct) =>
            {
                using var archivingScope = factory.Services.CreateScope();
                var archivingDb = archivingScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                (await archivingDb.Boards.FindAsync(board.Id))!.Archive();
                await archivingDb.SaveChangesAsync(ct);
                return await realDecks.SaveAsync(deck, revision, ct);
            });
        var service = new ThinkingDeckService(writingScope.ServiceProvider.GetRequiredService<ICardRepository>(), interleavedDecks.Object,
            writingScope.ServiceProvider.GetRequiredService<IAuthorizationService>(), writingScope.ServiceProvider.GetRequiredService<IBoardRepository>());
        var result = await service.SaveAsync(boardBefore.OwnerId!.Value, board.Id, card.Id,
            new SaveThinkingDeckDto(existingDeck ? 1 : 0, []), default);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Conflict");
        db.ChangeTracker.Entries().Should().BeEmpty("failed dependent writes must not leave pending changes for a later save");
        using var verification = factory.Services.CreateScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var archived = (await verifyDb.Boards.FindAsync(board.Id))!;
        archived.IsArchived.Should().BeTrue();
        archived.CardMutationMarker.Should().Be(marker);
        var persisted = await verifyDb.Set<ThinkingDeck>().FindAsync(card.Id);
        if (existingDeck)
        {
            persisted!.Revision.Should().Be(1);
            persisted.ReadLayers().Should().BeEquivalentTo(original);
        }
        else persisted.Should().BeNull();
    }

    [Fact]
    public async Task EndpointsRequireAuthentication()
    {
        using var client = factory.CreateClient();
        var url = Url(Guid.NewGuid(), Guid.NewGuid());
        await ApiTestHarness.AssertUnauthorizedAsync(await client.GetAsync(url));
        await ApiTestHarness.AssertUnauthorizedAsync(await client.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, [])));
    }

    [Fact]
    public async Task SavesOrderedLayersAndOptions_RejectsStaleRevision_AndDeletesWithCard()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        var url = Url(board.Id, card.Id);
        var initial = await client.GetFromJsonAsync<ThinkingDeckDto>(url);
        initial!.Revision.Should().Be(0);
        initial.CanWrite.Should().BeTrue();
        initial.Layers.Should().BeEmpty();
        var a = new ThinkingItem(Guid.NewGuid(), "Keep the simple path");
        var b = new ThinkingItem(Guid.NewGuid(), "Explore another path");
        ThinkingLayer[] layers = [new(Guid.NewGuid(), "options", "Choose a path", "Tradeoffs", [a, b], b.Id),
            new(Guid.NewGuid(), "question", "What remains unknown?", "An answer in progress", [])];
        var save = await client.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, layers));
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var saved = await client.GetFromJsonAsync<ThinkingDeckDto>(url);
        saved!.Layers.Should().BeEquivalentTo(layers, options => options.WithStrictOrdering());
        saved.Revision.Should().Be(1);
        (await client.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, []))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<ThinkingDeckDto>(url))!.Layers.Should().HaveCount(2);
        (await client.PutAsJsonAsync(url, new SaveThinkingDeckDto(1, []))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/boards/{board.Id}/cards")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/boards/{board.Id}/cards/{card.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Set<ThinkingDeck>().AnyAsync(deck => deck.CardId == card.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AccessIsBoardScoped_ViewerCannotWrite_AndForeignCardCannotBeSpoofed()
    {
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        var (board, card) = await Setup(owner);
        var otherUser = await ApiTestHarness.AuthenticateAsync(other, "thinking-other");
        var url = Url(board.Id, card.Id);
        await ApiTestHarness.AssertForbiddenAsync(await other.GetAsync(url));
        await ApiTestHarness.AssertForbiddenAsync(await other.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, [])));
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, otherUser.UserId, UserRole.Viewer))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.GetFromJsonAsync<ThinkingDeckDto>(url))!.CanWrite.Should().BeFalse();
        await ApiTestHarness.AssertForbiddenAsync(await other.PutAsJsonAsync(url, new SaveThinkingDeckDto(0, [])));
        var second = await ApiTestHarness.CreateBoardAsync(owner, "other-thinking");
        (await owner.GetAsync(Url(second.Id, card.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync(Url(second.Id, card.Id), new SaveThinkingDeckDto(0, []))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DatabaseCompareAndSwapRejectsTwoWritersAndInitialCreationRace()
    {
        using var client = factory.CreateClient();
        var (_, card) = await Setup(client);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = new ThinkingDeckRepository(firstScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var second = new ThinkingDeckRepository(secondScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var a = new ThinkingDeck(card.Id); a.Replace([]);
        var b = new ThinkingDeck(card.Id); b.Replace([]);
        (await first.SaveAsync(a, 0, default)).Should().BeTrue();
        (await second.SaveAsync(b, 0, default)).Should().BeFalse();
        using var thirdScope = factory.Services.CreateScope();
        using var fourthScope = factory.Services.CreateScope();
        var third = new ThinkingDeckRepository(thirdScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var fourth = new ThinkingDeckRepository(fourthScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var c = await third.GetAsync(card.Id, default);
        var d = await fourth.GetAsync(card.Id, default);
        c!.Replace([new(Guid.NewGuid(), "note", "Winner", "", [])]);
        d!.Replace([new(Guid.NewGuid(), "note", "Stale", "", [])]);
        (await third.SaveAsync(c, 1, default)).Should().BeTrue();
        (await fourth.SaveAsync(d, 1, default)).Should().BeFalse();
        (await fourth.GetAsync(card.Id, default))!.ReadLayers()[0].Title.Should().Be("Winner");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("options")]
    public async Task RejectsInvalidKindsAndMissingSelectedOption(string kind)
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        var layers = new[] { new ThinkingLayer(Guid.NewGuid(), kind, "Bad selection", "", [], Guid.NewGuid()) };
        (await client.PutAsJsonAsync(Url(board.Id, card.Id), new SaveThinkingDeckDto(0, layers))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BoardJsonRoundTripPreservesThinkingOnNewCards_WithoutWritingSource()
    {
        using var owner = factory.CreateClient();
        using var importer = factory.CreateClient();
        var (board, card) = await Setup(owner);
        await ApiTestHarness.AuthenticateAsync(importer, "thinking-importer");
        var option = new ThinkingItem(Guid.NewGuid(), "Chosen route");
        ThinkingLayer[] layers = [new(Guid.NewGuid(), "options", "Routes", "Keep context", [option, new(Guid.NewGuid(), "Alternative")], option.Id)];
        (await owner.PutAsJsonAsync(Url(board.Id, card.Id), new SaveThinkingDeckDto(0, layers))).StatusCode.Should().Be(HttpStatusCode.OK);
        await ApiTestHarness.AssertForbiddenAsync(await importer.GetAsync($"/api/export/boards/{board.Id}/json"));
        var exported = await owner.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}/json");
        exported!.ThinkingDecks.Should().HaveCount(1);
        exported.ThinkingDecks![0].CardId.Should().Be(card.Id);
        var importedResponse = await importer.PostAsJsonAsync("/api/import/boards/json", exported);
        importedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await importedResponse.Content.ReadAsStringAsync());
        var imported = (await importedResponse.Content.ReadFromJsonAsync<ImportResultDto>())!;
        imported.BoardId.Should().NotBe(board.Id);
        var importedCards = await importer.GetFromJsonAsync<List<CardDto>>($"/api/boards/{imported.BoardId}/cards");
        importedCards.Should().HaveCount(1);
        var importedCard = importedCards![0];
        importedCard.Id.Should().NotBe(card.Id);
        var deck = await importer.GetFromJsonAsync<ThinkingDeckDto>(Url(imported.BoardId!.Value, importedCard.Id));
        deck!.Revision.Should().Be(1);
        deck.Layers.Should().BeEquivalentTo(layers, options => options.WithStrictOrdering());
        (await importer.PutAsJsonAsync(Url(imported.BoardId.Value, importedCard.Id), new SaveThinkingDeckDto(1, []))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetFromJsonAsync<ThinkingDeckDto>(Url(board.Id, card.Id)))!.Layers.Should().BeEquivalentTo(layers);
    }

    [Fact]
    public async Task ImportRejectsForeignThinkingReferencesAndUnsupportedSchema_WithoutPartialBoard()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        var exported = (await client.GetFromJsonAsync<ExportBoardDto>($"/api/export/boards/{board.Id}"))!;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var before = await db.Boards.CountAsync();
        var foreignReference = exported with { ThinkingDecks = [new(Guid.NewGuid(), new(1, []))] };
        (await client.PostAsJsonAsync("/api/import/boards/json", foreignReference)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unsupportedSchema = exported with { ThinkingDecks = [new(card.Id, new(99, []))] };
        (await client.PostAsJsonAsync("/api/import/boards/json", unsupportedSchema)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidLayer = exported with { ThinkingDecks = [new(card.Id, new(1, [new(Guid.NewGuid(), "unknown", "", "", [])]))] };
        (await client.PostAsJsonAsync("/api/import/boards/json", invalidLayer)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await db.Boards.CountAsync()).Should().Be(before);
        (await client.GetFromJsonAsync<ThinkingDeckDto>(Url(board.Id, card.Id)))!.Revision.Should().Be(0);
    }

    [Fact]
    public async Task LegacyBoardExportWithoutThinkingStillImports()
    {
        using var client = factory.CreateClient();
        var (board, _) = await Setup(client);
        var exported = JsonNode.Parse(await client.GetStringAsync($"/api/export/boards/{board.Id}/json"))!.AsObject();
        exported.Remove("thinkingDecks");
        var response = await client.PostAsJsonAsync("/api/import/boards/json", exported);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var imported = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var cards = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{imported.BoardId}/cards"))!;
        (await client.GetFromJsonAsync<ThinkingDeckDto>(Url(imported.BoardId!.Value, cards[0].Id)))!.Layers.Should().BeEmpty();
    }

    private async Task<(BoardDto, CardDto)> Setup(HttpClient client)
    {
        await ApiTestHarness.AuthenticateAsync(client, "thinking-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "thinking");
        var columnResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "To do", null, null));
        var column = await columnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        var cardResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column!.Id, "A task", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        return (board, (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!);
    }
    private static string Url(Guid boardId, Guid cardId) => $"/api/boards/{boardId}/cards/{cardId}/thinking";
}
