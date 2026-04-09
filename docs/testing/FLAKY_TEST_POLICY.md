# Flaky Test Policy

Last Updated: 2026-04-09

## Purpose

This document defines how flaky E2E tests are identified, quarantined, and remediated in the Taskdeck test suite. The goal is to maintain CI signal quality: a red build should always mean a real problem.

## Definition

A test is **flaky** when it produces inconsistent pass/fail results across runs without any code change. Common causes:

- Timing-dependent waits or race conditions
- Test isolation failures (shared state between tests or browser profiles)
- Browser-specific rendering timing (especially cross-browser matrix)
- Network/server startup non-determinism

## Tagging Strategy

Taskdeck E2E tests use Playwright tag annotations in test titles:

| Tag | Purpose | Runs in CI |
|-----|---------|------------|
| (no tag) | Default smoke tests | PR gate (chromium only) |
| `@smoke` | Explicit smoke designation | PR gate (chromium only) |
| `@cross-browser` | Critical journeys across all desktop browsers | Nightly + manual (`testing` label) |
| `@mobile` | Mobile viewport responsive tests | Nightly + manual (`testing` label) |
| `@quarantine` | Known flaky, excluded from CI | Never (local debug only) |

### How to tag a test

Add the tag to the test title string:

```typescript
test('@cross-browser board creation workflow', async ({ page }) => {
  // ...
})

test('@mobile card editing on small screen', async ({ page }) => {
  // ...
})
```

Multiple tags can be combined:

```typescript
test('@cross-browser @mobile responsive navigation', async ({ page }) => {
  // ...
})
```

## CI Matrix Strategy

| CI Lane | Trigger | Projects Run | Tag Filter |
|---------|---------|-------------|------------|
| `ci-required.yml` (PR gate) | Every PR/push | chromium only | All tests except `@mobile` |
| `ci-extended.yml` | `testing` label or manual | All 5 projects | Per-project grep (see config) |
| `ci-nightly.yml` | Daily 03:25 UTC | All 5 projects | Per-project grep (see config) |

## Quarantine Process

### Step 1: Identify

When a test fails intermittently (2+ inconsistent results in nightly or PR runs):

1. File a GitHub issue with label `flaky-test` and link the failing test file/line
2. Include failure logs, trace artifacts, and which browser(s) are affected

### Step 2: Quarantine

Add `@quarantine` tag to the test title:

```typescript
test('@quarantine @cross-browser flaky board reload test', async ({ page }) => {
  // ...
})
```

The Playwright config excludes `@quarantine` from all CI projects via `grepInvert`. The test still runs locally for debugging.

To add quarantine exclusion to all projects, add this to `playwright.config.ts` in the top-level `use` block or per-project:

```typescript
grepInvert: /@quarantine/,
```

### Step 3: Investigate

The issue assignee must:

1. Reproduce locally (run the specific test with `--repeat-each=5`)
2. Check for timing issues (missing `waitFor`, race conditions)
3. Check for test isolation issues (shared state, database leaks)
4. Check for browser-specific behavior (compare across projects)

### Step 4: Fix and Un-quarantine

1. Fix the root cause
2. Verify stability: run `npx playwright test --project=<affected> --grep="test name" --repeat-each=10`
3. Remove the `@quarantine` tag
4. Close the issue with a link to the fix PR

## Remediation Timeline

| Severity | SLA | Escalation |
|----------|-----|------------|
| Blocks PR gate (chromium smoke) | Fix within 24 hours or quarantine | Immediate team notification |
| Nightly cross-browser failure | Fix within 1 week | Review in next standup |
| Nightly mobile-only failure | Fix within 2 weeks | Track in sprint backlog |

## Prevention Guidelines

1. **Use explicit waits**: Always `await expect(locator).toBeVisible()` before interacting
2. **Avoid fixed timeouts**: Use `waitForResponse` / `waitForURL` instead of `page.waitForTimeout`
3. **Isolate test state**: Each test gets a fresh user via `registerAndAttachSession`
4. **Use unique names**: Include `Date.now()` in board/card/column names to prevent collisions
5. **Test deterministically**: Avoid tests that depend on animation timing or CSS transitions
6. **Keep browser profiles independent**: Never share cookies, localStorage, or database state across browser projects

## Monitoring

- Nightly CI results are reviewed daily for new failures
- Flaky test issues are prioritized alongside regular bugs
- A test that has been quarantined for more than 30 days without progress should be escalated or removed
