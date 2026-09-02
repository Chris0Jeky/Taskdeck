# v0.3/v0.4 acceleration bundle — 2026-09-02 re-pass (archive + validated head-start)

Last Updated: 2026-09-02

Tracker `#2376` (follow-up to `#2348`). Base `main` `de488fea0`. The 2026-08-30 pass (`RECONCILIATION.md`)
dispositioned the bundle and admitted a first wave; it archived nothing and left the 34 issue packs unread by
anyone but its author. This pass archives the durable material, validates every issue pack against today's
source, and gives each open target issue one curated head-start file plus a comment. Planning input, not
authority: the live issue, accepted ADRs and `docs/STATUS.md` win over anything here.

## What shipped since the 2026-08-30 reconciliation (do not re-plan)

| Bundle lane | Delivered | Evidence | Consequence for the head-start |
| --- | --- | --- | --- |
| CF-01 durable Capture (`#2255`, "first executable vertical", five child PRs) | Closed 2026-08-30 | PR `#2344`; `Captures` table, ID-preserving backfill as a reconcile pass, dual-write, Inbox reads through `ICaptureStore`, `SourceAsset` foundation | CF-02/CF-03 now sit behind the residuals CF-01b `#2345` (JSON-twin retirement) and CF-01c `#2347` (divergence repair); the pack's parity-digest reader was never built and is not owed |
| Production backup/restore proof (`#2238`) | Closed 2026-08-31 | PR `#2361`: AES-256-GCM `RecoveryArchive`, `taskdeck --backup` / `--restore` intercepted pre-host, container wrappers in `deploy/Dockerfile.production`, `scripts/ci/run-container-backup-restore-smoke.sh`, `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` | The bundle's manifest-file design is superseded; only the restore-drill timing worksheet survives (folded into `issues/1772-*.md`) |
| Connector-key decryptability (`#2239`) | Closed 2026-08-31 | PR `#2360`: `taskdeck --verify-connectors`, read-only `immutable=1` open, refuses WAL/SHM sidecars, wrong-key and damaged-ciphertext deliberately indistinguishable | `ConnectorKeyVerifier.cs` superseded; the candidate's `--json` output and "not exercised" rule are LOW follow-ups |
| Smart CI baseline / control plane (bundle: "future prerequisite") | Merged 2026-08-30 | PRs `#2341` (ADR-0066, baseline), `#2342` (shadow policy, fail-closed planner, observation gate, pin inventory, runner broker), `#2343` (artifact retention), `#2346` (base-branch checkout); `ci/schemas/ci-run.v1.schema.json` and `evaluate-gate.mjs` already write a receipt | CI-06/10/12/15 extend the shipped control plane; the bundle's same-name `ci-run.v1` schema is incompatible with the shipped one |
| Context Fabric reconciliation (external audit of the scaffold) | Merged 2026-08-30 | PR `#2320`: `SourceAsset`, three capture state axes, Worker Protocol v1-alpha, `IBlobStore` reference semantics | Several packs describe a scaffold that is now live behaviour |
| REF-0 measurement tooling (`#2236`) | Open PR `#2356`, parked | `scripts/analysis/` not on `main` yet | Restart that PR; the bundle's ranker is its ancestor, not a replacement |
| Telemetry decision (`#1308`) | Option B ruled 2026-08-29 (q-5); `docs/TELEMETRY.md` on `main` | Issue comment 2026-08-29T23:54Z | The pack's "decision pending" framing is stale; endpoint, retention and publication values remain open |
| v0.5 / v0.6 vocabulary (`ProcessingProfilePreset`, `ProcessingEgressClass`, `ProcessorEligibility`, `ProcessingConsentState`, `MetricAvailability`) | Merged 2026-09-02 | PR `#2372` | Not a v0.4 concern, but CF-03's policy-snapshot digest must use them |

Milestones at re-pass time: milestone 4 (v0.3) 58 open / 30 closed; milestone 5 (v0.4) 27 open / 3 closed
(bundle snapshot: 61/18 and 24/0). Milestone 5 gained `#2345`, `#2347`, `#2351`, `#1644` since the snapshot.

## Re-validation receipt (this machine, the re-added bundle copy)

