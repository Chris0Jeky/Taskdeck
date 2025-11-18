using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Tests.TestUtilities;

/// <summary>
/// Provides factory methods for creating test entities with sensible defaults.
/// </summary>
public static class TestDataBuilder
{
    public static Board CreateBoard(string name = "Test Board", string description = "Test description", bool isArchived = false)
    {
        var board = new Board(name, description);
        if (isArchived)
            board.Archive();
        return board;
    }

    public static Column CreateColumn(
        Guid boardId,
        string name = "Test Column",
        int position = 0,
        int? wipLimit = null)
    {
        return new Column(boardId, name, position, wipLimit);
    }

    public static Card CreateCard(
        Guid boardId,
        Guid columnId,
        string title = "Test Card",
        string? description = null,
        DateTimeOffset? dueDate = null,
        int position = 0,
        bool isBlocked = false,
        string? blockReason = null)
    {
        var card = new Card(boardId, columnId, title, description, dueDate, position);
        if (isBlocked && !string.IsNullOrEmpty(blockReason))
            card.Block(blockReason);
        return card;
    }

    public static Label CreateLabel(
        Guid boardId,
        string name = "Test Label",
        string colorHex = "#FF0000")
    {
        return new Label(boardId, name, colorHex);
    }

    public static CardLabel CreateCardLabel(Guid cardId, Guid labelId)
    {
        return new CardLabel(cardId, labelId);
    }

    /// <summary>
    /// Creates a board with columns and cards for comprehensive testing scenarios.
    /// </summary>
    public static (Board board, Column[] columns, Card[] cards) CreateBoardWithColumnsAndCards()
    {
        var board = CreateBoard("Test Board");

        var todoColumn = CreateColumn(board.Id, "To Do", position: 0);
        var inProgressColumn = CreateColumn(board.Id, "In Progress", position: 1, wipLimit: 2);
        var doneColumn = CreateColumn(board.Id, "Done", position: 2);

        var columns = new[] { todoColumn, inProgressColumn, doneColumn };

        var card1 = CreateCard(board.Id, todoColumn.Id, "Task 1", position: 0);
        var card2 = CreateCard(board.Id, todoColumn.Id, "Task 2", position: 1);
        var card3 = CreateCard(board.Id, inProgressColumn.Id, "Task 3", position: 0);

        var cards = new[] { card1, card2, card3 };

        return (board, columns, cards);
    }
}
