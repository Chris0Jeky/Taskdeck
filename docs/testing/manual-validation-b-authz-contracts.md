# Manual Validation Slice B: Authz Policy, Cross-User Isolation, and API Error Contracts

Last Updated: 2026-04-02

Companion references:
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist)
- `docs/STATUS.md` (current implementation snapshot)
- `docs/TESTING_GUIDE.md` (test operations reference)

## Purpose

Validate authorization policy enforcement, cross-user data isolation, and API error payload contracts across all protected controller families. This slice complements the existing manual checklist (Slice A / Sections A-O) by focusing exclusively on the security and contract boundary.

## Fixture Setup: Two-User Cross-Isolation Environment

### Prerequisites

1. Backend API running at `http://localhost:5000`.
2. Fresh or known-baseline SQLite database (recommend deleting `taskdeck.db` and restarting API).

### User Accounts

| Alias     | Username       | Email                  | Password      | Notes                |
|-----------|----------------|------------------------|---------------|----------------------|
| **UserA** | `testuser_a`   | `a@test.local`         | `TestPass1!`  | Primary test user    |
| **UserB** | `testuser_b`   | `b@test.local`         | `TestPass2!`  | Cross-isolation peer |

### Fixture Bootstrap Script

Run these curl commands in order. Save the bearer tokens for subsequent steps.

```bash
# 1. Register UserA
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser_a","email":"a@test.local","password":"TestPass1!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_a.txt

# 2. Register UserB
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser_b","email":"b@test.local","password":"TestPass2!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_b.txt

# 3. Store tokens
TOKEN_A=$(cat /tmp/token_a.txt)
TOKEN_B=$(cat /tmp/token_b.txt)

# 4. UserA creates a board (shared fixture board)
BOARD_A=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"UserA Private Board"}' | tee /dev/stderr | jq -r '.id')

# 5. UserA creates a column on that board
COL_A=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/columns \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"Todo","position":0}' | tee /dev/stderr | jq -r '.id')

# 6. UserA creates a card on that board
CARD_A=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/cards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"title":"UserA Card","columnId":"'$COL_A'"}' | tee /dev/stderr | jq -r '.id')

# 7. UserA creates a label on that board
LABEL_A=$(curl -s -X POST http://localhost:5000/api/boards/$BOARD_A/labels \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"Priority","colorHex":"#ff0000"}' | tee /dev/stderr | jq -r '.id')

# 8. UserA creates an agent profile (for cross-user agent isolation tests)
AGENT_A=$(curl -s -X POST http://localhost:5000/api/agents \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"name":"UserA Test Agent","templateKey":"default","scopeType":"User","description":"Fixture agent"}' | tee /dev/stderr | jq -r '.id')

# 9. UserA creates a knowledge item (for cross-user knowledge isolation tests)
KNOWLEDGE_A=$(curl -s -X POST http://localhost:5000/api/knowledge \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{"title":"UserA Knowledge","content":"Fixture content","sourceType":"Manual"}' | tee /dev/stderr | jq -r '.id')

echo "TOKEN_A=$TOKEN_A"
echo "TOKEN_B=$TOKEN_B"
echo "BOARD_A=$BOARD_A"
echo "COL_A=$COL_A"
echo "CARD_A=$CARD_A"
echo "LABEL_A=$LABEL_A"
echo "AGENT_A=$AGENT_A"
echo "KNOWLEDGE_A=$KNOWLEDGE_A"
```

### Fixture Invariants

- UserA owns `BOARD_A` and all entities within it.
- UserA owns `AGENT_A` and `KNOWLEDGE_A`.
- UserB has no board access grants for `BOARD_A`.
- Neither user is an admin unless explicitly promoted.

---

## Run Metadata Template

Record this before and after each manual run:
- Date/time (UTC)
- Commit SHA
- OS and shell
- DB baseline (`fresh` or `existing`)
- Env flags changed (if any)
- Artifacts collected (curl output files, screenshots)

---

