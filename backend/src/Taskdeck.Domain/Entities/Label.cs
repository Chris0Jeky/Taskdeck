using System.Text.RegularExpressions;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public partial class Label : Entity
{
    private string _name = string.Empty;
    private string _colorHex = string.Empty;
    private readonly List<CardLabel> _cardLabels = new();

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

    public string Name
    {
        get => _name;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Label name cannot be empty");

            if (value.Length > 30)
                throw new DomainException(ErrorCodes.ValidationError, "Label name cannot exceed 30 characters");

            _name = value;
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        private set
        {
            if (!IsValidHexColor(value))
                throw new DomainException(ErrorCodes.ValidationError, "ColorHex must be a valid hex color in format #RRGGBB");

            _colorHex = value.ToUpperInvariant();
        }
    }

    public IReadOnlyCollection<CardLabel> CardLabels => _cardLabels.AsReadOnly();

    private Label() : base() { }

    public Label(Guid boardId, string name, string colorHex) : base()
    {
        BoardId = boardId;
        Name = name;
        ColorHex = colorHex;
    }

    public void Update(string? name = null, string? colorHex = null)
    {
        if (name != null)
            Name = name;

        if (colorHex != null)
            ColorHex = colorHex;

        Touch();
    }

    private static bool IsValidHexColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return false;

        return HexColorRegex().IsMatch(color);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    // Navigation properties management
    internal void AddCardLabel(CardLabel cardLabel)
    {
        _cardLabels.Add(cardLabel);
    }

    internal void RemoveCardLabel(CardLabel cardLabel)
    {
        _cardLabels.Remove(cardLabel);
    }
}
