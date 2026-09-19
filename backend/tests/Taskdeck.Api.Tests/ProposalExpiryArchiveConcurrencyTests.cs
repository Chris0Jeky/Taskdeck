using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression contract for #2170. The expiry query filters boards that are already archived, but
/// a board can be archived after that read and before the expiry save. Automatic expiry must join
/// the same board concurrency boundary as interactive proposal decisions.
/// </summary>
public sealed class ProposalExpiryArchiveConcurrencyTests
{
    [Fact]
    public async Task Service_WhenArchiveCommitsAfterExpiryGuard_RollsBackTheExpiry()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-proposal-expiry-service-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(databasePath))
                .Options;
            var seeded = await SeedExpiredProposalAsync(options);

            await using var expiryDb = new TaskdeckDbContext(options);
            await using var archiveDb = new TaskdeckDbContext(options);
            var expiryBoard = await expiryDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);
            var expiryProposal = await expiryDb.AutomationProposals
                .SingleAsync(proposal => proposal.Id == seeded.ProposalId);
            var archiveBoard = await archiveDb.Boards.SingleAsync(board => board.Id == seeded.BoardId);

            var boards = new Mock<IBoardRepository>();
            boards
                .Setup(repository => repository.GetByIdsAsync(
                    It.IsAny<IEnumerable<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { expiryBoard });

            var proposals = new Mock<IAutomationProposalRepository>();
            proposals
                .Setup(repository => repository.GetExpiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExpiredProposalSweep(new[] { expiryProposal }, 0));

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.SetupGet(work => work.Boards).Returns(boards.Object);
            unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
            unitOfWork
                .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken cancellationToken) =>
                {
                    // The query and board guard have already observed an active board. Archive wins
                    // immediately before the automatic lane attempts its atomic expiry save.
                    archiveBoard.Archive();
                    await archiveDb.SaveChangesAsync(cancellationToken);
                    return await SaveWithConflictMappingAsync(expiryDb, cancellationToken);
                });

            var policy = new AutomationPolicyEngine(unitOfWork.Object);
            var inner = new AutomationProposalService(
                unitOfWork.Object,
                policyEngine: policy);
            IAutomationProposalService service = new ProposalExpiryGuardedService(
                inner,
                unitOfWork.Object,
                policy);

            var result = await service.ExpireProposalsAsync();

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.Conflict);
            proposals.Verify(
                repository => repository.GetExpiredAsync(It.IsAny<CancellationToken>()),
                Times.Once,
                "the guard and lifecycle mutation must consume the same authoritative sweep");
            await using var verifyDb = new TaskdeckDbContext(options);
            (await verifyDb.Boards.SingleAsync(board => board.Id == seeded.BoardId))
                .IsArchived.Should().BeTrue();
            (await verifyDb.AutomationProposals.SingleAsync(proposal => proposal.Id == seeded.ProposalId))
                .Status.Should().Be(
                    ProposalStatus.PendingReview,
                    "the board marker and expiry must share one transaction, so the losing expiry rolls back");
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Service_WhenProposalExpiresAfterGuardSnapshot_DoesNotMutateTheUnguardedCandidate()
    {
        var ownerId = Guid.NewGuid();
        var guardedBoardId = Guid.NewGuid();
        var lateBoard = new Board("Late expiry board", ownerId: ownerId);
        var guarded = new AutomationProposal(
            ProposalSourceType.Queue,
            ownerId,
            "Already expired",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            guardedBoardId,
            expiryMinutes: 1);
        var late = new AutomationProposal(
            ProposalSourceType.Queue,
            ownerId,
            "Crosses the threshold after the guard snapshot",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            lateBoard.Id,
            expiryMinutes: 60);
        SetExpiresAt(guarded, DateTime.UtcNow.AddMinutes(-5));

        var sweeps = new Queue<ExpiredProposalSweep>(new[]
        {
            new ExpiredProposalSweep(new[] { guarded }, 0),
            new ExpiredProposalSweep(new[] { guarded, late }, 0),
        });
        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(repository => repository.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sweeps.Dequeue());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // The second candidate's board archives after the authoritative query but before
                // save. Because that candidate was absent from the guarded snapshot, it must not
                // join this mutation batch even though it became stale in the meantime.
                lateBoard.Archive();
                return 1;
            });

        var policy = new Mock<IAutomationPolicyEngine>();
        policy
            .Setup(engine => engine.GuardProposalDecisionWritesAsync(
                It.Is<IEnumerable<Guid?>>(ids => ids.SequenceEqual(new Guid?[] { guardedBoardId })),
                It.IsAny<CancellationToken>()))
            .Callback(() => SetExpiresAt(late, DateTime.UtcNow.AddMinutes(-5)))
            .ReturnsAsync(Result.Success());

        var inner = new AutomationProposalService(
            unitOfWork.Object,
            policyEngine: policy.Object);
        IAutomationProposalService service = new ProposalExpiryGuardedService(
            inner,
            unitOfWork.Object,
            policy.Object);

        var result = await service.ExpireProposalsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        guarded.Status.Should().Be(ProposalStatus.Expired);
        late.Status.Should().Be(
            ProposalStatus.PendingReview,
            "a proposal outside the guarded authoritative snapshot must wait for the next sweep");
        lateBoard.IsArchived.Should().BeTrue();
        proposals.Verify(
            repository => repository.GetExpiredAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "guard and mutation must consume one authoritative candidate snapshot");
        unitOfWork.Verify(
            work => work.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_WhenArchiveLandsBetweenQueryAndGuard_DefersBeforeMutatingTheProposal()
    {
        var boardId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            Guid.NewGuid(),
            "Archive won after the expiry query",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId,
            expiryMinutes: 1);
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(repository => repository.GetExpiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpiredProposalSweep(new[] { proposal }, 0));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork
            .Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var policy = new Mock<IAutomationPolicyEngine>();
        policy
            .Setup(engine => engine.GuardProposalDecisionWritesAsync(
                It.Is<IEnumerable<Guid?>>(ids => ids.SequenceEqual(new Guid?[] { boardId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(
                ErrorCodes.InvalidOperation,
                "Cannot modify proposals on an archived board."));

        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(unitOfWork.Object);
        services.AddSingleton<IAutomationPolicyEngine>(policy.Object);
        using var serviceProvider = services.BuildServiceProvider();
        var logger = new InMemoryLogger<ProposalHousekeepingWorker>();
        var worker = new ProposalHousekeepingWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new WorkerSettings(),
            new WorkerHeartbeatRegistry(),
            logger);

        await InvokeExpirySweepAsync(worker);

        proposal.Status.Should().Be(
            ProposalStatus.PendingReview,
            "an archive observed by the second-stage guard must stop mutation before SaveChanges");
        policy.Verify(
            engine => engine.GuardProposalDecisionWritesAsync(
                It.IsAny<IEnumerable<Guid?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(
            work => work.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        logger.Entries.Should().ContainSingle(entry =>
            entry.Message.Contains("Deferred expiring") &&
            entry.Message.Contains("board state changed"));
        logger.Entries.Should().OnlyContain(entry =>
            !entry.Message.Contains(proposal.Id.ToString(), StringComparison.OrdinalIgnoreCase) &&
            !entry.Message.Contains(proposal.Summary, StringComparison.Ordinal));
    }

    private static async Task<SeededExpiry> SeedExpiredProposalAsync(
        DbContextOptions<TaskdeckDbContext> options)
    {
        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        var user = new User(
            $"expiry-race-{Guid.NewGuid():N}",
            $"expiry-race-{Guid.NewGuid():N}@example.com",
            "Password1!");
        var board = new Board("Expiry race board", ownerId: user.Id);
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            user.Id,
            "Expired proposal guarded by board concurrency",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id,
            expiryMinutes: 1);
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        db.AddRange(user, board, proposal);
        await db.SaveChangesAsync();
        return new SeededExpiry(board.Id, proposal.Id);
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

    private static async Task InvokeExpirySweepAsync(ProposalHousekeepingWorker worker)
    {
        var method = typeof(ProposalHousekeepingWorker).GetMethod(
            "ExpireStaleProposalsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var invocation = method!.Invoke(worker, new object[] { CancellationToken.None });
        invocation.Should().BeAssignableTo<Task>();
        await (Task)invocation!;
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        var property = typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(proposal, expiresAt);
    }

    private static void DeleteTemporaryDatabase(string databasePath)
    {
        foreach (var path in TestWebApplicationFactory.GetDatabaseCleanupTargets(databasePath))
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort test cleanup; assertions above own the useful failure.
            }
        }
    }

    private sealed record SeededExpiry(Guid BoardId, Guid ProposalId);
}
