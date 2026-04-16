# Manual Rehearsal Runbook: Slice C — Automation Proposals, Chat Bootstrap, and Execution Safety

Last Updated: 2026-04-16

Companion references:
- `docs/testing/MANUAL_VALIDATION_SLICE_C_SCENARIOS.md` (scenario catalog)
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist, Section D)
- `docs/GOLDEN_PRINCIPLES.md` (GP-06: Review-First Automation Safety)

## Purpose

Executable runbook for manual validation of the Slice C scenario catalog. Each section maps directly to a scenario ID (TST09-SC-NNN) and provides step-by-step instructions, evidence capture requirements, and pass/fail criteria.

---

## Environment Setup

### 1. Backend

```bash
# From repo root
# Optional: delete database for fresh state
rm -f backend/src/Taskdeck.Api/taskdeck.db

# Start API
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

Verify: `curl -s http://localhost:5000/health/live` returns `200`.

### 2. Frontend

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Verify: open `http://localhost:5173` (or fallback port) in browser.

### 3. User Fixture

Run the bootstrap script from `MANUAL_VALIDATION_SLICE_C_SCENARIOS.md` (Fixture Bootstrap Script section) to create UserA, UserB, and the test board. Save all token and ID values.

### 4. Browser Setup

- Use Chromium-based browser (Chrome or Edge) with DevTools open.
- Open Console tab to monitor for JavaScript errors.
- Open Network tab to observe API calls.

### 5. Run Metadata

Record before starting:

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | |
| Browser and version | |
| OS | |
| DB baseline | `fresh` / `existing` |
| LLM provider mode | `mock` / `live` |
| Env flags changed | |
| Artifacts directory | |

---

## Evidence Capture Instructions

### What to capture

| Evidence type | When to capture | Format |
|---|---|---|
| **Screenshot** | On every FAIL; on PASS for scenarios marked "evidence required" | PNG, named `TST09-SC-NNN_step-N.png` |
| **Browser console log** | On every FAIL; on PASS if JavaScript errors are present | Copy-paste to text file `TST09-SC-NNN_console.txt` |
| **Network request/response** | On API-level FAIL scenarios (status code mismatches) | HAR export or copy-paste of request/response headers+body |
| **curl output** | For all API-direct scenarios (SC-018 through SC-034) | Save to `/tmp/sc_NNN.json` as directed in scenario catalog |
| **Board state screenshot** | Before and after any proposal execution test | PNG, named `TST09-SC-NNN_board_before.png` / `TST09-SC-NNN_board_after.png` |

### Naming convention

```
artifacts/
  YYYY-MM-DD_run/
    TST09-SC-001_pass.txt
    TST09-SC-011_fail_screenshot.png
    TST09-SC-011_fail_console.txt
    TST09-SC-018_curl_output.json
    run_metadata.txt
```

---

## Execution Checklist

### Category 1: Chat Session Behavior

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-001 | Create chat session without board context | [ ] | [ ] | [ ] | |
| TST09-SC-002 | Create board-scoped chat session | [ ] | [ ] | [ ] | |
| TST09-SC-003 | Non-actionable chat prompt (greeting) | [ ] | [ ] | [ ] | |
| TST09-SC-004 | Non-actionable chat prompt (system question) | [ ] | [ ] | [ ] | |
| TST09-SC-005 | Actionable prompt -- create card | [ ] | [ ] | [ ] | Evidence required: board state screenshot |
| TST09-SC-006 | Actionable prompt -- move card | [ ] | [ ] | [ ] | Evidence required: board state screenshot |
| TST09-SC-007 | Actionable prompt -- archive card | [ ] | [ ] | [ ] | |
| TST09-SC-008 | Multi-instruction message | [ ] | [ ] | [ ] | |

### Category 2: Malformed and Adversarial Prompts

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-009 | Empty message submission | [ ] | [ ] | [ ] | |
| TST09-SC-010 | Extremely long message (10K+ chars) | [ ] | [ ] | [ ] | |
| TST09-SC-011 | XSS payload in chat message | [ ] | [ ] | [ ] | Evidence required: screenshot showing escaped text |
| TST09-SC-012 | SQL injection payload | [ ] | [ ] | [ ] | |
| TST09-SC-013 | HTML injection in session title | [ ] | [ ] | [ ] | Evidence required: screenshot showing escaped title |
| TST09-SC-014 | Unicode and emoji in chat messages | [ ] | [ ] | [ ] | |