| Check | Result |
| --- | --- |
| Inventory | 219 files; the copy lacks `01_MILESTONE_5/issue-packs/2094.md` (only the issue-comment survives), so `verify_bundle.py` fails on "M5 issue-pack coverage 23 of 24" and one checksum line; every other checksum matches |
| `validate_task_queue.py` | pass (internally well-formed; queue states are stale and were not archived) |
| Bundle Python suite | 11 passed, 1 failed — the documented Windows `is_absolute()` failure, unchanged from 2026-08-30 |
| C# candidates | build clean in isolation from the archive location (`candidates/README.md` has the command) |
| JSON Schemas | 3 valid draft 2020-12 schemas (`ci-run`, `quarantine`, `backup-manifest`); the handoff copies of `ci-run` / `quarantine` were byte-identical to the smart-ci copies and were not duplicated |
| Archived Python tests | `candidates/python-tests/_load.py` re-pointed at the archive layout (the one non-verbatim archived file, declared in its header); same 11/1 result |
| Docs governance | `node scripts/check-docs-governance.mjs` green |

## Per-issue findings

Detail, evidence and the full correction lists live in `issues/<n>-*.md`. Dispositions: **superseded** (shipped
or ruled), **adapt** (idea survives, pack is wrong in parts), **adopt** (pack right as written), **decline**.

