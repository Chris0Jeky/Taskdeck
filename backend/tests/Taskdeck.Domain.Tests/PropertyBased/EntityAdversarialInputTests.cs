using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Adversarial input tests for domain entity construction and mutation.
/// Exercises boundary conditions with unicode, control characters, script tags,
/// SQL injection patterns, null bytes, BOM, and surrogate pairs.
/// Key property: valid params always construct, invalid always throw DomainException,
/// and NO unhandled exceptions from any random input.
/// </summary>
public class EntityAdversarialInputTests
{
    private const int MaxTests = 200;

    // ─────────────────────── Adversarial string generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        // Unicode edge cases
        Gen.Constant("\u0000"),                        // null byte
        Gen.Constant("\uFEFF"),                        // BOM
        Gen.Constant("\uFFFD"),                        // replacement character
        Gen.Constant("\uD800"),                        // lone high surrogate (invalid)
        Gen.Constant("\uDBFF\uDFFF"),                  // max surrogate pair
        Gen.Constant("\u200B"),                        // zero-width space
        Gen.Constant("\u200E"),                        // left-to-right mark
        Gen.Constant("\u202E"),                        // right-to-left override
        Gen.Constant("\u0301"),                        // combining accent
        Gen.Constant("é"),                             // precomposed
        Gen.Constant("e\u0301"),                       // decomposed equivalent
        Gen.Constant("👨‍👩‍👧‍👦"),                // family emoji (multi-codepoint)
        Gen.Constant("𝕋𝕖𝕤𝕥"),                        // math bold symbols
        Gen.Constant("田中太郎"),                       // CJK
        Gen.Constant("مرحبا"),                         // Arabic RTL
        Gen.Constant("\u0E01\u0E38"),                   // Thai combining

        // Control characters
        Gen.Constant("\x01\x02\x03"),                  // ASCII control chars
        Gen.Constant("\x07"),                           // bell
        Gen.Constant("\x08"),                           // backspace
        Gen.Constant("\x1B[31m"),                       // ANSI escape
        Gen.Constant("\r\n\r\n"),                       // CRLF
        Gen.Constant("\t\t\t"),                         // tabs

        // XSS/injection payloads
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE boards; --"),
        Gen.Constant("\" OR 1=1 --"),
        Gen.Constant("<img src=x onerror=alert(1)>"),
        Gen.Constant("{{constructor.constructor('return this')()}}"),
        Gen.Constant("javascript:alert(1)"),
        Gen.Constant("data:text/html,<script>alert(1)</script>"),

        // Length boundary strings
        Gen.Constant(new string('a', 0)),              // empty
        Gen.Constant(" "),                             // single space
        Gen.Constant(new string('\t', 50)),            // many tabs
        Gen.Constant(new string('\n', 50)),            // many newlines

