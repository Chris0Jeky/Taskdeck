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

public class AutomationPlannerServiceTests
{
    private readonly Mock<IAutomationProposalService> _proposalServiceMock;
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly AutomationPlannerService _service;

    public AutomationPlannerServiceTests()
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

    #region ParseInstruction Tests

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForEmptyInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync("", userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForEmptyUserId()
    {
        // Act
        var result = await _service.ParseInstructionAsync("create card 'test'", Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForCreateCardInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "create card 'Test Task'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync("create card 'Test Task'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => 
                dto.RequestedByUserId == userId && 
                dto.BoardId == boardId &&
                dto.SourceType == ProposalSourceType.Manual &&
                dto.SourceReferenceId == null &&
                !string.IsNullOrWhiteSpace(dto.CorrelationId) &&
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "create" &&
                dto.Operations[0].TargetType == "card"
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldUseProvidedSourceMetadata_WhenSpecified()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sourceReferenceId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Queue,
            sourceReferenceId,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "create card 'Queue Task'",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            null,
            null,
            null,
            null,
            correlationId,
            new List<ProposalOperationDto>());

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync(
            "create card 'Queue Task'",
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Queue,
            sourceReferenceId: sourceReferenceId,
            correlationId: correlationId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.SourceType == ProposalSourceType.Queue &&
                dto.SourceReferenceId == sourceReferenceId &&
                dto.CorrelationId == correlationId &&
                dto.RequestedByUserId == userId &&
                dto.BoardId == boardId),
            default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_WhenCorrelationIdExceedsMaxLength()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var correlationId = new string('c', 101);

        // Act
        var result = await _service.ParseInstructionAsync(
            "create card 'Queue Task'",
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Queue,
            sourceReferenceId: Guid.NewGuid().ToString(),
            correlationId: correlationId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("CorrelationId cannot exceed 100 characters");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_WhenCorrelationIdIsWhitespace()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync(
            "create card 'Queue Task'",
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Queue,
            sourceReferenceId: Guid.NewGuid().ToString(),
            correlationId: "   ");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("CorrelationId cannot be empty when provided");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_WhenSourceReferenceIdIsWhitespace()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync(
            "create card 'Queue Task'",
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Queue,
            sourceReferenceId: "   ",
            correlationId: Guid.NewGuid().ToString());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("SourceReferenceId cannot be empty when provided");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_WhenSourceReferenceIdExceedsMaxLength()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var sourceReferenceId = new string('s', 101);

        // Act
        var result = await _service.ParseInstructionAsync(
            "create card 'Queue Task'",
            userId,
            boardId,
            default,
            sourceType: ProposalSourceType.Queue,
            sourceReferenceId: sourceReferenceId,
            correlationId: Guid.NewGuid().ToString());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("SourceReferenceId cannot exceed 100 characters");
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default), Times.Never);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForCreateCardWithColumnName()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "In Progress", 1);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "create card 'Task' in column 'In Progress'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync("create card 'Task' in column 'In Progress'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForCreateCardWithoutBoardId()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync("create card 'Test Task'", userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Board ID is required");
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForMoveCardInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "Done", 2);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            $"move card {cardId} to column 'Done'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync($"move card {cardId} to column 'Done'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => 
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "move" &&
                dto.Operations[0].TargetType == "card"
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForArchiveCardInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Medium,
            $"archive card {cardId}",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Medium);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync($"archive card {cardId}", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => 
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "archive"
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForArchiveCardsMatchingInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var card1 = TestDataBuilder.CreateCard(boardId, Guid.NewGuid(), "Old Task 1");
        var card2 = TestDataBuilder.CreateCard(boardId, Guid.NewGuid(), "Old Task 2");
        var card3 = TestDataBuilder.CreateCard(boardId, Guid.NewGuid(), "New Task");

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Card> { card1, card2, card3 });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Medium,
            "archive cards matching 'Old'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Medium);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync("archive cards matching 'Old'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => 
                dto.Operations != null &&
                dto.Operations.Count == 2 // Only card1 and card2 match
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForUpdateCardTitleInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            $"update card {cardId} title 'New Title'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync($"update card {cardId} title 'New Title'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => 
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "update"
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForRenameBoardInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "rename board to 'Renamed Board'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync("rename board to 'Renamed Board'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "update" &&
                dto.Operations[0].TargetType == "board" &&
                dto.Operations[0].TargetId == boardId.ToString() &&
                dto.Operations[0].Parameters.Contains("\"name\":\"Renamed Board\"")
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForRenameBoardWithoutBoardId()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync("rename board to 'Renamed Board'", userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Board ID is required for board operations");
    }

    [Fact]
    public async Task ParseInstruction_ShouldCreateProposal_ForMoveColumnInstruction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var backlog = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);
        var inProgress = TestDataBuilder.CreateColumn(boardId, "In Progress", 1);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { backlog, inProgress });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "move column 'In Progress' to position 0",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ParseInstructionAsync("move column 'In Progress' to position 0", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _proposalServiceMock.Verify(s => s.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto =>
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "reorder" &&
                dto.Operations[0].TargetType == "column" &&
                dto.Operations[0].TargetId == inProgress.Id.ToString()
            ), default), Times.Once);
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForMoveColumnOutOfRangePosition()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var backlog = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);
        var inProgress = TestDataBuilder.CreateColumn(boardId, "In Progress", 1);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { backlog, inProgress });

        // Act
        var result = await _service.ParseInstructionAsync("move column 'In Progress' to position 4", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Allowed range is 0 to 1");
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_ForUnrecognizedPattern()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        // Act
        var result = await _service.ParseInstructionAsync("do something random", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Could not parse instruction");
    }

    [Fact]
    public async Task ParseInstruction_ShouldReturnFailure_WhenPermissionValidationFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var column = TestDataBuilder.CreateColumn(boardId, "To Do", 0);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column> { column });

        var expectedProposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "create card 'Test'",
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

        _policyEngineMock.Setup(e => e.ClassifyRisk(It.IsAny<IEnumerable<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
        _proposalServiceMock.Setup(s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), default))
            .ReturnsAsync(Result.Success(expectedProposal));
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(userId, boardId, It.IsAny<IEnumerable<ProposalOperationDto>>(), default))
            .ReturnsAsync(Result.Failure(ErrorCodes.Forbidden, "No access"));

        // Act
        var result = await _service.ParseInstructionAsync("create card 'Test'", userId, boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    #endregion
}
