# Testing Guide

This is the active testing guide for Taskdeck.

Last Updated: 2026-08-09
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Current Verified Totals (2026-05-16)

- Backend: **6,614 passing** (0 failed, 6 skipped; 6,620 total) -- verified 2026-05-16 via `dotnet test backend/Taskdeck.sln -c Release -m:1` on `main` after bulk merge of PRs `#1055`–`#1074`
  - Domain: 1,626 passed
  - Application: 3,185 passed
  - API integration: 1,685 passed (0 failed, 2 skipped; 1,687 total)
  - CLI contract (**newer than this dated aggregate**): 112 passed / 0 skipped / 0 failed on Windows at `#1533`; hosted exact-head Windows recertification remains required
  - Architecture boundaries: 0 failed, **1 skipped** (only INV-09/DataFlowRegistry; INV-10/11/12 un-skipped with real assertions in #1126) — exact pass/total pending CI recertification (#1138)
  - Integration project (**newer than this dated aggregate**): 35 tests at `#1520` — 28 PostgreSQL-backed cases plus 7 Docker-independent fixture/native checks. Dockerless evidence is 7 passed / 28 skipped; positive PostgreSQL evidence requires all 28 container cases to execute and pass the hosted TRX identity contract.
- Frontend unit: **3,267 passing** -- verified 2026-05-16 post-bulk-merge (CI)
- Frontend E2E (smoke + automation/ops + capture loop + starter-pack fixtures + concurrency harness + error recovery/multi-board/edge journeys + cross-browser matrix + onboarding/review/capture/keyboard/dark-mode + validation slices C/D/E + integrated verification): default required lane passing
- Combined automated total: **~9,881+ passing** (backend 6,614 + frontend unit 3,267 + E2E)

Verification note:
- backend total of 6,614 is locally recertified after bulk merge of 15 PRs (`#1055`–`#1074`) on 2026-05-16
- bulk merge wave (2026-05-16): security fixes (3 PRs), test coverage (2), RFAI features (5), PAPER frontend (2), dependency updates (3)
- prior recertification: backend 6,336 (2026-05-05 after Paper backend gap PR `#1040`), frontend 2,805 (2026-04-25)
- growth since last recertification: backend +278 passing tests, frontend +462 passing tests

## OpenAI-Compatible Provider Replacement Checkpoint (`#1306`)

Current-main integration verification on 2026-08-09 exercises the compatible
provider, the dispatch/accounting seam, and the registered transport chain:

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~OpenAiCompatibleLlmProviderTests|FullyQualifiedName~CircuitBreakerStateTrackerTests|FullyQualifiedName~LlmDispatchTrackingHandlerTests|FullyQualifiedName~ChatServiceTests|FullyQualifiedName~LlmCaptureTriageExtractorTests"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~LlmProviderRegistrationTests|FullyQualifiedName~ProtectedOutboundTelemetryHandlerTests|FullyQualifiedName~CircuitBreakerTests"
```

Results: **157 passed / 0 failed / 0 skipped** in Application and **81 passed /
0 failed / 0 skipped** in API. Coverage includes buffered completion and real
registered-loopback SSE; UTF-8 byte, line, event, and aggregate ceilings;
content-filter/refusal handling without persisting provider detail; schema-v2
triage compatibility; zero/known usage normalization; every-redirect rejection; exact-origin egress and DNS
checks; direct-only proxy and telemetry controls; scoped Sentry removal; 501
stream fallback outside the compatible Polly failure set; pre-dispatch versus
dispatched quota settlement; and deterministic half-open race/cooldown behavior
across separate Polly and companion circuit states.

The compatible registration expands the protected-client inventory from the
four clients proved by `#1513` to five: OpenAI, OpenAICompatible, Gemini, Ollama,
and outbound webhook delivery. The earlier pre-integration provider tree passed the required full gate:

```powershell
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Result: **7,660 passed, 5 intentional skips, 0 failed** (Domain 1,636;
Application 3,678; API 2,189 + 4 skips; CLI 100; Architecture 22 + 1 skip;
Integration 35). Docs governance, golden-principles governance, GitHub-operations
governance, and `git diff --check` also passed. The streamed-refusal repair has
a clean independent review; exact merged-head hosted CI, DCO, and final integration review remain pending. A
maintainer-supplied compatible-provider key and a visibly incremental stream in
the real UI are separate human gates; loopback transport tests do not prove
either.
## 2026-08-02 REVIVAL-08 M2 Checkpoint (`#1304`)

Long-transcript triage needs both the chunk-planning contract and the existing proposal-first golden
path. Run the focused checks below when changing transcript map-reduce, quota reservation estimates,
the transcript input cap, or its readiness-progress boundary:

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptTriageChunkingTests|FullyQualifiedName~LlmCaptureTriageExtractorTests|FullyQualifiedName~LlmQuotaServiceTests"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptTriageLlmGoldenPathIntegrationTests|FullyQualifiedName~LlmQuotaReservationConcurrencyTests|FullyQualifiedName~PdfPigArtefactExtractionTests|FullyQualifiedName~HealthApiTests"
```

```powershell
Set-Location frontend/taskdeck-web
npm test -- src/tests/components/CaptureModal.spec.ts
npm run typecheck
```

The map-reduce path must preserve proposal-first behavior: any failed map leg falls back for the whole
capture, never persists a partial automatic board write. The M3 contract has its own checkpoint
below; `#1305` evidence linkage remains outside both checkpoints.

## 2026-08-02 REVIVAL-08 M3 Contract Checkpoint (`#1304`)

When changing the strict LLM schema-v2 contract, prompt/parser, exact evidence quote boundary, or
the v2-to-proposal mapping, run:

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~CaptureTriageOutputContractTests|FullyQualifiedName~LlmCaptureTriagePromptTests|FullyQualifiedName~LlmCaptureTriageExtractorTests|FullyQualifiedName~CaptureTriageServiceTests"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptTriageLlmGoldenPathIntegrationTests"
```

The application seam must reject missing, unknown, wrongly typed, noncanonical, over-limit, or
non-verbatim model fields as a complete fallback; it must not normalize or retain a partial map leg.
It also pins that model classification, assignee, due-date, and confidence metadata never enter
executable operation parameters. The API golden path proves a valid exact quote remains reviewable
in the proposed card description. `#1305` durable evidence spans/provenance/API/UI linkage remains
outside this contract-only checkpoint.

## 2026-08-01 Merged-Main Checkpoints (`#1305`, `#1354`, `#1520`)

The durable Transcript foundation is covered directly by its domain, portability/deletion, repository,
and migration/bootstrap seams:

```powershell
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptTests"
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~DataExportServiceTests|FullyQualifiedName~AccountDeletionServiceTests|FullyQualifiedName~GdprDataExportRoundTripTests"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptRepositoryIntegrationTests|FullyQualifiedName~MigrationBootstrapTests"
```

Current-main result: **5 Domain + 63 Application + 14 API tests passed**, with no failures or
skips. This proves persistence, export/deletion, and migration/bootstrap behavior only; `#1305`
remains open for triage linkage, evidence spans, provenance API reads, and Paper deep links.

The MCP create-card column contract is exercised through the real write tool and SQLite-backed
proposal lifecycle:

```powershell
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~McpToolsTests"
```

Current-main result: **33 passed / 0 failed / 0 skipped**. The cases pin omitted-column
canonicalization and the inaccessible-board, wrong-board-column, and columnless-board failures.

The container result parser and normal Dockerless contract are independently reproducible:

```powershell
py -3 -B -m unittest scripts.ci.test_assert_container_integration_results
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release
```

Current-main result: **5/5 parser tests passed** and the local Dockerless project reported
**7 passed / 28 skipped / 0 failed**. That second result proves graceful gating only. Positive
PostgreSQL evidence comes from the hosted required-mode lane described below, whose TRX must contain
zero skipped PostgreSQL-backed cases and at least 28 passing fully qualified PostgreSQL tests.

## Proxy-Safe Direct Egress Checkpoint (`#1513`)

Issue-branch verification shipped in PR `#1516` on 2026-07-27 and covers the direct-only primary
clients for OpenAI, Gemini, Ollama, and outbound webhook delivery:

```powershell
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~LlmProviderRegistrationTests|FullyQualifiedName~OutboundWebhookConnectCallbackTests|FullyQualifiedName~OutboundWebhookDeliveryWorkerTests|FullyQualifiedName~ProtectedOutboundTelemetryHandlerTests"
```

Result: **66 passed, 0 failed, 0 skipped**. The tests resolve the real
`IHttpMessageHandlerFactory` pipelines, assert `UseProxy = false`,
`AllowAutoRedirect = false`, and the existing `ConnectCallback`; exercise a
hostile configured proxy against blocked loopback, private, and link-local
origins without exposing protected content; and prove permitted direct
`localhost` delivery without consulting the proxy. They also resolve each concrete provider to
prove the selected localhost policy reaches runtime validation, capture Trace-level logs to prove
default request logging is absent, dispatch both `ProbeAsync` and `CompleteAsync` through all three
concrete registered providers to loopback endpoints, fully decode Content-Length or chunked JSON bodies,
assert provider-specific payloads, prove Production policy injection overrides a raw Ollama localhost
opt-in before dispatch; and correlate unique control/protected requests
to prove normal trace propagation/activity/metric export while protected requests propagate no
`traceparent`, `tracestate`, or baggage and contribute no destination dimensions to Taskdeck's
configured OpenTelemetry exporter. The metric guarantee is deliberately scoped to Taskdeck's exporter, not arbitrary
process-global listeners. Registered-provider controls also prove that outer .NET HTTP EventSource
payloads do not contain the configured path/query while the real configured origin reaches the wire,
and that Sentry's outbound handler was removed only from the four then-registered protected clients: the unrelated
`GitHubConnectorProvider` client retains the handler and sends `sentry-trace`. Public caller-owned
provider clients retain their configured URI, request body, and authentication. The guarantee does
not cover independently installed Activity/Meter listeners or transport-stage host/IP observation.

```powershell
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~OpenAiLlmProviderTests|FullyQualifiedName~GeminiLlmProviderTests|FullyQualifiedName~OllamaLlmProviderTests|FullyQualifiedName~LlmProviderResilienceTests|FullyQualifiedName~LlmProviderSelectionPolicyTests|FullyQualifiedName~LlmProviderConstructorCompatibilityTests|FullyQualifiedName~ProtectedOutboundTelemetryHandlerTests"
```

The six provider-dispatch cases passed five consecutive repetitions, and the registered EventSource
and scoped-Sentry boundary pair passed three fresh-process repetitions. The provider and
selection-policy compatibility subset (`OpenAiLlmProviderTests`, `GeminiLlmProviderTests`,
`OllamaLlmProviderTests`, `LlmProviderResilienceTests`, and `LlmProviderSelectionPolicyTests`)
passed **151 / 0 failed / 0 skipped**; the constructor-compatibility and remasking classes add
**7 / 0 failed / 0 skipped**, so the documented Application filter proves **158 / 0 / 0**.
Docs governance, golden-principles governance, GitHub-operations governance, and
`git diff --check` passed on the same working tree.

The full serialized backend passed at pre-documentation head `dad8d22a` with **7,539 passed,
5 intentional skips, and 0 failed** (Domain 1,636; Application 3,577; API 2,171 + 4 skips;
CLI 100; Architecture 20 + 1 skip; Integration 35). The only subsequent issue-scope change is
this verification-document correction; the current-main merge adds the separately reviewed
`#1522` frontend/docs slice and leaves the backend subtree identical. Published head `dc2a099c`
then passed exact-head Required CI 16/16, CI Extended with four successes and 11 intentional
path-based skips, and CodeQL 4/4 before merging as `840874ac`.

## Roadmap v4 Verification Spine (Seeded 2026-04-25)

Tracker `#972` seeds the next review-first AI verification program. Delivered items are marked; remaining items are planned work until their implementation issues land:

- `#973`: (**delivered**, `#986`) twelve roadmap invariants covering automation-only mutation safety, proposal execution idempotency/version checks, outbound egress envelope coverage, disclosure registry coverage, MCP tool-definition hash pinning, telemetry content rejection, and proposal source-span integrity.
- `#974`: (**delivered**, `#989`) schema/provider smoke coverage for `IntentEnvelopeV1`, `TaskdeckProposalBatch`, `IChatClient` adapter viability, and the `JsonSchemaExporter` vs handwritten-schema decision. 117 tests.
- `#975`--`#977`: (**delivered**, `#993`/`#994`/`#991` + `#1071`/`#1058`/`#1062`) golden proposal dataset checks, schema validity, extractive quote/span verification, inferred evidence-link resolution, field confidence scoring, and edit-before-approve paths. Full delivery: `IProposalGenerator`/`FieldVerifier`/`ProposalGeneratorV1` (`#1071`), revision endpoints + edit-before-approve flow (`#1058`), Paper Review deep-dive wired to backend APIs (`#1062`). Combined: provenance 139 + revision 70 + confidence 136 + generator/verifier/review wiring tests. _(Update `#1198`, 2026-06-13: `ProposalGeneratorV1` + its `IProposalGenerator` interface and test class were removed as dead code (zero consumers); `FieldVerifierTests` and the provenance/confidence/revision suites are unaffected.)_
- `#978`--`#979`: (**both delivered**, `#990`/`#1050`) vector-search fallback tests, embedding backfill safety (61 tests). RFAI-07 hybrid retrieval, duplicate calibration, and memory-assisted generation delivered in `#1050`.
- `#980`: (**delivered**, `#992` + `#1073`/`#1074`) TelemetryGuard fuzz rejection, egress registry completeness (108 tests), egress disclosure API endpoint (`#1073`), privacy insights API for proposal outcome cohorts (`#1074`).
- `#981`: (**delivered**, `#1052`) agent runtime hardening — property tests for no approve/direct-mutation tools, egress handler violation tests, MCP definition re-approval, and scheduled Inbox Digest quota/coalescing.
- `#982`--`#983`: (**both delivered**, `#1078`/`#1079`) ambient capture provenance — PWA share target (RFAI-10, `#1078`; browser-extension prototype deferred — only a `CaptureSource.BrowserExtension = 10` enum placeholder exists, no MV3/WXT artifact shipped), and the VS Code extension + voice prototype ambient channel (RFAI-11, `#1079`).
- `#984`: (**delivered**, `#1080`) beta-gate work — learning loop UI, provenance drawer, Ollama flag. NOTE: the CI-visible `RoadmapInvariantTests` for INV-10 (MCP hash-pin), INV-11 (TelemetryGuard), and INV-12 (provenance source spans) are now un-skipped with real assertions against the shipped services (`#1126`). Only INV-09 (DataFlowRegistry) remains `[Fact(Skip = "...")]` — that registry is genuinely unbuilt.

## Windows PowerShell Command Convention

For agent-run commands on Windows, do not use `&&` directly in PowerShell. Prefer fail-fast sequences that check `$LASTEXITCODE`, for example:

```powershell
Push-Location frontend/taskdeck-web
npm run typecheck; if ($LASTEXITCODE -ne 0) { Pop-Location; exit $LASTEXITCODE }
npm run build; if ($LASTEXITCODE -ne 0) { Pop-Location; exit $LASTEXITCODE }
npx vitest --run
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { exit $code }
```

## Workflow Lint Bootstrap Checks

CI Extended's `Workflow Lint` job bootstraps checksum-pinned Actionlint and Pyflakes artifacts directly, uses the Ubuntu runner's ShellCheck through an explicit path, runs the focused contract suite, then lints every checked-out workflow verbosely. The hosted job is the authoritative integration proof: its unchanged-head log must show both checksum checks, Actionlint 1.7.12, Pyflakes 3.4.0, the runner ShellCheck version, the checked-out SHA, a positive workflow count, seven passing contract checks, and a zero-error repository lint. A run that fails before checkout or never reads the workflows is not green evidence.

On native Windows, set Git Bash explicitly because bare `bash` can resolve to the Microsoft Store/WSL alias. The static, ordering, and checksum boundary is the portable fast path:

```powershell
$env:BASH_BIN = 'C:\Program Files\Git\bin\bash.exe'
node --test --test-name-pattern='pins|bootstrap boundary|checksum verifier' scripts/ci/actionlint-bootstrap.test.mjs
```

The full seven-check suite additionally requires local Actionlint, ShellCheck, and Pyflakes executables:

```powershell
$env:BASH_BIN = 'C:\Program Files\Git\bin\bash.exe'
$env:ACTIONLINT_BIN = '<path-to-actionlint>'
$env:ACTIONLINT_SHELLCHECK_BIN = '<path-to-shellcheck>'
$env:ACTIONLINT_PYFLAKES_BIN = '<path-to-pyflakes>'
node --test scripts/ci/actionlint-bootstrap.test.mjs
```

Do not infer external-linter coverage from Actionlint alone: Actionlint can skip ShellCheck or Pyflakes when they are unavailable. Keep the explicit tool paths and the fixture assertions for `SC2086` and the Pyflakes undefined-name diagnostic.

## Agentic Operating Layer Checks

For docs/skill/agent-tooling changes, use targeted checks rather than the full product suite unless product runtime files changed. Taskdeck installs no project runtime hooks. The local sequence renders deliberately recorded JSONL entries before testing synchronization; Required CI does not render and keeps its test-before-governance order, so an unprojected JSONL change fails instead of being masked. On Windows PowerShell, use the verified Python launcher:

```powershell
$ErrorActionPreference = "Stop"
try {
    Get-Command py -ErrorAction Stop | Out-Null
    Get-Command node -ErrorAction Stop | Out-Null
    Get-Content -Raw .mcp.json | ConvertFrom-Json -ErrorAction Stop | Out-Null
    $claudeSettings = Get-Content -Raw .claude\settings.json | ConvertFrom-Json -ErrorAction Stop
    Get-Content -Raw .agent-harness\tier.json | ConvertFrom-Json -ErrorAction Stop | Out-Null
    if ($null -ne $claudeSettings.PSObject.Properties['hooks']) { throw 'Taskdeck project hooks must remain absent.' }
    if ($null -ne $claudeSettings.permissions.PSObject.Properties['deny']) { throw 'Taskdeck project deny rules must remain absent.' }
    if (Test-Path -LiteralPath .codex\hooks.json) { throw 'Taskdeck Codex project hooks must remain absent.' }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .codex\skills\taskdeck-question-batch; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .codex\skills\taskdeck-failure-capture; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .codex\skills\taskdeck-interface-map; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .claude\skills\taskdeck-question-batch; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .claude\skills\taskdeck-failure-capture; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" .claude\skills\taskdeck-interface-map; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B scripts/agent_hooks/render_failure_ledger.py; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoLogo -NoProfile -NonInteractive -File scripts\git\Test-New-CodexIssueWorktree.ps1; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
node scripts\check-docs-governance.mjs; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
node scripts\check-golden-principles.mjs; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
node --test scripts\check-github-ops-governance.test.mjs; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
node scripts\check-github-ops-governance.mjs; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

On POSIX, use `python3 -B` for the manual utilities and fail fast:

```sh
set -eu
python3 -B -m json.tool .mcp.json >/dev/null
python3 -B -m json.tool .claude/settings.json >/dev/null
python3 -B -m json.tool .agent-harness/tier.json >/dev/null
python3 -B -c 'import json, pathlib; settings = json.loads(pathlib.Path(".claude/settings.json").read_text()); assert "hooks" not in settings; assert "deny" not in settings["permissions"]; assert not pathlib.Path(".codex/hooks.json").exists()'
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .codex/skills/taskdeck-question-batch
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .codex/skills/taskdeck-failure-capture
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .codex/skills/taskdeck-interface-map
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .claude/skills/taskdeck-question-batch
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .claude/skills/taskdeck-failure-capture
python3 -B "$HOME/.codex/skills/.system/skill-creator/scripts/quick_validate.py" .claude/skills/taskdeck-interface-map
python3 -B scripts/agent_hooks/render_failure_ledger.py
python3 -B -m unittest discover -s scripts/agent_hooks -p 'test_render_failure_ledger.py'
node scripts/check-docs-governance.mjs
node scripts/check-golden-principles.mjs
node --test scripts/check-github-ops-governance.test.mjs
node scripts/check-github-ops-governance.mjs
```

The staging-gate governance regression pins the complete parked workflow after normalizing line
endings. Any intentional edit to that workflow requires a reviewed digest and fixture update plus
Actionlint; substring checks are not treated as proof of effective YAML semantics.

The project settings checks are structural proof only. A fresh runtime hook inventory is still needed to distinguish no Taskdeck project hooks from surviving user-, organization-, or runtime-level controls.

When MCP availability itself is part of the change, also run the active runtime's MCP listing/auth command if available. Do not claim remote MCP connectivity unless the current session actually verified it.

When `.claude/settings.json` changes outside this path, also parse it with PowerShell:

```powershell
Get-Content -Raw .claude\settings.json | ConvertFrom-Json | Out-Null
```

## GitHub Project Priority Audit Checks

For changes to `scripts/github/Sync-TaskdeckProjectPriority.ps1`, run the offline parser and regression checks first:

```powershell
$script = "scripts\github\Sync-TaskdeckProjectPriority.ps1"
$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script).Path, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -ne 0) { $parseErrors | Format-List; exit 1 }

