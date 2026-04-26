using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardHistoryServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly CardHistoryService _service;

    public CardHistoryServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();

        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);

        _service = new CardHistoryService(_unitOfWorkMock.Object);
    }

    #region Validation Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_ShouldReturnValidationError_WhenProposalIdIsEmpty()
    {
        var result = await _service.GetCardHistoryForProposalAsync(Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Proposal ID");
    }

    [Fact]
    public async Task GetCardHistoryForProposalAsync_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        var proposalId = Guid.NewGuid();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposalId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region Empty History Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_ShouldReturnEmptyList_WhenNoOperations()
    {
        var proposal = CreateProposal();
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region Single Card History Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_SingleCard_ShouldReturnMixedHistory()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOperation(cardId, "move");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var auditLogs = new List<AuditLog>
        {
            new AuditLog("Card", cardId, AuditAction.Created, Guid.NewGuid()),
            new AuditLog("Card", cardId, AuditAction.Updated, Guid.NewGuid(), "{\"title\":\"new\"}")
        };
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(auditLogs);

        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        // 2 audit log entries + 1 pending operation = 3 rows
        result.Value.Should().HaveCount(3);

        // Verify that the pending operation is present
        result.Value.Should().Contain(r => r.Status == CardHistoryStatus.Pending);
        // Verify that past audit entries are present
        result.Value.Where(r => r.Status == CardHistoryStatus.Past).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCardHistoryForProposalAsync_ShouldMarkAppliedProposals_AsApplied()
    {
        var cardId = Guid.NewGuid();
        var currentProposal = CreateProposalWithCardOperation(cardId, "update");

        var appliedProposal = CreateProposal();
        appliedProposal.Approve(Guid.NewGuid());
        appliedProposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(currentProposal.Id, default))
            .ReturnsAsync(currentProposal);

        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());

        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync(appliedProposal);

        var result = await _service.GetCardHistoryForProposalAsync(currentProposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Status == CardHistoryStatus.Applied);
    }

    #endregion

    #region Multi-Card Proposal Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_MultiCard_ShouldCombineHistories()
    {
        var cardId1 = Guid.NewGuid();
        var cardId2 = Guid.NewGuid();

        var proposal = CreateProposalWithMultipleCardOperations(cardId1, cardId2);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var logs1 = new List<AuditLog>
        {
            new AuditLog("Card", cardId1, AuditAction.Created, Guid.NewGuid())
        };
        var logs2 = new List<AuditLog>
        {
            new AuditLog("Card", cardId2, AuditAction.Created, Guid.NewGuid()),
            new AuditLog("Card", cardId2, AuditAction.Moved, Guid.NewGuid())
        };

        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId1, 200, default))
            .ReturnsAsync(logs1);
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId2, 200, default))
            .ReturnsAsync(logs2);

        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId1.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId2.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        // 1 + 2 audit logs + 2 pending operations = 5 rows
        result.Value.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetCardHistoryForProposalAsync_MultiCard_ShouldDeduplicateSharedRelatedProposal()
    {
        var cardId1 = Guid.NewGuid();
        var cardId2 = Guid.NewGuid();

        var proposal = CreateProposalWithMultipleCardOperations(cardId1, cardId2);

        // A single related proposal that affected both cards
        var sharedRelatedProposal = CreateProposal();
        sharedRelatedProposal.Approve(Guid.NewGuid());
        sharedRelatedProposal.MarkAsApplied();

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId1, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId2, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());

        // Both cards return the same related proposal
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId1.ToString(), default))
            .ReturnsAsync(sharedRelatedProposal);
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId2.ToString(), default))
            .ReturnsAsync(sharedRelatedProposal);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        // 2 pending operations + 1 related proposal (deduplicated, not 2) = 3 rows
        result.Value.Should().HaveCount(3);
        result.Value.Where(r => r.Status == CardHistoryStatus.Applied).Should().HaveCount(1);
    }

    #endregion

    #region Serial Numbering Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_ShouldAssignSequentialSerials()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOperation(cardId, "move");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var logs = new List<AuditLog>
        {
            new AuditLog("Card", cardId, AuditAction.Created, Guid.NewGuid()),
            new AuditLog("Card", cardId, AuditAction.Updated, Guid.NewGuid())
        };
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(logs);

        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Serial.Should().Be("#001");
        result.Value[1].Serial.Should().Be("#002");
        result.Value[2].Serial.Should().Be("#003");
    }

    [Fact]
    public void FormatSerial_ShouldPadToThreeDigits()
    {
        CardHistoryService.FormatSerial(1).Should().Be("#001");
        CardHistoryService.FormatSerial(9).Should().Be("#009");
        CardHistoryService.FormatSerial(10).Should().Be("#010");
        CardHistoryService.FormatSerial(100).Should().Be("#100");
        CardHistoryService.FormatSerial(999).Should().Be("#999");
    }

    [Fact]
    public void FormatSerial_ShouldHandleFourDigits()
    {
        // Should still work, just wider
        CardHistoryService.FormatSerial(1000).Should().Be("#1000");
    }

    #endregion

    #region Age Formatting Tests

    [Fact]
    public void FormatAge_SameDay_ShouldReturnTimeOnly()
    {
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 25, 11, 42, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("11:42");
    }

    [Fact]
    public void FormatAge_SameDay_MidnightBoundary_ShouldReturnTimeOnly()
    {
        var now = new DateTimeOffset(2026, 4, 25, 0, 5, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 25, 0, 1, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("0:01");
    }

    [Fact]
    public void FormatAge_Yesterday_ShouldReturnYestWithTime()
    {
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 24, 16, 4, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("yest 16:04");
    }

    [Fact]
    public void FormatAge_Yesterday_CrossMidnight_ShouldReturnYest()
    {
        // Now is 00:01 on April 25, timestamp is 23:59 on April 24
        var now = new DateTimeOffset(2026, 4, 25, 0, 1, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 24, 23, 59, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("yest 23:59");
    }

    [Fact]
    public void FormatAge_ThisWeek_ShouldReturnDayAndTime()
    {
        // Thursday Apr 23 seen from Saturday Apr 25 (2 days ago)
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 23, 11, 0, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Contain("11:00");
        // Should contain abbreviated day name (e.g., "Thu")
        result.Should().MatchRegex(@"^[A-Z][a-z]{2} \d{1,2}:\d{2}$");
    }

    [Fact]
    public void FormatAge_SixDaysAgo_ShouldReturnDayAndTime()
    {
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 19, 9, 15, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().MatchRegex(@"^[A-Z][a-z]{2} \d{1,2}:\d{2}$");
    }

    [Fact]
    public void FormatAge_Older_ShouldReturnMonthAndDay()
    {
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("Apr 15");
    }

    [Fact]
    public void FormatAge_MuchOlder_ShouldReturnMonthAndDay()
    {
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("Jan 05");
    }

    [Fact]
    public void FormatAge_NonUtcTimestamp_ShouldConvertToUtc()
    {
        // Timestamp is in UTC+5, should be converted to UTC before formatting
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 25, 16, 42, 0, TimeSpan.FromHours(5));
        // In UTC, this is 11:42

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("11:42");
    }

    [Fact]
    public void FormatAge_SevenDaysAgo_ShouldReturnMonthAndDay()
    {
        // 7 days should be "older" not "this week"
        var now = new DateTimeOffset(2026, 4, 25, 14, 30, 0, TimeSpan.Zero);
        var timestamp = new DateTimeOffset(2026, 4, 18, 11, 0, 0, TimeSpan.Zero);

        var result = CardHistoryService.FormatAge(timestamp, now);

        result.Should().Be("Apr 18");
    }

    #endregion

    #region Status Classification Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_CurrentProposalOps_ShouldBePending()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOperation(cardId, "move");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().OnlyContain(r => r.Status == CardHistoryStatus.Pending);
    }

    [Fact]
    public async Task GetCardHistoryForProposalAsync_AuditEntries_ShouldBePast()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOperation(cardId, "update");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var logs = new List<AuditLog>
        {
            new AuditLog("Card", cardId, AuditAction.Created, Guid.NewGuid())
        };
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(logs);
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Where(r => r.Status == CardHistoryStatus.Past).Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCardHistoryForProposalAsync_RejectedRelatedProposal_ShouldBePast()
    {
        var cardId = Guid.NewGuid();
        var currentProposal = CreateProposalWithCardOperation(cardId, "move");

        var rejectedProposal = CreateProposal();
        rejectedProposal.Reject(Guid.NewGuid(), "Not needed");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(currentProposal.Id, default))
            .ReturnsAsync(currentProposal);
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync(rejectedProposal);

        var result = await _service.GetCardHistoryForProposalAsync(currentProposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Where(r => r.Status == CardHistoryStatus.Past).Should().HaveCount(1);
    }

    #endregion

    #region Non-Card Operations Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_NoCardTargets_ShouldReturnPendingOnly()
    {
        // Proposal that targets columns, not cards
        var proposal = CreateProposal();
        var columnOp = new AutomationProposalOperation(
            proposal.Id, 0, "create_column", "Column", "{}", Guid.NewGuid().ToString());
        proposal.AddOperation(columnOp);

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Status.Should().Be(CardHistoryStatus.Pending);
    }

    #endregion

    #region Self-Referencing Proposal Tests

    [Fact]
    public async Task GetCardHistoryForProposalAsync_SameProposalAsRelated_ShouldNotDuplicate()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOperation(cardId, "move");

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);
        _auditLogRepoMock.Setup(r => r.GetByEntityAsync("Card", cardId, 200, default))
            .ReturnsAsync(Array.Empty<AuditLog>());
        // Related proposal query returns the same proposal
        _proposalRepoMock.Setup(r => r.GetLatestByOperationTargetAsync("card", cardId.ToString(), default))
            .ReturnsAsync(proposal);

        var result = await _service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        // Should only have 1 row (the pending operation), not a duplicate entry
        result.Value.Should().HaveCount(1);
        result.Value[0].Status.Should().Be(CardHistoryStatus.Pending);
    }

    #endregion

    #region Helper Methods

    private static AutomationProposal CreateProposal()
    {
        return new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal summary",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            Guid.NewGuid());
    }

    private static AutomationProposal CreateProposalWithCardOperation(Guid cardId, string actionType)
    {
        var proposal = CreateProposal();
        var op = new AutomationProposalOperation(
            proposal.Id,
            0,
            actionType,
            "Card",
            "{}",
            Guid.NewGuid().ToString(),
            cardId.ToString());
        proposal.AddOperation(op);
        return proposal;
    }

    private static AutomationProposal CreateProposalWithMultipleCardOperations(Guid cardId1, Guid cardId2)
    {
        var proposal = CreateProposal();
        var op1 = new AutomationProposalOperation(
            proposal.Id, 0, "move", "Card", "{}", Guid.NewGuid().ToString(), cardId1.ToString());
        var op2 = new AutomationProposalOperation(
            proposal.Id, 1, "update", "Card", "{}", Guid.NewGuid().ToString(), cardId2.ToString());
        proposal.AddOperation(op1);
        proposal.AddOperation(op2);
        return proposal;
    }

    #endregion
}
