# Manual Validation Slice C: Automation Proposals, Chat Bootstrap, and Execution Safety

Last Updated: 2026-04-16

Companion references:
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist, Section D)
- `docs/testing/manual-validation-a-workspace-board-ux.md` (Slice A)
- `docs/testing/manual-validation-b-authz-contracts.md` (Slice B)
- `docs/GOLDEN_PRINCIPLES.md` (GP-06: Review-First Automation Safety)
- `docs/STATUS.md` (current implementation snapshot)

## Purpose

Validate the automation proposal lifecycle, chat session behavior, checklist bootstrap, idempotency guarantees, and audit trail correctness. This slice is the primary validation surface for GP-06 (review-first automation safety) -- ensuring that no chat or automation path silently mutates board state.

## Fixture Setup

### Prerequisites

1. Backend API running at `http://localhost:5000`.
2. Frontend dev server running at `http://localhost:5173` (or fallback port).
3. Fresh SQLite database (delete `backend/src/Taskdeck.Api/taskdeck.db` and restart API).

### User Accounts

| Alias     | Username        | Email                   | Password      | Notes                |
|-----------|-----------------|-------------------------|---------------|----------------------|
| **UserA** | `testuser_ca`   | `ca@test.local`         | `TestPass1!`  | Primary test user    |
| **UserB** | `testuser_cb`   | `cb@test.local`         | `TestPass2!`  | Cross-isolation peer |

### Fixture Bootstrap Script

```bash
# 1. Register UserA
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser_ca","email":"ca@test.local","password":"TestPass1!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_ca.txt

# 2. Register UserB
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser_cb","email":"cb@test.local","password":"TestPass2!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_cb.txt

# 3. Store tokens
TOKEN_A=$(cat /tmp/token_ca.txt)
TOKEN_B=$(cat /tmp/token_cb.txt)

# 4. UserA creates a board with columns
BOARD_A=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"SliceC Automation Board"}' | tee /dev/stderr | jq -r '.id')

COL_TODO=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/columns \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"Todo","position":0}' | tee /dev/stderr | jq -r '.id')

COL_DONE=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/columns \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"Done","position":1}' | tee /dev/stderr | jq -r '.id')

# 5. UserA creates a card
CARD_A=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/cards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"title":"Existing Card Alpha","columnId":"'$COL_TODO'"}' | tee /dev/stderr | jq -r '.id')

echo "TOKEN_A=$TOKEN_A"
echo "TOKEN_B=$TOKEN_B"
echo "BOARD_A=$BOARD_A"
echo "COL_TODO=$COL_TODO"
echo "COL_DONE=$COL_DONE"
echo "CARD_A=$CARD_A"
```

### Fixture Invariants

- UserA owns `BOARD_A` with columns `Todo` and `Done` and card `Existing Card Alpha` in `Todo`.
- UserB has no board access grants for `BOARD_A`.
- Mock LLM provider is active (default configuration).

---

## Run Metadata Template

Record before and after each manual run:
- Date/time (UTC)
- Commit SHA
- OS and shell
- Browser and version
- DB baseline (`fresh` or `existing`)
- LLM provider mode (`mock` or `live`)
- Env flags changed (if any)
- Artifacts collected (curl output, screenshots, browser console logs)

---

## Category 1: Chat Session Behavior

### TST09-SC-001: Create chat session without board context

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | Medium |
| **Preconditions** | UserA authenticated, no board created required |

**Steps:**
1. Navigate to `/workspace/automations/chat`.
2. Fill session title with "General Chat Session".
3. Leave board context field empty.
4. Click "Create Session".

**Expected Outcome:**
- Session is created and appears in the session list.
- Session has no board context association.
- Chat input is available for sending messages.

---

### TST09-SC-002: Create board-scoped chat session

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | High |
| **Preconditions** | UserA authenticated, BOARD_A created |

**Steps:**
1. Navigate to `/workspace/automations/chat`.
2. Fill session title with "Board Scoped Session".
3. Enter `BOARD_A` ID in the board context field.
4. Click "Create Session".

**Expected Outcome:**
- Session is created with board context preserved.
- Subsequent messages have access to board data for tool-calling.

---

### TST09-SC-003: Non-actionable chat prompt (greeting)

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | Medium |
| **Preconditions** | Active chat session (TST09-SC-001 or TST09-SC-002) |

