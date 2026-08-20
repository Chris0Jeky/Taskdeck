namespace Taskdeck.Application.DTOs;

/// <param name="CanWrite">
/// Whether the CALLING user may write to this board — server-computed, claims-first,
/// never accepted from client input. True for the board owner and for any member whose
/// <c>BoardAccess.CanWrite()</c> role admits writes (Owner / Admin / Editor).
/// <para>
/// It is scoped to the caller the DTO was produced for, so it is only meaningful on
/// caller-scoped surfaces. The boards LIST endpoint (<c>GET /api/boards</c>) always
/// computes it; mappings with no acting user (unauthenticated service overloads, the
/// board JSON export artifact) emit <c>false</c> — fail-closed, not "unknown".
/// </para>
/// </param>
public record BoardDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanWrite = false
);

public record BoardDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ColumnDto> Columns
);

public record CreateBoardDto(
    string Name,
    string? Description
);

public record UpdateBoardDto(
    string? Name,
    string? Description,
    bool? IsArchived
);