powershell -NoProfile -File $script -SelfTest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`-SelfTest` is authentication-free, currently reports **102 checks**, and includes a mocked 1,001-item project plus fail-closed cases for early termination, exact duplicate IDs, case-distinct IDs, count/stamp drift, repeated or missing cursors, exact-Boolean outer and nested pagination metadata (including null/string/number/object values), truncated nested connections, a positive limit ceiling, aggregated missing/multiple same-repository Issue priority labels before PR lookups, raw REST Issue/PullRequest normalization, same-repository authority, default-off external Issue authority, mixed/external-only references, strict `Priority V` fallback, ignored-reference source drift, ordered option dispatch, zero-write option validation, partial writer failures, and verified post-apply output. The restart coverage proves drift-then-success, late-page whole-snapshot restart from `after = null`, explicit bound exhaustion, malformed-metadata mixed-fault non-retry precedence, CLI-scale limit compatibility, intrinsic overflow and terminal premature-count precedence over count/stamp drift, and zero Apply writes when the pre-write snapshot exhausts its restart budget or intrinsic pagination faults are observed. The Required CI docs-governance job parses the script and runs this same authentication-free suite.

Then exercise the live read-only boundaries:

```powershell
# On a project larger than 1,000 items, this must exit nonzero rather than sample.
powershell -NoProfile -File $script -Limit 1000 -Json
if ($LASTEXITCODE -eq 0) { throw "Expected the configured ceiling to fail closed." }

# Complete audit. Do not treat a nonzero exit as a clean result.
powershell -NoProfile -File $script -Json
```

A successful JSON audit must say `complete: true`, with `scanned == reportedTotalCount`; a clean audit additionally requires `needsUpdate: 0`. Body references that resolve to PullRequests do not contribute Issue priority. External Issue references are default-off, visible non-authority: verify `ignoredIssueReferenceCount` and every exact record in `ignoredIssueReferences`; external labels never contribute ranking, an external-only PR derives `Priority V`, and an external closing Issue permits same-repository body fallback. External Issue or PullRequest content placed directly in the project remains fatal. Missing or multiple labels on actual same-repository Issues are aggregated data-policy failures that abort before PR reference resolution or writes; fix every listed label defect first. Never add `-Apply` merely to test the helper. Only with `project` scope and reviewed updates should an operator run `-Apply`; ignored-reference identity/count participates in the pre-write drift guard, the command must perform its complete post-apply audit even after a partial writer failure, and a final separate read-only audit must prove the resulting project is complete and clean.

## Paper Backend Gap Testing (2026-05-05, PRs `#1031`–`#1040`)

The Paper backend gap wave through PR `#1040` added ~480 new backend tests across 10 delivered/merge-ready issues. Each delivered PR received adversarial review; later review rounds found and fixed issues including a 100k entity memory risk, a board-scoping error, missing FK enforcement, CancellationToken threading gaps, projected WIP false negatives, JSON parsing 500s, and conflict-detector false positives.

### Cadence Aggregation Tests (`#1015`/`#1031`)

`backend/tests/Taskdeck.Domain.Tests/Entities/CadenceSnapshotTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/CadenceServiceTests.cs` — **26 tests** covering:
- CadenceBucket hour validation (0-23), event count non-negative, equality semantics
- CadenceSnapshot: 24-bucket invariant, null guard, cached `Empty()` singleton, first/peak/last action computation
- CadenceService: empty day, single event, full day aggregation, peak hour ties, midnight boundary, date normalization

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Cadence"
```

### Streak Query Tests (`#1016`/`#1032`)

`backend/tests/Taskdeck.Domain.Tests/Entities/StreakDayTests.cs`, `StreakResultTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/StreakServiceTests.cs` — **61 tests** covering:
- StreakDay: intensity bucket validation (0-4), DateOnly handling, sealed flag
- StreakResult: current/longest streak invariant (current cannot exceed longest), empty days
- StreakService: empty history, single day, continuous streak, gap in streak, gap at end, intensity quartile bucketing, day count boundaries (1, 90, 365), server-side `CountByDateAsync` aggregate query

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Streak"
```

### Seal Day Tests (`#1017`/`#1037`)

`backend/tests/Taskdeck.Domain.Tests/Entities/DailySnapshotTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/DailySealServiceTests.cs` — **28 tests** covering:
- DailySnapshot: construction, seal idempotency (second seal is no-op preserving original timestamp), future date rejection, IsSealed property, empty userId rejection
- DailySealService: seal new day, seal existing unsealed, seal already-sealed (idempotent), validation errors, status checks for missing/sealed/unsealed snapshots, CancellationToken propagation
- UnitOfWork: DailySnapshot unique constraint violation recovery (concurrent seal race condition)

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Seal or FullyQualifiedName~DailySnapshot"
```

### Tomorrow Note Tests (`#1018`/`#1035`)

`backend/tests/Taskdeck.Domain.Tests/Entities/TomorrowNoteTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/TomorrowNoteServiceTests.cs` — **25 tests** covering:
- TomorrowNote: constructor validation, text max length (500 chars), date handling, UpdateText behavior and timestamp
- TomorrowNoteService: get existing/missing note, save new/update existing (upsert), empty userId rejection, null text handling, max length boundary
- UnitOfWork: TomorrowNote unique constraint violation recovery (concurrent upsert race condition)

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~TomorrowNote"
```

### Provenance Query Tests (`#1019`/`#1039`)

`backend/tests/Taskdeck.Application.Tests/Services/ProvenanceQueryServiceTests.cs` — **41 tests** covering:
- Icon map: 26-entry case-insensitive map with fallback default icon
- Weight bucketing: extractive >= 0.7 confidence → "primary", < 0.7 → "contextual", inferred → "inferred"
- Human-readable value strings with quote snippet truncation, `Math.Round` for confidence display
- Empty provenance (returns empty list, not error), missing proposal, authorization
- FK enforcement via `AddProposalProvenanceForeignKey` migration

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~ProvenanceQuery"
```

### Side-Effect Analysis Tests (`#1020`/`#1033`)

`backend/tests/Taskdeck.Domain.Tests/Entities/SideEffectTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/SideEffectAnalyzerTests.cs` — **66 tests** covering:
- SideEffectRow: value object creation, tone enum, equality/hash contract
- Reversibility: default 6h window (21,600,000ms), summary/description
- SideEffectAnalyzer: 7-category tone classification (Cards, Subtasks, Comments, Activity, Notifications, Webhooks, Calendar), target-type-aware card mutation detection, column mutation inclusion, webhook conditional on operations existing, risk-based reversibility (Critical → 3h)

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~SideEffect"
```

### Confidence Breakdown Tests (`#1021`/`#1036`)

`backend/tests/Taskdeck.Domain.Tests/Confidence/ConfidenceComponentTests.cs`, `ConfidenceBreakdownTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/Confidence/ConfidenceBreakdownServiceTests.cs` — **63 tests** covering:
- ConfidenceComponent: value range [0..1], NaN/Infinity rejection, key validation
- ConfidenceBreakdown: overall/threshold range, MeetsThreshold computed property, defensive component list copy
- ConfidenceBreakdownService: 4-component weighted computation (Pattern match, Reach, Reversibility, Recency), reach formula `2.0 / (2.0 + log2(n))`, risk-level reversibility scoring, recency from expiry window, threshold note generation, static weight map

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~ConfidenceBreakdown"
```

### Conflict Detection Tests (`#1022`/`#1040`)

Status: reconciled and locally verified after merge-conflict recovery.

`backend/tests/Taskdeck.Domain.Tests/Entities/ConflictRowTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/ProposalConflictDetectorTests.cs`, `backend/tests/Taskdeck.Api.Tests/AutomationProposalRepositoryIntegrationTests.cs`, `backend/tests/Taskdeck.Api.Tests/CardCommentRepositoryIntegrationTests.cs` — **78 tests** covering:
- ConflictRow: tone enum, value object creation, equality
- ProposalConflictDetector: 7 detection rules — stale data (excludes create-card ops), missing target card, WIP limit, duplicate pending proposals (all pending, not just latest), high/critical risk, outbound webhooks, active comments, multi-op on same card, positive signals (column capacity, fresh data)
- Entity caching (each card/column fetched at most once), safe JSON parsing with ValueKind checks, projected WIP accounting including departures, missing target columns, soft-deleted comment exclusion, repository aggregate methods, tone-sorted output (Warn → Info → Ok)

Run:
```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release --filter "FullyQualifiedName~ConflictRow"
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~ProposalConflictDetector"
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~AutomationProposalRepositoryIntegrationTests|FullyQualifiedName~CardCommentRepositoryIntegrationTests"
```

### Card History Tests (`#1023`/`#1034`)

`backend/tests/Taskdeck.Domain.Tests/Entities/CardHistoryRowTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/CardHistoryServiceTests.cs` — **42 tests** covering:
- CardHistoryRow: serial formatting, status enum, validation, equality
- CardHistoryService: single/multi-card history, serial numbering, age formatting (same day, yesterday, this week, older) with `InvariantCulture`, status classification (pending/applied/past), proposal deduplication via `HashSet<Guid>`, bounded output (200/card, 500 total), proper JSON property parsing for update events

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~CardHistory"
```

### Similar Past Decisions Tests (`#1024`/`#1038`)

`backend/tests/Taskdeck.Domain.Tests/SimilarPast/SimilarPastDecisionTests.cs`, `SimilarPastResultTests.cs`, `backend/tests/Taskdeck.Application.Tests/Services/SimilarDecisionServiceTests.cs` — **50 tests** covering:
- SimilarPastDecision: value object validation, title truncation (200 chars), verdict enum
- SimilarPastResult: apply rate computation, division-by-zero safety, negative input rejection
- SimilarDecisionService: board-scoped action-class matching (review fixed userId filter), user-scoped fallback, top-3 limiting with full-population apply rate, self-exclusion, serial/date formatting (ISO week with 2-digit year), 200-proposal lookback limit, SARGable `OrderByDescending(DecidedAt)` ordering

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~SimilarPast or FullyQualifiedName~SimilarDecision"
```

## Roadmap v4 Second-Wave Testing (2026-04-25, PRs `#989`–`#994`)

The RFAI-02 through RFAI-08 foundational slice wave (PRs `#989`–`#994`) added ~631 new backend tests across 6 PRs. Each PR received adversarial review with review-added tests fixing bot findings from Gemini and Codex connector reviews.

### IntentEnvelopeV1 Tests (RFAI-02, `#974`/`#989`)

`backend/tests/Taskdeck.Domain.Tests/Entities/IntentEnvelopeV1Tests.cs`, `IntentCandidateTests.cs`, `SourceBlockTests.cs`, `SourceSpanTests.cs`, `EvidenceLinkTests.cs`, `TaskdeckProposalBatchTests.cs`, `ProposalBatchSchemaRoundTripTests.cs` — **117 tests** covering:
- IntentEnvelopeV1 lifecycle (Created→Extracting→Processed), candidate addition, evidence linking
- SourceBlock/SourceSpan validation: offset ranges, snippet length consistency, evidence fabrication prevention
- IntentCandidate confidence bounds, evidence link construction
- ProposalBatch schema round-trip smoke tests against handwritten JSON schema

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~IntentEnvelope or FullyQualifiedName~SourceSpan or FullyQualifiedName~SourceBlock or FullyQualifiedName~IntentCandidate or FullyQualifiedName~EvidenceLink or FullyQualifiedName~ProposalBatch"
```

### Semantic Memory Vector Index Tests (RFAI-06, `#978`/`#990`)

`backend/tests/Taskdeck.Application.Tests/Services/InMemoryVectorIndexTests.cs`, `InMemoryEmbeddingGeneratorTests.cs`, `EmbeddingBackfillServiceTests.cs`, `FallbackSemanticSearchServiceTests.cs` — **61 tests** covering:
- InMemoryVectorIndex: upsert, duplicate replacement, batch upsert, nearest-neighbor accuracy, topK limits, metadata filtering, delete, concurrent reads/writes, cosine similarity edge cases (zero/orthogonal/parallel vectors)
- InMemoryEmbeddingGenerator: dimensionality, determinism, normalization, empty/null input, batch alignment, cross-instance consistency
- EmbeddingBackfillService: generator-unavailable skip, batch processing, individual failure isolation, stale vector pruning, cancellation
- FallbackSemanticSearchService: FTS fallback, empty queries, vector search happy path, exception-triggered fallback

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~VectorIndex or FullyQualifiedName~EmbeddingGenerator or FullyQualifiedName~EmbeddingBackfill or FullyQualifiedName~SemanticSearch"
```

### Confidence Pipeline Tests (RFAI-05, `#977`/`#991`)

`backend/tests/Taskdeck.Domain.Tests/Confidence/ConfidenceScoreTests.cs`, `FieldConfidenceTests.cs`, `SelfConsistencyQuotaTests.cs`, `SelfConsistencyPolicyTests.cs`, `ConfidenceBucketTests.cs` and `backend/tests/Taskdeck.Application.Tests/Services/Confidence/BrierScoreCalculatorTests.cs`, `ConfidenceAggregatorTests.cs` — **136 tests** covering:
- ConfidenceScore: boundary values, floating-point precision, epsilon equality, hash code consistency, CompareTo/Equals alignment, NaN/Infinity rejection
- SelfConsistencyQuota: immutability (Consume returns new instances), overflow protection, non-finite cost rejection, budget exhaustion
- ConfidenceBucket: contiguous boundaries with no gaps/overlaps, monotonic sweep verification
- BrierScoreCalculator: calibration math, skill score, division-by-zero safety, non-finite input rejection
- ConfidenceAggregator: weighted combination, missing-source handling, zero-weight safety

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Confidence or FullyQualifiedName~BrierScore"
```

### Eval Harness and Egress Tests (RFAI-08, `#980`/`#992`)

