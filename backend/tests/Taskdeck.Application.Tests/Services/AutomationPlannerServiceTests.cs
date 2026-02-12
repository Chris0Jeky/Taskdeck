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
                dto.Operations != null &&
                dto.Operations.Count == 1 &&
                dto.Operations[0].ActionType == "create" &&
                dto.Operations[0].TargetType == "card"
            ), default), Times.Once);
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
