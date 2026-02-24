using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class OutboundWebhookService : IOutboundWebhookService
{
    private const int MaxEndpointUrlLength = 500;
    private const int MaxEventFilterCount = 20;
    private const int MaxEventFilterLength = 120;
    private const int MaxSerializedEventFiltersLength = 400;
    private const string EventFilterDelimiter = "|";

    private static readonly Regex EventFilterRegex = new(
        @"^\*$|^[a-z]+(\.[a-z]+)?\.(\*|[a-z]+)$|^[a-z]+\.(\*|[a-z]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions WebhookEnvelopeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IUnitOfWork _unitOfWork;

    public OutboundWebhookService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<OutboundWebhookSubscriptionSecretDto>> CreateSubscriptionAsync(
        Guid boardId,
        Guid actorUserId,
        CreateOutboundWebhookSubscriptionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto == null)
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(
                ErrorCodes.ValidationError,
                "Request body is required.");
        }

        if (!TryValidateEndpointUrl(dto.EndpointUrl, out var normalizedEndpoint, out var endpointValidationError))
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(
                ErrorCodes.ValidationError,
                endpointValidationError!);
        }

        var normalizedFiltersResult = NormalizeEventFilters(dto.EventFilters);
        if (!normalizedFiltersResult.IsSuccess)
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(
                normalizedFiltersResult.ErrorCode,
                normalizedFiltersResult.ErrorMessage);
        }

        var signingSecret = GenerateSigningSecret();

        try
        {
            var subscription = new OutboundWebhookSubscription(
                boardId,
                actorUserId,
                normalizedEndpoint!,
                signingSecret,
                normalizedFiltersResult.Value);
            await _unitOfWork.OutboundWebhookSubscriptions.AddAsync(subscription, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new OutboundWebhookSubscriptionSecretDto(
                MapSubscription(subscription),
                signingSecret));
        }
        catch (DomainException ex)
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<OutboundWebhookSubscriptionDto>>> ListSubscriptionsAsync(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await _unitOfWork.OutboundWebhookSubscriptions.GetActiveByBoardAsync(boardId, cancellationToken);
        return Result.Success<IReadOnlyList<OutboundWebhookSubscriptionDto>>(subscriptions.Select(MapSubscription).ToList());
    }

    public async Task<Result<OutboundWebhookSubscriptionSecretDto>> RotateSecretAsync(
        Guid boardId,
        Guid subscriptionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _unitOfWork.OutboundWebhookSubscriptions.GetByIdForBoardAsync(
            boardId,
            subscriptionId,
            cancellationToken);
        if (subscription == null)
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(
                ErrorCodes.NotFound,
                $"Webhook subscription with ID {subscriptionId} was not found on board {boardId}.");
        }

        var newSecret = GenerateSigningSecret();
        try
        {
            subscription.RotateSecret(newSecret);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new OutboundWebhookSubscriptionSecretDto(
                MapSubscription(subscription),
                newSecret));
        }
        catch (DomainException ex)
        {
            return Result.Failure<OutboundWebhookSubscriptionSecretDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> RevokeSubscriptionAsync(
        Guid boardId,
        Guid subscriptionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _unitOfWork.OutboundWebhookSubscriptions.GetByIdForBoardAsync(
            boardId,
            subscriptionId,
            cancellationToken);
        if (subscription == null)
        {
            return Result.Failure(
                ErrorCodes.NotFound,
                $"Webhook subscription with ID {subscriptionId} was not found on board {boardId}.");
        }

        try
        {
            subscription.Revoke(actorUserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> EnqueueBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        var eventType = $"{mutation.EntityType}.{mutation.Operation}".Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(eventType) || eventType == ".")
        {
            return Result.Failure(ErrorCodes.ValidationError, "Webhook event type could not be determined.");
        }

        var subscriptions = await _unitOfWork.OutboundWebhookSubscriptions.GetActiveByBoardAsync(
            mutation.BoardId,
            cancellationToken);

        foreach (var subscription in subscriptions)
        {
            if (!subscription.MatchesEvent(eventType))
            {
                continue;
            }

            var deliveryId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new OutboundWebhookEventEnvelope(
                deliveryId,
                eventType,
                mutation.BoardId,
                mutation.EntityType,
                mutation.Operation,
                mutation.EntityId,
                mutation.OccurredAt),
                WebhookEnvelopeJsonOptions);
            var delivery = new OutboundWebhookDelivery(
                subscription.Id,
                mutation.BoardId,
                eventType,
                payload);
            await _unitOfWork.OutboundWebhookDeliveries.AddAsync(delivery, cancellationToken);
            subscription.MarkTriggered();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static bool TryValidateEndpointUrl(
        string? endpointUrl,
        out string? normalizedEndpoint,
        out string? validationError)
    {
        normalizedEndpoint = null;
        validationError = null;

        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            validationError = "Endpoint URL is required.";
            return false;
        }

        var trimmed = endpointUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            validationError = "Endpoint URL must be an absolute URI.";
            return false;
        }

        var isHttps = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            validationError = "Endpoint URL must use http or https.";
            return false;
        }

        if (isHttp && !string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "Non-localhost webhook endpoints must use https.";
            return false;
        }

        if (IsBlockedIpHost(parsed.Host))
        {
            validationError = "Endpoint host is not allowed.";
            return false;
        }

        normalizedEndpoint = parsed.ToString();
        if (normalizedEndpoint.Length > MaxEndpointUrlLength)
        {
            validationError = $"Endpoint URL must be {MaxEndpointUrlLength} characters or fewer.";
            normalizedEndpoint = null;
            return false;
        }

        return true;
    }

    private static Result<List<string>> NormalizeEventFilters(List<string>? filters)
    {
        var normalized = (filters ?? ["*"])
            .Select(filter => (filter ?? string.Empty).Trim().ToLowerInvariant())
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filter => filter, StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add("*");
        }

        if (normalized.Count > MaxEventFilterCount)
        {
            return Result.Failure<List<string>>(
                ErrorCodes.ValidationError,
                $"A webhook subscription can contain at most {MaxEventFilterCount} event filters.");
        }

        foreach (var filter in normalized)
        {
            if (filter!.Length > MaxEventFilterLength)
            {
                return Result.Failure<List<string>>(
                    ErrorCodes.ValidationError,
                    $"Event filter '{filter}' exceeds the maximum length of {MaxEventFilterLength} characters.");
            }

            if (!EventFilterRegex.IsMatch(filter!))
            {
                return Result.Failure<List<string>>(
                    ErrorCodes.ValidationError,
                    $"Invalid event filter '{filter}'.");
            }
        }

        var serializedFilters = string.Join(EventFilterDelimiter, normalized);
        if (serializedFilters.Length > MaxSerializedEventFiltersLength)
        {
            return Result.Failure<List<string>>(
                ErrorCodes.ValidationError,
                $"Serialized event filters must be {MaxSerializedEventFiltersLength} characters or fewer.");
        }

        return Result.Success(normalized);
    }

    private static bool IsBlockedIpHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IPAddress.TryParse(host, out var ipAddress))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ipAddress))
        {
            return true;
        }

        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];

            return first == 0 ||
                   first == 10 ||
                   first == 127 ||
                   (first == 100 && second >= 64 && second <= 127) ||
                   (first == 169 && second == 254) ||
                   (first == 172 && second >= 16 && second <= 31) ||
                   (first == 192 && second == 168) ||
                   first >= 224;
        }

        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ipAddress.Equals(IPAddress.IPv6None) ||
                ipAddress.IsIPv6LinkLocal ||
                ipAddress.IsIPv6SiteLocal ||
                ipAddress.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = ipAddress.GetAddressBytes();
            // RFC4193 Unique local addresses (fc00::/7).
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static string GenerateSigningSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static OutboundWebhookSubscriptionDto MapSubscription(OutboundWebhookSubscription subscription)
    {
        return new OutboundWebhookSubscriptionDto(
            subscription.Id,
            subscription.BoardId,
            subscription.EndpointUrl,
            subscription.GetEventFilters().ToList(),
            subscription.IsActive,
            subscription.CreatedAt,
            subscription.UpdatedAt,
            subscription.RevokedAt,
            subscription.LastTriggeredAt);
    }

    private sealed record OutboundWebhookEventEnvelope(
        Guid DeliveryId,
        string EventType,
        Guid BoardId,
        string EntityType,
        string Operation,
        Guid? EntityId,
        DateTimeOffset OccurredAt);
}
