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
/// Unit/integration tests for the MCP board resource layer.
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
        services.AddScoped<LabelService>();

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

    private BoardResources CreateBoardResources(IServiceScope scope, Guid userId)
    {
        return new BoardResources(
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<ColumnService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            scope.ServiceProvider.GetRequiredService<LabelService>(),
            new FixedUserContextProvider(userId));
    }

    // ── StdioUserContextProvider tests ────────────────────────────────────────

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredUserId_ReturnsThatId()
    {
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

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
        (await provider.GetUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenNoConfiguredId_FallsBackToFirstUser()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("bob", "bob@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenNoUsersAndNoConfig_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var config = new ConfigurationBuilder().Build();

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        var act = () => provider.GetCurrentUserIdAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*no users found*");
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdIsEmpty_FallsBackToDb()
    {
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

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        (await provider.GetCurrentUserIdAsync()).Should().Be(user.Id);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdDoesNotExistInDb_FallsBackToFirstUser()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var realUser = new User("hank", "hank@example.com", "Password1!");
        await uow.Users.AddAsync(realUser);
        await uow.SaveChangesAsync();

        var phantomId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServer:DefaultUserId"] = phantomId.ToString()
            })
            .Build();

        var provider = new StdioUserContextProvider(config, dbContext, NullLogger<StdioUserContextProvider>.Instance);

        var resolved = await provider.GetCurrentUserIdAsync();
        resolved.Should().Be(realUser.Id, "phantom user ID should not be used; should fall back to first DB user");
        resolved.Should().NotBe(phantomId);
    }

    [Fact]
    public async Task StdioUserContextProvider_WhenConfiguredIdDoesNotExistAndNoUsers_Throws()
    {
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

        var act = () => provider.GetCurrentUserIdAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
           .WithMessage("*no users found*");
    }

    // ── BoardResources.ListBoards tests ──────────────────────────────────────

    [Fact]
    public async Task BoardResources_ListBoards_ReturnsCompactJsonWithRequiredFields()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("charlie", "charlie@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        await boardService.CreateBoardAsync(new CreateBoardDto("Alpha", null), user.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Beta", null), user.Id);

        var resources = CreateBoardResources(scope, user.Id);

        var json = await resources.ListBoards();

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
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("diana", "diana@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        await boardService.CreateBoardAsync(new CreateBoardDto("Active", null), user.Id);
        var archivedResult = await boardService.CreateBoardAsync(new CreateBoardDto("Archived", null), user.Id);
        await boardService.UpdateBoardAsync(archivedResult.Value.Id, new UpdateBoardDto(null, null, IsArchived: true), user.Id);

        var resources = CreateBoardResources(scope, user.Id);

        var json = await resources.ListBoards();

        using var doc = JsonDocument.Parse(json);
        var boards = doc.RootElement.GetProperty("boards");
        boards.GetArrayLength().Should().Be(1);
        boards[0].GetProperty("name").GetString().Should().Be("Active");
        boards[0].GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BoardResources_ListBoards_ReturnsCorrectColumnAndCardCounts()
    {
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

        var resources = CreateBoardResources(scope, user.Id);

        var json = await resources.ListBoards();

        using var doc = JsonDocument.Parse(json);
        var boardItem = doc.RootElement.GetProperty("boards")[0];
        boardItem.GetProperty("columnCount").GetInt32().Should().Be(2);
        boardItem.GetProperty("cardCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task BoardResources_ListBoards_EmptyResultWhenNoBoards()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();

        var user = new User("frank", "frank@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var resources = CreateBoardResources(scope, user.Id);

        var json = await resources.ListBoards();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("boards").GetArrayLength().Should().Be(0);
        root.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    // ── Multi-user authorization scoping tests ─────────────────────────────────

    [Fact]
    public async Task BoardResources_ListBoards_OnlyReturnsCurrentUserBoards()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var userAlice = new User("alice-scope", "alice-scope@example.com", "Password1!");
        var userBob = new User("bob-scope", "bob-scope@example.com", "Password1!");
        await uow.Users.AddAsync(userAlice);
        await uow.Users.AddAsync(userBob);
        await uow.SaveChangesAsync();

        var authService = new AuthorizationService(uow);
        var boardService = new BoardService(uow, authService);

        await boardService.CreateBoardAsync(new CreateBoardDto("Alice Board 1", null), userAlice.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Alice Board 2", null), userAlice.Id);
        await boardService.CreateBoardAsync(new CreateBoardDto("Bob Board 1", null), userBob.Id);

        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        var labelService = scope.ServiceProvider.GetRequiredService<LabelService>();

        var aliceResources = new BoardResources(boardService, columnService, cardService, labelService, new FixedUserContextProvider(userAlice.Id));
        var aliceJson = await aliceResources.ListBoards();

        var bobResources = new BoardResources(boardService, columnService, cardService, labelService, new FixedUserContextProvider(userBob.Id));
        var bobJson = await bobResources.ListBoards();

        using var aliceDoc = JsonDocument.Parse(aliceJson);
        var aliceBoards = aliceDoc.RootElement.GetProperty("boards");
        aliceBoards.GetArrayLength().Should().Be(2);
        aliceDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        foreach (var board in aliceBoards.EnumerateArray())
        {
            board.GetProperty("name").GetString().Should().StartWith("Alice Board");
        }

        using var bobDoc = JsonDocument.Parse(bobJson);
        var bobBoards = bobDoc.RootElement.GetProperty("boards");
        bobBoards.GetArrayLength().Should().Be(1);
        bobDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        bobBoards[0].GetProperty("name").GetString().Should().Be("Bob Board 1");
    }

    // ── BoardResources.GetBoardDetail tests ──────────────────────────────────

    [Fact]
    public async Task BoardResources_GetBoardDetail_ReturnsColumnsAndLabels()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var labelService = scope.ServiceProvider.GetRequiredService<LabelService>();

        var user = new User("detail-user", "detail@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("DetailBoard", null), user.Id);
        var boardId = board.Value.Id;

        await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Todo", null, 5));
        await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Done", null, null));
        await labelService.CreateLabelAsync(new CreateLabelDto(boardId, "bug", "#e74c3c"));

        var resources = CreateBoardResources(scope, user.Id);
        var json = await resources.GetBoardDetail(boardId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("id").GetGuid().Should().Be(boardId);
        root.GetProperty("name").GetString().Should().Be("DetailBoard");
        root.GetProperty("columns").GetArrayLength().Should().Be(2);
        root.GetProperty("labels").GetArrayLength().Should().Be(1);

        var firstLabel = root.GetProperty("labels").EnumerateArray().First();
        firstLabel.GetProperty("name").GetString().Should().Be("bug");
        firstLabel.GetProperty("color").GetString().Should().BeEquivalentTo("#e74c3c");
    }

    [Fact]
    public async Task BoardResources_GetBoardDetail_InvalidId_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = new User("invalid-user", "invalid@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var resources = CreateBoardResources(scope, user.Id);

        var act = () => resources.GetBoardDetail("not-a-guid");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*invalid board ID*");
    }

    // ── BoardResources.GetColumnCards tests ───────────────────────────────────

    [Fact]
    public async Task BoardResources_GetColumnCards_ReturnsCardsInColumn()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        var user = new User("cards-user", "cards@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("CardsBoard", null), user.Id);
        var boardId = board.Value.Id;

        var col = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Backlog", null, null));
        var colId = col.Value.Id;
        await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Card A", "desc", null, null));
        await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Card B", null, null, null));

        var resources = CreateBoardResources(scope, user.Id);
        var json = await resources.GetColumnCards(boardId.ToString(), colId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("columnName").GetString().Should().Be("Backlog");
        root.GetProperty("totalCount").GetInt32().Should().Be(2);
        root.GetProperty("cards").GetArrayLength().Should().Be(2);

        var firstCard = root.GetProperty("cards").EnumerateArray().First();
        firstCard.GetProperty("title").GetString().Should().Be("Card A");
        firstCard.GetProperty("hasDescription").GetBoolean().Should().BeTrue();
    }

    // ── BoardResources.GetCardDetail tests ───────────────────────────────────

    [Fact]
    public async Task BoardResources_GetCardDetail_ReturnsFullDetail()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        var user = new User("detail-card-user", "detailcard@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("CardBoard", null), user.Id);
        var boardId = board.Value.Id;
        var col = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Active", null, null));
        var card = await cardService.CreateCardAsync(new CreateCardDto(boardId, col.Value.Id, "My Card", "Full description", null, null));

        var resources = CreateBoardResources(scope, user.Id);
        var json = await resources.GetCardDetail(boardId.ToString(), card.Value.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("title").GetString().Should().Be("My Card");
        root.GetProperty("description").GetString().Should().Be("Full description");
        root.GetProperty("columnName").GetString().Should().Be("Active");
    }

    // ── BoardResources.GetBoardLabels tests ──────────────────────────────────

    [Fact]
    public async Task BoardResources_GetBoardLabels_ReturnsLabels()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var labelService = scope.ServiceProvider.GetRequiredService<LabelService>();

        var user = new User("labels-user", "labels@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("LabelBoard", null), user.Id);
        var boardId = board.Value.Id;
        await labelService.CreateLabelAsync(new CreateLabelDto(boardId, "feature", "#2ecc71"));
        await labelService.CreateLabelAsync(new CreateLabelDto(boardId, "bug", "#e74c3c"));

        var resources = CreateBoardResources(scope, user.Id);
        var json = await resources.GetBoardLabels(boardId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("boardName").GetString().Should().Be("LabelBoard");
        root.GetProperty("totalCount").GetInt32().Should().Be(2);
        root.GetProperty("labels").GetArrayLength().Should().Be(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Stub IUserContextProvider that returns a fixed user ID.</summary>
    internal sealed class FixedUserContextProvider : IUserContextProvider
    {
        private readonly Guid _userId;
        public FixedUserContextProvider(Guid userId) => _userId = userId;
        public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_userId);
        public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(_userId);
    }
}
