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
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for MCP CaptureResources and ProposalResources.
/// </summary>
public class McpResourcesTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _dbPath;

    public McpResourcesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-resources-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                    ["Connectors:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
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

    // ── ProposalResources tests ──────────────────────────────────────────────

    [Fact]
    public async Task ProposalResources_ListProposals_ReturnsCompactJson()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();

        var user = new User("prop-user", "prop@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        // Create a proposal
        await proposalService.CreateProposalAsync(new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: user.Id,
            Summary: "Test proposal",
            RiskLevel: RiskLevel.Medium,
            CorrelationId: Guid.NewGuid().ToString(),
            Operations: new List<CreateProposalOperationDto>
            {
                new(0, "create", "card", "{}", Guid.NewGuid().ToString())
            }));

        var resources = new ProposalResources(
            proposalService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await resources.ListProposals();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("proposals", out _).Should().BeTrue();
        root.TryGetProperty("totalCount", out _).Should().BeTrue();
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var first = root.GetProperty("proposals").EnumerateArray().First();
        first.TryGetProperty("id", out _).Should().BeTrue();
        first.TryGetProperty("summary", out _).Should().BeTrue();
        first.TryGetProperty("status", out _).Should().BeTrue();
        first.TryGetProperty("riskLevel", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_ReturnsOperations()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();

        var user = new User("detail-prop-user", "detailprop@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        // Detail retrieval now routes the preview through the diff-path gates (#1415), so the
        // proposal's operations must clear the shared operation-contract validator. Two board
        // updates on the requester's own board keep the two-operation shape while passing.
        var board = await boardService.CreateBoardAsync(new CreateBoardDto("DetailBoard", null), user.Id);
        board.IsSuccess.Should().BeTrue();
        var op0 = JsonSerializer.Serialize(new { name = "Rename one", boardId = board.Value.Id });
        var op1 = JsonSerializer.Serialize(new { name = "Rename two", boardId = board.Value.Id });

        var proposal = await proposalService.CreateProposalAsync(new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: user.Id,
            Summary: "Detail test",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: board.Value.Id,
            Operations: new List<CreateProposalOperationDto>
            {
                new(0, "update", "board", op0, Guid.NewGuid().ToString(), TargetId: board.Value.Id.ToString()),
                new(1, "update", "board", op1, Guid.NewGuid().ToString(), TargetId: board.Value.Id.ToString())
            }));
        proposal.IsSuccess.Should().BeTrue();

        var resources = new ProposalResources(
            proposalService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await resources.GetProposalDetail(proposal.Value.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(proposal.Value.Id);
        root.GetProperty("summary").GetString().Should().Be("Detail test");
        root.GetProperty("operationCount").GetInt32().Should().Be(2);
        root.GetProperty("operations").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_InvalidId_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = new User("badid-user", "badid@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var resources = new ProposalResources(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var act = () => resources.GetProposalDetail("not-a-guid");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ProposalResources diff-gate tests (#1415) ────────────────────────────
    // The proposal_detail resource must never serve the stored DiffPreview without the same
    // diff-path gates the HTTP surface runs (#1370/#1376/#1398/#1413). Open proposals route
    // through the live gated diff; decided (terminal) proposals serve the stored preview but
    // still re-check the requester/board-access gate.

    private async Task<Guid> CreateBoardScopedUserAsync(IServiceScope scope, string tag)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = new User($"{tag}-{Guid.NewGuid():N}", $"{tag}-{Guid.NewGuid():N}@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(Guid ProposalId, Guid BoardId)> CreateBoardScopedProposalAsync(IServiceScope scope, Guid userId)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();

        // Owner access is implicit and cannot be revoked without reassigning the board, so the
        // board is owned by a separate user and the requester gets an explicit (revocable)
        // BoardAccess row instead — a revoked-access test then just deletes that row.
        var owner = new User($"owner-{Guid.NewGuid():N}", $"owner-{Guid.NewGuid():N}@example.com", "Password1!");
        await uow.Users.AddAsync(owner);
        await uow.SaveChangesAsync();

        var board = await boardService.CreateBoardAsync(new CreateBoardDto("GateBoard", null), owner.Id);
        board.IsSuccess.Should().BeTrue();

        await uow.BoardAccesses.AddAsync(new BoardAccess(board.Value.Id, userId, UserRole.Editor, owner.Id));
        await uow.SaveChangesAsync();

        // An update-board op clears the shared operation-contract validator (a create-card op
        // would require a columnId), so the resource test exercises the diff-path gates rather
        // than op-shape rejection.
        var parameters = JsonSerializer.Serialize(new { name = "Renamed board", boardId = board.Value.Id });
        var created = await proposalService.CreateProposalAsync(new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: userId,
            Summary: "Gate detail test",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: board.Value.Id,
            Operations: new List<CreateProposalOperationDto>
            {
                new(0, "update", "board", parameters, Guid.NewGuid().ToString(), TargetId: board.Value.Id.ToString())
            }));
        created.IsSuccess.Should().BeTrue();
        return (created.Value.Id, board.Value.Id);
    }

    private async Task MakeTerminalWithStoredPreviewAsync(IServiceScope scope, Guid userId, Guid proposalId, string preview)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var entity = await uow.AutomationProposals.GetByIdAsync(proposalId);
        entity!.SetDiffPreview(preview);
        await uow.AutomationProposals.UpdateAsync(entity);
        await uow.SaveChangesAsync();

        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();
        (await proposalService.ApproveProposalAsync(proposalId, userId)).IsSuccess.Should().BeTrue();
        (await proposalService.MarkAsAppliedAsync(proposalId)).IsSuccess.Should().BeTrue();
    }

    private async Task RevokeBoardAccessAsync(IServiceScope scope, Guid boardId, Guid userId)
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var access = await uow.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        access.Should().NotBeNull();
        await uow.BoardAccesses.DeleteAsync(access!);
        await uow.SaveChangesAsync();
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_NonTerminal_ReturnsLiveGatedDiff()
    {
        using var scope = _serviceProvider.CreateScope();
        var userId = await CreateBoardScopedUserAsync(scope, "gate-live");
        var (proposalId, _) = await CreateBoardScopedProposalAsync(scope, userId);

        var resources = new ProposalResources(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(userId));

        var json = await resources.GetProposalDetail(proposalId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        // An open proposal reports its live, gate-passed diff marked "live".
        root.GetProperty("status").GetString().Should().Be("PendingReview");
        root.GetProperty("diffPreviewSource").GetString().Should().Be("live");
        root.GetProperty("diffPreview").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_Terminal_WithAccess_ReturnsStoredPreviewWithMarker()
    {
        using var scope = _serviceProvider.CreateScope();
        var userId = await CreateBoardScopedUserAsync(scope, "gate-stored");
        var (proposalId, _) = await CreateBoardScopedProposalAsync(scope, userId);
        await MakeTerminalWithStoredPreviewAsync(scope, userId, proposalId, "STORED-HISTORICAL-PREVIEW");

        var resources = new ProposalResources(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(userId));

        var json = await resources.GetProposalDetail(proposalId.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        // A decided proposal serves its stored historical preview, explicitly marked "stored".
        root.GetProperty("status").GetString().Should().Be("Applied");
        root.GetProperty("diffPreviewSource").GetString().Should().Be("stored");
        root.GetProperty("diffPreview").GetString().Should().Be("STORED-HISTORICAL-PREVIEW");
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_Terminal_RevokedBoardAccess_DeniesStoredPreview()
    {
        using var scope = _serviceProvider.CreateScope();
        var userId = await CreateBoardScopedUserAsync(scope, "gate-revoked-term");
        var (proposalId, boardId) = await CreateBoardScopedProposalAsync(scope, userId);
        await MakeTerminalWithStoredPreviewAsync(scope, userId, proposalId, "SECRET-STORED-PREVIEW");
        await RevokeBoardAccessAsync(scope, boardId, userId);

        var resources = new ProposalResources(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(userId));

        // A requester who lost board access is denied — never handed the stored preview.
        var act = () => resources.GetProposalDetail(proposalId.ToString());
        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("does not have access");
        ex.Message.Should().NotContain("SECRET-STORED-PREVIEW");
    }

    [Fact]
    public async Task ProposalResources_GetProposalDetail_NonTerminal_RevokedBoardAccess_Denies()
    {
        using var scope = _serviceProvider.CreateScope();
        var userId = await CreateBoardScopedUserAsync(scope, "gate-revoked-open");
        var (proposalId, boardId) = await CreateBoardScopedProposalAsync(scope, userId);
        await RevokeBoardAccessAsync(scope, boardId, userId);

        var resources = new ProposalResources(
            scope.ServiceProvider.GetRequiredService<IAutomationProposalService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(userId));

        // The live diff path runs the same permission gate: revoked access is denied here too.
        var act = () => resources.GetProposalDetail(proposalId.ToString());
        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("does not have access");
    }

    // ── CaptureResources tests ───────────────────────────────────────────────

    [Fact]
    public async Task CaptureResources_ListCaptures_ReturnsCompactJson()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var captureService = scope.ServiceProvider.GetRequiredService<ICaptureService>();

        var user = new User("cap-user", "cap@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        await captureService.CreateAsync(user.Id, new CreateCaptureItemDto(null, "Capture item 1"));

        var resources = new CaptureResources(
            captureService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await resources.ListCaptures();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("captures", out _).Should().BeTrue();
        root.TryGetProperty("totalCount", out _).Should().BeTrue();
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CaptureResources_GetCaptureDetail_ReturnsFullDetail()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var captureService = scope.ServiceProvider.GetRequiredService<ICaptureService>();

        var user = new User("capdetail-user", "capdetail@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var capture = await captureService.CreateAsync(user.Id, new CreateCaptureItemDto(null, "Detailed capture item"));

        var resources = new CaptureResources(
            captureService,
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var json = await resources.GetCaptureDetail(capture.Value.Id.ToString());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("id").GetGuid().Should().Be(capture.Value.Id);
        root.TryGetProperty("rawText", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CaptureResources_GetCaptureDetail_InvalidId_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = new User("capbad-user", "capbad@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var resources = new CaptureResources(
            scope.ServiceProvider.GetRequiredService<ICaptureService>(),
            new McpBoardResourcesTests.FixedUserContextProvider(user.Id));

        var act = () => resources.GetCaptureDetail("not-a-guid");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
