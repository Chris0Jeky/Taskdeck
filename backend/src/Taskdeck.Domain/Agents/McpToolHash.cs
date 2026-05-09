using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Agents;

/// <summary>
/// Tracks the hash of an MCP tool definition (name + description + schema).
/// When a tool's definition changes, the hash changes and user re-approval
/// is required before the tool can be used again.
/// </summary>
public sealed class McpToolHash : Entity
{
    private const int MaxToolNameLength = 200;
    private const int MaxHashLength = 128;

    /// <summary>The user who approved this tool definition.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The MCP tool name (unique per user).</summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>SHA-256 hash of (name, description, inputSchema).</summary>
    public string DefinitionHash { get; private set; } = string.Empty;

    /// <summary>Whether the user has approved this specific definition hash.</summary>
    public bool IsApproved { get; private set; }

    /// <summary>When the user last approved (or null if never approved).</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    private McpToolHash() : base() { } // EF Core

    public McpToolHash(Guid userId, string toolName, string definitionHash)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(toolName))
            throw new DomainException(ErrorCodes.ValidationError, "ToolName cannot be empty");

        if (toolName.Length > MaxToolNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"ToolName cannot exceed {MaxToolNameLength} characters");

        if (string.IsNullOrWhiteSpace(definitionHash))
            throw new DomainException(ErrorCodes.ValidationError, "DefinitionHash cannot be empty");

        if (definitionHash.Length > MaxHashLength)
            throw new DomainException(ErrorCodes.ValidationError, $"DefinitionHash cannot exceed {MaxHashLength} characters");

        UserId = userId;
        ToolName = toolName;
        DefinitionHash = definitionHash;
        IsApproved = false;
    }

    /// <summary>
    /// Approve the current definition hash. Only valid when the hash is current.
    /// </summary>
    public void Approve()
    {
        IsApproved = true;
        ApprovedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>
    /// Update the definition hash when the tool definition changes.
    /// Automatically revokes approval, requiring user re-approval.
    /// </summary>
    public void UpdateHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new DomainException(ErrorCodes.ValidationError, "DefinitionHash cannot be empty");

        if (newHash.Length > MaxHashLength)
            throw new DomainException(ErrorCodes.ValidationError, $"DefinitionHash cannot exceed {MaxHashLength} characters");

        if (DefinitionHash == newHash)
            return; // No change — keep existing approval state

        DefinitionHash = newHash;
        IsApproved = false;
        ApprovedAt = null;
        Touch();
    }

    /// <summary>
    /// Returns true if the tool is approved and the given hash matches the stored hash.
    /// </summary>
    public bool IsDefinitionApproved(string currentHash)
    {
        return IsApproved && DefinitionHash == currentHash;
    }
}
