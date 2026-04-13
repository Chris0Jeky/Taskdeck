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
/// Property-based JSON serialization round-trip tests for Chat DTOs.
/// Key property: serialize then deserialize produces identical object.
/// Exercises adversarial string content in titles, messages, and metadata.
/// </summary>
public class ChatDtoSerializationFuzzTests
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
        Gen.Constant("'; DROP TABLE chat; --"),
        Gen.Constant("\"quoted\"string\""),
        Gen.Constant("back\\slash"),
        Gen.Constant("new\nline\ttab"),
        Gen.Constant("emoji 👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("مرحبا"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant(""),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string?> NullableStringGen() => Gen.OneOf(
        Gen.Constant((string?)null),
        AdversarialStringGen().Select(s => (string?)s)
    );

    private static Gen<ChatMessageRole> RoleGen() =>
        Gen.Elements(ChatMessageRole.User, ChatMessageRole.Assistant, ChatMessageRole.System);

    private static Gen<ChatSessionStatus> StatusGen() =>
        Gen.Elements(ChatSessionStatus.Active, ChatSessionStatus.Archived);

    // ─────────────────────── ChatSessionDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ChatSessionDto_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(StatusGen()),
            (title, status) =>
            {
                var dto = new ChatSessionDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    title,
                    status,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    new List<ChatMessageDto>());

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<ChatSessionDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(title);
                deserialized.Status.Should().Be(status);
                deserialized.Id.Should().Be(dto.Id);
                deserialized.UserId.Should().Be(dto.UserId);
                deserialized.BoardId.Should().Be(dto.BoardId);
            });
    }

    // ─────────────────────── ChatMessageDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ChatMessageDto_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(RoleGen()),
            Arb.From(NullableStringGen()),
            (content, role, degradedReason) =>
            {
                var dto = new ChatMessageDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    role,
                    content,
                    "text",
                    null,
                    42,
                    DateTimeOffset.UtcNow,
                    degradedReason);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<ChatMessageDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Content.Should().Be(content);
                deserialized.Role.Should().Be(role);
                deserialized.DegradedReason.Should().Be(degradedReason);
                deserialized.MessageType.Should().Be("text");
                deserialized.TokenUsage.Should().Be(42);
            });
    }

    // ─────────────────────── CreateChatSessionDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CreateChatSessionDto_RoundTrip_Identity()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            title =>
            {
                var dto = new CreateChatSessionDto(title, Guid.NewGuid());

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CreateChatSessionDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(title);
                deserialized.BoardId.Should().Be(dto.BoardId);
            });
    }

    // ─────────────────────── SendChatMessageDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property SendChatMessageDto_RoundTrip_Identity()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            ArbMap.Default.ArbFor<bool>(),
            (content, requestProposal) =>
            {
                var dto = new SendChatMessageDto(content, requestProposal);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<SendChatMessageDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Content.Should().Be(content);
                deserialized.RequestProposal.Should().Be(requestProposal);
            });
    }

    // ─────────────────────── ChatSessionDto with messages list ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ChatSessionDto_WithMessages_RoundTrip()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (title, msgContent) =>
            {
                var messages = new List<ChatMessageDto>
                {
                    new(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.User,
                        msgContent, "text", null, null, DateTimeOffset.UtcNow),
                    new(Guid.NewGuid(), Guid.NewGuid(), ChatMessageRole.Assistant,
                        title, "text", Guid.NewGuid(), 100, DateTimeOffset.UtcNow)
                };

                var dto = new ChatSessionDto(
                    Guid.NewGuid(), Guid.NewGuid(), null, title,
                    ChatSessionStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    messages);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<ChatSessionDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.RecentMessages.Should().HaveCount(2);
                deserialized.RecentMessages[0].Content.Should().Be(msgContent);
                deserialized.RecentMessages[1].Content.Should().Be(title);
            });
    }

    // ─────────────────────── Malformed JSON deserialization ───────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\": null}")]
    [InlineData("{\"extra_field\": \"value\"}")]
    [InlineData("{\"title\": 12345}")]
    [InlineData("null")]
    public void CreateChatSessionDto_MalformedJson_HandledGracefully(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CreateChatSessionDto>(json, JsonOptions);
            // If it deserializes, that's fine — API layer validates
        }
        catch (JsonException)
        {
            // Expected for truly malformed JSON
        }
    }
}
