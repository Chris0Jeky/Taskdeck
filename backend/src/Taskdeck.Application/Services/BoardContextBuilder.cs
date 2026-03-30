using System.Text;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Builds a bounded board context string suitable for inclusion in LLM system prompts.
/// The context includes the board name, column names and positions, recent card titles
/// per column, and label names. Enforces a token budget to prevent prompt bloat.
/// </summary>
public class BoardContextBuilder : IBoardContextBuilder
{
    /// <summary>
    /// Approximate maximum character count for the board context string.
    /// At ~4 chars per token, 500 tokens ≈ 2000 characters.
    /// </summary>
    internal const int MaxContextCharacters = 2000;

    /// <summary>
    /// Maximum number of card titles to include per column.
    /// </summary>
    internal const int MaxCardsPerColumn = 5;

    private readonly IUnitOfWork _unitOfWork;

    public BoardContextBuilder(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<string?> BuildContextAsync(Guid boardId, CancellationToken ct = default)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId, ct);
        if (board == null)
            return null;

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct))
            .OrderBy(c => c.Position)
            .ToList();

        var cards = (await _unitOfWork.Cards.GetByBoardIdAsync(boardId, ct))
            .ToList();

        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("## Current Board Context");
        sb.Append("Board: ").AppendLine(board.Name);

        if (columns.Count > 0)
        {
            sb.AppendLine("Columns (in order):");
            foreach (var column in columns)
            {
                sb.Append("  - ").Append(column.Name).Append(" (position ").Append(column.Position).AppendLine(")");

                var columnCards = cards
                    .Where(c => c.ColumnId == column.Id)
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(MaxCardsPerColumn)
                    .ToList();

                foreach (var card in columnCards)
                {
                    sb.Append("    * ").AppendLine(card.Title);

                    if (sb.Length >= MaxContextCharacters)
                        break;
                }

                if (sb.Length >= MaxContextCharacters)
                    break;
            }
        }

        if (sb.Length < MaxContextCharacters && labels.Count > 0)
        {
            sb.Append("Labels: ").AppendLine(string.Join(", ", labels.Select(l => l.Name)));
        }

        // Truncate to budget if needed
        if (sb.Length > MaxContextCharacters)
        {
            sb.Length = MaxContextCharacters;
            sb.AppendLine("...(truncated)");
        }

        return sb.ToString();
    }
}
