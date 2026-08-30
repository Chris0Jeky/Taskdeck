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
- `archive`: `cardId`. Cards do not have a separate archived state; applying this operation marks the card blocked with the generated reason `Archived by an approved proposal.` The preview shows that exact blocked-state transition before approval.
- `add-label` / `remove-label`: `cardId` plus exactly one of board-scoped `labelId` or `labelName`. Separator-free and underscore aliases remain accepted at apply time for existing callers.

Invariants:

- Operations remain proposal-first; handlers are reached only after approval and policy revalidation.
- Every operation carries its own idempotency key. Label add/remove is also state-idempotent when retried.
- Due dates are parsed from exact `YYYY-MM-DD` or ISO-8601 timestamps with explicit `Z`/numeric offsets and normalized to UTC before apply. Offsetless timestamps and locale-formatted dates are rejected.
- Label names and IDs resolve only against the target card or create operation's board. Name-based operations reject case-insensitive duplicate matches as ambiguous; callers must use a label ID or make the board's label names unique.
- Parameter `boardId`, `cardId`, `columnId`, and typed `TargetId` identities must agree with each other and with the proposal's authorized `BoardId`; cross-board revisions fail before preview or apply.
- Preview and Apply validate the same effective revision payload with `ProposalOperationContractValidator` and use the same field parsers. Invalid/conflicting dates, malformed labels, or scope redirects cannot produce an approval preview.
- `ProposalOperationInputValidator` intentionally validates token/JSON shape, size, and depth only. Do not turn it into a verb allowlist; planner, chat, capture, and MCP callers share this extensible boundary.

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
