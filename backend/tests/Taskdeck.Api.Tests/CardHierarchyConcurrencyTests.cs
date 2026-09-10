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
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardHierarchyConcurrencyTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task EmptyColumnDeleteCannotCascadeAConcurrentHierarchyCreate()
    {
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "hierarchy-column-race");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Column race");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Parent", null, null, null));
        var parent = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        var columnResponse = await client.PostAsJsonAsync($"/api/boards/{boardId}/columns", new CreateColumnDto(boardId, "Empty", null, null));
        var target = (await columnResponse.Content.ReadFromJsonAsync<ColumnDto>())!;
        using var scope = factory.Services.CreateScope(); var real = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        columns.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.Columns.GetByIdAsync(id, ct));
        columns.Setup(r => r.GetByIdWithCardsAsync(target.Id, It.IsAny<CancellationToken>())).Returns(async (Guid id, CancellationToken ct) =>
        {
            var snapshot = await real.Columns.GetByIdWithCardsAsync(id, ct);
            (await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, target.Id, "Concurrent child", null, null, null, ParentCardId: parent.Id))).EnsureSuccessStatusCode();
            return snapshot;
        });
        columns.Setup(r => r.DeleteAsync(It.IsAny<Column>(), It.IsAny<CancellationToken>())).Returns((Column column, CancellationToken ct) => real.Columns.DeleteAsync(column, ct));
        var proxy = new Mock<IUnitOfWork>();
        proxy.SetupGet(u => u.Columns).Returns(columns.Object);
        proxy.SetupGet(u => u.Boards).Returns(real.Boards);
        proxy.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        var service = new ColumnService(proxy.Object);
        var action = async () => await service.DeleteColumnAsync(target.Id);
        (await action.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        var cards = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards"))!;
        cards.Single(card => card.Title == "Concurrent child").ColumnId.Should().Be(target.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ParentArchiveOrDeleteRejectsConcurrentChildAttachmentOrEdit(bool delete, bool editChild)
    {
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "hierarchy-empty-race");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Empty parent race");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Parent", null, null, null));
        var parent = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        CardDto? child = null;
        if (editChild)
        {
            var childResponse = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Concurrent child", null, null, null, ParentCardId: parent.Id));
            child = (await childResponse.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var preview = (await client.GetFromJsonAsync<CardDetachPreviewDto>($"/api/boards/{boardId}/cards/{parent.Id}/detach-preview"))!;
        var confirmation = new CardLifecycleDto(preview.ExpectedUpdatedAt, preview.ExpectedChildrenFingerprint);
        using var scope = factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        cards.Setup(r => r.GetByIdAsync(parent.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.Cards.GetByIdAsync(id, ct));
        cards.Setup(r => r.GetByIdWithLabelsAsync(parent.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.Cards.GetByIdWithLabelsAsync(id, ct));
        cards.Setup(r => r.DeleteAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>())).Returns((Card card, CancellationToken ct) => real.Cards.DeleteAsync(card, ct));
        cards.Setup(r => r.StageDependencyProjectionInvalidationAsync(boardId, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.Cards.StageDependencyProjectionInvalidationAsync(id, ct));
        cards.Setup(r => r.GetHierarchyByBoardIdAsync(boardId, It.IsAny<CancellationToken>())).Returns(async (Guid id, CancellationToken ct) =>
        {
            var snapshot = await real.Cards.GetHierarchyByBoardIdAsync(id, ct);
            if (editChild)
                (await client.PatchAsJsonAsync($"/api/boards/{boardId}/cards/{child!.Id}", new { description = "Concurrent edit", expectedUpdatedAt = child.UpdatedAt })).EnsureSuccessStatusCode();
            else
                (await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Concurrent child", null, null, null, ParentCardId: parent.Id))).EnsureSuccessStatusCode();
            return snapshot;
        });
        var proxy = new Mock<IUnitOfWork>();
        proxy.SetupGet(u => u.Cards).Returns(cards.Object);
        proxy.SetupGet(u => u.Boards).Returns(real.Boards);
        proxy.SetupGet(u => u.AuditLogs).Returns(real.AuditLogs);
        proxy.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        var service = new CardService(proxy.Object);
        if (delete)
            (await service.DeleteCardAsync(parent.Id, confirmation: confirmation)).ErrorCode.Should().Be(ErrorCodes.Conflict);
        else
            (await service.SetArchivedAsync(boardId, parent.Id, true, confirmation)).ErrorCode.Should().Be(ErrorCodes.Conflict);
        var retained = (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{parent.Id}"))!;
        retained.IsArchived.Should().BeFalse();
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards"))!.Single(card => card.Title == "Concurrent child").ParentCardId.Should().Be(parent.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentCardReparentLosesWhenAnotherGraphWriteCommits(bool depth)
    {
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "hierarchy-race");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Graph race");
        var columnResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "Next", null, null));
        var column = (await columnResponse.Content.ReadFromJsonAsync<ColumnDto>())!;
        async Task<CardDto> Create(string title, Guid? parent = null)
        {
            var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column.Id, title, null, null, null, ParentCardId: parent));
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var a = await Create("A"); var b = await Create("B");
        var c = await Create("C"); var d = await Create("D", c.Id); var e = await Create("E", d.Id);
        using var scope = factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var cards = new Mock<ICardRepository>();
        cards.Setup(r => r.GetByIdWithLabelsAsync(a.Id, It.IsAny<CancellationToken>())).Returns((Guid id, CancellationToken ct) => real.Cards.GetByIdWithLabelsAsync(id, ct));
        cards.Setup(r => r.GetHierarchyByBoardIdAsync(board.Id, It.IsAny<CancellationToken>())).Returns(async (Guid id, CancellationToken ct) =>
        {
            var snapshot = await real.Cards.GetHierarchyByBoardIdAsync(id, ct);
            var winner = await client.PatchAsJsonAsync($"/api/boards/{board.Id}/cards/{(depth ? c.Id : b.Id)}",
                new { parentCardId = a.Id, expectedUpdatedAt = depth ? c.UpdatedAt : b.UpdatedAt });
            winner.EnsureSuccessStatusCode();
            return snapshot;
        });
        var proxy = new Mock<IUnitOfWork>();
        proxy.SetupGet(u => u.Cards).Returns(cards.Object);
        proxy.SetupGet(u => u.Boards).Returns(real.Boards);
        proxy.SetupGet(u => u.AuditLogs).Returns(real.AuditLogs);
        proxy.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) => real.SaveChangesAsync(ct));
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var service = new CardService(proxy.Object, notifier.Object);
        var result = await service.UpdateCardAsync(a.Id, new UpdateCardDto(null, null, null, null, null, null,
            ExpectedUpdatedAt: a.UpdatedAt, ParentCardId: b.Id));
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        using var verify = factory.Services.CreateScope();
        var fresh = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await fresh.Cards.SingleAsync(card => card.Id == a.Id)).ParentCardId.Should().BeNull();
        (await fresh.Boards.SingleAsync(candidate => candidate.Id == board.Id)).UpdatedAt.Should().Be(board.UpdatedAt);
        (await fresh.AuditLogs.AnyAsync(row => row.EntityId == a.Id)).Should().BeTrue(); // Original creation survives.
        (await fresh.AuditLogs.AnyAsync(row => row.EntityId == a.Id && row.Changes != null && row.Changes.StartsWith("ParentCardId:"))).Should().BeFalse();
    }
}
