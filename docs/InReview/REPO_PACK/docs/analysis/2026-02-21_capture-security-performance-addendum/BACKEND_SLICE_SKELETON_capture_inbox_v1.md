# Capture Inbox MVP — Backend Slice Skeleton (v1)

Last Updated: 2026-02-21  
Scope: Backend-only implementation skeleton (API + Application + Worker + Tests).  
Goal: Add a **Capture Inbox** that turns raw text into **reviewable automation proposals** (never auto-execute).

This document is intentionally explicit: it is designed to be copy-pasted into GitHub issues and followed by agents.

---

## 0) Implementation choice (concrete)

**MVP choice:** Reuse the existing **LLM Queue** persistence (`Taskdeck.Domain.Entities.LlmRequest` / table `LlmRequests`) as the storage for capture items.

Why this is the correct MVP fit in Taskdeck *as it exists today*:

- `LlmRequest` already models: `{UserId, BoardId?, RequestType, Payload, Status, ErrorMessage, RetryCount, CreatedAt, ProcessedAt}`.
- There is already a worker (`Taskdeck.Api.Workers.LlmQueueToProposalWorker`) and queue service (`Taskdeck.Application.Services.LlmQueueService`).
- You get auditability and lifecycle states “for free”.
- You avoid new tables until the product semantics are proven.

**Result:** the Capture Inbox becomes a “semantic wrapper” over the queue.

---

## 1) RequestType conventions (non-negotiable)

Create a tiny convention and keep it stable:

- `inbox.capture.v1` → capture items that should be triaged into proposals.

Optional future request types (reserve now, implement later):

- `inbox.capture.note.v1` → store as a single note card if no tasks found
- `inbox.capture.meeting.v1` → meeting transcript defaults (stronger summarization)
- `automation.instruction.v1` → (recommended) explicit instruction parsing queue type

**Rule in worker:**
- If `RequestType` starts with `inbox.capture.` → route to Capture triage pipeline.
- Else → route to existing instruction parser (`AutomationPlannerService.ParseInstructionAsync`).

---

## 2) Payload schema (store structured JSON in LlmRequest.Payload)

Even though `LlmRequest.Payload` is a string, store a JSON document in it for capture items:

### 2.1 CapturePayloadV1 JSON

```json
{
  "version": 1,
  "source": "keyboard",
  "text": "raw captured text...",
  "clientCreatedAt": "2026-02-21T12:34:56Z",
  "titleHint": "Optional short title",
  "externalRef": "Optional external id/url"
}
```

- `version`: integer (required, fixed 1)
- `source`: enum string (optional; default `"keyboard"`)
- `text`: string (required)
- `clientCreatedAt`: ISO timestamp string (optional)
- `titleHint`: string (optional)
- `externalRef`: string (optional)

---

## 3) Backend files to create / modify (exact paths)

### 3.1 Application DTOs

Create:

- `backend/src/Taskdeck.Application/DTOs/CaptureDtos.cs`

Add:

- `public record CaptureItemDto(...)`
- `public record CaptureItemSummaryDto(...)`
- `public record CreateCaptureItemDto(Guid? BoardId, string Text, string? Source = null, string? TitleHint = null, string? ExternalRef = null)`
- `public record CaptureFilterDto(string? Status = null, Guid? BoardId = null, int Limit = 50)`
- `internal record CapturePayloadV1(...)` (used for serialization)

**Design constraints**
- `TextExcerpt` should be derived server-side (first ~200 chars of `text`, normalized whitespace).
- For lists, return summary DTO (don’t ship full text by default).

---

### 3.2 Application Services

Create interface:

- `backend/src/Taskdeck.Application/Services/ICaptureService.cs`

```csharp
public interface ICaptureService
{
    Task<Result<CaptureItemDto>> CreateAsync(Guid userId, CreateCaptureItemDto dto, CancellationToken ct = default);
    Task<Result<CaptureItemDto>> GetByIdAsync(Guid userId, Guid itemId, CancellationToken ct = default);
    Task<Result<List<CaptureItemSummaryDto>>> ListAsync(Guid userId, CaptureFilterDto filter, CancellationToken ct = default);
    Task<Result> CancelAsync(Guid userId, Guid itemId, CancellationToken ct = default);
    Task<Result<Guid?>> GetLinkedProposalIdAsync(Guid userId, Guid itemId, CancellationToken ct = default);
}
```

Create implementation:

- `backend/src/Taskdeck.Application/Services/CaptureService.cs`