`backend/tests/Taskdeck.Application.Tests/Services/TelemetryGuardTests.cs`, `EgressRegistryTests.cs`, `InsightMetricTests.cs`, `Eval/EvalHarnessTests.cs` — **108 tests** covering:
- TelemetryGuard: allowlist enforcement, URL/email detection, non-finite doubles, null values, max-length strings, ReDoS adversarial input, unsupported value type rejection (dictionaries, DTOs, arrays, DateTime, Guid)
- EgressRegistry: seed entry completeness, case-insensitive host matching, wildcard pattern matching, runtime registration, host validation, thread safety
- InsightMetric: PII-freedom (reflection-based verification), structural constraints
- EvalHarness: runner execution, category coverage, summarization, seed case determinism

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~TelemetryGuard or FullyQualifiedName~EgressRegistry or FullyQualifiedName~InsightMetric or FullyQualifiedName~EvalHarness"
```

### Proposal Provenance Tests (RFAI-03, `#975`/`#993`)

`backend/tests/Taskdeck.Domain.Tests/Entities/ProposalProvenanceTests.cs`, `ProvenanceFieldTests.cs`, `ProposalOutcomeTests.cs`, `EvidenceLinkTests.cs` covering:
- ProposalProvenance: field addition, parent-ID validation, field count tracking
- ProvenanceField: extractive quote enforcement, confidence bounds, kind validation
- ProposalOutcome: content-free decision ledger, decision type coverage

_(Update `#1215`, 2026-07-04: `FieldVerificationResultTests`, `FuzzyTextMatcherTests`, and `DeterministicPreExtractorTests` were removed with the dead-code sweep of `FieldVerifier`/`DeterministicPreExtractor`/`FuzzyTextMatcher` — see STATUS. The original RFAI-03 suite was 139 tests; the remaining provenance/outcome/evidence tests are unaffected.)_

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Provenance or FullyQualifiedName~ProposalOutcome"
```

### Proposal Revision Tests (RFAI-04, `#976`/`#994`)

`backend/tests/Taskdeck.Domain.Tests/Entities/ProposalRevisionTests.cs`, `ProposalRevisionChainTests.cs`, `CompilerValidationResultTests.cs`, `OperationRiskTests.cs`, `ProposalOutcomeTests.cs`, `UnsupportedOperationFailureTests.cs` — **70 tests** covering:
- ProposalRevision: creation, immutability (private setters), validation, DateTimeOffset precision
- Revision chain: latest resolution, ordering, no-revisions case, many-revisions integrity, unique constraint
- CompilerValidationResult: success/failure factory, risk aggregation
- OperationRisk: value equality semantics, risk level + reason
- OutcomeType: decision coverage (Approved/EditedThenApproved/Rejected/Ignored)

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~ProposalRevision or FullyQualifiedName~CompilerValidation or FullyQualifiedName~OperationRisk or FullyQualifiedName~UnsupportedOperation"
```

## Audit-Finding Remediation Wave Testing (2026-04-24, PRs `#960`–`#969`)

The 2026-04-24 audit-finding remediation wave (PRs `#960`–`#969`) added ~186 new tests across 10 issues. Each PR received two rounds of adversarial review (original self-review + independent cold review); the second round found and fixed 15+ issues including 3 critical bugs (SQLite DateTimeOffset comparison, SQL Server DELETE syntax, keyboard navigation).

### Composable Tests (FE-22, FE-21)

- `frontend/taskdeck-web/src/tests/composables/useCardModal.spec.ts` — **61 tests** covering card/isOpen watchers, `formattedDueDate`/`isOverdue`/`isFormValid` computed branches, capture provenance loading, save with field deltas, delete flow, comment CRUD, reply drafts, `canEditComment`, cleanup
- `frontend/taskdeck-web/src/tests/composables/useStarterPackCatalog.spec.ts` — **40 tests** covering `loadCatalog`, `filteredPacks`, `selectedPack`, `runPreview`, `applyPack`, `extractConflictResult`, guard-clause early returns
- `frontend/taskdeck-web/src/tests/composables/useStarterPackImport.spec.ts` — **48 tests** covering `validateImportJson`, `handleFileUpload`, `runImportPreview`, `applyImportPack`, guard/error paths
- `frontend/taskdeck-web/src/tests/composables/useStarterPackResult.spec.ts` — **37 tests** covering `normalizeConflictSeverity`, `actionSummary`, `outcomeSummaryLabel`, `outcomeSummaryToneClass`

Coverage impact: `src/composables/**` branch coverage rose from 79.22% to 83.27% (threshold: 80%).

### Backend Tests (OPS-31, SEC-31)

- `backend/tests/Taskdeck.Api.Tests/Workers/AuditRetentionWorkerIntegrationTests.cs` — audit retention batch deletion with SQLite DateTimeOffset formatting
- `backend/tests/Taskdeck.Application.Tests/Services/OAuthScopeValidatorTests.cs` — scope parsing (comma/space/tab), required/expected validation, case-sensitive comparison, null safety

### Error Reporting Tests (FE-24)

- `frontend/taskdeck-web/src/tests/utils/errorReporting.logError.spec.ts` — 3 new tests for `.message`-bearing objects, variadic DEV/PROD modes

## Production Hardening Wave Testing (2026-04-22, PRs `#902`–`#913`)

The 2026-04-22 production hardening wave (PRs `#902`–`#913`) added ~210+ new tests across 10 tracked audit issues plus 2 CI stabilisation fixes. Each PR received self-review plus bot reviews; review-fix commits addressed CI failures, false-positive tests, performance bugs, and test-flake root causes.

### SSRF Protection Tests (SEC-26, `#850`/`#905`)

`backend/tests/Taskdeck.Application.Tests/Services/SsrfProtectionServiceTests.cs` — **40 `[Fact]`/`[Theory]` entries expanding to 83 test cases**:
- Private IPv4 ranges: `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- IPv6 ranges: `::1`, `fc00::/7`, `fe80::/10`, IPv4-mapped IPv6
- Cloud metadata: AWS `169.254.169.254`, GCP `metadata.google.internal`, Azure `169.254.169.254`, Alibaba `100.100.100.200`, AWS IMDSv2 IPv6 `fd00:ec2::254`
- Bypass attempts: decimal (`2130706433`), hex (`0x7f000001`), octal (`0177.0.0.1`), short-form (`127.1`) — all normalised by `.NET Uri` before the IP range check
- Scheme validation: non-HTTP/HTTPS rejected; credential-bearing URLs stripped
- LLM provider URL HTTPS enforcement (except with `allowLocalhostEndpoints`)

Plus expanded `OutboundWebhookEndpointGuardTests` with cloud metadata hostname coverage and LLM provider selection policy tests for private IP, IPv6 loopback, IPv4-mapped IPv6, and cloud metadata `BaseUrl`.

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~SsrfProtectionService"
```

### File Content Validation Tests (SEC-30, `#860`/`#910`)

`backend/tests/Taskdeck.Application.Tests/Services/FileContentValidatorTests.cs` — **52 `[Fact]`/`[Theory]` entries** covering:
- Valid/invalid text with BOM
- Binary content detection (null bytes, C1 control characters `0x80-0x9F`)
- Unicode smart quotes (`U+2018-U+201D`) and dashes (`U+2013-U+2014`) pass correctly (they are above `0x9F`)
- Character-based limits (maxChars parameter) for CJK/emoji safety
- JSON structure validation with BOM-aware parsing
- SQLite magic byte detection (`SQLite format 3\0`)
- Size limits: `maxBytes: 0` disables the cap (used by `ExportController` for round-trip)

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~FileContentValidator"
```

### Migration Bootstrap Tests (OPS-28, `#864`/`#907`)

`backend/tests/Taskdeck.Api.Tests/MigrationBootstrapTests.cs` — **5 tests** ensuring the EF Core migration chain remains unbroken:
- Migrations apply cleanly to a fresh SQLite database
- All expected tables are created (verified via reflection against `DbContext` DbSets, not a hardcoded list)
- Re-running `Migrate()` is idempotent
- `HasPendingModelChanges()` reports no drift (detects real model/snapshot divergence, not just missing migration files — surfaced the missing `ExternalLogins.UserId` FK)
- All migration timestamps are distinct

`GetUserTables()` uses `OpenConnection()`/`CloseConnection()` instead of disposing the `DbContext`-owned connection. Workflow guide at `docs/platform/EF_MIGRATION_WORKFLOW.md`.

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MigrationBootstrap"
```

### Options Validation Tests (OPS-27, `#863`/`#908`)

`backend/tests/Taskdeck.Api.Tests/Validation/OptionsValidationTests.cs` — **34 `[Fact]`/`[Theory]` entries** covering data-annotation boundaries for 15 settings classes, 4 cross-property validators (`WorkerSettings`, `JwtSettings`, `SentrySettings`, `RateLimitingSettings`), case-insensitive regex for `Llm:Provider`/`Cache:Provider`, and an integration test proving the app starts with valid defaults.

Run:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~OptionsValidation"
```

### Board Pagination API Tests (PERF-12, `#848`/`#909`)

`backend/tests/Taskdeck.Api.Tests/BoardPaginationApiTests.cs` — **11 integration tests** covering default pagination, empty list, limit enforcement, offset skipping, partial page, limit clamped to 200, negative offset, offset beyond total, zero limit clamped to 1, full page iteration, and cross-user isolation with pagination metadata. Existing tests that deserialised `List<BoardDto>` from `GET /api/boards` migrated to use the new `PaginatedResult<BoardDto>` shape via `ApiTestHarness.ListBoardsAsync`/`ListBoardsPaginatedAsync` helpers.

### CLI Tests Restored (TST-58, `#853`/`#906`)

CLI test discovery was fixed by adding missing `[Fact]`/`[Theory]` attributes and extracting a shared `CliTestHarness` (replacing ~90-line duplication across 5 files). The CLI suite totalled approximately **78 tests across 10 files** at time of this wave; its newer exact local count is **127 tests** at `#1530`:

| File | Tests |
|------|-------|
| `ArgParserTests.cs` | 15 |
| `ApiKeyCommandTests.cs` | 12 |
| `CardsCommandTests.cs` | 11 |
| `ColumnsCommandTests.cs` | 10 |
| `BoardsCommandTests.cs` | 7 |
| `ConsoleOutputTests.cs` | 7 |
| `CliActorIdentityTests.cs` | 5 |
| `ExitCodesTests.cs` | 4 |
| `CliJsonContractTests.cs` | 4 |
| `CommandDispatcherTests.cs` | 3 |

Harness improvements: `AppContext.BaseDirectory` dll lookup replaces the fragile repo-tree walk; invalid timeouts are rejected before temporary-directory allocation; every child receives an isolated harness working directory; all real-process contracts, including `CliJsonContractTests`, use the shared launcher; a source invariant rejects any second process-launch file; ordinary real-process launches are serialized while lifecycle tests inject a wider gate only for deterministic cleanup coverage; stdout, stderr, and exit are observed concurrently; and the default outer process deadline is derived from `SerializedMigrator.DefaultLockTimeout` (30 seconds) plus a separately bounded command-completion budget (30 seconds), leaving 60 seconds total for the inner lock wait followed by migration, dispatch, and disposal. Default harnesses copy a process-local, fully disposed, fully migrated empty SQLite byte template into their own paths, so command contracts still execute real CLI startup and `SerializedMigrator` lock acquisition without rebuilding the complete migration chain for every test. The template is built exactly once through a thread-safe `Lazy<byte[]>`, uses pooling-disabled SQLite without persistent WAL state, is read only after context disposal, and leaves no persistent template directory. Copies remain mutation-isolated. `preprovisionDatabase: false` retains an explicit cold-start path; the full-lifecycle test proves an absent database is created by the CLI, all migrations apply, all eight trace phases complete, and product tables remain empty. Deadline cleanup kills the tree, falls back to a direct root kill when tree termination reports an expected platform error, and polls every explicitly tracked PID for up to five seconds before returning. Every failure after a successful process start, including output-drain failures, takes that termination/reap path before the launch slot is released. The first fault is observed without awaiting sibling tasks; remaining observations are canceled and explicitly settled after cleanup. A throwing cancellation callback cannot bypass reap and is preserved as an additional cause. Successful cleanup otherwise preserves the selected original failure, while cleanup failure preserves all causes and poisons the shared launch gate. Poisoning wakes queued callers with an error while retaining the failed root's capacity so no later child is admitted beside it; bounded reap expiry additionally reports the exact live-PID set. When the test harness supplies its allow-listed correlation, the CLI derives a fixed-format trace filename inside its own working directory; normal invocations receive no correlation. Timeout output is redacted to command shape plus fixed process/task/phase state, and tracing failure is fail-open. Deterministic process-start, queue, cancellation, output-drain fault, cancellation-callback failure, and reaper-barrier signals prove both two-root overlap and one-slot serialization/reap ordering without fixed-delay scheduler assumptions. The deterministic deadline-contract test proves the outer bound remains strictly greater than the migrator's lock bound without sleeping. The hard-deadline reap regression retains an injected 500-millisecond deadline and deliberately accepts any startup phase, while a separate migration-lock regression waits for the bounded trace to reach `migration-begin` before injecting cancellation. The lifecycle stress collection disables parallelization so its fixed two-root probe never adds load beside other CLI test classes. `[Collection("Console Tests")]` on `ConsoleOutputTests` preserves `Console.Out` thread safety when xUnit runs classes in parallel, and `InternalsVisibleTo` on `Taskdeck.Cli` lets `Cli.Tests` unit-test internal types directly.

Run:
```bash
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release
```

The deterministic timeout/reap contract can be run separately:

```bash
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CliTestHarnessTests"
```

### CI Stabilisation Tests

- `#912` (ActivityView Windows flake): `ActivityView.spec.ts` `seedBoards()` now assigns deterministic ordered ISO timestamps so the component's `updatedAt DESC` sort is stable across platforms regardless of `Date` resolution. Verified 10/10 consecutive runs green locally.
- `#913` (FirstRunBootstrapper cross-process race): serialised via named cross-process mutex + atomic rename. Guards the parallel xUnit-process JSON-corruption race that `#911` surfaced.

## Feature/Security Expansion Wave Testing (2026-04-09, PRs `#806`–`#813`)

The feature and security expansion wave (PRs `#806`–`#813`) added ~231+ new tests across 8 PRs. Each PR received two rounds of adversarial review (self + independent cold review); the independent round caught 9 CRITICAL and 11 HIGH issues — all fixed.

New test categories:
- **Calendar endpoint**: 8 backend tests covering date range validation, board-access scoping, overdue/blocked status, empty results
- **Note import**: 38 backend unit tests for markdown section splitting, web clip intake, validation, provenance; 6 frontend API client tests
- **Agent surfaces**: 42 frontend tests across agentStore (15), AgentsView (8), AgentRunsView (8), AgentRunDetailView (11)
- **Telemetry/observability**: 25 backend unit tests (opt-in enforcement, event validation, property allowlist) + 13 backend integration tests (DI, endpoints) + 25 frontend tests (consent, store buffering, API)
- **OAuth PKCE/account linking**: 24+ backend tests covering DB-backed auth codes, atomic consumption, PKCE, account linking conflicts
- **SSO/OIDC/MFA**: 30+ backend tests covering TOTP validation, email collision, cross-provider isolation, username deduplication, MFA policy, recovery codes
- **Staged deployment**: smoke test script with 9 automated checks (health, API, auth, frontend, SignalR, static assets, security headers, container restart)

Storybook (non-test tooling): `npm run storybook` runs 17 Td* primitive stories; `npm run storybook:build` produces static output.

## Supplementary Test Depth Wave (2026-04-13, PRs `#821`–`#826`)

~429 new tests across 6 PRs. Each PR received two rounds of adversarial review (self-review + independent cold review). Key review findings and fixes:

### Concurrency and Race Condition Stress Tests (`#705`/`#825`)

22 backend tests across 7 files in `backend/tests/Taskdeck.Api.Tests/Concurrency/`:
- Queue claim races (4): double-claim prevention, stale timestamp, batch processing, two-worker different items
- Card update conflicts (5): concurrent moves, stale-write 409, last-writer-wins, column reorder, concurrent creation
- Proposal approval races (4): double-approve, approve+expire, approve+reject, double-execute
- Webhook delivery concurrency (2), board presence (2), rate limiting (3), cross-user isolation (2)
- Uses `SemaphoreSlim` barriers for true simultaneous execution; SQLite serialization limitations documented

Running:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Concurrency"
```

### Frontend Store Integration Tests (`#711`/`#821`)

88 frontend tests across 6 files in `frontend/taskdeck-web/src/tests/store/`:
- chatApi integration (22), boardStore column reorder/conflict (11), queueStore polling (12)
- sessionStore OIDC/SSO (14), notificationStore realtime (15), workspaceStore mode persistence (14)
- Mocks HTTP layer (not API modules) to test full store → API → HTTP chain