**Steps:**
1. In an active chat session, type "Hello, how are you?" in the message input.
2. Leave "Request proposal generation" unchecked.
3. Click "Send Message".

**Expected Outcome:**
- Assistant response appears with conversational text.
- No proposal is generated (no proposal reference in message).
- Board state is unchanged.

---

### TST09-SC-004: Non-actionable chat prompt (question about the system)

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | Medium |
| **Preconditions** | Active board-scoped chat session |

**Steps:**
1. Type "What columns does my board have?" in the message input.
2. Click "Send Message".

**Expected Outcome:**
- With mock provider: assistant responds with generic mock text or tool-call simulation.
- With live provider: intermediate "Looking up..." status messages appear via SignalR, then response lists actual board columns.
- No proposals generated.
- Board state is unchanged.

---

### TST09-SC-005: Actionable prompt -- create card

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | Critical |
| **Preconditions** | Active board-scoped chat session, "Request proposal generation" checkbox available |

**Steps:**
1. Type `create card "New Feature Request"` in the message input.
2. Check "Request proposal generation" checkbox.
3. Click "Send Message".

**Expected Outcome:**
- Assistant response includes a proposal reference (proposal ID visible in chat message).
- Proposal is created with status `PendingReview`.
- Board state is unchanged (no card added yet -- GP-06 compliance).
- Proposal is visible in `/workspace/review`.

---

### TST09-SC-006: Actionable prompt -- move card

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | Critical |
| **Preconditions** | Board-scoped chat session, `CARD_A` exists in `Todo` column |

**Steps:**
1. Type `move card "Existing Card Alpha" to Done` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- Proposal generated for a move operation.
- Card remains in `Todo` column until proposal is approved and executed.
- Proposal summary describes the move operation.

---

### TST09-SC-007: Actionable prompt -- archive card

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | High |
| **Preconditions** | Board-scoped chat session, card exists on board |

**Steps:**
1. Type `archive card "Existing Card Alpha"` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- Proposal generated for an archive operation.
- Card remains visible and unarchived until proposal is approved and executed.

---

### TST09-SC-008: Multi-instruction message

| Field | Value |
|---|---|
| **Category** | Chat Session Behavior |
| **Severity** | High |
| **Preconditions** | Board-scoped chat session |

**Steps:**
1. Type `Add a column called Testing and create a card called Unit Tests` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- Multiple proposals generated from a single message (one for column creation, one for card creation).
- Both proposals appear in `/workspace/review`.
- Board state unchanged until proposals are individually approved and executed.

---

## Category 2: Malformed and Adversarial Prompts

### TST09-SC-009: Empty message submission

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | Medium |
| **Preconditions** | Active chat session |

**Steps:**
1. Clear the message input field (leave it empty or whitespace-only).
2. Attempt to click "Send Message".

**Expected Outcome:**
- Send button is disabled or submission is rejected with a validation message.
- No assistant message or proposal is created.
- No error in browser console beyond expected validation.

---

### TST09-SC-010: Extremely long message (boundary test)

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | Medium |
| **Preconditions** | Active chat session |

**Steps:**
1. Paste a message with 10,000+ characters (e.g., repeated text).
2. Click "Send Message".

**Expected Outcome:**
- Either: message is accepted and processed without crashing (truncation is acceptable).
- Or: client-side validation rejects the message with a clear length error.
- No server 500 error. No unhandled exception in browser console.

---

### TST09-SC-011: XSS payload in chat message

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | Critical |
| **Preconditions** | Active chat session |

**Steps:**
1. Type `<script>alert('xss')</script>` in the message input.
2. Click "Send Message".

**Expected Outcome:**
- Message is sent and displayed as escaped text, not executed as HTML/JS.
- No alert dialog appears.
- The raw text `<script>alert('xss')</script>` is visible in the chat history.
- Assistant response does not execute any scripts.

---

### TST09-SC-012: SQL injection payload in chat message

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | Critical |
| **Preconditions** | Active chat session |

**Steps:**
1. Type `'; DROP TABLE ChatMessages; --` in the message input.
2. Click "Send Message".

**Expected Outcome:**
- Message is stored and displayed as plain text.
- No database error or data loss.
- Subsequent operations work normally (session can continue).

---

