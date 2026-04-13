using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for KnowledgeDocument entity invariants.
/// Verifies title/content length boundaries, sourceUrl/tags validation,
/// archive lifecycle, and adversarial input handling.
/// </summary>
public class KnowledgeDocumentPropertyTests
{
    private const int MaxTests = 200;

    // ─────────────────────── Generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE knowledge; --"),
        Gen.Constant("👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant(""),
        Gen.Constant(" "),
        Gen.Constant((string)null!),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string> ValidTitleGen() =>
        Gen.Choose(1, 200)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements('a', 'b', 'c', '1', '2', ' ', '-'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    private static Gen<string> ValidContentGen() =>
        Gen.Choose(1, 500)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements('a', 'b', 'c', '1', '2', ' ', '.', '\n'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    // ─────────────────────── Construction properties ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ValidParams_AlwaysCreatesDocument()
    {
        return Prop.ForAll(
            Arb.From(ValidTitleGen()),
            Arb.From(ValidContentGen()),
            (title, content) =>
            {
                var doc = new KnowledgeDocument(
                    Guid.NewGuid(), title, content, KnowledgeSourceType.Manual);
                doc.Title.Should().Be(title);
                doc.Content.Should().Be(content);
                doc.SourceType.Should().Be(KnowledgeSourceType.Manual);
                doc.IsArchived.Should().BeFalse();
                doc.SourceUrl.Should().BeNull();
                doc.Tags.Should().BeNull();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyGuidUserId_AlwaysThrows()
    {
        var act = () => new KnowledgeDocument(
            Guid.Empty, "Title", "Content", KnowledgeSourceType.Manual);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        return true.ToProperty();
    }

    // ─────────────────────── Title boundary values ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(1000)]
    public void Title_BoundaryLength_HandledCorrectly(int length)
    {
        var title = length == 0 ? "" : new string('t', length);
        var act = () => new KnowledgeDocument(
            Guid.NewGuid(), title, "Valid content", KnowledgeSourceType.Manual);

        if (length == 0 || length > 200)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var doc = act();
            doc.Title.Length.Should().Be(length);
        }
    }

    // ─────────────────────── Content boundary values ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50_000)]
    [InlineData(50_001)]
    public void Content_BoundaryLength_HandledCorrectly(int length)
    {
        var content = length == 0 ? "" : new string('c', length);
        var act = () => new KnowledgeDocument(
            Guid.NewGuid(), "Title", content, KnowledgeSourceType.Manual);

        if (length == 0 || length > 50_000)
        {
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
        else
        {
            var doc = act();
            doc.Content.Length.Should().Be(length);
        }
    }

    // ─────────────────────── SourceUrl boundary ───────────────────────

    [Theory]
    [InlineData(2001)]
    [InlineData(5000)]
    public void SourceUrl_ExceedingLimit_Throws(int length)
    {
        var url = new string('u', length);
        var act = () => new KnowledgeDocument(
            Guid.NewGuid(), "Title", "Content", KnowledgeSourceType.Manual,
            sourceUrl: url);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2000)]
    public void SourceUrl_WithinLimit_Succeeds(int length)
    {
        var url = new string('u', length);
        var doc = new KnowledgeDocument(
            Guid.NewGuid(), "Title", "Content", KnowledgeSourceType.Manual,
            sourceUrl: url);
        doc.SourceUrl.Should().Be(url);
    }

    // ─────────────────────── Tags boundary ───────────────────────

    [Theory]
    [InlineData(2001)]
    [InlineData(5000)]
    public void Tags_ExceedingLimit_Throws(int length)
    {
        var tags = new string('t', length);
        var act = () => new KnowledgeDocument(
            Guid.NewGuid(), "Title", "Content", KnowledgeSourceType.Manual,
            tags: tags);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
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
                    _ = new KnowledgeDocument(
                        Guid.NewGuid(), title, "Valid content", KnowledgeSourceType.Manual);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"KnowledgeDocument constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Constructor_NeverThrowsUnhandled_OnAdversarialContent()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            content =>
            {
                try
                {
                    _ = new KnowledgeDocument(
                        Guid.NewGuid(), "Title", content, KnowledgeSourceType.Manual);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"KnowledgeDocument constructor threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── Archive lifecycle ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property ArchiveUnarchive_CyclePreservesContent()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 5)),
            cycles =>
            {
                var doc = new KnowledgeDocument(
                    Guid.NewGuid(), "Title", "Content", KnowledgeSourceType.Manual);

                for (int i = 0; i < cycles; i++)
                {
                    doc.Archive();
                    doc.IsArchived.Should().BeTrue();
                    doc.Unarchive();
                    doc.IsArchived.Should().BeFalse();
                }
                doc.Title.Should().Be("Title");
                doc.Content.Should().Be("Content");
            });
    }

    [Fact]
    public void Update_WhenArchived_Throws()
    {
        var doc = new KnowledgeDocument(
            Guid.NewGuid(), "Title", "Content", KnowledgeSourceType.Manual);
        doc.Archive();

        var act = () => doc.Update("NewTitle", "NewContent");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Property(MaxTest = MaxTests)]
    public Property Update_WithAdversarialInputs_NeverThrowsUnhandled()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (title, content) =>
            {
                var doc = new KnowledgeDocument(
                    Guid.NewGuid(), "OriginalTitle", "OriginalContent",
                    KnowledgeSourceType.Manual);
                try
                {
                    doc.Update(title, content);
                }
                catch (DomainException)
                {
                    // Expected
                }
                catch (Exception ex) when (ex is NullReferenceException or ArgumentException
                    or FormatException or IndexOutOfRangeException or OverflowException)
                {
                    throw new Exception(
                        $"KnowledgeDocument.Update threw unexpected {ex.GetType().Name}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── SQL injection stored verbatim ───────────────────────

    [Theory]
    [InlineData("'; DROP TABLE knowledge_documents; --")]
    [InlineData("\" OR 1=1 --")]
    public void SqlInjection_InTitle_StoredAsLiteral(string title)
    {
        var doc = new KnowledgeDocument(
            Guid.NewGuid(), title, "Content", KnowledgeSourceType.Manual);
        doc.Title.Should().Be(title, "SQL injection strings should be stored verbatim");
    }
}
