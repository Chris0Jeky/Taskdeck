using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class TomorrowNoteTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly DateOnly ValidDate = new(2026, 4, 25);

    #region Constructor validation

    [Fact]
    public void Constructor_ValidArgs_CreatesNote()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "Focus on shipping");

        note.UserId.Should().Be(ValidUserId);
        note.Date.Should().Be(ValidDate);
        note.Text.Should().Be("Focus on shipping");
        note.Id.Should().NotBe(Guid.Empty);
        note.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        note.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new TomorrowNote(Guid.Empty, ValidDate, "text");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*User ID*");
    }

    [Fact]
    public void Constructor_DefaultDate_Throws()
    {
        var act = () => new TomorrowNote(ValidUserId, default, "text");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*Date*");
    }

    [Fact]
    public void Constructor_NullText_Throws()
    {
        var act = () => new TomorrowNote(ValidUserId, ValidDate, null!);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*null*");
    }

    [Fact]
    public void Constructor_TextExceedsMaxLength_Throws()
    {
        var longText = new string('a', TomorrowNote.MaxTextLength + 1);

        var act = () => new TomorrowNote(ValidUserId, ValidDate, longText);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*500*");
    }

    [Fact]
    public void Constructor_EmptyText_Succeeds()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, string.Empty);

        note.Text.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_TextAtMaxLength_Succeeds()
    {
        var maxText = new string('a', TomorrowNote.MaxTextLength);

        var note = new TomorrowNote(ValidUserId, ValidDate, maxText);

        note.Text.Should().HaveLength(TomorrowNote.MaxTextLength);
    }

    #endregion

    #region UpdateText

    [Fact]
    public void UpdateText_ValidText_UpdatesTextAndTimestamp()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "original");
        var before = note.UpdatedAt;

        note.UpdateText("updated");

        note.Text.Should().Be("updated");
        note.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void UpdateText_EmptyText_Succeeds()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "original");

        note.UpdateText(string.Empty);

        note.Text.Should().BeEmpty();
    }

    [Fact]
    public void UpdateText_NullText_Throws()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "original");

        var act = () => note.UpdateText(null!);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateText_ExceedsMaxLength_Throws()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "original");
        var longText = new string('b', TomorrowNote.MaxTextLength + 1);

        var act = () => note.UpdateText(longText);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateText_AtMaxLength_Succeeds()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "original");
        var maxText = new string('c', TomorrowNote.MaxTextLength);

        note.UpdateText(maxText);

        note.Text.Should().HaveLength(TomorrowNote.MaxTextLength);
    }

    #endregion

    #region MaxTextLength constant

    [Fact]
    public void MaxTextLength_Is500()
    {
        TomorrowNote.MaxTextLength.Should().Be(500);
    }

    #endregion

    #region Date semantics

    [Fact]
    public void Constructor_DifferentDates_CreateDistinctNotes()
    {
        var note1 = new TomorrowNote(ValidUserId, new DateOnly(2026, 4, 24), "yesterday");
        var note2 = new TomorrowNote(ValidUserId, new DateOnly(2026, 4, 25), "today");

        note1.Date.Should().NotBe(note2.Date);
        note1.Id.Should().NotBe(note2.Id);
    }

    #endregion
}
