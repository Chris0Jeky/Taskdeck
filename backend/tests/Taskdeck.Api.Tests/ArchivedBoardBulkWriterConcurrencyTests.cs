using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Archive-race regressions for the three bulk card writers — external import, starter-pack apply,
/// and archive-item restore (<c>#2114</c>). They extend the per-card guard proven in
/// <see cref="ArchivedBoardCardWriteConcurrencyTests"/> (<c>#2110</c>) to the writers that check
/// <see cref="Board.IsArchived"/> once and then build a whole batch before saving.
/// </summary>
/// <remarks>
/// <para>
/// The race is forced deterministically rather than by timing: each test runs the archive to
/// completion on a second connection from inside a callback the writer itself invokes *after* its
/// archived-state check and *before* its <c>SaveChanges</c>. That is the exact window the check
/// cannot cover, so these tests fail on any writer that only reads <c>IsArchived</c>.
/// </para>
/// <para>
/// The mocked <c>SaveChangesAsync</c> reproduces the one translation the real
/// <c>UnitOfWork.SaveChangesAsync</c> performs — <see cref="DbUpdateConcurrencyException"/> becomes
/// <c>DomainException(ErrorCodes.Conflict)</c>. Everything else runs against real EF Core, real
/// migrations, and a real SQLite file, so the conditional <c>UPDATE Boards ... WHERE
/// ConcurrencyToken = ?</c> is genuinely issued and genuinely matches zero rows.
/// </para>
/// <para>
/// Each writer also gets an independence regression. The guard deliberately does not *advance* the
/// token, so proving rejection is only half the contract: two writers that read the same board must
/// still both succeed, or the fix would have quietly serialized every bulk write on a board.
/// </para>
/// </remarks>
public sealed class ArchivedBoardBulkWriterConcurrencyTests
{
    private const string TargetColumnName = "To Do";

    // ---------------------------------------------------------------- external import

    [Fact]
    public async Task ImportToBoardAsync_WhenArchiveCommitsAfterTheBoardRead_RejectsTheImport()
    {
        var dbPath = NewDatabasePath("bulk-import-race");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, _) = await SeedBoardAsync(options);

            await using var writeDb = new TaskdeckDbContext(options);
            var board = await LoadBoardWithDetailsAsync(writeDb, boardId);

            var unitOfWork = CreateUnitOfWork(writeDb);
            StubBoardReads(unitOfWork, board);
            StubCardAdds(unitOfWork, writeDb);

            // The adapter parses after ImportToBoardAsync has already read the board and cleared its
            // archived check, and long before the import's own transaction opens. Archiving here is
            // therefore the precise check-then-act window.
            var adapter = new ArchiveInjectingAdapter(
                SingleCandidate("Imported card", "import-dedupe-1"),
                onParse: () => ArchiveOnAnotherConnection(options, boardId));

            var service = new ExternalImportService(unitOfWork.Object, [adapter]);

