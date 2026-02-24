using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OutboundWebhookServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IOutboundWebhookSubscriptionRepository> _subscriptionRepositoryMock;
    private readonly Mock<IOutboundWebhookDeliveryRepository> _deliveryRepositoryMock;

    public OutboundWebhookServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _subscriptionRepositoryMock = new Mock<IOutboundWebhookSubscriptionRepository>();
        _deliveryRepositoryMock = new Mock<IOutboundWebhookDeliveryRepository>();

        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.OutboundWebhookSubscriptions)
            .Returns(_subscriptionRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.OutboundWebhookDeliveries)
            .Returns(_deliveryRepositoryMock.Object);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldReturnValidationError_WhenEndpointIsNonHttpsNonLocalhost()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        var boardId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var request = new CreateOutboundWebhookSubscriptionDto("http://example.com/webhook");

        var result = await service.CreateSubscriptionAsync(boardId, actorId, request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("https");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldPersistSubscriptionAndReturnSecret()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        OutboundWebhookSubscription? persistedSubscription = null;
        _subscriptionRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<OutboundWebhookSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundWebhookSubscription, CancellationToken>((subscription, _) => persistedSubscription = subscription)
            .ReturnsAsync((OutboundWebhookSubscription subscription, CancellationToken _) => subscription);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://example.com/hook", ["card.*"]));

        result.IsSuccess.Should().BeTrue();
        result.Value.SigningSecret.Should().NotBeNullOrWhiteSpace();
        result.Value.Subscription.EventFilters.Should().ContainSingle(filter => filter == "card.*");
        persistedSubscription.Should().NotBeNull();
        persistedSubscription!.EndpointUrl.Should().Be("https://example.com/hook");
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldAllowLocalhostHttpEndpoint()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        OutboundWebhookSubscription? persistedSubscription = null;
        _subscriptionRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<OutboundWebhookSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundWebhookSubscription, CancellationToken>((subscription, _) => persistedSubscription = subscription)
            .ReturnsAsync((OutboundWebhookSubscription subscription, CancellationToken _) => subscription);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("http://localhost:5173/webhook"));

        result.IsSuccess.Should().BeTrue();
        persistedSubscription.Should().NotBeNull();
        persistedSubscription!.EndpointUrl.Should().StartWith("http://localhost:5173/webhook");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldRejectLoopbackIpHost()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://127.0.0.1/webhook"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldRejectEndpointLongerThan500Characters()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        var path = new string('a', 490);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto($"https://example.com/{path}"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("500 characters");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldRejectInvalidEventFilter()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://example.com/hook", ["card/updated"]));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Invalid event filter");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldRejectEventFilterThatExceedsMaxLength()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        var longFilter = $"{new string('a', 121)}.*";

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://example.com/hook", [longFilter]));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("maximum length");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldRejectSerializedEventFiltersLongerThanColumn()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        var filters = Enumerable.Range(0, 20)
            .Select(index => $"{new string((char)('a' + (index % 26)), 18)}.{new string((char)('a' + ((index + 1) % 26)), 18)}")
            .ToList();

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://example.com/hook", filters));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Serialized event filters");
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldDefaultFiltersToWildcard_WhenNotProvided()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        OutboundWebhookSubscription? persistedSubscription = null;
        _subscriptionRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<OutboundWebhookSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundWebhookSubscription, CancellationToken>((subscription, _) => persistedSubscription = subscription)
            .ReturnsAsync((OutboundWebhookSubscription subscription, CancellationToken _) => subscription);

        var result = await service.CreateSubscriptionAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CreateOutboundWebhookSubscriptionDto("https://example.com/hook"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Subscription.EventFilters.Should().ContainSingle().Which.Should().Be("*");
        persistedSubscription.Should().NotBeNull();
        persistedSubscription!.GetEventFilters().Should().ContainSingle().Which.Should().Be("*");
    }

    [Fact]
    public async Task EnqueueBoardMutationAsync_ShouldOnlyQueueDeliveriesForMatchingSubscriptions()
    {
        var boardId = Guid.NewGuid();
        var matching = new OutboundWebhookSubscription(
            boardId,
            Guid.NewGuid(),
            "https://example.com/matching",
            "secret",
            ["card.*"]);
        var nonMatching = new OutboundWebhookSubscription(
            boardId,
            Guid.NewGuid(),
            "https://example.com/non-matching",
            "secret",
            ["proposal.*"]);
        _subscriptionRepositoryMock
            .Setup(repository => repository.GetActiveByBoardAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matching, nonMatching]);

        var createdDeliveries = new List<OutboundWebhookDelivery>();
        _deliveryRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<OutboundWebhookDelivery>(), It.IsAny<CancellationToken>()))
            .Callback<OutboundWebhookDelivery, CancellationToken>((delivery, _) => createdDeliveries.Add(delivery))
            .ReturnsAsync((OutboundWebhookDelivery delivery, CancellationToken _) => delivery);

        var service = new OutboundWebhookService(_unitOfWorkMock.Object);
        var result = await service.EnqueueBoardMutationAsync(
            new BoardRealtimeEvent(boardId, "card", "updated", Guid.NewGuid(), DateTimeOffset.UtcNow));

        result.IsSuccess.Should().BeTrue();
        createdDeliveries.Should().ContainSingle();
        createdDeliveries[0].SubscriptionId.Should().Be(matching.Id);
        createdDeliveries[0].EventType.Should().Be("card.updated");
        using var payload = JsonDocument.Parse(createdDeliveries[0].Payload);
        payload.RootElement.TryGetProperty("deliveryId", out _).Should().BeTrue();
        payload.RootElement.TryGetProperty("eventType", out _).Should().BeTrue();
        payload.RootElement.TryGetProperty("boardId", out _).Should().BeTrue();
        payload.RootElement.TryGetProperty("DeliveryId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueBoardMutationAsync_ShouldReturnValidationError_WhenMutationTypeIsInvalid()
    {
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);

        var result = await service.EnqueueBoardMutationAsync(
            new BoardRealtimeEvent(Guid.NewGuid(), string.Empty, string.Empty, null, DateTimeOffset.UtcNow));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task RotateSecretAsync_ShouldReturnNotFound_WhenSubscriptionDoesNotExist()
    {
        _subscriptionRepositoryMock
            .Setup(repository => repository.GetByIdForBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OutboundWebhookSubscription?)null);
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);

        var result = await service.RotateSecretAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RevokeSubscriptionAsync_ShouldRevokeSubscriptionAndPersist()
    {
        var actorId = Guid.NewGuid();
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret");
        _subscriptionRepositoryMock
            .Setup(repository => repository.GetByIdForBoardAsync(
                subscription.BoardId,
                subscription.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        var service = new OutboundWebhookService(_unitOfWorkMock.Object);

        var result = await service.RevokeSubscriptionAsync(subscription.BoardId, subscription.Id, actorId);

        result.IsSuccess.Should().BeTrue();
        subscription.IsActive.Should().BeFalse();
        subscription.RevokedByUserId.Should().Be(actorId);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