### Category 3: Proposal Lifecycle

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-015 | Proposal approve then execute (golden path) | [ ] | [ ] | [ ] | Evidence required: board state before/after |
| TST09-SC-016 | Proposal reject | [ ] | [ ] | [ ] | |
| TST09-SC-017 | Verify board state unchanged after reject | [ ] | [ ] | [ ] | Evidence required: board state screenshot |
| TST09-SC-018 | Double-approve prevention | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-019 | Double-execute prevention | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-020 | Execute without Idempotency-Key | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-021 | Expired proposal handling | [ ] | [ ] | [ ] | May require waiting or DB manipulation |
| TST09-SC-022 | Approve expired proposal via API | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-023 | Proposal diff human-readable | [ ] | [ ] | [ ] | Evidence required: diff panel screenshot |
| TST09-SC-024 | Proposal dismiss for terminal statuses | [ ] | [ ] | [ ] | |

### Category 4: Checklist Bootstrap

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-025 | Checklist text generates proposal via capture triage | [ ] | [ ] | [ ] | |
| TST09-SC-026 | Checklist bootstrap end-to-end | [ ] | [ ] | [ ] | Evidence required: card provenance links screenshot |
| TST09-SC-027 | Multi-item checklist capture | [ ] | [ ] | [ ] | |

### Category 5: Cross-User Proposal Isolation

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-028 | UserB cannot see UserA proposals | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-029 | UserB cannot approve UserA proposal | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-030 | UserB cannot see UserA chat sessions | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-031 | UserB cannot execute UserA proposal | [ ] | [ ] | [ ] | API-direct test |

### Category 6: Audit Trail Verification

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-032 | Proposal execution creates audit entry | [ ] | [ ] | [ ] | |
| TST09-SC-033 | Rejected proposal audit entry | [ ] | [ ] | [ ] | |
| TST09-SC-034 | Board mutation audit after proposal apply | [ ] | [ ] | [ ] | |

### Category 7: High-Risk Intent Prompts

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-035 | Bulk operation prompt | [ ] | [ ] | [ ] | GP-06 compliance check |
| TST09-SC-036 | Delete/destructive intent prompt | [ ] | [ ] | [ ] | GP-06 compliance check |
| TST09-SC-037 | Prompt with negation context | [ ] | [ ] | [ ] | |

### Category 8: LLM Health and Provider States

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-038 | LLM health banner states | [ ] | [ ] | [ ] | |
| TST09-SC-039 | Degraded provider response handling | [ ] | [ ] | [ ] | May require live provider |

### Category 9: Review View UX Correctness

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-040 | Review card layout and sticky footer | [ ] | [ ] | [ ] | Evidence required: screenshot |
| TST09-SC-041 | Review proposal links preserve board context | [ ] | [ ] | [ ] | |

### Category 10: Execution Safety Edge Cases

| ID | Title | Pass | Fail | Skip | Notes |
|---|---|---|---|---|---|
| TST09-SC-042 | Reject then re-approve prevention | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-043 | Execute without prior approve | [ ] | [ ] | [ ] | API-direct test |
| TST09-SC-044 | Concurrent approve race condition | [ ] | [ ] | [ ] | Requires two browser tabs |
| TST09-SC-045 | Proposal on deleted/archived board | [ ] | [ ] | [ ] | |

---

## Summary Section

Complete after the run:

| Metric | Count |
|---|---|
| Total scenarios | 45 |
| Passed | |
| Failed | |
| Skipped | |
| Blocked | |

---

## Defect Triage Template

When a scenario fails, file a defect using this template:

