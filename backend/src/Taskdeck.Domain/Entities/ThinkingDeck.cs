using System.Text.Json;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public sealed record ThinkingItem(Guid Id, string Text, bool Completed = false);
public sealed record ThinkingLayer(Guid Id, string Kind, string Title, string Body,
    IReadOnlyList<ThinkingItem> Items, Guid? SelectedOptionId = null);

/// <summary>Optional human-authored working material. Array order is presentation order.</summary>
public sealed class ThinkingDeck
{
    public Guid CardId { get; private set; }
    public long Revision { get; private set; }
    public int SchemaVersion { get; private set; } = 1;
    public string LayersJson { get; private set; } = "[]";

    private ThinkingDeck() { }
    public ThinkingDeck(Guid cardId) { CardId = cardId; }

    public IReadOnlyList<ThinkingLayer> ReadLayers() =>
        JsonSerializer.Deserialize<List<ThinkingLayer>>(LayersJson)!;

    public void Replace(IReadOnlyList<ThinkingLayer> layers)
    {
        if (layers is null || layers.Count > 40)
            throw new DomainException(ErrorCodes.ValidationError, "Use at most 40 thinking layers.");
        var ids = new HashSet<Guid>();
        foreach (var layer in layers)
        {
            if (layer is null || layer.Id == Guid.Empty || !ids.Add(layer.Id) ||
                layer.Kind is not ("note" or "question" or "options" or "steps" or "thread") ||
                layer.Title is null || layer.Title.Length > 200 || layer.Body is null || layer.Body.Length > 8000 ||
                layer.Items is null || layer.Items.Count > 50)
                throw new DomainException(ErrorCodes.ValidationError, "Invalid thinking layer, type, ID or text length.");
            var itemIds = new HashSet<Guid>();
            foreach (var item in layer.Items)
                if (item is null || item.Id == Guid.Empty || !itemIds.Add(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 2000)
                    throw new DomainException(ErrorCodes.ValidationError, "Invalid thinking item.");
            if (layer.Kind is "note" or "question" && layer.Items.Count != 0 ||
                layer.SelectedOptionId.HasValue && (layer.Kind != "options" || !itemIds.Contains(layer.SelectedOptionId.Value)) ||
                layer.Kind != "steps" && layer.Items.Any(item => item.Completed))
                throw new DomainException(ErrorCodes.ValidationError, "Thinking item state does not match its layer.");
        }
        var json = JsonSerializer.Serialize(layers);
        if (json.Length > 100000)
            throw new DomainException(ErrorCodes.ValidationError, "Thinking deck exceeds 100,000 characters.");
        LayersJson = json;
        Revision++;
    }
}
