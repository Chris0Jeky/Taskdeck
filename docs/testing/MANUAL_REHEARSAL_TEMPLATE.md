# Manual Rehearsal Runbook Template

Last Updated: 2026-04-15

Companion Active Docs:
- `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Purpose

This template provides a standard format for conducting manual verification cycles as part of the integrated verification program. Each rehearsal run produces a structured evidence package that can be reviewed, audited, and referenced for release gating.

## When to Use This Template

- **Release candidate preparation**: Before tagging a release, complete a rehearsal covering all Tier 1 and selected Tier 2 scenarios from the integrated verification strategy.
- **Major feature delivery**: After merging a wave of cross-cutting changes, verify subsystem interactions that automated tests cannot fully cover.
- **Post-incident validation**: After fixing a production issue, rehearse the affected scenario to confirm the fix and check for regressions.
- **Quarterly confidence check**: Schedule a lightweight rehearsal (~30 minutes) to maintain team familiarity with manual validation paths.

---

## Run Metadata (Fill Before Starting)

Copy this block into the rehearsal evidence record:

```
Rehearsal ID:       RH-YYYY-MM-DD-NNN
Date/Time (UTC):    YYYY-MM-DD HH:MM UTC
Operator:           <name or handle>
Commit SHA:         <full 40-char SHA from `git rev-parse HEAD`>
Branch:             <branch name>
Browser:            <name and version, e.g. Chrome 124.0.6367.91>
OS:                 <e.g. Windows 10 Pro 10.0.19045>
DB Baseline:        fresh | existing | seeded (specify seed script)
Backend URL:        <e.g. http://localhost:5000>
Frontend URL:       <e.g. http://localhost:5173>
Env Overrides:      <list any non-default env vars, or "none">
LLM Provider Mode:  Mock | OpenAI | Gemini | <other>
```

---

## Environment Setup Checklist

Before starting the rehearsal, confirm the following:

- [ ] Backend is running and healthy: `curl http://localhost:5000/health/ready` returns `200`
- [ ] Frontend is running and accessible in the target browser
- [ ] Database is in the expected baseline state (fresh or seeded as specified)
- [ ] Browser DevTools console is open for error monitoring
- [ ] Network tab is open for API response inspection (optional but recommended)
- [ ] Screenshot/recording tool is ready for evidence capture
- [ ] Any required test accounts are created and credentials are available

---

## Scenario Execution Table

For each scenario in the rehearsal plan, fill one row. Use the scenario IDs from `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md` (V-01 through V-18) or custom scenario IDs for ad-hoc checks.

| Scenario ID | Description | Steps | Expected Result | Actual Result | Status | Evidence | Notes |
|-------------|-------------|-------|-----------------|---------------|--------|----------|-------|
| V-XX | _Brief description_ | _Numbered steps or reference to detailed checklist section_ | _What should happen_ | _What actually happened_ | Pass / Fail / Skip / Blocked | _Screenshot filename, request ID, or log snippet_ | _Any observations_ |

### Example Entry

| Scenario ID | Description | Steps | Expected Result | Actual Result | Status | Evidence | Notes |
|-------------|-------------|-------|-----------------|---------------|--------|----------|-------|
| V-02 | Full capture-to-board pipeline | 1. Register new user 2. Create board via Today 3. Capture text 4. Triage from inbox 5. Approve proposal 6. Apply to board 7. Verify card on board | Card appears on board with correct title and provenance links | Card appeared with title "Fix auth bug" in "To Do" column; provenance link to capture item works | Pass | `screenshots/v02-card-on-board.png`, `screenshots/v02-provenance.png` | Capture-to-card took ~6 seconds |

---

## Defect Triage Process

When a scenario fails:

1. **Document immediately**: Record the actual result, evidence, and any error messages in the scenario table.
2. **Classify severity**:
   - **Critical**: Core workflow broken (capture, proposals, board mutations, auth). Blocks release.
   - **High**: Cross-component interaction broken but workaround exists. Should block release unless risk-accepted.
   - **Medium**: Degraded UX or non-critical feature broken. May proceed with release if tracked.
   - **Low**: Cosmetic or minor behavioral deviation. Does not block release.
3. **File issue**: Create a GitHub issue with:
   - Reproduction steps from the scenario table
   - Evidence artifacts (screenshots, logs, request/response payloads)
   - Severity classification
   - Link to this rehearsal run
4. **Link to rehearsal**: Add the issue number to the Notes column of the scenario table.

### Severity Decision Matrix

| Impact | User-facing? | Workaround? | Severity |
|--------|-------------|-------------|----------|
| Core workflow broken | Yes | No | Critical |
| Core workflow broken | Yes | Yes | High |
| Feature broken | Yes | No | High |
| Feature broken | Yes | Yes | Medium |
| Visual/UX glitch | Yes | N/A | Medium or Low |
| Internal/ops only | No | N/A | Low |

---

## Sign-Off Requirements

A rehearsal is complete when:

1. All planned scenarios have a status of Pass, Fail (with linked issue), Skip (with documented reason), or Blocked (with documented blocker).
2. All Critical and High defects have linked GitHub issues.
3. The operator has recorded the summary below.

### Rehearsal Summary (Fill After Completing)

```
Total scenarios planned:    NN
Passed:                     NN
Failed:                     NN (issues: #NNN, #NNN)
Skipped:                    NN (reasons: ...)
Blocked:                    NN (blockers: ...)

Release gate assessment:
  Tier 1 (Critical Path):  PASS | FAIL
  Tier 2 (High-Value):     PASS | FAIL | PARTIAL (N of M passed)
  Tier 3 (Extended):       PASS | FAIL | PARTIAL | NOT ATTEMPTED

Operator sign-off:          <name>, <date>
Reviewer sign-off:          <name>, <date> (required for release candidates)
```

---

## Quick Reference: Scenario Tiers

From `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`:

- **Tier 1 (V-01 to V-04)**: Critical path scenarios that must pass for any release.
- **Tier 2 (V-05 to V-10)**: High-value cross-cutting scenarios that must pass for feature releases.
- **Tier 3 (V-11 to V-18)**: Extended coverage recommended for major releases.

See the strategy document for full scenario descriptions, subsystem mappings, and the automated/manual split.

---

## Artifact Storage Convention

Store rehearsal artifacts in the following structure:

```
docs/testing/rehearsals/
  RH-2026-04-15-001/
    README.md          (copy of this template, filled out)
    screenshots/
      v01-board-state.png
      v02-provenance.png
      ...
    logs/
      backend-errors.txt (if any)
    api-traces/
      v04-401-response.json (optional)
```

For CI-driven rehearsals, upload artifacts to the workflow run instead of committing to the repository.