### E2E Scenario Expansion (`#712`/`#822`)

20 Playwright scenarios across 5 spec files:
- `onboarding.spec.ts` (5): fresh user empty states, setup dialog, starter pack structure
- `review-proposals.spec.ts` (3): board-scoped filtering, multiple proposals, show completed toggle
- `capture-edge-cases.spec.ts` (4): empty/whitespace rejection, Escape dismiss, board-linked capture
- `keyboard-navigation.spec.ts` (4): keyboard board creation, command palette arrows, `?` help toggle
- `dark-mode.spec.ts` (4): persistence across views, toggle-off restore, system `prefers-color-scheme`

### Frontend View and Component Coverage (`#716`/`#826`)

107 tests across 8 files covering previously untested views and components:
- ArchiveView (11), MetricsView (16), BoardView (12), ReviewView (10)
- AutomationChatView (16), CardItem (21), BoardCanvas (12), BoardActionRail (9)

### Property-Based and Adversarial Input Tests (`#717`/`#824`)

162 tests across 8 files:
- Domain property tests (93): ChatSession, ChatMessage, Notification, KnowledgeDocument, WebhookSubscription
- Application fuzz tests (19): JSON round-trip for chat/notification DTOs with adversarial content
- API adversarial tests (50): raw JSON with float/overflow positions, XSS/injection payloads, unicode blocks, extra unknown fields

### Resilience and Degraded-Mode Tests (`#720`/`#823`)

30 tests across 3 files:
- LLM provider resilience (13): garbage/empty/429/timeout for OpenAI/Gemini, probe unhealthy
- Queue accumulation resilience (3): accumulation without corruption, rapid concurrent captures
- Frontend slow-API/storage resilience (14): loading states, throttle dedup, corrupted localStorage/token

## Post-Merge Batch Testing Notes (2026-04-12)

After batch-merging PRs `#800`, `#805`, `#811`, `#813`, `#815`, `#819`, `#820`, the following additional test categories are now on `main`:

### Resilience and Degraded-Mode Tests (`#720`/`#820`)

34 tests (18 backend + 16 frontend) covering:
- Backend: ChatService LLM provider failure/fallback, worker crash/retry/cancellation/max-retries
- Frontend: store error states, SignalR reconnect polling fallback

### MFA/OIDC Security Tests (`#82`/`#813`)

30+ backend tests covering TOTP validation, OIDC provider isolation, email collision prevention, username deduplication, MFA policy enforcement, and recovery code lifecycle.

Running MFA/OIDC tests:
```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Mfa"
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~Oidc"
```

### Telemetry and Analytics Tests (`#549`/`#811`)

63 tests (38 backend + 25 frontend):
- Backend: opt-in enforcement, event property validation against allowlist, value truncation, TelemetryController endpoints
- Frontend: consent management, DNT/GPC detection, store event buffering/flush, analytics script injection

### Distributed Cache Tests (`#85`/`#805`)

32 backend tests covering `ICacheService` implementations (InMemory sweep/cap, Redis reconnect/degradation, NoOp pass-through), board list cache-aside with TTL and write-through invalidation.

### OAuth Token Lifecycle Tests (`#723`/`#815`)

19+ integration tests covering DB-backed auth code store (valid exchange, expiry, replay prevention, concurrent atomicity, cleanup), JWT lifecycle (expiry, wrong key, garbage token, deactivated user), and SignalR query-string auth.

### MCP HTTP Transport Tests (`#654`/`#819`)

49 tests (11 domain + 38 integration) covering the API key entity (`tdsk_` prefix, SHA-256 hashing), real Streamable HTTP initialize/session/resource traffic at `/mcp`, missing/invalid/expired/revoked/valid Bearer keys, root-route exclusion, cross-user board isolation, correlation-matched telemetry, pre-authentication IP throttling, literal per-key partitioning, explicit no-CORS preflight behavior, all ASP.NET any-host forms, standalone loopback defaults, and REST key management.

Focused gate:

```powershell
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~McpHttpTransportApiKeyTests"
```

The standalone runtime proof also starts the normal API and `--mcp --transport http` against one throwaway SQLite database, creates a user/board/key through REST, then verifies `401 / 200 / 202 / 200` for missing-key, initialize, initialized notification, and `taskdeck://boards`, plus `404` at `/`. The security repair probe starts standalone with `AllowedHosts=localhost;*` and a one-request authentication window, proving hostile Host `400`, then missing-key `401`, repeated-attempt `429`, and root `404`. On Windows PowerShell 5.1, capture non-2xx status from `WebException.Response`; `Invoke-WebRequest -SkipHttpErrorCheck` is PowerShell 7-only.

## E2E Parallelization (TST-60, `#867`/`#949`)

Playwright E2E suite runs in parallel (`fullyParallel: true`) with 2-worker default for both local and CI runs. Worker count is overridable via `TASKDECK_E2E_WORKERS` environment variable.

Per-test isolation is built into the data layer: `registerUserSession` provisions a unique user per test with `Date.now() + random` suffixes; board/column/card names follow the same pattern; data is scoped server-side by authenticated user.

SQLite connection string uses `Pooling=True;Default Timeout=30` for E2E runs. `Cache=Shared` was intentionally removed (increases contention vs default private-cache mode). WAL mode (`PRAGMA journal_mode=WAL`) is documented as future work for genuine concurrent-read throughput.

Key test patterns for parallel safety:
- Use idempotent Playwright actions (`.check()` instead of `.click()`) when the dependent DOM element is `v-if`-gated on the control's reactive state
- UI tests touching controls whose sibling DOM is conditionally gated should wait for the dependent element, not the control itself

## Platform Expansion Testing Capabilities (2026-04-09)

The platform expansion wave (PRs `#796`–`#805`) delivered four new testing capabilities:

### Cross-Browser and Mobile E2E Matrix (TST-02, `#87`/`#800`)

Playwright config expanded with 5 projects: `chromium` (all tests), `firefox`/`webkit` (`@cross-browser` only), `mobile-chrome` Pixel 7/`mobile-safari` iPhone 14 (`@mobile` only). Global `@quarantine` tag exclusion.

Run commands:
```bash
cd frontend/taskdeck-web
npx playwright test --project=chromium               # PR gate (default)
npx playwright test --project=firefox                 # Firefox cross-browser
npx playwright test --grep @mobile                    # All mobile tests
npx playwright test                                   # Full matrix (nightly)
```

Tagging convention: `@smoke` (quick CI), `@cross-browser` (multi-browser), `@mobile` (viewport), `@quarantine` (flaky, excluded). See `docs/testing/FLAKY_TEST_POLICY.md`.

CI: `reusable-e2e-cross-browser.yml` in nightly + extended (testing label/manual). PR gate stays Chromium-only.

### Visual Regression Testing (TST-03, `#88`/`#797`)

Playwright `toHaveScreenshot()` with dedicated config: 1280x720 viewport, animations disabled, 0.5% pixel tolerance, light color scheme.

Run commands:
```bash
cd frontend/taskdeck-web
npx playwright test --config playwright.visual.config.ts              # Run visual tests
npx playwright test --config playwright.visual.config.ts --update-snapshots  # Update baselines
```

20 visual tests (expanded from 7 in TST-59, `#865`/`#948`): board (empty + populated), command palette (open + search), archive, inbox, home, login, register, today, calendar (clock-pinned), metrics, review, notifications, settings, card modal (timestamp-masked), column edit modal (timestamp-masked), board toolbar, capture/inbox views. Clock pinning via `page.clock.install()` for date-dependent views; `document.fonts.ready` wait for font-load determinism; dynamic content hidden via `data-testid="timestamp"` + `hideDynamicContent` helper. Policy at `docs/testing/VISUAL_REGRESSION_POLICY.md`.

CI: `reusable-visual-regression.yml` in extended CI (testing/visual label). Uploads diff artifacts on failure. Baseline bootstrap: CI detects missing `__screenshots__/` directory, runs `--update-snapshots`, and uploads the `visual-regression-baselines` artifact for committing in a follow-up PR.

### Mutation Testing (TST-05, `#90`/`#796`)

Backend (Stryker.NET): targets `Taskdeck.Domain` with `Taskdeck.Domain.Tests`. Thresholds: break=60, high=80.
Frontend (Stryker JS): targets `captureStore`, `boardStore`, and `board/*.ts` submodules with vitest runner.

Run commands:
```bash
# Backend
cd backend && dotnet tool install dotnet-stryker && dotnet stryker
# Frontend
cd frontend/taskdeck-web && npm run mutation:test
```

CI: `mutation-testing.yml` runs weekly (Sunday 04:00 UTC) + manual dispatch. Non-blocking, reports uploaded as artifacts. Policy at `docs/testing/MUTATION_TESTING_POLICY.md`.

### Container Integration Tests (TST-06, `#91`/`#804`)

`Taskdeck.Integration.Tests` uses `Testcontainers.PostgreSql` for ephemeral database isolation. Each PostgreSQL-backed test method gets a fresh database. Docker is required for positive PostgreSQL execution; without responsive Docker, the container cases skip before Testcontainers validation while Docker-independent fixture/native checks still run.

Run commands:
```bash
# Run all (Dockerless is green-with-skips, not PostgreSQL parity proof)
dotnet test backend/tests/Taskdeck.Integration.Tests -c Release
# Run alongside main suite (integration tests auto-skip without Docker)
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Current project count: 35 tests — 28 PostgreSQL-backed cases covering Board CRUD, Card operations, proposal lifecycle, cross-class isolation, parallel execution, and repository parity; plus 7 Docker-independent fixture/native checks. Guide at `docs/testing/TESTCONTAINERS_GUIDE.md`.

CI: `reusable-container-integration.yml` in extended CI (testing label). The hosted lane sets
`TASKDECK_REQUIRE_DOCKER=true`, first forces Docker unavailable as a negative control, and requires
the resulting TRX to contain the explicit Docker-required failure. Its real run then verifies the
TRX has zero skipped tests and at least 28 passing PostgreSQL-backed results. A normal local green
run with all 28 container cases skipped proves only graceful Dockerless gating; do not set the
required-mode flag for that local contract.

## Product-Coherence Testing Priorities (2026-03-07)

Testing priorities have shifted from "does the harness exist?" toward "does the product remain understandable under change?"

Near-horizon priorities:

- protect the current golden path: capture -> triage -> review -> execute -> board
- keep the deterministic first-run Playwright guardrail aligned to the shipped `Home -> capture -> review -> execute -> board` loop (`#328`, delivered)
- add explicit coverage for action-oriented empty states and board-centered context travel as those surfaces land
- keep stakeholder/demo recording opt-in; it supports product evidence, but it is not the primary product smoke

High-signal additions and delivered guardrails:

- `Home` view state coverage
- `Today` view state coverage
- workspace mode navigation rendering
- proposal summary card coverage
- board action rail coverage
- first-run golden-path Playwright smoke coverage, now delivered as the required regression guardrail in `#328`

Telemetry and release-gate follow-through from the expanded blueprint:

- product telemetry/event taxonomy documented in `#341`/`#741` — see `docs/product/TELEMETRY_TAXONOMY.md` (taxonomy spec, not shipped instrumentation); reuses `#77` as baseline; `#328` provides the delivered first-run guardrail
- keep event names privacy-safe and product-shaped using the canonical `noun.verb` format from `docs/product/TELEMETRY_TAXONOMY.md` (for example `capture.modal_opened`, `capture.submitted`, `proposal.approved`, `proposal.rejected`, `card.created`, `board.loaded`, `auth_session.started`, `agent_run.completed`, `agent_run.failed`)
- treat launch framing as evidence gates, not marketing labels:
  - `R1` novice-first beta -> coherent `Home -> capture -> review -> execute -> board` path
  - `R2` agent foundation alpha -> inspectable runs, policies, and bounded templates
  - `R3` knowledge/integrations alpha -> durable searchable context plus supervised connector flows

## Codex Coverage Wave (TST-CODEX-01 to TST-CODEX-15, delivered 2026-03-28)

A dedicated test-coverage wave designed for token-efficient agents (Codex, lightweight LLM runners). Each task is self-contained with pattern files, source paths, and verify commands in `docs/codex-tasks/`.

Tracked issues: `#415` to `#429`. PRs: `#436` to `#448`. All delivered and merged 2026-03-28 after adversarial review pass with fixes for tautological assertions, missing guard branches, and edge-case gaps.

| Tier | Tasks | Scope | Issues |
|------|-------|-------|--------|
| 1 — Frontend API | labelsApi, columnsApi, usersApi | Mock HTTP, verify URL/payload | `#415`-`#417` |
| 2 — Frontend Composables | useErrorMapper, useEscapeToClose, useShortcutContext | Pure function + lifecycle tests | `#418`-`#420` |
| 3 — Frontend Stores | auditStore, queueStore (real coverage, not demo) | Pinia store with mocked API | `#421`-`#422` |
| 4 — Backend Domain | CardComment, Notification, AutomationProposal, LlmUsageRecord | Entity construction + invariants | `#423`-`#426` |
| 5 — Backend Services | OutboundWebhookSignature (expand), WorkerHeartbeatRegistry, CompositeBoardRealtimeNotifier | Service tests with mocking | `#427`-`#429` |

Remaining coverage gaps (post-wave, now tracked in TST-32 to TST-57 wave `#721`):
- Frontend: 1 API module untested (captureApi), remaining composables/stores have baseline coverage → tracked in `#711`, `#716`
- Backend: Infrastructure repositories partially covered (7 classes, 77 tests in `#699`/`#730`; remaining repos untested); remaining domain entities untested → tracked in `#701`; 1 of 5 workers untested → tracked in `#700`

## LLM Tool-Calling Coverage (PR #669, delivered 2026-04-01)

Tracking issue: `#649` (Phase 1 of `#647`)

New test coverage:
- `ToolCallingChatOrchestratorTests`: multi-turn loop, timeout, max-round enforcement
- `ReadToolSchemasTests`: schema generation for all 5 read tools
- `MockLlmProviderToolCallingTests` / `MockToolCallDispatcherTests` / `MockToolResultsTests`: mock provider tool-calling dispatch and result formatting
- `OpenAiToolCallingParseTests` / `GeminiToolCallingParseTests`: provider-specific tool-call response parsing

Manual validation recommended: send "What cards are in my Backlog?" via chat with Mock provider and verify dynamic tool-calling response.

## MCP Server Coverage (PR #664, delivered 2026-04-01)

Tracking issue: `#652` (Phase 1 of `#648`)

New test coverage:
- `McpBoardResourcesTests`: `taskdeck://boards` resource listing, phantom-user fallback, multi-user board scoping

Manual validation recommended: configure `mcp.example.json` in Claude Code / Cursor and ask "What boards do I have?" to verify resource delivery.

## GDPR Data Portability Coverage (PR #666, delivered 2026-04-01)

Tracking issue: `#83`

New test coverage:
- `DataExportServiceTests` (10 tests): user-scoped data export completeness, versioned payload shape, cross-user isolation
- `AccountDeletionServiceTests` (15 tests): password re-auth, confirmation phrase enforcement, PII anonymization, audit ref cleanup, deactivated-user login rejection

## Board Metrics Coverage (PR #667, delivered 2026-04-01)

Tracking issue: `#77`

New test coverage:
- `BoardMetricsServiceTests` (12 backend tests): board-scoped metric aggregation, date range filtering, label grouping
- `metricsApi.spec.ts` (4 frontend tests): API client mock verification

## GitHub OAuth Frontend Coverage (PR #668, delivered 2026-04-01)

Tracking issue: `#539`

New test coverage:
- `authApi.spec.ts` (3 tests): `getProviders` and `exchangeOAuthCode` API calls
- `sessionStore.spec.ts` (2 tests): OAuth code exchange store action

## Rigorous Test Expansion Wave (TST-32 to TST-57, seeded 2026-04-03)

Tracker issue: `#721`. Seeded from a systematic codebase audit across backend, frontend, and cross-cutting integration boundaries.

Security finding during audit: `#722` (SEC-20) — `ChangePassword` endpoint does not verify caller identity. **RESOLVED** in `#732` (2026-04-04).

### Wave Scope

22 issues spanning integration tests, edge cases, adversarial inputs, failure modes, and cross-user data isolation. Focus is on integration seams (where services interact) rather than adding more isolated unit tests.

| Priority | Issues | Theme | Status |
|----------|--------|-------|--------|
| I | ~~`#703`~~ | Capture → triage → proposal → review → board end-to-end golden path | **Delivered** (`#735`) |
| II | ~~`#699`~~, ~~`#700`~~, ~~`#702`~~, ~~`#704`~~, ~~`#705`~~, ~~`#707`~~, ~~`#723`~~, ~~`#725`~~ | Infrastructure repos, worker, controller gaps, data isolation, concurrency, auth, OAuth, frontend HTTP interceptor | **8 of 8 delivered** |
| III | ~~`#701`~~, ~~`#706`~~, ~~`#708`~~, ~~`#709`~~, ~~`#710`~~, ~~`#711`~~, ~~`#712`~~, ~~`#713`~~, ~~`#714`~~, ~~`#715`~~, ~~`#716`~~, ~~`#718`~~, ~~`#719`~~, ~~`#720`~~, ~~`#726`~~ | Domain state machines, SignalR, proposal lifecycle, LLM tool-calling, webhooks, frontend stores/views, export/import, error contracts, archive, metrics, notifications, resilience | **15 of 15 delivered** |
| IV | ~~`#717`~~ | Property-based and adversarial input tests (extends `#89`) | **Delivered** (`#789`) |

