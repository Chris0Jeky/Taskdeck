namespace Taskdeck.Domain.Entities;

public class CardLabel
{
    public Guid CardId { get; private set; }
    public Card Card { get; internal set; } = null!;

    public Guid LabelId { get; private set; }
    public Label Label { get; internal set; } = null!;

    private CardLabel() { }

    public CardLabel(Guid cardId, Guid labelId)
    {
        CardId = cardId;
        LabelId = labelId;
    }
}
