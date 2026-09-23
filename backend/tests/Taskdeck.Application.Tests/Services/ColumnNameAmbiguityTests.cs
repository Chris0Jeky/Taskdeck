using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Regression tests for #3427: boards may contain duplicate column names
/// (names are not a domain uniqueness invariant), so every name-resolution
/// site must reject ambiguous names instead of silently targeting the first match.
/// </summary>
public class ColumnNameAmbiguityTests
{
    private readonly Mock<IAutomationProposalService> _proposalService = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngine = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IColumnRepository> _columnRepo = new();
    private readonly Mock<ICardRepository> _cardRepo = new();
    private readonly Mock<IBoardRepository> _boardRepo = new();
    private readonly Mock<ILabelRepository> _labelRepo = new();

    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ColumnNameAmbiguityTests()
    {
        _unitOfWork.Setup(u => u.Columns).Returns(_columnRepo.Object);
        _unitOfWork.Setup(u => u.Cards).Returns(_cardRepo.Object);
        _unitOfWork.Setup(u => u.Boards).Returns(_boardRepo.Object);
        _unitOfWork.Setup(u => u.Labels).Returns(_labelRepo.Object);

        _policyEngine.Setup(p => p.ClassifyRisk(It.IsAny<IReadOnlyList<ProposalOperationDto>>()))
            .Returns(RiskLevel.Low);
    }

    private ToolExecutionContext MakeContext() => new(_boardId, _userId);

    private void SetupColumns(params Column[] columns)
    {
        _columnRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns);
        foreach (var column in columns)
        {
            _columnRepo.Setup(r => r.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(column);
        }
    }

    private static JsonElement ParseArgs(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string ErrorOf(string result)
    {
        using var doc = JsonDocument.Parse(result);
        doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue(result);
        return error.GetString() ?? "";
    }

    private void SetupProposalCreation()
    {
        var proposalId = Guid.NewGuid();
        _proposalService.Setup(p => p.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProposalDto(
                proposalId, ProposalSourceType.Chat, null, _boardId, _userId,
                ProposalStatus.PendingReview, RiskLevel.Low, "Test proposal",
                null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                DateTime.UtcNow.AddDays(1), null, null,
                null, null, Guid.NewGuid().ToString(),
                new List<ProposalOperationDto>())));
    }

    #region Chat write tools

