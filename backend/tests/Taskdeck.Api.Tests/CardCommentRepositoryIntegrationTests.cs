using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardCommentRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CardCommentRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CountByCardIdAsync_ExcludesSoftDeletedComments()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ICardCommentRepository>();

        var user = new User($"comment-{Guid.NewGuid():N}", $"comment-count-{Guid.NewGuid():N}@example.com", "hash");
        var board = new Board("Comment count board", ownerId: user.Id);
        var column = new Column(board.Id, "Todo", 0);
        var card = new Card(board.Id, column.Id, "Commented card");
        var activeComment = new CardComment(card.Id, board.Id, user.Id, "Still active");
        var deletedComment = new CardComment(card.Id, board.Id, user.Id, "Now deleted");
        deletedComment.SoftDelete();

        db.Users.Add(user);
        db.Boards.Add(board);
        db.Columns.Add(column);
        db.Cards.Add(card);
        db.CardComments.AddRange(activeComment, deletedComment);
        await db.SaveChangesAsync();

        var count = await repo.CountByCardIdAsync(card.Id);

        count.Should().Be(1);
    }
}
