using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

public class ProposalRevisionRaceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalRevisionRaceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SaveChangesAsync_DuplicateProposalRevisionNumber_ThrowsDomainConflict()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var editorId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            editorId,
            "Proposal revision race test",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid());

        db.AutomationProposals.Add(proposal);
        await db.SaveChangesAsync();

        await unitOfWork.ProposalRevisions.AddAsync(new ProposalRevision(
            proposal.Id,
            1,
            editorId,
            """{"operations":[{"id":1}]}""",
            "First edit"));
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.ProposalRevisions.AddAsync(new ProposalRevision(
            proposal.Id,
            1,
            editorId,
            """{"operations":[{"id":2}]}""",
            "Concurrent edit"));

        var act = () => unitOfWork.SaveChangesAsync();

        var assertion = await act.Should().ThrowAsync<DomainException>();
        assertion.Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        assertion.Which.Message.Should().Contain("another session");
    }

    [Fact]
    public async Task GuardedRecoveryRevision_ConcurrentApprovalShouldWinWithoutPersistingRevision()
    {
        var editorId = Guid.NewGuid();
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            editorId,
            "Guarded recovery race test",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid(),
            Guid.NewGuid().ToString());
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters: "{\"title\":\"Original Card\"}",
            idempotencyKey: "capture-recovery-race"));

        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            seedDb.AutomationProposals.Add(proposal);
            await seedDb.SaveChangesAsync();
        }

        using var recoveryScope = _factory.Services.CreateScope();
        var recoveryUnitOfWork = recoveryScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var recoveryProposal = await recoveryUnitOfWork.AutomationProposals.GetByIdAsync(proposal.Id);
        recoveryProposal.Should().NotBeNull();
        recoveryProposal!.GuardPendingRevisionCommit();
        await recoveryUnitOfWork.ProposalRevisions.AddAsync(new ProposalRevision(
            proposal.Id,
            revisionNumber: 1,
            editorUserId: editorId,
            revisedPayload: """{"operations":[{"sequence":0,"actionType":"create","targetType":"card","parameters":"{\"title\":\"Recovered Card\"}","idempotencyKey":"capture-recovery-race"}]}""",
            reason: "interrupted capture recovery"));

        using (var decisionScope = _factory.Services.CreateScope())
        {
            var decisionUnitOfWork = decisionScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var decisionProposal = await decisionUnitOfWork.AutomationProposals.GetByIdAsync(proposal.Id);
            decisionProposal.Should().NotBeNull();
            decisionProposal!.Approve(editorId);
            await decisionUnitOfWork.SaveChangesAsync();
        }

        var saveRecovery = () => recoveryUnitOfWork.SaveChangesAsync();
        var conflict = await saveRecovery.Should().ThrowAsync<DomainException>();
        conflict.Which.ErrorCode.Should().Be(ErrorCodes.Conflict);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationUnitOfWork = verificationScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storedProposal = await verificationUnitOfWork.AutomationProposals.GetByIdAsync(proposal.Id);
        storedProposal.Should().NotBeNull();
        storedProposal!.Status.Should().Be(ProposalStatus.Approved);
        var storedRevisions = await verificationUnitOfWork.ProposalRevisions.GetByProposalIdAsync(proposal.Id);
        storedRevisions.Should().BeEmpty();
    }
}