    [Fact]
    public async Task ProposeCreateCard_AmbiguousColumn_ReturnsErrorWithoutProposal()
    {
        SetupColumns(
            TestDataBuilder.CreateColumn(_boardId, "Backlog", 0),
            TestDataBuilder.CreateColumn(_boardId, "Backlog", 1));
        SetupProposalCreation();
        var executor = new ProposeCreateCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(MakeContext(), ParseArgs("""{"title":"Dup card","column_name":"backlog"}"""));

        ErrorOf(result).Should().Contain("mbiguous");
        _proposalService.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeMoveCard_AmbiguousTargetColumn_ReturnsErrorWithoutProposal()
    {
        var sourceColumn = TestDataBuilder.CreateColumn(_boardId, "Source", 0);
        var card = new Card(_boardId, sourceColumn.Id, "Movable");
        SetupColumns(
            sourceColumn,
            TestDataBuilder.CreateColumn(_boardId, "Target", 1),
            TestDataBuilder.CreateColumn(_boardId, "Target", 2));
        _cardRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([card]);
        SetupProposalCreation();
        var executor = new ProposeMoveCardExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(
            MakeContext(),
            ParseArgs($$"""{"card_id":"{{BoardContextBuilder.FormatShortId(card.Id)}}","target_column":"target"}"""));

        ErrorOf(result).Should().Contain("mbiguous");
        _proposalService.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeBulkMove_AmbiguousSourceColumn_ReturnsErrorWithoutProposal()
    {
        SetupColumns(
            TestDataBuilder.CreateColumn(_boardId, "Source", 0),
            TestDataBuilder.CreateColumn(_boardId, "Source", 1),
            TestDataBuilder.CreateColumn(_boardId, "Target", 2));
        _cardRepo.Setup(r => r.GetByColumnIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupProposalCreation();
        var executor = new ProposeBulkMoveExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(
            MakeContext(),
            ParseArgs("""{"source_column":"source","target_column":"target"}"""));

        ErrorOf(result).Should().Contain("mbiguous");
        _proposalService.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposeBulkMove_AmbiguousTargetColumn_ReturnsErrorWithoutProposal()
    {
        SetupColumns(
            TestDataBuilder.CreateColumn(_boardId, "Source", 0),
            TestDataBuilder.CreateColumn(_boardId, "Target", 1),
            TestDataBuilder.CreateColumn(_boardId, "Target", 2));
        _cardRepo.Setup(r => r.GetByColumnIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupProposalCreation();
        var executor = new ProposeBulkMoveExecutor(_proposalService.Object, _policyEngine.Object, _unitOfWork.Object);

        var result = await executor.ExecuteAsync(
            MakeContext(),
            ParseArgs("""{"source_column":"source","target_column":"target"}"""));

        ErrorOf(result).Should().Contain("mbiguous");
        _proposalService.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Chat read tools

    [Fact]
    public async Task ListCardsInColumn_AmbiguousColumn_ReturnsError()
    {
        SetupColumns(
            TestDataBuilder.CreateColumn(_boardId, "Backlog", 0),
            TestDataBuilder.CreateColumn(_boardId, "Backlog", 1));
        _labelRepo.Setup(r => r.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _cardRepo.Setup(r => r.GetByColumnIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var executor = new ListCardsInColumnExecutor(_unitOfWork.Object);

        var result = await executor.ExecuteAsync(_boardId, ParseArgs("""{"column_name":"backlog"}"""));

        ErrorOf(result).Should().Contain("mbiguous");
    }

    #endregion

    #region Automation planner

    [Fact]
    public async Task AutomationPlanner_AmbiguousColumn_ReturnsValidationErrorWithoutProposal()
    {
        var user = new User("planner", "planner@example.com", "hashedPassword");
        var board = TestDataBuilder.CreateBoard();
        var first = TestDataBuilder.CreateColumn(board.Id, "Dup", 0);
        var second = TestDataBuilder.CreateColumn(board.Id, "Dup", 1);
        var users = new Mock<IUserRepository>();
        var boards = new Mock<IBoardRepository>();
        var access = new Mock<IBoardAccessRepository>();
        _unitOfWork.Setup(u => u.Users).Returns(users.Object);
        _unitOfWork.Setup(u => u.Boards).Returns(boards.Object);
        _unitOfWork.Setup(u => u.BoardAccesses).Returns(access.Object);
        users.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        boards.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        access.Setup(r => r.HasAccessAsync(board.Id, user.Id, It.IsAny<UserRole?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _columnRepo.Setup(r => r.GetByBoardIdAsync(board.Id, default)).ReturnsAsync([first, second]);
        _columnRepo.Setup(r => r.GetByIdAsync(first.Id, default)).ReturnsAsync(first);
        _columnRepo.Setup(r => r.GetByIdAsync(second.Id, default)).ReturnsAsync(second);
        _columnRepo.Setup(r => r.GetByIdWithCardsAsync(first.Id, default)).ReturnsAsync(first);
        _columnRepo.Setup(r => r.GetByIdWithCardsAsync(second.Id, default)).ReturnsAsync(second);
        SetupProposalCreation();
        var service = new AutomationPlannerService(
            _proposalService.Object,
            new AutomationPolicyEngine(_unitOfWork.Object),
            _unitOfWork.Object);

        var result = await service.ParseInstructionAsync("create card 'DupTarget' in column 'Dup'", user.Id, board.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("mbiguous");
        _proposalService.Verify(
            s => s.CreateProposalAsync(It.IsAny<CreateProposalDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region External import

    [Fact]
    public async Task ExternalImport_AmbiguousTargetColumn_ReturnsValidationError()
    {
        var board = TestDataBuilder.CreateBoard();
        AddToCollection(board, "_columns", TestDataBuilder.CreateColumn(board.Id, "Imported", 0));
        AddToCollection(board, "_columns", TestDataBuilder.CreateColumn(board.Id, "Imported", 1));
        _boardRepo.Setup(r => r.GetByIdWithDetailsAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        var parseResult = new ExternalImportParseResult(
            Provider: ExternalImportProviders.Csv,
            Profile: ExternalImportProfiles.OutreachContactsV1,
            RowsReceived: 0,
            RowsParsed: 0,
            Candidates: [],
            Conflicts: []);
        var service = new ExternalImportService(_unitOfWork.Object, [new FakeImportAdapter(parseResult)]);
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: "Display Name,Company\nAlice,Acme",
            TargetColumnName: "imported",
            DryRun: true);

        var result = await service.ImportToBoardAsync(board.Id, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("mbiguous");
    }

    private static void AddToCollection<T>(object target, string name, T value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        var collection = field?.GetValue(target) as IList<T>;
        collection.Should().NotBeNull($"fixture navigation {name} must exist");
        collection!.Add(value);
    }

    private sealed class FakeImportAdapter(ExternalImportParseResult result) : IExternalImportAdapter
    {
        public string Provider => ExternalImportProviders.Csv;
        public Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request) => Result.Success(result);
    }

    #endregion
}
