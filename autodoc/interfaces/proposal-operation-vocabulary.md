# Proposal Operation Vocabulary Agent Interface

Entry points:

- `backend/src/Taskdeck.Application/Services/Pipeline/OperationHandlerRegistry.cs`: apply-time vocabulary and parameter-to-service mapping.
- `backend/src/Taskdeck.Application/Services/AutomationProposalService.cs`: original and revision-aware human-readable preview.
- `backend/src/Taskdeck.Application/Services/Tools/ProposeCreateCardExecutor.cs` and `ProposeUpdateCardExecutor.cs`: chat/MCP proposal construction.
- `backend/src/Taskdeck.Application/Services/ProposalOperationInputValidator.cs`: create-boundary shape and JSON safety checks.

Card operations:

- `create`: `boardId`, `columnId`, `title`; optional `description`, `dueDate`, and label-name array `labels`.
- `update`: `cardId`; at least one of `title`, `description`, `dueDate`, `clearDueDate`, or replacement label-name array `labels`. An explicit null `dueDate` also clears it.
- `move`: `cardId`, `columnId`.
- `archive`: `cardId`.
- `add-label` / `remove-label`: `cardId` plus exactly one of board-scoped `labelId` or `labelName`. Separator-free and underscore aliases remain accepted at apply time for existing callers.

Invariants:

- Operations remain proposal-first; handlers are reached only after approval and policy revalidation.
- Every operation carries its own idempotency key. Label add/remove is also state-idempotent when retried.
- Due dates are parsed from `YYYY-MM-DD` or ISO-8601 timestamps and normalized to UTC before apply.
- Label names and IDs resolve only against the target card or create operation's board.
- Preview and Apply read the same effective revision payload and use the same date parser. A due-date or label change shown after revision is the change the executor applies.
- `ProposalOperationInputValidator` intentionally validates token/JSON shape, size, and depth only. Do not turn it into a verb allowlist; planner, chat, capture, and MCP callers share this extensible boundary.

Edit seams:

- Add an apply verb in `OperationHandlerRegistry`, then add its preview detail in `AutomationProposalService`.
- Add chat/MCP reachability in `WriteToolSchemas` and the matching `Propose*Executor`.
- Add risk classification in `AutomationPolicyEngine`; simple reversible card metadata changes are Low pending the opt-in policy work tracked by #1307.

Do not read by default:

- `backend/src/Taskdeck.Application/Schemas/proposal-batch.v1.schema.json` is aspirational import scaffolding, not the apply-time vocabulary authority.

Verification:

- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~OperationHandlerRegistry|FullyQualifiedName~OperationParameterParser|FullyQualifiedName~AutomationProposalService|FullyQualifiedName~AutomationPolicyEngine|FullyQualifiedName~WriteTool"`
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~ProposalRevisionApiTests"`
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Docs/status sync:

- Shipped behavior belongs in `docs/STATUS.md`; roadmap sequencing belongs in `docs/IMPLEMENTATION_MASTERPLAN.md`. The batch coordinator owns those post-merge updates.