### TST09-SC-013: HTML injection in session title

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | High |
| **Preconditions** | UserA authenticated |

**Steps:**
1. Navigate to `/workspace/automations/chat`.
2. Fill session title with `<img src=x onerror=alert(1)>`.
3. Click "Create Session".

**Expected Outcome:**
- Session is created with the literal text as its title.
- No image error event fires. No alert dialog.
- Title renders as escaped text in the session list.

---

### TST09-SC-014: Unicode and emoji in chat messages

| Field | Value |
|---|---|
| **Category** | Adversarial Prompts |
| **Severity** | Low |
| **Preconditions** | Active chat session |

**Steps:**
1. Type a message containing mixed Unicode: `Create card "Bug fix for issue \u2603 \uD83D\uDE00"`.
2. Click "Send Message".

**Expected Outcome:**
- Message is stored and displayed with Unicode characters intact.
- If proposal generation is enabled, the card title in the proposal preserves Unicode.

---

## Category 3: Proposal Lifecycle

### TST09-SC-015: Proposal approve then execute (golden path)

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Critical |
| **Preconditions** | Proposal in `PendingReview` status from TST09-SC-005 or similar |

**Steps:**
1. Navigate to `/workspace/review`.
2. Locate the pending proposal card.
3. Click "Approve for board".
4. Verify status transitions to "Approved, ready to apply".
5. Click "Apply to board" and confirm the dialog.

**Expected Outcome:**
- Status transitions: `PendingReview` -> `Approved` -> `Applied`.
- Board state is updated (e.g., new card appears on the board).
- Proposal card disappears from the review list (or shows as applied).
- Applied change is visible on the board view.

---

### TST09-SC-016: Proposal reject

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Critical |
| **Preconditions** | Proposal in `PendingReview` status |

**Steps:**
1. Navigate to `/workspace/review`.
2. Locate the pending proposal card.
3. Click "Reject" (provide reason if high/critical risk).

**Expected Outcome:**
- Status transitions to `Rejected`.
- Board state is unchanged -- no cards created, moved, or archived.
- Proposal shows rejected status with reason (if provided).

---

### TST09-SC-017: Verify board state unchanged after reject

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Critical |
| **Preconditions** | TST09-SC-016 completed |

**Steps:**
1. Navigate to `/workspace/boards/{BOARD_A}`.
2. Count cards and verify column contents.

**Expected Outcome:**
- Card count and column contents match the state before the proposal was created.
- No ghost cards, no moved cards, no archived cards.

---

### TST09-SC-018: Double-approve prevention

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | High |
| **Preconditions** | Proposal already in `Approved` status |

**Steps:**
1. Using API directly, attempt to POST to `/api/automation/proposals/{id}/approve` for an already-approved proposal.

```bash
curl -s -o /tmp/sc018.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PROPOSAL_ID/approve \
  -H "Authorization: Bearer $TOKEN_A"
cat /tmp/sc018.json | jq .
```

**Expected Outcome:**
- HTTP 409 (Conflict) or 400 (InvalidOperation).
- Error payload: `{ "errorCode": "InvalidOperation", "message": "Cannot approve proposal in status Approved" }`.
- Proposal status remains `Approved` (no state corruption).

---

### TST09-SC-019: Double-execute prevention (idempotency)

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Critical |
| **Preconditions** | Proposal already in `Applied` status |

**Steps:**
1. Attempt to execute an already-applied proposal via API:

```bash
curl -s -o /tmp/sc019.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PROPOSAL_ID/execute \
  -H "Authorization: Bearer $TOKEN_A" \
  -H "Idempotency-Key: $(uuidgen)"
cat /tmp/sc019.json | jq .
```

**Expected Outcome:**
- HTTP 200 (idempotent success) — the execute endpoint is idempotent by design.
- Board state unchanged (no duplicate card creation, no duplicate move).
- Proposal remains in `Applied` status.

---

### TST09-SC-020: Execute without Idempotency-Key header

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | High |
| **Preconditions** | Proposal in `Approved` status |

**Steps:**
1. Attempt to execute proposal without the Idempotency-Key header:

```bash
curl -s -o /tmp/sc020.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PROPOSAL_ID/execute \
  -H "Authorization: Bearer $TOKEN_A"
cat /tmp/sc020.json | jq .
```

