using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Property-based JSON serialization round-trip tests for Notification DTOs.
/// Key property: serialize then deserialize produces identical object for all input content.
/// </summary>
public class NotificationDtoSerializationFuzzTests
{
    private const int MaxTests = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE notifications; --"),
        Gen.Constant("\"quoted\""),
        Gen.Constant("back\\slash"),
        Gen.Constant("new\nline"),
        Gen.Constant("emoji 👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant(""),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string?> NullableStringGen() => Gen.OneOf(
        Gen.Constant((string?)null),
        AdversarialStringGen().Select(s => (string?)s)
    );

    private static Gen<NotificationType> TypeGen() =>
        Gen.Elements(
            NotificationType.Mention,
            NotificationType.Assignment,
            NotificationType.ProposalOutcome,
            NotificationType.BoardChange,
            NotificationType.System);

    private static Gen<NotificationCadence> CadenceGen() =>
        Gen.Elements(NotificationCadence.Immediate, NotificationCadence.Digest);

    // ─────────────────────── NotificationDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property NotificationDto_RoundTrip_PreservesTitle()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(TypeGen()),
            Arb.From(CadenceGen()),
            (title, type, cadence) =>
            {
                var dto = new NotificationDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    type,
                    cadence,
                    title,
                    "Valid message",
                    "Card",
                    Guid.NewGuid(),
                    false,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<NotificationDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(title);
                deserialized.Type.Should().Be(type);
                deserialized.Cadence.Should().Be(cadence);
                deserialized.Id.Should().Be(dto.Id);
                deserialized.IsRead.Should().BeFalse();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NotificationDto_RoundTrip_PreservesMessage()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            message =>
            {
                var dto = new NotificationDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationType.System,
                    NotificationCadence.Immediate,
                    "Valid title",
                    message,
                    "Card",
                    Guid.NewGuid(),
                    false,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<NotificationDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Message.Should().Be(message);
                deserialized.UserId.Should().Be(dto.UserId);
            });
    }

    // ─────────────────────── CreateNotificationRequestDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CreateNotificationRequestDto_RoundTrip_Identity()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            Arb.From(NullableStringGen()),
            (title, message, sourceEntityType) =>
            {
                var dto = new CreateNotificationRequestDto(
                    Guid.NewGuid(),
                    NotificationType.System,
                    title,
                    message,
                    Guid.NewGuid(),
                    sourceEntityType,
                    Guid.NewGuid(),
                    "dedup-key");

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CreateNotificationRequestDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(title);
                deserialized.Message.Should().Be(message);
                deserialized.SourceEntityType.Should().Be(sourceEntityType);
                deserialized.UserId.Should().Be(dto.UserId);
            });
    }

    // ─────────────────────── NotificationDto with nullable fields ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property NotificationDto_WithNullableFields_RoundTrips()
    {
        return Prop.ForAll(
            Arb.From(NullableStringGen()),
            ArbMap.Default.ArbFor<bool>(),
            (sourceEntityType, isRead) =>
            {
                var readAt = isRead ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
                var dto = new NotificationDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    null, // null boardId
                    NotificationType.System,
                    NotificationCadence.Immediate,
                    "Title",
                    "Message",
                    sourceEntityType,
                    null, // null sourceEntityId
                    isRead,
                    readAt,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<NotificationDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.BoardId.Should().BeNull();
                deserialized.SourceEntityType.Should().Be(sourceEntityType);
                deserialized.SourceEntityId.Should().BeNull();
                deserialized.IsRead.Should().Be(isRead);
                if (isRead)
                    deserialized.ReadAt.Should().NotBeNull();
                else
                    deserialized.ReadAt.Should().BeNull();
            });
    }

    // ─────────────────────── Malformed JSON ───────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\": null, \"message\": null}")]
    [InlineData("{\"unknownField\": 42}")]
    [InlineData("{\"type\": 999}")]
    [InlineData("null")]
    public void NotificationDto_MalformedJson_HandledGracefully(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<NotificationDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Expected
        }
    }
}
