# Worker containment blueprint

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
