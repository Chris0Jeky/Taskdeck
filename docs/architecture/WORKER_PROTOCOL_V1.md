# Taskdeck Worker Protocol v1

Last Updated: 2026-08-30

**Status:** contract fixed by ADR-0065 §Decision 10 (scaffolded 2026-08-30); the supervisor, host and
conformance suite are CF-04 (`#2258`), with the ADR-0048 memory-capped extraction worker (`#1429`) as the
first sidecar. Typed shadow of this document:
`backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs`; manifest contract:
`backend/src/Taskdeck.Application/Processing/Schemas/processor-manifest.v1.schema.json` and
`ProcessorManifest` / `ProcessorManifestValidator` beside it. Origin: the 2026-08-30 planning pack's
proof of concept (`docs/analysis/2026-08-30-context-fabric/TASKDECK_WORKER_PROTOCOL_POC.md`).

The protocol separates **what a processor can do** (its manifest) from **how it is reached**
(transport). The desktop uses supervised JSON-RPC 2.0 over stdio; hosted deployments may map the same
envelopes onto a durable queue and object storage without changing a single field.

## 1. Roles

| Role | Owns |
| --- | --- |
| **Taskdeck host** (`IProcessorHost`, CF-04) | Process lifetime, the spool directory, deadlines, output caps, cancellation, network posture, provenance recording |
| **Processor** (sidecar or remote adapter) | Turning one input into one or more typed representations; reporting progress, usage and content-free warnings |
| **Manifest** | The processor's declared capabilities, accepted media types, resources, privacy class and cost model |

A processor **never** holds a domain mutation tool. It can produce representations; it cannot create,
move or edit work (GP-06, GP-09, ADR-0065 §Decision 9).

## 2. Security model

- Taskdeck starts the sidecar and owns its lifetime; an orphaned process is a host bug, not a feature.
- Each process receives a per-process random **session secret**; messages carrying the wrong secret are
  dropped and the process is terminated.
- Inputs are supplied through a Taskdeck-managed **spool directory** (`spool://<handle>`) or a
  short-lived authenticated **content handle** — never an arbitrary filesystem path the user supplied.
- The manifest declares required hosts; a processor whose manifest says `networkRequired: false` runs
  with the network denied where the platform allows it, and the conformance suite proves the denial.
- **stdout is protocol-only.** Diagnostics go to stderr and must be content-free (no user text, no
  audio bytes, no file names beyond the spool handle). A content-bearing stderr line fails conformance.
- Every result carries the processor id, version, model and configuration hash so provenance can name
  the engine that actually ran (the ADR-0045 honesty rule, extended to every modality).

## 3. Messages

All messages are JSON-RPC 2.0 objects, one per line (LF-terminated, UTF-8, no BOM) on the stdio
transport. Property names are camelCase; enumerations are kebab-case strings.

### 3.1 `processor.describe` (request → manifest)

The host may ask a running processor for its manifest to cross-check the registered one. The result is
the manifest JSON object; a mismatch with the registered manifest fails conformance.

### 3.2 `processor.run` (request)

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "method": "processor.run",
  "params": {
    "protocolVersion": 1,
    "capability": "audio.transcribe",
    "input": {
      "assetId": "7ec00b5e-7b9f-4ebd-8a31-f018340bb0aa",
      "mediaType": "audio/webm",
      "contentHandle": "spool://2b9e",
      "sha256": "<64 hex>",
      "byteSize": 2481000
    },
    "options": {
      "language": "auto",
      "qualityTier": "balanced",
      "wordTimestamps": false,
      "diarization": false,
      "maxSpeakers": null
    },
    "limits": {
      "deadlineUtc": "2026-08-30T03:00:00Z",
      "maxWallTimeMs": 600000,
      "maxOutputBytes": 5000000
    }
  }
}
```

`WorkerProtocolValidator.ValidateRunParams` rejects: a protocol version other than 1; a capability
outside `ProcessingCapability.All`; an empty asset id, media type or content handle; a digest that is
not 64 hex characters; a non-positive byte size; non-positive limits or speaker counts.

### 3.3 `processor.progress` (notification, processor → host)

```json
{
  "jsonrpc": "2.0",
  "method": "processor.progress",
  "params": { "jobId": "job-7f41", "phase": "transcribing", "fraction": 0.54, "messageCode": "audio.transcribing" }
}
```

`messageCode` is a stable, translatable code — never free text derived from content.

### 3.4 `processor.cancel` (request, host → processor)

`params: { "jobId": "job-7f41" }`. The processor must stop within its declared grace period and answer
the original `processor.run` with `status: "cancelled"`; the host terminates the process if it does not.

### 3.5 Result

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "result": {
    "status": "completed",
    "processor": { "id": "taskdeck.whisperx", "version": "1.0.0", "model": "large-v3-turbo", "configurationHash": "sha256:…" },
    "representations": [
      {
        "kind": "Transcript",
        "schemaVersion": 1,
        "language": "en",
        "text": "…",
        "segments": [
          { "charStart": 0, "charEnd": 76, "startMs": 4200, "endMs": 11840, "speakerLabel": "SPEAKER_00", "confidence": 0.93 }
        ]
      }
    ],
    "warnings": [],
    "usage": { "wallTimeMs": 18420, "audioDurationMs": 181000, "peakRamMb": 3210, "peakVramMb": 5740 }
  }
}
```

