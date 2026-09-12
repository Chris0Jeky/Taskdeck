using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SideEffectAnalyzerLifecycleDisclosureTests
{
    [Theory]
    [InlineData("restore-lifecycle", "card")]
    [InlineData("RESTORE-LIFECYCLE", "card")]
    [InlineData("Restore-Lifecycle", "CARD")]
    public async Task RestoreOnly_DisclosesRestorationWithCaseInsensitiveClassification(string action, string target)
    {
        var result = await Analyze((action, target));
        var cards = result.Rows.Single(row => row.Key == "Cards");
        cards.Value.Should().Be("Restores cards on the board");
        cards.Tone.Should().Be("active");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedArchiveAndRestore_DisclosesRestorationWithOrWithoutColumns(bool addColumn)
    {
        var operations = new List<(string Action, string Target)>
        {
            ("archive-lifecycle", "card"), ("restore-lifecycle", "card")
        };
        if (addColumn) operations.Add(("create", "column"));
        var result = await Analyze(operations.ToArray());
        result.Rows.Single(row => row.Key == "Cards").Value.Should().Be(addColumn
            ? "Archives and restores cards and adds columns on the board"
            : "Archives and restores cards on the board");
    }

    [Theory]
    [InlineData("create", "Creates")]
    [InlineData("move", "Moves")]
    [InlineData("archive", "Blocks")]
    [InlineData("archive-lifecycle", "Archives")]
    public async Task WithoutRestore_DisclosesOnlyTheActualCardAndColumnEffects(string action, string verb)
    {
        var cardsOnly = await Analyze((action, "card"));
        var withColumn = await Analyze((action, "card"), ("create", "column"));
        cardsOnly.Rows.Single(row => row.Key == "Cards").Value.Should().Be($"{verb} cards on the board");
        withColumn.Rows.Single(row => row.Key == "Cards").Value.Should().Be($"{verb} cards and adds columns on the board");
    }

    [Fact]
    public async Task NonCardRestore_DoesNotClaimCardsWillBeRestored()
    {
        var result = await Analyze(("create", "card"), ("restore-lifecycle", "artefact"));
        result.Rows.Single(row => row.Key == "Cards").Value.Should().Be("Creates cards on the board");
    }

    [Fact]
    public async Task NoOperations_PreservesThePassiveCardsRow()
    {
        var result = await Analyze();
        var cards = result.Rows.Single(row => row.Key == "Cards");
        cards.Value.Should().Be("No board mutations");
        cards.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task PersistedAndEffectiveSnapshotPaths_ExposeTheSameLifecycleDisclosure()
    {
        var proposal = new AutomationProposal(ProposalSourceType.Chat, Guid.NewGuid(), "Restore a card",
            RiskLevel.Low, Guid.NewGuid().ToString());
        proposal.AddOperation(new AutomationProposalOperation(proposal.Id, 0, "restore-lifecycle", "card",
            "{}", Guid.NewGuid().ToString()));
        var repository = new Mock<IAutomationProposalRepository>();
        repository.Setup(r => r.GetByIdAsync(proposal.Id, default)).ReturnsAsync(proposal);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.AutomationProposals).Returns(repository.Object);
        var analyzer = new SideEffectAnalyzer(unitOfWork.Object);

        var persisted = await analyzer.AnalyzeAsync(proposal.Id);
        var effective = await analyzer.AnalyzeAsync(EffectiveProposal(("restore-lifecycle", "card")));

        persisted.IsSuccess.Should().BeTrue();
        effective.IsSuccess.Should().BeTrue();
        persisted.Value.Should().BeEquivalentTo(effective.Value);
        persisted.Value.Rows.Should().HaveCount(7);
        persisted.Value.Rows.Single(row => row.Key == "Cards").Value.Should().Be("Restores cards on the board");
        repository.Verify(r => r.GetByIdAsync(proposal.Id, default), Times.Once);
    }

    private static async Task<ProposalSideEffectsDto> Analyze(params (string Action, string Target)[] operations)
    {
        var analyzer = new SideEffectAnalyzer(new Mock<IUnitOfWork>().Object);
        var result = await analyzer.AnalyzeAsync(EffectiveProposal(operations));
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static ProposalDto EffectiveProposal(params (string Action, string Target)[] operations) => new(
        Guid.NewGuid(), ProposalSourceType.Chat, null, null, Guid.NewGuid(), ProposalStatus.PendingReview,
        RiskLevel.Low, "Lifecycle disclosure", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        DateTime.UtcNow.AddDays(1), null, null, null, null, Guid.NewGuid().ToString(),
        operations.Select((operation, index) => new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), index, operation.Action, operation.Target, null,
            "{}", Guid.NewGuid().ToString(), null)).ToList());
}
