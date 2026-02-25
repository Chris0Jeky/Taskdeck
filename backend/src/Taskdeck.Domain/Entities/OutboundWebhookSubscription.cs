using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class OutboundWebhookSubscription : Entity
{
    private const string EventFilterDelimiter = "|";
    private const int MaxEndpointUrlLength = 500;
    private const int MaxSigningSecretLength = 200;
    private const int MaxSerializedEventFiltersLength = 400;

    public Guid BoardId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string EndpointUrl { get; private set; } = string.Empty;
    public string SigningSecret { get; private set; } = string.Empty;
    public string EventFilters { get; private set; } = "*";
    public bool IsActive { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public DateTimeOffset? LastTriggeredAt { get; private set; }

    public Board Board { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

    private OutboundWebhookSubscription() : base()
    {
    }

    public OutboundWebhookSubscription(
        Guid boardId,
        Guid createdByUserId,
        string endpointUrl,
        string signingSecret,
        IEnumerable<string>? eventFilters = null)
        : base()
    {
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty.");

        if (createdByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "CreatedBy user ID cannot be empty.");

        if (string.IsNullOrWhiteSpace(endpointUrl))
            throw new DomainException(ErrorCodes.ValidationError, "Endpoint URL is required.");

        if (string.IsNullOrWhiteSpace(signingSecret))
            throw new DomainException(ErrorCodes.ValidationError, "Signing secret is required.");

        var normalizedEndpointUrl = endpointUrl.Trim();
        if (normalizedEndpointUrl.Length > MaxEndpointUrlLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Endpoint URL must be {MaxEndpointUrlLength} characters or fewer.");

        var normalizedSigningSecret = signingSecret.Trim();
        if (normalizedSigningSecret.Length > MaxSigningSecretLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Signing secret must be {MaxSigningSecretLength} characters or fewer.");

        BoardId = boardId;
        CreatedByUserId = createdByUserId;
        EndpointUrl = normalizedEndpointUrl;
        SigningSecret = normalizedSigningSecret;
        EventFilters = NormalizeEventFilters(eventFilters);
        IsActive = true;
    }

    public IReadOnlyList<string> GetEventFilters()
    {
        return EventFilters
            .Split(EventFilterDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public void RotateSecret(string newSecret)
    {
        if (string.IsNullOrWhiteSpace(newSecret))
            throw new DomainException(ErrorCodes.ValidationError, "Signing secret is required.");

        var normalizedSecret = newSecret.Trim();
        if (normalizedSecret.Length > MaxSigningSecretLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Signing secret must be {MaxSigningSecretLength} characters or fewer.");

        EnsureActive();
        SigningSecret = normalizedSecret;
        Touch();
    }

    public void UpdateEventFilters(IEnumerable<string>? eventFilters)
    {
        EnsureActive();
        EventFilters = NormalizeEventFilters(eventFilters);
        Touch();
    }

    public void Revoke(Guid revokedByUserId)
    {
        if (revokedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "RevokedBy user ID cannot be empty.");

        EnsureActive();

        IsActive = false;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedByUserId = revokedByUserId;
        Touch();
    }

    public void MarkTriggered()
    {
        LastTriggeredAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public bool MatchesEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var normalizedEventType = eventType.Trim().ToLowerInvariant();
        var filters = GetEventFilters();
        foreach (var filter in filters)
        {
            if (filter == "*")
            {
                return true;
            }

            if (filter.EndsWith(".*", StringComparison.Ordinal))
            {
                var filterPrefix = filter[..^1];
                if (normalizedEventType.StartsWith(filterPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (string.Equals(filter, normalizedEventType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeEventFilters(IEnumerable<string>? eventFilters)
    {
        var normalizedFilters = (eventFilters ?? ["*"])
            .Select(filter => filter?.Trim().ToLowerInvariant())
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filter => filter, StringComparer.Ordinal)
            .ToList();

        if (normalizedFilters.Count == 0)
        {
            normalizedFilters.Add("*");
        }

        var serialized = string.Join(EventFilterDelimiter, normalizedFilters);
        if (serialized.Length > MaxSerializedEventFiltersLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Serialized event filters must be {MaxSerializedEventFiltersLength} characters or fewer.");

        return serialized;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new DomainException(ErrorCodes.InvalidOperation, "Webhook subscription is revoked.");
    }
}
