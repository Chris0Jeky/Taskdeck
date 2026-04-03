using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for MCP read tools, write tools, and proposal tools.
/// </summary>
public class McpToolsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _dbPath;

    public McpToolsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-tools-{Guid.NewGuid():N}.db");

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
        services.AddScoped<AuthorizationService>();
        services.AddScoped<IAuthorizationService>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<AutomationProposalService>();
        services.AddScoped<IAutomationProposalService>(sp => sp.GetRequiredService<AutomationProposalService>());
        services.AddScoped<CaptureService>();
        services.AddScoped<ICaptureService>(sp => sp.GetRequiredService<CaptureService>());

        _serviceProvider = services.BuildServiceProvider();

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

    private async Task<(User User, Guid BoardId, Guid ColumnId)> SetupBoardAsync(IServiceScope scope)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();

        var user = new User($"user-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("TestBoard", null), user.Id);
        var col = await columnService.CreateColumnAsync(new CreateColumnDto(board.Value.Id, "Backlog", null, null));

        return (user, board.Value.Id, col.Value.Id);
    }

    // ── ReadTools tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchCards_ReturnsMatchingCards()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Fix login bug", "auth issue", null, null));
        await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Add feature X", null, null, null));

        var tools = new ReadTools(
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            cardService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await tools.SearchCards("login");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var cards = root.GetProperty("cards");
        cards.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchCards_RespectsMaxResults()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        for (int i = 0; i < 5; i++)
            await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, $"Card {i}", null, null, null));

        var tools = new ReadTools(
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            cardService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await tools.SearchCards("Card", max_results: 2);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("cards").GetArrayLength().Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchCards_InvalidBoardId_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, _, _) = await SetupBoardAsync(scope);

        var tools = new ReadTools(
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await tools.SearchCards("test", board_id: "not-a-guid");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetBoardSummary_ReturnsColumnBreakdown()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

        await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Task 1", null, null, null));

        var tools = new ReadTools(
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            cardService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await tools.GetBoardSummary(boardId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("name").GetString().Should().Be("TestBoard");
        root.GetProperty("totalCardCount").GetInt32().Should().Be(1);
        root.GetProperty("columns").GetArrayLength().Should().Be(1);
    }

    // ── WriteTools tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCard_ReturnsProposalId()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.CreateCard(boardId.ToString(), "New Card", colId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("proposalId", out var proposalId).Should().BeTrue("write tool must return proposalId");
        root.GetProperty("status").GetString().Should().Be("Pending");
        root.TryGetProperty("message", out _).Should().BeTrue();
        proposalId.GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateCard_InvalidBoardId_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, _, _) = await SetupBoardAsync(scope);

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.CreateCard("not-a-guid", "Card");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MoveCard_ReturnsProposalId()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();

        var card = await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Moveable", null, null, null));
        var col2 = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Done", null, null));

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.MoveCard(boardId.ToString(), card.Value.Id.ToString(), col2.Value.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("proposalId", out _).Should().BeTrue("move_card must return proposalId");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task UpdateCard_ReturnsProposalId()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        var card = await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Original", null, null, null));

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.UpdateCard(boardId.ToString(), card.Value.Id.ToString(), title: "Updated Title");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("proposalId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCard_NoFieldsProvided_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, _) = await SetupBoardAsync(scope);

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.UpdateCard(boardId.ToString(), Guid.NewGuid().ToString());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue("should error when no fields provided");
    }

    [Fact]
    public async Task ArchiveCard_ReturnsHighRiskProposal()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        var card = await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "ToArchive", null, null, null));

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.ArchiveCard(boardId.ToString(), card.Value.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("proposalId", out _).Should().BeTrue();

        // Verify the proposal was created with High risk
        var proposalId = doc.RootElement.GetProperty("proposalId").GetGuid();
        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();
        var proposal = await proposalService.GetProposalByIdAsync(proposalId);
        proposal.Value.RiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public async Task CreateCapture_AddsToInboxDirectly()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, _) = await SetupBoardAsync(scope);

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.CreateCapture("Remember to fix the tests", boardId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("captureId", out _).Should().BeTrue("create_capture should return captureId");
        root.TryGetProperty("proposalId", out _).Should().BeFalse("create_capture should NOT return proposalId");
    }

    [Fact]
    public async Task CreateColumn_ReturnsProposalId()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, _) = await SetupBoardAsync(scope);

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var json = await tools.CreateColumn(boardId.ToString(), "In Progress", wip_limit: 3);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("proposalId", out _).Should().BeTrue();
        doc.RootElement.GetProperty("status").GetString().Should().Be("Pending");
    }

    // ── ProposalTools tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetProposalStatus_ReturnsStatusInfo()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);

        // Create a proposal via write tool
        var writeTools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var createJson = await writeTools.CreateCard(boardId.ToString(), "Status Check Card", colId.ToString());
        using var createDoc = JsonDocument.Parse(createJson);
        var proposalId = createDoc.RootElement.GetProperty("proposalId").GetGuid();

        // Now check status
        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var statusJson = await proposalTools.GetProposalStatus(proposalId.ToString());

        using var statusDoc = JsonDocument.Parse(statusJson);
        var root = statusDoc.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(proposalId);
        root.GetProperty("status").GetString().Should().Be("PendingReview");
        root.GetProperty("operationCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ListProposals_ReturnsPendingByDefault()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);

        var writeTools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        await writeTools.CreateCard(boardId.ToString(), "Proposal Card 1", colId.ToString());
        await writeTools.CreateCard(boardId.ToString(), "Proposal Card 2", colId.ToString());

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await proposalTools.ListProposals();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListProposals_FiltersByStatus()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);

        var writeTools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        await writeTools.CreateCard(boardId.ToString(), "FilterCard", colId.ToString());

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        // Filter by Applied status (should be empty since we just created)
        var json = await proposalTools.ListProposals(status: "Applied");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListProposals_InvalidStatus_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, _, _) = await SetupBoardAsync(scope);

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await proposalTools.ListProposals(status: "InvalidStatus");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetProposalStatus_InvalidId_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, _, _) = await SetupBoardAsync(scope);

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await proposalTools.GetProposalStatus("not-a-guid");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DismissProposal_InvalidId_ReturnsError()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, _, _) = await SetupBoardAsync(scope);

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await proposalTools.DismissProposal("not-a-guid");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DismissProposal_PendingProposal_CannotBeDismissed()
    {
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);

        var writeTools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        var createJson = await writeTools.CreateCard(boardId.ToString(), "Dismiss Me", colId.ToString());
        using var createDoc = JsonDocument.Parse(createJson);
        var proposalId = createDoc.RootElement.GetProperty("proposalId").GetGuid();

        var proposalTools = new ProposalTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await proposalTools.DismissProposal(proposalId.ToString());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("dismissed").GetInt32().Should().Be(0,
            "pending proposals should not be dismissible");
    }

    // ── Write tools review-first compliance test ─────────────────────────────

    [Fact]
    public async Task WriteTools_NeverMutateDirectly_AlwaysReturnProposalId()
    {
        // This test verifies the GP-06 invariant: all write tools must return
        // a proposalId and never mutate state directly.
        using var scope = _serviceProvider.CreateScope();
        var (user, boardId, colId) = await SetupBoardAsync(scope);
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        var card = await cardService.CreateCardAsync(new CreateCardDto(boardId, colId, "Immutable", null, null, null));
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        var col2 = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, "Done", null, null));

        var tools = new WriteTools(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id),
            scope.ServiceProvider.GetRequiredService<ICaptureService>());

        // All write tools that should produce proposals
        var results = new[]
        {
            await tools.CreateCard(boardId.ToString(), "Test", colId.ToString()),
            await tools.MoveCard(boardId.ToString(), card.Value.Id.ToString(), col2.Value.Id.ToString()),
            await tools.UpdateCard(boardId.ToString(), card.Value.Id.ToString(), title: "New"),
            await tools.ArchiveCard(boardId.ToString(), card.Value.Id.ToString()),
            await tools.CreateColumn(boardId.ToString(), "NewCol"),
        };

        foreach (var json in results)
        {
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("proposalId", out _).Should().BeTrue(
                $"All write tools must return proposalId. Got: {json}");
            doc.RootElement.GetProperty("status").GetString().Should().Be("Pending");
        }
    }
}
