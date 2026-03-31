using System.Text;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Builds a bounded board context string suitable for inclusion in LLM system prompts.
/// The context includes the board name, column names and positions, card IDs and titles
/// per column (with labels), and board-level label names.
/// Enforces a token budget to prevent prompt bloat.
/// </summary>
public class BoardContextBuilder : IBoardContextBuilder
{
    /// <summary>
    /// Approximate maximum character count for the board context string.
    /// At ~4 chars per token, 1000 tokens ≈ 4000 characters.
    /// </summary>
    internal const int MaxContextCharacters = 4000;

    /// <summary>
    /// Maximum number of cards to include per column.
    /// </summary>
    internal const int MaxCardsPerColumn = 5;

    /// <summary>
    /// Number of characters used for the short card ID prefix (first N hex chars of GUID).
    /// </summary>
    internal const int ShortIdLength = 8;

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

        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .ToList();

        // Build a label lookup for card-level label display
        var labelLookup = labels.ToDictionary(l => l.Id, l => l.Name);

        var sb = new StringBuilder();
        sb.AppendLine("## Current Board Context");
        sb.Append("Board: ").AppendLine(board.Name);

        if (columns.Count > 0)
        {
            sb.Append("Columns: ").AppendLine(
                string.Join(" → ", columns.Select(c => c.Name)));

            foreach (var column in columns)
            {
                var columnCards = (await _unitOfWork.Cards.GetByColumnIdAsync(column.Id, ct))
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(MaxCardsPerColumn)
                    .ToList();

                if (columnCards.Count == 0)
                    continue;

                sb.Append("Cards in \"").Append(column.Name).AppendLine("\":");

                foreach (var card in columnCards)
                {
                    var shortId = FormatShortId(card.Id);
                    sb.Append("  [").Append(shortId).Append("] ").Append(card.Title);

                    // Append card-level labels if any
                    var cardLabelNames = card.CardLabels
                        .Where(cl => labelLookup.ContainsKey(cl.LabelId))
                        .Select(cl => labelLookup[cl.LabelId])
                        .ToList();

                    if (cardLabelNames.Count > 0)
                    {
                        sb.Append(" [").Append(string.Join(", ", cardLabelNames)).Append(']');
                    }

                    sb.AppendLine();

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

        // Truncate to budget strictly: set length to budget minus marker, then append marker.
        if (sb.Length > MaxContextCharacters)
        {
            const string marker = "...(truncated)";
            sb.Length = Math.Max(0, MaxContextCharacters - marker.Length);
            sb.Append(marker);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the first <see cref="ShortIdLength"/> hex characters of a GUID,
    /// without hyphens, for compact card identification in the context.
    /// </summary>
    internal static string FormatShortId(Guid id)
    {
        return id.ToString("N")[..ShortIdLength];
    }
}
