# Engineering review bundle reconciliation

> **Dated, non-authoritative analysis (2026-08-30).** Live code, executable checks,
> accepted ADRs, canonical docs, and current GitHub issues outrank this note and the
> external bundle it evaluates.

Tracker: [#2349](https://github.com/Chris0Jeky/Taskdeck/issues/2349)

Bundle source commit: `221aa88c80f5b2c3265ac794edc2ade0edd70c72`

First live reconciliation head: `ca93903c8e64b37b8e6187b5eac138b732f47264`

This note owns only the evidence and disposition of the 2026-08-30 external engineering-review
bundle. Update it when #2349's intake map changes; archive nothing separately when the tracker
closes because the dated folder is already historical analysis. The supplied bundle remains an
untracked local source under `filesAndResources/taskdeck-review-bundle/`; its generated reports,
patches, dashboard, and examples are intentionally not promoted wholesale.

## Outcome

The bundle is useful, but it is not merge-ready or fully current.

- The authenticated PWA API-cache defect is confirmed and is the only new v0.3 release blocker
  from this review.
- Hosted-session, database-transfer, connector-rotation, refactor, performance, test-typecheck,
  and Smart CI recommendations already have live owner issues.
- The unexpected-error finding is real on persisted and non-standard surfaces, but the bundle
  overstates ordinary HTTP exposure because Taskdeck already sanitizes standard
  `UnexpectedError` responses.
- The entitlement design has a sound modular-monolith core, but implementation remains blocked by
  the licensing/business-model decision, retention evidence, and a stable billing-subject model.
- Neither supplied patch should be applied unchanged.
- No production code from the bundle was adopted in this primer.

## Validation and drift

The bundle's own validator was run both independently and against live Taskdeck:

```text
py -3 filesAndResources/taskdeck-review-bundle/scripts/validate_bundle.py filesAndResources/taskdeck-review-bundle
Bundle validation passed: 150 checks

py -3 filesAndResources/taskdeck-review-bundle/scripts/validate_bundle.py filesAndResources/taskdeck-review-bundle --repo .
Bundle validation passed: 152 checks
```

The second run includes `git apply --check` for both patches. Applicability proves only that the
diff context still matches; it does not prove architecture, compilation, tests, or runtime behavior.

The bundle source commit is an ancestor of the live baseline. The 42 intervening commits changed
116 files (`16,816` insertions and `159` deletions), principally through Context Fabric and Smart
CI work. Re-running the supplied metric script directly against this checkout is unsafe because it
does not exclude `.worktrees/`; the preserved #1638 worktree was counted as a second repository
and inflated test LOC to 410,378. The live figures below were regenerated from an exact
`git archive` of the live head.

| Metric | Bundle snapshot | Live head | Disposition |
| --- | ---: | ---: | --- |
| Hand-written production code LOC | 138,617 | 139,784 | Reproduced; +1,167 |
| Test code LOC | 222,557 | 224,379 | Reproduced; +1,822 |
| Test-to-production ratio | 1.61:1 | 1.61:1 | Stable |
| Generated/migration LOC | 74,789 | 77,143 | Reproduced; +2,354 |
| Hand-written production files | 1,201 | 1,208 | Reproduced; +7 |
| Production files over 500 code LOC | 47 | 47 | Stable |
| Production files over 1,000 code LOC | 5 | 7 | Capture and export crossed the threshold |
| ADRs | 65 | 66 | Current count confirmed |
| GitHub Actions workflows | 33 | 35 | Current count confirmed |
| Frontend specs outside complete typecheck | 64 | 64 | 40 quarantined + 24 root specs |

The bundle's approximate `[Fact]`/`[Theory]`, Vitest, and Playwright declaration totals are
regex-dependent headline estimates and are not emitted by the supplied metric scripts. They were
not promoted as exact repository facts. The checked-in `metrics/refined_metrics.json` also uses
an older, smaller schema than the current `scripts/refined_metrics.py` output, even though its
checksum is internally valid.

### Current structural hotspots

| Rank | File | Code LOC | Ruling |
| ---: | --- | ---: | --- |
| 1 | `PaperReviewView.vue` | 1,600 | Extract selection, preview, decision, and route lifecycles under #2236 |
| 2 | `AutomationProposalService.cs` | 1,574 | Extract pure decision policy and narrow commands under #2236 |
| 3 | `FirstRunBootstrapper.cs` | 1,227 | Separate pure planning from filesystem/secrets/process execution |
| 4 | `CaptureService.cs` | 1,061 | Re-measure after active Context Fabric churn settles |
| 5 | `OpenAiCompatibleLlmProvider.cs` | 1,047 | Separate protocol codecs/parsers when this seam is next touched |
| 6 | `PaperBoardView.vue` | 1,020 | Retain as a measured #2236 candidate |
| 7 | `DataExportService.cs` | 1,013 | Do not refactor during active export/Context Fabric evolution |

File size is a change-risk signal, not a defect by itself. Extraction should follow behavioral
ownership, transaction boundaries, churn, and characterization tests rather than a line-count
rewrite target.

## Security and patch review

### SEC-001 and patch 0001

Confirmed mechanism:

- `frontend/taskdeck-web/vite.config.ts` registers a Workbox `NetworkFirst` runtime route for
  `/api/`, with a one-day `taskdeck-api-cache-v2` cache and a ten-second timeout.
- browser bearer identity is stored separately in `localStorage`;
- CacheStorage survives logout and account changes, while the cache match is not partitioned by
  authenticated identity.

An offline request or timeout can therefore replay a response cached under a previous login. This
is tracked as [#2350](https://github.com/Chris0Jeky/Taskdeck/issues/2350), Priority I in v0.3.

Patch 0001 has two merge-blocking design gaps:

1. it adds API `no-store` inside `SecurityHeadersMiddleware.ApplyHeaders`, but that path is only
   registered when optional `SecurityHeadersSettings.Enabled` is true; the privacy header must be
   unconditional for every `/api/**` response;
2. token changes schedule cache deletion fire-and-forget, so the patch does not prove that a new
   identity cannot issue a read before purge completes.

The activation-time `event.waitUntil` cleanup is a useful starting point. Execution must preserve
the share-target queue and prove a real old-service-worker upgrade plus two-account offline/timeout
behavior.

### SEC-003 and patch 0002

Taskdeck already has two important safe boundaries:

- `UnhandledExceptionMiddleware` maps unhandled HTTP exceptions to a generic response; and
- `ResultExtensions.ToErrorActionResult` replaces ordinary 500-class `Result` messages with
  `An unexpected error occurred.`

The bundle's CSV mixes several classes: deliberate `DomainException` or parse messages, protected
logging, ordinary standard-HTTP results already sanitized at the controller, and genuine raw
messages persisted or emitted through batch, run-state, health, MCP, or CLI surfaces. The broad
claim that every listed catch exposes an HTTP message is therefore incorrect, while the
cross-surface design gap remains real.

Patch 0002 is not the right central contract because it:

- creates a second error-reference scheme instead of using the existing bounded request/trace ID;
- proves only returned-text sanitization with a `NullLogger`, not full-exception logging and
  correlation;
- pilots a standard HTTP path while leaving the persisted and non-standard surfaces untouched; and
- supplies no low-false-positive guard for future generic catches.

The corrected owner is [#2351](https://github.com/Chris0Jeky/Taskdeck/issues/2351), with #2281
retaining the batch-execute receipt/timing slice and #2213 retaining provider-health disclosure.

### Other security findings

- Hosted browser token storage is not a new issue. #1644 now owns the explicit
  `LocalBearer`/`HostedSession` decision and is v0.4 / Priority I, sequenced after #2350.
- AES-GCM credential encryption is sound at the primitive level. Version, key-ID, AAD binding,
  safe failure, and resumable rotation remain lifecycle debt under #1134; no plaintext or direct
  exploit was established.
- The bundle did not dynamically penetrate a deployment or reproduce the cache defect in a
  two-account browser. Those claims remain static-mechanism evidence until #2350 executes them.

## Data, performance, modularity, and health rulings

- Database-file export/import still materializes `byte[]` and can amplify a large payload in
  memory. However, the endpoint is DevelopmentSandbox-only today, so the bundle's generic hosted
  P2 framing is too strong. #1166 owns WAL-native streaming/activation design; #2238 owns
  production backup/restore.
- `DataExportService.StreamUserDataExportAsync` already writes a paged streaming user export.
  DATA-003's premise is substantially superseded; remaining per-section bounds belong to measured
  follow-ups, not a second export architecture.
- Proposal, Paper Review, and first-run extractions are useful #2236 inputs. Narrowing
  `IUnitOfWork` is an incremental rule inside a touched vertical slice, not a standalone
  repository rewrite.
- Query/memory budgets belong to #2237 and must record hardware, dataset, commit, SQL/row counts,
  allocations/RSS, and percentiles. The bundle did not execute runtime benchmarks.
- Smart CI impact maps and feedback-time work are superseded by ADR-0066, #2324, and its CI-NN
  children.
- The proposed agent-context manifest duplicates or cuts across `autodoc/AGENT_INDEX.md`, #1291,
  and agent-harness#101. It is rejected until those owners demonstrate a missing enforceable fact.
- The root formatting and staged .NET analyzer baseline is genuinely missing and is tracked at
  [#2352](https://github.com/Chris0Jeky/Taskdeck/issues/2352), v0.5 / Priority III.
- The 40-file frontend quarantine plus 24 root test specs are already owned by #1607, now assigned
  to v0.5. Node types must remain in a separate test project from the DOM production/spec project.

## Twenty-brief disposition

| Brief | Disposition | Live owner / reason |
| --- | --- | --- |
| SEC-001 | Adopt with corrections | New #2350, v0.3 / Priority I |
| SEC-002 | Existing owner | #1644, v0.4 / Priority I; explicit hosted profile |
| SEC-003 | Adapt | New #2351, v0.4 / Priority II; build on existing HTTP/correlation boundary |
| SEC-004 | Existing owner | #1134; confirm live connector lifecycle before scheduling |
| DATA-001 | Existing owner | #1166; add stream/temp lease and memory proof |
| DATA-002 | Existing owner | #1166 + #2238; prefer quiesce/restart over unproved hot swap |
| DATA-003 | Substantially superseded | Streaming user export already exists |
| MOD-001 | Existing owner | #2236 proposal decision/command seam |
| MOD-002 | Existing owner | #2236 Paper Review lifecycles |
| MOD-003 | Existing owner | #2236 first-run planning seam |
| MOD-004 | Execution rule | Narrow dependencies only in touched slices |
| PERF-001 | Existing owner | #2237 benchmark and ranked-candidate programme |
| HEALTH-001 | Adopt with staged scope | New #2352, v0.5 / Priority III |
| HEALTH-002 | Existing owner | #1607, v0.5 / Priority III |
| HEALTH-003 | Reject as competing authority | Existing index/harness owners first |
| HEALTH-004 | Superseded | #2324 and CI-01 through CI-15 |
| ENT-001 | Decision-preparation only | New #2353, unmilestoned / Priority IV |
| ENT-002 | Deferred | No allowance implementation before kernel, subject, and real consumer |
| ENT-003 | Deferred | No offline paid lease before legal/product grace decision |
| ENT-004 | Deferred | No billing provider/projector before #2012 and hosted security gates |

## Milestone and queue ruling

- **v0.3:** #2350 is the sole new release blocker because the unsafe cache ships in the current PWA
  surface.
- **v0.4:** #2349 records this primer; #1644 and #2351 carry the hosted security boundary. Existing
  #2236/#2237 remain the refactor/performance owners. Milestone membership does not make every item
  an open-beta blocker.
- **v0.5:** #2352 and #1607 carry measured health ratchets.
- **v0.6/v0.7:** no bundle-only issue was manufactured to fill a release. Current product themes
  outrank the external roadmap.
- **Commercial horizon:** #2353 remains unmilestoned and decision-blocked. #2012 and retention
  evidence must precede paid-tier implementation; v0.4 hosted open beta is not a commercial
  commitment.

The five newly seeded records (#2349-#2353) and the two reclassified existing issues (#1644 and
#1607) were verified in ProjectV2 as `Pending` with matching Priority fields. No item was promoted
to `Now` or `Next`. The complete post-apply project audit scanned 2,325/2,325 items, wrote 32
field corrections across the concurrently changing project, and ended with zero Priority drift.

## Entitlement architecture ruling

The bounded-module direction is retained, with corrections documented in
[ENTITLEMENTS_PRIMER.md](ENTITLEMENTS_PRIMER.md):

- resource authorization precedes entitlement evaluation;
- the `LICENSING.md` free boundary is monotonic and cannot be revoked by plan or provider state;
- subject identity stays abstract until the work/account model is settled;
- degraded behavior is explicit per operation and cannot collapse into a permissive
  `IsAllowed=true`;
- queued work is rechecked when claimed;
- MCP discovery filtering is only usability; invocation rechecks are authoritative;
- allowances use atomic reserve/commit/release/reconcile semantics;
- normal requests read a local snapshot and never call a payment provider; and
- an `ee/` directory alone does not establish a safe GPL/proprietary legal boundary.

## Not verified

- A deployed two-account offline/timeout reproduction of #2350.
- Compilation or focused/full tests for either supplied patch; neither patch was applied.
- Runtime latency, RSS, query, large-database, or browser-profile measurements.
- Live payment, OAuth, email, LLM-provider, cloud, backup/restore, or licence-lease behavior.
- Legal conclusions about GPL, proprietary modules, relicensing, tax, consumer law, or trademarks.
- Exact declaration-style test counts beyond the reproducible LOC/file metrics.

## Next safe slices

1. Implement #2350 alone, adapting patch 0001 and proving the upgrade/account-switch boundary.
2. Decide and record #1644's hosted-session profile before public registration work.
3. Inventory #2351's non-standard/persisted error surfaces, then pilot one surface using the
   existing correlation boundary.
4. Execute #2236/#2237 measurement-first slices without overlapping active Context Fabric files.
5. Keep #2353 architectural only until #2012 and the subject/retention gates are resolved.
