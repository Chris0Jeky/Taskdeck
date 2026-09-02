# Worker containment blueprint

> **Validated 2026-09-02 against `main` `de488fea0`.**
> - **Nothing in §2–§7 exists yet.** Grepped across `backend/`: `IProcessorHost`, `ProcessorRegistry` and `IProcessorRegistry` have **zero hits**; `spool` appears only as the two scheme constants in `Application/Processing/Protocol/WorkerProtocol.cs` and one comment in `IBlobStore.cs`. There is no supervisor, no spool directory, no session secret and no conformance harness. What *does* ship is the typed contract: `WorkerProtocol` (`Version = 1`, `Stability = "v1-alpha"`), `WorkerProtocolValidator`, `WorkerProtocolErrorCodes`, `ProcessorManifest` + `ProcessorManifestValidator` + the embedded `processor-manifest.v1.schema.json`, and `ProcessingCapability`. Read the blueprint as the design for CF-04 `#2258`, not as a description of `main`.
> - **§5's transport rules are already enforced in code, and more strictly than the text implies.** `ValidateRunParams` rejects any content handle that is not `spool://` or `content://`, an unknown or duplicated input id, a source-asset or representation input without a 64-hex `sha256` and a positive `byteSize`, and non-positive limits. Manifest enum tokens are matched exactly by `StrictKebabCaseEnumConverterFactory` (`"SIDECAR"` and `1` are rejected), and unknown manifest members fail at parse time. §5's "capability options validated against declared contract" is the exception: `ProcessorCapabilityContract.OptionsSchema` is a declared string that **nothing evaluates at runtime**, and whether to take a JSON-schema dependency is still an open decision on `#2258`.
> - **§8 lists twelve conformance items; the published spec lists eleven.** `docs/architecture/WORKER_PROTOCOL_V1.md` §6 is the authority. The extra item here is "describe/session proof", which the spec carries in §2 (security model) rather than in the numbered suite — a real gap in the spec's list, worth closing there rather than maintaining two numberings. The archived `docs-drafts/PROCESSOR_CONFORMANCE_CHECKLIST.md` is a *third* count (fourteen).
> - **§3's handshake requires a protocol change, not just host code.** Spec §3.1 defines `processor.describe` as "request → the manifest JSON object", and there is no `ProcessorDescribeParams` / `ProcessorDescribeResult` record in `WorkerProtocol.cs` at all — only the `DescribeMethod` constant. Carrying a challenge on the request and an HMAC proof on the response is a field addition, which v1-alpha explicitly permits; make it in the same PR as the supervisor so spec, record and validator move together.
> - **§7's memory cap has no protocol field, and that is correct.** `ProcessorRunLimits` is `(DeadlineUtc, MaxWallTimeMs, MaxOutputBytes)` — no memory limit. The cap is an OS/host setting (ADR-0048); putting it on the wire would let a processor negotiate its own containment. Do not "complete" the limits record from this blueprint.
> - **§7's exit classification is the unbuilt hard part.** The blueprint asks the host to distinguish "OS memory kill" from "protocol malformed" and "deadline exceeded". On `main` the only extraction outcome vocabulary is `ArtefactExtractionWarningCodes` (nine values: `no-text-layer`, `page-limit`, `character-limit`, `input-too-large`, `invalid-utf8`, `invalid-text`, `extractor-error`, `extractor-contract-error`, `extraction-timeout`) — there is **no memory-kill code**, and `ArtefactExtractionService.cs:132` already documents that conflating outcomes loses information. `#1429` must add one.
> - **§1's "PdfPig is the first contained sidecar" is right, but the in-process ceiling it assumes as defence in depth does not exist.** `BoundedFilterProvider`, `ExtractionMaxDecodedBytes` and `decoded-size-limit` all grep to zero hits in `backend/`; they are unmerged work on the parked PR `#1417`, with four open review findings. The only shipped bound is the permit gate `ArtefactExtractionGate` (`ExtractionMaxConcurrency`, default 2) plus the `ExtractionTimeoutSeconds` wall clock — and artefact extraction has **no production caller** at all (`IArtefactExtractionService` appears only at `Api/Extensions/ApplicationServiceRegistration.cs:95`).
> - **§9's evidence version has moved.** The receipt asks for OS/platform/runtime versions; add the PdfPig version explicitly. `backend/Directory.Packages.props:51` pins **0.1.16**, while ADR-0047 and ADR-0048 describe **0.1.15**'s hard-coded `DefaultFilterProvider.Instance` xref path in the present tense (`docs/STATUS.md:139` records the drift, `#1429`'s 2026-08-26 comment asks for re-verification, and it has not been done).
>
> The body below is the bundle text, unedited.

## 1. Boundary

A processor is untrusted with respect to resource use and output correctness, even when its code ships with Taskdeck. It never receives domain mutation capabilities.

```text
Taskdeck runner
  → processor host registry
     → in-process host OR supervised sidecar OR remote host
        → protocol conformance
        → typed outputs
  → validate outputs
  → transactional run/representation commit
```

PdfPig is the first contained sidecar. WhisperX is the second materially different processor required before v1-alpha can become v1.

## 2. Supervisor state machine

```text
Created → Starting → Describing → Ready → Running → Completing → Stopped
                    ↘ Rejected
Running → Cancelling → GraceExpired → Killing → Stopped
Any non-terminal → Faulted → cleanup → Stopped
```

Every transition has one content-free reason code and timing. Only one job runs per process unless `concurrent-jobs` is explicitly declared and host-enforced.

## 3. Session handshake

- Host generates a random per-process secret and challenge.
- Secret is supplied through a protected environment variable or inherited handle, never JSON or command-line arguments.
- `processor.describe` includes the public challenge; response includes HMAC(secret, protocol version + challenge + processor identity).
- Host verifies with fixed-time comparison.
- Challenge is one-use; replayed proof is rejected.
- Secret/challenge/proof are never logged.

## 4. Content transport

Preferred order:

1. protected spool directory with random host-created filename and least privileges;
2. short-lived authenticated local content handle;
3. bounded stdin stream only for small inputs.

Never accept a user-supplied arbitrary path. Spool lifecycle:

- host creates;
- writes with declared-size ceiling and hash;
- grants worker read-only access;
- deletes after terminal state/cancellation;
- startup scavenger removes stale files older than a documented threshold.

## 5. Framing and limits

- JSON-RPC 2.0 over a clearly specified frame (newline-delimited or length-prefixed; use the existing spec).
- Maximum frame, output count, segment/region/candidate count, warning count and total output bytes.
- Strict enum tokens and null-entry rejection.
- Capability options validated against declared contract.
- Stderr is diagnostics only, bounded and content-free; stdout is protocol only.

## 6. Cancellation and termination

1. Runner sends protocol cancel.
2. Wait configurable grace period.
3. Close stdin/transport.
4. Terminate process tree.
5. Escalate hard kill if still alive.
6. Await exit and cleanup spool/log buffers.
7. Persist one run outcome; do not double-record timeout + kill.

Cancellation by user, deadline, cost ceiling, memory cap and host shutdown use distinct reason codes but the same cleanup path.

## 7. OS containment

### Windows

- Create suspended or assign immediately to a Job Object before untrusted work.
- `KILL_ON_JOB_CLOSE` and process-tree limits.
- Job memory limit as the hard boundary.
- Optional active-process limit.
- Capture exit reason and peak memory without source content.

### Linux/container

- cgroup v2/container memory maximum, swap policy and pids limit.
- read-only filesystem except spool.
- no network for local-only processor.
- dropped capabilities and non-root user.
- kill entire cgroup/process group.

The host must distinguish “OS memory kill” from “protocol malformed” and “deadline exceeded” where the platform exposes enough evidence.

## 8. Conformance suite

Every processor passes:

1. manifest/schema validation;
2. describe/session proof;
3. declared capability/MIME enforcement;
4. options validation;
5. deterministic fixture hash;
6. cancellation and process-tree termination;
7. deadline/output-size enforcement;
8. stderr policy;
9. malformed output rejection;
10. local-only network denial;
11. crash recovery and idempotent replay;
12. processor/model/config provenance round-trip.

A deliberately bad fixture processor is required; a suite that only tests conformant implementations cannot prove rejection.

## 9. Decompression-bomb acceptance receipt

Record:

- fixture ID/hash and why it exercises the xref-stream path;
- configured worker memory cap;
- host peak memory and worker peak memory;
- OS/platform/runtime versions;
- observed exit classification;
- exactly one failed run/warning row;
- capture and source asset still readable;
- no leaked worker/process/spool file;
- repeated run result and host health.

Do not include the malicious payload itself in logs or issue comments beyond the controlled fixture repository path/hash.
