using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for Notification entity invariants.
/// Verifies title/message length boundaries, adversarial input handling,
/// and read/unread state machine transitions.
/// </summary>
public class NotificationPropertyTests
{
    private const int MaxTests = 200;

    // ─────────────────────── Generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("\u202E"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE notifications; --"),
        Gen.Constant("👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant(""),
        Gen.Constant(" "),
        Gen.Constant((string)null!),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<NotificationType> NotificationTypeGen() =>
        Gen.Elements(
            NotificationType.Mention,
            NotificationType.Assignment,
            NotificationType.ProposalOutcome,
            NotificationType.BoardChange,
            NotificationType.System);

    private static Gen<NotificationCadence> CadenceGen() =>
        Gen.Elements(NotificationCadence.Immediate, NotificationCadence.Digest);

    // ─────────────────────── Construction properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ValidParams_AlwaysCreatesNotification()
    {
        return Prop.ForAll(
            Arb.From(NotificationTypeGen()),
            Arb.From(CadenceGen()),
            (type, cadence) =>
            {
                var notification = new Notification(
                    Guid.NewGuid(), type, cadence, "Valid Title", "Valid message content");
                notification.Type.Should().Be(type);
                notification.Cadence.Should().Be(cadence);
                notification.Title.Should().Be("Valid Title");
                notification.Message.Should().Be("Valid message content");
                notification.IsRead.Should().BeFalse();
                notification.ReadAt.Should().BeNull();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyGuidUserId_AlwaysThrows()
    {
        var act = () => new Notification(
            Guid.Empty, NotificationType.System, NotificationCadence.Immediate,
            "Title", "Message");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        return true.ToProperty();
    }

    // ─────────────────────── Title boundary values ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(160)]
    [InlineData(161)]
    [InlineData(1000)]
    public void Notification_TitleLength_HandledCorrectly(int length)
    {
        var title = length == 0 ? "" : new string('t', length);
        var act = () => new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            title, "Valid message");

        if (length == 0 || length > 160)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var n = act();
            n.Title.Length.Should().Be(length);
        }
    }

    // ─────────────────────── Message boundary values ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(10_000)]
    public void Notification_MessageLength_HandledCorrectly(int length)
    {
        var message = length == 0 ? "" : new string('m', length);
        var act = () => new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            "Title", message);

        if (length == 0 || length > 2000)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var n = act();
            n.Message.Length.Should().Be(length);
        }
    }

    // ─────────────────────── Adversarial input handling ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialTitle()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            title =>
            {
                try
                {
                    _ = new Notification(
                        Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
                        title, "Valid message");
                }
                catch (DomainException)
                {
                    // Expected for invalid titles
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Notification constructor threw unexpected {ex.GetType().Name} for title: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialMessage()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            message =>
            {
                try
                {
                    _ = new Notification(
                        Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
                        "Valid Title", message);
                }
                catch (DomainException)
                {
                    // Expected for invalid messages
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Notification constructor threw unexpected {ex.GetType().Name} for message: {ex.Message}");
                }
            });
    }

    // ─────────────────────── SourceEntityType boundary ───────────────────────

    [Theory]
    [InlineData(51)]
    [InlineData(100)]
    public void SourceEntityType_ExceedingLimit_Throws(int length)
    {
        var sourceType = new string('s', length);
        var act = () => new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            "Title", "Message", sourceEntityType: sourceType);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void SourceEntityType_WithinLimit_Succeeds(int length)
    {
        var sourceType = new string('s', length);
        var n = new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            "Title", "Message", sourceEntityType: sourceType);
        n.SourceEntityType.Should().Be(sourceType);
    }

    // ─────────────────────── DeduplicationKey boundary ───────────────────────

    [Theory]
    [InlineData(201)]
    [InlineData(500)]
    public void DeduplicationKey_ExceedingLimit_Throws(int length)
    {
        var key = new string('k', length);
        var act = () => new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            "Title", "Message", deduplicationKey: key);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── Read/Unread state machine ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property MarkReadUnread_CycleIsIdempotent()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 10)),
            cycles =>
            {
                var n = new Notification(
                    Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
                    "Title", "Message");

                for (int i = 0; i < cycles; i++)
                {
                    n.MarkAsRead();
                    n.IsRead.Should().BeTrue();
                    n.ReadAt.Should().NotBeNull();

                    n.MarkAsUnread();
                    n.IsRead.Should().BeFalse();
                    n.ReadAt.Should().BeNull();
                }
            });
    }

    [Fact]
    public void MarkAsRead_WhenAlreadyRead_IsIdempotent()
    {
        var n = new Notification(
            Guid.NewGuid(), NotificationType.System, NotificationCadence.Immediate,
            "Title", "Message");
        n.MarkAsRead();
        var firstReadAt = n.ReadAt;

        // Second call should not change ReadAt
        n.MarkAsRead();
        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().Be(firstReadAt);
    }
}
