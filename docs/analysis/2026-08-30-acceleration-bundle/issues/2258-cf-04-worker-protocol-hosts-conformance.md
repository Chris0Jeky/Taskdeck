# CF-04 — Worker Protocol v1 + processor manifest/registry + conformance suite (#2258)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue (body checklist plus its four 2026-08-30 comments), ADR-0065 §Decision 10 and §Amendments, `docs/architecture/WORKER_PROTOCOL_V1.md` and `docs/STATUS.md` win. Corrections to the bundle's issue pack are in the last section.

## Outcome

The typed protocol exists and validates; **nothing runs it**. This issue builds the one host
abstraction, the one sidecar supervisor and the conformance suite that together turn Worker Protocol
v1-alpha into a boundary a processor is actually contained by — and it is the same supervisor
`#1429` (PdfPig) and CF-14 (WhisperX) must use. The protocol becomes v1 only when those two
materially different processors both pass the suite.

## Live dependencies (verified 2026-09-02)

| Issue | State | Relationship | Note |
| --- | --- | --- | --- |
| CF-03 `#2257` | open | predecessor **for two conformance items only** | Items 9 (crash recovery / idempotent replay) and 10 (provenance round-trip into `ProcessingRun`) name a job and a run receipt. Grepped `ProcessingJob`/`ProcessingRun` as types across `backend/src`: only `Domain/Enums/ProcessingJobState.cs`. Items 1–8 and 11 need no job |
| `#1429` PdfPig containment | open | **mutual** — first sidecar and first proof | CF-04's acceptance box 1 is `#1429`'s containment proof; `#1429`'s 2026-08-30 comment makes CF-04's supervisor its transport. See correction 2 for how the cycle resolves |
| CF-14 `#2268` WhisperX | open (v0.5) | co-condition for v1 | `WorkerProtocol.Stability = "v1-alpha"` stays draft until PdfPig **and** WhisperX conform |
| CF-06 `#2260` | open | consumer | A `representation` input and a `ProcessorRepresentationOutput` have no store to resolve to or land in yet |
| CF-07 `#2261` | open | consumer, and owns one open protocol question | `#2261`'s 2026-09-02 comment asks CF-07 to rule on zero-length `TimeRange` and on where duration bounding lives; the protocol validator is the other candidate home |
| CF-18 `#2272`, GEN-03 `#1317` | open | consumers | Later processors under the same host |

Grepped `IProcessorHost`, `ProcessorRegistry`, `IProcessorRegistry` across `backend/`: **zero hits**.
Grepped `spool` across `backend/src`: only the two scheme constants in `WorkerProtocol.cs` and one
comment in `IBlobStore.cs`. There is no supervisor, no spool directory, no session secret and no
conformance harness on `main`.

## Child slices (one PR each, in order)

