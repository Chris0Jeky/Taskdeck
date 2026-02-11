namespace Taskdeck.Domain.Enums;

/// <summary>
/// Defines the role types for users with different permission levels.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Owner has full control over the board including deletion and permission management.
    /// </summary>
    Owner = 0,

    /// <summary>
    /// Admin can manage board content and grant permissions to others (except ownership transfer).
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Editor can create, modify, and delete cards, columns, and labels.
    /// </summary>
    Editor = 2,

    /// <summary>
    /// Viewer can only read board content without making modifications.
    /// </summary>
    Viewer = 3
}
