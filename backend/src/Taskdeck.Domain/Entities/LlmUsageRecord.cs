using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Tracks per-request LLM token usage for quota enforcement and cost visibility.
/// </summary>
public class LlmUsageRecord : Entity
{
    public Guid UserId { get; private set; }
    public LlmSurface Surface { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    private LlmUsageRecord() : base() { }

    public LlmUsageRecord(
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(provider))
            throw new DomainException(ErrorCodes.ValidationError, "Provider cannot be empty");

        if (inputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Input tokens cannot be negative");

        if (outputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Output tokens cannot be negative");

        UserId = userId;
        Surface = surface;
        Provider = provider;
        Model = model ?? string.Empty;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public int TotalTokens => InputTokens + OutputTokens;
}
