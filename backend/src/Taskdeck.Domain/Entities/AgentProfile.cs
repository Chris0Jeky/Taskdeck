using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public enum AgentScopeType { Workspace = 0, Board = 1 }

public sealed class AgentProfile : Entity
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;
    private const int MaxTemplateKeyLength = 100;
    private const int MaxPolicyJsonLength = 8000;

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string TemplateKey { get; private set; } = string.Empty;
    public AgentScopeType ScopeType { get; private set; }
    public Guid? ScopeBoardId { get; private set; }
    public string PolicyJson { get; private set; } = "{}";
    public bool IsEnabled { get; private set; } = true;

    private AgentProfile() : base() { } // EF Core

    public AgentProfile(
        Guid userId,
        string name,
        string templateKey,
        AgentScopeType scopeType,
        Guid? scopeBoardId = null,
        string? description = null,
        string? policyJson = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.ValidationError, "Name cannot be empty");

        if (name.Length > MaxNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Name cannot exceed {MaxNameLength} characters");

        if (string.IsNullOrWhiteSpace(templateKey))
            throw new DomainException(ErrorCodes.ValidationError, "TemplateKey cannot be empty");

        if (templateKey.Length > MaxTemplateKeyLength)
            throw new DomainException(ErrorCodes.ValidationError, $"TemplateKey cannot exceed {MaxTemplateKeyLength} characters");

        if (scopeType == AgentScopeType.Board && (!scopeBoardId.HasValue || scopeBoardId.Value == Guid.Empty))
            throw new DomainException(ErrorCodes.ValidationError, "ScopeBoardId is required for Board scope");

        if (description is not null && description.Length > MaxDescriptionLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Description cannot exceed {MaxDescriptionLength} characters");

        if (policyJson is not null && policyJson.Length > MaxPolicyJsonLength)
            throw new DomainException(ErrorCodes.ValidationError, $"PolicyJson cannot exceed {MaxPolicyJsonLength} characters");

        UserId = userId;
        Name = name;
        TemplateKey = templateKey;
        ScopeType = scopeType;
        ScopeBoardId = scopeBoardId;
        Description = description ?? string.Empty;
        PolicyJson = policyJson ?? "{}";
        IsEnabled = true;
    }

    public void UpdateMetadata(string name, string? description = null, string? policyJson = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.ValidationError, "Name cannot be empty");

        if (name.Length > MaxNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Name cannot exceed {MaxNameLength} characters");

        if (description is not null && description.Length > MaxDescriptionLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Description cannot exceed {MaxDescriptionLength} characters");

        if (policyJson is not null && policyJson.Length > MaxPolicyJsonLength)
            throw new DomainException(ErrorCodes.ValidationError, $"PolicyJson cannot exceed {MaxPolicyJsonLength} characters");

        Name = name;

        if (description is not null)
            Description = description;

        if (policyJson is not null)
            PolicyJson = policyJson;

        Touch();
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Touch();
    }
}
