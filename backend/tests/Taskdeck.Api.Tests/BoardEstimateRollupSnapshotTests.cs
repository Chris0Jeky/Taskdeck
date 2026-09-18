using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Real-provider proof for the estimate-rollup read boundary (#3058).
/// The reader pauses after materialising cards while a second SQLite connection
/// moves that card, deletes its old column and revokes its assignee's membership.
/// A deferred WAL read transaction must let the writer commit without exposing
/// any of those newer rows to the remaining reader queries.
/// </summary>
public sealed class BoardEstimateRollupSnapshotTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"taskdeck-rollup-snapshot-{Guid.NewGuid():N}.db");
    private readonly TaskCompletionSource<bool> _cardsRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _continueRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ConcurrentMoveDeleteAndMembershipRevocation_ReturnOneCoherentSnapshot()
    {
        await using var provider = BuildProvider();
        var fixture = await SeedAsync(provider);

        await using var readerScope = provider.CreateAsyncScope();
        var service = readerScope.ServiceProvider.GetRequiredService<IBoardEstimateRollupService>();
        var readTask = service.GetAsync(fixture.BoardId, fixture.OwnerId);

        await _cardsRead.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var writerScope = provider.CreateAsyncScope();
        var writer = writerScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var card = await writer.Cards.SingleAsync(row => row.Id == fixture.CardId);
        var obsoleteColumn = await writer.Columns.SingleAsync(row => row.Id == fixture.OriginalColumnId);
        var revokedAccess = await writer.BoardAccesses.SingleAsync(row =>
            row.BoardId == fixture.BoardId && row.UserId == fixture.MemberId);

        card.MoveToColumn(fixture.NewColumnId, 0);
        writer.Remove(obsoleteColumn);
        writer.Remove(revokedAccess);

        var writeTask = writer.SaveChangesAsync();
        Exception? writerBlocked = null;
        try
        {
            await writeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException exception)
        {
            writerBlocked = exception;
        }
        finally
        {
            _continueRead.TrySetResult(true);
        }

        var result = await readTask.WaitAsync(TimeSpan.FromSeconds(10));
        await writeTask.WaitAsync(TimeSpan.FromSeconds(10));

        writerBlocked.Should().BeNull(
            "the read-only snapshot must use SQLite's deferred BEGIN and not reserve the WAL writer slot");
        result.IsSuccess.Should().BeTrue();
        result.Value.Board.Should().Be(new(1, 60, 0));
        result.Value.Columns.Should().HaveCount(2);
        result.Value.Columns.Single(row => row.ColumnId == fixture.OriginalColumnId).Totals
            .Should().Be(new(1, 60, 0));
        result.Value.Columns.Single(row => row.ColumnId == fixture.NewColumnId).Totals
            .Should().Be(new(0, 0, 0));
        result.Value.Participants.Single(row => row.UserId == fixture.MemberId).Totals
            .Should().Be(new(1, 60, 0));
        result.Value.Participants.Single(row => row.UserId == fixture.OwnerId).Totals
            .Should().Be(new(0, 0, 0));
        result.Value.Unassigned.Should().Be(new(0, 0, 0));

        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await verify.Cards.SingleAsync(row => row.Id == fixture.CardId)).ColumnId
            .Should().Be(fixture.NewColumnId);
        (await verify.Columns.AnyAsync(row => row.Id == fixture.OriginalColumnId)).Should().BeFalse();
        (await verify.BoardAccesses.AnyAsync(row =>
            row.BoardId == fixture.BoardId && row.UserId == fixture.MemberId)).Should().BeFalse();
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestSqlite.ConnectionString(_dbPath),
                ["Connectors:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["Cache:Provider"] = "none"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        // Keep production UnitOfWork, ColumnRepository and CardAssignmentStore.
        // Only the first repository read is decorated with a deterministic gate.
        services.RemoveAll<ICardRepository>();
        services.AddScoped<ICardRepository>(serviceProvider =>
        {
            var context = serviceProvider.GetRequiredService<TaskdeckDbContext>();
            var repository = new Mock<ICardRepository>();
            repository
                .Setup(candidate => candidate.GetForEstimateRollupsAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (Guid boardId, CancellationToken cancellationToken) =>
                {
                    var cards = await context.Cards
                        .AsNoTracking()
                        .IgnoreAutoIncludes()
                        .Where(card => card.BoardId == boardId && !card.IsArchived)
                        .Include(card => card.Assignments)
                        .AsSingleQuery()
                        .ToListAsync(cancellationToken);

                    _cardsRead.TrySetResult(true);
                    await _continueRead.Task.WaitAsync(cancellationToken);
                    return (IReadOnlyList<Card>)cards;
                });
            return repository.Object;
        });

        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(service => service.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));
        services.RemoveAll<IAuthorizationService>();
        services.AddScoped<IAuthorizationService>(_ => authorization.Object);
        services.AddScoped<IBoardEstimateRollupService, BoardEstimateRollupService>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private async Task<Fixture> SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        await context.Database.EnsureCreatedAsync();

        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            (await command.ExecuteScalarAsync()).Should().Be("wal");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        var owner = new User("snapshot-owner", "snapshot-owner@example.com", "hash");
        var member = new User("snapshot-member", "snapshot-member@example.com", "hash");
        var board = new Board("Snapshot board", ownerId: owner.Id);
        var original = new Column(board.Id, "Original", 0);
        var next = new Column(board.Id, "Next", 1);
        var card = new Card(board.Id, original.Id, "Estimate", position: 0);
        card.SetEstimatedEffortMinutes(60);
        var access = new BoardAccess(board.Id, member.Id, UserRole.Viewer, owner.Id);
        var assignment = new CardAssignment(card.Id, member.Id, owner.Id);

        context.AddRange(owner, member, board, original, next, card, access, assignment);
        await context.SaveChangesAsync();

        return new(
            board.Id,
            owner.Id,
            member.Id,
            original.Id,
            next.Id,
            card.Id);
    }

    public void Dispose()
    {
        _continueRead.TrySetResult(true);
        foreach (var path in TestWebApplicationFactory.GetDatabaseCleanupTargets(_dbPath))
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Test cleanup must not obscure a more useful assertion failure.
            }
        }
    }

    private sealed record Fixture(
        Guid BoardId,
        Guid OwnerId,
        Guid MemberId,
        Guid OriginalColumnId,
        Guid NewColumnId,
        Guid CardId);
}