The live issue body already proposes (a)–(f); this table keeps those identifiers and adds ordering
and startability.

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CF04-a-host-registry` | `IProcessorHost`, an in-process host, and a registry that loads, validates and matches manifests (`plaintext`, `pdfpig`, `mock`) with health | — | implementation | **Yes — start here.** `ProcessorManifest` + `ProcessorManifestValidator` + the embedded schema already exist; the two in-process extractors (`PlainTextArtefactTextExtractor`, `PdfPigArtefactTextExtractor`) already exist and are resolved as `IEnumerable<IArtefactTextExtractor>`. Nothing in this slice needs a job, a run or a child process |
| `CF04-b-supervisor` | Child-process launch, per-process session secret + `processor.describe` challenge/proof, stdio framing, spool directory, cancellation grace, process-tree kill | a | implementation | No — needs the host abstraction it plugs into. It also needs a protocol addition (see Architecture: `processor.describe` has no params/result record today) |
| `CF04-c-limits-security` | Frame/output/segment/region/candidate/warning caps, network denial for `networkRequired: false`, stderr content policy, spool scavenger | b | implementation | No |
| `CF04-d-conformance` | The suite plus a conformant `mock` and a **deliberately non-conformant** fixture processor rejected at registration | c | implementation | Items 1–8 and 11 yes after c; items 9–10 no — they need CF-03 |
| `CF04-e-pdfpig` | `#1429` delivered through this supervisor; protocol status stays alpha until CF-14 also passes | d, `#1429` MEM-1..3 | implementation | No |
| `CF04-f-docs-discovery` | `docs/platform/CONFIGURATION_REFERENCE.md` sidecar discovery/enablement; default install requires no sidecar | e | docs + config | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Wire contract | `WorkerProtocol` (`Version = 1`, `Stability = "v1-alpha"`), `JsonRpcRequest/Notification/Response`, `JsonRpcError`, `ProcessorErrorData` | **exists** | `backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs` (961 lines) |
| Run contract | `ProcessorRunParams(ProtocolVersion, Capability, Inputs, Options, Limits)`, `ProcessorRunInput(Kind, Id, MediaType, ContentHandle, Sha256, ByteSize, Role)`, `ProcessorRunOptions(Language, QualityTier, Capability)`, `ProcessorRunLimits(DeadlineUtc, MaxWallTimeMs, MaxOutputBytes)` | **exists** | Typed multiple inputs; `Kind` ∈ `source-asset` \| `representation` \| `context-snapshot` |
| Result contract | `ProcessorRunResult(Status, Processor, Outputs, Warnings, Usage)`, `ProcessorIdentity(Id, Version, Model, ConfigurationHash)`, the three output families, `ProcessorOutputJsonConverter` (order-independent `type` dispatch), `ProcessorUnknownOutput` | **exists** | A completed run must carry a `ConfigurationHash` and at least one representation or candidate batch |
| Structural validation | `WorkerProtocolValidator`: `ValidateResponseEnvelope`, `ValidateNotificationEnvelope`, `ValidateProgress`, `ValidateCancel`, `ValidateRunParams`, `ValidateResult(result)` and `ValidateResult(result, request)` | **exists** | Enum-valued strings matched against `Enum.GetNames`; null array entries rejected; a candidate batch only from `semantic.extract`; evidence `representationId` must be one of *this run's* representation inputs |
| Error codes | `WorkerProtocolErrorCodes` −32000/−32010/−32011/−32012/−32020/−32021/−32022/−32030/−32040 | **exists** | Wire codes, distinct from `ErrorCodes` (the domain's PascalCase constants) |
| Manifest | `ProcessorManifest`, `ProcessorCapabilityContract(Outputs, OutputSchemas, OptionsSchema)`, `ProcessorManifestValidator`, `ProcessorManifestResources` (schema + WhisperX example embedded and read by tests), `StrictKebabCaseEnumConverterFactory` | **exists** | `Processing/ProcessorManifest.cs`, `ProcessorManifestValidator.cs`, `Processing/Schemas/`. Unknown members rejected at parse time (`JsonUnmappedMemberHandling.Disallow`) |
| Capability vocabulary and the externalizable rule | `ProcessingCapability` (13 constants + `RepresentationProducing` / `Externalizable` / `InProcessOnly`) | **exists**, enforced | `ProcessorManifestValidator.ValidateCapabilities` rejects a sidecar/remote manifest declaring `context.resolve`, `change.plan` or `change.verify` |
| Execution / locality vocabulary | `ProcessorExecutionMode` (in-process · sidecar · remote), `ProcessorLocality`, `ProcessorGpuRequirement`, `ProcessorCostModelType` | **exists** | `Domain/Enums/` |
| Existing processors to register | `PlainTextArtefactTextExtractor` (Api DI), `PdfPigArtefactTextExtractor` (Infrastructure DI), behind `IArtefactTextExtractor` / `ArtefactExtractionService` | **exists** | First-match-on-MIME selection today; the registry replaces that selection, not the extractors |
| Worker liveness | `WorkerHeartbeatRegistry` (`Api/Workers/`), read by `/health/ready` | **exists** | Reuse for host/sidecar readiness — ADR-0048 is one supervisor |
| `IProcessorHost`, in-process host, registry | — | **missing** | Grepped; no hits anywhere in `backend/` |
| Sidecar supervisor, spool directory, session secret/challenge/proof, cancellation grace setting, `concurrent-jobs` honouring | — | **missing** | Grepped `spool`, `SessionSecret`, `concurrent-jobs`: only the protocol constants and spec prose |
| `processor.describe` params / result records | only `WorkerProtocol.DescribeMethod` | **missing** | There is no `ProcessorDescribeParams`/`ProcessorDescribeResult` type. The session handshake the checklist requires therefore needs a **protocol addition**, which v1-alpha explicitly permits |
| Options-schema evaluation | `ProcessorCapabilityContract.OptionsSchema` is a declared string | **exists, unevaluated** | The schema file is published, not evaluated at runtime. The JSON-schema-library decision is still open in the issue checklist |
| Conformance suite | — | **missing** | Only `Taskdeck.Application.Tests/Processing/{ProcessorManifestValidatorTests, WorkerProtocolSerializationTests}` exist |

**The handshake is a protocol change, not just host code.** `docs/architecture/WORKER_PROTOCOL_V1.md`
§3.1 says only "the result is the manifest JSON object". Adding a challenge to the request and a
proof to the response is a field addition to `processor.describe`; make it in the same PR as the
supervisor so the spec, the typed record and the validator move together.

## Implementation plan

**Preflight.** Read `#2258`'s body checklist and all four comments — the last one (2026-08-30 15:34)
adds three v1-hardening items that are *not* in the body: per-capability **input** shapes in
`capabilityContracts`, evidence `TextSpan` bounding against the stored representation at persistence
time, and the output family of `content.inspect`. Read `WORKER_PROTOCOL_V1.md` §2, §3, §6 and §7
and ADR-0065 §Decision 10.

**Sequence.** a → b → c → d, then e (`#1429`) and f. Slices a–d are the shippable core; the protocol
cannot leave alpha until e and CF-14 both pass.

**Producer-owned paths** (to be created): `backend/src/Taskdeck.Application/Processing/Hosting/`,
`backend/src/Taskdeck.Infrastructure/Processing/` (the child-process supervisor — process launch,
Job Object / cgroup and filesystem access are Infrastructure concerns, not Application),
`backend/tests/Taskdeck.Application.Tests/Processing/`, `backend/tests/Taskdeck.Integration.Tests/Processing/`.

**Integration-owner seams:** `Processing/Protocol/WorkerProtocol.cs` (the describe records),
`docs/architecture/WORKER_PROTOCOL_V1.md`, `Api/Extensions/ApplicationServiceRegistration.cs`,
`Infrastructure/DependencyInjection.cs`, `Api/Workers/WorkerHeartbeatRegistry.cs`,
`docs/platform/CONFIGURATION_REFERENCE.md`, `docs/STATUS.md`.

**Rollout / rollback.** The registry and in-process host can default **on** (they replace
first-match-on-MIME with declared matching and change no observable behaviour). The sidecar path
ships **off**; the default install must boot with no sidecar present. Never add a silent
"spawn failed → parse in process" fallback once the contained path is enabled — that is the exact
bypass ADR-0048 exists to prevent; record the fail-closed choice in the PR (`#1429` names it too).

**Definition of done.** Items 1–8 and 11 of spec §6 green for `plaintext`, `pdfpig` and `mock`; a
deliberately non-conformant fixture rejected at registration; items 9–10 explicitly deferred to the
PR that lands after CF-03, named in the issue rather than silently skipped.

## Test plan

- [ ] Application: a manifest declaring an unknown capability, a duplicate capability, a
  contract-less capability, or (as a sidecar) an in-process-only capability is rejected — extend
  `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~ProcessorManifestValidator"`
- [ ] Application: the registry selects a processor by (capability, media type) and refuses when no
  manifest accepts the pair, with the `-32010` / `-32011` distinction preserved
- [ ] Application: `processor.describe` disagreeing with the registered manifest fails registration
- [ ] Application: session proof — a correct proof is accepted **once**, a replay of the same
  challenge is rejected, a proof bound to a different processor id or protocol version is rejected,
  and the secret never appears in an envelope or a log line (assert over captured log output)
- [ ] Infrastructure: cancellation acknowledged inside the grace period; ignored cancellation escalates
  to process-tree termination; a grandchild process does not survive
- [ ] Infrastructure: an input reaches the child only as `spool://` or `content://`; a run whose input
  handle is a filesystem path is rejected before launch (already `ValidateRunParams`, prove it end to end)
- [ ] Infrastructure: spool files are deleted on every terminal state including cancellation and crash;
  a startup scavenger removes stale ones
- [ ] Infrastructure: a local-only manifest's process cannot reach the network (or the platform gap is
  documented and xfail-tracked)