**Expected Outcome:**
- HTTP 400 (ValidationError).
- Error payload: `{ "errorCode": "ValidationError", "message": "..." }`.
- Proposal remains in `Approved` status -- not executed.

---

### TST09-SC-021: Expired proposal handling

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | High |
| **Preconditions** | Proposal that has passed its `ExpiresAt` timestamp (default expiry: 1440 minutes/24 hours; for testing, wait or create a proposal with a short expiry window via direct DB manipulation) |

**Steps:**
1. Navigate to `/workspace/review`.
2. Locate the expired proposal.

**Expected Outcome:**
- Expired proposal shows distinct "Expired" status badge -- not "Approved, ready to apply".
- Approve and Apply buttons are NOT shown for expired proposals.
- A "Dismiss" button is available.
- Dismissing the expired proposal removes it from the review list.
- If the page is open when a proposal expires, reactive 60-second clock transitions it to expired state.

---

### TST09-SC-022: Approve expired proposal via API

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | High |
| **Preconditions** | Proposal past its `ExpiresAt` timestamp |

**Steps:**
1. Attempt to approve an expired proposal via API:

```bash
curl -s -o /tmp/sc022.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$EXPIRED_PROPOSAL_ID/approve \
  -H "Authorization: Bearer $TOKEN_A"
cat /tmp/sc022.json | jq .
```

**Expected Outcome:**
- HTTP 409 or 400 with `InvalidOperation` error code.
- Message: "Cannot approve expired proposal".
- Proposal status unchanged.

---

### TST09-SC-023: Proposal diff shows human-readable descriptions

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Medium |
| **Preconditions** | Proposal in `PendingReview` status with operations |

**Steps:**
1. Navigate to `/workspace/review`.
2. Expand the proposal detail to view the diff panel.

**Expected Outcome:**
- Diff shows human-readable operation descriptions (e.g., `Create card "Fix login bug" in column "To Do"`).
- No raw GUIDs visible in the diff (card titles and column names resolved).
- Diff panel has "Operation details" heading and proper word-wrapping.
- Falls back to raw GUID only when name resolution fails.

---

### TST09-SC-024: Proposal dismiss for terminal statuses

| Field | Value |
|---|---|
| **Category** | Proposal Lifecycle |
| **Severity** | Medium |
| **Preconditions** | Proposals in various terminal statuses (Applied, Rejected, Failed, Expired) |

**Steps:**
1. For each terminal status (Applied, Rejected, Failed, Expired):
   - Locate or create a proposal in that status.
   - Verify a "Dismiss" action is available.
   - Click "Dismiss".

**Expected Outcome:**
- Dismissed proposals are removed from the active review list.
- Status transitions to `Dismissed`.
- Cannot dismiss a `PendingReview` proposal (dismiss button absent or API rejects).

---

## Category 4: Checklist Bootstrap via Chat

### TST09-SC-025: Checklist text generates proposal via capture triage

| Field | Value |
|---|---|
| **Category** | Checklist Bootstrap |
| **Severity** | High |
| **Preconditions** | BOARD_A with Todo column, UserA authenticated |

**Steps:**
1. Navigate to `/workspace/inbox`.
2. Create a capture item with checklist text: `- [ ] Set up CI pipeline`.
3. Click on the capture item and start triage.
4. Wait for triage to complete and proposal to be created.

**Expected Outcome:**
- Capture status transitions through: New -> Triaging -> ProposalCreated.
- A proposal is created with a card creation operation.
- The proposal summary references the checklist item text.
- Board state unchanged until proposal is approved and executed.

---

### TST09-SC-026: Checklist bootstrap end-to-end (capture to board)

| Field | Value |
|---|---|
| **Category** | Checklist Bootstrap |
| **Severity** | Critical |
| **Preconditions** | TST09-SC-025 completed, proposal in PendingReview |

**Steps:**
1. Navigate to `/workspace/review`.
2. Approve the checklist-derived proposal.
3. Execute the proposal.
4. Navigate to the board.

**Expected Outcome:**
- Card with title matching the checklist item appears on the board.
- Card has provenance links: "Open Capture" and "Open Proposal" links visible in card detail.
- Clicking "Open Capture" navigates to the inbox item.
- Clicking "Open Proposal" navigates to the review entry.

---

### TST09-SC-027: Multi-item checklist capture

