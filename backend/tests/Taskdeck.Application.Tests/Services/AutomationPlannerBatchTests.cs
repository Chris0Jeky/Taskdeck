using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AutomationPlannerBatchTests
{
    private readonly Mock<IAutomationProposalService> _proposalServiceMock;
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly AutomationPlannerService _service;

    public AutomationPlannerBatchTests()
    {
        _proposalServiceMock = new Mock<IAutomationProposalService>();
        _policyEngineMock = new Mock<IAutomationPolicyEngine>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _service = new AutomationPlannerService(
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _unitOfWorkMock.Object);
    }

    private ProposalDto CreateExpectedProposal(Guid userId, Guid boardId)
    {
        return new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "batch",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            "corr1",
            new List<ProposalOperationDto>()
        );
    }

    private void SetupMocksForSuccess(Guid userId, Guid boardId)
    {
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(CreateExpectedProposal(userId, boardId)));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());
    }

    #region ParseBatchInstructionAsync - Validation

    [Fact]
    public async Task ParseBatchInstruction_ShouldReturnFailure_WhenInstructionsListIsEmpty()
    {
        var userId = Guid.NewGuid();

        var result = await _service.ParseBatchInstructionAsync(
            new List<string>(), userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldReturnFailure_WhenUserIdIsEmpty()
    {
        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create card 'test'" }, Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("UserId cannot be empty");
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldReturnFailure_WhenCorrelationIdIsWhitespace()
    {
        var userId = Guid.NewGuid();

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create card 'test'" },
            userId,
            correlationId: "   ");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("CorrelationId cannot be empty when provided");
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldReturnFailure_WhenAllInstructionsAreWhitespace()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "  ", "", "   " },
            userId,
            boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region ParseBatchInstructionAsync - Batch Card Creation

    [Fact]
    public async Task ParseBatchInstruction_ShouldCreateMultipleCardOps_ForBatchCardCreate()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create cards: meeting setup, IT onboarding, HR orientation" },
            userId,
            boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 3 &&
                dto.Operations.All(o => o.ActionType == "create" && o.TargetType == "card") &&
                dto.Operations.Select(o => o.IdempotencyKey).Distinct().Count() == 3),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldCreateMultipleCardOps_ForAddTasksPattern()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "add tasks for onboarding: laptop setup, email creation, building access" },
            userId,
            boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 3),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldTrimTitles_InBatchCardCreate()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create cards:  task one ,  task two  " },
            userId,
            boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 2 &&
                dto.Operations[0].Parameters.Contains("task one") &&
                dto.Operations[1].Parameters.Contains("task two")),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldSkipEmptyTitles_InBatchCardCreate()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create cards: task one, , , task two" },
            userId,
            boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 2),
            default), Times.Once);
    }

    #endregion

    #region ParseBatchInstructionAsync - Multiple Instructions

    [Fact]
    public async Task ParseBatchInstruction_ShouldCombineMultipleSingleInstructions()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var instructions = new List<string>
        {
            "create card 'Task A'",
            "create card 'Task B'",
            "create card 'Task C'"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 3 &&
                dto.Operations.All(o => o.ActionType == "create" && o.TargetType == "card") &&
                dto.Operations[0].Sequence == 0 &&
                dto.Operations[1].Sequence == 1 &&
                dto.Operations[2].Sequence == 2),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldCombineMixedOperationTypes()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();

        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(CreateExpectedProposal(userId, boardId)));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        var instructions = new List<string>
        {
            "create card 'New Task'",
            $"archive card {cardId}"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 2 &&
                dto.Operations[0].ActionType == "create" &&
                dto.Operations[1].ActionType == "archive"),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldPartiallySucceed_WhenSomeInstructionsFail()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var instructions = new List<string>
        {
            "create card 'Valid Task'",
            "this is not a valid instruction"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Summary.Contains("1 instruction(s) could not be parsed")),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldSkipWhitespaceInstructions()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var instructions = new List<string>
        {
            "create card 'Task A'",
            "  ",
            "",
            "create card 'Task B'"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 2),
            default), Times.Once);
    }

    #endregion

    #region ParseBatchInstructionAsync - Batch Size Limit

    [Fact]
    public async Task ParseBatchInstruction_ShouldReturnFailure_WhenExceedingMaxBatchSize()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        // Create 31 cards via batch syntax
        var titles = string.Join(", ", Enumerable.Range(1, 31).Select(i => $"task {i}"));
        var instructions = new List<string> { $"create cards: {titles}" };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("exceeds maximum of 30");
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldSucceed_AtExactlyMaxBatchSize()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        // Create exactly 30 cards
        var titles = string.Join(", ", Enumerable.Range(1, 30).Select(i => $"task {i}"));
        var instructions = new List<string> { $"create cards: {titles}" };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 30),
            default), Times.Once);
    }

    #endregion

    #region ParseBatchInstructionAsync - Idempotency Keys

    [Fact]
    public async Task ParseBatchInstruction_ShouldAssignUniqueIdempotencyKeys_ToEachOperation()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var instructions = new List<string>
        {
            "create cards: task A, task B, task C"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Select(o => o.IdempotencyKey).Distinct().Count() == dto.Operations.Count),
            default), Times.Once);
    }

    #endregion

    #region ParseBatchInstructionAsync - Source Metadata

    [Fact]
    public async Task ParseBatchInstruction_ShouldPassSourceMetadata()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sourceReferenceId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create card 'Test'" },
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Chat,
            sourceReferenceId: sourceReferenceId,
            correlationId: correlationId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.SourceType == ProposalSourceType.Chat &&
                dto.SourceReferenceId == sourceReferenceId &&
                dto.CorrelationId == correlationId),
            default), Times.Once);
    }

    #endregion

    #region ParseBatchInstructionAsync - Summary

    [Fact]
    public async Task ParseBatchInstruction_ShouldGenerateCorrectSummary_ForAllSuccesses()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var instructions = new List<string>
        {
            "create card 'Task A'",
            "create card 'Task B'"
        };

        var result = await _service.ParseBatchInstructionAsync(instructions, userId, boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Summary == "Batch: 2 operations"),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseBatchInstruction_ShouldGenerateCorrectSummary_ForSingleOperation()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        SetupMocksForSuccess(userId, boardId);

        var result = await _service.ParseBatchInstructionAsync(
            new List<string> { "create card 'Solo'" },
            userId,
            boardId);

        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Summary == "Batch: 1 operation"),
            default), Times.Once);
    }

    #endregion

    #region TryParseBatchCardCreateAsync - Direct Tests

    [Theory]
    [InlineData("create cards: task A, task B")]
    [InlineData("Create Cards: task A, task B")]
    [InlineData("add cards: task A, task B")]
    [InlineData("add tasks: task A, task B")]
    [InlineData("create tasks: task A, task B")]
    public async Task TryParseBatchCardCreate_ShouldMatch_VariousPatterns(string instruction)
    {
        var boardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var ops = await _service.TryParseBatchCardCreateAsync(instruction, boardId, default);

        ops.Should().NotBeNull();
        ops!.Count.Should().Be(2);
        ops.All(o => o.ActionType == "create" && o.TargetType == "card").Should().BeTrue();
    }

    [Fact]
    public async Task TryParseBatchCardCreate_ShouldReturnNull_ForNonMatchingInstruction()
    {
        var boardId = Guid.NewGuid();

        var ops = await _service.TryParseBatchCardCreateAsync("create card 'single task'", boardId, default);

        ops.Should().BeNull();
    }

    [Fact]
    public async Task TryParseBatchCardCreate_ShouldReturnNull_WhenNoBoardId()
    {
        var ops = await _service.TryParseBatchCardCreateAsync("create cards: a, b", null, default);

        ops.Should().BeNull();
    }

    [Fact]
    public async Task TryParseBatchCardCreate_ShouldReturnNull_WhenNoColumnsInBoard()
    {
        var boardId = Guid.NewGuid();
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column>());

        var ops = await _service.TryParseBatchCardCreateAsync("create cards: a, b", boardId, default);

        ops.Should().BeNull();
    }

    [Fact]
    public async Task TryParseBatchCardCreate_ShouldSupportForSyntax()
    {
        var boardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var ops = await _service.TryParseBatchCardCreateAsync(
            "create cards for sprint 5: task A, task B, task C",
            boardId, default);

        ops.Should().NotBeNull();
        ops!.Count.Should().Be(3);
    }

    #endregion

    #region TryParseOperationsAsync - Direct Tests

    [Fact]
    public async Task TryParseOperations_ShouldReturnCreateCardOp()
    {
        var boardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var ops = await _service.TryParseOperationsAsync("create card 'My Task'", boardId, default);

        ops.Should().NotBeNull();
        ops!.Count.Should().Be(1);
        ops[0].ActionType.Should().Be("create");
        ops[0].TargetType.Should().Be("card");
    }

    [Fact]
    public async Task TryParseOperations_ShouldReturnNull_ForUnparsableInstruction()
    {
        var boardId = Guid.NewGuid();

        var ops = await _service.TryParseOperationsAsync("do something weird", boardId, default);

        ops.Should().BeNull();
    }

    [Fact]
    public async Task TryParseOperations_ShouldReturnArchiveCardOp()
    {
        var cardId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var ops = await _service.TryParseOperationsAsync($"archive card {cardId}", boardId, default);

        ops.Should().NotBeNull();
        ops!.Count.Should().Be(1);
        ops[0].ActionType.Should().Be("archive");
        ops[0].TargetId.Should().Be(cardId.ToString());
    }

    [Fact]
    public async Task TryParseOperations_ShouldReturnRenameBoardOp()
    {
        var boardId = Guid.NewGuid();

        var ops = await _service.TryParseOperationsAsync("rename board to 'New Name'", boardId, default);

        ops.Should().NotBeNull();
        ops!.Count.Should().Be(1);
        ops[0].ActionType.Should().Be("update");
        ops[0].TargetType.Should().Be("board");
    }

    #endregion

    #region MaxBatchSize Constant

    [Fact]
    public void MaxBatchSize_ShouldBe30()
    {
        AutomationPlannerService.MaxBatchSize.Should().Be(30);
    }

    #endregion
}
