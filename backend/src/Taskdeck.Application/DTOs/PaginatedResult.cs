namespace Taskdeck.Application.DTOs;

/// <summary>
/// Generic paginated response wrapper. Provides the current page of items
/// along with pagination metadata so clients can implement offset-based paging.
/// </summary>
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    bool HasMore,
    int Offset,
    int Limit
);