```markdown
## Title
[TST09-SC-NNN] <short description of failure>

## Labels
manual-validation-c, bug

## Severity
Critical / High / Medium / Low

## Scenario Reference
- Scenario ID: TST09-SC-NNN
- Step that failed: Step N
- Slice C catalog: `docs/testing/MANUAL_VALIDATION_SLICE_C_SCENARIOS.md`

## Environment
- Date/time (UTC):
- Commit SHA:
- Browser:
- OS:
- LLM provider mode:

## Expected Behavior
<from scenario catalog>

## Observed Behavior
<what actually happened>

## Evidence
- Screenshot: <path or attachment>
- Console log: <path or attachment>
- Network capture: <path or attachment>

## GP-06 Impact Assessment
Does this failure indicate a silent/destructive board mutation? [Yes/No]
If Yes: escalate to Critical severity regardless of original assessment.

## Reproduction Steps
1. <step-by-step to reproduce>
2. ...

## Related Issues
- <link to any existing related issues>
```

---

## Severity Definitions (Slice C Specific)

| Level | Definition | Example |
|---|---|---|
| **Critical** | Automation mutates board state without explicit user approval (GP-06 violation); cross-user data access | Card appears on board before proposal is approved; UserB can read UserA's proposals |
| **High** | State machine bypass or safety invariant violation | Double-approve succeeds; execute without approve succeeds; rejected proposal can be re-approved |
| **Medium** | Missing or incorrect error payload; UI rendering issue; missing audit entry | 500 instead of 400 on invalid operation; diff shows raw GUIDs; no audit trail for execution |
| **Low** | Cosmetic issue; inconsistent message text; non-blocking UX friction | Error message wording differs from expected; health banner color slightly off |

---

## Automation Coverage Map

These scenarios have corresponding Playwright E2E tests:

| Scenario | E2E Test File | Coverage |
|---|---|---|
| TST09-SC-001 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-002 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-003 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-005 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-010 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-011 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-012 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-013 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-015 | `validation-automation-proposals.spec.ts` | Full |
| TST09-SC-016/017 | `validation-automation-proposals.spec.ts` | Full |
| TST09-SC-018 | `validation-automation-proposals.spec.ts` | API-level |
| TST09-SC-019 | `validation-automation-proposals.spec.ts` | API-level |
| TST09-SC-020 | `validation-automation-proposals.spec.ts` | API-level |
| TST09-SC-028/029 | `validation-automation-proposals.spec.ts` | API-level |
| TST09-SC-030 | `validation-automation-proposals.spec.ts` / `validation-chat-bootstrap.spec.ts` | API-level |
| TST09-SC-038 | `validation-chat-bootstrap.spec.ts` | Full |
| TST09-SC-042 | `validation-automation-proposals.spec.ts` | API-level |
| TST09-SC-043 | `validation-automation-proposals.spec.ts` | API-level |

### Scenarios requiring manual-only validation

| Scenario | Reason for manual-only |
|---|---|
| TST09-SC-004 | Tool-calling behavior depends on live provider |
| TST09-SC-006, SC-007 | Card ID prefix resolution requires specific board state |
| TST09-SC-008 | Multi-instruction parsing behavior varies by provider |
| TST09-SC-009 | Client-side validation behavior is UI-specific |
| TST09-SC-014 | Unicode rendering verification requires visual inspection |
| TST09-SC-021, SC-022 | Expired proposal requires time passage or DB manipulation |
| TST09-SC-023 | Diff rendering requires visual inspection |
| TST09-SC-024 | Dismiss behavior across multiple statuses requires sequential state setup |
| TST09-SC-025-027 | Capture triage flow covered by `capture-loop.spec.ts`; slice C adds manual observation |
| TST09-SC-031 | Cross-user execute isolation |
| TST09-SC-032-034 | Audit trail verification requires API inspection |
| TST09-SC-035-037 | High-risk/negation intent behavior varies by provider |
| TST09-SC-039 | Degraded response styling requires visual inspection |
| TST09-SC-040 | Sticky footer and scroll behavior require visual inspection |
| TST09-SC-041 | Multi-entry-point navigation requires manual browser work |
| TST09-SC-044 | Race condition requires precise two-tab timing |
| TST09-SC-045 | Archived board edge case requires sequential state manipulation |