`status` is one of `completed | failed | cancelled`. A `completed` run emits at least one
representation. Segments are ordered by `charStart`, do not overlap, satisfy
`0 ≤ charStart ≤ charEnd ≤ text.length`, carry non-negative timestamps with `startMs ≤ endMs`, and a
confidence in `[0, 1]` when present (`WorkerProtocolValidator.ValidateResult`). Segment char offsets
are UTF-16 code units over `text` — the same unit the shipped transcript evidence spans use.

### 3.6 Failure

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "error": {
    "code": -32020,
    "message": "Processor could not allocate the requested model",
    "data": { "errorCode": "RESOURCE_EXHAUSTED", "retryable": true, "safeDetail": "Try a smaller model or CPU fallback." }
  }
}
```

`safeDetail` may name a model or a resource; it may never contain a fragment of the user's material.

## 4. Error codes (server-defined range `-32000..-32099`)

| Code | Constant | Meaning |
| --- | --- | --- |
| -32000 | `ProcessorFailure` | Unclassified processor failure |
| -32010 | `UnsupportedCapability` | Capability not declared by this processor |
| -32011 | `UnsupportedMediaType` | Input media type outside `accepts` |
| -32020 | `ResourceExhausted` | Model/RAM/VRAM could not be allocated (usually retryable on a smaller route) |
| -32021 | `DeadlineExceeded` | `limits.deadlineUtc` or `maxWallTimeMs` passed |
| -32022 | `OutputTooLarge` | Result would exceed `maxOutputBytes` |
| -32030 | `Cancelled` | Cancelled by the host |
| -32040 | `ProtocolVersionMismatch` | Processor cannot speak the requested version |

## 5. Manifest (v1)

See the JSON schema. Rules the schema cannot express, enforced by `ProcessorManifestValidator`:

- capabilities are drawn from `ProcessingCapability.All` and declared once;
- a `local` processor cannot declare `networkRequired: true` and cannot `execute: remote`;
- a `remote` processor must declare `networkRequired: true` **and** its `allowedHosts`;
- a processor with `networkRequired: false` cannot declare `allowedHosts`;
- `resources.gpu: none` cannot require VRAM; a `free-local` cost model cannot carry a unit price;
- data classes are `text | audio | image | document | metadata`; currency is a three-letter ISO code.

Example: `docs/analysis/2026-08-30-context-fabric/whisperx-processor.example.json` (validated by
`ProcessorManifestValidatorTests`).

## 6. Conformance tests (CF-04 — every processor must pass)

1. manifest/schema validation and `processor.describe` agreement;
2. declared MIME and capability enforcement (`-32010` / `-32011`);
3. cancellation and process termination within the grace period;
4. deadline and output-size limits (`-32021` / `-32022`);
5. no content written to stderr in normal operation;
6. deterministic result hashing for fixed fixtures and configuration;
7. malformed-result rejection (the validator above);
8. network denial for a local-only manifest;
9. crash recovery and idempotent replay (same job id, same result hash);
10. processor/model/configuration provenance round-trip into `ProcessingRun`.

## 7. Transport notes

- **stdio (desktop):** one JSON object per line; the host writes requests, reads results and
  notifications, and never interleaves two runs on one process unless the manifest declares
  `features: ["concurrent-jobs"]`.
- **queue (hosted, later):** the same `params` object is the job payload, `result`/`error` the
  completion record, and `processor.progress` a progress event; `contentHandle` resolves to an object
  store URL through `IBlobStore` (ADR-0061 stage 3 only).