- [ ] Application: stderr is bounded and content-free; a fixture that writes user text to stderr fails conformance
- [ ] Application: malformed results — unknown `type`, out-of-order members, null array entries,
  oversized frame, a candidate batch from a non-`semantic.extract` run — are all rejected before persistence
- [ ] Application: a fixed fixture produces a deterministic result hash across runs
- [ ] Api: `/health/ready` reflects host/sidecar readiness through `WorkerHeartbeatRegistry`; a default
  install with no sidecar is ready
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1` — the supervisor must not put process/filesystem code in Application
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Describe times out, or the child dies before the handshake — one rejected registration, no orphan.
- Bad or replayed proof; a proof computed over a different canonical protocol-version string (see
  Candidate defects — `1` vs `"v1-alpha"` is currently unspecified).
- Protocol version mismatch (`-32040`) against a host that must still tolerate unknown *members*.
- An output union whose `type` member is not first — already handled by `ProcessorOutputJsonConverter`;
  keep a regression case.
- Oversized frame, stderr flood, a partial JSON frame at EOF.
- Cancel ignored; cancel racing a natural completion — one terminal outcome, one reason code, never
  both a timeout and a kill recorded.
- A child that spawns grandchildren; on Windows, antivirus-induced launch delay.
- Non-conformant options against a declared `optionsSchema` while no schema evaluator is chosen.
- `concurrent-jobs` declared by a manifest the host does not yet honour — must be a refusal, not silent serialization.
- `content.inspect` output family is undecided (issue comment 4): today it would have to return a
  `representation`, which validates but misdescribes a diagnostics-plus-structure result.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Blueprint | `docs/analysis/2026-08-30-acceleration-bundle/architecture/WORKER_CONTAINMENT_BLUEPRINT.md` | §2 supervisor state machine, §4 content-transport preference order, §6 seven-step cancellation ladder, §7 per-OS containment, §9 the bomb acceptance receipt | Read its 2026-09-02 validation preface. Its §8 lists **12** conformance items where the spec lists 11 — the extra is the session proof, which lives in spec §2, not §6 |
| C# candidate | `.../candidates/dotnet/WorkerSessionProof.cs` (+ `candidates/dotnet/tests/WorkerSessionProofTests.cs`) | HMAC-SHA256 over `protocolVersion \n challenge \n processorId`, base64url, fixed-time compare, env-var secret name | Reference only. Binds a protocol version whose canonical form is unspecified; provides **no** replay rejection despite the blueprint requiring a one-use challenge; zeroes byte arrays while leaking the same material in managed strings; one test. See Candidate defects |
| Test vectors | `.../testing/test-vectors/worker-protocol-valid.sample.json`, `worker-protocol-invalid.sample.json` | The *shape* of a conformance fixture pair and five adversarial names (numeric enum, null output member, arbitrary path, oversized frame, wrong session proof) | The "valid" sample does **not** validate against `ValidateRunParams` — six field-level divergences, listed in correction 5. Rewrite before use |
| Diagram | `.../diagrams/worker-containment.svg` (`.dot` beside it) | runner → registry → host → {in-process, supervisor, remote} → OS boundary → validator → transactional commit | Explanatory; accurate to the intended target. It draws the commit step, which is CF-03's |
| Docs draft | `.../docs-drafts/PROCESSOR_CONFORMANCE_CHECKLIST.md` | A 14-item checkbox form of the suite | **Adapt, do not adopt as a doc.** It is a superset of spec §6: it adds spool/process cleanup after every terminal state, and "default install works with the processor disabled" — both worth adding to §6 rather than publishing a parallel checklist |
| Testing doc | `.../testing/EXPECTED_ERROR_CODES.md`, `ADVERSARIAL_CASES.md` | Cross-check for the `-320xx` mapping and the adversarial fixture list | Generic floor; the shipped `WorkerProtocolErrorCodes` is the authority |

## Corrections to the bundle

1. **Pack's "Reconciled current state"** — "Protocol records, schema, strict enum validation and
   several review fixes are merged. Open work is host implementation, session handshake,
   cancellation/termination, option-schema decision, reserved concurrency behavior, limits and
   conformance." **Accurate and still true on `main`.** Keep it. It is the pack's best line.
2. **Pack's dependency metadata is cyclic.** `2258.md` says *Unblocks: #1429*; `1429.md` says
   *Depends on: #2257, #2258 / Unblocks: #2258*. Both cannot hold. The resolution on `main`:
   CF-04 (a)–(d) precede `#1429`'s MEM-4/MEM-5 (PdfPig adapter, bomb proof), while `#1429`'s
   MEM-1..MEM-3 (launcher contract, Windows Job Object, Linux cgroup) are supervisor internals that
   land *inside* CF04-b/c. There is one supervisor and one order; say so in both issues.