        // Arbitrary from FsCheck
        Arb.Generate<string>()
    );

    private static Gen<string> ValidNameGen(int maxLen) =>
        Gen.Choose(1, maxLen)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(
                    'a', 'b', 'c', 'X', 'Y', 'Z', '1', '2', '-', '_', ' ', '.'))
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    // ─────────────────────── Board adversarial tests ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Board_Constructor_NeverThrowsUnhandledException_OnAdversarialName()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            name =>
            {
                var act = () => new Board(name);
                // Must either succeed or throw DomainException — never ArgumentException,
                // NullReferenceException, FormatException, etc.
                try
                {
                    act();
                }
                catch (DomainException)
                {
                    // Expected for invalid input
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Board constructor threw unexpected {ex.GetType().Name} for input [{name?.Length ?? -1} chars]: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Board_Update_NeverThrowsUnhandledException_OnAdversarialStrings()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (name, description) =>
            {
                var board = new Board("SafeBoard");
                try
                {
                    board.Update(name: name, description: description);
                }
                catch (DomainException)
                {
                    // Expected for invalid input
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Board.Update threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE boards; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("{{constructor.constructor('return this')()}}")]
    [InlineData("Board\x00Name")]          // null byte in middle
    [InlineData("\uFEFFBoard")]            // BOM prefix
    [InlineData("Board\u200BName")]        // zero-width space
    [InlineData("Board\u202EemaN")]        // RTL override
    [InlineData("田中太郎のボード")]         // CJK
    [InlineData("مرحبا")]                  // Arabic
    [InlineData("👨‍👩‍👧‍👦")]            // Multi-codepoint emoji
    [InlineData("e\u0301")]                // Combining character
    public void Board_AcceptsOrRejectsAdversarialName_WithoutCrash(string name)
    {
        var act = () => new Board(name);

        // Either succeeds (name within length and non-whitespace) or throws DomainException
        try
        {
            var board = act();
            board.Name.Should().NotBeNullOrEmpty();
        }
        catch (DomainException ex)
        {
            ex.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        }
    }

    // ─────────────────────── Card adversarial tests ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Card_Constructor_NeverThrowsUnhandledException_OnAdversarialTitle()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            title =>
            {
                var boardId = Guid.NewGuid();
                var columnId = Guid.NewGuid();
                try
                {
                    _ = new Card(boardId, columnId, title);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Card constructor threw unexpected {ex.GetType().Name} for title [{title?.Length ?? -1} chars]: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Card_Update_NeverThrowsUnhandledException_OnAdversarialDescription()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            desc =>
            {
                var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "SafeTitle");
                try
                {
                    card.Update(description: desc);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Card.Update threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(10_000)]
    public void Card_Title_BoundaryLength_HandledCorrectly(int length)
    {
        var title = length == 0 ? "" : new string('x', length);
        var act = () => new Card(Guid.NewGuid(), Guid.NewGuid(), title);

        if (length == 0 || length > 200)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var card = act();
            card.Title.Length.Should().Be(length);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(50_000)]
    public void Card_Description_BoundaryLength_HandledCorrectly(int length)
    {
        var desc = new string('d', length);
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");

        if (length > 2000)
        {
            var act = () => card.Update(description: desc);
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            card.Update(description: desc);
            card.Description.Length.Should().Be(length);
        }
    }

    [Property(MaxTest = MaxTests)]
    public Property Card_Block_NeverThrowsUnhandled_OnAdversarialReason()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            reason =>
            {
                var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
                try
                {
                    card.Block(reason);
                }
                catch (DomainException)
                {
                    // Expected for empty/whitespace reasons
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException)
                {
                    throw new Exception(
                        $"Card.Block threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── Column adversarial tests ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Column_Constructor_NeverThrowsUnhandled_OnAdversarialName()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            name =>
            {
                try
                {
                    _ = new Column(Guid.NewGuid(), name, 0);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Column constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(1000)]
    public void Column_Name_BoundaryLength_HandledCorrectly(int length)
    {
        var name = length == 0 ? "" : new string('c', length);
        var act = () => new Column(Guid.NewGuid(), name, 0);

        if (length == 0 || length > 50)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var col = act();
            col.Name.Length.Should().Be(length);
        }
    }

    [Property(MaxTest = MaxTests)]
    public Property Column_WipLimit_BoundaryValues()
    {
        return Prop.ForAll(
            Arb.From<int>(),
            wipLimit =>
            {
                try
                {
                    _ = new Column(Guid.NewGuid(), "Col", 0, wipLimit);
                }
                catch (DomainException ex)
                {
                    // WIP limit must be > 0
                    ex.ErrorCode.Should().Be(ErrorCodes.ValidationError);
                    wipLimit.Should().BeLessThanOrEqualTo(0);
                }
            });
    }

    // ─────────────────────── Label adversarial tests ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Label_Constructor_NeverThrowsUnhandled_OnAdversarialInputs()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (name, colorHex) =>
            {
                try
                {
                    _ = new Label(Guid.NewGuid(), name, colorHex);
                }
                catch (DomainException)
                {
                    // Expected for invalid name or color
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"Label constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#ffffff", true)]
    [InlineData("#ABC123", true)]
    [InlineData("", false)]
    [InlineData("#GGG000", false)]
    [InlineData("#12345", false)]
    [InlineData("#1234567", false)]
    [InlineData("000000", false)]
    [InlineData("red", false)]
    [InlineData("<script>", false)]
    [InlineData("#\x00\x00\x00\x00\x00\x00", false)]
    public void Label_ColorHex_ValidationBoundaries(string colorHex, bool shouldSucceed)
    {
        if (shouldSucceed)
        {
            var label = new Label(Guid.NewGuid(), "ValidName", colorHex);
            label.ColorHex.Should().Be(colorHex.ToUpperInvariant());
        }
        else
        {
            var act = () => new Label(Guid.NewGuid(), "ValidName", colorHex);
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
    }

    // ─────────────────────── AutomationProposal adversarial tests ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property Proposal_Constructor_NeverThrowsUnhandled_OnAdversarialSummary()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            summary =>
            {
                try
                {
                    _ = new AutomationProposal(
                        ProposalSourceType.Chat,
                        Guid.NewGuid(),
                        summary,
                        RiskLevel.Low,
                        Guid.NewGuid().ToString());
                }
                catch (DomainException)
                {
                    // Expected for empty/too-long summary
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"AutomationProposal constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(100_000)]
    public void Proposal_Summary_BoundaryLength_HandledCorrectly(int length)
    {
        var summary = length == 0 ? "" : new string('s', length);
        var act = () => new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            summary,
            RiskLevel.Low,
            Guid.NewGuid().ToString());

        if (length == 0 || length > 500)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var proposal = act();
            proposal.Summary.Length.Should().Be(length);
        }
    }

    // ─────────────────────── Entity base class property tests ───────────────────────

    [Fact]
    public void Entity_Touch_AdvancesUpdatedAt()
    {
        var board = new Board("TestBoard");
        var initialUpdatedAt = board.UpdatedAt;

        // Touch is called by Update
        Thread.Sleep(1); // Ensure clock ticks
        board.Update(name: "NewName");

        board.UpdatedAt.Should().BeOnOrAfter(initialUpdatedAt);
    }

    [Fact]
    public void Entity_ConstructorAlways_SetsNonEmptyId()
    {
        var board = new Board("Board");
        board.Id.Should().NotBeEmpty();
        board.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        board.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ─────────────────────── Cross-entity: SQL injection in names ───────────────────────

    [Theory]
    [InlineData("'; DROP TABLE boards; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("Robert'); DROP TABLE students;--")]
    [InlineData("1; SELECT * FROM users")]
    [InlineData("UNION SELECT password FROM users")]
    public void SqlInjection_InBoardName_StoredAsLiteral_NotExecuted(string name)
    {
        // These are within the 100 char limit and non-whitespace,
        // so the domain should accept them as literal strings.
        var board = new Board(name);
        board.Name.Should().Be(name, "SQL injection strings should be stored verbatim");
    }

    [Theory]
    [InlineData("'; DROP TABLE cards; --")]
    [InlineData("\" OR 1=1 --")]
    public void SqlInjection_InCardTitle_StoredAsLiteral_NotExecuted(string title)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), title);
        card.Title.Should().Be(title, "SQL injection strings should be stored verbatim");
    }

    // ─────────────────────── Card: position boundary values ───────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Card_SetPosition_Negative_Throws(int position)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var act = () => card.SetPosition(position);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Card_SetPosition_NonNegative_Succeeds(int position)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.SetPosition(position);
        card.Position.Should().Be(position);
    }

    // ─────────────────────── Board: Guid.Empty in constructor ───────────────────────

    [Fact]
    public void Board_EmptyGuidOwnerId_Throws()
    {
        var act = () => new Board("Name", ownerId: Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Card_EmptyGuidCardId_Throws()
    {
        var act = () => new Card(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Title");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── Proposal: expiryMinutes boundary ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Proposal_ExpiryMinutes_ZeroOrNegative_Throws(int expiryMinutes)
    {
        var act = () => new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Summary",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: expiryMinutes);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    [InlineData(int.MaxValue)]
    public void Proposal_ExpiryMinutes_Positive_Succeeds(int expiryMinutes)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Summary",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            expiryMinutes: expiryMinutes);

        proposal.Status.Should().Be(ProposalStatus.PendingReview);
    }
}
