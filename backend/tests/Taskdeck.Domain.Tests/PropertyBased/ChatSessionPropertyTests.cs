using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for ChatSession entity invariants.
/// Verifies construction, title validation, and state machine transitions
/// hold for all generated inputs.
/// </summary>
public class ChatSessionPropertyTests
{
    private const int MaxTests = 200;

    // ─────────────────────── Generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("\u202E"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE sessions; --"),
        Gen.Constant("👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("\x01\x02\x03"),
        Gen.Constant("\x1B[31m"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant(""),
        Gen.Constant(" "),
        Gen.Constant("\t"),
        Gen.Constant((string)null!),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Arbitrary<string> ValidTitleArb()
    {
        var gen = Gen.Choose(1, 200)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(
                    'a', 'b', 'c', 'X', 'Y', '1', '2', ' ', '-', '_'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }

    // ─────────────────────── Construction properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ValidTitle_AlwaysCreatesChatSession()
    {
        return Prop.ForAll(
            ValidTitleArb(),
            title =>
            {
                var session = new ChatSession(Guid.NewGuid(), title);
                session.Title.Should().Be(title);
                session.Status.Should().Be(ChatSessionStatus.Active);
                session.Id.Should().NotBeEmpty();
                session.Messages.Should().BeEmpty();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceTitle_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n", "  \t\n  ")),
            title =>
            {
                var act = () => new ChatSession(Guid.NewGuid(), title);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property TitleExceeding200Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(201, 500).Select(len => new string('t', len))),
            longTitle =>
            {
                var act = () => new ChatSession(Guid.NewGuid(), longTitle);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyGuidUserId_AlwaysThrows()
    {
        return Prop.ForAll(
            ValidTitleArb(),
            title =>
            {
                var act = () => new ChatSession(Guid.Empty, title);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    // ─────────────────────── Adversarial title handling ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialTitle()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            title =>
            {
                try
                {
                    _ = new ChatSession(Guid.NewGuid(), title);
                }
                catch (DomainException)
                {
                    // Expected for invalid titles
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"ChatSession constructor threw unexpected {ex.GetType().Name} for title [{title?.Length ?? -1} chars]: {ex.Message}");
                }
            });
    }

    // ─────────────────────── UpdateTitle properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property UpdateTitle_NeverThrowsUnhandled_OnAdversarialTitle()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            newTitle =>
            {
                var session = new ChatSession(Guid.NewGuid(), "OriginalTitle");
                try
                {
                    session.UpdateTitle(newTitle);
                }
                catch (DomainException)
                {
                    // Expected for invalid titles
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"ChatSession.UpdateTitle threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property UpdateTitle_PreservesStatusAndMessages()
    {
        return Prop.ForAll(
            ValidTitleArb(),
            newTitle =>
            {
                var session = new ChatSession(Guid.NewGuid(), "OriginalTitle");
                var originalStatus = session.Status;
                session.UpdateTitle(newTitle);
                session.Title.Should().Be(newTitle);
                session.Status.Should().Be(originalStatus);
                session.Messages.Should().BeEmpty();
            });
    }

    // ─────────────────────── State machine properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ArchiveReactivate_CyclePreservesTitle()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 10)),
            cycles =>
            {
                var session = new ChatSession(Guid.NewGuid(), "TestSession");
                for (int i = 0; i < cycles; i++)
                {
                    session.Archive();
                    session.Status.Should().Be(ChatSessionStatus.Archived);

                    session.Reactivate();
                    session.Status.Should().Be(ChatSessionStatus.Active);
                }
                session.Title.Should().Be("TestSession");
            });
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_ThrowsDomainException()
    {
        var session = new ChatSession(Guid.NewGuid(), "Test");
        session.Archive();
        var act = () => session.Archive();
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Reactivate_WhenAlreadyActive_ThrowsDomainException()
    {
        var session = new ChatSession(Guid.NewGuid(), "Test");
        var act = () => session.Reactivate();
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    // ─────────────────────── SQL injection in titles stored verbatim ───────────────────────

    [Theory]
    [InlineData("'; DROP TABLE chat_sessions; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("Robert'); DROP TABLE students;--")]
    public void SqlInjection_InTitle_StoredAsLiteral(string title)
    {
        var session = new ChatSession(Guid.NewGuid(), title);
        session.Title.Should().Be(title, "SQL injection strings should be stored verbatim");
    }
}
