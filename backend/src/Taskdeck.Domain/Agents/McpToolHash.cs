using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Agents;

/// <summary>
/// Tracks the SHA-256 hash of an MCP tool definition (name + description + inputSchema)
/// for a given user. When the definition changes, approval is automatically revoked
/// and must be re-granted before the tool can be used. Implements GP-10 MCP integrity.
/// </summary>
public sealed class McpToolHash : Entity
{
    private const int MaxToolNameLength = 200;
    private const int MaxDefinitionHashLength = 128;

    public Guid UserId { get; private set; }
    public string ToolName { get; private set; } = string.Empty;
    public string DefinitionHash { get; private set; } = string.Empty;
    public bool IsApproved { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    private McpToolHash() : base() { } // EF Core

    public McpToolHash(Guid userId, string toolName, string definitionHash) : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(toolName))
            throw new DomainException(ErrorCodes.ValidationError, "ToolName cannot be empty");

        if (toolName.Length > MaxToolNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"ToolName cannot exceed {MaxToolNameLength} characters");

        if (string.IsNullOrWhiteSpace(definitionHash))
            throw new DomainException(ErrorCodes.ValidationError, "DefinitionHash cannot be empty");

        if (definitionHash.Length > MaxDefinitionHashLength)
            throw new DomainException(ErrorCodes.ValidationError, $"DefinitionHash cannot exceed {MaxDefinitionHashLength} characters");

        UserId = userId;
        ToolName = toolName;
        DefinitionHash = definitionHash;
        IsApproved = false;
        ApprovedAt = null;
    }

    /// <summary>
    /// Marks this tool definition as approved by the user.
    /// </summary>
    public void Approve()
    {
        IsApproved = true;
        ApprovedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>
    /// Updates the stored hash. If the new hash differs from the current one,
    /// approval is automatically revoked (the user must re-approve).
    /// </summary>
    public void UpdateHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new DomainException(ErrorCodes.ValidationError, "DefinitionHash cannot be empty");

        if (newHash.Length > MaxDefinitionHashLength)
            throw new DomainException(ErrorCodes.ValidationError, $"DefinitionHash cannot exceed {MaxDefinitionHashLength} characters");

        if (!string.Equals(DefinitionHash, newHash, StringComparison.Ordinal))
        {
            DefinitionHash = newHash;
            IsApproved = false;
            ApprovedAt = null;
        }

        Touch();
    }

    /// <summary>
    /// Returns true only if the tool is approved AND the provided hash
    /// matches the currently stored (approved) hash.
    /// </summary>
    public bool IsDefinitionApproved(string currentHash)
    {
        return IsApproved
            && string.Equals(DefinitionHash, currentHash, StringComparison.Ordinal);
    }
}