## Section 1: Unauthenticated Access Denial (401)

All `[Authorize]` controller families must return `401` with the standard error payload when no bearer token is provided.

### Expected payload shape

```json
{
  "errorCode": "<non-empty string>",
  "message": "<non-empty string>"
}
```

Note: ASP.NET Core's JWT middleware may return a bare `401` without a JSON body on some paths. Document whether the body is present or absent for each check; absent JSON body on `401` is a known framework behavior, not necessarily a bug, but should be tracked.

| ID    | Method | Route                                          | Expected Status | Payload Check         |
|-------|--------|-------------------------------------------------|-----------------|-----------------------|
| B-01  | GET    | `/api/boards`                                   | 401             | JSON `errorCode`+`message` or bare 401 |
| B-02  | GET    | `/api/boards/{random-guid}`                     | 401             | Same                  |
| B-03  | POST   | `/api/boards`                                   | 401             | Same                  |
| B-04  | GET    | `/api/boards/{random-guid}/columns`             | 401             | Same                  |
| B-05  | GET    | `/api/boards/{random-guid}/cards`               | 401             | Same                  |
| B-06  | GET    | `/api/boards/{random-guid}/labels`              | 401             | Same                  |
| B-07  | GET    | `/api/boards/{random-guid}/cards/{random-guid}/comments` | 401   | Same                  |
| B-08  | GET    | `/api/boards/{random-guid}/access`              | 401             | Same                  |
| B-09  | GET    | `/api/boards/{random-guid}/webhooks`            | 401             | Same                  |
| B-10  | GET    | `/api/boards/{random-guid}/starter-packs/catalog` | 401           | Same                  |
| B-11  | POST   | `/api/capture/items`                            | 401             | Same                  |
| B-12  | GET    | `/api/capture/items`                            | 401             | Same                  |
| B-13  | GET    | `/api/llm/chat/sessions`                        | 401             | Same                  |
| B-14  | POST   | `/api/llm/chat/sessions`                        | 401             | Same                  |
| B-15  | GET    | `/api/automation/proposals`                     | 401             | Same                  |
| B-16  | GET    | `/api/archive/items`                            | 401             | Same                  |
| B-17  | GET    | `/api/ops/cli/templates`                        | 401             | Same                  |
| B-18  | GET    | `/api/logs`                                     | 401             | Same                  |
| B-19  | GET    | `/api/audit/users/me`                           | 401             | Same                  |
| B-20  | GET    | `/api/users`                                    | 401             | Same                  |
| B-21  | GET    | `/api/notifications`                            | 401             | Same                  |
| B-22  | GET    | `/api/workspace/home`                           | 401             | Same                  |
| B-23  | GET    | `/api/workspace/today`                          | 401             | Same                  |
| B-24  | GET    | `/api/llm-queue/user`                           | 401             | Same                  |
| B-25  | GET    | `/api/llm/quota/usage`                          | 401             | Same                  |
| B-26  | GET    | `/api/abuse/actors/{random-guid}/status`        | 401             | Same                  |
| B-27  | GET    | `/api/agents`                                   | 401             | Same                  |
| B-28  | GET    | `/api/knowledge`                                | 401             | Same                  |
| B-29  | GET    | `/api/export/boards/{random-guid}`              | 401             | Same                  |
| B-30  | GET    | `/api/export/database`                          | 401             | Same                  |
| B-31  | POST   | `/api/boards/{random-guid}/imports/external`    | 401             | Same                  |
| B-32  | GET    | `/api/workspace/preferences`                    | 401             | Same                  |
| B-33  | GET    | `/api/account/export`                           | 401             | Same                  |
| B-34  | POST   | `/api/account/delete`                           | 401             | Same                  |
| B-35  | GET    | `/api/metrics/boards/{random-guid}`             | 401             | Same                  |

### Curl template for unauthenticated checks

```bash
# Replace METHOD and ROUTE for each row
curl -s -o /tmp/b_XX.json -w "%{http_code}" -X GET http://localhost:5000/api/boards
# Then inspect:
cat /tmp/b_XX.json | jq .
```

