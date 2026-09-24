# Advisory UX scenario pilot

Status: draft authoring pack, 2026-09-23. Browser execution, model judgment and native skill activation are NOT RUN. No product behavior, CI gate or existing test is changed.

## Existing ownership

The product journey remains owned by [GH-2901](https://github.com/Chris0Jeky/Taskdeck/issues/2901), especially UX-01 experience switching with unsaved state and zero unintended side effects. This pack does not replace its broader session protocol, renderer matrix, import/export cases or human preference study.

The scenario/rubric contract and adapter are owned by [agent-harness T1 GH-282](https://github.com/Chris0Jeky/agent-harness/issues/282), [T5 GH-286](https://github.com/Chris0Jeky/agent-harness/issues/286) and [PR GH-289](https://github.com/Chris0Jeky/agent-harness/pull/289). Runnable procedure is [claude-config PR GH-323](https://github.com/Chris0Jeky/claude-config/pull/323), with local pilot acceptance tracked by [P5 GH-313](https://github.com/Chris0Jeky/claude-config/issues/313). Taskdeck owns only the product-specific scenario and subsequent product regressions.

The original research pack is machine-local and was not available to this implementation session. This is a proposal aligned to the live issues and inspected product source, not a claimed transcription of unseen research. Reconciliation stays in the research homes.

## Pack and source binding

[experience-continuity.yaml](experience-continuity.yaml) uses the draft `ux-scenario-pack/0` authoring contract. The JSON Schema lives in the harness PR, not a copied schema here. YAML is a serialization of the same model. No new parser, command runner or automation endpoint is shipped.

The subject revision is `622d9d820f48e68ac7fa77d94c9ca2536155ddf8`, whose source was inspected. Before a local run, bind to the actual candidate and recheck fixture/control semantics. Do not relabel old evidence as current by changing only a SHA.

The existing [workspace-overhaul specification](https://github.com/Chris0Jeky/Taskdeck/blob/622d9d820f48e68ac7fa77d94c9ca2536155ddf8/frontend/taskdeck-web/tests/e2e/workspace-overhaul.spec.ts) contains `keeps Home capture and saved thinking across all experience combinations`. Its local setup uses the existing authSession and boardHelpers support. These helpers are product-owned, not exported harness APIs. The pack asks the observer to draft a thought, switch experiences and save exactly once.

**Coverage gap:** the existing specification covers draft retention and saved thinking, but the pack additionally requires explicit baseline/delta evidence for capture/proposal/approval/execution/board-card/provider effects. T5 has not bound all those counters. Missing counter evidence must block the affected assertion, not become a model-estimated zero. The pack cannot run as written until its preconditions are satisfied.

## Local execution boundary

Use the repository's available project browser tooling and existing deterministic tests, with one controller per session. Keep the seeded terminology: Plane B is deterministic observation/checking; Plane A is advisory UX judgment. Do not replace required browser regression gates with this pack or put an LLM score into merge-blocking CI.

The existing Playwright configuration supports `TASKDECK_E2E_DB`, `TASKDECK_E2E_WORKERS` and project `chromium`. A run-owned database variable does not prove a reused server is isolated. Verify the actual server, synthetic user/data and provider configuration; refuse a personal database or unknown live-provider egress. The first pilot is local-agent only, not a cloud browser/agent run.

After the documented repository setup and isolation checks, this existing-test command is a possible baseline, **not executed here**:

```powershell
Set-Location frontend/taskdeck-web
$env:TASKDECK_E2E_DB = Join-Path $env:TEMP ('taskdeck-ux-' + [guid]::NewGuid().ToString() + '.db')
$env:TASKDECK_E2E_WORKERS = '1'
npx.cmd playwright test tests/e2e/workspace-overhaul.spec.ts --project=chromium --grep 'keeps Home capture and saved thinking across all experience combinations' --trace on --workers=1
```

This command runs the existing test, not an implemented YAML adapter or every new counter assertion. Trace-on retains the successful control for local review; no CI setting changes. Muse computer use is untrusted until exact-host/runtime qualification. A read-only recipe or shell result is not proof of browser/vision capability.

## Evidence and review

Keep raw traces/screenshots/auth state local by default. A run receipt needs exact product/configuration identity, fixture/reset identity, action/step log, artifacts with digests and capture times, explicit failed/retried actions and before/after durable outcomes. Record `partial` or `blocked_environment` when appropriate. Never repair the product invisibly during an observation or use an API shortcut to hide a failed UI action.

Task completion, deterministic assertion results and UX quality findings are separate outputs. The independent judge may assess effectiveness, usability, simplicity, clarity and feel only to the extent evidenced. Static images cannot establish timing or tactile feedback. A clean automated accessibility scan is not full accessibility acceptance. Missing evidence remains missing, not a passing score.

Deduplicate supported findings against GH-2901 and existing product defects. Prepare a sanitized evidence-bound draft using the harness issue-seed format; an authorized coordinator owns publication. A reproduced functional bug gets a separate narrow deterministic regression/fix PR. Subjective preferences are batched for the design owner, not auto-filed as dozens of defects.

## Remaining work

T1 reviews the authoring contract; T5 binds the counters and evidence adapter; T2 captures a known-good control and seeded-defect fixture locally; P5 proves fresh-session skill discovery/activation and an actual observed journey. The broader GH-2901 acceptance remains open. Full Taskdeck docs-governance/link checks and browser execution are not established by the schema check.

This docs-only pilot does not change shipped-state documents, outstanding owner decisions, application code, tests, provider defaults, permissions or CI. Alibi is deferred rather than given an ungrounded copy of this Taskdeck-specific journey.
