using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A short note written on day X intended for display on day X+1's morning open.
/// Each user may have at most one note per date.
/// </summary>
public class TomorrowNote : Entity
{
    public const int MaxTextLength = 500;

    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Text { get; private set; } = string.Empty;

    private TomorrowNote() : base()
    {
    }

    public TomorrowNote(Guid userId, DateOnly date, string text) : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (date == default)
            throw new DomainException(ErrorCodes.ValidationError, "Date is required");

        ValidateText(text);

        UserId = userId;
        Date = date;
        Text = text;
    }

    public void UpdateText(string text)
    {
        ValidateText(text);
        Text = text;
        Touch();
    }

    private static void ValidateText(string text)
    {
        if (text is null)
            throw new DomainException(ErrorCodes.ValidationError, "Text cannot be null");

        if (text.Length > MaxTextLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Text cannot exceed {MaxTextLength} characters");
    }
}
