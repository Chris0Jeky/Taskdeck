using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AuditApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public AuditApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuditEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/audit/boards/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/audit/entities/Card/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/audit/users/me"));
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnOk_ForAccessibleBoard()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        await AuthenticateAsAsync("audit-board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "audit-private-board", "Audit security test");

        await AuthenticateAsAsync("audit-board-outsider");
        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnBadRequest_WhenLimitIsInvalid()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task GetBoardHistory_ShouldIncludeCardAndColumnActivity()
    {
        var user = await AuthenticateAsAsync("audit-board-scope");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "audit-board-scope-board", "Board-scoped history test");
        var column = await CreateColumnAsync(board.Id, "Board Scope Column");
        var card = await CreateCardAsync(board.Id, column.Id, "Board Scope Card");

        var boardLog = await SeedAuditLogAsync("Board", board.Id, user.UserId);
        var cardLog = await SeedAuditLogAsync("Card", card.Id, user.UserId);
        var columnLog = await SeedAuditLogAsync("Column", column.Id, user.UserId);

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<List<AuditLogDto>>();
        logs.Should().NotBeNull();
        logs!.Should().Contain(log => log.Id == boardLog.Id, "board history should include board-level audit entries");
        logs.Should().Contain(log => log.Id == cardLog.Id, "board history should include card-level audit entries");
        logs.Should().Contain(log => log.Id == columnLog.Id, "board history should include column-level audit entries");
    }

    [Fact]
    public async Task GetEntityHistory_ShouldReturnOk_ForAccessibleEntity()
    {
        var board = await CreateBoardAsync();
        var column = await CreateColumnAsync(board.Id, "Audit Entity Column");
        var card = await CreateCardAsync(board.Id, column.Id, "Audit Entity Card");

        var response = await _client.GetAsync($"/api/audit/entities/Card/{card.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEntityHistory_ShouldReturnForbidden_WhenEntityBelongsToDifferentBoard()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "audit-entity-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "audit-entity-outsider");

        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "audit-entity-board", "Audit entity security test");
        var createColumnResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Audit Entity Column", null, null));
        createColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await createColumnResponse.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();

        var createCardResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column!.Id, "Audit Entity Card", null, null, null));
        createCardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await createCardResponse.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();

        var response = await outsiderClient.GetAsync($"/api/audit/entities/Card/{card!.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetEntityHistory_ShouldReturnNotFound_WhenEntityDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/audit/entities/Card/{Guid.NewGuid()}");
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task GetEntityHistory_ShouldReturnLogs_WhenStoredEntityTypeUsesDifferentCasing()
    {
        var user = await AuthenticateAsAsync("audit-entity-casing");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "audit-casing-board", "Audit casing test board");
        var column = await CreateColumnAsync(board.Id, "Audit Casing Column");
        var card = await CreateCardAsync(board.Id, column.Id, "Audit Casing Card");
        var seededLog = await SeedAuditLogAsync("card", card.Id, user.UserId);

        var response = await _client.GetAsync($"/api/audit/entities/Card/{card.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await response.Content.ReadFromJsonAsync<List<AuditLogDto>>();
        logs.Should().NotBeNull();
        logs!.Should().Contain(log => log.Id == seededLog.Id && log.EntityType == "card");
    }

    [Fact]
    public async Task GetUserHistory_ShouldReturnOk_ForCurrentUser()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync("/api/audit/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "audit-board", "Audit integration tests");
    }

    private async Task<TestUserContext> AuthenticateAsAsync(string stem)
    {
        var context = await ApiTestHarness.AuthenticateAsync(_client, stem);
        _isAuthenticated = true;
        return context;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await AuthenticateAsAsync("audit-suite");
    }

    private async Task<ColumnDto> CreateColumnAsync(Guid boardId, string name)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await response.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        return column!;
    }

    private async Task<CardDto> CreateCardAsync(Guid boardId, Guid columnId, string title)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        return card!;
    }

    private async Task<AuditLog> SeedAuditLogAsync(string entityType, Guid entityId, Guid? userId = null)
    {
        var auditLog = new AuditLog(entityType, entityId, AuditAction.Updated, userId, "seeded audit log");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync();

        return auditLog;
    }
}
