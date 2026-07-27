using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Repositories;
using Taskdeck.Integration.Tests.Fixtures;
using Xunit;

namespace Taskdeck.Integration.Tests;

[Collection(PostgresTestCollection.Name)]
public class CardRepositoryPostgresIntegrationTests : PostgresIntegrationTestBase
{
    public CardRepositoryPostgresIntegrationTests(PostgresContainerFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task GetTitleMatchesByBoardIdAsync_PreservesUnicodeMatchingAndHardBounds()
    {
        SkipIfDockerUnavailable();
        var user = new User("postgres-card-title-match", "postgres-card-title-match@example.com", "hash");
        Db.Users.Add(user);
        var board = new Board("Postgres title match board", ownerId: user.Id);
        Db.Boards.Add(board);
        var column = new Column(board.Id, "Todo", 0);
        Db.Columns.Add(column);

        var unicode = new Card(board.Id, column.Id, "Caf\u00e9 planning", position: 0);
        var later = new Card(board.Id, column.Id, "Later card", position: 1);
        Db.Cards.AddRange(unicode, later);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var repository = new CardRepository(Db);
        var unicodeMatches = await repository.GetTitleMatchesByBoardIdAsync(
            board.Id,
            "CAF\u00c9",
            maxResults: 1,
            maxCardsToScan: 2);
        var truncatedScan = await repository.GetTitleMatchesByBoardIdAsync(
            board.Id,
            "absent",
            maxResults: 1,
            maxCardsToScan: 1);

        unicodeMatches.CardIds.Should().Equal(unicode.Id);
        unicodeMatches.IsExhaustive.Should().BeTrue();
        truncatedScan.CardIds.Should().BeEmpty();
        truncatedScan.IsExhaustive.Should().BeFalse();
    }
}