**Implementation notes**
- Persist capture items as `LlmRequest` with:
  - `RequestType = "inbox.capture.v1"`
  - `Payload = JsonSerializer.Serialize(CapturePayloadV1)`
  - `Status = RequestStatus.Pending`
  - `BoardId = dto.BoardId`
- Validate:
  - `userId != Guid.Empty`
  - `dto.Text` not empty
  - if `dto.BoardId != null`: verify board exists and user has access (reuse existing checks from `LlmQueueService.AddToQueueAsync`)

**Repository usage**
- Prefer: `IUnitOfWork.LlmQueue` for add/get operations.
- If you need new repo methods (filtering by user + requestType + status), add them to:
  - `backend/src/Taskdeck.Application/Interfaces/ILlmQueueRepository.cs`
  - Implement in `backend/src/Taskdeck.Infrastructure/Repositories/LlmQueueRepository.cs`

---

### 3.3 Worker routing (critical)

Modify:

- `backend/src/Taskdeck.Api/Workers/LlmQueueToProposalWorker.cs`

Add a branch near:

```csharp
var proposalResult = await _planner.ParseInstructionAsync(item.Payload, item.UserId, item.BoardId, cancellationToken);
```

Replace with:

```csharp
Result<ProposalDto> proposalResult;

if (item.RequestType.StartsWith("inbox.capture.", StringComparison.OrdinalIgnoreCase))
{
    proposalResult = await _captureTriageService.TriageCaptureRequestAsync(item, cancellationToken);
}
else
{
    proposalResult = await _planner.ParseInstructionAsync(
        item.Payload,
        item.UserId,
        item.BoardId,
        cancellationToken,
        sourceType: ProposalSourceType.Queue,
        sourceReferenceId: item.Id.ToString(),
        correlationId: item.Id.ToString());
}
```

This requires:

1) Injecting a new service into the worker:
- `CaptureTriageService` (new) OR add method to `CaptureService` (not recommended; keep triage separate).

2) Extending `IAutomationPlannerService.ParseInstructionAsync` signature to accept optional:
- `ProposalSourceType sourceType`
- `string? sourceReferenceId`
- `string? correlationId`

…and updating `AutomationPlannerService` accordingly.

**Why do this now**
- Today, queued proposals are created with `ProposalSourceType.Manual`, which breaks provenance. Fixing it unlocks “trust UX” everywhere.

---

### 3.4 Capture triage service (deterministic MVP with LLM hook)

Create:

- `backend/src/Taskdeck.Application/Services/ICaptureTriageService.cs`
- `backend/src/Taskdeck.Application/Services/CaptureTriageService.cs`

Signature:

```csharp
public interface ICaptureTriageService
{
    Task<Result<ProposalDto>> TriageCaptureRequestAsync(LlmRequest item, CancellationToken ct = default);
}
```

Implementation steps:

1) Deserialize `CapturePayloadV1` from `item.Payload`
   - If payload is not JSON, treat entire payload as `text` for backward compatibility.
2) Determine target board + column:
   - If `item.BoardId` is null → return `Result.Failure(ErrorCodes.ValidationError, "Capture item requires a boardId for MVP")`
   - Load board columns: `await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct)` (or equivalent)
   - Choose **first column by Position** as default “inbox column”
3) Extract card plans (deterministic):
   - Parse Markdown checkboxes: `- [ ] ...`
   - Parse bullet lines: `- ...`, `* ...`
   - Parse numbered lines: `1. ...`
   - If no lines extracted:
     - Create a single card plan:
       - title: `titleHint ?? "Captured note"`
       - description: truncate raw text to <= 2000 chars with suffix `\n\n[Truncated]` if needed
4) Convert card plans → `CreateProposalDto` with operations:
   - `ActionType = "create"`
   - `TargetType = "card"`
   - parameters JSON: `{ "boardId": "...", "columnId": "...", "title": "...", "description": "..." }`
   - idempotency keys should be deterministic:
     - Example: `capture:{item.Id}:op:{sequence}`
5) Create the proposal via `IAutomationProposalService.CreateProposalAsync`
   - Must set: `SourceType = ProposalSourceType.Queue`
   - `SourceReferenceId = item.Id.ToString()`
   - `CorrelationId = item.Id.ToString()`

**LLM hook (future)**
- Add optional: if config `Llm.EnableLiveProviders` and `Capture.EnableLlmTriage` → run LLM-based extraction first, then fall back to deterministic.

---

### 3.5 API Controller

Create:

- `backend/src/Taskdeck.Api/Controllers/CaptureController.cs`

Routing:

- `[Route("api/capture")]`
- `[Authorize]`

Endpoints:

- `POST /api/capture/items` → calls `ICaptureService.CreateAsync`
  - returns `201 Created` + body `CaptureItemDto`
- `GET /api/capture/items?status=&boardId=&limit=` → calls `ICaptureService.ListAsync`
- `GET /api/capture/items/{id}` → calls `GetByIdAsync`
- `POST /api/capture/items/{id}/cancel` → calls `CancelAsync` (204)
- `GET /api/capture/items/{id}/proposal` → calls `GetLinkedProposalIdAsync` (200)

Error handling:
- Use existing patterns:
  - `AuthenticatedControllerBase` for `CurrentUserId`
  - `result.ToErrorActionResult()` for errors (keeps `{ errorCode, message }` contract)

---

## 4) Linked proposal mapping (pick one MVP option)

You have two viable options. Choose one and implement it consistently.

### Option A (recommended): store ProposalId on LlmRequest

Pros: simplest UI, no extra lookups.  
Cons: DB migration + DTO updates.

Steps:

1) Modify domain entity:
   - `backend/src/Taskdeck.Domain/Entities/LlmRequest.cs`
   - Add: `public Guid? ProposalId { get; private set; }`
   - Add method: `public void LinkProposal(Guid proposalId)`

2) Update EF config:
   - `backend/src/Taskdeck.Infrastructure/Persistence/Configurations/LlmRequestConfiguration.cs`
   - Map `ProposalId` (nullable) + add index `(UserId, ProposalId)` optionally

3) Create migration under `backend/src/Taskdeck.Infrastructure/Migrations/...`

4) Update DTO:
   - `backend/src/Taskdeck.Application/DTOs/LlmQueueDtos.cs` → extend `LlmRequestDto` with `Guid? ProposalId`
   - Update mapping in `LlmQueueService` and API.

5) In worker, after creating proposal:
   - `item.LinkProposal(proposal.Id);`
   - then mark completed.

### Option B: derive proposal by SourceReferenceId

Pros: no DB changes.  
Cons: extra query + ambiguity if correlation broken.

Steps:
- Ensure proposals are created with:
  - `SourceType = Queue`
  - `SourceReferenceId = item.Id`
- Implement `ICaptureService.GetLinkedProposalIdAsync` by querying proposals repo:
  - `GetLatestBySourceReferenceAsync(ProposalSourceType.Queue, item.Id.ToString())`

---

## 5) Tests (exact files)

### 5.1 API integration tests

Create:

- `backend/tests/Taskdeck.Api.Tests/CaptureApiTests.cs`

Test cases (minimum):

- Unauthorized matrix for all endpoints (like `LlmQueueApiTests`)
- Create capture returns 201, status Pending, requestType inbox.capture.v1 (indirectly)
- List returns only current user’s items
- Cancel returns 204, forbidden cross-user, notfound for missing
- (If implementing Option A) proposalId appears after processing:
  - Submit capture
  - Trigger `POST /api/llm-queue/process-next` OR rely on auto worker? (Prefer deterministic by calling process-next endpoint)
  - Verify `GET /api/capture/items/{id}/proposal` returns non-null proposalId

### 5.2 Application unit tests

Create:

- `backend/tests/Taskdeck.Application.Tests/CaptureTriageServiceTests.cs`

Test cases:

- Bullet list produces N create-card operations
- No tasks produces 1 create-card operation with truncated description
- Payload non-JSON fallback path works
- Description truncation respects 2000 char limit

---

## 6) Minimal config toggles (optional but recommended)

Add to `backend/src/Taskdeck.Api/appsettings.json`:

```json
"Capture": {
  "EnableLlmTriage": false,
  "MaxCaptureTextLength": 20000
}
```

Create settings model (optional):

- `backend/src/Taskdeck.Application/Services/CaptureSettings.cs`

---

## 7) Done definition (acceptance)

- New endpoints exist and are protected by JWT.
- Capture items persist and appear in list/get.
- Worker routes inbox capture requests to triage path.
- Triage produces proposals (PendingReview) that can be approved/executed.
- Error contract remains `{ errorCode, message }`.
- Tests added and passing:
  - `dotnet test backend/tests/Taskdeck.Api.Tests`
  - `dotnet test backend/tests/Taskdeck.Application.Tests`

---

## 8) Follow-ups (deliberately out of MVP)

- Keyboard-first “global capture” UI and hotkeys (frontend).
- Real LLM triage with JSON output + schema validation.
- Multi-board routing policy (“guess board”).
- PII redaction + consent UX for sending capture to external LLM providers.
- Voice capture (mic) + `Permissions-Policy` adjustments in nginx.
