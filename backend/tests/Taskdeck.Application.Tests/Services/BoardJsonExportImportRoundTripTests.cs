using System.Reflection;
using System.Text.Json;
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

/// <summary>
/// Round-trip integrity tests for board JSON export and import.
/// Verifies that export → import produces semantically equivalent data,
/// with coverage for special characters, large datasets, empty boards,
/// and error handling.
/// </summary>
public class BoardJsonExportImportRoundTripTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly BoardJsonExportImportService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public BoardJsonExportImportRoundTripTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _labelRepoMock = new Mock<ILabelRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);

        var sandboxSettings = new DevelopmentSandboxSettings { Enabled = true };
        _service = new BoardJsonExportImportService(_unitOfWorkMock.Object, sandboxSettings);
    }

    [Fact]
    public async Task RoundTrip_FullBoard_PreservesAllData()
    {
        // Arrange: create board with columns, cards, labels, positions
        var owner = CreateUser("owner");
        var board = new Board("Project Alpha", "A test board", owner.Id);
        var col1 = new Column(board.Id, "Backlog", 0, wipLimit: null);
        var col2 = new Column(board.Id, "In Progress", 1, wipLimit: 3);
        var col3 = new Column(board.Id, "Done", 2, wipLimit: null);

        var label1 = new Label(board.Id, "Bug", "#FF0000");
        var label2 = new Label(board.Id, "Feature", "#00FF00");

        var card1 = new Card(board.Id, col1.Id, "Fix login", "Login form breaks on mobile", position: 0);
        var cardLabel1 = new CardLabel(card1.Id, label1.Id);
        SetNavigation(cardLabel1, "Label", label1);
        AddToPrivateCollection(card1, "_cardLabels", cardLabel1);

        var dueDate = DateTimeOffset.UtcNow.AddDays(7);
        var card2 = new Card(board.Id, col2.Id, "Add dashboard", "New analytics dashboard", dueDate, position: 0);
        var cardLabel2 = new CardLabel(card2.Id, label2.Id);
        SetNavigation(cardLabel2, "Label", label2);
        AddToPrivateCollection(card2, "_cardLabels", cardLabel2);

        var card3 = new Card(board.Id, col3.Id, "Setup CI", "Configure GitHub Actions", position: 0);

        AddToPrivateCollection(col1, "_cards", card1);
        AddToPrivateCollection(col2, "_cards", card2);
        AddToPrivateCollection(col3, "_cards", card3);
        AddToPrivateCollection(board, "_columns", col1);
        AddToPrivateCollection(board, "_columns", col2);
        AddToPrivateCollection(board, "_columns", col3);
        AddToPrivateCollection(board, "_labels", label1);
        AddToPrivateCollection(board, "_labels", label2);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        // Act: export to JSON, then import from the same JSON
        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue("export should succeed");

        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);

        // Assert
        importResult.IsSuccess.Should().BeTrue("import of exported JSON should succeed");
        importResult.Value.ColumnsImported.Should().Be(3, "all 3 columns should be imported");
        importResult.Value.CardsImported.Should().Be(3, "all 3 cards should be imported");
        importResult.Value.LabelsImported.Should().Be(2, "all 2 labels should be imported");

        // Verify the export DTO structure was complete before import
        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto.Should().NotBeNull();
        exportDto!.Board.Name.Should().Be("Project Alpha");
        exportDto.Board.Description.Should().Be("A test board");
        exportDto.Columns.Should().HaveCount(3);
        exportDto.Columns.Select(c => c.Name).Should().ContainInOrder("Backlog", "In Progress", "Done");
        exportDto.Cards.Should().HaveCount(3);
        exportDto.Labels.Should().HaveCount(2);

        // Verify positions are preserved in export
        var exportColumns = exportDto.Columns.OrderBy(c => c.Position).ToList();
        exportColumns[0].Position.Should().Be(0);
        exportColumns[1].Position.Should().Be(1);
        exportColumns[2].Position.Should().Be(2);

        // Verify card-label associations are in export
        var exportedCard1 = exportDto.Cards.First(c => c.Title == "Fix login");
        exportedCard1.Labels.Should().ContainSingle(l => l.Name == "Bug");

        var exportedCard2 = exportDto.Cards.First(c => c.Title == "Add dashboard");
        exportedCard2.Labels.Should().ContainSingle(l => l.Name == "Feature");
        exportedCard2.DueDate.Should().NotBeNull("due date should be preserved");
    }

    [Fact]
    public async Task RoundTrip_SpecialCharacters_PreservedThroughExportImport()
    {
        // Arrange: board with unicode, emoji, markdown in various fields
        var owner = CreateUser("owner");
        var board = new Board("Projet \u00c9quipe \ud83d\ude80", "Description with **bold** and _italic_", owner.Id);
        var col = new Column(board.Id, "T\u00e2ches \u00e0 faire", 0);
        var label = new Label(board.Id, "\ud83d\udd34 Urgent", "#FF0000");

        var card = new Card(board.Id, col.Id, "R\u00e9soudre le probl\u00e8me d'encodage \ud83d\udcdd",
            "# Steps\n1. V\u00e9rifier l'entr\u00e9e\n2. Tester avec des caract\u00e8res sp\u00e9ciaux: <>&\"'\n3. \u2714\ufe0f Valider",
            position: 0);
        var cardLabel = new CardLabel(card.Id, label.Id);
        SetNavigation(cardLabel, "Label", label);
        AddToPrivateCollection(card, "_cardLabels", cardLabel);
        AddToPrivateCollection(col, "_cards", card);
        AddToPrivateCollection(board, "_columns", col);
        AddToPrivateCollection(board, "_labels", label);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        // Act
        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto.Should().NotBeNull();

        // Assert: all special characters survive serialization
        exportDto!.Board.Name.Should().Be("Projet \u00c9quipe \ud83d\ude80");
        exportDto.Board.Description.Should().Contain("**bold**");
        exportDto.Columns.First().Name.Should().Be("T\u00e2ches \u00e0 faire");
        exportDto.Labels.First().Name.Should().Be("\ud83d\udd34 Urgent");
        exportDto.Cards.First().Title.Should().Contain("\ud83d\udcdd");
        exportDto.Cards.First().Description.Should().Contain("<>&\"'");

        // Import the exported JSON
        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue("import with special characters should succeed");
        importResult.Value.ColumnsImported.Should().Be(1);
        importResult.Value.CardsImported.Should().Be(1);
        importResult.Value.LabelsImported.Should().Be(1);
    }

    [Fact]
    public async Task RoundTrip_LargeDataset_100Cards5Columns_AllPositionsCorrect()
    {
        // Arrange: board with 5 columns and 100+ cards
        var owner = CreateUser("owner");
        var board = new Board("Large Board", "Stress test", owner.Id);
        var columns = Enumerable.Range(0, 5)
            .Select(i => new Column(board.Id, $"Column {i}", i))
            .ToList();

        var cards = new List<Card>();
        for (var i = 0; i < 100; i++)
        {
            var colIndex = i % 5;
            var positionInColumn = i / 5;
            var card = new Card(board.Id, columns[colIndex].Id, $"Card {i}", $"Description {i}", position: positionInColumn);
            AddToPrivateCollection(columns[colIndex], "_cards", card);
            cards.Add(card);
        }

        foreach (var col in columns)
            AddToPrivateCollection(board, "_columns", col);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        // Act
        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto.Should().NotBeNull();
        exportDto!.Cards.Should().HaveCount(100, "all 100 cards should be in export");
        exportDto.Columns.Should().HaveCount(5, "all 5 columns should be in export");

        // Verify positions are correct per column in the export
        var columnIds = exportDto.Columns.ToDictionary(c => c.Id, c => c.Name);
        foreach (var colDto in exportDto.Columns)
        {
            var cardsInColumn = exportDto.Cards
                .Where(c => c.ColumnId == colDto.Id)
                .OrderBy(c => c.Position)
                .ToList();

            cardsInColumn.Should().HaveCount(20, $"column '{colDto.Name}' should have 20 cards");
            for (var i = 0; i < cardsInColumn.Count; i++)
            {
                cardsInColumn[i].Position.Should().Be(i,
                    $"card at index {i} in column '{colDto.Name}' should have position {i}");
            }
        }

        // Import the exported JSON
        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue("import of large dataset should succeed");
        importResult.Value.ColumnsImported.Should().Be(5);
        importResult.Value.CardsImported.Should().Be(100);
    }

    [Fact]
    public async Task RoundTrip_EmptyBoard_NoColumnsNoCards()
    {
        // Arrange: board with nothing
        var owner = CreateUser("owner");
        var board = new Board("Empty Board", ownerId: owner.Id);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        // Act
        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto.Should().NotBeNull();
        exportDto!.Columns.Should().BeEmpty();
        exportDto.Cards.Should().BeEmpty();
        exportDto.Labels.Should().BeEmpty();
        exportDto.Board.Name.Should().Be("Empty Board");

        // Import
        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue();
        importResult.Value.ColumnsImported.Should().Be(0);
        importResult.Value.CardsImported.Should().Be(0);
        importResult.Value.LabelsImported.Should().Be(0);
    }

    [Fact]
    public async Task RoundTrip_BoardWithNullDescription_PreservedCorrectly()
    {
        var owner = CreateUser("owner");
        var board = new Board("No Description Board", description: null, ownerId: owner.Id);
        var col = new Column(board.Id, "Todo", 0);
        AddToPrivateCollection(board, "_columns", col);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto!.Board.Description.Should().BeNull();

        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue();
        importResult.Value.ColumnsImported.Should().Be(1);
    }

    [Fact]
    public void ConvertExportToImportDto_MapsAllFields()
    {
        // Arrange: create a realistic export payload and verify the conversion
        var boardId = Guid.NewGuid();
        var colId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var dueDate = now.AddDays(14);

        var exportDto = new ExportBoardDto(
            new BoardDto(boardId, "Test Board", "Board desc", false, now, now),
            new[]
            {
                new ColumnDto(colId, boardId, "Backlog", 0, 5, 2, now, now),
                new ColumnDto(Guid.NewGuid(), boardId, "Done", 1, null, 0, now, now)
            },
            new[]
            {
                new CardDto(Guid.NewGuid(), boardId, colId, "Card A", "Desc A", dueDate, false, null, 0,
                    new List<LabelDto> { new LabelDto(Guid.NewGuid(), boardId, "Bug", "#FF0000", now, now) }, now, now),
                new CardDto(Guid.NewGuid(), boardId, colId, "Card B", null, null, false, null, 1,
                    new List<LabelDto>(), now, now)
            },
            new[]
            {
                new LabelDto(Guid.NewGuid(), boardId, "Bug", "#FF0000", now, now)
            },
            new List<BoardAccessDto>(),
            now,
            "tester");

        // Act
        var importDto = BoardJsonExportImportService.ConvertExportToImportDto(exportDto);

        // Assert
        importDto.Name.Should().Be("Test Board");
        importDto.Description.Should().Be("Board desc");
        importDto.Columns.Should().HaveCount(2);
        importDto.Columns.First().Name.Should().Be("Backlog");
        importDto.Columns.First().Position.Should().Be(0);
        importDto.Columns.First().WipLimit.Should().Be(5);
        importDto.Cards.Should().HaveCount(2);
        importDto.Cards.First().Title.Should().Be("Card A");
        importDto.Cards.First().Description.Should().Be("Desc A");
        importDto.Cards.First().ColumnName.Should().Be("Backlog");
        importDto.Cards.First().DueDate.Should().Be(dueDate);
        importDto.Cards.First().Labels.Should().ContainSingle(l => l == "Bug");
        importDto.Labels.Should().ContainSingle(l => l.Name == "Bug");
    }

    [Fact]
    public void ConvertExportToImportDto_RejectsExportWithDuplicateColumnNames()
    {
        var now = DateTimeOffset.UtcNow;
        var exportDto = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Board", null, false, now, now),
            new[]
            {
                new ColumnDto(Guid.NewGuid(), Guid.NewGuid(), "Todo", 0, null, 0, now, now),
                new ColumnDto(Guid.NewGuid(), Guid.NewGuid(), "Todo", 1, null, 0, now, now)
            },
            Array.Empty<CardDto>(),
            Array.Empty<LabelDto>(),
            new List<BoardAccessDto>(),
            now, "tester");

        var act = () => BoardJsonExportImportService.ConvertExportToImportDto(exportDto);
        act.Should().Throw<JsonException>().WithMessage("*duplicate column name*");
    }

    [Fact]
    public void ConvertExportToImportDto_RejectsExportWithDuplicateLabelNames()
    {
        var now = DateTimeOffset.UtcNow;
        var exportDto = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Board", null, false, now, now),
            Array.Empty<ColumnDto>(),
            Array.Empty<CardDto>(),
            new[]
            {
                new LabelDto(Guid.NewGuid(), Guid.NewGuid(), "Bug", "#FF0000", now, now),
                new LabelDto(Guid.NewGuid(), Guid.NewGuid(), "Bug", "#00FF00", now, now)
            },
            new List<BoardAccessDto>(),
            now, "tester");

        var act = () => BoardJsonExportImportService.ConvertExportToImportDto(exportDto);
        act.Should().Throw<JsonException>().WithMessage("*duplicate label name*");
    }

    [Fact]
    public void ConvertExportToImportDto_RejectsCardReferencingUnknownColumnId()
    {
        var now = DateTimeOffset.UtcNow;
        var unknownColId = Guid.NewGuid();
        var exportDto = new ExportBoardDto(
            new BoardDto(Guid.NewGuid(), "Board", null, false, now, now),
            new[] { new ColumnDto(Guid.NewGuid(), Guid.NewGuid(), "Todo", 0, null, 0, now, now) },
            new[] { new CardDto(Guid.NewGuid(), Guid.NewGuid(), unknownColId, "Orphan Card", null, null, false, null, 0, new List<LabelDto>(), now, now) },
            Array.Empty<LabelDto>(),
            new List<BoardAccessDto>(),
            now, "tester");

        var act = () => BoardJsonExportImportService.ConvertExportToImportDto(exportDto);
        act.Should().Throw<JsonException>().WithMessage("*unknown column ID*");
    }

    [Fact]
    public void TryDeserializeImportDto_HandlesCorruptJson()
    {
        var result = BoardJsonExportImportService.TryDeserializeImportDto("{not valid json!!!");
        result.Should().BeNull();
    }

    [Fact]
    public void TryDeserializeImportDto_HandlesEmptyObject()
    {
        // "{}" deserializes as ImportBoardDto with null Name (returns null for that),
        // then tries ExportBoardDto which has null Board, causing a JsonException.
        // The method should not propagate this to the caller unhandled when called via
        // ImportBoardFromJsonAsync (which catches JsonException), but TryDeserializeImportDto
        // itself throws. This test documents that behavior.
        var act = () => BoardJsonExportImportService.TryDeserializeImportDto("{}");
        act.Should().Throw<JsonException>().WithMessage("*missing board metadata*");
    }

    [Fact]
    public async Task ImportBoardFromJsonAsync_RejectsEmptyPayload()
    {
        var result = await _service.ImportBoardFromJsonAsync("", Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ImportBoardFromJsonAsync_RejectsWhitespacePayload()
    {
        var result = await _service.ImportBoardFromJsonAsync("   ", Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ImportBoardFromJsonAsync_RejectsInvalidJsonFormat()
    {
        var user = CreateUser("user");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await _service.ImportBoardFromJsonAsync("completely invalid JSON {{{{", user.Id);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ImportBoardAsync_RejectsDuplicateLabelNames()
    {
        var user = CreateUser("user");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<Label>(), default))
            .ReturnsAsync((Label l, CancellationToken ct) => l);

        var dto = new ImportBoardDto(
            "Board",
            "Desc",
            new[] { new ImportColumnDto("Todo", 0, null) },
            Array.Empty<ImportCardDto>(),
            new[]
            {
                new ImportLabelDto("Bug", "#FF0000"),
                new ImportLabelDto("Bug", "#00FF00")
            });

        var result = await _service.ImportBoardAsync(dto, user.Id);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Duplicate label name");
    }

    [Fact]
    public async Task ImportBoardAsync_RejectsDuplicateColumnNames()
    {
        var user = CreateUser("user");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);

        var dto = new ImportBoardDto(
            "Board",
            "Desc",
            new[]
            {
                new ImportColumnDto("Todo", 0, null),
                new ImportColumnDto("TODO", 1, null)
            },
            Array.Empty<ImportCardDto>(),
            Array.Empty<ImportLabelDto>());

        var result = await _service.ImportBoardAsync(dto, user.Id);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Duplicate column name");
    }

    [Fact]
    public async Task ImportBoardAsync_RejectsCardReferencingUndeclaredLabel()
    {
        var user = CreateUser("user");
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);

        var dto = new ImportBoardDto(
            "Board",
            null,
            new[] { new ImportColumnDto("Todo", 0, null) },
            new[] { new ImportCardDto("Card", null, "Todo", 0, null, new[] { "NonExistentLabel" }) },
            Array.Empty<ImportLabelDto>());

        var result = await _service.ImportBoardAsync(dto, user.Id);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("NonExistentLabel");
    }

    [Fact]
    public async Task RoundTrip_ExportJsonIsValidParsableJson()
    {
        var owner = CreateUser("owner");
        var board = new Board("JSON Validation Board", ownerId: owner.Id);
        var col = new Column(board.Id, "Todo", 0);
        var card = new Card(board.Id, col.Id, "Test Card", "Description with \"quotes\" and \\ backslashes", position: 0);
        AddToPrivateCollection(col, "_cards", card);
        AddToPrivateCollection(board, "_columns", col);

        SetupExportMocks(board, owner);

        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        // Verify the JSON is valid and parseable by any standard JSON parser
        var json = exportResult.Value;
        json.Should().NotBeNullOrWhiteSpace();

        // Should not have trailing commas
        json.Should().NotContain(",]");
        json.Should().NotContain(",}");

        // Should parse without exceptions
        var doc = JsonDocument.Parse(json);
        doc.Should().NotBeNull();
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task RoundTrip_WipLimitPreservedThroughExportImport()
    {
        var owner = CreateUser("owner");
        var board = new Board("WIP Board", ownerId: owner.Id);
        var col = new Column(board.Id, "In Progress", 0, wipLimit: 5);
        AddToPrivateCollection(board, "_columns", col);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto!.Columns.First().WipLimit.Should().Be(5, "WIP limit should be in export");

        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RoundTrip_CardWithNoLabels_HandledCorrectly()
    {
        var owner = CreateUser("owner");
        var board = new Board("Label-free Board", ownerId: owner.Id);
        var col = new Column(board.Id, "Todo", 0);
        var card = new Card(board.Id, col.Id, "No Labels Card", "Description", position: 0);
        AddToPrivateCollection(col, "_cards", card);
        AddToPrivateCollection(board, "_columns", col);

        SetupExportMocks(board, owner);
        SetupImportMocks(owner);

        var exportResult = await _service.ExportBoardToJsonAsync(board.Id, owner.Id);
        exportResult.IsSuccess.Should().BeTrue();

        var exportDto = JsonSerializer.Deserialize<ExportBoardDto>(exportResult.Value, JsonOptions);
        exportDto!.Cards.First().Labels.Should().BeEmpty("card has no labels");

        var importResult = await _service.ImportBoardFromJsonAsync(exportResult.Value, owner.Id);
        importResult.IsSuccess.Should().BeTrue();
        importResult.Value.CardsImported.Should().Be(1);
    }

    [Fact]
    public async Task CrossUserIsolation_ExportForbidden_ForNonOwnerWithoutAccess()
    {
        var owner = CreateUser("owner");
        var otherUser = CreateUser("other");
        var board = new Board("Private Board", ownerId: owner.Id);

        // Sandbox disabled so access checks are enforced
        var strictService = new BoardJsonExportImportService(_unitOfWorkMock.Object);

        _userRepoMock.Setup(r => r.GetByIdAsync(otherUser.Id, default)).ReturnsAsync(otherUser);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, otherUser.Id, default))
            .ReturnsAsync((BoardAccess?)null);

        var result = await strictService.ExportBoardAsync(board.Id, otherUser.Id);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExportBoardAsync_ReturnsNotFound_WhenBoardDoesNotExist()
    {
        var owner = CreateUser("owner");
        _userRepoMock.Setup(r => r.GetByIdAsync(owner.Id, default)).ReturnsAsync(owner);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Board?)null);

        var result = await _service.ExportBoardAsync(Guid.NewGuid(), owner.Id);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExportBoardAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        var result = await _service.ExportBoardAsync(Guid.NewGuid(), Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    // --- Helper methods ---

    private void SetupExportMocks(Board board, User owner)
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(owner.Id, default)).ReturnsAsync(owner);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default)).ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(Array.Empty<BoardAccess>());
    }

    private void SetupImportMocks(User user)
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<Label>(), default))
            .ReturnsAsync((Label l, CancellationToken ct) => l);
    }

    private static User CreateUser(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new User($"{stem}_{suffix}", $"{stem}_{suffix}@example.com", "hashedpassword");
    }

    private static void AddToPrivateCollection<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        var collection = field?.GetValue(target) as IList<T>;
        collection.Should().NotBeNull($"field '{fieldName}' should exist on {target.GetType().Name}");
        collection!.Add(value);
    }

    private static void SetNavigation(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull($"property '{propertyName}' should exist on {target.GetType().Name}");
        property!.SetValue(target, value);
    }
}