---

## Section 2: Cross-User Board Isolation (403 vs 404)

UserB attempts to access UserA's board and its child entities. The system should deny access. The expected behavior is:

- **404 (opaque denial)**: The system does not reveal whether the resource exists. This is the preferred security posture for board-level lookups to avoid enumeration.
- **403 (explicit denial)**: The system confirms the resource exists but the user lacks permission. Used when the resource existence is not sensitive (e.g., after board-access sharing is set up).

Document the actual behavior for each check. If the system returns `404` where `403` was expected (or vice versa), note it as a finding but not necessarily a bug -- opaque denial (404) is often more secure.

| ID    | Method | Route (using UserA's BOARD_A)                   | Token   | Expected | Notes                          |
|-------|--------|--------------------------------------------------|---------|----------|--------------------------------|
| B-40  | GET    | `/api/boards`                                    | UserB   | 200      | Returns only UserB's boards (empty or UserB-owned) |
| B-41  | GET    | `/api/boards/{BOARD_A}`                          | UserB   | 404      | Opaque denial: board not visible to UserB |
| B-42  | PUT    | `/api/boards/{BOARD_A}`                          | UserB   | 404      | Cannot update foreign board    |
| B-43  | DELETE | `/api/boards/{BOARD_A}`                          | UserB   | 404      | Cannot delete foreign board    |
| B-44  | GET    | `/api/boards/{BOARD_A}/columns`                  | UserB   | 404      | Board-scoped child isolation   |
| B-45  | POST   | `/api/boards/{BOARD_A}/columns`                  | UserB   | 404      | Cannot create column on foreign board |
| B-46  | GET    | `/api/boards/{BOARD_A}/cards`                    | UserB   | 404      | Board-scoped child isolation   |
| B-47  | POST   | `/api/boards/{BOARD_A}/cards`                    | UserB   | 404      | Cannot create card on foreign board |
| B-48  | GET    | `/api/boards/{BOARD_A}/labels`                   | UserB   | 404      | Board-scoped child isolation   |
| B-49  | POST   | `/api/boards/{BOARD_A}/labels`                   | UserB   | 404      | Cannot create label on foreign board |
| B-50  | GET    | `/api/boards/{BOARD_A}/cards/{CARD_A}/comments`  | UserB   | 404      | Nested child isolation         |
| B-51  | GET    | `/api/boards/{BOARD_A}/access`                   | UserB   | 403 or 404 | Access list for foreign board |
| B-52  | POST   | `/api/boards/{BOARD_A}/access`                   | UserB   | 403 or 404 | Cannot grant access on foreign board |
| B-53  | GET    | `/api/boards/{BOARD_A}/webhooks`                 | UserB   | 403 or 404 | Webhook list on foreign board |
| B-54  | GET    | `/api/boards/{BOARD_A}/starter-packs/catalog`    | UserB   | 404      | Starter pack catalog on foreign board |
| B-55  | POST   | `/api/boards/{BOARD_A}/starter-packs/apply`      | UserB   | 404      | Cannot apply pack to foreign board |
| B-56  | GET    | `/api/export/boards/{BOARD_A}`                   | UserB   | 404      | Cannot export foreign board    |
| B-57  | GET    | `/api/export/boards/{BOARD_A}/json`              | UserB   | 404      | Cannot JSON-export foreign board |
| B-58  | GET    | `/api/audit/boards/{BOARD_A}`                    | UserB   | 404      | Cannot view audit for foreign board |
| B-59  | POST   | `/api/boards/{BOARD_A}/imports/external`         | UserB   | 403 or 404 | Cannot import into foreign board |
| B-60  | GET    | `/api/boards/{BOARD_A}/cards/{CARD_A}/provenance`| UserB   | 404      | Cannot view card provenance on foreign board |
| B-61  | GET    | `/api/metrics/boards/{BOARD_A}`                  | UserB   | 404      | Cannot view metrics for foreign board |

### Curl template for cross-user checks

```bash
# UserB attempts to read UserA's board
curl -s -o /tmp/b_41.json -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN_B" \
  http://localhost:5000/api/boards/$BOARD_A
cat /tmp/b_41.json | jq .
```

---

## Section 3: Cross-User Non-Board-Scoped Isolation

These controllers are user-scoped but not board-scoped. Verify that UserB cannot access UserA's private data.

| ID    | Method | Route                                            | Token   | Expected | Notes                          |
|-------|--------|--------------------------------------------------|---------|----------|--------------------------------|
| B-70  | GET    | `/api/capture/items`                             | UserB   | 200      | Returns only UserB's captures (empty list) |
| B-71  | GET    | `/api/llm/chat/sessions`                         | UserB   | 200      | Returns only UserB's sessions (empty list) |
| B-72  | GET    | `/api/automation/proposals`                      | UserB   | 200      | Returns only UserB's proposals (empty list) |
| B-73  | GET    | `/api/archive/items`                             | UserB   | 200      | Returns only UserB's archived items |
| B-74  | GET    | `/api/notifications`                             | UserB   | 200      | Returns only UserB's notifications |
| B-75  | GET    | `/api/llm-queue/user`                            | UserB   | 200      | Returns only UserB's queue items |
| B-76  | GET    | `/api/audit/users/me`                            | UserB   | 200      | Returns only UserB's own audit trail |
| B-77  | GET    | `/api/workspace/home`                            | UserB   | 200      | Returns UserB's workspace home |
| B-78  | GET    | `/api/workspace/today`                           | UserB   | 200      | Returns UserB's today view     |
| B-79  | GET    | `/api/workspace/preferences`                     | UserB   | 200      | Returns UserB's preferences    |
| B-80  | GET    | `/api/knowledge`                                 | UserB   | 200      | Returns only UserB's knowledge items |
| B-81  | GET    | `/api/agents`                                    | UserB   | 200      | Returns only UserB's agent profiles |

### Verification method

For each `200` response above, confirm the response body is empty or contains only data belonging to UserB. Cross-reference against UserA's data created in the fixture setup.

---

## Section 4: True-Missing Resource (404) vs Cross-User Denial

Verify that accessing a genuinely nonexistent resource returns `404` with the correct error payload, indistinguishable from the opaque denial in Section 2.

| ID    | Method | Route                                            | Token   | Expected | Notes                          |
|-------|--------|--------------------------------------------------|---------|----------|--------------------------------|
| B-90  | GET    | `/api/boards/00000000-0000-0000-0000-000000000001` | UserA | 404      | Board does not exist           |
| B-91  | GET    | `/api/boards/{BOARD_A}/cards/00000000-0000-0000-0000-000000000001/provenance` | UserA | 404 | Card provenance for nonexistent card on own board |
| B-92  | GET    | `/api/boards/{BOARD_A}/columns` (after deleting all) | UserA | 200 | Empty list, not 404            |
| B-93  | GET    | `/api/capture/items/00000000-0000-0000-0000-000000000001` | UserA | 404 | Capture item does not exist    |
| B-94  | GET    | `/api/llm/chat/sessions/00000000-0000-0000-0000-000000000001` | UserA | 404 | Chat session does not exist    |
| B-95  | GET    | `/api/automation/proposals/00000000-0000-0000-0000-000000000001` | UserA | 404 | Proposal does not exist        |
| B-96  | GET    | `/api/archive/items/00000000-0000-0000-0000-000000000001` | UserA | 404 | Archive item does not exist    |

### Indistinguishability check

Compare the `404` response payload from B-41 (cross-user denial) with B-90 (true missing). The `errorCode` and `message` structure should be identical in shape (both contain `errorCode` and `message` fields). The `message` text may differ, but the structure must not leak whether the resource exists.

---

## Section 5: API Error Payload Contract Verification

Verify that all error responses from auth/validation paths conform to the standard contract:

```json
{
  "errorCode": "<string from ErrorCodes constants>",
  "message": "<human-readable string>"
}
```

Known `errorCode` values (from `ErrorCodes` in `Taskdeck.Domain.Exceptions`):
- `NotFound`, `ValidationError`, `WipLimitExceeded`, `Conflict`, `UnexpectedError`
- `Unauthorized`, `Forbidden`, `TooManyRequests`, `AuthenticationFailed`
- `InvalidOperation`, `LlmQuotaExceeded`, `LlmKillSwitchActive`, `AbuseContainmentActive`

### Status-to-ErrorCode mapping

| HTTP Status | Expected ErrorCodes                              |
|-------------|--------------------------------------------------|
| 400         | `ValidationError`, `WipLimitExceeded`            |
| 401         | `AuthenticationFailed`, `Unauthorized`           |
| 403         | `Forbidden`                                      |
| 404         | `NotFound`                                       |
| 409         | `Conflict`, `InvalidOperation`                   |
| 429         | `TooManyRequests`, `LlmQuotaExceeded`            |
| 500         | `UnexpectedError`, `AbuseContainmentActive` (no explicit mapping -- falls to default) |
| 503         | `LlmKillSwitchActive`                            |

### Targeted error-contract checks

| ID    | Trigger                                                | Expected Status | Expected errorCode    | Payload check               |
|-------|--------------------------------------------------------|-----------------|-----------------------|-----------------------------|
| B-100 | `POST /api/auth/login` with empty body                 | 401             | `AuthenticationFailed`| `errorCode`+`message` present |
| B-101 | `POST /api/auth/login` with wrong password             | 401             | `AuthenticationFailed`| `errorCode`+`message` present |
| B-102 | `POST /api/auth/register` with duplicate username      | 409             | `Conflict`            | `errorCode`+`message` present |
| B-103 | `GET /api/llm-queue/status/not-a-real-status`          | 400             | `ValidationError`     | `errorCode`+`message` present |
| B-104 | `POST /api/automation/proposals/{id}/execute` without `Idempotency-Key` header (use a real approved proposal ID -- a fake ID returns 404 before the header check) | 400 | `ValidationError` | `errorCode`+`message` present |
| B-105 | `GET /api/export/database` (sandbox disabled)          | 403             | `Forbidden`           | `errorCode`+`message` present |
| B-106 | `POST /api/import/database` (sandbox disabled)         | 403             | `Forbidden`           | `errorCode`+`message` present |
| B-107 | `POST /api/boards/{BOARD_A}/cards` with missing `columnId` | 400         | `ValidationError`     | `errorCode`+`message` present |
| B-108 | `POST /api/boards/{BOARD_A}/columns` with empty name   | 400             | `ValidationError`     | `errorCode`+`message` present |
| B-109 | `PATCH /api/boards/{BOARD_A}/cards/{CARD_A}` with `If-Match` stale ETag (if supported) | 409 | `Conflict` | `errorCode`+`message` present |
| B-110 | `POST /api/auth/change-password` with wrong current password | 401 or 400 | `AuthenticationFailed` or `ValidationError` | `errorCode`+`message` present |
| B-111 | `POST /api/account/delete` with wrong password              | 401 or 400 | `AuthenticationFailed` or `ValidationError` | `errorCode`+`message` present |
| B-112 | `POST /api/account/delete` with correct password but wrong confirmation phrase | 400 | `ValidationError` | `errorCode`+`message` present |

### Curl template for error-contract checks

```bash
# B-100: Login with empty body
curl -s -o /tmp/b_100.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{}'
cat /tmp/b_100.json | jq .

# B-103: Invalid queue status
curl -s -o /tmp/b_103.json -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN_A" \
  http://localhost:5000/api/llm-queue/status/not-a-real-status
cat /tmp/b_103.json | jq .

# B-104: Execute proposal without Idempotency-Key
# NOTE: Replace the proposal ID below with a real approved proposal ID.
# Using a fake ID will return 404 before reaching the Idempotency-Key check.
curl -s -o /tmp/b_104.json -w "%{http_code}" \
  -X POST http://localhost:5000/api/automation/proposals/<REAL_APPROVED_PROPOSAL_ID>/execute \
  -H "Authorization: Bearer $TOKEN_A" \
  -H "Content-Type: application/json"
cat /tmp/b_104.json | jq .

# B-105: Database export with sandbox disabled
curl -s -o /tmp/b_105.json -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN_A" \
  http://localhost:5000/api/export/database
cat /tmp/b_105.json | jq .
```

---

## Section 6: Unauthenticated Endpoints (Negative Confirmation)

These endpoints are intentionally unauthenticated. Verify they remain accessible without a bearer token.

| ID    | Method | Route                    | Expected Status | Notes                          |
|-------|--------|--------------------------|-----------------|--------------------------------|
| B-120 | POST   | `/api/auth/login`        | 401 (bad creds) | Endpoint itself is open; 401 is credential failure, not auth-gate |
| B-121 | POST   | `/api/auth/register`     | 200 or 409      | Open registration              |
| B-122 | GET    | `/health/live`           | 200             | Health probe, no auth          |
| B-123 | GET    | `/health/ready`          | 200             | Readiness probe, no auth       |
| B-124 | GET    | `/api/auth/providers`    | 200             | Provider discovery, no auth    |

---

## Section 7: Advanced Controller Families

These controllers have specialized auth or role requirements beyond standard board ownership.

### Ops CLI (`/api/ops/cli`)

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-130 | GET    | `/api/ops/cli/templates`     | UserA   | 200 or 403 | May require admin/ops role  |
| B-131 | POST   | `/api/ops/cli/run`           | UserA   | 200 or 403 | Role-gated execution        |
| B-132 | GET    | `/api/ops/cli/templates`     | No token| 401      | Unauthenticated denial         |

### Logs (`/api/logs`)

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-135 | GET    | `/api/logs`                  | UserA   | 200 or 403 | May require admin/ops role  |
| B-136 | GET    | `/api/logs/stream`           | UserA   | 200 or 403 | SSE streaming, role check   |
| B-137 | GET    | `/api/logs`                  | No token| 401      | Unauthenticated denial         |

### Users (`/api/users`)

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-140 | GET    | `/api/users`                 | UserA   | 200 or 403 | User listing may be restricted |
| B-141 | GET    | `/api/users/{UserA_ID}`      | UserB   | 200 or 403 | Cross-user profile visibility |
| B-142 | POST   | `/api/users/{UserA_ID}/deactivate` | UserB | 403   | Cannot deactivate another user |
| B-143 | PUT    | `/api/users/{UserA_ID}`      | UserB   | 403      | Cannot update another user     |

### Abuse Containment (`/api/abuse`)

| ID    | Method | Route                                         | Token   | Expected | Notes                          |
|-------|--------|-----------------------------------------------|---------|----------|--------------------------------|
| B-150 | GET    | `/api/abuse/actors/{UserA_ID}/status`         | UserA   | 200 or 403 | May require admin role      |
| B-151 | POST   | `/api/abuse/actors/override`                  | UserA   | 200      | **No admin gate in current code** -- any authenticated user can override; file security issue if unexpected |

### LLM Quota (`/api/llm`)

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-155 | GET    | `/api/llm/quota/usage`       | UserA   | 200      | Own quota usage                |
| B-156 | GET    | `/api/llm/quota/status`      | UserA   | 200      | Own quota status               |
| B-157 | POST   | `/api/llm/killswitch`        | UserA   | 403      | Admin-only killswitch          |
| B-158 | GET    | `/api/llm/killswitch`        | UserA   | 200 or 403 | Killswitch status read      |

### Agent Profiles and Runs (`/api/agents`)

| ID    | Method | Route                              | Token   | Expected | Notes                          |
|-------|--------|-------------------------------------|---------|----------|--------------------------------|
| B-160 | GET    | `/api/agents`                       | UserB   | 200      | Returns only UserB's agents    |
| B-161 | GET    | `/api/agents/{AGENT_A}`             | UserB   | 404      | Cross-user agent isolation     |
| B-162 | POST   | `/api/agents/{AGENT_A}/runs`        | UserB   | 404      | Cannot trigger run on foreign agent |

### Knowledge (`/api/knowledge`)

| ID    | Method | Route                              | Token   | Expected | Notes                          |
|-------|--------|-------------------------------------|---------|----------|--------------------------------|
| B-165 | GET    | `/api/knowledge`                    | UserB   | 200      | Returns only UserB's items     |
| B-166 | GET    | `/api/knowledge/{KNOWLEDGE_A}`      | UserB   | 404      | Cross-user knowledge isolation |

### Outbound Webhooks (`/api/boards/{boardId}/webhooks`)

| ID    | Method | Route                                          | Token   | Expected | Notes                          |
|-------|--------|------------------------------------------------|---------|----------|--------------------------------|
| B-170 | GET    | `/api/boards/{BOARD_A}/webhooks`               | UserB   | 403 or 404 | Foreign board webhook list  |
| B-171 | POST   | `/api/boards/{BOARD_A}/webhooks`               | UserB   | 403 or 404 | Cannot create webhook on foreign board |

### External Imports (`/api/boards/{boardId}/imports/external`)

| ID    | Method | Route                                                  | Token   | Expected | Notes                          |
|-------|--------|--------------------------------------------------------|---------|----------|--------------------------------|
| B-175 | POST   | `/api/boards/{BOARD_A}/imports/external`               | UserB   | 403 or 404 | Cannot import into foreign board |

### Data Portability (`/api/account`) — SEC-08

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-180 | GET    | `/api/account/export`        | UserA   | 200      | Returns only UserA's data      |
| B-181 | GET    | `/api/account/export`        | UserB   | 200      | Returns only UserB's data (cross-user isolation) |
| B-182 | POST   | `/api/account/delete`        | UserA   | 401/400  | Wrong password: rejected       |
| B-183 | POST   | `/api/account/delete`        | UserA   | 400      | Wrong confirmation phrase: rejected |

### Board Metrics (`/api/metrics`) — ANL-01

| ID    | Method | Route                                  | Token   | Expected | Notes                          |
|-------|--------|----------------------------------------|---------|----------|--------------------------------|
| B-185 | GET    | `/api/metrics/boards/{BOARD_A}`        | UserA   | 200      | Owner can view own board metrics |
| B-186 | GET    | `/api/metrics/boards/{BOARD_A}`        | UserB   | 404      | Cannot view foreign board metrics |

### Auth Provider Discovery (`/api/auth/providers`)

| ID    | Method | Route                        | Token   | Expected | Notes                          |
|-------|--------|------------------------------|---------|----------|--------------------------------|
| B-190 | GET    | `/api/auth/providers`        | No token| 200      | Intentionally unauthenticated; returns provider availability |

---

## Findings Log

Record mismatches between expected and actual behavior here during execution.

| ID    | Expected | Actual | Severity | Linked Issue | Notes |
|-------|----------|--------|----------|--------------|-------|
|       |          |        |          |              |       |

Severity levels:
- **Critical**: Auth bypass allows data access across user boundaries
- **High**: Wrong status code leaks resource existence (403 instead of 404 on board lookups)
- **Medium**: Missing or malformed error payload (no `errorCode`/`message`)
- **Low**: Inconsistent error message text (structure correct, wording differs)

---

## Regression Rerun Instructions

1. Check out the target commit on `main`.
2. Follow Fixture Setup above to create fresh two-user state.
3. Execute all sections (B-01 through B-175) recording pass/fail.
4. Compare against previous run's Findings Log.
5. File new issues for any regressions; link them here.

Previous runs:
| Date       | Commit   | Runner | Findings Count | Notes |
|------------|----------|--------|----------------|-------|
| (template) | (sha)    | (name) | 0              |       |
