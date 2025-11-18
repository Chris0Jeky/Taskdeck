namespace Taskdeck.Domain.Entities;

public class CardLabel
{
    public Guid CardId { get; private set; }
    public Card Card { get; private set; } = null!;

    public Guid LabelId { get; private set; }
    public Label Label { get; private set; } = null!;

    private CardLabel() { }

    public CardLabel(Guid cardId, Guid labelId)
    {
        CardId = cardId;
        LabelId = labelId;
    }
}