**Wave progress**: 25 of 25 issues delivered (plus SEC-20 fix). ~1350+ new tests across six delivery waves. **Wave complete.** Final deliveries: concurrency stress tests (`#705`/`#793` — 13 tests), property-based adversarial tests (`#717`/`#789` — 211 tests).

### Key Gaps Identified (updated 2026-04-04)

- ~~**Infrastructure repositories**~~: 7 classes now have 77 integration tests (`#699`/`#730`); remaining repositories still untested
- ~~**`LlmQueueToProposalWorker`**~~: **RESOLVED** — 24 integration tests delivered (`#700`/`#734`) covering happy path, error/retry, cancellation, fair-batch, and capture triage paths
- ~~**Cross-user data isolation**~~: **RESOLVED** — 38 integration tests delivered (`#704`/`#733`) covering all major API boundaries; 3 false-positive tests caught and fixed in adversarial review
- ~~**Frontend HTTP interceptor and router auth guard**~~: **RESOLVED** — 33 tests delivered (`#725`/`#765`): 19 HTTP interceptor tests + 14 router integration tests
- ~~**Golden path**~~: **RESOLVED** — 7 integration tests delivered (`#703`/`#735`) proving full capture → triage → proposal → review → board pipeline
- ~~**Domain entity state machines**~~: **RESOLVED** — 174 exhaustive tests delivered (`#701`/`#740`) covering CommandRun, ArchiveItem, ChatSession, UserPreference, NotificationPreference, CardLabel, CardCommentMention
- ~~**SignalR hub integration**~~: **RESOLVED** — 19 integration tests delivered (`#706`/`#751`) covering auth, presence, multi-user, authorization, and edge cases
- ~~**LLM tool-calling edge cases**~~: **RESOLVED** — 101 tests delivered (`#709`/`#747`) for orchestrator, provider abstraction, intent classifier, and tool executor registry
- ~~**Export/import integrity**~~: **RESOLVED** — 64 round-trip tests delivered (`#713`/`#752`) covering JSON, CSV, GDPR, database, and cross-format validation
- ~~**API error contract regression**~~: **RESOLVED** — 57 tests delivered (`#714`/`#753`) verifying GP-03 error contract across 7 endpoint families
- ~~**Archive lifecycle**~~: **RESOLVED** — 74 tests delivered (`#715`/`#755`): 45 domain state machine + 29 API integration covering cross-user isolation, conflict detection, audit trail
- ~~**Board metrics accuracy**~~: **RESOLVED** — 61 tests delivered (`#718`/`#749`): 51 service + 10 controller covering throughput, cycle time, WIP, blocked cards, done-column heuristic
- ~~**Notification delivery**~~: **RESOLVED** — 36 tests delivered (`#719`/`#746`) covering all 5 types, deduplication, preference filtering, cross-user isolation, batch operations
- ~~**Webhook HMAC signature verification**~~: **RESOLVED** — 11 tests delivered (`#726`/`#750`) covering header format, HMAC round-trip, wrong-key rejection, secret rotation, timing-safe comparison
- ~~**Webhook delivery reliability and SSRF**~~: **RESOLVED** — 78 webhook tests across 9 files delivered (`#710`/`#756`) covering retry/backoff, dead-letter, SSRF boundary conditions (private IPv4/IPv6 ranges via `OutboundWebhookEndpointGuardTests`)

## Mutation Testing Pilot (TST-05, `#90`)

Mutation testing is available as a non-blocking quality signal for detecting weak assertions and test gaps.

### Scope

- **Backend**: Stryker.NET targeting `Taskdeck.Domain` (entity state machines, validation, business rules)
- **Frontend**: Stryker JS targeting `captureStore.ts`, `boardStore.ts`, and `board/*.ts` submodules (core data flow stores)

### Running locally

```bash
# Backend (requires dotnet-stryker global tool)
cd backend
dotnet stryker --config-file stryker-config.json

# Frontend
cd frontend/taskdeck-web
npm run mutation:test
```

### CI

Weekly workflow (Sunday 04:00 UTC) + manual dispatch via `.github/workflows/mutation-testing.yml`. Reports uploaded as artifacts.

### Policy and triage

See `docs/testing/MUTATION_TESTING_POLICY.md` for threshold strategy, report interpretation, and follow-up process.

### Relationship to Existing Test Issues

- Extends `#254` (testing harness improvement wave, delivered)
- Extends `#89` (property/fuzz pilot, delivered)
- Complements `#90` (mutation testing pilot)
- Complements `#91` (Testcontainers for isolation)
- Feeds into `#135` (integrated multi-component verification program) — **delivered** as `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`

### Delivered: Infrastructure Repository Integration Tests (`#699`/`#730`)

First delivery from the rigorous test expansion wave. 77 integration tests across 7 repository classes running against real SQLite (not mocks or in-memory substitutes).

Pattern:
- Each test class creates a fresh SQLite database via `DbContextOptionsBuilder<TaskdeckDbContext>` with a unique filename
- Tests exercise actual EF Core queries, GUID formatting, ordering, pagination, and filtering against real SQLite behavior
- Database is cleaned up after each test run

Key findings:
- Found and fixed a real `LlmQueueRepository` ordering bug where queue items were not returned in the expected FIFO order
- Confirmed correct behavior for raw SQL queries, in-memory pagination edge cases, and GUID string formatting across repositories

Coverage:
- 7 repository classes tested (including `LlmQueueRepository`, `BoardRepository`, `CardRepository`, and others)
- Tests validate query correctness, cross-user isolation, empty-result handling, and ordering guarantees

This establishes the pattern for testing remaining infrastructure repositories tracked in the wave (`#721`).

### Delivered: SEC-20 ChangePassword Identity Bypass Fix (`#722`/`#732`)

Security fix: `ChangePassword` endpoint now derives userId exclusively from JWT claims instead of accepting client-supplied `UserId`. 5 new integration tests (unauthenticated 401, own-account success, wrong password, cross-user body-UserId ignored, invalid token).

### Delivered: Golden-Path Integration Test (`#703`/`#735`)

7 integration tests exercising the full capture → triage → proposal → review → board pipeline against real SQLite with Mock LLM provider:
- Happy path: single capture → proposal → approve → card on board with correct title and column placement
- Multi-operation: 3 checklist items → proposal with 3 operations → 3 cards created atomically
- Rejection: proposal rejected → board remains empty
- Cross-user isolation: User B cannot read/approve/execute User A's proposal
- Audit trail: card creation via proposal recorded in board audit log
- Provenance integrity: full backward-traceable chain (capture → proposal → card) at DB level
- Triage failure: capture without board fails deterministically

### Delivered: Cross-User Data Isolation Tests (`#704`/`#733`)

38 integration tests proving cross-user isolation across all major API boundaries:
- Boards, columns, cards, captures, proposals, notifications, audit trails, chat sessions, knowledge docs, webhooks, board exports, labels, board access controls
- 3 shared-board tests (grant, scope limitation, revocation)
- Adversarial review caught 3 false-positive tests (LlmQueue never seeded, notifications never created, mark-notification used fabricated GUID) and missing precondition assertions

### Delivered: LlmQueueToProposalWorker Integration Tests (`#700`/`#734`)

24 tests for the central background worker (previously zero coverage):
- Happy path, empty queue, transient error retry, max-retry boundary, permanent failure
- Unhandled exceptions, already-claimed items, capture triage paths, disabled processing
- Graceful cancellation, `BuildFairBatchItems` logic, retry backoff, multi-item batch
- Adversarial review fixed: fake repository ignoring status transitions, misleading race-condition test, weak interleaving assertions, premature ServiceProvider disposal

### Delivered: Controller HTTP Integration Tests (`#702`/`#738`)

67 tests covering 6 previously-untested controllers + 17 new authz regression matrix entries:
- DataPortabilityApiTests (8), AbuseContainmentApiTests (12), MetricsApiTests (7), SearchApiTests (6), AgentProfilesApiTests (10), AgentRunsApiTests (7)
- Discovered 2 pre-existing bugs: `GET /api/agents` and `GET /api/agents/{id}/runs` return 500
- Adversarial review fixed: weak `NotBe(OK)` assertions, resource leak, leaked file from another branch

### Delivered: Proposal Lifecycle Edge Cases (`#708`/`#736`)

74 tests across domain (42), application (25), and api (7) layers:
- Expiry timing boundaries, double-apply/fail prevention, comprehensive state machine violations
- Batch expiry, worker-vs-manual-approval race, dismissal edge cases, operation mutation guards
- Adversarial review fixed: clock-resolution flakiness (`AddMilliseconds` → `AddSeconds`), string-based Theory refactoring risk, aggressive cancellation timeout; added 5 new edge case tests

### Delivered: OAuth/Auth Edge Case Tests (`#707`/`#737`)

44 tests across service (31) and controller (13) layers:
- Login edge cases (blank creds, inactive user, wrong password, concurrent JWT uniqueness)
- Registration edge cases (duplicate email, invalid lengths)
- Token validation (malformed, wrong key, expired, future nbf, wrong issuer/audience, missing sub, deleted/inactive user)
- OAuth code exchange (empty, invalid, replay, expired), open redirect prevention
- **Production bug found and fixed**: `ExternalLoginAsync` `Substring(0, 50)` overflow for short usernames

### Delivered: MCP Full Resource and Tool Inventory (`#653`/`#739`)

42 MCP-specific tests for the full inventory:
- 9 resources under `taskdeck://` URI scheme
- 11 tools (2 read + 6 write + 3 proposal management)
- GP-06 compliance verified: all write tools produce proposals, `approve_proposal` excluded
- **User-scoping gap found and fixed in adversarial review**: proposal resources/tools were not checking `RequestedByUserId`

### Delivered: Domain Entity State Machine Exhaustive Tests (`#701`/`#740`)

174 tests across 7 entity test classes:
- **CommandRun** (68 tests): all 6 states × 5 transitions (valid + invalid), constructor validation, `SetOutputPreview` boundary (1000 chars), `SetTruncated` idempotency, `AddLog`, Touch verification
- **ArchiveItem** (41 tests): all 4 states × 4 transitions, constructor validation (entityType, name length 200, Guid.Empty, empty snapshot), round-trip flows
- **ChatSession** (22 tests): Active/Archived lifecycle, AddMessage blocked on archived, UpdateTitle validation
- **UserPreference** (18 tests): DismissOnboarding/ReplayOnboarding, RecordOnboardingCompletion once-only guard, UpdateWorkspaceMode
- **NotificationPreference** (7 tests): constructor validation, Update permutations
- **CardLabel** (4 tests): join entity construction
- **CardCommentMention** (6 tests): constructor validation, username length boundary (50 chars)
- Two rounds of adversarial review fixed misleading test name and leftover unused variable

### Delivered: SignalR Hub and Realtime Integration Tests (`#706`/`#751`)

19 integration tests using WebApplicationFactory with SignalR test client:
- Authentication (3): unauthenticated rejection, valid/invalid token
- Presence lifecycle (5): join broadcast, set/clear editing, leave cleanup, abrupt disconnect
- Multi-user (2): multiple users see all members, same-user two-connection aggregation
- Authorization (3): join/leave/editing without board access rejected
- Edge cases (6): board switching, two-tab disconnect, non-existent board, Guid.Empty, timestamps, cross-board isolation
- Adversarial review fixed false-positive auth tests (bare Exception → HttpRequestException+401), silent timeout, resource leak, missing status assertions

### Delivered: LLM Provider and Tool-Calling Edge Cases (`#709`/`#747`)

101 tests across 4 test classes:
- **ToolCallingChatOrchestratorEdgeCaseTests** (18): per-round timeout, empty tool calls, concurrent calls, cancellation, metadata, token accumulation, loop detection (added in review)
- **LlmProviderAbstractionEdgeCaseTests** (24): default CompleteWithToolsAsync throws, MockLlmProvider edge cases, provider selection, kill switch
- **LlmIntentClassifierEdgeCaseTests** (49): negation filtering, other-tool questions, positive intent, non-actionable, prompt injection, disambiguation, plurals, alternate verbs
- **ToolExecutorRegistryEdgeCaseTests** (10): empty registry, case-insensitive lookup, duplicate/null registration (added in review)
- Adversarial review fixed false-positive prompt injection test, replaced 30-second slow test, added loop detection and registry edge cases

### Delivered: Data Export/Import Round-Trip Integrity Tests (`#713`/`#752`)

64 tests across 5 test files:
- **BoardJsonExportImport** (23): full round-trip, special characters, 100-card scale, empty boards, WIP limits, cross-user isolation, corrupt JSON, duplicate labels
- **CsvImport** (23): RFC 4180 edge cases, BOM, CRLF, deduplication, 1000-row scale, missing fields, invalid dates
- **GdprDataExport** (9): valid parseable JSON, empty user, field preservation, cross-user isolation, version/timestamp
- **DatabaseExportImport** (21): byte-level round-trip, corrupted/truncated rejection, SQLite signature validation, oversized payload
- **CrossFormatImport** (11): format mismatch detection, binary garbage, wrong JSON shapes
- Adversarial review fixed weak DueDate assertion, brittle JSON substring checks, non-deterministic test branching

### Delivered: API Error Contract Regression Tests (`#714`/`#753`)

57 tests across 7 test files in `ErrorContract/` namespace:
- **Board** (9), **Card** (10), **Column** (11), **Capture** (8), **Proposal** (7), **Label** (4), **ContentType/Format** (7)
- All error assertions through `ApiTestHarness.AssertErrorContractAsync` validating GP-03 `{errorCode, message}` shape
- Adversarial review fixed 12 weak 404 assertions missing errorCode, 2 false-positive GP-03 tests, non-deterministic unauthenticated test, misleading test name

### Delivered: Archive and Restore Lifecycle Tests (`#715`/`#755`)

74 tests across domain (45) and API integration (29):
- **Domain** (45): all valid/invalid ArchiveItem transitions, full lifecycle sequences, Touch timestamp updates, constructor validation boundaries
- **API** (29): board/card/column archive-restore cycles, cross-user isolation (3 tests), double-archive/restore handling (409), conflict detection (Rename/Fail strategies), snapshot integrity, audit trail, restore to non-existent/archived boards, filter by type/status/board, auth enforcement
- Adversarial review fixed 2 false-positive tests missing key assertions, 1 missing position check, 2 weak assertions pinned to specific status codes

### Delivered: Board Metrics Accuracy Verification Tests (`#718`/`#749`)

61 tests across service (51) and controller (10):
- Done column detection (14): named patterns, case-insensitivity, positional fallback, multiple done-like columns
- Throughput (6): card counting, bounce, same-day grouping, non-done exclusion
- Cycle time (8): exact calculation, multi-column paths, averages, in-progress exclusion, zero cycle time
- WIP (4): per-column counts, position ordering, WIP limits
- Blocked cards (5): sort by duration, reasons, unblocked exclusion
- Controller (10): from-after-to validation, label filter, response structure, date range handling
- Adversarial review fixed misleading test name, vacuous sort assertion, silent reflection failure, naming convention

### Delivered: Notification Delivery Integration Tests (`#719`/`#746`)

36 integration tests:
- Delivery (5): all 5 notification types (Mention, Assignment, ProposalOutcome, BoardChange, System)
- Deduplication (4): same-key rejection, different-key allowance, no-key duplicates
- Preference filtering (6): type-level enable/disable, in-app channel kill switch, digest-only, BoardChange always-on
- Cross-user isolation (2): notifications scoped to owner, mark-all-read scoped
- Mark as read (4): basic, idempotent, 404, cross-user forbidden
- Batch (3): count returned, board-scoped, zero unread
- Pagination (4): limit enforcement, unread/board filters, invalid limit
- Auth (5): all endpoints reject unauthenticated
- Adversarial review fixed PascalCase typo, 4 weak assertions tightened, overly generous performance threshold
- Production observation noted: `NotificationRepository.GetByUserIdAsync` materializes all rows before in-memory pagination (tracked separately)

## Backend Commands

Run full backend verification (recommended):

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run project-split backend verification:

```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release
```

### Concurrent-card HTTP 500 diagnostic lane (#1512)

The concurrent-card regression remains strict: every response must be `201 Created`. A non-201
response is a failure, never a retry, quarantine, or accepted result. When it fails, the test-only
diagnostic may report only bounded request/response correlation IDs, HTTP status, API error code,
outer/last-inspected exception type, an explicit classification-truncated flag, and SQLite
primary/extended numeric codes; it must not include bodies, credentials, user content, exception
messages, exception summaries, or exception objects. The bounded graph walk covers aggregate branches
without presenting a capped wrapper as the root. Run each repetition in a fresh
`dotnet test` process so host/database state is not reused. A green repetition proves only that the
failure did not occur; it is not root-cause evidence.

