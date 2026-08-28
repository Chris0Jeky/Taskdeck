using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class UnitOfWorkRollbackTests : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public UnitOfWorkRollbackTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RollbackTransactionAsync_ClearsTrackedStateBeforeLaterSave()
    {
        var user = new User(
            $"rollback-{Guid.NewGuid():N}",
            $"rollback-{Guid.NewGuid():N}@example.com",
            "Password1!");
        var board = new Board("Rollback tracker board", ownerId: user.Id);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            user.Id,
            "Rollback tracker proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        var originalBoardToken = board.ConcurrencyToken;

        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            seedDb.AddRange(user, board, proposal);
            await seedDb.SaveChangesAsync();
        }

        using (var mutationScope = _factory.Services.CreateScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var unitOfWork = mutationScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var trackedBoard = await unitOfWork.Boards.GetByIdAsync(board.Id);
            var trackedProposal = await unitOfWork.AutomationProposals.GetByIdAsync(proposal.Id);
            trackedBoard.Should().NotBeNull();
            trackedProposal.Should().NotBeNull();

            await unitOfWork.BeginTransactionAsync();
            trackedBoard!.Archive();
            trackedProposal!.Approve(user.Id);
            await unitOfWork.SaveChangesAsync();

            await unitOfWork.RollbackTransactionAsync();

            db.ChangeTracker.Entries().Should().BeEmpty(
                "rolled-back values and concurrency tokens must not survive into a recovery write");

            var reloadedBoard = await unitOfWork.Boards.GetByIdAsync(board.Id);
            var reloadedProposal = await unitOfWork.AutomationProposals.GetByIdAsync(proposal.Id);
            reloadedBoard.Should().NotBeNull();
            reloadedProposal.Should().NotBeNull();
            reloadedBoard!.IsArchived.Should().BeFalse();
            reloadedBoard.ConcurrencyToken.Should().Be(originalBoardToken);
            reloadedProposal!.Status.Should().Be(ProposalStatus.PendingReview);

            reloadedBoard.RecordDependentMutation();
            reloadedProposal.Reject(user.Id, "Recovery decision");
            await unitOfWork.SaveChangesAsync();

            await unitOfWork.RollbackTransactionAsync();
            db.ChangeTracker.Entries().Should().BeEmpty(
                "the no-active-transaction recovery path must also reset tracked state");
        }

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var storedBoard = await verificationDb.Boards.AsNoTracking()
            .SingleAsync(entity => entity.Id == board.Id);
        var storedProposal = await verificationDb.AutomationProposals.AsNoTracking()
            .SingleAsync(entity => entity.Id == proposal.Id);
        storedBoard.IsArchived.Should().BeFalse();
        storedProposal.Status.Should().Be(ProposalStatus.Rejected);
    }
}