| Issue | Bundle claim | Live state | Disposition | Startable now |
| --- | --- | --- | --- | --- |
| `#2256` CF-02 | "enums, mapping, columns exist in the scaffold; remaining work is intake/API stamping" | Live, not scaffold. `producerOverride` / `producedByPrincipalId` exist on `CaptureIntakeService` with zero callers, while current MCP authentication has no server-owned `AgentProfile` binding | adapt (blocked) | record and implement authenticated API-key/stdio-to-`AgentProfile` binding first; unbound MCP stays `Human` + null |
| `#2257` CF-03 | state machine first; job/run tables absent | Tables absent is right; the live comment makes the canonical policy-snapshot digest the first freeze; candidate forks the shipped `ProcessingJobState`; the proposed schema/fixture/checker landed through merged PR `#2371` and pass on this rebased head, but remain draft inputs rather than an accepted contract | adapt | policy snapshot record + canonical serializer + digest tests, reconciling the draft inputs with the live issue and ADR before freezing them |
| `#2276` CF-23 | interface corrected; streaming/quotas/dedupe/migration missing | Correct, plus the by-reference read asked for on 2026-08-30 is missing; `ArtefactService.CreateAsync` buffers the whole upload before the quota check; no dedupe | adapt | schema + fake + contract suite — the only CF v0.4 issue not behind `#2345` / `#2347` |
| `#2089` capture links | Capture, Representation, EvidenceAnchor must land first | Capture landed; the other two are enums only; `#2345` / `#2347` are new blockers; frontend root and per-board realtime misstated | adapt (blocked) | contract-only decision PR (union at Capture + Card, reuse `CaptureUserDisposition`, tombstone, correction by append) |
| `#2258` CF-04 | open work is hosts, handshake, cancellation, options schema, conformance | Accurate (zero hits for host/registry/spool); the `Depends on #2257` / `Unblocks #1429` lines are cyclic with `#1429` | adapt | in-process host + registry (no job/run or child process needed) |
| `#2259` CF-05 | adapter migration; delete request-type SQL routing | The future blocker is C#: `CaptureTriageService` gates LLM triage on `IsTranscriptSource`; changing routing safely needs CF-03/CF-06 to supply a selected capability and compatible representation, and the SQL predicates are also used by the CF-01 backfill store | adapt | extract the current predicate behind a named seam with byte-identical behavior; replace it only after CF-03/CF-06 |
| `#2260` CF-06 | backfill headers + orphan Capture creation | Half the repair: `SourceAsset.FromLegacyArtefact` has no caller, so `ArtefactExtraction` has no legal parent; header fields are non-nullable while legacy `Transcript` rows have none | adapt | settle the descriptor as a domain type (six invariants, four decisions) |
| `#2261` CF-07 | vectors mark zero-length spans invalid | The ruling is cost-free: the sole span writer guarantees `End > Start`; no validator bounds a span against its text | adopt the finding, adapt the vectors | freeze the kind → field matrix; CF-04 needs it too |
| `#1429` memory cap | depends on CF-03 and CF-04; port and repair `BoundedFilterProvider` | CF-03 not needed; `BoundedFilterProvider` has zero hits (a port from parked `#1417`, not a repair); PdfPig pinned 0.1.16 while the ADRs say 0.1.15; extraction still unwired | adapt | launcher contract + exit classification + fake launcher, as CF-04 supervisor internals |
| `#2087` item types | ready after contract freeze; depth 3; `work_parent_*` codes | ADR-0060 accepted; nothing shipped; depth convention unruled; snake_case codes map to 500 | adapt (blocked) | record whether depth 3 counts nodes or edges before the hierarchy contract/validator |
| `#2092` links | depends on `#2087` | No functional dependency (parent is a column, links a table); only the shared-file freeze | adapt | `WorkRelationType` + `WorkItemLink` + unique index + canonicalizer |
| `#2093` participants | blocked by the merged `#2240`; integer-minute estimates | `#2240` is open and forked, not merged; the estimate has no assignment dependency; unit unruled | adapt (blocked) | record time/minutes versus relative size, including bounds, before persisting an estimate |
| `#2094` custom fields | "ADR-0062 removed the blocker" | False: the same ratification deferred custom fields past ADR-0061 stage 2; definitions need a new proposal target type | decline (blocked) | none |
| `#2240` assignments v0.3 | active shared contract | Design fork confirmed: no assignee field anywhere (`AssigneeId`, `CardAssignment` zero hits); fork is not in `OUTSTANDING_TASKS.md` | adapt (blocked on the ruling) | on option A: the assignment contract |
| `#2330` CI-06 | shard ten named slow classes into seven shards; "6 vs 15.5 min" | Nine of ten class names do not exist; suite is `Taskdeck.Api.Tests` (176 classes); baseline is 7.2 vs 17.3 min mean; the issue body inherited the fictional names | adapt | inventory extractor + union checker; harness repair before any split |
| `#2334` CI-10 | nightly split; release double-qualifies | Confirmed; the double trigger is two workflows (`ci-release.yml` and `release-security.yml`); SBOM/provenance already ships; consolidation is phase 3 per `docs/ci/SMART_CI.md` | adapt | pure coordinator module + read-only measurement of the double build |
| `#2336` CI-12 | ship `ci-run.v1` schema + weekly report | Schema already on `main` and written by `evaluate-gate.mjs`; the bundle's same-name schema is incompatible; "missing receipt fails the gate" waits for shadow mode to end | superseded (schema) / adapt (fields) | extend the shipped schema and writer together |
| `#2339` CI-15 | new governance model + validator | Model already accepted (ADR-0066 invariant 9); nothing enforces it; validator has local-time and renewable-cap defects | adapt | schema + checker with an empty entries array |
| `#2241` SSE | needs a real parser, transport, fallback, endpoint | All four shipped (`OpenAiCompatibleLlmProvider.StreamAsync`, byte-bounded lines, `[DONE]`, usage, fallback; `ChatController.GetStream` flushes per event); no test under `/api/llm/chat/stream` | mostly superseded | the AC3 API contract test; live smoke needs a maintainer key |
| `#2236` refactoring | check in the ranker | PR `#2356` open and parked; `FirstRunBootstrapper.cs` is 1,620 lines not ~700; Paper views live under `views/paper/` | adapt | restart `#2356` on its recorded contract |
| `#2237` benchmarking | k6 + bundle budgets exist | Richer than claimed (summary contract, threshold checker, hand-set p95/p99); `benchmarks/`, `scripts/performance/`, `docs/performance/` absent; export `int.MaxValue` calls confirmed | adapt | result schema + scenario contract |
| `#1308` telemetry | Option B selected; blockers endpoint/retention/publication | Correct but undated; `docs/TELEMETRY.md` exists; Discussions still off; no feedback affordance, diagnostic bundle or beta badge in the frontend | adapt | three independent frontend PRs with no network path |
| `#1310` hosted launch kit | reframe from v0.3 to v0.4 | Already done (`#2242` split out); launch-date gate scrapped by q-6; MIT → GPL-3.0 drift (ADR-0050) missed | superseded + adapt | AC6 metrics baseline |
| `#2243` hosted epic | depends on `#1772`, `#2238`, `#2239`, `#1308`, `#1310` | `#2238` / `#2239` closed; omits `#1644` and `#1653`; "secrets rotatable" not achievable; five-rung ladder with v0.3 at stage 0; `#2012` is a two-part hard gate | adapt | cross-user adversarial isolation matrix |
| `#1772` private instance | complete `#2238` / `#2239` first | Both closed; container wrappers and the decrypt seam exist | superseded (prereqs) | stage-1 deployment skeleton + restore-drill evidence template, values blank at CL-1 |
| `#1131` CLI | add an `ops` group; exit codes; route authorization | `--backup` / `--restore` / `--verify-connectors` exist pre-host by design; exit codes 0/1/2 published; the real bar is `AuthorizationService.CanWriteBoardAsync` | decline (ops group) / adapt | none — blocked on the (a)/(b) product decision |
| `#1309` MCP | substantially landed; narrow to distribution | AC1/AC2/AC4 merged; AC3 mechanism-only by decision; body still advertises three closed defects as live | superseded + adapt | body reconciliation + four LOW residuals |
| `#2185` archive no-op | fixed in PR `#2222` | Fixed in PR `#2216`; both review residuals reproduce; `ArchiveItem` has zero production callers | adapt | API-level approve → execute persisted-archive test |
| `#2193` partial dates | fixed in PR `#2214`; add Dec/Jan and RFC822 tests | Fixed in PR `#2206`; December tests exist; culture already invariant | adapt (half superseded) | January-reference mirror + same-day boundary tests |
| `#2235` spring clean | after-cutover dead-file inventory | The live checklist is eight doc/issue/ADR items due by the tag; `docs/STATUS.md` does not mention the recovery capability | decline (framing) | STATUS.md recovery lines, then issue hygiene |
| `#2242` downloadable kit | checksums, signatures, SBOM | `LAUNCH_KIT.md` absent; signing does not exist (RT-1/2/3 open) | adapt | create the file with a claim → evidence ledger |
| `#1644` token storage | not in the bundle | `tokenStorage.ts` unchanged; single read seam in `http.ts`; the hosted blueprint omits the browser session model | adopt (issue as-is) | ADR draft; `#2350` first |
| `#2351` unexpected failures | not in the bundle | Already specifies content-free codes and a surface inventory; the only bundle contribution (`OpsCommandExitCodes.cs`) conflicts with the shipped contract | decline (no file) | — |

