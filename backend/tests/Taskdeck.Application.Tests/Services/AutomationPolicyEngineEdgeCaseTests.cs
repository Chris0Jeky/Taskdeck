using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge-case tests for AutomationPolicyEngine covering policy validation,
/// expiry enforcement during execution, and empty-policy defaults.
/// Addresses issue #708 (TST-41).
/// </summary>
public class AutomationPolicyEngineEdgeCaseTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly AutomationPolicyEngine _engine;

    public AutomationPolicyEngineEdgeCaseTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _engine = new AutomationPolicyEngine(_unitOfWorkMock.Object);
    }

    #region ValidatePolicy — Expiry

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenProposalHasExpired()
    {
        var proposal = CreateProposalDto(
            expiresAt: DateTime.UtcNow.AddMinutes(-10),
            operations: new List<ProposalOperationDto> { CreateOperationDto() });

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public void ValidatePolicy_ShouldSucceed_WhenProposalHasNotExpired()
    {
        var proposal = CreateProposalDto(
            expiresAt: DateTime.UtcNow.AddMinutes(60),
            operations: new List<ProposalOperationDto> { CreateOperationDto() });

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region ValidatePolicy — Operation Limits

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenOperationCountExceedsMax()
    {
        var operations = Enumerable.Range(0, 51)
            .Select(i => CreateOperationDto(sequence: i))
            .ToList();
        var proposal = CreateProposalDto(operations: operations);

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum operation count");
    }

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenNoOperations()
    {
        var proposal = CreateProposalDto(operations: new List<ProposalOperationDto>());

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least one operation");
    }

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenOperationSequencesAreDuplicated()
    {
        var operations = new List<ProposalOperationDto>
        {
            CreateOperationDto(sequence: 0),
            CreateOperationDto(sequence: 0)
        };
        var proposal = CreateProposalDto(operations: operations);

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unique");
    }

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenOperationSequenceIsNegative()
    {
        var operations = new List<ProposalOperationDto>
        {
            CreateOperationDto(sequence: -1)
        };
        var proposal = CreateProposalDto(operations: operations);

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-negative");
    }

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenParametersExceedMaxLength()
    {
        var longParams = new string('x', 10001);
        var operations = new List<ProposalOperationDto>
        {
            CreateOperationDto(parameters: longParams)
        };
        var proposal = CreateProposalDto(operations: operations);

        var result = _engine.ValidatePolicy(proposal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum length");
    }

    #endregion

    #region ClassifyRisk — Default Behavior

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_WhenNoOperations()
    {
        // GP-06: empty policy defaults to review-first (low risk = still requires review)
        var risk = _engine.ClassifyRisk(Enumerable.Empty<ProposalOperationDto>());

        risk.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_WhenDeleteBoard()
    {
        var operations = new[]
        {
            CreateOperationDto(actionType: "delete", targetType: "board")
        };

        var risk = _engine.ClassifyRisk(operations);

        risk.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_WhenManyOperations()
    {
        var operations = Enumerable.Range(0, 21)
            .Select(i => CreateOperationDto(sequence: i, actionType: "create", targetType: "card"))
            .ToList();

        var risk = _engine.ClassifyRisk(operations);

        risk.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnHigh_WhenDeleteCard()
    {
        var operations = new[]
        {
            CreateOperationDto(actionType: "delete", targetType: "card")
        };

        var risk = _engine.ClassifyRisk(operations);

        risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnMedium_WhenArchiveOperation()
    {
        var operations = new[]
        {
            CreateOperationDto(actionType: "archive", targetType: "card")
        };

        var risk = _engine.ClassifyRisk(operations);

        risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_ForSimpleCreate()
    {
        var operations = new[]
        {
            CreateOperationDto(actionType: "create", targetType: "card")
        };

        var risk = _engine.ClassifyRisk(operations);

        risk.Should().Be(RiskLevel.Low);
    }

    #endregion

    #region ValidatePolicy — Null Proposal

    [Fact]
    public void ValidatePolicy_ShouldFail_WhenProposalIsNull()
    {
        var result = _engine.ValidatePolicy(null!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null");
    }

    #endregion

    #region Helpers

    private static ProposalDto CreateProposalDto(
        DateTime? expiresAt = null,
        List<ProposalOperationDto>? operations = null)
    {
        return new ProposalDto(
            Id: Guid.NewGuid(),
            SourceType: ProposalSourceType.Queue,
            SourceReferenceId: null,
            BoardId: Guid.NewGuid(),
            RequestedByUserId: Guid.NewGuid(),
            Status: ProposalStatus.Approved,
            RiskLevel: RiskLevel.Low,
            Summary: "Test proposal",
            DiffPreview: null,
            ValidationIssues: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            ExpiresAt: expiresAt ?? DateTime.UtcNow.AddMinutes(60),
            DecidedAt: DateTime.UtcNow.AddMinutes(-5),
            DecidedByUserId: Guid.NewGuid(),
            AppliedAt: null,
            FailureReason: null,
            CorrelationId: Guid.NewGuid().ToString(),
            Operations: operations ?? new List<ProposalOperationDto> { CreateOperationDto() }
        );
    }

    private static ProposalOperationDto CreateOperationDto(
        int sequence = 0,
        string actionType = "create",
        string targetType = "card",
        string parameters = "{\"title\":\"Test\"}")
    {
        return new ProposalOperationDto(
            Id: Guid.NewGuid(),
            ProposalId: Guid.NewGuid(),
            Sequence: sequence,
            ActionType: actionType,
            TargetType: targetType,
            TargetId: null,
            Parameters: parameters,
            IdempotencyKey: Guid.NewGuid().ToString(),
            ExpectedVersion: null
        );
    }

    #endregion
}
