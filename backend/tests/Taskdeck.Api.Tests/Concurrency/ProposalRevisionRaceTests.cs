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
}