| Field | Value |
|---|---|
| **Category** | Checklist Bootstrap |
| **Severity** | Medium |
| **Preconditions** | BOARD_A with columns, UserA authenticated |

**Steps:**
1. Create a capture item with multiple checklist lines:
   ```
   - [ ] Write unit tests
   - [ ] Write integration tests
   - [ ] Update documentation
   ```
2. Triage the capture item.

**Expected Outcome:**
- Triage processes the multi-line input.
- At least one proposal is generated (behavior may vary: single proposal with multiple operations, or multiple proposals).
- No data loss -- all checklist items are represented in proposals.

---

## Category 5: Cross-User Proposal Isolation

### TST09-SC-028: UserB cannot see UserA's proposals

| Field | Value |
|---|---|
| **Category** | Cross-User Isolation |
| **Severity** | Critical |
| **Preconditions** | UserA has proposals from previous scenarios, UserB authenticated |

**Steps:**
1. Using UserB's token, list all proposals:

```bash
curl -s -o /tmp/sc028.json -w "%{http_code}" \
  http://localhost:5000/api/automation/proposals \
  -H "Authorization: Bearer $TOKEN_B"
cat /tmp/sc028.json | jq .
```

**Expected Outcome:**
- HTTP 200 with empty array `[]` (UserB has no proposals).
- UserA's proposals are not visible to UserB.

---

### TST09-SC-029: UserB cannot approve UserA's proposal

| Field | Value |
|---|---|
| **Category** | Cross-User Isolation |
| **Severity** | Critical |
| **Preconditions** | UserA has a pending proposal, UserB authenticated |

**Steps:**
1. Using UserB's token, attempt to approve UserA's proposal:

```bash
curl -s -o /tmp/sc029.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PROPOSAL_ID/approve \
  -H "Authorization: Bearer $TOKEN_B"
cat /tmp/sc029.json | jq .
```

**Expected Outcome:**
- HTTP 404 (opaque denial -- UserB cannot see the proposal exists).
- UserA's proposal status unchanged.

---

### TST09-SC-030: UserB cannot see UserA's chat sessions

| Field | Value |
|---|---|
| **Category** | Cross-User Isolation |
| **Severity** | Critical |
| **Preconditions** | UserA has chat sessions, UserB authenticated |

**Steps:**
1. Using UserB's token, list all chat sessions:

```bash
curl -s -o /tmp/sc030.json -w "%{http_code}" \
  http://localhost:5000/api/llm/chat/sessions \
  -H "Authorization: Bearer $TOKEN_B"
cat /tmp/sc030.json | jq .
```

**Expected Outcome:**
- HTTP 200 with empty array `[]`.
- UserA's chat sessions are not visible to UserB.

---

### TST09-SC-031: UserB cannot execute UserA's approved proposal

| Field | Value |
|---|---|
| **Category** | Cross-User Isolation |
| **Severity** | Critical |
| **Preconditions** | UserA has an approved proposal |

**Steps:**
1. Using UserB's token, attempt to execute UserA's proposal:

```bash
curl -s -o /tmp/sc031.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PROPOSAL_ID/execute \
  -H "Authorization: Bearer $TOKEN_B" \
  -H "Idempotency-Key: $(uuidgen)"
cat /tmp/sc031.json | jq .
```

**Expected Outcome:**
- HTTP 404 (opaque denial).
- UserA's board state unchanged.

---

## Category 6: Audit Trail Verification

### TST09-SC-032: Proposal execution creates audit entry

| Field | Value |
|---|---|
| **Category** | Audit Trail |
| **Severity** | High |
| **Preconditions** | Proposal has been executed (TST09-SC-015 or similar) |

**Steps:**
1. Navigate to `/workspace/activity` or query the audit API:

```bash
curl -s http://localhost:5000/api/audit/users/me \
  -H "Authorization: Bearer $TOKEN_A" | jq .
```

**Expected Outcome:**
- Audit trail contains an entry for the proposal execution.
- Entry includes: action type, board reference, user reference, timestamp.
- The audit entry is traceable back to the original proposal ID.

---

### TST09-SC-033: Rejected proposal audit entry

| Field | Value |
|---|---|
| **Category** | Audit Trail |
| **Severity** | Medium |
| **Preconditions** | Proposal has been rejected (TST09-SC-016 or similar) |

