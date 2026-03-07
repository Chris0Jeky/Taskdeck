using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public enum AgentScopeType
{
    Workspace = 0,
    Board = 1,
}

public sealed class AgentProfile : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string TemplateKey { get; private set; } = string.Empty;
    public AgentScopeType ScopeType { get; private set; }
    public Guid? ScopeBoardId { get; private set; }
    public string PolicyJson { get; private set; } = "{}";
    public bool IsEnabled { get; private set; } = true;

    private AgentProfile() { }

    public AgentProfile(Guid userId, string name, string templateKey, AgentScopeType scopeType, Guid? scopeBoardId = null)
    {
        if (userId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.ValidationError, "Agent name is required.");
        if (string.IsNullOrWhiteSpace(templateKey)) throw new DomainException(ErrorCodes.ValidationError, "Template key is required.");
        if (scopeType == AgentScopeType.Board && (!scopeBoardId.HasValue || scopeBoardId == Guid.Empty))
            throw new DomainException(ErrorCodes.ValidationError, "Board scope requires ScopeBoardId.");

        UserId = userId;
        Name = name.Trim();
        TemplateKey = templateKey.Trim();
        ScopeType = scopeType;
        ScopeBoardId = scopeBoardId;
    }

    public void UpdateMetadata(string name, string? description, string? policyJson)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.ValidationError, "Agent name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        PolicyJson = string.IsNullOrWhiteSpace(policyJson) ? "{}" : policyJson.Trim();
        Touch();
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Touch();
    }
}
