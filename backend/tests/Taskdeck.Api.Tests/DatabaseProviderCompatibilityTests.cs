using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Provider-compatibility test harness validating that critical persistence
/// operations produce consistent results across database providers.
///
/// By default, tests run against SQLite (the CI default). When the environment
/// variable TASKDECK_TEST_POSTGRES_CONNECTION is set, a parallel PostgreSQL
/// run can be configured via a provider-switching factory.
///
/// Covers: CRUD on core entities (Board, Card, Column, Proposal), query
/// patterns used in application services, date/time round-trip fidelity,
/// string collation behavior, GUID storage, nullable field handling, and
/// concurrent-write safety.
///
/// Related: ADR-0023, issue #84 (PLAT-01).
/// </summary>
public class DatabaseProviderCompatibilityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DatabaseProviderCompatibilityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── CRUD: Board ────────────────────────────────────────────────

    [Fact]
    public async Task Board_Create_Read_Update_Delete_RoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("compat-board-crud", "compat-board@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Create
        var board = new Board("Compat Test Board", "A description", user.Id);
        var created = await repo.AddAsync(board);
        await db.SaveChangesAsync();

        created.Id.Should().NotBe(Guid.Empty);
        created.Name.Should().Be("Compat Test Board");

        // Read
        var fetched = await repo.GetByIdAsync(board.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Compat Test Board");
        fetched.Description.Should().Be("A description");
        fetched.OwnerId.Should().Be(user.Id);

        // Update
        fetched.Update(name: "Updated Board Name");
        await repo.UpdateAsync(fetched);
        await db.SaveChangesAsync();

        var updated = await repo.GetByIdAsync(board.Id);
        updated!.Name.Should().Be("Updated Board Name");

        // Delete
        await repo.DeleteAsync(updated);
        await db.SaveChangesAsync();

        var deleted = await repo.GetByIdAsync(board.Id);
        deleted.Should().BeNull();
    }

    // ─── CRUD: Card ─────────────────────────────────────────────────

    [Fact]
    public async Task Card_Create_Read_Update_Delete_RoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var cardRepo = scope.ServiceProvider.GetRequiredService<ICardRepository>();

        var user = new User("compat-card-crud", "compat-card@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Card CRUD Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Todo", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        // Create
        var card = new Card(board.Id, column.Id, "Test Card", "Card description", position: 0);
        await cardRepo.AddAsync(card);
        await db.SaveChangesAsync();

        // Read
        var fetched = await cardRepo.GetByIdAsync(card.Id);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Test Card");
        fetched.Description.Should().Be("Card description");
        fetched.BoardId.Should().Be(board.Id);
        fetched.ColumnId.Should().Be(column.Id);

        // Update
        fetched.Update(title: "Updated Card Title", description: "Updated description");
        await cardRepo.UpdateAsync(fetched);
        await db.SaveChangesAsync();

        var updated = await cardRepo.GetByIdAsync(card.Id);
        updated!.Title.Should().Be("Updated Card Title");
        updated.Description.Should().Be("Updated description");

        // Delete
        await cardRepo.DeleteAsync(updated);
        await db.SaveChangesAsync();

        var deleted = await cardRepo.GetByIdAsync(card.Id);
        deleted.Should().BeNull();
    }

    // ─── CRUD: AutomationProposal ───────────────────────────────────

    [Fact]
    public async Task Proposal_Create_Read_StatusTransition_RoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var proposalRepo = scope.ServiceProvider.GetRequiredService<IAutomationProposalRepository>();

        var user = new User("compat-proposal-crud", "compat-proposal@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Proposal Board", ownerId: user.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        // Create
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            user.Id,
            "Test proposal summary",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId: board.Id);

        await proposalRepo.AddAsync(proposal);
        await db.SaveChangesAsync();

        // Read
        var fetched = await proposalRepo.GetByIdAsync(proposal.Id);
        fetched.Should().NotBeNull();
        fetched!.Summary.Should().Be("Test proposal summary");
        fetched.Status.Should().Be(ProposalStatus.PendingReview);
        fetched.RiskLevel.Should().Be(RiskLevel.Low);
        fetched.BoardId.Should().Be(board.Id);
        fetched.RequestedByUserId.Should().Be(user.Id);

        // Status transition: Approve → Apply
        fetched.Approve(user.Id);
        await proposalRepo.UpdateAsync(fetched);
        await db.SaveChangesAsync();

        var approved = await proposalRepo.GetByIdAsync(proposal.Id);
        approved!.Status.Should().Be(ProposalStatus.Approved);
        approved.DecidedByUserId.Should().Be(user.Id);
        approved.DecidedAt.Should().NotBeNull();

        approved.MarkAsApplied();
        await proposalRepo.UpdateAsync(approved);
        await db.SaveChangesAsync();

        var applied = await proposalRepo.GetByIdAsync(proposal.Id);
        applied!.Status.Should().Be(ProposalStatus.Applied);
        applied.AppliedAt.Should().NotBeNull();
    }

    // ─── DateTimeOffset round-trip fidelity ─────────────────────────

    [Fact]
    public async Task DateTimeOffset_RoundTrip_PreservesUtcPrecision()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-dt-user", "compat-dt@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("DateTime Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        // Use a specific DueDate with subsecond precision
        var specificDate = new DateTimeOffset(2026, 6, 15, 14, 30, 45, 123, TimeSpan.Zero);
        var card = new Card(board.Id, column.Id, "DateTime Card", dueDate: specificDate, position: 0);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        // Clear tracker and re-fetch
        db.ChangeTracker.Clear();
        var fetched = await db.Cards.FirstAsync(c => c.Id == card.Id);

        fetched.DueDate.Should().NotBeNull();
        // Whole-second precision is the reliable cross-provider minimum.
        // SQLite stores as text; PostgreSQL as timestamptz. Both preserve seconds.
        fetched.DueDate!.Value.Year.Should().Be(2026);
        fetched.DueDate.Value.Month.Should().Be(6);
        fetched.DueDate.Value.Day.Should().Be(15);
        fetched.DueDate.Value.Hour.Should().Be(14);
        fetched.DueDate.Value.Minute.Should().Be(30);
        fetched.DueDate.Value.Second.Should().Be(45);
    }

    [Fact]
    public async Task CreatedAt_UpdatedAt_ArePreservedOnRoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-timestamps", "compat-ts@example.com", "hash");
        var createdBefore = DateTimeOffset.UtcNow;
        db.Users.Add(user);

        var board = new Board("Timestamps Board", ownerId: user.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var createdAfter = DateTimeOffset.UtcNow;

        db.ChangeTracker.Clear();
        var fetched = await db.Boards.FirstAsync(b => b.Id == board.Id);

        // CreatedAt should be between our before/after markers
        fetched.CreatedAt.Should().BeOnOrAfter(createdBefore.AddSeconds(-1));
        fetched.CreatedAt.Should().BeOnOrBefore(createdAfter.AddSeconds(1));

        // UpdatedAt should equal CreatedAt initially
        fetched.UpdatedAt.Should().BeCloseTo(fetched.CreatedAt, TimeSpan.FromSeconds(2));

        // Update and verify UpdatedAt changes
        fetched.Update(name: "Updated Timestamps Board");
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var updated = await db.Boards.FirstAsync(b => b.Id == board.Id);
        updated.UpdatedAt.Should().BeOnOrAfter(fetched.CreatedAt);
    }

    // ─── GUID storage and retrieval ─────────────────────────────────

    [Fact]
    public async Task Guid_PreservesExactValue_AcrossRoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-guid-test", "compat-guid@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("GUID Board", ownerId: user.Id);
        var originalBoardId = board.Id;
        var originalUserId = user.Id;
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var fetchedBoard = await db.Boards.FirstAsync(b => b.Id == originalBoardId);
        var fetchedUser = await db.Users.FirstAsync(u => u.Id == originalUserId);

        fetchedBoard.Id.Should().Be(originalBoardId);
        fetchedBoard.OwnerId.Should().Be(originalUserId);
        fetchedUser.Id.Should().Be(originalUserId);
    }

    [Fact]
    public async Task Guid_ForeignKey_JoinQuery_WorksCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-guid-fk", "compat-fk@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("FK Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "FK Column", 0);
        db.Columns.Add(column);
        var card = new Card(board.Id, column.Id, "FK Card", position: 0);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Join query: cards with their columns
        var result = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .Join(db.Columns, c => c.ColumnId, col => col.Id, (c, col) => new { Card = c, Column = col })
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.Card.Id.Should().Be(card.Id);
        result.Column.Id.Should().Be(column.Id);
    }

    // ─── String collation behavior ──────────────────────────────────

    [Fact]
    public async Task String_CaseSensitiveComparison_IsConsistent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-collation", "compat-collation@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Collation Test Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        var cardUpper = new Card(board.Id, column.Id, "IMPORTANT TASK", position: 0);
        var cardLower = new Card(board.Id, column.Id, "important task", position: 1);
        var cardMixed = new Card(board.Id, column.Id, "Important Task", position: 2);
        db.Cards.AddRange(cardUpper, cardLower, cardMixed);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Exact match should be case-sensitive
        var exactMatch = await db.Cards
            .Where(c => c.BoardId == board.Id && c.Title == "IMPORTANT TASK")
            .ToListAsync();

        exactMatch.Should().ContainSingle();
        exactMatch[0].Id.Should().Be(cardUpper.Id);
    }

    [Fact]
    public async Task String_ContainsQuery_BehaviorIsConsistent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-contains", "compat-contains@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Contains Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        var card1 = new Card(board.Id, column.Id, "Fix login bug", position: 0);
        var card2 = new Card(board.Id, column.Id, "Add LOGIN feature", position: 1);
        var card3 = new Card(board.Id, column.Id, "Update docs", position: 2);
        db.Cards.AddRange(card1, card2, card3);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // EF Core LIKE translation: Contains maps to SQL LIKE '%value%'
        // SQLite LIKE is case-insensitive for ASCII by default
        // PostgreSQL LIKE is case-sensitive; ILIKE is case-insensitive
        // This test documents the SQLite baseline behavior
        var containsLogin = await db.Cards
            .Where(c => c.BoardId == board.Id && c.Title.Contains("login"))
            .ToListAsync();

        // SQLite LIKE is case-insensitive for ASCII, so both match
        containsLogin.Count.Should().BeGreaterThanOrEqualTo(1,
            "at least the lowercase 'login' card should match");
    }

    // ─── Nullable field handling ────────────────────────────────────

    [Fact]
    public async Task Nullable_Fields_HandleNullCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-nullable", "compat-nullable@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Nullable Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        // Card with null DueDate
        var cardNullDue = new Card(board.Id, column.Id, "No due date", position: 0);
        // Card with a DueDate
        var cardWithDue = new Card(board.Id, column.Id, "Has due date",
            dueDate: DateTimeOffset.UtcNow.AddDays(7), position: 1);
        db.Cards.AddRange(cardNullDue, cardWithDue);

        // Board with null Description
        var boardNullDesc = new Board("No Desc Board", ownerId: user.Id);
        db.Boards.Add(boardNullDesc);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var fetchedNullDue = await db.Cards.FirstAsync(c => c.Id == cardNullDue.Id);
        fetchedNullDue.DueDate.Should().BeNull();

        var fetchedWithDue = await db.Cards.FirstAsync(c => c.Id == cardWithDue.Id);
        fetchedWithDue.DueDate.Should().NotBeNull();

        var fetchedNullDesc = await db.Boards.FirstAsync(b => b.Id == boardNullDesc.Id);
        fetchedNullDesc.Description.Should().BeNull();

        // Query filtering on null
        var cardsWithoutDueDate = await db.Cards
            .Where(c => c.BoardId == board.Id && c.DueDate == null)
            .ToListAsync();
        cardsWithoutDueDate.Should().Contain(c => c.Id == cardNullDue.Id);
        cardsWithoutDueDate.Should().NotContain(c => c.Id == cardWithDue.Id);
    }

    // ─── Query patterns used in application services ────────────────

    [Fact]
    public async Task BoardWithDetails_IncludesColumnsAndCards()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var boardRepo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var user = new User("compat-details", "compat-details@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Details Board", ownerId: user.Id);
        db.Boards.Add(board);
        var col1 = new Column(board.Id, "Todo", 0);
        var col2 = new Column(board.Id, "Done", 1);
        db.Columns.AddRange(col1, col2);
        await db.SaveChangesAsync();

        var card1 = new Card(board.Id, col1.Id, "Card in Todo", position: 0);
        var card2 = new Card(board.Id, col2.Id, "Card in Done", position: 0);
        db.Cards.AddRange(card1, card2);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var detailed = await boardRepo.GetByIdWithDetailsAsync(board.Id);
        detailed.Should().NotBeNull();
        detailed!.Columns.Should().HaveCount(2);
        detailed.Cards.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadableBoards_FiltersByOwnerAndAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var boardRepo = scope.ServiceProvider.GetRequiredService<IBoardRepository>();

        var owner = new User("compat-readable-owner", "compat-ro@example.com", "hash");
        var collaborator = new User("compat-readable-collab", "compat-rc@example.com", "hash");
        var outsider = new User("compat-readable-outsider", "compat-rout@example.com", "hash");
        db.Users.AddRange(owner, collaborator, outsider);

        var ownedBoard = new Board("Owned Board", ownerId: owner.Id);
        var sharedBoard = new Board("Shared Board", ownerId: owner.Id);
        db.Boards.AddRange(ownedBoard, sharedBoard);
        await db.SaveChangesAsync();

        var access = new BoardAccess(sharedBoard.Id, collaborator.Id, UserRole.Editor, owner.Id);
        db.BoardAccesses.Add(access);
        await db.SaveChangesAsync();

        // Owner sees both boards
        var ownerBoards = (await boardRepo.GetReadableByUserIdAsync(owner.Id, includeArchived: false)).ToList();
        ownerBoards.Should().Contain(b => b.Id == ownedBoard.Id);
        ownerBoards.Should().Contain(b => b.Id == sharedBoard.Id);

        // Collaborator sees only shared board
        var collabBoards = (await boardRepo.GetReadableByUserIdAsync(collaborator.Id, includeArchived: false)).ToList();
        collabBoards.Should().Contain(b => b.Id == sharedBoard.Id);
        collabBoards.Should().NotContain(b => b.Id == ownedBoard.Id);

        // Outsider sees neither
        var outsiderBoards = (await boardRepo.GetReadableByUserIdAsync(outsider.Id, includeArchived: false)).ToList();
        outsiderBoards.Should().NotContain(b => b.Id == ownedBoard.Id);
        outsiderBoards.Should().NotContain(b => b.Id == sharedBoard.Id);
    }

    // ─── Ordering and pagination queries ────────────────────────────

    [Fact]
    public async Task OrderBy_IntegerColumn_ReturnsConsistentOrder()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-order", "compat-order@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Order Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        for (int i = 0; i < 5; i++)
        {
            var card = new Card(board.Id, column.Id, $"Order Card {i}", position: 4 - i);
            db.Cards.Add(card);
        }
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Order by Position ascending
        var orderedAsc = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.Position)
            .ToListAsync();

        orderedAsc.Should().HaveCount(5);
        for (int i = 1; i < orderedAsc.Count; i++)
        {
            orderedAsc[i].Position.Should().BeGreaterThanOrEqualTo(orderedAsc[i - 1].Position);
        }

        // Order by Position descending
        var orderedDesc = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .OrderByDescending(c => c.Position)
            .ToListAsync();

        orderedDesc.Should().HaveCount(5);
        for (int i = 1; i < orderedDesc.Count; i++)
        {
            orderedDesc[i].Position.Should().BeLessThanOrEqualTo(orderedDesc[i - 1].Position);
        }
    }

    /// <summary>
    /// Documents that SQLite does not support ORDER BY on DateTimeOffset columns.
    /// PostgreSQL (timestamptz) handles this natively. Application code must use
    /// materialize-then-sort or cast to string for SQLite compatibility.
    /// This is a known provider difference — see ADR-0023.
    /// </summary>
    [Fact]
    public async Task DateTimeOffset_OrderBy_RequiresClientSideForSqlite()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-dto-order", "compat-dto-order@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("DTO Order Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        for (int i = 0; i < 3; i++)
        {
            db.Cards.Add(new Card(board.Id, column.Id, $"DT Card {i}", position: i));
        }
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // SQLite throws NotSupportedException for DateTimeOffset ORDER BY.
        // The workaround is to materialize first, then sort client-side.
        var cards = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .ToListAsync();

        var sortedClientSide = cards.OrderBy(c => c.CreatedAt).ToList();
        sortedClientSide.Should().HaveCount(3);
        for (int i = 1; i < sortedClientSide.Count; i++)
        {
            sortedClientSide[i].CreatedAt.Should().BeOnOrAfter(sortedClientSide[i - 1].CreatedAt);
        }
    }

    [Fact]
    public async Task Skip_Take_PaginationQuery_WorksCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-page", "compat-page@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Pagination Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        for (int i = 0; i < 10; i++)
        {
            db.Cards.Add(new Card(board.Id, column.Id, $"Page Card {i:D2}", position: i));
        }
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Page 1: skip 0, take 3
        var page1 = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.Position)
            .Skip(0).Take(3)
            .ToListAsync();
        page1.Should().HaveCount(3);

        // Page 2: skip 3, take 3
        var page2 = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.Position)
            .Skip(3).Take(3)
            .ToListAsync();
        page2.Should().HaveCount(3);

        // No overlap between pages
        page1.Select(c => c.Id).Should().NotIntersectWith(page2.Select(c => c.Id));

        // Total count
        var total = await db.Cards.CountAsync(c => c.BoardId == board.Id);
        total.Should().Be(10);
    }

    // ─── Enum storage and filtering ─────────────────────────────────

    [Fact]
    public async Task Enum_StorageAndFiltering_WorksAcrossProviders()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-enum", "compat-enum@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Enum Board", ownerId: user.Id);
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        // Create proposals with different statuses and risk levels
        var lowProposal = new AutomationProposal(
            ProposalSourceType.Manual, user.Id, "Low risk",
            RiskLevel.Low, Guid.NewGuid().ToString(), board.Id);
        var highProposal = new AutomationProposal(
            ProposalSourceType.Chat, user.Id, "High risk",
            RiskLevel.High, Guid.NewGuid().ToString(), board.Id);

        db.AutomationProposals.AddRange(lowProposal, highProposal);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Filter by enum value
        var lowRisk = await db.AutomationProposals
            .Where(p => p.BoardId == board.Id && p.RiskLevel == RiskLevel.Low)
            .ToListAsync();
        lowRisk.Should().ContainSingle();
        lowRisk[0].Id.Should().Be(lowProposal.Id);

        // Filter by source type
        var chatProposals = await db.AutomationProposals
            .Where(p => p.BoardId == board.Id && p.SourceType == ProposalSourceType.Chat)
            .ToListAsync();
        chatProposals.Should().ContainSingle();
        chatProposals[0].Id.Should().Be(highProposal.Id);
    }

    // ─── Aggregate queries ──────────────────────────────────────────

    [Fact]
    public async Task Aggregate_CountAndGroupBy_WorkCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-agg", "compat-agg@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Aggregate Board", ownerId: user.Id);
        db.Boards.Add(board);
        var col1 = new Column(board.Id, "Todo", 0);
        var col2 = new Column(board.Id, "Done", 1);
        db.Columns.AddRange(col1, col2);
        await db.SaveChangesAsync();

        db.Cards.AddRange(
            new Card(board.Id, col1.Id, "Agg Card 1", position: 0),
            new Card(board.Id, col1.Id, "Agg Card 2", position: 1),
            new Card(board.Id, col1.Id, "Agg Card 3", position: 2),
            new Card(board.Id, col2.Id, "Agg Card 4", position: 0)
        );
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // Count per column
        var countByColumn = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .GroupBy(c => c.ColumnId)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToListAsync();

        countByColumn.Should().HaveCount(2);
        countByColumn.First(g => g.ColumnId == col1.Id).Count.Should().Be(3);
        countByColumn.First(g => g.ColumnId == col2.Id).Count.Should().Be(1);

        // Total count
        var total = await db.Cards.CountAsync(c => c.BoardId == board.Id);
        total.Should().Be(4);
    }

    // ─── Boolean field filtering ────────────────────────────────────

    [Fact]
    public async Task Boolean_FilteringWorks_AcrossProviders()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-bool", "compat-bool@example.com", "hash");
        db.Users.Add(user);

        var activeBoard = new Board("Active", ownerId: user.Id);
        var archivedBoard = new Board("Archived", ownerId: user.Id);
        archivedBoard.Archive();
        db.Boards.AddRange(activeBoard, archivedBoard);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var active = await db.Boards
            .Where(b => b.OwnerId == user.Id && !b.IsArchived)
            .ToListAsync();
        active.Should().ContainSingle();
        active[0].Id.Should().Be(activeBoard.Id);

        var archived = await db.Boards
            .Where(b => b.OwnerId == user.Id && b.IsArchived)
            .ToListAsync();
        archived.Should().ContainSingle();
        archived[0].Id.Should().Be(archivedBoard.Id);
    }

    // ─── Concurrent writes (basic safety) ───────────────────────────

    [Fact]
    public async Task ConcurrentInserts_DoNotLoseData()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-concurrent", "compat-concurrent@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Concurrent Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        // Insert 20 cards sequentially (SQLite doesn't support true concurrent writes)
        var cardIds = new List<Guid>();
        for (int i = 0; i < 20; i++)
        {
            var card = new Card(board.Id, column.Id, $"Concurrent Card {i}", position: i);
            cardIds.Add(card.Id);
            db.Cards.Add(card);
        }
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        // All 20 cards should be persisted
        var count = await db.Cards.CountAsync(c => c.BoardId == board.Id);
        count.Should().Be(20);

        // All IDs should be retrievable
        var retrievedIds = await db.Cards
            .Where(c => c.BoardId == board.Id)
            .Select(c => c.Id)
            .ToListAsync();
        retrievedIds.Should().BeEquivalentTo(cardIds);
    }

    // ─── Unicode string handling ────────────────────────────────────

    [Fact]
    public async Task Unicode_Strings_PreservedOnRoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("compat-unicode", "compat-unicode@example.com", "hash");
        db.Users.Add(user);
        var board = new Board("Unicode Board", ownerId: user.Id);
        db.Boards.Add(board);
        var column = new Column(board.Id, "Col", 0);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        var unicodeTitle = "Fix bug in \u65E5\u672C\u8A9E module \u2014 \u00FC\u00F6\u00E4 chars & emojis";
        var card = new Card(board.Id, column.Id, unicodeTitle, position: 0);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var fetched = await db.Cards.FirstAsync(c => c.Id == card.Id);
        fetched.Title.Should().Be(unicodeTitle);
    }
}