3. **Pack says CF-04 depends on `#2257`.** Over-stated. Only conformance items 9 (crash recovery /
   idempotent replay) and 10 (provenance round-trip into `ProcessingRun`) need CF-03; items 1–8 and 11
   need nothing beyond what is on `main`. Blocking the whole umbrella on CF-03 costs the one slice
   (a) that is startable today.
4. **Pack's recommended children (a)–(f) are already in the live issue body** (added by the
   2026-08-30 reconciliation pass). The pack neither adds nor contradicts them; it is not new
   information, and the issue's own list is the authority.
5. **`worker-protocol-valid.sample.json` is not valid.** Checked field by field against
   `ProcessorRunParams` / `ValidateRunParams`: (i) `protocolVersion` is the string `"v1-alpha"` where
   the record takes `int` and the validator requires `1`; (ii) `capability` is `document.extract`,
   which is not in `ProcessingCapability.All` — the shipped spelling is `document.extract-text`;
   (iii) the input member is `type` where the record's member is `kind`; (iv) the input has no
   `contentHandle`, so it is rejected as "must be a host-issued spool:// or content:// handle";
   (v) it has no `sha256` and no `byteSize`, both required for a `source-asset` input; (vi)
   `deadlineUtc` and `maximumOutputBytes` sit on `params` where the record nests them under `limits`
   as `deadlineUtc` / `maxOutputBytes`. A conformance fixture that the shipped validator rejects is
   worse than no fixture.
