# Proposal Operation Vocabulary Agent Interface

Entry points:

- `backend/src/Taskdeck.Application/Services/Pipeline/OperationHandlerRegistry.cs`: apply-time vocabulary and parameter-to-service mapping.
- `backend/src/Taskdeck.Application/Services/Pipeline/ProposalOperationContractValidator.cs`: shared preview/apply parameter semantics and board-scope binding.
- `backend/src/Taskdeck.Application/Services/AutomationProposalService.cs`: original and revision-aware human-readable preview.
- `backend/src/Taskdeck.Application/Services/Tools/ProposeCreateCardExecutor.cs` and `ProposeUpdateCardExecutor.cs`: chat proposal construction using label names.
- `backend/src/Taskdeck.Api/Mcp/WriteTools.cs`: public MCP proposal construction using label IDs.
- `backend/src/Taskdeck.Application/Services/ProposalOperationInputValidator.cs`: create-boundary shape and JSON safety checks.

Card operations:

- `create`: `boardId`, `columnId`, `title`; optional `description`, `dueDate`, and exactly one replacement-label representation: name array `labels` or UUID-string array `labelIds`.
- `update`: `cardId`; at least one of `title`, `description`, `dueDate`, `clearDueDate`, `labels`, or `labelIds`. At the operation-payload layer, an explicit null `dueDate` clears it. The chat tool treats `due_date: null` as omitted and emits clearing only for `clear_due_date: true`. `dueDate` and a true `clearDueDate` are mutually exclusive.
- `move`: `cardId`, `columnId`.
- `archive`: `cardId`. This legacy identity permanently retains Block semantics: applying it marks the card blocked with the generated reason `Archived by an approved proposal.` The preview shows that exact blocked-state transition before approval. Existing approvals and the legacy MCP `archive_card` and chat `propose_archive_card` tools keep this contract.
- `archive-lifecycle` / `restore-lifecycle`: `cardId`, `expectedUpdatedAt` (the displayed card timestamp). Archive hides the retained card from active work; restore returns it to its original column/position, subject to that column's WIP limit measured against the occupancy the same proposal's earlier operations produce, so a restore behind a card create or move that takes the last slot is refused at preview instead of failing mid-apply (`#2926`). A restore cannot sit behind another lifecycle operation: `ProposalHierarchyValidator` admits at most one hierarchy-affecting operation per proposal, so batch archive/restore in a single proposal is refused outright and the projection's lifecycle deltas are carried only against that gate being relaxed later. Preview and Apply reject stale timestamps, wrong archive state, unavailable columns, and another operation on the same card in the same proposal. The separate MCP `archive_card_lifecycle` / `restore_archived_card` tools only create proposals; explicit approval and Apply remain required. Both names are in `SideEffectAnalyzer.CardMutatingActions`, so the review Cards row discloses the board mutation, and Apply persists exactly one `Archived`/`Unarchived` audit row written by `ExecutionAuditRecorder` with the proposal provenance (the handler passes `recordLifecycleAudit: false` so `CardService.SetArchivedAsync` does not stage a second row); direct API archive/restore keeps its own single actor-stamped row (`#2939`). The realtime `card.archived`/`card.restored` event (and the `card.updated` events for children detached by an archive) is likewise staged on the apply lane: the handler passes the executor's `DeferredBoardRealtimeNotifier` as `notificationSink`, and `AutomationExecutorService` publishes the buffer only after `CommitTransactionAsync` and discards it on every non-commit exit, so an operation that fails later emits nothing; direct API archive/restore passes no sink and still notifies immediately (`#2934`); the trade is that a lifecycle event's outbound webhook delivery rows are now written by that post-commit flush, outside any transaction, so a host crash between the commit and the flush loses them with no retry, where before they rolled back with the mutation - tracked in `#3024`.
- `add-label` / `remove-label`: `cardId` plus exactly one of board-scoped `labelId` or `labelName`. Separator-free and underscore aliases remain accepted at apply time for existing callers.
- `workItemType` (`Task` / `Epic` / `Spike`) is read only by `create` and `update`; every other card action ignores it at apply, so `ProposalOperationContractValidator` rejects it there with `Parameter 'workItemType' is not supported by card action '<action>'` rather than letting the preview announce a type transition Apply would not perform (`#2950`).

Invariants:

- Operations remain proposal-first; handlers are reached only after approval and policy revalidation.
- Every `ExecutionAuditRecorder` row - the lifecycle receipts included - is stamped with the authenticated applying user, not the proposal's requester, so it agrees with the actor a handler's own mutation row records; the requester is preserved as `requested by user <id>` in the row's provenance text, and remains the actor only on internal lanes that execute without an authenticated caller (`#2978`).
- Every operation carries its own idempotency key. Label add/remove is also state-idempotent when retried.
- Due dates are parsed from exact `YYYY-MM-DD` or ISO-8601 timestamps with explicit `Z`/numeric offsets and normalized to UTC before apply. Offsetless timestamps and locale-formatted dates are rejected.
- Label names and IDs resolve only against the target card or create operation's board. Name-based operations reject case-insensitive duplicate matches as ambiguous; callers must use a label ID or make the board's label names unique.
- Parameter `boardId`, `cardId`, `columnId`, and typed `TargetId` identities must agree with each other and with the proposal's authorized `BoardId`; cross-board revisions fail before preview or apply.
- Preview and Apply validate the same effective revision payload with `ProposalOperationContractValidator` and use the same field parsers. Invalid/conflicting dates, malformed labels, or scope redirects cannot produce an approval preview.
- `ProposalOperationInputValidator` intentionally validates token/JSON shape, size, and depth only. Do not turn it into a verb allowlist; planner, chat, capture, and MCP callers share this extensible boundary.
- `ProposalDto.Operations` is emitted in ascending `Sequence` on every wire path (`MapToDto` and the revision-materialized `BuildEffectiveProposalDto`), and `Presentation.OperationHeadlines` is built from that same ordered list, so index pairing between the two arrays is part of the contract; ties keep source order (stable sort). Consumers that need the order still sort by `sequence` themselves rather than trusting array order from an older backend (PR `#2609`, `#2563`).

Edit seams:

- Add an apply verb in `OperationHandlerRegistry`, then add its preview detail in `AutomationProposalService`.
- Add chat reachability in `WriteToolSchemas` and the matching `Propose*Executor`; add MCP reachability in `Taskdeck.Api/Mcp/WriteTools.cs`.
- Add risk classification in `AutomationPolicyEngine`; simple reversible card metadata changes are Low pending the opt-in policy work tracked by #1307.

Do not read by default:

- The RFAI-02 `proposal-batch.v1.schema.json` import scaffolding was removed under `#1305` AC3; it was never the apply-time vocabulary authority. The live LLM-output contracts are `backend/src/Taskdeck.Application/Schemas/capture-triage-output.*.json` (transcript triage), and per-field evidence/provenance is carried by the mapped `ProvenanceEvidenceLink` / `ProvenanceField` types — not a batch schema.

Verification:

- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~OperationHandlerRegistry|FullyQualifiedName~OperationParameterParser|FullyQualifiedName~AutomationProposalService|FullyQualifiedName~AutomationPolicyEngine|FullyQualifiedName~WriteTool"`
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~ProposalRevisionApiTests"`
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~McpToolsTests"`
- `npx vitest --run src/tests/composables/useCardModal.spec.ts` from `frontend/taskdeck-web`
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Docs/status sync:

- Shipped behavior belongs in `docs/STATUS.md`; roadmap sequencing belongs in `docs/IMPLEMENTATION_MASTERPLAN.md`. The batch coordinator owns those post-merge updates.
