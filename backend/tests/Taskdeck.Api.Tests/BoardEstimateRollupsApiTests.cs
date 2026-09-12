using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class BoardEstimateRollupsApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<(HttpClient Client, TestUserContext Owner, BoardDetailDto Board)> CreateAsync()
    {
        var client = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(client, "estimate-owner");
        var id = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Estimate totals");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{id}"))!;
        return (client, owner, board);
    }

    private static Card AddCard(TaskdeckDbContext db, BoardDetailDto board, int? minutes, params Guid[] assignees)
    {
        var card = new Card(board.Id, board.Columns[0].Id, "Estimate");
        card.SetEstimatedEffortMinutes(minutes);
        if (assignees.Length > 0) card.ReplaceAssignments(assignees, assignees[0]);
        db.Cards.Add(card);
        return card;
    }

    [Fact]
    public async Task OwnerWithoutAccessRowAndViewerReadMixedTotalsWhileUnauthenticatedAndForeignReadersAreDenied()
    {
        var (client, owner, board) = await CreateAsync(); using var owned = client;
        using var viewer = factory.CreateClient();
        var member = await ApiTestHarness.AuthenticateAsync(viewer, "estimate-viewer");
        using var outsider = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsider, "estimate-outsider");
        var otherBoardId = await ApiTestHarness.CreateBoardWithColumnAsync(outsider, "Foreign estimates");
        var otherBoard = (await outsider.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{otherBoardId}"))!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.RemoveRange(await db.BoardAccesses.Where(a => a.BoardId == board.Id && a.UserId == owner.UserId).ToListAsync());
            db.BoardAccesses.Add(new BoardAccess(board.Id, member.UserId, UserRole.Viewer, owner.UserId));
            AddCard(db, board, null, member.UserId);
            AddCard(db, board, 0);
            AddCard(db, board, 90, owner.UserId, member.UserId);
            AddCard(db, board, 999).Archive();
            AddCard(db, otherBoard, 800);
            await db.SaveChangesAsync();
        }
        var url = $"/api/boards/{board.Id}/estimate-rollups";
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await outsider.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/boards/{otherBoard.Id}/estimate-rollups")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/boards/{Guid.NewGuid()}/estimate-rollups")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var reader in new[] { client, viewer })
        {
            var totals = (await reader.GetFromJsonAsync<BoardEstimateRollupDto>(url))!;
            totals.BoardId.Should().Be(board.Id);
            totals.Board.Should().Be(new EstimateTotalsDto(3, 90, 1));
            totals.Unassigned.Should().Be(new EstimateTotalsDto(1, 0, 0));
            totals.Participants.Single(p => p.UserId == owner.UserId).Totals.Should().Be(new EstimateTotalsDto(1, 90, 0));
            totals.Participants.Single(p => p.UserId == member.UserId).Totals.Should().Be(new EstimateTotalsDto(2, 90, 1));
            totals.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task NewReadsReflectMovesArchiveRestoreAssignmentRevokeAndCardDeletion()
    {
        var (client, owner, board) = await CreateAsync(); using var owned = client;
        using var viewer = factory.CreateClient();
        var member = await ApiTestHarness.AuthenticateAsync(viewer, "estimate-revoke");
        Guid cardId, columnId, accessId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var column = new Column(board.Id, "Done", 1); columnId = column.Id; db.Columns.Add(column);
            var access = new BoardAccess(board.Id, member.UserId, UserRole.Viewer, owner.UserId);
            accessId = access.Id; db.BoardAccesses.Add(access);
            cardId = AddCard(db, board, 60, member.UserId).Id;
            await db.SaveChangesAsync();
        }
        var url = $"/api/boards/{board.Id}/estimate-rollups";
        (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!.Unassigned.CardCount.Should().Be(0);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Cards.FindAsync(cardId))!.MoveToColumn(columnId, 0);
            await db.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!.Columns.Single(c => c.ColumnId == columnId).Totals.KnownEstimateMinutes.Should().Be(60);
        (await client.DeleteAsync($"/api/boards/{board.Id}/access/{accessId}")).EnsureSuccessStatusCode();
        (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var revoked = (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!;
        revoked.Participants.Should().NotContain(p => p.UserId == member.UserId);
        revoked.Unassigned.KnownEstimateMinutes.Should().Be(60);
        foreach (var archived in new[] { true, false })
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
                var card = (await db.Cards.FindAsync(cardId))!;
                if (archived) card.Archive(); else card.Restore();
                await db.SaveChangesAsync();
            }
            (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!.Board.CardCount.Should().Be(archived ? 0 : 1);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Cards.Remove((await db.Cards.FindAsync(cardId))!);
            await db.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!.Board.Should().Be(new EstimateTotalsDto(0, 0, 0));
    }

    [Fact]
    public async Task DeletedParticipantDisappearsAndTheirRetainedCardBecomesUnassigned()
    {
        var (client, owner, board) = await CreateAsync(); using var owned = client;
        using var memberClient = factory.CreateClient();
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "estimate-erasure");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(board.Id, member.UserId, UserRole.Viewer, owner.UserId));
            AddCard(db, board, 75, member.UserId);
            await db.SaveChangesAsync();
        }
        var url = $"/api/boards/{board.Id}/estimate-rollups";
        (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!.Participants
            .Should().Contain(p => p.UserId == member.UserId && p.Totals.KnownEstimateMinutes == 75);
        (await memberClient.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT")))
            .EnsureSuccessStatusCode();
        var after = (await client.GetFromJsonAsync<BoardEstimateRollupDto>(url))!;
        after.Board.Should().Be(new EstimateTotalsDto(1, 75, 0));
        after.Unassigned.Should().Be(new EstimateTotalsDto(1, 75, 0));
        after.Participants.Should().NotContain(p => p.UserId == member.UserId);
    }

    [Fact]
    public async Task RepositoryUsesOneFreshQueryForAllCardsAndAssignmentsWithoutAggregateSchema()
    {
        var (client, owner, board) = await CreateAsync(); using var owned = client;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var target = AddCard(db, board, 10, owner.UserId);
        for (var i = 0; i < 25; i++) AddCard(db, board, i, owner.UserId);
        AddCard(db, board, 999).Archive();
        await db.SaveChangesAsync();
        var counter = new ReadCounter();
        await using var reader = new TaskdeckDbContext(new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).AddInterceptors(counter).Options);
        var repository = new CardRepository(reader);
        var first = await repository.GetForEstimateRollupsAsync(board.Id);
        counter.Count.Should().Be(1);
        counter.LastCommand.Should().NotContain("\"Users\"");
        first.Should().HaveCount(26).And.OnlyContain(card => card.Assignments.Count == 1);
        reader.ChangeTracker.Entries<Card>().Should().BeEmpty();
        target.SetEstimatedEffortMinutes(45); await db.SaveChangesAsync();
        var second = await repository.GetForEstimateRollupsAsync(board.Id);
        counter.Count.Should().Be(2);
        second.Single(card => card.Id == target.Id).EstimatedEffortMinutes.Should().Be(45);
        reader.Model.GetEntityTypes().Should().NotContain(entity => entity.ClrType.Name.Contains("EstimateRollup"));
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public string LastCommand { get; private set; } = string.Empty;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            LastCommand = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}
