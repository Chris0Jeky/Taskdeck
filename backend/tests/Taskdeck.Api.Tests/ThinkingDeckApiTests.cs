using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ThinkingDeckApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
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