```powershell
$exact = "FullyQualifiedName=Taskdeck.Api.Tests.Concurrency.CardUpdateConflictTests.ConcurrentCardCreation_SameColumn_AllCreatedNoDuplicates"

$matrix = "FullyQualifiedName=Taskdeck.Api.Tests.ConcurrencyRaceConditionStressTests.BoardCreation_ConcurrentMultiUser_NoCrossContamination|FullyQualifiedName=Taskdeck.Api.Tests.Resilience.QueueAccumulationResilienceTests.RapidCaptureSubmission_DoesNotCorruptQueue|FullyQualifiedName=Taskdeck.Api.Tests.Concurrency.ProcessNextClaimRaceTests.ProcessNext_TenParallelWorkers_NoErrorsUnderConcurrentAccess|FullyQualifiedName=Taskdeck.Api.Tests.Concurrency.WebhookDeliveryConcurrencyTests.ConcurrentBoardMutations_EachCreatesDeliveryRecord|$exact"

1..20 | ForEach-Object {
    dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter $exact
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter $matrix
```

The `#1512` diagnostic-only checkpoint passed focused diagnostics 6/6, the exact race 20/20 in
fresh processes, the five-case historical/current concurrency matrix 5/5, and five CI-equivalent
full API runs (the final two were 2,130 passed / 4 skipped / 0 failed). No spontaneous 500,
causal exception, or SQLite/`SQLITE_BUSY` classification was captured, so the root cause remains open.

Note:
- If `Debug` runs fail with file-lock errors, stop running `Taskdeck.Api` processes or use `-c Release`.
- If backend tests unexpectedly bind to a live LLM provider in local Development, force deterministic mock mode before running the suite:
  - PowerShell: `$env:Llm__EnableLiveProviders='false'; $env:Llm__AllowLiveProvidersInDevelopment='false'; $env:Llm__Provider='Mock'; dotnet test backend/Taskdeck.sln -c Release -m:1`

## Container Integration Tests (Testcontainers)

Run the integration project. Docker is optional for the graceful-skip gate but required for positive PostgreSQL parity evidence:

```bash
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release
```

Run a specific test class:

```bash
dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~BoardCrudIntegrationTests"
```

Note:
- Without responsive Docker, the expected result at #1518 is 7 passed / 28 skipped / 0 failed. The availability probe is bounded and terminates/reaps a timed-out `docker info` process.
- With Docker running, require all 35 tests to pass with zero skips; 28 of those tests exercise PostgreSQL. A green Dockerless run is not PostgreSQL parity evidence.
- When positive PostgreSQL proof is intended, verify Docker first with `docker info`.
- First run downloads the `postgres:16-alpine` image (~80MB); subsequent runs use the cached image.
- Tests are parallel-safe: each test class gets its own isolated database within a shared PostgreSQL container.
- See `docs/testing/TESTCONTAINERS_GUIDE.md` for full setup and authoring guide.

## Frontend Unit + Build

```bash
cd frontend/taskdeck-web
npm run lint
npm run test:coverage
npm run typecheck
npm run build
```

Frontend spec type-checking (`#1468`, ADR-0049, delivered 2026-08-07):
- `npm run typecheck` is `vue-tsc -b`, which builds every project referenced from
  `frontend/taskdeck-web/tsconfig.json`. `tsconfig.app.json` covers production source and still
  excludes `src/tests/**`; `tsconfig.vitest.json` covers the spec tree. No CI workflow change was
  needed — the existing `Run frontend typecheck` step picks the new project up.
- Before this, **nothing type-checked a spec**: `vue-tsc` skipped them and vitest transpiles without
  checking (`--typecheck` is opt-in and applies to `*.test-d.ts`). A spec could reference a property
  that does not exist and every gate stayed green. `#1462` hit exactly that.
- `tsconfig.vitest.json` mirrors `tsconfig.app.json`'s compiler options exactly. Do **not** add
  `"node"` to its `types`: it clears 13 `TS2591` in the quarantined specs (3 bare `process`, 10
  `node:` module imports) and
  breaks production source pulled in as a dependency. Measured, exactly one error —
  `PaperHomeView.vue(238,5): TS2322: Type 'number' is not assignable to type 'Timeout'`, because
  `greetingTimer` is annotated `ReturnType<typeof window.setInterval>`, which resolves to node's
  `Timeout` while the call still returns the DOM `number`. `"vitest/globals"` is likewise
  unnecessary; 283 of the 284 specs import their vitest symbols explicitly.
- Its `include` carries `src/**/*.d.ts` on purpose. `src/types/web-speech.d.ts` is *ambient* —
  global scope, imported by nothing — so a `src/tests/**`-only include drops it, and production
  source pulled in as a dependency then compiles without those globals. Without that line,
  un-quarantining `composables/useVoiceCapture.spec.ts` reports 3 errors in untouched production
  source and masks 2 of the spec's own by making two `@ts-expect-error` directives spuriously
  "used". Keep the line, and add any new ambient declaration under `src/`, not elsewhere.
- Its `exclude` array is a **quarantine**, not configuration. It listed the 64 files carrying the
  415 pre-existing errors measured 2026-08-07, over the 286 `.ts` files under `src/tests/` (284
  specs plus `setup.ts` and a mock). **New spec files are checked by default** because they are not
  in the list. The list may only shrink — delete an entry once its file is fixed, never add one to
  turn a red build green. Burn-down is tracked in `#1607`.
- **Scope caveat — do not read this as "the test suite is type-checked".** A full Vitest run
  executes **302** spec files: 284 under `src/tests/` and 18 under the frontend-root `tests/`
  directory. This project gates **220** of them (284 − 64). The **82** it does not gate are those
  64 plus those 18. (Do not subtract 222 from 302 — 222 counts *files in the project*, including
  `setup.ts` and a mock that are not specs.) The 18 are Node-flavoured — they import the `.mjs`
  files under `scripts/` and use `process`/`NodeJS` — so they need a Node type environment and
  therefore a fourth, separate project; putting them here would require the one setting that breaks
  production source. Measured with this project's options: 54 errors in the run — 42 across 15 of
  the 18 specs, plus 12 in three `playwright.*.ts` helpers pulled in as dependencies. Also
  tracked in `#1607`.
- Type-level assertions are now available in ordinary specs: `expectTypeOf` erases at runtime, so
  the assertion is discharged by `vue-tsc -b` rather than by the vitest run. See
  `src/tests/api/automationApi.spec.ts` for the worked example (it pins
  `Proposal.approvedRevisionId`, which its runtime tests structurally could not).

Frontend lint suppression guidance:
- Prefer fixing lint violations over suppressing them.
- Keep suppressions as narrow as possible (`eslint-disable-next-line` with reason).
- Avoid file-wide disables unless absolutely required and documented with a follow-up issue.

