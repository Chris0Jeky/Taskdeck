using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ArchivedBoardProposalDecisionConcurrencyTests
{
    [Fact]
    public async Task Reject_WhenArchiveCommitsAfterGuard_RollsBackDecisionAndKeepsArchive()
    {
        var dbPath = TemporaryDatabasePath("decision");
        try
        {
            var options = CreateOptions(dbPath);
            var seeded = await SeedProposalAsync(options);

            await using var decisionDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var decisionBoard = await decisionDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var decisionProposal = await decisionDb.AutomationProposals
                .SingleAsync(proposal => proposal.Id == seeded.ProposalId);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var unitOfWork = CreateDecisionUnitOfWork(
                decisionDb,
                decisionBoard,
                decisionProposal,
                beforeFirstSave: async () =>
                {
                    archiveBoard.Archive();
                    await archiveDb.SaveChangesAsync();
                });
            var service = new AutomationProposalService(unitOfWork.Object);

            var result = await service.RejectProposalAsync(
                seeded.ProposalId,
                seeded.UserId,
                new UpdateProposalStatusDto("Archive wins"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);
            await AssertArchivedWithProposalStatusAsync(
                options,
                seeded,
                ProposalStatus.PendingReview);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task CreateRevision_WhenArchiveCommitsAfterGuard_RollsBackRevisionAndKeepsArchive()
    {
        var dbPath = TemporaryDatabasePath("revision");
        try
        {
            var options = CreateOptions(dbPath);
            var seeded = await SeedProposalAsync(options);

            await using var revisionDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var revisionBoard = await revisionDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var revisionProposal = await revisionDb.AutomationProposals
                .SingleAsync(proposal => proposal.Id == seeded.ProposalId);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var unitOfWork = CreateDecisionUnitOfWork(
                revisionDb,
                revisionBoard,
                revisionProposal,
                beforeFirstSave: async () =>
                {
                    archiveBoard.Archive();
                    await archiveDb.SaveChangesAsync();
                });
            var revisionRepository = new Mock<IProposalRevisionRepository>();
            unitOfWork.SetupGet(work => work.ProposalRevisions).Returns(revisionRepository.Object);
            revisionRepository
                .Setup(repository => repository.GetNextRevisionNumberAsync(
                    seeded.ProposalId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            revisionRepository
                .Setup(repository => repository.AddAsync(
                    It.IsAny<ProposalRevision>(),
                    It.IsAny<CancellationToken>()))
                .Returns((ProposalRevision revision, CancellationToken _) =>
                {
                    revisionDb.ProposalRevisions.Add(revision);
                    return Task.FromResult(revision);
                });
            var service = new ProposalRevisionService(
                unitOfWork.Object,
                new AutomationPolicyEngine(unitOfWork.Object));
            var dto = new CreateProposalRevisionDto(
                seeded.ProposalId,
                seeded.UserId,
                BuildRevisionPayload(seeded.ProposalId, seeded.BoardId),
                "Archive wins");

            var result = await service.CreateRevisionAsync(dto);

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);
            await AssertArchivedWithProposalStatusAsync(
                options,
                seeded,
                ProposalStatus.PendingReview);
            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.ProposalRevisions.CountAsync(revision => revision.ProposalId == seeded.ProposalId))
                .Should().Be(0, "the revision and board marker share one failed save");
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task Execute_WhenArchiveCommitsAfterGuard_RollsBackOperationAndAppliedStatus()
    {
        var dbPath = TemporaryDatabasePath("execute");
        try
        {
            var options = CreateOptions(dbPath);
            var seeded = await SeedProposalAsync(options, approved: true);

            await using var executionDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var archiveCommitted = false;
            var boards = new Mock<IBoardRepository>();
            var proposals = new Mock<IAutomationProposalRepository>();
            var auditLogs = new Mock<IAuditLogRepository>();
            var llmQueue = new Mock<ILlmQueueRepository>();
            var revisions = new Mock<IProposalRevisionRepository>();
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
            unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
            unitOfWork.SetupGet(work => work.AuditLogs).Returns(auditLogs.Object);
            unitOfWork.SetupGet(work => work.LlmQueue).Returns(llmQueue.Object);
            unitOfWork.SetupGet(work => work.ProposalRevisions).Returns(revisions.Object);
            boards
                .Setup(repository => repository.GetByIdsAsync(
                    It.IsAny<IEnumerable<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (IEnumerable<Guid> _, CancellationToken cancellationToken) =>
                    (IEnumerable<Board>)await executionDb.Boards
                        .Where(board => board.Id == seeded.BoardId)
                        .ToListAsync(cancellationToken));
            boards
                .Setup(repository => repository.GetByIdAsync(
                    seeded.BoardId,
                    It.IsAny<CancellationToken>()))
                .Returns((Guid _, CancellationToken cancellationToken) => executionDb.Boards
                    .SingleOrDefaultAsync(board => board.Id == seeded.BoardId, cancellationToken));
            proposals
                .Setup(repository => repository.GetByIdAsync(
                    seeded.ProposalId,
                    It.IsAny<CancellationToken>()))
                .Returns((Guid _, CancellationToken cancellationToken) => executionDb.AutomationProposals
                    .SingleOrDefaultAsync(proposal => proposal.Id == seeded.ProposalId, cancellationToken));
            unitOfWork
                .Setup(work => work.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            unitOfWork
                .Setup(work => work.CommitTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            unitOfWork
                .Setup(work => work.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    executionDb.ChangeTracker.Clear();
                    return Task.CompletedTask;
                });
            unitOfWork
                .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    if (!archiveCommitted)
                    {
                        archiveCommitted = true;
                        archiveBoard.Archive();
                        await archiveDb.SaveChangesAsync(cancellationToken);
                    }

                    return await SaveWithConflictMappingAsync(executionDb, cancellationToken);
                });
            auditLogs
                .Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AuditLog auditLog, CancellationToken _) => auditLog);

            var operation = new ProposalOperationDto(
                Guid.NewGuid(),
                seeded.ProposalId,
                0,
                "update",
                "board",
                seeded.BoardId.ToString(),
                JsonSerializer.Serialize(new
                {
                    boardId = seeded.BoardId,
                    name = "Racing operation must roll back"
                }),
                Guid.NewGuid().ToString("N"),
                null);
            var proposalDto = BuildProposalDto(seeded, operation);
            var proposalService = new Mock<IAutomationProposalService>();
            proposalService
                .Setup(service => service.GetProposalByIdAsync(
                    seeded.ProposalId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(proposalDto));
            var realPolicyEngine = new AutomationPolicyEngine(unitOfWork.Object);
            var policyEngine = new Mock<IAutomationPolicyEngine>();
            policyEngine.Setup(engine => engine.ValidatePolicy(It.IsAny<ProposalDto>()))
                .Returns(Result.Success());
            policyEngine
                .Setup(engine => engine.ValidatePermissionsAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<IEnumerable<ProposalOperationDto>>(),
                    BoardAccessBar.Write,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());
            policyEngine
                .Setup(engine => engine.GuardProposalDecisionWritesAsync(
                    It.IsAny<IEnumerable<Guid?>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IEnumerable<Guid?> boardIds, CancellationToken cancellationToken) =>
                    realPolicyEngine.GuardProposalDecisionWritesAsync(boardIds, cancellationToken));
            var executor = new AutomationExecutorService(
                unitOfWork.Object,
                proposalService.Object,
                policyEngine.Object,
                new CardService(unitOfWork.Object),
                new BoardService(unitOfWork.Object),
                new ColumnService(unitOfWork.Object));

            var result = await executor.ExecuteProposalAsync(
                seeded.ProposalId,
                Guid.NewGuid().ToString("N"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);
            await using var verifyDb = new TaskdeckDbContext(options);
            var persistedBoard = await verifyDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            persistedBoard.IsArchived.Should().BeTrue();
            persistedBoard.Name.Should().Be("Proposal concurrency board",
                "the board update operation must roll back while the racing archive persists");
            (await verifyDb.AutomationProposals.SingleAsync(proposal => proposal.Id == seeded.ProposalId))
                .Status.Should().Be(ProposalStatus.Approved,
                    "neither Applied nor the separately guarded Failed write may land");
            (await verifyDb.AuditLogs.CountAsync()).Should().Be(0);
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    [Fact]
    public async Task IndependentDecisions_FromStaleActiveBoardReads_BothRemainAllowed()
    {
        var dbPath = TemporaryDatabasePath("active-decisions");
        try
        {
            var options = CreateOptions(dbPath);
            var seeded = await SeedTwoProposalsAsync(options);

            await using var firstDb = new TaskdeckDbContext(options);
            await using var secondDb = new TaskdeckDbContext(options);
            var firstBoard = await firstDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var secondBoard = await secondDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var firstProposal = await firstDb.AutomationProposals
                .SingleAsync(proposal => proposal.Id == seeded.FirstProposalId);
            var secondProposal = await secondDb.AutomationProposals
                .SingleAsync(proposal => proposal.Id == seeded.SecondProposalId);
            var firstService = new AutomationProposalService(
                CreateDecisionUnitOfWork(firstDb, firstBoard, firstProposal).Object);
            var secondService = new AutomationProposalService(
                CreateDecisionUnitOfWork(secondDb, secondBoard, secondProposal).Object);

            var firstResult = await firstService.RejectProposalAsync(
                seeded.FirstProposalId,
                seeded.UserId,
                new UpdateProposalStatusDto("First active decision"));
            var secondResult = await secondService.RejectProposalAsync(
                seeded.SecondProposalId,
                seeded.UserId,
                new UpdateProposalStatusDto("Second active decision"));

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();
            await using var verifyDb = new TaskdeckDbContext(options);
            var statuses = await verifyDb.AutomationProposals
                .Where(proposal =>
                    proposal.Id == seeded.FirstProposalId ||
                    proposal.Id == seeded.SecondProposalId)
                .Select(proposal => proposal.Status)
                .ToListAsync();
            statuses.Should().OnlyContain(status => status == ProposalStatus.Rejected);
            (await verifyDb.Boards.SingleAsync(board => board.Id == seeded.BoardId))
                .IsArchived.Should().BeFalse();
        }
        finally
        {
            DeleteTemporaryDatabase(dbPath);
        }
    }

    private static Mock<IUnitOfWork> CreateDecisionUnitOfWork(
        TaskdeckDbContext db,
        Board board,
        AutomationProposal proposal,
        Func<Task>? beforeFirstSave = null)
    {
        var boards = new Mock<IBoardRepository>();
        var proposals = new Mock<IAutomationProposalRepository>();
        var revisions = new Mock<IProposalRevisionRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var saveStarted = false;
        unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(work => work.ProposalRevisions).Returns(revisions.Object);
        boards
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { board });
        proposals
            .Setup(repository => repository.GetByIdAsync(
                proposal.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        revisions
            .Setup(repository => repository.GetRefsByProposalIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProposalRevisionRef>());
        revisions
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProposalRevision>());
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                if (!saveStarted)
                {
                    saveStarted = true;
                    if (beforeFirstSave is not null)
                        await beforeFirstSave();
                }

                return await SaveWithConflictMappingAsync(db, cancellationToken);
            });
        return unitOfWork;
    }

    private static async Task<int> SaveWithConflictMappingAsync(
        TaskdeckDbContext db,
        CancellationToken cancellationToken)
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
    }

    private static ProposalDto BuildProposalDto(
        SeededProposal seeded,
        ProposalOperationDto operation)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProposalDto(
            seeded.ProposalId,
            ProposalSourceType.Chat,
            null,
            seeded.BoardId,
            seeded.UserId,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Execute concurrency proposal",
            null,
            null,
            now,
            now,
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow,
            seeded.UserId,
            null,
            null,
            Guid.NewGuid().ToString("N"),
            new List<ProposalOperationDto> { operation });
    }

    private static string BuildRevisionPayload(Guid proposalId, Guid boardId)
    {
        return JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    proposalId,
                    sequence = 0,
                    actionType = "update",
                    targetType = "board",
                    targetId = boardId.ToString(),
                    parameters = JsonSerializer.Serialize(new
                    {
                        boardId,
                        name = "Revision that must roll back"
                    }),
                    idempotencyKey = Guid.NewGuid().ToString("N"),
                    expectedVersion = (string?)null
                }
            }
        });
    }

    private static async Task<SeededProposal> SeedProposalAsync(
        DbContextOptions<TaskdeckDbContext> options,
        bool approved = false)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        var user = new User(
            $"proposal-race-{Guid.NewGuid():N}",
            $"proposal-race-{Guid.NewGuid():N}@example.com",
            "Password1!");
        var board = new Board("Proposal concurrency board", ownerId: user.Id);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            user.Id,
            "Proposal concurrency decision",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        if (approved)
            proposal.Approve(user.Id);

        db.AddRange(user, board, proposal);
        await db.SaveChangesAsync();
        return new SeededProposal(user.Id, board.Id, proposal.Id);
    }

    private static async Task<SeededProposalPair> SeedTwoProposalsAsync(
        DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        var user = new User(
            $"proposal-active-{Guid.NewGuid():N}",
            $"proposal-active-{Guid.NewGuid():N}@example.com",
            "Password1!");
        var board = new Board("Active decision board", ownerId: user.Id);
        var first = new AutomationProposal(
            ProposalSourceType.Chat,
            user.Id,
            "First active decision",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        var second = new AutomationProposal(
            ProposalSourceType.Chat,
            user.Id,
            "Second active decision",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        db.AddRange(user, board, first, second);
        await db.SaveChangesAsync();
        return new SeededProposalPair(user.Id, board.Id, first.Id, second.Id);
    }

    private static async Task AssertArchivedWithProposalStatusAsync(
        DbContextOptions<TaskdeckDbContext> options,
        SeededProposal seeded,
        ProposalStatus expectedStatus)
    {
        await using var verifyDb = new TaskdeckDbContext(options);
        (await verifyDb.Boards.SingleAsync(board => board.Id == seeded.BoardId))
            .IsArchived.Should().BeTrue();
        (await verifyDb.AutomationProposals.SingleAsync(proposal => proposal.Id == seeded.ProposalId))
            .Status.Should().Be(expectedStatus);
    }

    private static DbContextOptions<TaskdeckDbContext> CreateOptions(string dbPath) =>
        new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .Options;

    private static string TemporaryDatabasePath(string stem) =>
        Path.Combine(Path.GetTempPath(), $"taskdeck-proposal-{stem}-{Guid.NewGuid():N}.db");

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

    private sealed record SeededProposal(Guid UserId, Guid BoardId, Guid ProposalId);

    private sealed record SeededProposalPair(
        Guid UserId,
        Guid BoardId,
        Guid FirstProposalId,
        Guid SecondProposalId);
}
