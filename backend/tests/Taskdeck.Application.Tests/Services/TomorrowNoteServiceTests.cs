using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class TomorrowNoteServiceTests
{
    private readonly Mock<ITomorrowNoteRepository> _repositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly TomorrowNoteService _service;

    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly DateOnly ValidDate = new(2026, 4, 25);

    public TomorrowNoteServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<TomorrowNote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TomorrowNote note, CancellationToken _) => note);

        _service = new TomorrowNoteService(_repositoryMock.Object, _unitOfWorkMock.Object);
    }

    #region GetNoteAsync

    [Fact]
    public async Task GetNoteAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.GetNoteAsync(Guid.Empty, ValidDate);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetNoteAsync_NoteNotFound_ReturnsSuccessWithNull()
    {
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TomorrowNote?)null);

        var result = await _service.GetNoteAsync(ValidUserId, ValidDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetNoteAsync_NoteExists_ReturnsResponse()
    {
        var note = new TomorrowNote(ValidUserId, ValidDate, "Focus on tests");
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(note);

        var result = await _service.GetNoteAsync(ValidUserId, ValidDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Text.Should().Be("Focus on tests");
        result.Value.Date.Should().Be(ValidDate);
        result.Value.Id.Should().Be(note.Id);
    }

    #endregion

    #region SaveNoteAsync — create new

    [Fact]
    public async Task SaveNoteAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.SaveNoteAsync(Guid.Empty, ValidDate, "text");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SaveNoteAsync_DefaultDate_ReturnsValidationError()
    {
        var result = await _service.SaveNoteAsync(ValidUserId, default, "text");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Date");
    }

    [Fact]
    public async Task SaveNoteAsync_NullText_ReturnsValidationError()
    {
        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, null!);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SaveNoteAsync_TextExceedsMaxLength_ReturnsValidationError()
    {
        var longText = new string('a', TomorrowNote.MaxTextLength + 1);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, longText);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task SaveNoteAsync_NewNote_CreatesAndReturnsResponse()
    {
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TomorrowNote?)null);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, "New note");

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("New note");
        result.Value.Date.Should().Be(ValidDate);

        _repositoryMock.Verify(
            r => r.AddAsync(It.Is<TomorrowNote>(n => n.Text == "New note" && n.Date == ValidDate),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveNoteAsync_EmptyText_Succeeds()
    {
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TomorrowNote?)null);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().BeEmpty();
    }

    #endregion

    #region SaveNoteAsync — race-condition recovery

    [Fact]
    public async Task SaveNoteAsync_RaceConditionConflict_RefetchesAndUpdatesWinner()
    {
        // Simulate: first GetByUserAndDateAsync returns null (no existing note),
        // second call (re-fetch after save) returns a different note (the race winner).
        var winnerNote = new TomorrowNote(ValidUserId, ValidDate, "winner text");
        var callCount = 0;

        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? null : winnerNote;
            });

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, "loser text that should win via last-writer-wins");

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(winnerNote.Id);
        result.Value.Text.Should().Be("loser text that should win via last-writer-wins");

        _repositoryMock.Verify(
            r => r.UpdateAsync(winnerNote, It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region SaveNoteAsync — update existing

    [Fact]
    public async Task SaveNoteAsync_ExistingNote_UpdatesText()
    {
        var existing = new TomorrowNote(ValidUserId, ValidDate, "original");
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, "updated");

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("updated");
        result.Value.Id.Should().Be(existing.Id);

        _repositoryMock.Verify(
            r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<TomorrowNote>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveNoteAsync_ExistingNote_UpdateToEmptyText_Succeeds()
    {
        var existing = new TomorrowNote(ValidUserId, ValidDate, "had content");
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, string.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().BeEmpty();
    }

    #endregion

    #region SaveNoteAsync — text at max length boundary

    [Fact]
    public async Task SaveNoteAsync_TextAtMaxLength_Succeeds()
    {
        var maxText = new string('z', TomorrowNote.MaxTextLength);
        _repositoryMock
            .Setup(r => r.GetByUserAndDateAsync(ValidUserId, ValidDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TomorrowNote?)null);

        var result = await _service.SaveNoteAsync(ValidUserId, ValidDate, maxText);

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().HaveLength(TomorrowNote.MaxTextLength);
    }

    #endregion
}
