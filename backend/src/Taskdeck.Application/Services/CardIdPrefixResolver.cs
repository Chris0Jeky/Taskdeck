using System.Text.RegularExpressions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Resolves shortened card ID prefixes (e.g., 8-char hex from tool responses)
/// to full GUIDs by prefix-matching against cards on a board.
///
/// Chat read tools return short IDs like "d2d8c7d2" for compact display,
/// but the proposal pipeline requires full GUIDs. This resolver bridges
/// the gap so users can reference cards by their short IDs in instructions.
/// </summary>
public static class CardIdPrefixResolver
{
    /// <summary>
    /// Matches a hex string that looks like a short card ID prefix (8 hex chars, no hyphens).
    /// </summary>
    private static readonly Regex ShortHexPattern = new(
        @"^[0-9a-f]{8}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns true if the given string looks like a short card ID prefix
    /// (exactly 8 hex characters, not parseable as a full GUID).
    /// </summary>
    public static bool IsShortIdPrefix(string idStr)
    {
        if (string.IsNullOrWhiteSpace(idStr))
            return false;

        // If it parses as a full GUID, it's not a short prefix
        if (Guid.TryParse(idStr, out _))
            return false;

        return ShortHexPattern.IsMatch(idStr);
    }

    /// <summary>
    /// Attempts to resolve a card ID string to a full GUID. If the string is already
    /// a valid GUID, it is returned directly. If it is a short hex prefix, the board's
    /// cards are searched for a unique match.
    /// </summary>
    /// <returns>
    /// Success with the resolved GUID, or Failure with a descriptive error message
    /// for not-found, ambiguous, or invalid inputs.
    /// </returns>
    public static async Task<Result<Guid>> ResolveCardIdAsync(
        string cardIdStr,
        Guid boardId,
        IUnitOfWork unitOfWork,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cardIdStr))
            return Result.Failure<Guid>("ValidationError", "Card ID cannot be empty");

        // Full GUID — return directly
        if (Guid.TryParse(cardIdStr, out var fullGuid))
            return Result.Success(fullGuid);

        // Not a valid short prefix pattern
        if (!IsShortIdPrefix(cardIdStr))
            return Result.Failure<Guid>("ValidationError", $"Invalid card ID: {cardIdStr}");

        // Prefix match against board cards
        var normalizedPrefix = cardIdStr.ToLowerInvariant();
        var boardCards = await unitOfWork.Cards.GetByBoardIdAsync(boardId, ct);
        var matches = boardCards
            .Where(c => c.Id.ToString("N").StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => Result.Failure<Guid>("NotFound",
                $"No card found matching prefix '{cardIdStr}' on this board"),
            1 => Result.Success(matches[0].Id),
            _ => Result.Failure<Guid>("ValidationError",
                $"Ambiguous card ID prefix '{cardIdStr}' matches {matches.Count} cards. " +
                "Use a full card ID or a more specific prefix.")
        };
    }
}
