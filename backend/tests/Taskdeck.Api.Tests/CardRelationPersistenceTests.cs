using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;
namespace Taskdeck.Api.Tests;

public class CardRelationPersistenceTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task ValidateIsReadOnly_StageAcceptsPrecedingCreate_AndRollbackLeavesNoGraphAuditOrCardChanges()
    {
        var (actor, boardId, a, b) = await Seed();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var service = Service(scope);
        var before = (await db.Cards.FindAsync(a))!.UpdatedAt;
        var validate = await service.ValidateMutationAsync(actor,boardId,new(a,b,"depends-on"),0,false,default);
        validate.IsSuccess.Should().BeTrue();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        await unit.BeginTransactionAsync();
        var created = new Card(boardId,(await db.Cards.FindAsync(a))!.ColumnId,"Ordered create");
        await unit.Cards.AddAsync(created);
        var staged = await service.StageMutationAsync(actor,boardId,new(created.Id,b,"duplicates"),0,false,default);
        staged.IsSuccess.Should().BeTrue();
        // Service does not save; the graph is not in SQLite until the executor persists it.
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM BoardDependencies WHERE BoardId={boardId}").SingleAsync()).Should().Be(0);
        await unit.AuditLogs.AddAsync(new AuditLog("board",boardId,AuditAction.Updated,actor,"rolled-back typed mutation"));
        await unit.SaveChangesAsync();
        await unit.RollbackTransactionAsync();
        using var verify = factory.Services.CreateScope();
        var final = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await final.Set<BoardDependencies>().AnyAsync(g => g.BoardId == boardId)).Should().BeFalse();
        (await final.Cards.AnyAsync(c => c.Id == created.Id)).Should().BeFalse();
        (await final.AuditLogs.AnyAsync(l => l.EntityId == boardId)).Should().BeFalse();
        (await final.Cards.FindAsync(a))!.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public async Task CanonicalDatabaseKeyRejectsDuplicate_AndStaleWriterCannotCommitAudit()
    {
        var (actor,boardId,a,b) = await Seed();
        using var loser = factory.Services.CreateScope();
        var loserDb = loser.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var loserUnit = loser.ServiceProvider.GetRequiredService<IUnitOfWork>();
        (await Service(loser).StageMutationAsync(actor,boardId,new(a,b,"relates-to"),0,false,default)).IsSuccess.Should().BeTrue();
        await loserUnit.AuditLogs.AddAsync(new AuditLog("board",boardId,AuditAction.Updated,actor,"loser relation"));
        using (var winner = factory.Services.CreateScope())
        {
            (await Service(winner).StageMutationAsync(actor,boardId,new(b,a,"relates-to"),0,false,default)).IsSuccess.Should().BeTrue();
            await winner.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        var losingSave = () => loserUnit.SaveChangesAsync();
        (await losingSave.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var edge = (await db.CardRelations.SingleAsync(e => e.BoardId == boardId)).ToEdge();
        var duplicate = () => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CardRelations(BoardId,SourceCardId,TargetCardId,RelationType) VALUES({boardId},{edge.SourceCardId},{edge.TargetCardId},{edge.RelationType})");
        (await duplicate.Should().ThrowAsync<SqliteException>()).Which.SqliteExtendedErrorCode.Should().Be(1555);
        (await db.AuditLogs.AnyAsync(l => l.EntityId == boardId)).Should().BeFalse();
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("delete")]
    public async Task EndpointRaceRejectsStagedMutation_AndLosingAuditIsNotCommitted(string action)
    {
        var (actor,boardId,a,b) = await Seed();
        using var loser = factory.Services.CreateScope();
        var unit = loser.ServiceProvider.GetRequiredService<IUnitOfWork>();
        (await Service(loser).StageMutationAsync(actor,boardId,new(a,b,"blocks"),0,false,default)).IsSuccess.Should().BeTrue();
        await unit.AuditLogs.AddAsync(new AuditLog("board",boardId,AuditAction.Updated,actor,"losing endpoint race"));
        using (var winner = factory.Services.CreateScope())
        {
            var db = winner.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var card = (await db.Cards.FindAsync(b))!;
            // Bypass service invalidation to prove the endpoint concurrency/FK guard itself.
            if (action == "archive") card.Archive(); else db.Remove(card);
            await db.SaveChangesAsync();
        }
        var losingSave = () => unit.SaveChangesAsync();
        (await losingSave.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        using var verify = factory.Services.CreateScope();
        var final = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await final.CardRelations.AnyAsync(e => e.BoardId == boardId)).Should().BeFalse();
        (await final.AuditLogs.AnyAsync(l => l.EntityId == boardId)).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyReplacementRetainsOtherKindsAndArchivedEdges_AndTypedReadIncludesThem()
    {
        var (actor,boardId,a,b) = await Seed();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var card = (await db.Cards.FindAsync(a))!;
        var c = new Card(boardId,card.ColumnId,"Active C"); var d = new Card(boardId,card.ColumnId,"Active D");
        db.AddRange(c,d);
        var graph = new BoardDependencies(boardId);
        graph.ReplaceRelations([new(a,b,"blocks"),new(c.Id,d.Id,"duplicates"),new(c.Id,d.Id,"blocks")]);
        db.Add(graph); card.Archive(); await db.SaveChangesAsync();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var legacy = new BoardDependencyService(new BoardDependencyRepository(db),unit,Authorization(),notifier.Object);
        (await legacy.SaveAsync(actor,boardId,new(1,[]),default)).IsSuccess.Should().BeTrue();
        var read = await Service(scope).GetAsync(actor,boardId,default);
        read.Value.Relations.Should().BeEquivalentTo(new[] { new CardRelationEdge(a,b,"blocks"),new CardRelationEdge(c.Id,d.Id,"duplicates") });
        (await legacy.GetAsync(actor,boardId,default)).Value.Edges.Should().BeEmpty();
        (await Service(scope).ValidateMutationAsync(actor,boardId,new(a,b,"blocks"),2,true,default)).IsSuccess.Should().BeFalse();
        var board = (await db.Boards.FindAsync(boardId))!; board.Archive(); await db.SaveChangesAsync();
        (await Service(scope).GetAsync(actor,boardId,default)).Value.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task HardDeleteRecordsRemovedRelationsWithActor_WithoutChangingRevision()
    {
        var (actor,boardId,a,b) = await Seed();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        (await Service(scope).StageMutationAsync(actor,boardId,new(a,b,"spawned-from"),0,false,default)).IsSuccess.Should().BeTrue();
        await unit.SaveChangesAsync();
        var notifier = new Mock<IBoardRealtimeNotifier>();
        var result = await new CardService(unit,notifier.Object).DeleteCardAsync(a,actorUserId: actor);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await db.CardRelations.AnyAsync(e => e.BoardId == boardId)).Should().BeFalse();
        (await db.Set<BoardDependencies>().SingleAsync(g => g.BoardId == boardId)).Revision.Should().Be(1);
        var receipt = await db.AuditLogs.SingleAsync(l => l.EntityId == boardId);
        receipt.UserId.Should().Be(actor); receipt.Changes.Should().Contain(a.ToString()).And.Contain(b.ToString()).And.Contain("spawned-from");
        notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(),It.IsAny<CancellationToken>()),Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RelationInsertionAfterDeleteSnapshotRejectsDelete_AndCannotLeaveFalseRemovalReceipt(bool hadGraph)
    {
        var (actor,boardId,a,b) = await Seed();
        if (hadGraph)
        {
            using var seed = factory.Services.CreateScope();
            (await Service(seed).StageMutationAsync(actor,boardId,new(a,b,"duplicates"),0,false,default)).IsSuccess.Should().BeTrue();
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        using var loser = factory.Services.CreateScope();
        var unit = loser.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var card = (await unit.Cards.GetByIdAsync(a))!;
        await unit.Cards.StageRelationRemovalAsync(card,actor);
        await unit.Cards.DeleteAsync(card);
        using (var winner = factory.Services.CreateScope())
        {
            (await Service(winner).StageMutationAsync(actor,boardId,new(a,b,"blocks"),hadGraph ? 1 : 0,false,default)).IsSuccess.Should().BeTrue();
            await winner.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        var deleting = () => unit.SaveChangesAsync();
        (await deleting.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        using var verify = factory.Services.CreateScope();
        var final = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await final.Cards.AnyAsync(c => c.Id == a)).Should().BeTrue();
        (await final.CardRelations.CountAsync(e => e.BoardId == boardId)).Should().Be(hadGraph ? 2 : 1);
        (await final.AuditLogs.AnyAsync(l => l.EntityId == boardId)).Should().BeFalse();
    }

    private BoardRelationService Service(IServiceScope scope) => new(
        new BoardDependencyRepository(scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>()),
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),Authorization());
    private static IAuthorizationService Authorization()
    {
        var auth = new Mock<IAuthorizationService>();
        auth.Setup(a => a.CanReadBoardAsync(It.IsAny<Guid>(),It.IsAny<Guid>())).ReturnsAsync(Result.Success(true));
        auth.Setup(a => a.CanWriteBoardAsync(It.IsAny<Guid>(),It.IsAny<Guid>())).ReturnsAsync(Result.Success(true));
        return auth.Object;
    }
    private async Task<(Guid Actor,Guid Board,Guid A,Guid B)> Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var tag = Guid.NewGuid().ToString("N");
        var owner = new User(tag,tag+"@example.com","hash");
        var board = new Board("Relations",ownerId:owner.Id); var column = new Column(board.Id,"Next",0);
        var a = new Card(board.Id,column.Id,"A"); var b = new Card(board.Id,column.Id,"B");
        db.AddRange(owner,board,column,a,b); await db.SaveChangesAsync();
        return (owner.Id,board.Id,a.Id,b.Id);
    }
}
