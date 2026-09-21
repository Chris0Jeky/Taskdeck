using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class BoardAccessRepositoryFreshnessTests
{
    [Fact]
    public async Task GetByBoardAndUserAsync_reads_role_changes_after_an_earlier_read()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-board-access-freshness-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .Options;

            var owner = new User("access-freshness-owner", $"access-freshness-owner-{Guid.NewGuid():N}@example.com", "hash");
            var member = new User("access-freshness-member", $"access-freshness-member-{Guid.NewGuid():N}@example.com", "hash");
            var board = new Board("Access freshness", ownerId: owner.Id);
            var access = new BoardAccess(board.Id, member.Id, UserRole.Editor, owner.Id);

            await using (var seed = new TaskdeckDbContext(options))
            {
                await seed.Database.MigrateAsync();
                seed.Users.AddRange(owner, member);
                seed.Boards.Add(board);
                seed.BoardAccesses.Add(access);
                await seed.SaveChangesAsync();
            }

            await using var reader = new TaskdeckDbContext(options);
            var repository = new BoardAccessRepository(reader);
            var initial = await repository.GetByBoardAndUserAsync(board.Id, member.Id);
            initial!.Role.Should().Be(UserRole.Editor);

            await using (var writer = new TaskdeckDbContext(options))
            {
                var persisted = await writer.BoardAccesses.SingleAsync(value =>
                    value.BoardId == board.Id && value.UserId == member.Id);
                persisted.UpdateRole(UserRole.Viewer, owner.Id);
                await writer.SaveChangesAsync();
            }

            var refreshed = await repository.GetByBoardAndUserAsync(board.Id, member.Id);
            refreshed!.Role.Should().Be(UserRole.Viewer);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(dbPath + suffix); }
                catch (IOException) { }
            }
        }
    }
}
