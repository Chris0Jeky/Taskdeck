# Manual Audit Pack

An opt-in headed Playwright suite for operator-visible debugging and pre-release sanity checks.

## Quick Start

```bash
cd frontend/taskdeck-web
npm run test:e2e:audit:headed
```

With live LLM provider probes:

```bash
TASKDECK_RUN_LIVE_LLM_TESTS=1 npm run test:e2e:audit:headed
```

## What It Covers

### Core Loop (Home -> Inbox/Capture -> Review -> Board)

1. Home landing page renders correctly
2. Capture item created and visible in Inbox
3. Triage initiated from Inbox detail view
4. Proposal appears in Review view after triage
5. Approve and apply proposal
6. Card appears on board with provenance links back to capture and proposal

### Advanced Checks

- Command palette search navigates to Inbox
- Capture hotkey (`Ctrl+Shift+C`) opens modal and saves item
- Board creation, column/card management, and filter panel

### Live LLM Provider Probe (opt-in)

- LLM health check (configured -> verified)
- First chat turn returns a live (non-degraded) response

Gated behind `TASKDECK_RUN_LIVE_LLM_TESTS=1`. Skipped by default.

## Screenshots

Every test step captures a numbered screenshot to the Playwright output directory. These are useful for visual regression comparison, audit trails, and debugging.

Screenshots are saved as `01-home.png`, `02-inbox-with-capture.png`, etc. in the test output path (typically `test-results/`).

## When to Use

| Scenario | Use this pack? |
|----------|---------------|
| Local operator audit before release | Yes |
| Visual debugging a UI regression | Yes |
| Pre-demo sanity check (quick) | Yes |
| Full stakeholder demo recording | No -- use `stakeholder-demo.spec.ts` with `TASKDECK_RUN_DEMO=1` |
| CI smoke gate | No -- use `npm run test:e2e` (default headless) |
| Live LLM provider verification | Yes, with `TASKDECK_RUN_LIVE_LLM_TESTS=1` |

## How It Differs from Other E2E Packs

- **Default smoke (`npm run test:e2e`)**: Headless, fast, runs in CI. Tests individual features in isolation.
- **Stakeholder demo recorder (`stakeholder-demo.spec.ts`)**: Requires seeded demo data, captures video, designed for external presentation. Opt-in via `TASKDECK_RUN_DEMO=1`.
- **Manual audit pack (`npm run test:e2e:audit:headed`)**: Headed with slow motion (250ms), captures screenshots at each milestone, covers the full capture-review-board loop end-to-end. Designed for operator debugging and quick visual audits. No demo seed required.

## Configuration

The pack uses the standard `playwright.config.ts` with these test-level overrides:

- `screenshot: 'on'` -- always capture screenshots
- `trace: 'retain-on-failure'` -- trace files kept on failure for debugging
- `launchOptions.slowMo: 250` -- 250ms delay between actions for visual clarity
- `--headed` -- browser visible (set via npm script)
- `--reporter=line` -- compact output for terminal readability
