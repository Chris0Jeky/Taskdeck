using System.Reflection;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ExternalImportServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepositoryMock;
    private readonly Mock<ICardRepository> _cardRepositoryMock;

    public ExternalImportServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepositoryMock = new Mock<IBoardRepository>();
        _cardRepositoryMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.Boards).Returns(_boardRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.Cards).Returns(_cardRepositoryMock.Object);
    }

    [Fact]
    public async Task ImportToBoardAsync_ShouldReturnValidationError_WhenProviderIsUnknown()
    {
        var service = new ExternalImportService(_unitOfWorkMock.Object, Array.Empty<IExternalImportAdapter>());
        var request = new ExternalImportRequestDto(
            Provider: "not-supported",
            Payload: "Display Name,Company\nAlice,Acme",
            TargetColumnName: "Imported",
            DryRun: true);

        var result = await service.ImportToBoardAsync(Guid.NewGuid(), request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported import provider");
        _boardRepositoryMock.Verify(repository => repository.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportToBoardAsync_DryRun_ShouldReportCreateUpdateAndSkipCounts()
    {
        var board = BuildBoardWithColumn("Imported");
        var boardId = board.Id;
        var targetColumnId = board.Columns.Single().Id;
        var existingCard = new Card(
            cardId: Guid.NewGuid(),
            boardId: boardId,
            columnId: targetColumnId,
            title: "Alice Example",
            description: "[taskdeck-import-meta] {\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"email:aliceexamplecom\"}",
            dueDate: null,
            position: 0);
        AttachCard(board, existingCard, targetColumnId);

        _boardRepositoryMock
            .Setup(repository => repository.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var parseResult = new ExternalImportParseResult(
            Provider: ExternalImportProviders.Csv,
            Profile: ExternalImportProfiles.OutreachContactsV1,
            RowsReceived: 3,
            RowsParsed: 3,
            Candidates:
            [
                new ExternalImportCandidate(2, "email:aliceexamplecom", "Alice Example", "[taskdeck-import-meta] {\"provider\":\"csv\",\"profile\":\"outreach.contacts.v1\",\"dedupeKey\":\"email:aliceexamplecom\"}"),
                new ExternalImportCandidate(3, "email:bobexamplecom", "Bob Example", "new"),
                new ExternalImportCandidate(4, "email:carolexamplecom", "Carol Example", "new")
            ],
            Conflicts: []);

        var service = new ExternalImportService(_unitOfWorkMock.Object, [new FakeAdapter(parseResult)]);
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: "unused",
            TargetColumnName: "Imported",
            DryRun: true);

        var result = await service.ImportToBoardAsync(boardId, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsCreated.Should().Be(2);
        result.Value.RowsUpdated.Should().Be(0);
        result.Value.RowsSkipped.Should().Be(1);
        result.Value.Applied.Should().BeFalse();
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportToBoardAsync_ShouldRollback_WhenPersistenceFails()
    {
        var board = BuildBoardWithColumn("Imported");
        var boardId = board.Id;

        _boardRepositoryMock
            .Setup(repository => repository.GetByIdWithDetailsAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database failure"));

        var parseResult = new ExternalImportParseResult(
            Provider: ExternalImportProviders.Csv,
            Profile: ExternalImportProfiles.OutreachContactsV1,
            RowsReceived: 1,
            RowsParsed: 1,
            Candidates:
            [
                new ExternalImportCandidate(2, "email:aliceexamplecom", "Alice Example", "new")
            ],
            Conflicts: []);

        var service = new ExternalImportService(_unitOfWorkMock.Object, [new FakeAdapter(parseResult)]);
        var request = new ExternalImportRequestDto(
            Provider: ExternalImportProviders.Csv,
            Payload: "unused",
            TargetColumnName: "Imported",
            DryRun: false);

        var result = await service.ImportToBoardAsync(boardId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Board BuildBoardWithColumn(string columnName)
    {
        var board = new Board("Import Board", ownerId: Guid.NewGuid());
        var column = new Column(board.Id, columnName, 0);
        AddToPrivateCollection(board, "_columns", column);
        return board;
    }

    private static void AttachCard(Board board, Card card, Guid columnId)
    {
        AddToPrivateCollection(board, "_cards", card);
        var column = board.Columns.Single(existingColumn => existingColumn.Id == columnId);
        AddToPrivateCollection(column, "_cards", card);
    }

    private static void AddToPrivateCollection<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        var collection = field?.GetValue(target) as IList<T>;
        collection.Should().NotBeNull($"field '{fieldName}' should exist on {target.GetType().Name}");
        collection!.Add(value);
    }

    private sealed class FakeAdapter : IExternalImportAdapter
    {
        private readonly ExternalImportParseResult _result;

        public FakeAdapter(ExternalImportParseResult result)
        {
            _result = result;
        }

        public string Provider => ExternalImportProviders.Csv;

        public Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request)
        {
            return Result.Success(_result);
        }
    }
}