## Startable now (no DI, migration or UI; no open ruling)

| Issue | Slice | Why it is safe |
| --- | --- | --- |
| `#2276` CF-23 | `IBlobStore` schema + fake + contract suite | Only CF v0.4 issue not behind `#2345` / `#2347`; contract-only |
| `#2261` CF-07 | kind → field matrix ruling + bounding rule | Cost-free ruling; unblocks CF-04 conformance items too |
| `#2258` CF-04 | in-process host + registry | Needs no job/run and no child process |
| `#2259` CF-05 | extract the existing triage eligibility predicate behind a named seam, with no behavior change | Keeps `IsTranscriptSource` until CF-03/CF-06 provide a selected capability and compatible representation |
| `#2330` CI-06 | test-inventory extractor + union checker | Corrects the fictional names before anyone shards |
| `#2339` CI-15 | quarantine schema + checker, empty entries | Enforces already-accepted policy |
| `#2241` | AC3 API contract test | Test-only; the provider already streams |
| `#1308` | feedback-URL builder, diagnostic bundle, beta badge | Frontend, no network path, ruling already made |
| `#2242` | `docs/product/LAUNCH_KIT.md` with a claim → evidence ledger | Docs; signing stays visibly unclaimed |
| `#2185` / `#2193` | the residual tests | Primary fixes landed |

Not startable without a ruling: `#2240` (assignment substrate fork), `#2094` (deferred by ratification),
`#1131` ((a)/(b) product decision), CF-02/CF-03 (behind `#2345` / `#2347`).

## Issue comments posted (2026-09-02)

One comment per open target issue, each linking its curated file (32 posted; none on the closed `#2255`, `#2238`,
`#2239` or on `#2351`, which the bundle adds nothing to).

