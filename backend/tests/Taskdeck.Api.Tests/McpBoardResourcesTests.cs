using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Mcp;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Unit/integration tests for the MCP board resource layer (Phase 1).
/// Uses isolated SQLite databases (one per test instance) to avoid flakiness.
/// </summary>
public class McpBoardResourcesTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _dbPath;

    public McpBoardResourcesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-tests-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}"
                })
                .Build());
        services.AddScoped<BoardService>();
        services.AddScoped<ColumnService>();
        services.AddScoped<CardService>();

        _serviceProvider = services.BuildServiceProvider();

        // Run EF migrations so schema is ready
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        db.Database.Migrate();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm", $"{_dbPath}-journal" })
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch (IOException) { /* cleanup is best-effort */ }
            }
        }
    }

    // ── StdioUserContextProvider tests ────────────────────────────────────────

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredUserId_ReturnsThatId()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("alice", "alice@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:DefaultUserId"] = user.Id.ToString()
            })
            .Build();

        // Act
        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Assert
        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
        (await provider.GetUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenNoConfiguredId_FallsBackToFirstUser()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("bob", "bob@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();

        // Act
        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Assert
        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenNoUsersAndNoConfig_Throws()
    {
        // Arrange — empty DB, no config
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var config = new ConfigurationBuilder().Build();

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Act & Assert
        var act = () => provider.GetCurrentUserIdAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*no users found*");
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdIsEmpty_FallsBackToDb()
    {
        // Arrange — Guid.Empty configured should be treated as invalid
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("grace", "grace@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:DefaultUserId"] = Guid.Empty.ToString()
            })
            .Build();

        // Act
        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Assert — should fall back to DB, not return Guid.Empty
        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdDoesNotExistInDb_FallsBackToFirstUser()
    {
        // Arrange — configured GUID is valid but does not match any user in the database
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var realUser = new User("hank", "hank@example.com", "Password1!");
        await uow.Users.AddAsync(realUser);
        await uow.SaveChangesAsync();

        var phantomId = Guid.NewGuid(); // does not exist in DB
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:DefaultUserId"] = phantomId.ToString()
            })
            .Build();

        // Act
        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Assert — should fall back to the real user, not return the phantom ID
        var resolved = await provider.GetCurrentUserIdAsync();
        resolved.Should().Be(realUser.Id, "phantom user ID should not be used; should fall back to first DB user");
        resolved.Should().NotBe(phantomId);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdDoesNotExistAndNoUsers_Throws()
    {
        // Arrange — configured GUID doesn't exist, and DB has no users
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var phantomId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:DefaultUserId"] = phantomId.ToString()
            })
            .Build();

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        // Act & Assert
        var act = () => provider.GetCurrentUserIdAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*no users found*");
    }

    // ── BoardResources tests ──────────────────────────────────────────────────

    [Fact]
    public async Task BoardResources_ListBoards_ReturnsCompactJsonWithRequiredFields()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("charlie", "charlie@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        await boardService.CreateBoardAsync(new CreateBoardDto("Alpha", null), user.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Beta", null), user.Id);

        var resources = new BoardResources(boardService, new FixedUserContextProvider(user.Id));

        // Act
        var json = await resources.ListBoards();

        // Assert — valid JSON with correct shape
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("boards", out var boardsArray).Should().BeTrue("result must have 'boards' array");
        root.TryGetProperty("totalCount", out var totalCount).Should().BeTrue("result must have 'totalCount'");

        boardsArray.ValueKind.Should().Be(JsonValueKind.Array);
        boardsArray.GetArrayLength().Should().Be(2);
        totalCount.GetInt32().Should().Be(2);

        foreach (var board in boardsArray.EnumerateArray())
        {
            board.TryGetProperty("id", out _).Should().BeTrue("each board must have 'id'");
            board.TryGetProperty("name", out _).Should().BeTrue("each board must have 'name'");
            board.TryGetProperty("columnCount", out _).Should().BeTrue("each board must have 'columnCount'");
            board.TryGetProperty("cardCount", out _).Should().BeTrue("each board must have 'cardCount'");
            board.TryGetProperty("isArchived", out _).Should().BeTrue("each board must have 'isArchived'");
            board.TryGetProperty("updatedAt", out _).Should().BeTrue("each board must have 'updatedAt'");
        }
    }

    [Fact]
    public async Task BoardResources_ListBoards_ExcludesArchivedBoards()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("diana", "diana@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        await boardService.CreateBoardAsync(new CreateBoardDto("Active", null), user.Id);
        var archivedResult = await boardService.CreateBoardAsync(new CreateBoardDto("Archived", null), user.Id);
        await boardService.UpdateBoardAsync(archivedResult.Value.Id, new UpdateBoardDto(null, null, IsArchived: true), user.Id);

        var resources = new BoardResources(boardService, new FixedUserContextProvider(user.Id));

        // Act
        var json = await resources.ListBoards();

        // Assert — only active board is returned
        using var doc = JsonDocument.Parse(json);
        var boards = doc.RootElement.GetProperty("boards");
        boards.GetArrayLength().Should().Be(1);
        boards[0].GetProperty("name").GetString().Should().Be("Active");
        boards[0].GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BoardResources_ListBoards_ReturnsCorrectColumnAndCardCounts()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        var user = new User("eve", "eve@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("CountTest", null), user.Id);
        var boardId = board.Value.Id;

        var col1 = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Backlog", null, null));
        await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Done", null, null));
        await cardService.CreateCardAsync(new CreateCardDto(boardId, col1.Value.Id, "Task A", null, null, null));

        var resources = new BoardResources(boardService, new FixedUserContextProvider(user.Id));

        // Act
        var json = await resources.ListBoards();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var boardItem = doc.RootElement.GetProperty("boards")[0];
        boardItem.GetProperty("columnCount").GetInt32().Should().Be(2);
        boardItem.GetProperty("cardCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task BoardResources_ListBoards_EmptyResultWhenNoBoards()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("frank", "frank@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var resources = new BoardResources(boardService, new FixedUserContextProvider(user.Id));

        // Act
        var json = await resources.ListBoards();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("boards").GetArrayLength().Should().Be(0);
        root.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    // ── Multi-user authorization scoping tests ─────────────────────────────────

    [Fact]
    public async Task BoardResources_ListBoards_OnlyReturnsCurrentUserBoards()
    {
        // Arrange — two users, each with their own board
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var userAlice = new User("alice-scope", "alice-scope@example.com", "Password1!");
        var userBob = new User("bob-scope", "bob-scope@example.com", "Password1!");
        await uow.Users.AddAsync(userAlice);
        await uow.Users.AddAsync(userBob);
        await uow.SaveChangesAsync();

        // Create a BoardService with AuthorizationService so ownership filtering is enforced.
        var authService = new AuthorizationService(uow);
        var boardService = new BoardService(uow, authService);

        await boardService.CreateBoardAsync(new CreateBoardDto("Alice Board 1", null), userAlice.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Alice Board 2", null), userAlice.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Bob Board 1", null), userBob.Id);

        // Act — list boards as Alice via MCP BoardResources
        var aliceResources = new BoardResources(boardService, new FixedUserContextProvider(userAlice.Id));
        var aliceJson = await aliceResources.ListBoards();

        // Act — list boards as Bob via MCP BoardResources
        var bobResources = new BoardResources(boardService, new FixedUserContextProvider(userBob.Id));
        var bobJson = await bobResources.ListBoards();

        // Assert — Alice sees only her 2 boards
        using var aliceDoc = JsonDocument.Parse(aliceJson);
        var aliceBoards = aliceDoc.RootElement.GetProperty("boards");
        aliceBoards.GetArrayLength().Should().Be(2);
        aliceDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        foreach (var board in aliceBoards.EnumerateArray())
        {
            board.GetProperty("name").GetString().Should().StartWith("Alice Board");
        }

        // Assert — Bob sees only his 1 board
        using var bobDoc = JsonDocument.Parse(bobJson);
        var bobBoards = bobDoc.RootElement.GetProperty("boards");
        bobBoards.GetArrayLength().Should().Be(1);
        bobDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        bobBoards[0].GetProperty("name").GetString().Should().Be("Bob Board 1");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Stub IUserContextProvider that returns a fixed user ID.</summary>
    private sealed class FixedUserContextProvider : IUserContextProvider
    {
        private readonly Guid _userId;
        public FixedUserContextProvider(Guid userId) => _userId = userId;
        public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_userId);
        public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(_userId);
    }
}