**Steps:**
1. Query the audit API for recent entries.

**Expected Outcome:**
- Audit trail records the rejection action.
- Rejection reason is preserved in the audit record (if provided).

---

### TST09-SC-034: Board mutation audit after proposal apply

| Field | Value |
|---|---|
| **Category** | Audit Trail |
| **Severity** | High |
| **Preconditions** | Card created via proposal execution |

**Steps:**
1. Query the board audit trail:

```bash
curl -s http://localhost:5000/api/audit/boards/$BOARD_A \
  -H "Authorization: Bearer $TOKEN_A" | jq .
```

**Expected Outcome:**
- Board audit log contains the card creation event.
- Audit entry links back to the proposal that caused the mutation.
- Timestamp is accurate and correlates with the proposal's `AppliedAt` time.

---

## Category 7: High-Risk Intent Prompts

### TST09-SC-035: Bulk operation prompt

| Field | Value |
|---|---|
| **Category** | High-Risk Intent |
| **Severity** | High |
| **Preconditions** | Board-scoped chat session, board with multiple cards |

**Steps:**
1. Type `move all cards to Done` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- If recognized: proposal(s) generated with appropriate risk level (High or Critical).
- Cards remain in their current columns until proposal is approved and executed.
- GP-06 compliance: no silent bulk mutation.
- If not recognized by mock provider: assistant responds conversationally without generating proposals.

---

### TST09-SC-036: Delete/destructive intent prompt

| Field | Value |
|---|---|
| **Category** | High-Risk Intent |
| **Severity** | Critical |
| **Preconditions** | Board-scoped chat session |

**Steps:**
1. Type `delete all cards on this board` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- Either: destructive intent is recognized and a High/Critical risk proposal is generated (requiring explicit approval).
- Or: the system does not support delete-all and responds with a conversational message explaining the limitation.
- In NO case are cards silently deleted.

---

### TST09-SC-037: Prompt with negation context

| Field | Value |
|---|---|
| **Category** | High-Risk Intent |
| **Severity** | Medium |
| **Preconditions** | Board-scoped chat session |

**Steps:**
1. Type `do NOT create a card called "Test Card"` in the message input.
2. Check "Request proposal generation".
3. Click "Send Message".

**Expected Outcome:**
- Negation context filtering prevents proposal generation (classifier detects "NOT" as negative context).
- No proposal created for card creation.
- Assistant responds conversationally acknowledging the negation.

---

## Category 8: LLM Health and Provider States

### TST09-SC-038: LLM health banner states

| Field | Value |
|---|---|
| **Category** | LLM Health |
| **Severity** | Medium |
| **Preconditions** | UserA authenticated |

**Steps:**
1. Navigate to `/workspace/automations/chat`.
2. Observe the LLM health banner.

**Expected Outcome:**
- With mock provider (default): banner shows `[data-llm-health-state="mock"]` with "Live LLM not active" text.
- With a configured live provider: banner shows `[data-llm-health-state="configured"]` initially.
- After probe verification: banner transitions to green "verified" or red "failed".

---

### TST09-SC-039: Degraded provider response handling

| Field | Value |
|---|---|
| **Category** | LLM Health |
| **Severity** | Medium |
| **Preconditions** | Chat session active |

**Steps:**
1. Send a message that would trigger a degraded response (mock provider may simulate this).
2. Observe the chat message rendering.

**Expected Outcome:**
- Degraded responses have `messageType: "degraded"` and include a `degradedReason`.
- Frontend renders degraded messages with a distinct visual treatment (not identical to successful responses).
- Chat flow continues functioning after a degraded response.

---

## Category 9: Review View UX Correctness

### TST09-SC-040: Review card layout and sticky footer

| Field | Value |
|---|---|
| **Category** | Review View UX |
| **Severity** | Medium |
| **Preconditions** | At least one pending proposal |

**Steps:**
1. Navigate to `/workspace/review`.
2. Locate a proposal card with long content (expand detail sections).
3. Scroll within the proposal card.

**Expected Outcome:**
- Action footer (Approve/Reject buttons) remains sticky at the bottom of the card.
- Card content is constrained to `max-height: 70vh` (80vh on mobile) with internal scrolling.
- Detail sections are collapsible.
- Risk level is color-coded.

---

### TST09-SC-041: Review proposal links preserve board context