            var result = await service.ImportToBoardAsync(boardId, BuildImportRequest());

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(b => b.Id == boardId)).IsArchived.Should().BeTrue();
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0,
                "the imported cards and the stale board update share one SaveChanges transaction");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task ImportToBoardAsync_WhenTwoImportsReadTheSameBoard_BothPersist()
    {
        var dbPath = NewDatabasePath("bulk-import-independent");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, _) = await SeedBoardAsync(options);

            // Both writers read the board before either commits, so both hold the same token. The
            // guard must not turn that into a conflict: nothing here archives the board.
            await using var firstDb = new TaskdeckDbContext(options);
            await using var secondDb = new TaskdeckDbContext(options);
            var firstBoard = await LoadBoardWithDetailsAsync(firstDb, boardId);
            var secondBoard = await LoadBoardWithDetailsAsync(secondDb, boardId);

            var firstService = CreateImportService(firstDb, firstBoard, "First import", "import-dedupe-a");
            var secondService = CreateImportService(secondDb, secondBoard, "Second import", "import-dedupe-b");

            var firstResult = await firstService.ImportToBoardAsync(boardId, BuildImportRequest());
            var secondResult = await secondService.ImportToBoardAsync(boardId, BuildImportRequest());

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(2);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    // ------------------------------------------------------------- starter-pack apply

    [Fact]
    public async Task ApplyToBoardAsync_WhenArchiveCommitsAfterTheBoardRead_RejectsTheApply()
    {
        var dbPath = NewDatabasePath("bulk-pack-race");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, _) = await SeedBoardAsync(options);

            await using var writeDb = new TaskdeckDbContext(options);
            var board = await LoadBoardWithDetailsAsync(writeDb, boardId);

            var unitOfWork = CreateUnitOfWork(writeDb);
            StubBoardReads(unitOfWork, board);
            StubCardAdds(unitOfWork, writeDb);
            StubColumnAndLabelAdds(unitOfWork, writeDb);

            // Manifest validation runs after ApplyToBoardAsync cleared its archived check and before
            // ApplyPlanAsync opens its transaction.
            var validator = new ArchiveInjectingManifestValidator(
                onValidate: () => ArchiveOnAnotherConnection(options, boardId));

            var service = new StarterPackApplyService(unitOfWork.Object, validator);

            var result = await service.ApplyToBoardAsync(
                boardId,
                new ApplyStarterPackDto(BuildManifest("Pack Backlog", "Seeded card"), DryRun: false));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(b => b.Id == boardId)).IsArchived.Should().BeTrue();
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0);
            (await verifyDb.Columns.CountAsync(column => column.BoardId == boardId)).Should().Be(1,
                "only the seeded column survives; the pack's column is rolled back with its cards");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task ApplyToBoardAsync_WhenAnImportReadsTheSameBoard_BothPersist()
    {
        var dbPath = NewDatabasePath("bulk-pack-independent");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, _) = await SeedBoardAsync(options);

            // Two *different* bulk writers, both holding the token read before either committed.
            // This is the regression against accidentally serializing the bulk writers on a board.
            await using var packDb = new TaskdeckDbContext(options);
            await using var importDb = new TaskdeckDbContext(options);
            var packBoard = await LoadBoardWithDetailsAsync(packDb, boardId);
            var importBoard = await LoadBoardWithDetailsAsync(importDb, boardId);

            var packUnitOfWork = CreateUnitOfWork(packDb);
            StubBoardReads(packUnitOfWork, packBoard);
            StubCardAdds(packUnitOfWork, packDb);
            StubColumnAndLabelAdds(packUnitOfWork, packDb);
            var packService = new StarterPackApplyService(
                packUnitOfWork.Object,
                new ArchiveInjectingManifestValidator());

            var importService = CreateImportService(importDb, importBoard, "Imported card", "import-dedupe-x");

            var packResult = await packService.ApplyToBoardAsync(
                boardId,
                new ApplyStarterPackDto(BuildManifest("Pack Backlog", "Seeded card"), DryRun: false));
            var importResult = await importService.ImportToBoardAsync(boardId, BuildImportRequest());

            packResult.IsSuccess.Should().BeTrue();
            importResult.IsSuccess.Should().BeTrue();

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(2);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    // ----------------------------------------------------------- archive-item restore

    [Fact]
    public async Task RestoreArchiveItemAsync_WhenArchiveCommitsAfterThePlannerCheck_RejectsTheRestore()
    {
        var dbPath = NewDatabasePath("bulk-restore-race");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, columnId) = await SeedBoardAsync(options);
            var restoredByUserId = await SeedUserAsync(options);
            var archiveItemId = await SeedCardArchiveItemAsync(options, boardId, columnId, "Restored card");

            await using var writeDb = new TaskdeckDbContext(options);
            var board = await LoadBoardWithDetailsAsync(writeDb, boardId);
            var archiveItem = await writeDb.ArchiveItems.SingleAsync(item => item.Id == archiveItemId);

            var unitOfWork = CreateUnitOfWork(writeDb);
            StubBoardReads(unitOfWork, board);
            StubCardAdds(unitOfWork, writeDb);
            StubAuditLogAdds(unitOfWork, writeDb);
            StubArchiveItemReads(unitOfWork, archiveItem);

            // RestorePlanner already cleared its "cannot restore to an archived board" check by the
            // time RestoreExecutor loads the destination column, so archiving here lands inside the
            // window that check leaves open.
            StubColumnWithCardsRead(
                unitOfWork,
                writeDb,
                columnId,
                onRead: () => ArchiveOnAnotherConnection(options, boardId));

            var service = new ArchiveRecoveryService(unitOfWork.Object);

            var result = await service.RestoreArchiveItemAsync(
                archiveItemId,
                new RestoreArchiveItemDto(boardId, RestoreMode.Copy, ConflictStrategy.Rename),
                restoredByUserId);

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(b => b.Id == boardId)).IsArchived.Should().BeTrue();
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(0);
            (await verifyDb.ArchiveItems.SingleAsync(item => item.Id == archiveItemId))
                .RestoreStatus.Should().Be(RestoreStatus.Available,
                    "the card insert, the stale board update, and MarkAsRestored share one " +
                    "SaveChanges, so a rejected restore must not consume the archive item");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_WhenTwoRestoresReadTheSameBoard_BothPersist()
    {
        var dbPath = NewDatabasePath("bulk-restore-independent");
        try
        {
            var options = BuildOptions(dbPath);
            var (boardId, columnId) = await SeedBoardAsync(options);
            var restoredByUserId = await SeedUserAsync(options);
            var firstItemId = await SeedCardArchiveItemAsync(options, boardId, columnId, "First restored card");
            var secondItemId = await SeedCardArchiveItemAsync(options, boardId, columnId, "Second restored card");

            await using var firstDb = new TaskdeckDbContext(options);
            await using var secondDb = new TaskdeckDbContext(options);
            var firstService = await CreateRestoreServiceAsync(firstDb, options, boardId, columnId, firstItemId);
            var secondService = await CreateRestoreServiceAsync(secondDb, options, boardId, columnId, secondItemId);

            var dto = new RestoreArchiveItemDto(boardId, RestoreMode.Copy, ConflictStrategy.Rename);
            var firstResult = await firstService.RestoreArchiveItemAsync(firstItemId, dto, restoredByUserId);
            var secondResult = await secondService.RestoreArchiveItemAsync(secondItemId, dto, restoredByUserId);

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();

            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Cards.CountAsync(card => card.BoardId == boardId)).Should().Be(2);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    // ------------------------------------------------------------------------ harness

    /// <summary>
    /// Builds a unit of work over a real <see cref="TaskdeckDbContext"/>, reproducing the only
    /// behaviour of the production <c>UnitOfWork</c> these tests depend on: the concurrency-failure
    /// translation, and transactions that really begin, commit, and roll back.
    /// </summary>
    private static Mock<IUnitOfWork> CreateUnitOfWork(TaskdeckDbContext db)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        IDbContextTransaction? transaction = null;

        unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                try
                {
                    return await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException exception)
                {
                    throw new DomainException(
                        ErrorCodes.Conflict,
                        "Record was updated by another session. Refresh and retry your action.",
                        exception);
                }
            });

        unitOfWork.Setup(work => work.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            });

        unitOfWork.Setup(work => work.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                if (transaction is null)
                    return;

                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
                transaction = null;
            });

        unitOfWork.Setup(work => work.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                if (transaction is null)
                    return;

                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                transaction = null;
            });

        return unitOfWork;
    }

    private static void StubBoardReads(Mock<IUnitOfWork> unitOfWork, Board board)
    {
        var boards = new Mock<IBoardRepository>();
        boards.Setup(repository => repository.GetByIdWithDetailsAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        boards.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
    }

    private static void StubCardAdds(Mock<IUnitOfWork> unitOfWork, TaskdeckDbContext db)
    {
        var cards = new Mock<ICardRepository>();
        cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
            .Returns((Card card, CancellationToken _) =>
            {
                db.Cards.Add(card);
                return Task.FromResult(card);
            });
        unitOfWork.SetupGet(work => work.Cards).Returns(cards.Object);
    }

    private static void StubColumnAndLabelAdds(Mock<IUnitOfWork> unitOfWork, TaskdeckDbContext db)
    {
        var columns = new Mock<IColumnRepository>();
        columns.Setup(repository => repository.AddAsync(It.IsAny<Column>(), It.IsAny<CancellationToken>()))
            .Returns((Column column, CancellationToken _) =>
            {
                db.Columns.Add(column);
                return Task.FromResult(column);
            });
        unitOfWork.SetupGet(work => work.Columns).Returns(columns.Object);

        var labels = new Mock<ILabelRepository>();
        labels.Setup(repository => repository.AddAsync(It.IsAny<Label>(), It.IsAny<CancellationToken>()))
            .Returns((Label label, CancellationToken _) =>
            {
                db.Labels.Add(label);
                return Task.FromResult(label);
            });
        unitOfWork.SetupGet(work => work.Labels).Returns(labels.Object);
    }

    private static void StubColumnWithCardsRead(
        Mock<IUnitOfWork> unitOfWork,
        TaskdeckDbContext db,
        Guid columnId,
        Action? onRead = null)
    {
        var columns = new Mock<IColumnRepository>();
        columns.Setup(repository => repository.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                onRead?.Invoke();
                return await db.Columns
                    .Include(column => column.Cards)
                    .SingleOrDefaultAsync(column => column.Id == columnId);
            });
        unitOfWork.SetupGet(work => work.Columns).Returns(columns.Object);
    }

    private static void StubAuditLogAdds(Mock<IUnitOfWork> unitOfWork, TaskdeckDbContext db)
    {
        var auditLogs = new Mock<IAuditLogRepository>();
        auditLogs.Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns((AuditLog log, CancellationToken _) =>
            {
                db.AuditLogs.Add(log);
                return Task.FromResult(log);
            });
        unitOfWork.SetupGet(work => work.AuditLogs).Returns(auditLogs.Object);
    }

    private static void StubArchiveItemReads(Mock<IUnitOfWork> unitOfWork, ArchiveItem archiveItem)
    {
        var archiveItems = new Mock<IArchiveItemRepository>();
        archiveItems.Setup(repository => repository.GetByIdAsync(archiveItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archiveItem);
        unitOfWork.SetupGet(work => work.ArchiveItems).Returns(archiveItems.Object);
    }

    private static ExternalImportService CreateImportService(
        TaskdeckDbContext db,
        Board board,
        string cardTitle,
        string dedupeKey)
    {
        var unitOfWork = CreateUnitOfWork(db);
        StubBoardReads(unitOfWork, board);
        StubCardAdds(unitOfWork, db);

        return new ExternalImportService(
            unitOfWork.Object,
            [new ArchiveInjectingAdapter(SingleCandidate(cardTitle, dedupeKey))]);
    }

    private static async Task<ArchiveRecoveryService> CreateRestoreServiceAsync(
        TaskdeckDbContext db,
        DbContextOptions<TaskdeckDbContext> options,
        Guid boardId,
        Guid columnId,
        Guid archiveItemId)
    {
        _ = options;
        var board = await LoadBoardWithDetailsAsync(db, boardId);
        var archiveItem = await db.ArchiveItems.SingleAsync(item => item.Id == archiveItemId);

        var unitOfWork = CreateUnitOfWork(db);
        StubBoardReads(unitOfWork, board);
        StubCardAdds(unitOfWork, db);
        StubAuditLogAdds(unitOfWork, db);
        StubArchiveItemReads(unitOfWork, archiveItem);
        StubColumnWithCardsRead(unitOfWork, db, columnId);

        return new ArchiveRecoveryService(unitOfWork.Object);
    }

    private static ExternalImportRequestDto BuildImportRequest() =>
        new(
            Provider: ExternalImportProviders.Csv,
            Payload: "parsed by the test adapter, not by CSV parsing",
            TargetColumnName: TargetColumnName,
            DryRun: false);

    private static ExternalImportParseResult SingleCandidate(string title, string dedupeKey) =>
        new(
            Provider: ExternalImportProviders.Csv,
            Profile: ExternalImportProfiles.OutreachContactsV1,
            RowsReceived: 1,
            RowsParsed: 1,
            Candidates: [new ExternalImportCandidate(1, dedupeKey, title, $"{title} description")],
            Conflicts: []);

    /// <summary>
    /// A manifest whose column and seed card are both new to the seeded board, so conflict detection
    /// is clean and the apply reaches its write phase.
    /// </summary>
    private static StarterPackManifestDto BuildManifest(string columnName, string seedCardTitle) =>
        new()
        {
            SchemaVersion = "1.0",
            PackId = "concurrency.pack.v1",
            DisplayName = "Concurrency pack",
            Compatibility = new StarterPackCompatibilityDto { MinTaskdeckVersion = "0.1.0" },
            Columns = [new StarterPackColumnDto { Name = columnName, Position = 5 }],
            SeedCards =
            [
                new StarterPackSeedCardDto { Title = seedCardTitle, ColumnName = columnName }
            ]
        };

    // ------------------------------------------------------------------------ fixtures

    private sealed class ArchiveInjectingAdapter : IExternalImportAdapter
    {
        private readonly ExternalImportParseResult _parseResult;
        private readonly Action? _onParse;

        public ArchiveInjectingAdapter(ExternalImportParseResult parseResult, Action? onParse = null)
        {
            _parseResult = parseResult;
            _onParse = onParse;
        }

        public string Provider => ExternalImportProviders.Csv;

        public Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request)
        {
            _onParse?.Invoke();
            return Result.Success(_parseResult);
        }
    }

    private sealed class ArchiveInjectingManifestValidator : IStarterPackManifestValidator
    {
        private readonly Action? _onValidate;

        public ArchiveInjectingManifestValidator(Action? onValidate = null)
        {
            _onValidate = onValidate;
        }

        public StarterPackManifestValidationResult ValidateJson(string manifestJson) =>
            throw new NotSupportedException("These tests apply a manifest object directly.");

        public StarterPackManifestValidationResult Validate(StarterPackManifestDto manifest)
        {
            _onValidate?.Invoke();
            return new StarterPackManifestValidationResult(
                manifest,
                Array.Empty<StarterPackManifestValidationError>());
        }
    }

    /// <summary>
    /// Runs an archive to completion on its own connection. Synchronous on purpose: it is called
    /// from inside the writers' synchronous callbacks, and blocking on an async commit there would
    /// trade a deterministic test for a deadlock risk.
    /// </summary>
    private static void ArchiveOnAnotherConnection(DbContextOptions<TaskdeckDbContext> options, Guid boardId)
    {
        using var archiveDb = new TaskdeckDbContext(options);
        var board = archiveDb.Boards.Single(candidate => candidate.Id == boardId);
        board.Archive();
        archiveDb.SaveChanges();
    }

    private static DbContextOptions<TaskdeckDbContext> BuildOptions(string dbPath) =>
        new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .Options;

    private static string NewDatabasePath(string label) =>
        Path.Combine(Path.GetTempPath(), $"taskdeck-{label}-{Guid.NewGuid():N}.db");

    private static Task<Board> LoadBoardWithDetailsAsync(TaskdeckDbContext db, Guid boardId) =>
        db.Boards
            .Include(board => board.Columns)
                .ThenInclude(column => column.Cards)
            .Include(board => board.Labels)
            .AsSplitQuery()
            .SingleAsync(board => board.Id == boardId);

    private static async Task<(Guid BoardId, Guid ColumnId)> SeedBoardAsync(
        DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();

        var board = new Board("Bulk writer board");
        var column = new Column(board.Id, TargetColumnName, 0);
        db.Boards.Add(board);
        db.Columns.Add(column);
        await db.SaveChangesAsync();

        return (board.Id, column.Id);
    }

    /// <summary>
    /// Seeds a real user. The restore path writes an <see cref="AuditLog"/>, whose <c>UserId</c> is a
    /// genuine foreign key to <c>Users</c>, so an invented actor id fails the insert on a constraint
    /// and would mask the concurrency outcome these tests are asserting.
    /// </summary>
    private static async Task<Guid> SeedUserAsync(DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);

        var suffix = Guid.NewGuid().ToString("N");
        var user = new User($"restorer-{suffix}", $"restorer-{suffix}@example.test", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<Guid> SeedCardArchiveItemAsync(
        DbContextOptions<TaskdeckDbContext> options,
        Guid boardId,
        Guid columnId,
        string title)
    {
        await using var db = new TaskdeckDbContext(options);

        // Written as literal JSON because CardSnapshot is internal to Taskdeck.Application; the
        // property names below are the ones its deserializer binds.
        var snapshotJson =
            $$"""
            {"Title":"{{title}}","Description":"restored from archive","DueDate":null,"IsBlocked":false,"BlockReason":null,"ColumnId":"{{columnId}}"}
            """;

        var archiveItem = new ArchiveItem(
            "card",
            Guid.NewGuid(),
            boardId,
            title,
            Guid.NewGuid(),
            snapshotJson);

        db.ArchiveItems.Add(archiveItem);
        await db.SaveChangesAsync();

        return archiveItem.Id;
    }

    private static void DeleteTemporaryDatabase(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = dbPath + suffix;
            if (!File.Exists(path))
                continue;

            try { File.Delete(path); }
            catch (IOException) { /* best-effort temporary database cleanup */ }
        }
    }
}
