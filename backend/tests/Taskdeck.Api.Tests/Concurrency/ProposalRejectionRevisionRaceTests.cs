using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// GH-1453: pause the real application service immediately before its real SQLite save.
/// Constructor timestamps do not establish commit visibility; the shared proposal token must
/// reject the losing write. No sleep, mutation retry, or concurrent factory initialization.
/// </summary>
public sealed class ProposalRejectionRevisionRaceTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private readonly IServiceProvider _services;

    public ProposalRejectionRevisionRaceTests(TestWebApplicationFactory factory) =>
        _services = factory.Services;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectionCommitsFirst_StagedRevisionConflictsAndCannotChangeRejectedContent(
        bool hasExistingRevision)
    {
        var proposal = await SeedAsync(hasExistingRevision);
        using var editing = _services.CreateScope();
        var editorUnit = editing.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var gate = new PausedSave(editorUnit);
        var revisionService = new ProposalRevisionService(
            gate.UnitOfWork, new AutomationPolicyEngine(editorUnit));
        var pendingRevision = revisionService.CreateRevisionAsync(new CreateProposalRevisionDto(
            proposal.Id, proposal.RequestedByUserId, Payload("Losing revision"), "Concurrent edit"));

        try
        {
            await gate.Reached.Task.WaitAsync(Deadline);
            // The revision has been constructed and staged BEFORE rejection, matching GH-1453.
            var db = editing.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var staged = db.ChangeTracker.Entries<ProposalRevision>()
                .Single(entry => entry.State == EntityState.Added).Entity;

            using var deciding = _services.CreateScope();
            var decisionUnit = deciding.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var rejected = await new AutomationProposalService(decisionUnit).RejectProposalAsync(
                proposal.Id, proposal.RequestedByUserId, new UpdateProposalStatusDto("Not needed"));
            rejected.IsSuccess.Should().BeTrue();
            staged.RevisedAt.UtcDateTime.Should().BeOnOrBefore(rejected.Value.DecidedAt!.Value);
            rejected.Value.Operations.Should().ContainSingle()
                .Which.Parameters.Should().Contain(hasExistingRevision ? "Existing revision" : "Original");
        }
        finally
        {
            gate.Release.TrySetResult(true);
            await pendingRevision.WaitAsync(Deadline);
        }

        var lost = await pendingRevision;
        lost.IsSuccess.Should().BeFalse();
        lost.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await VerifyAsync(proposal.Id, ProposalStatus.Rejected, hasExistingRevision ? 1 : 0,
            hasExistingRevision ? "Existing revision" : "Original");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevisionCommitsFirst_StaleRejectionConflictsWithoutChangingDecisionState(
        bool hasExistingRevision)
    {
        var proposal = await SeedAsync(hasExistingRevision);
        using var deciding = _services.CreateScope();
        var decisionUnit = deciding.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var gate = new PausedSave(decisionUnit);
        var service = new AutomationProposalService(
            gate.UnitOfWork, policyEngine: new AutomationPolicyEngine(decisionUnit));
        var pendingRejection = service.RejectProposalAsync(
            proposal.Id, proposal.RequestedByUserId, new UpdateProposalStatusDto("Stale decision"));

        try
        {
            await gate.Reached.Task.WaitAsync(Deadline);
            using var editing = _services.CreateScope();
            var editorUnit = editing.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var revised = await new ProposalRevisionService(
                editorUnit, new AutomationPolicyEngine(editorUnit)).CreateRevisionAsync(
                    new CreateProposalRevisionDto(proposal.Id, proposal.RequestedByUserId,
                        Payload("Winning revision"), "Concurrent edit"));
            revised.IsSuccess.Should().BeTrue();
        }
        finally
        {
            gate.Release.TrySetResult(true);
            await pendingRejection.WaitAsync(Deadline);
        }

        var lost = await pendingRejection;
        lost.IsSuccess.Should().BeFalse();
        lost.ErrorCode.Should().Be(ErrorCodes.Conflict);
        await VerifyAsync(proposal.Id, ProposalStatus.PendingReview, hasExistingRevision ? 2 : 1,
            "Winning revision");
    }

    private async Task<AutomationProposal> SeedAsync(bool hasExistingRevision)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var proposal = new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(),
            "Rejection revision race", RiskLevel.Low, Guid.NewGuid().ToString("N"));
        proposal.AddOperation(new AutomationProposalOperation(proposal.Id, 0, "create", "card",
            "{\"title\":\"Original\"}", "rejection-race"));
        db.AutomationProposals.Add(proposal);
        if (hasExistingRevision)
            db.ProposalRevisions.Add(new ProposalRevision(proposal.Id, 1, proposal.RequestedByUserId,
                Payload("Existing revision"), "Previous edit"));
        await db.SaveChangesAsync();
        return proposal;
    }

    private async Task VerifyAsync(Guid id, ProposalStatus status, int revisionCount, string title)
    {
        // Reopen through an independent scope: tracked objects from either racer are not evidence.
        using var scope = _services.CreateScope();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var stored = await unit.AutomationProposals.GetByIdAsync(id);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(status);
        if (status == ProposalStatus.PendingReview)
        {
            stored.DecidedAt.Should().BeNull();
            stored.DecidedByUserId.Should().BeNull();
            stored.FailureReason.Should().BeNull();
        }
        var revisions = await unit.ProposalRevisions.GetByProposalIdAsync(id);
        revisions.Should().HaveCount(revisionCount);
        var result = await new AutomationProposalService(unit).GetProposalByIdAsync(id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Operations.Should().ContainSingle().Which.Parameters.Should().Contain(title);
    }

    private static string Payload(string title) =>
        "{\"operations\":[{\"sequence\":0,\"actionType\":\"create\",\"targetType\":\"card\"," +
        "\"parameters\":\"{\\\"title\\\":\\\"" + title + "\\\"}\",\"idempotencyKey\":\"rejection-race\"}]}";

    private sealed class PausedSave
    {
        public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IUnitOfWork UnitOfWork { get; }

        public PausedSave(IUnitOfWork inner)
        {
            var mock = new Mock<IUnitOfWork>(MockBehavior.Strict);
            mock.SetupGet(unit => unit.AutomationProposals).Returns(inner.AutomationProposals);
            mock.SetupGet(unit => unit.ProposalRevisions).Returns(inner.ProposalRevisions);
            mock.SetupGet(unit => unit.Users).Returns(inner.Users);
            mock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    Reached.TrySetResult(true);
                    await Release.Task.WaitAsync(Deadline, token);
                    return await inner.SaveChangesAsync(token);
                });
            UnitOfWork = mock.Object;
        }
    }
}
