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
        var proposalService = scope.ServiceProvider.GetRequiredService<IAutomationProposalService>();

        var user = new User("detail-prop-user", "detailprop@example.com", "Password1!");
        await uow.Users.AddAsync(user);
        await uow.SaveChangesAsync();

        var proposal = await proposalService.CreateProposalAsync(new CreateProposalDto(
            SourceType: ProposalSourceType.Manual,
            RequestedByUserId: user.Id,
            Summary: "Detail test",
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString(),
            Operations: new List<CreateProposalOperationDto>
            {
                new(0, "create", "card", "{\"title\":\"test\"}", Guid.NewGuid().ToString()),
                new(1, "move", "card", "{}", Guid.NewGuid().ToString())
            }));

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
