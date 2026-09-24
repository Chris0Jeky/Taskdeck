using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Resolves a user-supplied column name against a board's columns.
/// Column names are not a domain uniqueness invariant, so resolution fails
/// closed on ambiguity instead of silently picking the first match.
/// Mirrors the label-name ambiguity handling in the proposal pipeline.
/// </summary>
public static class ColumnNameResolver
{
    /// <summary>
    /// Finds the unique column matching <paramref name="columnName"/> (case-insensitive).
    /// Returns Ambiguous when two or more columns share the name.
    /// </summary>
    public static ColumnNameResolution Resolve(IEnumerable<Column> columns, string columnName)
    {
        Column? first = null;
        foreach (var column in columns)
        {
            if (!string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (first is not null)
                return ColumnNameResolution.Ambiguous();
            first = column;
        }

        return first is null ? ColumnNameResolution.NotFound() : ColumnNameResolution.Found(first);
    }

    /// <summary>
    /// Shared user-facing message for ambiguous column names. Column tools accept
    /// names only (no column-id parameter exists), so the remedy is to rename a duplicate.
    /// </summary>
    public static string AmbiguousMessage(string columnName) =>
        $"Column name '{columnName}' is ambiguous on this board; rename one of the duplicate columns and retry";
}

public enum ColumnResolutionOutcome
{
    Found,
    NotFound,
    Ambiguous
}

public readonly record struct ColumnNameResolution(ColumnResolutionOutcome Outcome, Column? Column)
{
    public static ColumnNameResolution Found(Column column) => new(ColumnResolutionOutcome.Found, column);

    public static ColumnNameResolution NotFound() => new(ColumnResolutionOutcome.NotFound, null);

    public static ColumnNameResolution Ambiguous() => new(ColumnResolutionOutcome.Ambiguous, null);
}
