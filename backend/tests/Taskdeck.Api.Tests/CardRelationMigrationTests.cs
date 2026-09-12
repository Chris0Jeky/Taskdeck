using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;
namespace Taskdeck.Api.Tests;

public class CardRelationMigrationTests
{
    [Fact]
    public async Task BackfillAndDownUp_PreserveLatestDependenciesEmptyHeadersAndUnrelatedCardMaterial()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TaskdeckDbContext(new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(connection).Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddCanonicalCardRelations"));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        var owner = new User("relation-migration", "relation@example.com", "hash");
        var board = new Board("Keep", ownerId: owner.Id);
        var other = new Board("Other", ownerId: owner.Id);
        var empty = new Board("Empty", ownerId: owner.Id);
        var fresh = new Board("New after upgrade", ownerId: owner.Id);
        var column = new Column(board.Id,"Next",0);
        var foreignColumn = new Column(other.Id,"Other",0);
        var freshColumn = new Column(fresh.Id,"New",0);
        var a = new Card(board.Id,column.Id,"Original", "keep material");
        var b = new Card(board.Id,column.Id,"Archived");
        var c = new Card(board.Id,column.Id,"Replacement");
        var foreign = new Card(other.Id,foreignColumn.Id,"Foreign");
        var newA = new Card(fresh.Id,freshColumn.Id,"New A");
        var newB = new Card(fresh.Id,freshColumn.Id,"New B");
        a.SetParent(c.Id); a.SetEstimatedEffortMinutes(90); b.Archive();
        db.AddRange(owner,board,other,empty,fresh,column,foreignColumn,freshColumn,a,b,c,foreign,newA,newB,new CardAssignment(a.Id,owner.Id,owner.Id));
        await db.SaveChangesAsync();
        var oldJson = JsonSerializer.Serialize(new[] { new CardDependency(a.Id,b.Id), new(a.Id,Guid.NewGuid()), new(a.Id,foreign.Id) });
        // JSON Guid text is lowercase while EF stores upper-case Id text.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO BoardDependencies(BoardId,Revision,EdgesJson) VALUES({board.Id},7,{oldJson})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO BoardDependencies(BoardId,Revision,EdgesJson) VALUES({empty.Id},3,'[]')");
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(migrations[index]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        var graph = await db.Set<BoardDependencies>().SingleAsync(g => g.BoardId == board.Id);
        graph.Revision.Should().Be(7);
        graph.ReadRelations().Should().Equal(new CardRelationEdge(b.Id,a.Id,"blocks"));
        (await db.Cards.FindAsync(b.Id))!.IsArchived.Should().BeTrue();
        graph.ReplaceRelations([new(c.Id,a.Id,"blocks"),new(a.Id,b.Id,"duplicates")]);
        var newGraph = new BoardDependencies(fresh.Id);
        newGraph.ReplaceRelations([new(newA.Id,newB.Id,"blocks"),new(newA.Id,newB.Id,"spawned-from")]);
        db.Add(newGraph);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(migrations[index-1]);
        var json = await db.Database.SqlQuery<string>($"SELECT EdgesJson AS Value FROM BoardDependencies WHERE BoardId={board.Id}").SingleAsync();
        JsonSerializer.Deserialize<CardDependency[]>(json).Should().Equal(new CardDependency(a.Id,c.Id));
        (await db.Database.SqlQuery<string>($"SELECT EdgesJson AS Value FROM BoardDependencies WHERE BoardId={empty.Id}").SingleAsync()).Should().Be("[]");
        var freshJson = await db.Database.SqlQuery<string>($"SELECT EdgesJson AS Value FROM BoardDependencies WHERE BoardId={fresh.Id}").SingleAsync();
        JsonSerializer.Deserialize<CardDependency[]>(freshJson).Should().Equal(new CardDependency(newB.Id,newA.Id));
        (await db.Database.SqlQuery<long>($"SELECT Revision AS Value FROM BoardDependencies WHERE BoardId={board.Id}").SingleAsync()).Should().Be(8);
        await migrator.MigrateAsync(migrations[index]);
        var restored = await db.Set<BoardDependencies>().SingleAsync(g => g.BoardId == board.Id);
        restored.ReadRelations().Should().Equal(new CardRelationEdge(c.Id,a.Id,"blocks"));
        (await db.Set<BoardDependencies>().SingleAsync(g => g.BoardId == empty.Id)).Revision.Should().Be(3);
        (await db.Set<BoardDependencies>().SingleAsync(g => g.BoardId == fresh.Id)).ReadRelations().Should().Equal(new CardRelationEdge(newA.Id,newB.Id,"blocks"));
        var savedA = (await db.Cards.FindAsync(a.Id))!;
        savedA.ParentCardId.Should().Be(c.Id); savedA.EstimatedEffortMinutes.Should().Be(90);
        savedA.Title.Should().Be(a.Title); savedA.Description.Should().Be(a.Description);
        savedA.UpdatedAt.Should().Be(a.UpdatedAt); savedA.ColumnId.Should().Be(column.Id);
        (await db.Set<CardAssignment>().SingleAsync()).UserId.Should().Be(owner.Id);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_foreign_key_check").SingleAsync()).Should().Be(0);
    }
}