| Field | Value |
|---|---|
| **Category** | Review View UX |
| **Severity** | Medium |
| **Preconditions** | Board-scoped proposal exists |

**Steps:**
1. Navigate to a proposal from chat: click proposal link in chat message.
2. Navigate to the same proposal from inbox: use "Open in Review" button on a triaged capture.
3. Navigate to the same proposal from notification (if notification was generated).
4. Navigate to the same proposal from card provenance (if card was created from proposal).

**Expected Outcome:**
- All navigation paths land on `/workspace/review?boardId={boardId}#proposal-{proposalId}`.
- Board context is preserved in the URL.
- Proposal card is scrolled into view and highlighted.

---

## Category 10: Execution Safety Edge Cases

### TST09-SC-042: Reject then re-approve prevention

| Field | Value |
|---|---|
| **Category** | Execution Safety |
| **Severity** | High |
| **Preconditions** | Proposal in `Rejected` status |

**Steps:**
1. Attempt to approve a rejected proposal via API:

```bash
curl -s -o /tmp/sc042.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$REJECTED_PROPOSAL_ID/approve \
  -H "Authorization: Bearer $TOKEN_A"
cat /tmp/sc042.json | jq .
```

**Expected Outcome:**
- HTTP 409 or 400 with `InvalidOperation`.
- Message: "Cannot approve proposal in status Rejected".
- Proposal remains in `Rejected` status.

---

### TST09-SC-043: Execute without prior approve

| Field | Value |
|---|---|
| **Category** | Execution Safety |
| **Severity** | Critical |
| **Preconditions** | Proposal in `PendingReview` status |

**Steps:**
1. Attempt to execute a proposal that has not been approved:

```bash
curl -s -o /tmp/sc043.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/$PENDING_PROPOSAL_ID/execute \
  -H "Authorization: Bearer $TOKEN_A" \
  -H "Idempotency-Key: $(uuidgen)"
cat /tmp/sc043.json | jq .
```

**Expected Outcome:**
- HTTP 409 or 400 with `InvalidOperation`.
- Message: "Only approved proposals can be marked as applied" (or similar).
- Board state unchanged.
- Proposal remains in `PendingReview` status.

---

### TST09-SC-044: Concurrent approve race condition

| Field | Value |
|---|---|
| **Category** | Execution Safety |
| **Severity** | Medium |
| **Preconditions** | Proposal in `PendingReview` status |

**Steps:**
1. Open two browser tabs to `/workspace/review`.
2. In both tabs, locate the same pending proposal.
3. Click "Approve" in both tabs as quickly as possible.

**Expected Outcome:**
- One approve succeeds, one fails gracefully.
- Proposal transitions to `Approved` exactly once.
- No duplicate board mutations.
- The failing tab shows an error message or stale-state notice.

---

### TST09-SC-045: Proposal on deleted/archived board

| Field | Value |
|---|---|
| **Category** | Execution Safety |
| **Severity** | Medium |
| **Preconditions** | Proposal pending for a board, board is then archived |

**Steps:**
1. Create a board-scoped proposal.
2. Archive the board.
3. Attempt to approve and execute the proposal.

**Expected Outcome:**
- Execution fails gracefully with a meaningful error (board not found or archived).
- No partial board mutation.
- Proposal can be dismissed.

---

## Findings Log

Record mismatches between expected and actual behavior during execution.

| ID | Expected | Actual | Severity | Linked Issue | Notes |
|---|---|---|---|---|---|
| | | | | | |

Severity levels:
- **Critical**: Automation mutates board state without explicit user approval (GP-06 violation)
- **High**: State machine bypass (double-approve, execute-without-approve) or cross-user data leak
- **Medium**: Missing error payload, incorrect status code, UI rendering issue
- **Low**: Inconsistent message text, cosmetic issue, non-blocking UX friction

---

## Regression Rerun Instructions

1. Check out the target commit on `main`.
2. Follow Fixture Setup to create fresh two-user state with board.
3. Execute all scenarios (TST09-SC-001 through TST09-SC-045) recording pass/fail.
4. Compare against previous run's Findings Log.
5. File new issues for any regressions; link them here.

Previous runs:
| Date | Commit | Runner | Findings Count | Notes |
|---|---|---|---|---|
| (template) | (sha) | (name) | 0 | |
