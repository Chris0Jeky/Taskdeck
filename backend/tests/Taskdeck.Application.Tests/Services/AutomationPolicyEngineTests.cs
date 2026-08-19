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

public class AutomationPolicyEngineTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly AutomationPolicyEngine _engine;

    public AutomationPolicyEngineTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _engine = new AutomationPolicyEngine(_unitOfWorkMock.Object);
    }

    #region ClassifyRisk Tests

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_ForEmptyOperations()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnLow_ForSimpleCardCreate()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Low);
    }

    [Theory]
    [InlineData("update", "{\"dueDate\":\"2026-07-14T00:00:00+00:00\"}")]
    [InlineData("add-label", "{\"labelName\":\"urgent\"}")]
    [InlineData("remove-label", "{\"labelName\":\"urgent\"}")]
    public void ClassifyRisk_ShouldReturnLow_ForReversibleCardMetadataChanges(string actionType, string parameters)
    {
        var operations = new List<ProposalOperationDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 0, actionType, "card", Guid.NewGuid().ToString(), parameters, "key1", null)
        };

        _engine.ClassifyRisk(operations).Should().Be(RiskLevel.Low,
            "due-date and label metadata changes follow the existing reversible card-update category pending #1307");
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnMedium_ForArchiveOperation()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "archive", "card", "card1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnMedium_ForManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 7)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Medium);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnHigh_ForDeleteOperation()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "card", "card1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnHigh_ForBoardUpdate()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", "board1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_ForBoardDelete()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "board", "board1", "{}", "key1", null)
        };

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void ClassifyRisk_ShouldReturnCritical_ForManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 25)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        // Act
        var risk = _engine.ClassifyRisk(operations);

        // Assert
        risk.Should().Be(RiskLevel.Critical);
    }

    #endregion

    #region ValidatePermissions Tests

    [Fact]
    public async Task ValidatePermissions_ShouldReturnSuccess_ForValidUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, null, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public async Task ValidatePermissions_ShouldReturnValidationError_ForNonObjectParameters(string parameters)
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, parameters, "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);

        var result = await _engine.ValidatePermissionsAsync(userId, null, operations, BoardAccessBar.Write);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("JSON object");
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForInvalidUserId()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>();

        // Act
        var result = await _engine.ValidatePermissionsAsync(Guid.Empty, null, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForNonexistentUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, null, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForNonexistentBoard()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnFailure_ForUnauthorizedBoardAccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(false);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    // #1426: the empty-operations case must run the FULL requester/board access gate — only the
    // per-operation contract checks are operation-dependent. The three tests below pin that an
    // operation-less proposal is gated on board existence and board access exactly like an
    // operation-bearing one; previously the empty-list short-circuit returned Success with the
    // board half skipped (the trap that forced every new consumer to add a manual fallback).

    [Fact]
    public async Task ValidatePermissions_ShouldReturnNotFound_ForEmptyOperations_WhenBoardMissing()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync((Board?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnValidationError_ForNullOperations()
    {
        // Arrange: a null operations argument is guarded before any work, returning the method's
        // own ValidationError rather than throwing ArgumentNullException from ToList().
        var userId = Guid.NewGuid();

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, null, null!, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnNotFound_ForEmptyOperations_WhenRequesterMissing()
    {
        // Arrange: the requester-existence half of the gate must also run for an empty op list
        // with a set boardId (the board-scoped path), not just the null-board path.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnForbidden_ForEmptyOperations_WhenBoardAccessDenied()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(false);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnSuccess_ForEmptyOperations_WhenBoardAccessGranted()
    {
        // Arrange: empty op list with a board the requester can access — the access gate passes
        // and the per-operation contract validator is (correctly) skipped, so the result is Success.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(true);

        // Act
        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region ValidateBoardAccess read/write bar Tests (#1836)

    // #1836: the board half of the gate takes an explicit BoardAccessBar because one gate serves
    // two lanes. BoardAccessBar.Write mirrors the API-side #1794/#1827 write bar for the MUTATION
    // lanes (proposal creation, approve, execute), so read-capable-but-not-write-capable
    // membership (Viewer) is refused at validation rather than at execute. BoardAccessBar.Read
    // keeps plain membership for the READ lanes (pending diff, terminal stored preview behind MCP
    // proposal_detail, #1415) so a member demoted to Viewer can still read their own proposals.

    [Fact]
    public async Task ValidateBoardAccess_ShouldRequireWriteCapableMembership_NotMereReadAccess()
    {
        // Arrange: a Viewer — HasAccessAsync says yes for "any role", no for "Editor or better".
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("viewer", "viewer@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, null, default))
            .ReturnsAsync(true);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default))
            .ReturnsAsync(false);

        // Act
        var result = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write);

        // Assert — an explicit Forbidden outcome, never an exception (the LLM-lane convention).
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("write access");
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldQueryWithEditorAsMinimumRole_UnderWriteBar()
    {
        // Pins the minimum role actually sent to the repository: UserRole.Editor is the exact
        // membership set BoardAccess.CanWrite() admits, and HasAccessAsync short-circuits the owner
        // separately. A `null` minimum role here would silently re-open the Viewer lane.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("editor", "editor@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(true);

        var result = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write);

        result.IsSuccess.Should().BeTrue();
        _boardAccessRepoMock.Verify(
            r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default),
            Times.Once);
        _boardAccessRepoMock.Verify(
            r => r.HasAccessAsync(boardId, userId, null, default),
            Times.Never);
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldQueryWithNullMinimumRole_UnderReadBar()
    {
        // The mirror image of the write-bar pin: the Read bar must send `null` (any membership),
        // the literal pre-#1836 argument. Sending UserRole.Editor here is exactly the regression
        // that cost a demoted-to-Viewer member their own proposals' detail (#1836 amendment).
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("viewer", "viewer@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(true);

        var result = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Read);

        result.IsSuccess.Should().BeTrue();
        _boardAccessRepoMock.Verify(
            r => r.HasAccessAsync(boardId, userId, null, default),
            Times.Once);
        _boardAccessRepoMock.Verify(
            r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default),
            Times.Never);
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldAdmitReadOnlyMember_UnderReadBar()
    {
        // THE regression this split exists to prevent: a Viewer — write-denied, membership-allowed
        // — reading a board-scoped proposal gate. Under the Read bar they are admitted.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("viewer", "viewer@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, null, default))
            .ReturnsAsync(true);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default))
            .ReturnsAsync(false);

        var readResult = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Read);
        var writeResult = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write);

        // Same user, same board, same fixture — the bar is the only difference.
        readResult.IsSuccess.Should().BeTrue();
        writeResult.IsSuccess.Should().BeFalse();
        writeResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldDenyNonMember_UnderReadBar()
    {
        // The Read bar is membership, not "anyone": a user with no BoardAccess row at all is
        // still Forbidden, with the pre-#1836 message the MCP/HTTP surfaces already assert on.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("stranger", "stranger@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), default))
            .ReturnsAsync(false);

        var result = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Read);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be($"User does not have access to board {boardId}");
        result.ErrorMessage.Should().NotContain("write access");
    }

    [Fact]
    public async Task ValidatePermissions_ShouldAdmitReadOnlyMember_UnderReadBar()
    {
        // The composed gate must carry the bar through, since the pending-diff read path calls
        // THIS method (not ValidateBoardAccessAsync) at BoardAccessBar.Read.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("viewer", "viewer@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", boardId.ToString(), $"{{\"name\":\"Renamed\",\"boardId\":\"{boardId}\"}}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, null, default))
            .ReturnsAsync(true);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default))
            .ReturnsAsync(false);

        var readResult = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Read);
        var writeResult = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        readResult.IsSuccess.Should().BeTrue();
        writeResult.IsSuccess.Should().BeFalse();
        writeResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidatePermissions_ShouldReturnForbidden_ForReadOnlyMember()
    {
        // The mirror has to hold through the composed gate too, which is what the approve/apply
        // and creation chains actually call.
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("viewer", "viewer@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, null, default))
            .ReturnsAsync(true);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default))
            .ReturnsAsync(false);

        var result = await _engine.ValidatePermissionsAsync(userId, boardId, operations, BoardAccessBar.Write);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldSucceed_ForWriteCapableMember()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("editor", "editor@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _boardAccessRepoMock
            .Setup(r => r.HasAccessAsync(boardId, userId, Taskdeck.Domain.Enums.UserRole.Editor, default))
            .ReturnsAsync(true);

        var result = await _engine.ValidateBoardAccessAsync(userId, boardId, BoardAccessBar.Write);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBoardAccess_ShouldSkipBoardGate_WhenBoardIdIsNull()
    {
        // Unchanged by either bar: a board-less requester check touches no board repository at all.
        var userId = Guid.NewGuid();
        var user = new User("someone", "someone@example.com", "hashedPassword");

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);

        var result = await _engine.ValidateBoardAccessAsync(userId, null, BoardAccessBar.Write);

        result.IsSuccess.Should().BeTrue();
        _boardAccessRepoMock.Verify(
            r => r.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Taskdeck.Domain.Enums.UserRole?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ValidatePolicy Tests

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForNullProposal()
    {
        // Act
        var result = _engine.ValidatePolicy(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForEmptyOperations()
    {
        // Arrange
        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
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

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForTooManyOperations()
    {
        // Arrange
        var operations = Enumerable.Range(0, 51)
            .Select(i => new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), i, "create", "card", null, "{}", $"key{i}", null))
            .ToList();

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum operation count");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForDuplicateSequences()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null),
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key2", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("sequences must be unique");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnFailure_ForExpiredProposal()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(-1), // Expired
            null,
            null,
            null,
            null,
            "corr1",
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public void ValidatePolicy_ShouldReturnSuccess_ForValidProposal()
    {
        // Arrange
        var operations = new List<ProposalOperationDto>
        {
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", "key1", null),
            new ProposalOperationDto(Guid.NewGuid(), Guid.NewGuid(), 1, "update", "card", "card1", "{}", "key2", null)
        };

        var proposal = new ProposalDto(
            Guid.NewGuid(),
            ProposalSourceType.Manual,
            null,
            null,
            Guid.NewGuid(),
            ProposalStatus.PendingReview,
            RiskLevel.Low,
            "Test",
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
            operations
        );

        // Act
        var result = _engine.ValidatePolicy(proposal);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
