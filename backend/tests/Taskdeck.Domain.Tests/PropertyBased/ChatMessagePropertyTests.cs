using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for ChatMessage entity invariants.
/// Verifies construction with adversarial content, role enumeration,
/// message type validation, and token usage boundaries.
/// </summary>
public class ChatMessagePropertyTests
{
    private const int MaxTests = 200;

    private static readonly string[] ValidMessageTypes =
    {
        "text", "proposal-reference", "error", "status", "degraded", "clarification"
    };

    // ─────────────────────── Generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("\u202E"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE messages; --"),
        Gen.Constant("👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant("\x01\x02\x03"),
        Gen.Constant(""),
        Gen.Constant(" "),
        Gen.Constant((string)null!),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string> ValidContentGen() =>
        Gen.Choose(1, 500)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements('a', 'b', 'c', '1', '2', ' ', '.', '!'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    private static Gen<ChatMessageRole> RoleGen() =>
        Gen.Elements(ChatMessageRole.User, ChatMessageRole.Assistant, ChatMessageRole.System);

    private static Gen<string> ValidMessageTypeGen() =>
        Gen.Elements(ValidMessageTypes);

    // ─────────────────────── Construction properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ValidParams_AlwaysCreatesMessage()
    {
        return Prop.ForAll(
            Arb.From(ValidContentGen()),
            Arb.From(RoleGen()),
            Arb.From(ValidMessageTypeGen()),
            (content, role, messageType) =>
            {
                var sessionId = Guid.NewGuid();
                var msg = new ChatMessage(sessionId, role, content, messageType);
                msg.SessionId.Should().Be(sessionId);
                msg.Role.Should().Be(role);
                msg.Content.Should().Be(content);
                msg.MessageType.Should().Be(messageType);
                msg.ProposalId.Should().BeNull();
                msg.TokenUsage.Should().BeNull();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceContent_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n")),
            content =>
            {
                var act = () => new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, content);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptySessionId_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(ValidContentGen()),
            content =>
            {
                var act = () => new ChatMessage(Guid.Empty, ChatMessageRole.User, content);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    // ─────────────────────── Adversarial content handling ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialContent()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            content =>
            {
                try
                {
                    _ = new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, content);
                }
                catch (DomainException)
                {
                    // Expected for invalid content
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"ChatMessage constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── MessageType validation ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property InvalidMessageType_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(
                "invalid", "TEXT", "Text", "execute", "delete",
                "<script>", "'; DROP TABLE --", "", " ")),
            messageType =>
            {
                var act = () => new ChatMessage(
                    Guid.NewGuid(), ChatMessageRole.User, "content", messageType);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    // ─────────────────────── TokenUsage boundary values ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property NegativeTokenUsage_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-1000, -1)),
            negUsage =>
            {
                var act = () => new ChatMessage(
                    Guid.NewGuid(), ChatMessageRole.User, "content",
                    tokenUsage: negUsage);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NonNegativeTokenUsage_AlwaysAccepted()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(0, 100_000)),
            usage =>
            {
                var msg = new ChatMessage(
                    Guid.NewGuid(), ChatMessageRole.User, "content",
                    tokenUsage: usage);
                msg.TokenUsage.Should().Be(usage);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property SetTokenUsage_NegativeAlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-1000, -1)),
            negUsage =>
            {
                var msg = new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, "content");
                var act = () => msg.SetTokenUsage(negUsage);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    // ─────────────────────── SetProposalId boundary ───────────────────────

    [Fact]
    public void SetProposalId_EmptyGuid_Throws()
    {
        var msg = new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, "content");
        var act = () => msg.SetProposalId(Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Property(MaxTest = MaxTests)]
    public Property SetProposalId_NonEmptyGuid_Succeeds()
    {
        return Prop.ForAll(
            Arb.From(Gen.Fresh(() => Guid.NewGuid())),
            proposalId =>
            {
                var msg = new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, "content");
                msg.SetProposalId(proposalId);
                msg.ProposalId.Should().Be(proposalId);
            });
    }

    // ─────────────────────── Adversarial DegradedReason ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Constructor_WithAdversarialDegradedReason_NeverThrowsUnhandled()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            reason =>
            {
                try
                {
                    _ = new ChatMessage(
                        Guid.NewGuid(), ChatMessageRole.Assistant, "content",
                        degradedReason: reason);
                }
                catch (DomainException)
                {
                    // Expected for some inputs
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException)
                {
                    throw new Exception(
                        $"ChatMessage constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── SQL injection stored verbatim ───────────────────────

    [Theory]
    [InlineData("'; DROP TABLE chat_messages; --")]
    [InlineData("\" OR 1=1 --")]
    public void SqlInjection_InContent_StoredAsLiteral(string content)
    {
        var msg = new ChatMessage(Guid.NewGuid(), ChatMessageRole.User, content);
        msg.Content.Should().Be(content, "SQL injection strings should be stored verbatim");
    }
}
