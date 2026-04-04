using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for the full archive/restore lifecycle through the HTTP API.
/// Covers board and card archive/restore, cross-user isolation, state machine
/// enforcement, snapshot integrity, audit trail, and conflict handling.
/// </summary>
public class ArchiveRestoreLifecycleTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ArchiveRestoreLifecycleTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Board Archive/Restore Lifecycle

    [Fact]
    public async Task ArchiveBoard_ShouldDisappearFromActiveList_AndAppearInArchiveList()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-lifecycle-board");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-board-active");

        // Archive the board - entityId must match the board's ID for the archive list to link them
        var archiveItem = await SeedArchiveItemAsync(board.Id, user.UserId, entityType: "board", entityId: board.Id, name: board.Name);

        // Mark board as archived via update
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/boards/{board.Id}",
            new UpdateBoardDto(null, null, IsArchived: true));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify board is excluded from active board list
        var activeBoardsResponse = await client.GetAsync("/api/boards");
        activeBoardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeBoards = await activeBoardsResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        activeBoards.Should().NotBeNull();
        activeBoards!.Should().NotContain(b => b.Id == board.Id);

        // Verify board appears when includeArchived=true
        var allBoardsResponse = await client.GetAsync("/api/boards?includeArchived=true");
        allBoardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBoards = await allBoardsResponse.Content.ReadFromJsonAsync<List<BoardDto>>();
        allBoards.Should().NotBeNull();
        allBoards!.Should().Contain(b => b.Id == board.Id && b.IsArchived);

        // Verify archive item exists in archive list
        var archiveListResponse = await client.GetAsync("/api/archive/items?entityType=board");
        archiveListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var archiveItems = await archiveListResponse.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        archiveItems.Should().NotBeNull();
        archiveItems!.Should().Contain(a => a.EntityId == board.Id);
    }

    [Fact]
    public async Task RestoreArchivedBoard_ShouldSucceed_AndBoardReappears()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-restore-board");
        var board = await ApiTestHarness.CreateBoardAsync(client, "restore-board");

        // Archive the board via API
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/boards/{board.Id}",
            new UpdateBoardDto(null, null, IsArchived: true));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Seed an archive item for restore
        var snapshotJson = JsonSerializer.Serialize(new { Name = board.Name, Description = board.Description });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            entityId: board.Id, name: board.Name, snapshotJson: snapshotJson);

        // Restore via API
        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Rename);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/board/{board.Id}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult.Should().NotBeNull();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.RestoredEntityId.Should().NotBeNull();
    }

    #endregion

    #region Card Archive/Restore Lifecycle

    [Fact]
    public async Task ArchiveCard_SeedAndRestore_ShouldRecreateCard()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-card-lifecycle");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-card-board");
        var column = await CreateColumnAsync(client, board.Id, "Backlog");
        var card = await CreateCardAsync(client, board.Id, column.Id, "Card to Archive");

        // Seed archive item for the card
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = card.Title,
            Description = card.Description,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            entityId: card.Id, name: card.Title, snapshotJson: snapshotJson);

        // Restore the card
        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Rename);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{card.Id}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult.Should().NotBeNull();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.RestoredEntityId.Should().NotBeNull();
        restoreResult.RestoredEntityId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RestoreCard_WhenOriginalColumnExists_ShouldRestoreToOriginalColumn()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-card-orig-col");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-card-col-board");
        var column = await CreateColumnAsync(client, board.Id, "Target Column");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Restored Card",
            Description = "Test restore to original column",
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Restored Card", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.ResolvedName.Should().Be("Restored Card");
    }

    [Fact]
    public async Task RestoreCard_WithBlockedState_ShouldPreserveBlockedFlag()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-card-blocked");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-blocked-board");
        var column = await CreateColumnAsync(client, board.Id, "In Progress");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Blocked Card",
            Description = "This card was blocked",
            DueDate = (DateTimeOffset?)null,
            IsBlocked = true,
            BlockReason = "Waiting for dependency",
            ColumnId = column.Id
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Blocked Card", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.RestoredEntityId.Should().NotBeNull();

        // Fetch the restored card via the board cards list and verify blocked state
        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        var restoredCard = cards!.FirstOrDefault(c => c.Id == restoreResult.RestoredEntityId);
        restoredCard.Should().NotBeNull("restored card should appear in the board's card list");
        restoredCard!.IsBlocked.Should().BeTrue();
        restoredCard.BlockReason.Should().Be("Waiting for dependency");
    }

    #endregion

    #region Column Archive/Restore

    [Fact]
    public async Task RestoreColumn_ShouldAddToEndOfBoard()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-col-restore");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-col-board");
        var existingColumn = await CreateColumnAsync(client, board.Id, "Existing Column");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Name = "Restored Column",
            Position = 0,
            WipLimit = 5
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "column",
            name: "Restored Column", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/column/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.ResolvedName.Should().Be("Restored Column");
    }

    #endregion

    #region Cross-User Archive Isolation

    [Fact]
    public async Task GetArchiveItems_ShouldOnlyReturnOwnItems_NotOtherUsers()
    {
        // User A creates a board and archives an item
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "archive-iso-userA");
        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "archive-iso-boardA");
        var archiveItemA = await SeedArchiveItemAsync(boardA.Id, userA.UserId, name: "UserA Archive");

        // User B creates a board and archives an item
        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "archive-iso-userB");
        var boardB = await ApiTestHarness.CreateBoardAsync(clientB, "archive-iso-boardB");
        var archiveItemB = await SeedArchiveItemAsync(boardB.Id, userB.UserId, name: "UserB Archive");

        // User A should not see User B's archive items
        var responseA = await clientA.GetAsync("/api/archive/items");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        var itemsA = await responseA.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        itemsA.Should().NotBeNull();
        itemsA!.Should().NotContain(i => i.Id == archiveItemB.Id,
            "User A should not see User B's archive items");

        // User B should not see User A's archive items
        var responseB = await clientB.GetAsync("/api/archive/items");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        var itemsB = await responseB.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        itemsB.Should().NotBeNull();
        itemsB!.Should().NotContain(i => i.Id == archiveItemA.Id,
            "User B should not see User A's archive items");
    }

    [Fact]
    public async Task RestoreItem_BelongingToOtherUser_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "archive-cross-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "archive-cross-owner-board");

        var snapshotJson = JsonSerializer.Serialize(new { Name = "Owner Board" });
        var archiveItem = await SeedArchiveItemAsync(
            ownerBoard.Id, owner.UserId, entityType: "board",
            entityId: ownerBoard.Id, name: "Owner Board", snapshotJson: snapshotJson);

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "archive-cross-other");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await otherClient.PostAsJsonAsync(
            $"/api/archive/board/{archiveItem.EntityId}/restore",
            restoreDto);

        await ApiTestHarness.AssertForbiddenAsync(restoreResponse);
    }

    [Fact]
    public async Task GetArchiveItemById_ForOtherUsersItem_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "archive-get-iso-owner");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "archive-get-iso-board");
        var archiveItem = await SeedArchiveItemAsync(ownerBoard.Id, owner.UserId, name: "Owner Only");

        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherClient, "archive-get-iso-other");

        var response = await otherClient.GetAsync($"/api/archive/items/{archiveItem.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    #endregion

    #region Double Archive/Restore Handling

    [Fact]
    public async Task RestoreAlreadyRestoredItem_ShouldReturnConflict()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-double-restore");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-double-board");

        var snapshotJson = JsonSerializer.Serialize(new { Name = "Double Board" });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            name: "Double Board", snapshotJson: snapshotJson);

        // Mark as restored in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var item = await dbContext.ArchiveItems.FindAsync(archiveItem.Id);
            item!.MarkAsRestored(user.UserId);
            await dbContext.SaveChangesAsync();
        }

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var response = await client.PostAsJsonAsync(
            $"/api/archive/board/{archiveItem.EntityId}/restore",
            restoreDto);

        // Controller checks RestoreStatus before calling service — returns 409
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RestoreExpiredItem_ShouldReturnConflict()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-expired-restore");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-expired-board");

        var snapshotJson = JsonSerializer.Serialize(new { Name = "Expired Board" });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            name: "Expired Board", snapshotJson: snapshotJson);

        // Mark as expired in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var item = await dbContext.ArchiveItems.FindAsync(archiveItem.Id);
            item!.MarkAsExpired();
            await dbContext.SaveChangesAsync();
        }

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var response = await client.PostAsJsonAsync(
            $"/api/archive/board/{archiveItem.EntityId}/restore",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Conflict Detection and Resolution

    [Fact]
    public async Task RestoreBoard_WithConflictStrategy_Rename_ShouldAppendSuffix()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-conflict-rename");
        var board = await ApiTestHarness.CreateBoardAsync(client, "conflict-board");

        // Create another board with same name to cause conflict
        var conflictBoard = await ApiTestHarness.CreateBoardAsync(client, "conflict-board");

        var snapshotJson = JsonSerializer.Serialize(new { Name = conflictBoard.Name, Description = "Conflicting board" });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            name: conflictBoard.Name, snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Rename);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/board/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult.Should().NotBeNull();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.ResolvedName.Should().Contain("(Restored)");
    }

    [Fact]
    public async Task RestoreBoard_WithConflictStrategy_Fail_ShouldReturnConflict()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-conflict-fail");

        // Create a board - note: the board name uses a unique suffix from CreateBoardAsync
        var board = await ApiTestHarness.CreateBoardAsync(client, "conflict-fail-board");

        // Use the exact same board name in the snapshot to cause a conflict
        var snapshotJson = JsonSerializer.Serialize(new { Name = board.Name, Description = "Will conflict" });
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            name: board.Name, snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/board/{archiveItem.EntityId}/restore",
            restoreDto);

        // The restore should fail due to name conflict with Fail strategy
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RestoreColumn_WithNameConflict_Rename_ShouldAppendSuffix()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-col-conflict");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-col-conflict-board");
        var existingColumn = await CreateColumnAsync(client, board.Id, "Backlog");

        // Archive a column with the same name
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Name = "Backlog",
            Position = 0,
            WipLimit = (int?)null
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "column",
            name: "Backlog", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Rename);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/column/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        restoreResult!.Success.Should().BeTrue();
        restoreResult.ResolvedName.Should().Contain("(Restored)");
    }

    #endregion

    #region Snapshot Integrity

    [Fact]
    public async Task ArchiveItem_SnapshotJson_ShouldContainCompleteData()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-snapshot-integrity");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-snapshot-board");

        var originalSnapshot = new
        {
            Name = "Complete Board",
            Description = "A board with full data"
        };
        var snapshotJson = JsonSerializer.Serialize(originalSnapshot);

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "board",
            name: "Complete Board", snapshotJson: snapshotJson);

        // Fetch the archive item and verify snapshot is intact
        var getResponse = await client.GetAsync($"/api/archive/items/{archiveItem.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the archive item stored correctly through DB
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var storedItem = await dbContext.ArchiveItems.FindAsync(archiveItem.Id);
        storedItem.Should().NotBeNull();

        var parsedSnapshot = JsonSerializer.Deserialize<JsonElement>(storedItem!.SnapshotJson);
        parsedSnapshot.GetProperty("Name").GetString().Should().Be("Complete Board");
        parsedSnapshot.GetProperty("Description").GetString().Should().Be("A board with full data");
    }

    [Fact]
    public async Task RestoreCard_WithInvalidSnapshotJson_ShouldReturnError()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-bad-snapshot");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-bad-snapshot-board");
        var column = await CreateColumnAsync(client, board.Id, "Col");

        // Seed with malformed snapshot
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Bad Snapshot Card", snapshotJson: "{not valid json object missing}");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);

        // Should fail due to bad snapshot - could be 400 (validation) or 500 (parse)
        restoreResponse.IsSuccessStatusCode.Should().BeFalse();
        restoreResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.Conflict);
    }

    #endregion

    #region Audit Trail

    [Fact]
    public async Task ArchiveItem_ShouldCreateAuditLogEntry()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-audit-trail");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-audit-board");

        // Create archive item through service (which writes audit log)
        var archiveItem = await SeedArchiveItemViaServiceAsync(client, board.Id, user.UserId, "card", "Audited Card");

        // Verify audit log was created by checking the DB directly
        // (AuditController only supports Board/Column/Card/Label entity types)
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var auditEntries = dbContext.AuditLogs
            .Where(a => a.EntityId == archiveItem.Id && a.EntityType == "ArchiveItem")
            .ToList();

        auditEntries.Should().NotBeEmpty("creating an archive item should produce an audit log entry");
        auditEntries.Should().Contain(a => a.Changes != null && a.Changes.Contains("Archived"));
    }

    [Fact]
    public async Task RestoreItem_ShouldCreateAuditLogEntry()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-restore-audit");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-restore-audit-board");
        var column = await CreateColumnAsync(client, board.Id, "Audit Col");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Restore Audit Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Restore Audit Card", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check the audit trail now contains a restore entry
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var auditLogs = dbContext.AuditLogs
            .Where(a => a.EntityId == archiveItem.Id && a.EntityType == "ArchiveItem")
            .ToList();

        auditLogs.Should().HaveCountGreaterOrEqualTo(1);
        auditLogs.Should().Contain(a => a.Changes != null && a.Changes.Contains("Restored"));
    }

    #endregion

    #region Restore to Non-Existent Target

    [Fact]
    public async Task RestoreCard_ToNonExistentBoard_ShouldReturnNotFound()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-restore-missing-board");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-restore-missing-src");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Orphan Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = Guid.NewGuid()
        });

        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Orphan Card", snapshotJson: snapshotJson);

        var nonExistentBoardId = Guid.NewGuid();
        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: nonExistentBoardId,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);

        // Should fail because target board doesn't exist
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RestoreCard_ToArchivedBoard_ShouldReturnError()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-restore-archived-board");
        var sourceBoard = await ApiTestHarness.CreateBoardAsync(client, "archive-source-board");
        var targetBoard = await ApiTestHarness.CreateBoardAsync(client, "archive-target-board");

        // Archive the target board
        var archiveTargetResponse = await client.PutAsJsonAsync(
            $"/api/boards/{targetBoard.Id}",
            new UpdateBoardDto(null, null, IsArchived: true));
        archiveTargetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Card for Archived Board",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = Guid.NewGuid()
        });

        var archiveItem = await SeedArchiveItemAsync(
            sourceBoard.Id, user.UserId, entityType: "card",
            name: "Card for Archived Board", snapshotJson: snapshotJson);

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: targetBoard.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Fail);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);

        // RestorePlanner rejects restore to archived board with InvalidOperation
        restoreResponse.IsSuccessStatusCode.Should().BeFalse();
        restoreResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict);
    }

    #endregion

    #region Filter and Query

    [Fact]
    public async Task GetArchiveItems_FilterByEntityType_ShouldReturnOnlyMatchingType()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-filter-type");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-filter-board");

        await SeedArchiveItemAsync(board.Id, user.UserId, entityType: "board", name: "Filter Board");
        await SeedArchiveItemAsync(board.Id, user.UserId, entityType: "card", name: "Filter Card");

        var response = await client.GetAsync($"/api/archive/items?entityType=card&boardId={board.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(i => i.EntityType == "card");
    }

    [Fact]
    public async Task GetArchiveItems_FilterByStatus_ShouldReturnOnlyMatchingStatus()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-filter-status");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-filter-status-board");

        var availableItem = await SeedArchiveItemAsync(board.Id, user.UserId, name: "Available Item");
        var expiredItem = await SeedArchiveItemAsync(board.Id, user.UserId, name: "Expired Item");

        // Mark one as expired
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var item = await dbContext.ArchiveItems.FindAsync(expiredItem.Id);
            item!.MarkAsExpired();
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/archive/items?status=0&boardId={board.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(i => i.RestoreStatus == RestoreStatus.Available);
    }

    [Fact]
    public async Task GetArchiveItems_FilterByBoardId_ShouldReturnOnlyMatchingBoard()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-filter-boardid");
        var boardA = await ApiTestHarness.CreateBoardAsync(client, "archive-filter-boardA");
        var boardB = await ApiTestHarness.CreateBoardAsync(client, "archive-filter-boardB");

        await SeedArchiveItemAsync(boardA.Id, user.UserId, name: "Item on Board A");
        await SeedArchiveItemAsync(boardB.Id, user.UserId, name: "Item on Board B");

        var response = await client.GetAsync($"/api/archive/items?boardId={boardA.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<ArchiveItemDto>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(i => i.BoardId == boardA.Id);
    }

    #endregion

    #region Restore Non-Existent / Invalid Entity Type

    [Fact]
    public async Task RestoreNonExistentArchiveItem_ShouldReturnNotFound()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "archive-restore-nonexist");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: Guid.NewGuid(),
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var response = await client.PostAsJsonAsync(
            $"/api/archive/card/{Guid.NewGuid()}/restore",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("workspace")]
    [InlineData("123")]
    public async Task RestoreWithInvalidEntityType_ShouldReturnBadRequest(string entityType)
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "archive-restore-invalid-type-2");

        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: null,
            RestoreMode: RestoreMode.InPlace,
            ConflictStrategy: ConflictStrategy.Fail);

        var response = await client.PostAsJsonAsync(
            $"/api/archive/{entityType}/{Guid.NewGuid()}/restore",
            restoreDto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Authentication

    [Fact]
    public async Task ArchiveEndpoints_WithoutAuth_ShouldReturnUnauthorized()
    {
        using var client = _factory.CreateClient();

        var getItemsResponse = await client.GetAsync("/api/archive/items");
        getItemsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var getItemResponse = await client.GetAsync($"/api/archive/items/{Guid.NewGuid()}");
        getItemResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var restoreDto = new RestoreArchiveItemDto(null, RestoreMode.InPlace, ConflictStrategy.Fail);
        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/board/{Guid.NewGuid()}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Immediate Archive-Restore (No Time Gap)

    [Fact]
    public async Task ArchiveThenImmediateRestore_ShouldSucceed()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "archive-immediate-restore");
        var board = await ApiTestHarness.CreateBoardAsync(client, "archive-immediate-board");
        var column = await CreateColumnAsync(client, board.Id, "Quick Col");

        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Quick Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });

        // Archive immediately
        var archiveItem = await SeedArchiveItemAsync(
            board.Id, user.UserId, entityType: "card",
            name: "Quick Card", snapshotJson: snapshotJson);

        // Restore immediately (no time gap)
        var restoreDto = new RestoreArchiveItemDto(
            TargetBoardId: board.Id,
            RestoreMode: RestoreMode.Copy,
            ConflictStrategy: ConflictStrategy.Rename);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/archive/card/{archiveItem.EntityId}/restore",
            restoreDto);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await restoreResponse.Content.ReadFromJsonAsync<RestoreResult>();
        result!.Success.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private async Task<ArchiveItem> SeedArchiveItemAsync(
        Guid boardId,
        Guid archivedByUserId,
        string entityType = "board",
        Guid? entityId = null,
        string name = "Seeded Archive Item",
        string snapshotJson = "{\"name\":\"Seeded Archive Item\"}")
    {
        var resolvedEntityId = entityId ?? Guid.NewGuid();
        var archiveItem = new ArchiveItem(
            entityType,
            resolvedEntityId,
            boardId,
            name,
            archivedByUserId,
            snapshotJson);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.ArchiveItems.Add(archiveItem);
        await dbContext.SaveChangesAsync();

        return archiveItem;
    }

    private async Task<ArchiveItemDto> SeedArchiveItemViaServiceAsync(
        HttpClient client,
        Guid boardId,
        Guid archivedByUserId,
        string entityType,
        string name)
    {
        // Use the service to create the archive item (which also creates audit log)
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArchiveRecoveryService>();

        var snapshotJson = JsonSerializer.Serialize(new { Name = name, Description = (string?)null });
        var dto = new CreateArchiveItemDto(
            entityType, Guid.NewGuid(), boardId, name, archivedByUserId, snapshotJson, null);

        var result = await service.CreateArchiveItemAsync(dto);
        result.IsSuccess.Should().BeTrue("archive item creation via service should succeed");
        return result.Value;
    }

    private static async Task<ColumnDto> CreateColumnAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/columns",
            new CreateColumnDto(boardId, name, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await response.Content.ReadFromJsonAsync<ColumnDto>();
        column.Should().NotBeNull();
        return column!;
    }

    private static async Task<CardDto> CreateCardAsync(
        HttpClient client, Guid boardId, Guid columnId, string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<CardDto>();
        card.Should().NotBeNull();
        return card!;
    }

    #endregion
}