6. **`worker-protocol-invalid.sample.json` mixes request and result mutations** over one base
   document (`outputs: [null]` is a *result* shape; the rest mutate a run request), and its
   `expect_error` values (`protocol_enum_invalid`, `protocol_output_null`, …) are snake_case tokens
   that exist nowhere in Taskdeck. The shipped validator returns human-readable path-prefixed
   strings, and the wire vocabulary is `WorkerProtocolErrorCodes` plus the spec's UPPER_SNAKE
   `errorCode`. Keep the five case *names* as the adversarial list; discard the codes.
7. **Pack's "avoid: protocol fixed before two different processors".** Correct and already binding —
   `WorkerProtocol.Stability = "v1-alpha"` and the spec header both say it. Not a risk to manage; a
   fact to preserve when writing the completion receipt.
8. **Pack's file-ownership globs omit the two files the work actually lands in.** It lists
   `backend/src/**/Processing/Protocol/**` and `backend/src/**/ProcessorHost*` but not
   `backend/src/Taskdeck.Infrastructure/` (where a process supervisor must live to keep
   `Architecture.Tests` green) nor `Api/Workers/WorkerHeartbeatRegistry.cs` (the readiness seam ADR-0048
   says to reuse).
9. **Pack's suggested-image block** points at `../path/to/worker-containment.svg`; the bundle's
   issue-comment file points at `docs/architecture/diagrams/worker-containment.svg`, which does not
   exist. The diagram is archived at
   `docs/analysis/2026-08-30-acceleration-bundle/diagrams/worker-containment.svg`; a relative path in
   a GitHub issue body does not resolve — link the repo path or nothing.
10. **Vocabulary check:** clean. The pack uses `in-process` / `sidecar` / `remote` and the
    externalizable boundary exactly as shipped, and never says "Controlled".