| Issue | Comment |
| --- | --- |
| `#1131` | [issuecomment-5503772981](https://github.com/Chris0Jeky/Taskdeck/issues/1131#issuecomment-5503772981) |
| `#1308` | [issuecomment-5503773270](https://github.com/Chris0Jeky/Taskdeck/issues/1308#issuecomment-5503773270) |
| `#1309` | [issuecomment-5503773559](https://github.com/Chris0Jeky/Taskdeck/issues/1309#issuecomment-5503773559) |
| `#1310` | [issuecomment-5503773831](https://github.com/Chris0Jeky/Taskdeck/issues/1310#issuecomment-5503773831) |
| `#1429` | [issuecomment-5503774105](https://github.com/Chris0Jeky/Taskdeck/issues/1429#issuecomment-5503774105) |
| `#1644` | [issuecomment-5503774400](https://github.com/Chris0Jeky/Taskdeck/issues/1644#issuecomment-5503774400) |
| `#1772` | [issuecomment-5503774686](https://github.com/Chris0Jeky/Taskdeck/issues/1772#issuecomment-5503774686) |
| `#2087` | [issuecomment-5503774968](https://github.com/Chris0Jeky/Taskdeck/issues/2087#issuecomment-5503774968) |
| `#2089` | [issuecomment-5503775304](https://github.com/Chris0Jeky/Taskdeck/issues/2089#issuecomment-5503775304) |
| `#2092` | [issuecomment-5503775616](https://github.com/Chris0Jeky/Taskdeck/issues/2092#issuecomment-5503775616) |
| `#2093` | [issuecomment-5503775930](https://github.com/Chris0Jeky/Taskdeck/issues/2093#issuecomment-5503775930) |
| `#2094` | [issuecomment-5503776193](https://github.com/Chris0Jeky/Taskdeck/issues/2094#issuecomment-5503776193) |
| `#2185` | [issuecomment-5503776475](https://github.com/Chris0Jeky/Taskdeck/issues/2185#issuecomment-5503776475) |
| `#2193` | [issuecomment-5503776742](https://github.com/Chris0Jeky/Taskdeck/issues/2193#issuecomment-5503776742) |
| `#2235` | [issuecomment-5503777053](https://github.com/Chris0Jeky/Taskdeck/issues/2235#issuecomment-5503777053) |
| `#2236` | [issuecomment-5503777363](https://github.com/Chris0Jeky/Taskdeck/issues/2236#issuecomment-5503777363) |
| `#2237` | [issuecomment-5503777675](https://github.com/Chris0Jeky/Taskdeck/issues/2237#issuecomment-5503777675) |
| `#2240` | [issuecomment-5503777970](https://github.com/Chris0Jeky/Taskdeck/issues/2240#issuecomment-5503777970) |
| `#2241` | [issuecomment-5503778289](https://github.com/Chris0Jeky/Taskdeck/issues/2241#issuecomment-5503778289) |
| `#2242` | [issuecomment-5503778586](https://github.com/Chris0Jeky/Taskdeck/issues/2242#issuecomment-5503778586) |
| `#2243` | [issuecomment-5503778931](https://github.com/Chris0Jeky/Taskdeck/issues/2243#issuecomment-5503778931) |
| `#2256` | [issuecomment-5503779257](https://github.com/Chris0Jeky/Taskdeck/issues/2256#issuecomment-5503779257) |
| `#2257` | [issuecomment-5503779565](https://github.com/Chris0Jeky/Taskdeck/issues/2257#issuecomment-5503779565) |
| `#2258` | [issuecomment-5503779888](https://github.com/Chris0Jeky/Taskdeck/issues/2258#issuecomment-5503779888) |
| `#2259` | [issuecomment-5503780167](https://github.com/Chris0Jeky/Taskdeck/issues/2259#issuecomment-5503780167) |
| `#2260` | [issuecomment-5503780498](https://github.com/Chris0Jeky/Taskdeck/issues/2260#issuecomment-5503780498) |
| `#2261` | [issuecomment-5503780805](https://github.com/Chris0Jeky/Taskdeck/issues/2261#issuecomment-5503780805) |
| `#2276` | [issuecomment-5503781109](https://github.com/Chris0Jeky/Taskdeck/issues/2276#issuecomment-5503781109) |
| `#2330` | [issuecomment-5503781428](https://github.com/Chris0Jeky/Taskdeck/issues/2330#issuecomment-5503781428) |
| `#2334` | [issuecomment-5503781731](https://github.com/Chris0Jeky/Taskdeck/issues/2334#issuecomment-5503781731) |
| `#2336` | [issuecomment-5503782034](https://github.com/Chris0Jeky/Taskdeck/issues/2336#issuecomment-5503782034) |
| `#2339` | [issuecomment-5503782331](https://github.com/Chris0Jeky/Taskdeck/issues/2339#issuecomment-5503782331) |

## Candidate-code admission contract

Unchanged from `RECONCILIATION.md`; restated in `candidates/README.md` next to the known-defects table.

## NOT verified and retained human gates

- No candidate was built into Taskdeck or executed against Taskdeck; the C# compile and Python suite prove
  internal consistency only.
- No `dotnet test`, `vitest` or `node --test` run backs the curated files' proving commands; those are
  proposals for the adopting PR.
- Docker is unavailable here: the container backup/restore smoke and worker-containment drills were read,
  not run.
- PR diffs for the delivered lanes were not read end to end; delivery claims come from current `main`
  source, the runbooks and issue comments.
- Whether PdfPig 0.1.16 still hard-codes `DefaultFilterProvider` was not checked against PdfPig source.
- The "every persisted transcript span has `End > Start`" finding is derived from the sole writer, not from
  auditing a database.
- No Project v2 state, milestone assignment, issue body or label was changed; no telemetry, release,
  signing, deployment, registration or visibility decision was inferred.
- `OUTSTANDING_TASKS.md`: all 42 open checkbox rows remain open; the `#2240` design fork is not among them
  and probably should be (recorded in `issues/2240-*.md`, not added here).