Frontend coverage threshold policy:
- Coverage thresholds are enforced via `frontend/taskdeck-web/vitest.config.ts` and are part of the required CI gate.
- Global thresholds protect against broad regressions; per-surface thresholds protect high-signal areas (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`).
- Ratchet rule: thresholds may stay flat or increase, but must not decrease.
- Threshold breach behavior can be validated locally with an override command, for example:
  - `cd frontend/taskdeck-web && npx vitest run --coverage --coverage.thresholds.lines=99 --coverage.thresholds.statements=99 --coverage.thresholds.functions=99 --coverage.thresholds.branches=99`

Frontend local dev server (manual workflows):

```bash
cd frontend/taskdeck-web
npm run dev
```

Notes:
- `npm run dev` now auto-resolves frontend port with fallback order `5173` -> `4173` -> `5001` when a port is restricted or unavailable.
- launcher now selects a bindable port first; occupied candidate ports (including existing Taskdeck listeners) are skipped for new Vite processes.
- launcher now applies strict-port startup semantics by default to avoid Vite auto-increment drift.
- explicit overrides remain supported (for example `npm run dev -- --host localhost --port 5001` or `TASKDECK_DEV_PORT=5001 npm run dev`).
- backend Development CORS defaults include localhost fallback ports (`4173`, `5001`) so login/API calls stay aligned when fallback startup is used.

## Frontend E2E

Install browser once:

```bash
cd frontend/taskdeck-web
npx playwright install chromium
```

Run E2E suite:

```bash
cd frontend/taskdeck-web
npx playwright test --reporter=line
```

Fallback (force an alternate frontend port):

PowerShell:

```powershell
cd frontend/taskdeck-web
$env:TASKDECK_E2E_FRONTEND_PORT='5001'
npx playwright test --reporter=line
```

Bash:

```bash
cd frontend/taskdeck-web
TASKDECK_E2E_FRONTEND_PORT=5001 npx playwright test --reporter=line
```

Optional E2E env overrides (Playwright config):
- `TASKDECK_E2E_FRONTEND_HOST` (default `localhost`)
- `TASKDECK_E2E_FRONTEND_PORT` (when unset, config auto-probes `5173`, then `4173`, then `5001`)
- `TASKDECK_E2E_FRONTEND_BASE_URL` (default `http://{host}:{port}`; must be `http://` with explicit port and no path/query/hash)
- `TASKDECK_E2E_API_BASE_URL` (default `http://localhost:5000/api`; must be `http://` with explicit port and API path)
- `TASKDECK_E2E_API_CORS_ORIGINS` (comma-separated additional origins merged with defaults: frontend origin plus `http://localhost:5174`; each value is passed to backend process as `Cors__DevelopmentAllowedOrigins__{index}`)
- `TASKDECK_E2E_REUSE_EXISTING_SERVER` (defaults to `true` locally and `false` in CI; full demo runs that inject live-provider backend overrides also switch reuse off by default so the intended backend process is actually launched; set `0` to force fresh backend/frontend startup or `1` to force reuse intentionally)

Override behavior notes:
- backend Playwright `webServer` readiness URL is derived from `TASKDECK_E2E_API_BASE_URL` as `{apiBaseUrl}/boards`
- backend Playwright process startup binds to the same API origin via `ASPNETCORE_URLS`
- backend Playwright startup now forces deterministic mock-provider mode by default; live-provider env is only injected for explicit demo runs (`TASKDECK_RUN_DEMO=1` / director path) when LLM steps are enabled

Troubleshooting note (Windows local environments):
- if Playwright startup fails with `listen EACCES` for the frontend port, keep `TASKDECK_E2E_FRONTEND_PORT` unset so auto-fallback can select the next bindable port.
- when auto-fallback is used, Playwright keeps runner/worker aligned by storing the first resolved fallback port in-process (`TASKDECK_E2E_RESOLVED_FRONTEND_PORT`) so worker-side config evaluation does not drift to a different fallback port after the frontend webServer starts.
- local reuse mode prefers identity-verified listeners; CI mode prefers bindable ports for first resolution.
- `TASKDECK_E2E_API_CORS_ORIGINS` is only needed for origins *beyond* the frontend origin (which is already included automatically by `resolveBackendCorsOrigins`). Setting `TASKDECK_E2E_FRONTEND_PORT` alone is sufficient -- the resulting frontend origin is added to the CORS allow-list automatically.
- investigation details and reproduction commands are documented in `docs/analysis/2026-02-25_frontend-gate-port-bind-and-cors-blockers.md`.

Run concurrency harness spec only:

```bash
cd frontend/taskdeck-web
npm run test:e2e:concurrency
```

Opt-in live-provider check (headed-friendly):

PowerShell:

```powershell
cd frontend/taskdeck-web
$env:TASKDECK_RUN_LIVE_LLM_TESTS='1'
npx playwright test tests/e2e/live-llm.spec.ts --headed --reporter=line
```

Headed manual-audit pack:

```powershell
cd frontend/taskdeck-web
npm run test:e2e:audit:headed
```

## Cross-Browser and Mobile E2E Testing

### Browser Projects

The Playwright config defines five projects:

| Project | Device Descriptor | When It Runs |
|---------|------------------|--------------|
| `chromium` | Desktop Chrome | Every PR (ci-required), nightly, manual |
| `firefox` | Desktop Firefox | Nightly, manual dispatch, `testing` label |
| `webkit` | Desktop Safari | Nightly, manual dispatch, `testing` label |
| `mobile-chrome` | Pixel 7 | Nightly, manual dispatch, `testing` label |
| `mobile-safari` | iPhone 14 | Nightly, manual dispatch, `testing` label |

### Test Tagging

Tests use tag annotations in their title strings to control which projects run them:

- **(no tag)** or `@smoke` — runs on chromium only (PR gate default)
- `@cross-browser` — runs on chromium, firefox, and webkit
- `@mobile` — runs on mobile-chrome and mobile-safari only
- `@quarantine` — excluded from all CI (see `docs/testing/FLAKY_TEST_POLICY.md`)

### Running Cross-Browser Tests Locally

Install all browsers (one-time):

```bash
cd frontend/taskdeck-web
npx playwright install --with-deps
```

Run a specific project:

```bash
npx playwright test --project=firefox --reporter=line
npx playwright test --project=mobile-safari --reporter=line
```

Run all projects:

```bash
npx playwright test --reporter=line
```

Run only cross-browser tagged tests across all desktop browsers:

```bash
npx playwright test --grep="@cross-browser" --reporter=line
```

Run only mobile tests:

```bash
npx playwright test --grep="@mobile" --reporter=line
```

### CI Configuration

- **PR gate** (`ci-required.yml`): calls `reusable-e2e-smoke.yml` which installs and runs chromium only. This keeps PR feedback fast (~12 min timeout).
- **Nightly** (`ci-nightly.yml`): calls `reusable-e2e-cross-browser.yml` which runs all 5 projects in a matrix with `fail-fast: false`.
- **Extended/manual** (`ci-extended.yml`): calls `reusable-e2e-cross-browser.yml` on `testing` label or manual dispatch.

### Writing New E2E Tests

1. **Default tests** (no tag): run on chromium in PR gate. Use for most new tests.
2. **Critical journeys** that must work cross-browser: add `@cross-browser` tag. These will also run on chromium in PR gate.
3. **Mobile-specific behavior** (viewport responsiveness, touch targets, overflow): add `@mobile` tag. These only run on mobile projects.
4. **Flaky or unstable tests**: add `@quarantine` tag and file an issue. See `docs/testing/FLAKY_TEST_POLICY.md`.

### Flaky Test Policy

See `docs/testing/FLAKY_TEST_POLICY.md` for the full quarantine/remediation process, SLA timelines, and prevention guidelines.

## Visual Regression Tests

Visual regression tests capture baseline screenshots of key UI surfaces and compare them against future renders to catch unintended layout changes.

**Policy document**: `docs/testing/VISUAL_REGRESSION_POLICY.md` (thresholds, false-positive mitigation, baseline management)

**Test location**: `frontend/taskdeck-web/tests/visual/`

**Config**: `frontend/taskdeck-web/playwright.visual.config.ts`

**Covered surfaces**: board view (empty + populated), command palette (open + search), archive view, inbox/capture view, home view

Run visual tests:

```bash
cd frontend/taskdeck-web
npm run test:visual
```

Update baselines after intentional UI changes:

```bash
cd frontend/taskdeck-web
npm run test:visual:update
```

Key settings: fixed viewport 1280x720, animations disabled, 0.5% pixel tolerance, platform-specific baselines (CI canonical platform: ubuntu-latest).

CI integration: runs in CI Extended pipeline with `testing` or `visual` PR labels. Diff artifacts uploaded on failure for review.

## Demo Tooling Policy

Default CI posture:

- Required Playwright regression lanes explicitly set `TASKDECK_RUN_DEMO=0`; the stakeholder recorder is never part of required CI.
- Load/concurrency Playwright coverage also keeps demo recording off by default so those lanes stay focused on product/runtime regressions.
- The deterministic demo regression command is `npm run demo:director:smoke`.
- Demo tooling remains supporting evidence for seeded workflows; it does not replace the required product smoke path.

Run the smoke path locally:

```bash
cd frontend/taskdeck-web
npm run demo:director:smoke
```

Policy notes:

- `demo:director:smoke` runs `engineering-sprint` with `--skip-llm`, zero autopilot turns, a fixed RNG seed, a stable artifact directory (`demo-artifacts/ci-smoke`), an isolated smoke DB (`taskdeck.demo.ci.db`), and fresh backend/frontend startup.
- when fresh-server mode cannot bind `http://localhost:5000/api`, the director automatically selects a free local API port; if explicit overrides still conflict, it prints a remediation hint for `TASKDECK_E2E_API_BASE_URL` / `TASKDECK_E2E_FRONTEND_PORT`.
- `ci-extended.yml` exposes a matching `demo-director-smoke` lane for explicit validation through `workflow_dispatch` or a PR labeled `automation` when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`.
- `npm run demo:seed` is expected to be rerun-safe on the canonical demo account: seeded captures, queue examples, chat evidence, comments, and Ops logs should be reused when present instead of multiplying on every local/manual regression run.
- `demo:director` validates its own options before Playwright passthrough; keep director flags before `--` and pass raw Playwright arguments only after `--`.
- Full stakeholder walkthrough recording remains manual/headed via `TASKDECK_RUN_DEMO=1`.
- opt-in live-provider chat verification is now separate from demo mode: use `TASKDECK_RUN_LIVE_LLM_TESTS=1` when you want a real-provider probe without running the full stakeholder demo flow.

## Saul-Facing Rehearsal Contract

Canonical operator contract:
- `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md`

Deterministic bootstrap for the Saul-facing story:

```bash
cd frontend/taskdeck-web
npm run demo:seed
npm run demo:run -- --clean --skip-llm client-onboarding
```

Deterministic artifact rehearsal bundle:

```bash
cd frontend/taskdeck-web
npm run demo:director -- --output-dir ./demo-artifacts/saul-rehearsal --e2e-db ./taskdeck.demo.saul.db --reset-e2e-db --fresh-servers --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul-rehearsal
```

Acceptance focus for this rehearsal:
- prove `Home -> Inbox/Capture -> Review -> Board`
- prove review-first trust language is visible without narration
- prove ACME onboarding capture becomes clean board work after explicit approval

## Load Harness (k6 + Playwright Concurrency)

Run local k6 board-heavy profile (backend API must be reachable at `K6_BASE_URL`):

```bash
docker run --rm --network host \
  --user "$(id -u):$(id -g)" \
  -e K6_BASE_URL=http://127.0.0.1:5000/api \
  -e K6_VUS=20 \
  -e K6_DURATION=90s \
  -e K6_USER_POOL=6 \
  -v "$PWD:/work" \
  -w /work \
  grafana/k6:0.49.0 \
  run tests/load/k6/board-heavy-load.js \
  --summary-export frontend/taskdeck-web/test-results/load/k6-summary.json
```

Notes:
- tune `K6_VUS`, `K6_DURATION`, and `K6_USER_POOL` per machine capacity.
- the default 20-VU SQLite profile warns when tagged board writes reach the measured 2000 ms p95 capacity and fails at 4500 ms; together with the 5000 ms global p99 gate these sit at ~1.5–1.7× the worst same-code nightly tail observed on shared runners (calibrated 2026-07-23 — evidence in #1445; the write-tail itself is tracked in #1446).
- aggregate p95/p99, board-read p95, error-rate, and check-rate thresholds remain hard gates.
- both reusable k6 workflows fail closed when the required summary is missing, empty, malformed, lacks any hard-gate metric, contains an out-of-domain direct/nested value, mixes conflicting flattened/nested metric evidence, or has threshold evidence that contradicts the corresponding strict numeric comparator (including equality boundaries); pinned k6 0.49's flattened breach flags are normalized before analysis, aggregate p95 must not exceed p99, and the `always()` artifact uploads still preserve available diagnostics.
- run `node --test scripts/ci/require-k6-summary.test.mjs` from the repository root for summary validation and workflow-wiring checks.
- run `node --test scripts/ci/check-k6-thresholds.test.mjs` from the repository root for the focused tagged-capacity analyzer checks.
- script thresholds fail on sustained latency/error budget breaches and emit actionable status/body diagnostics.

## Container Baseline Validation

```bash
TASKDECK_JWT_SECRET=local-test-secret TASKDECK_CONNECTORS_ENCRYPTION_KEY=local-test-key docker compose -f deploy/docker-compose.yml --profile baseline config
docker build -f deploy/docker/backend.Dockerfile -t taskdeck-api:local .
docker build --build-arg VITE_API_BASE_URL=/api -f deploy/docker/frontend.Dockerfile -t taskdeck-web:local .
```

Deployment script smoke path (PowerShell):

```powershell
powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1
powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1 -Port 8080  # if TASKDECK_PROXY_PORT differs, set -Port to match
powershell -File ./scripts/deploy/Stop-TaskdeckStack.ps1
```

Deployment hardening matrix automation (PowerShell):

```powershell
powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080
```

Hardening matrix pass/fail criteria:
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`

## Failure-Injection Drills

Repeatable failure-injection scenarios for deployment and MCP workflows:

```bash
bash scripts/drills/run-all-drills.sh        # local run
bash scripts/drills/run-all-drills.sh --ci    # CI-compatible with machine-readable output
```

Scenarios covered:
- Missing SQLite database at startup
- Locked SQLite database at startup
- Readiness-check timeout behavior
- MCP configuration validation / unknown-server handling
- Reverse-proxy misconfiguration regression

Drill documentation and recovery paths: `docs/ops/FAILURE_INJECTION_DRILLS.md`

## Terraform IaC Baseline Validation

Static validation (no cloud apply required):

```powershell
terraform fmt -check -recursive deploy/terraform/aws
powershell -File ./scripts/deploy/Test-TaskdeckTerraformBaseline.ps1
```

Real-environment drift check (requires environment-specific `terraform.tfvars`, backend config, and AWS credentials):

```powershell
powershell -File ./scripts/deploy/Invoke-TaskdeckTerraformDriftCheck.ps1 `
  -Environment staging `
  -VarFile deploy/terraform/aws/environments/staging/terraform.tfvars `
  -BackendConfigFile deploy/terraform/aws/environments/staging/backend.hcl `
  -RefreshOnly
```

Notes:
- `Test-TaskdeckTerraformBaseline.ps1` runs `terraform init -backend=false` and `terraform validate` for `dev`, `staging`, and `prod`.
- `Invoke-TaskdeckTerraformDriftCheck.ps1` relies on `terraform plan -detailed-exitcode`; `0` means no changes, `2` means drift for `-RefreshOnly` or planned changes for a non-refresh-only run, and any other exit is a failure.
- The Terraform baseline intentionally provisions the current single-node Docker deployment model; the JWT signing secret comes from a pre-created SecureString SSM parameter, and the SQLite path lives on a dedicated persistent EBS data volume so routine host replacement does not discard `/var/lib/taskdeck/taskdeck.db`.
- `staging` and `prod` default `protect_data_volume` to `true`; intentional destroys or migrations that must remove the data volume require a reviewed switch to the unprotected path plus a reviewed module-source change to relax/remove `prevent_destroy` before the destructive apply.
- Changing an existing environment from `protect_data_volume = false` to `true` also replaces the underlying EBS volume with a new protected one; treat that as a destructive migration and capture a backup or snapshot first.
- Staged rollout policy, managed DB, and full secret-rotation posture remain tracked in `#101`, `#84`, and `#110`.

## MCP Operations Validation

```powershell
docker mcp server ls
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1
```

Optional servers (`postman`, `dockerhub`) warning mode:

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional
```

Optional servers strict mode (fail-fast on missing prereqs/runtime failures):

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors
```

CI-friendly variants:

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -CiMode
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -SkipOptionalWhenMissingPrereqs -CiMode
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors -CiMode
```

## CI Gates

Required workflow: `.github/workflows/ci-required.yml`

- `dco`
  - Checks the explicit pull-request `base.sha..head.sha` range after a full-history checkout.
    `scripts/ci/check-dco-signoffs.sh` parses only native Git trailer blocks with
    `git -c core.commentChar=# interpret-trailers --parse`, checks every submitted commit including
    multi-parent merges, and requires a nonempty `Signed-off-by: Name <email>` identity matching the
    commit author or committer case-insensitively. The one repository-established Dependabot mapping
    (`dependabot[bot]` / GitHub's bot author email to `support@github.com`) remains trailer-required
    and is not a general bot exemption. Missing objects and Git/range/parser errors fail closed.
  - The SHA-pinned `KineticCafe/actions-dco` action remains visible as a step-level non-blocking
    diagnostic. The repository verifier determines the job step result, while the job itself remains
    **advisory** (`continue-on-error: true`); promotion into branch protection remains maintainer-owned
    under #1173.
  - Local contract: `bash scripts/ci/test-check-dco-signoffs.sh`
- `docs-governance`
  - Ubuntu `Docs Governance` enforces required active docs, failure-ledger synchronization, Golden
    Principles, and GitHub-operations invariants
  - Windows `Worktree Helper (Windows PowerShell)` runs
    `powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Test-New-CodexIssueWorktree.ps1`
    as a 28-case harness enforcing detached-first creation, clean source helper/guard/initializer
    artifacts (including local clean-filter canaries and index-hidden byte changes), independent
    guard/initializer selected-base blob pinning, fail-closed initialization, and
    permission-contract regressions. The helper's self-check is covered as pre-mutation hygiene, not
    as authentication before PowerShell starts executing it; selected-base OID checks likewise do
    not authenticate target initializer/guard bytes at handoff execution time. The permission model
    asserts no project-wide opt-in to Claude Code's unsandboxed Windows PowerShell tool, trusted
    versus untrusted project settings, main-checkout-only helper invocation, two exact target-bound
    guard/initializer launch rules without a second Claude worktree, restored host opt-in, directly
    pasteable here-string syntax, real PowerShell 5.1 two-argv transport, occupied-target rejection
    before normal or dry-run ref mutation, bounded dirty-artifact cleanup without force,
    clean-only separate-Git-dir late-collision removal plus preservation of
    tracked/untracked/ignored content and bytes hidden by `assume-unchanged`/`skip-worktree`,
    case-variant remote refresh, and a real stalled remote-helper root plus child that
    must be absent before bounded failure returns without changing refs or worktrees. It also covers
    post-checkout-hook timeouts after worktree registration, proving clean populated targets plus
    locked metadata are removed safely while dirty or index-hidden bytes and their registrations
    are preserved, alongside pre-mutation rejection of invalid, overlong, or
    namespace-colliding Windows branch refs
- `backend-architecture`
  - Enforces architecture boundaries in CI
- `backend-unit`
  - Domain + Application + CLI contract tests
  - Ubuntu and Windows matrix
- `api-integration`
  - API integration tests
  - Ubuntu and Windows matrix
- `frontend-unit`
  - Lint + coverage-threshold Vitest + typecheck + build
  - Ubuntu and Windows matrix
  - Uploads JUnit + coverage artifacts (`test-results/`, `coverage/`) for triage
- `container-images`
  - Validates compose rendering
  - Builds backend/frontend container images
  - Exports compressed image artifacts plus SHA256 checksums
- `e2e-smoke`
  - Playwright smoke + automation/ops + fixture bootstrap flow
  - Ubuntu only
  - Depends on all prior gates
- `migration-validation`
  - EF Core migration chain validation via `scripts/ci/validate-migrations.sh` (TST-61, `#869`/`#916`)
  - Runs in parallel with other required jobs
- `secret-scan` (#1132, ADR-0035)
  - Gitleaks PR-diff secret detection via `reusable-gitleaks.yml` (scan-mode `pr`, **enforcing** — fails on a newly-introduced secret)
  - Guarded to `pull_request` events (gitleaks PR mode is invalid on `push`/`merge_group`)
- `dependency-security` (#1132, ADR-0035)
  - Vulnerable-dependency scan via `reusable-dependency-security-signals.yml`; production deps only (`frontend-omit-dev`), high+ severity
  - **Advisory** (`enforce-findings: false`) pending baseline remediation (#1175); flips to enforcing per #1175
- `sast-scan` (#1132, ADR-0035)
  - Semgrep SAST via `reusable-sast-scanning.yml`
  - **Advisory** (`enforce-findings: false`) pending the pre-existing finding baseline triage (#1175)
- Frontend bundle-size budget runs **enforcing** as a step inside `frontend-unit` (`scripts/ci/check-bundle-size.mjs`, total-js < 1200 KB)

> Phased enforcement (ADR-0035): only `secret-scan` and the bundle check hard-block today;
> `dependency-security`/`sast-scan` run on every PR but are advisory until the baseline is clean.
> Branch-protection registration of the new check contexts is tracked in #1173.

Extended workflow: `.github/workflows/ci-extended.yml`

- `workflow-lint`
  - Actionlint validation for `.github/workflows/**` drift
- `dependency-review`
  - PR dependency change risk signal (`actions/dependency-review-action`)
- `backend-solution` + `e2e-smoke` + `load-concurrency-harness`
  - opt-in on PRs labeled `testing` or manual `workflow_dispatch` (runs Playwright smoke suite via `reusable-e2e-smoke.yml`)
  - load harness lane runs k6 board-heavy profile plus Playwright multi-session concurrency spec via `reusable-load-concurrency-harness.yml`
- `demo-director-smoke`
  - opt-in on PRs labeled `automation` or manual `workflow_dispatch`; PR-triggered runs still require watched-path changes because `ci-extended.yml` does not include `docs/**`
  - runs the deterministic `demo:director:smoke` path via `reusable-demo-director-smoke.yml`
- `sast-scanning`
  - Semgrep SAST with custom C# and TypeScript rules via `reusable-sast-scanning.yml` (CI-01, `#870`/`#915`)
  - opt-in on PRs labeled `security` or manual `workflow_dispatch`
  - advisory mode by default; enforceable via workflow input
  - `scripts/ci/summarize-sast-findings.mjs` produces human-readable summary
- `performance-regression-gate`
  - k6 thresholds (aggregate p95 <2s and p99 <5s, tagged SQLite board-write p95 <4.5s with a warning at the measured 2s capacity, error rate <1%) + bundle size checks via `reusable-performance-regression-gate.yml` (CI-03, `#872`/`#918`, recalibrated by `#1358` then `#1445`)
  - opt-in on PRs labeled `performance` or manual `workflow_dispatch`
  - `scripts/ci/check-bundle-size.mjs` and `scripts/ci/check-k6-thresholds.mjs` for deterministic threshold enforcement

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression (`dotnet test backend/Taskdeck.sln -c Release -m:1`)
- scheduled/manual E2E smoke suite (`reusable-e2e-smoke.yml`)
- scheduled/manual load-concurrency harness (`reusable-load-concurrency-harness.yml`)
- scheduled/manual container image regression
- scheduled/manual SAST scanning (Semgrep) via `reusable-sast-scanning.yml`
- scheduled/manual performance regression gate via `reusable-performance-regression-gate.yml`
- `developer-portal`: builds API, fetches `/swagger/v1/swagger.json`, runs `@redocly/cli build-docs`, uploads `artifacts/developer-portal/` including docs from `docs/api/` (PR #658)

Nightly quality workflow: `.github/workflows/nightly-quality.yml`

- scheduled/manual reporting lane for quality telemetry (non-blocking for required PR CI checks)
- backend coverage artifacts:
  - Domain coverage (`Taskdeck.Domain.Tests` with XPlat Code Coverage)
  - Application coverage (`Taskdeck.Application.Tests` with XPlat Code Coverage)
- frontend coverage artifacts:
  - `npm run test:coverage` output (`coverage/` + `test-results/`)
- dependency/security signal artifacts:
  - `dotnet list package --vulnerable --include-transitive` output + exit code
  - `npm audit --audit-level=high --json` output + exit code
  - normalized dependency-security summary (`summary.md`, `summary.json`) linked to `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`

Triage usage:
- check workflow step summary first for signal exit codes
- inspect uploaded artifacts to differentiate command failures from dependency findings
- treat this lane as reporting-first; promote to stricter gating only through a dedicated follow-up issue/decision

Release/security workflow: `.github/workflows/release-security.yml`

- release/tag/manual dependency inventory artifact generation
- backend/frontend vulnerability signal capture
- manual strict-enforcement option that fails on unresolved high/critical findings, non-zero dependency scan exits, or unparseable scan outputs
- reusable container artifact/checksum lane for release-ready outputs

CI extended dependency-security lane:

- `.github/workflows/ci-extended.yml` now exposes an opt-in `Dependency Security Signals` job through manual dispatch or PRs labeled `security`
- this lane is reporting-first and uses the same normalized summary format as nightly/release flows

## Testing Harness Improvement Wave (Delivered 2026-02-24)

Tracking issues:
- wave tracker: `#254`
- delivered execution: `#255` to `#260`

Already-covered pack scenarios (no duplicate implementation issue required):
- WIP limit enforcement already covered across application/API/E2E.
- sandbox-gated database import/export rejection outside Development already covered.
- starter-pack idempotency/conflict safety already covered.

Knowledge transfer applied to existing seeds:
- `#89`: targeted property/fuzz pilot surfaces (manifest/query/import-export boundaries)
- `#90`: non-blocking scheduled mutation-lane posture
- `#106`: dependency/security signal command baseline (`dotnet list package --vulnerable`, `npm audit`)
- `#168`: CI topology routing for OpenAPI/nightly-quality lanes

Delivered outcomes:
- `#255` removed residual wall-clock flake vectors and centralized reusable E2E polling helpers
- `#256` locked drag/drop persistence after full reload into Playwright smoke coverage
- `#257` centralized representative `400/401/403/404/409` API error-contract assertions
- `#258` added OpenAPI generation + parse-validation artifacts in CI
- `#259` codified `docs/GOLDEN_PRINCIPLES.md` with lightweight mechanical enforcement
- `#260` added the non-blocking nightly-quality workflow for coverage and dependency/security signal artifacts

Useful local checks for this wave:

```bash
rg -n "Thread\\.Sleep|new Promise\\(.*setTimeout" backend/tests frontend/taskdeck-web/tests/e2e
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~ApiErrorContractApiTests"
(cd frontend/taskdeck-web && npx playwright test tests/e2e/smoke.spec.ts tests/e2e/automation-ops.spec.ts tests/e2e/capture-loop.spec.ts --reporter=line)
node scripts/check-golden-principles.mjs
node scripts/check-docs-governance.mjs
```

OpenAPI guardrail local checks (`#258`):

```powershell
./scripts/ci/generate-openapi-artifact.ps1 -OutputPath "artifacts/openapi/taskdeck-api.json"
./scripts/ci/validate-openapi.ps1 -SpecPath "artifacts/openapi/taskdeck-api.json"
```

Malformed-output simulation (deterministic parse failure check):

```powershell
"not-json" | Set-Content -Path artifacts/openapi/invalid-openapi.json
./scripts/ci/validate-openapi.ps1 -SpecPath "artifacts/openapi/invalid-openapi.json"
```

Follow-up intentionally deferred from this issue:
- snapshot/diff enforcement against a checked-in OpenAPI baseline remains a future enhancement
- current guardrail scope is generation + parse/shape validation + CI artifact publication

## Outreach CRM Deferred Wave (Planning, 2026-02-23)

Tracking issues:
- wave tracker: `#262`
- deferred execution: `#263` to `#268`

Reuse links (no duplicate implementation issue):
- `#75` delivered import-adapter foundation for outreach CSV mapping/dedupe profile
- `#77` analytics model/dashboards for future outreach scoreboard metrics
- `#175` first-party starter-pack catalog expansion for outreach blueprint inclusion

Planned quality expectations when implementation starts:
- YAML front-matter parser round-trip stability tests (contact fields + timeline preservation)
- cadence scheduling determinism + throughput-control guardrail tests
- API/UX regression for contact logging and dashboard action loops
- E2E coverage for outreach loop: import/apply -> contact update -> cadence proposal -> dashboard action flow

## Coverage Map

- Domain invariants:
  - `backend/tests/Taskdeck.Domain.Tests`
- Application services:
  - `backend/tests/Taskdeck.Application.Tests`
  - Includes board/card/column/label/auth/authorization/board-access/export-import/history/queue plus automation/archive/chat/ops/log services
  - Includes database export/import guardrail coverage (sandbox gating, payload validation, file replacement)
  - Includes external import-adapter parsing and board upsert orchestration coverage (CSV/outreach profile, dedupe policy, rollback safety path)
  - Includes starter-pack manifest parsing/validation, first-party catalog validity, and apply-planning coverage
  - Includes LLM tool-calling orchestrator coverage (multi-turn loop, timeout, round limits) and read tool schema generation
  - Includes GDPR data export service (user-scoped completeness, versioned payload) and account deletion service (re-auth, confirmation phrase, PII anonymization)
  - Includes board metrics service coverage (aggregation, date range, label grouping)
  - Includes MCP board resource coverage (listing, phantom-user fallback, multi-user scoping)
  - Includes integrations registry service coverage (connector CRUD, enable/disable lifecycle, event logging)
- HTTP contracts and behavior mappings:
  - `backend/tests/Taskdeck.Api.Tests`
  - Includes core + automation/archive/chat/ops/log/health controllers
  - Includes rate-limit policy coverage (`RateLimitingApiTests`) for burst throttling, retry metadata contract, reset-window recovery, and cross-user boundary behavior
  - Includes security-header baseline coverage (`SecurityHeadersApiTests`) for success/auth-failure paths and HTTPS HSTS posture assertions
  - Includes board-scoped external import endpoint coverage (authz, malformed input, duplicate handling, apply/update flow, rollback safety)
  - Includes outbound webhook API and worker coverage (`OutboundWebhooksApiTests`, `OutboundWebhookDeliveryWorkerTests`) for claim/reload handling, cancellation requeue, and non-success HTTP retry/dead-letter branches
  - Includes `ResultExtensions` mapping tests for standardized API error/status behavior
  - Includes integrations controller coverage (7 endpoints: CRUD + enable/disable, auth enforcement)
- CLI contracts:
  - `backend/tests/Taskdeck.Cli.Tests`
- Architecture boundaries:
  - `backend/tests/Taskdeck.Architecture.Tests`
  - Enforces project-reference boundaries between Domain/Application/Infrastructure/API projects
  - Enforces source-layer purity via forbidden namespace imports in Domain and Application source trees
  - Enforces API controller boundary invariants:
    - only `AuthController` and `HealthController` may inherit `ControllerBase` directly
    - protected controllers must declare `[Authorize]`
  - Failure remediation:
    - move forbidden dependencies to the correct layer abstraction/interface
    - route protected HTTP surface through `AuthenticatedControllerBase`
    - add/restore `[Authorize]` on protected controller classes
- Frontend unit behavior:
  - `frontend/taskdeck-web/src/tests`
  - Components, stores, API modules, composables, utilities
  - Includes shared utility tests for `queryBuilder` and `errorMessage`
  - Includes GitHub OAuth API client and session store coverage (`authApi`, `sessionStore`)
  - Includes board metrics API client and store coverage (`metricsApi`, `metricsStore`)
- End-to-end journeys:
  - `frontend/taskdeck-web/tests/e2e`
  - Includes deterministic starter-pack fixture bootstrap coverage for `small`, `medium`, and `edge` manifest scenarios
  - Includes unauthenticated SignalR negotiate rejection coverage aligned with the runtime client handshake path
  - Includes dedicated multi-session concurrency regression coverage (`tests/e2e/concurrency.spec.ts`)
  - Includes integrated multi-component verification journeys (`tests/e2e/integrated-verification.spec.ts`) — see `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`
  - Includes manual validation slice C E2E coverage: `tests/e2e/validation-automation-proposals.spec.ts` (8 tests), `tests/e2e/validation-chat-bootstrap.spec.ts` (9 tests)
  - Includes manual validation slice D E2E coverage: `tests/e2e/validation-ops-logs-health.spec.ts` (17 tests)
  - Includes manual validation slice E E2E coverage: `tests/e2e/validation-starter-packs.spec.ts`, `tests/e2e/validation-archive-recovery.spec.ts`, `tests/e2e/validation-activity-traceability.spec.ts` (23+ tests)
  - Includes integrated verification E2E coverage: `tests/e2e/integrated-verification.spec.ts` (4 tests covering capture-to-board pipeline, board bootstrap, workspace navigation, auth denial)
- Load and concurrency API profile:
  - `tests/load/k6/board-heavy-load.js`
  - Includes seeded-user board-heavy read/write load mix and threshold-based regression diagnostics

## Integrated Multi-Component Verification

The integrated verification program ties automated and manual testing into cross-component scenarios that validate subsystem interactions end-to-end.

Key resources:
- `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md` — scenario matrix, release gating criteria, automated/manual split
- `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md` — standard template for manual verification cycles
- `frontend/taskdeck-web/tests/e2e/integrated-verification.spec.ts` — automated cross-component E2E journeys

The strategy defines 18 verification scenarios (V-01 through V-18) across 5 subsystem areas. Scenarios are tiered by severity:
- **Tier 1** (V-01 to V-04): Critical path — must pass for any release
- **Tier 2** (V-05 to V-10): High-value cross-cutting — must pass for feature releases
- **Tier 3** (V-11 to V-18): Extended coverage — recommended for major releases

## Manual Verification

Use `docs/MANUAL_TEST_CHECKLIST.md` for action-by-action manual validation.
Use `docs/ops/OBSERVABILITY_BASELINE.md` for telemetry dashboard/alert baseline and observability smoke validation.
Use `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md` for structured manual rehearsal cycles with evidence capture.

Detailed step-indexed validation checklists:
- Slice A — workspace shell, board lifecycle, keyboard UX: `docs/testing/manual-validation-a-workspace-board-ux.md`
- Slice B — authz policy, cross-user isolation, API error contracts: `docs/testing/manual-validation-b-authz-contracts.md`
- Slice C — automation proposals, chat bootstrap, execution safety (45 scenarios): `docs/testing/MANUAL_VALIDATION_SLICE_C_SCENARIOS.md`; rehearsal runbook: `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_C.md`
- Slice D — ops CLI, log query/correlation, health telemetry (25 scenarios): `docs/testing/MANUAL_VALIDATION_SLICE_D_SCENARIOS.md`; rehearsal runbook: `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_D.md`
- Slice E — starter packs, archive recovery, activity traceability (25 scenarios): `docs/testing/MANUAL_VALIDATION_SLICE_E_SCENARIOS.md`; rehearsal runbook: `docs/testing/MANUAL_REHEARSAL_RUNBOOK_SLICE_E.md`

Integrated verification program:
- Cross-component verification strategy (18 scenarios): `docs/testing/INTEGRATED_VERIFICATION_STRATEGY.md`
- Manual rehearsal template: `docs/testing/MANUAL_REHEARSAL_TEMPLATE.md`

## Thesis Alignment Validation (Capture Realignment)

This section defines validation expectations for the capture-first direction.

Current state:
- capture MVP loop is shipped end-to-end (`#200` to `#211`)
- capture loop assertions below are required baseline checks for regression safety

Required assertions:
- capture action is fast and deterministic (target under 10 seconds to persisted artifact in normal local conditions)
- triage path stays proposal-first (no direct board mutation from model output)
- provenance links are visible from proposal/card surfaces back to capture source
- error and auth contracts remain stable (`ApiErrorResponse`, `401/403/404` policy)

Recommended execution pairing:
- automated: API + frontend unit + E2E capture loop (`#210` delivered, retained as active regression path)
- manual: capture friction/trust checks in `docs/MANUAL_TEST_CHECKLIST.md`

## Incident Rehearsals

Manual incident rehearsals complement automated tests by validating diagnosis and recovery workflows against realistic failure conditions. Rehearsals are scheduled monthly (lightweight, ~30 min) and quarterly (deep drill, ~2 hours).

Key resources:
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` -- schedule, rotation, and process
- `docs/ops/rehearsal-scenarios/` -- scenario templates (health degradation, telemetry gaps, deployment failures)
- `docs/ops/EVIDENCE_TEMPLATE.md` -- evidence package format
- `docs/ops/REHEARSAL_BACKOFF_RULES.md` -- how rehearsal findings become tracked issues
- `docs/ops/rehearsals/` -- completed rehearsal evidence packages

Rehearsals are distinct from the automated failure-injection drill suite (`docs/ops/FAILURE_INJECTION_DRILLS.md`). Drills are scripted and CI-runnable; rehearsals are human-driven and focus on diagnosis speed, tooling gaps, and recovery muscle memory.

## Development Sandbox Mode

For local development only, authorization bypass can be enabled via:
- `backend/src/Taskdeck.Api/appsettings.Development.json`
- `DevelopmentSandbox.Enabled = true`

Safety boundary:
- Sandbox bypass is forced off outside Development environment.
- Validation and data integrity rules still apply.

## Webhook HMAC Signature Verification Coverage (PR #750, delivered 2026-04-04)

Tracking issue: `#726`

New test coverage:
- `OutboundWebhookHmacDeliveryTests` (11 tests): header format verification (`sha256=<64-hex>`), HMAC round-trip receiver recompute and match, wrong-key rejection, secret rotation produces different signature, body/content-type matching, large payload (100 kB), timing-safe comparison via `CryptographicOperations.FixedTimeEquals`, determinism, key-differ properties

Key adversarial review findings fixed: secret rotation test was testing different subscriptions (not actual rotation on same subscription); BCL-testing assertions replaced with real domain property tests.

## Webhook Delivery Reliability and SSRF Coverage (PR #756, delivered 2026-04-04)

Tracking issue: `#710`

New test coverage across webhook test suite (78 tests total across 9 files):
- `OutboundWebhookEndpointGuardTests` (Application.Tests): SSRF guard cases covering private IPv4 ranges and endpoint validation
- `OutboundWebhookServiceTests` (Application.Tests, 19 tests): service-level webhook subscription and delivery orchestration
- `OutboundWebhookSignatureTests` (Application.Tests, 8 tests): HMAC signature computation and verification
- `OutboundWebhookDeliveryWorkerTests` (Api.Tests, 8 tests): worker-level delivery scheduling and retry logic
- `OutboundWebhookHmacDeliveryTests` (Api.Tests, 11 tests): end-to-end HMAC delivery including header format, round-trip, wrong-key rejection
- `OutboundWebhooksApiTests` (Api.Tests, 10 tests): API endpoint contract for webhook subscription management
- `OutboundWebhookDeliveryRepositoryTests` (Api.Tests, 3 tests): repository-level delivery persistence
- `OutboundWebhookDeliveryTests` (Domain.Tests, 8 tests): domain entity state and transitions
- `OutboundWebhookSubscriptionTests` (Domain.Tests, 7 tests): subscription domain entity

Key adversarial review fix: `HttpClient` resource leaks across 9 test methods.

Manual validation recommended: configure a webhook endpoint with a known secret and verify that (a) the `X-Taskdeck-Webhook-Signature` header (alongside `X-Taskdeck-Webhook-Timestamp`) is present and verifiable with HMAC-SHA256, and (b) a webhook targeting `http://localhost/` or `http://10.0.0.1/` is rejected at the SSRF guard.

## Frontend Regression Test Wave (PRs #742–#745, #748, #743, #744, #754, delivered 2026-04-04)

Tracking issues: `#683`, `#680`, `#685`, `#686`, `#687`, `#688`

New test files:
- `boardStore.wipLimit.spec.ts` (7 tests): WIP-limit toast deduplication regression for `createCard` and `moveCard`; guards against future double-toast introduction
- `sessionStore.authToast.spec.ts` (20 tests): auth-flow toast lifecycle — login/register/OAuth failure and success toasts, cross-flow isolation, auto-removal independence; uses real `toastStore` backed by fresh Pinia
- `router/authGuard.spec.ts` (new): auth guard decision table — unauthenticated redirect, expired-token cleanup, authenticated pass-through, deflection from /login when authenticated, demo mode, 12-route exhaustive table
- `router/workspaceRouteStability.spec.ts` (new): workspace mode persistence across simulated reloads, hydration drift prevention, `resetForLogout` cleanup
- `InboxView.spec.ts` (+21 tests): single-item triage action states (per status variant), bulk action bar visibility and count, batchBusy disabled state, select-all behavior; all assertions on DOM state

Frontend suite total after this wave: **1592 passing** (up from 1496 pre-wave).

## Feature, Analytics, MCP, Chat, Testing, and UX Wave (PRs #787–#793, delivered 2026-04-08)

Tracking issues: `#78`, `#79`, `#249`, `#576`, `#654`, `#705`, `#717`

New test coverage (~390+ new tests total):

### Backend

- `MetricsExportServiceTests.cs` (21 unit tests + 5 adversarial-review injection tests): CSV structure validation, all 5 sections, CSV injection prevention vectors including embedded newlines
- `MetricsExportApiTests.cs` (8 integration tests): auth, cross-user isolation, empty board, date range, Content-Disposition headers
- `ForecastingServiceTests.cs` (32 tests): validation, authorization, edge cases (zero throughput, no done column, single data point, large card counts, bounce deduplication, history-window-vs-span)
- `ApiKey` domain tests (11 tests): entity construction, SHA-256 hashing, `tdsk_` prefix, revocation, expiration
- API key integration tests (20 tests): auth, key lifecycle (create/list/revoke), cross-user isolation, MCP endpoint access
- `ClarificationDetectorTests.cs` (22 tests + 6 false-positive regression): pattern detection, skip phrases, round counting, prompt building, strong/weak signal split
- `ChatServiceClarificationTests.cs` (7 tests): service-level clarification flow, round enforcement, skip behavior
- `ConcurrencyRaceConditionStressTests.cs` (13 tests): queue claim races, card conflicts, proposal approval races, rate limiting, multi-user stress
- `EntityAdversarialInputTests.cs` (77 FsCheck tests): Board, Card, Column, Label, AutomationProposal with adversarial strings, boundary lengths, GUID validation
- `JsonSerializationRoundTripFuzzTests.cs` (29 tests): serialize/deserialize identity, GUID format variations, DateTime boundaries, malformed JSON
- `AdversarialInputApiTests.cs` (80 tests): no 500s from adversarial input across all major endpoints, malformed JSON, wrong content types, concurrent adversarial

### Frontend

- `InboxView.spec.ts` (+7 tests): primitive-driven loading/error/empty state assertions, skeleton detection, retry button
- `inputSanitization.spec.ts` (16 fast-check tests): card titles, search queries, board names, chat messages, URL encoding, JSON round-trip, Unicode edge cases
- `storeResilience.spec.ts` (9 fast-check tests): random action sequences on board store, API error handling, adversarial content

### Dependencies added

- Backend: `FsCheck` and `FsCheck.Xunit` (for property-based testing, extending existing pattern)
- Frontend: `fast-check` (dev dependency, for property-based testing)

### Key adversarial review findings fixed

- **HIGH**: CSV injection via embedded newlines in export (`#787`), throughput double-counting in forecasting (`#790`), false-positive clarification heuristic (`#791`)
- **MEDIUM**: Key-existence oracle + modulo bias in API key generation (`#792`), capture DTO round-trip test (`#789`), history window denominator (`#790`), CancellationToken forwarding (`#787`)
- Fixed test quality issues: misleading doc comments, weak assertions, non-thread-safe variables, redundant ARIA roles, missing screen reader announcements

Backend suite total after this wave: **~3,460+ passing** (estimated at time of wave). Frontend suite total: **~1,891 passing** (estimated at time of wave). Combined: **~5,370+** (estimated at time of wave). See [Current Verified Totals](#current-verified-totals-2026-05-16) for latest recertified counts.

### Test expansion wave (`#721`) completion

This wave delivered the final 2 issues from the rigorous test expansion wave (`#721`):
- `#705` — Concurrency and race condition stress tests (13 tests)
- `#717` — Property-based and adversarial input tests (211 tests)

**All 25 of 25 issues in the test expansion wave are now delivered.** Total new tests from the wave: ~1,350+.
