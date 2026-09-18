from pathlib import Path


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one {label} replacement, found {count}")
    return source.replace(old, new, 1)


service_path = Path("backend/src/Taskdeck.Application/Services/CardService.cs")
service = service_path.read_text(encoding="utf-8-sig")
service = replace_once(
    service,
    "    public async Task<Result<CardDto>> CreateCardAsync(\n"
    "        CreateCardDto dto,\n"
    "        Guid? cardId,\n"
    "        CancellationToken cancellationToken = default)\n"
    "    {\n"
    "        return await CreateCardAsync(dto, cardId, actorUserId: null, cancellationToken);\n"
    "    }\n\n"
    "    public async Task<Result<CardDto>> CreateCardAsync(\n"
    "        CreateCardDto dto,\n"
    "        Guid? cardId,\n"
    "        Guid? actorUserId,\n"
    "        CancellationToken cancellationToken = default)\n"
    "    {\n"
    "        try\n"
    "        {\n",
    "    public async Task<Result<CardDto>> CreateCardAsync(\n"
    "        CreateCardDto dto,\n"
    "        Guid? cardId,\n"
    "        CancellationToken cancellationToken = default,\n"
    "        IBoardRealtimeNotifier? notificationSink = null)\n"
    "    {\n"
    "        return await CreateCardAsync(dto, cardId, actorUserId: null, cancellationToken, notificationSink);\n"
    "    }\n\n"
    "    public async Task<Result<CardDto>> CreateCardAsync(\n"
    "        CreateCardDto dto,\n"
    "        Guid? cardId,\n"
    "        Guid? actorUserId,\n"
    "        CancellationToken cancellationToken = default,\n"
    "        IBoardRealtimeNotifier? notificationSink = null)\n"
    "    {\n"
    "        var notifier = notificationSink ?? _realtimeNotifier;\n"
    "        try\n"
    "        {\n",
    "card-create overload seam",
)
service = replace_once(
    service,
    "            await _realtimeNotifier.NotifyBoardMutationAsync(\n"
    "                new BoardRealtimeEvent(card.BoardId, \"card\", \"created\", card.Id, DateTimeOffset.UtcNow),\n"
    "                cancellationToken);\n",
    "            await notifier.NotifyBoardMutationAsync(\n"
    "                new BoardRealtimeEvent(card.BoardId, \"card\", \"created\", card.Id, DateTimeOffset.UtcNow),\n"
    "                cancellationToken);\n",
    "card-created notification seam",
)
service_path.write_text(service, encoding="utf-8-sig", newline="\n")

registry_path = Path("backend/src/Taskdeck.Application/Services/Pipeline/OperationHandlerRegistry.cs")
registry = registry_path.read_text(encoding="utf-8")
registry = replace_once(
    registry,
    "            case \"create\":\n"
    "                return await CreateCardAsync(parameters, operation.TargetId, cancellationToken);\n",
    "            case \"create\":\n"
    "                return await CreateCardAsync(parameters, operation.TargetId, cancellationToken,\n"
    "                    deferredNotifications);\n",
    "create dispatch seam",
)
registry = replace_once(
    registry,
    "    private async Task<Result> CreateCardAsync(\n"
    "        JsonElement parameters,\n"
    "        string? targetId,\n"
    "        CancellationToken cancellationToken)\n",
    "    private async Task<Result> CreateCardAsync(\n"
    "        JsonElement parameters,\n"
    "        string? targetId,\n"
    "        CancellationToken cancellationToken,\n"
    "        DeferredBoardRealtimeNotifier? deferredNotifications)\n",
    "private create handler seam",
)
registry = replace_once(
    registry,
    "        var result = await _cardService.CreateCardAsync(dto, cardId, cancellationToken);\n",
    "        var result = await _cardService.CreateCardAsync(\n"
    "            dto,\n"
    "            cardId,\n"
    "            cancellationToken,\n"
    "            notificationSink: deferredNotifications);\n",
    "card service create call",
)
registry_path.write_text(registry, encoding="utf-8", newline="\n")

Path(".github/workflows/pr-3189-branch-patch.yml").unlink()
Path("scripts/ci/pr3189_patch.py").unlink()
