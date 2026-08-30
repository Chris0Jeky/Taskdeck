# Taskdeck Worker Protocol v1-alpha

Last Updated: 2026-08-30

**Status: draft (v1-alpha).** Scaffolded by ADR-0065 §Decision 10 on 2026-08-30 and restructured the
same day after the external audit of PR `#2280` (ADR-0065 §Amendments). The wire version is `1`; the
contract is **not fixed** until two materially different processors pass the CF-04 (`#2258`)
conformance suite — PdfPig through the memory-contained extraction worker (`#1429`) and WhisperX through
the sidecar path (CF-14 `#2268`). Until then field additions are expected; hosts tolerate unknown
members. Typed shadow of this document:
`backend/src/Taskdeck.Application/Processing/Protocol/WorkerProtocol.cs`; manifest contract:
`backend/src/Taskdeck.Application/Processing/Schemas/processor-manifest.v1.schema.json` with the
canonical example `whisperx-processor.example.json` beside it (both embedded in the assembly and read by
the tests) and `ProcessorManifest` / `ProcessorManifestValidator`. Origin: the 2026-08-30 planning pack's
proof of concept (`docs/analysis/2026-08-30-context-fabric/TASKDECK_WORKER_PROTOCOL_POC.md`).

The protocol separates **what a processor can do** (its manifest) from **how it is reached**
(transport). The desktop uses supervised JSON-RPC 2.0 over stdio; hosted deployments may map the same
envelopes onto a durable queue and object storage without changing a single field.

## 1. Roles and the externalizable boundary

| Role | Owns |
| --- | --- |
| **Taskdeck host** (`IProcessorHost`, CF-04) | Process lifetime, the spool directory, deadlines, output caps, cancellation, network posture, provenance recording |
| **Processor** (sidecar or remote adapter) | Turning typed inputs into typed outputs; reporting progress, usage and content-free diagnostics |
| **Manifest** | The processor's declared capabilities and their contracts, accepted media types, resources, privacy class and cost model |

A processor **never** holds a domain mutation tool. It can produce representations and candidates; it
cannot create, move or edit work (GP-06, GP-09, ADR-0065 §Decision 9).

A sidecar or remote processor may declare only **externalizable** capabilities
(`ProcessingCapability.Externalizable`): `content.inspect`, `text.normalize`, `document.extract-text`,
`image.ocr`, `image.describe`, `audio.preprocess`, `audio.transcribe`, `audio.align`, `audio.diarize`,
and `semantic.extract` (whose result is a typed candidate batch, never a mutation). `context.resolve`,
`change.plan` and `change.verify` stay **in-process** — with authority evaluation and execution, which
are not capabilities at all — because they need live domain state, permissions, policy and concurrency
semantics. The manifest validator rejects a sidecar or remote manifest that declares one.

## 2. Security model

- Taskdeck starts the sidecar and owns its lifetime; an orphaned process is a host bug, not a feature.
- Each process receives a per-process random **session secret** at launch (an environment variable
  the host sets — it is *not* a field on the JSON-RPC envelopes); a process that cannot present it on
  `processor.describe` is terminated. Transport-level; implemented by the CF-04 supervisor.
- Inputs are supplied through a Taskdeck-managed **spool directory** (`spool://<handle>`) or a
  short-lived authenticated **content handle** (`content://<handle>`) — never an arbitrary filesystem
  path the user supplied. `WorkerProtocolValidator.ValidateRunParams` rejects any other scheme.
- The manifest declares required hosts; a processor whose manifest says `networkRequired: false` runs
  with the network denied where the platform allows it, and the conformance suite proves the denial.
- **stdout is protocol-only.** Diagnostics go to stderr and must be content-free (no user text, no
  audio bytes, no file names beyond the spool handle). A content-bearing stderr line fails conformance.
- Every result carries the processor id, version, model and configuration hash so provenance can name
  the engine that actually ran (the ADR-0045 honesty rule, extended to every modality).

## 3. Messages

All messages are JSON-RPC 2.0 objects, one per line (LF-terminated, UTF-8, no BOM) on the stdio
transport. Property names are camelCase. Manifest enumerations (`execution`, `locality`, `gpu`,
`costModel.type`) are kebab-case strings with **exact** spelling (`"SIDECAR"` and `1` are rejected);
the protocol's own `status`, `kind`, `type`, `phase`, `messageCode`, `errorCode`, `derivation` and
`severity` values are plain strings with the spellings shown below (`kind` is the `RepresentationKind`
or `SemanticCandidateKind` name, e.g. `Transcript`; `errorCode` is UPPER_SNAKE). Protocol messages
tolerate unknown members (a newer sidecar may add fields); manifests do not. The host validates the
response envelope (`ValidateResponseEnvelope`: `jsonrpc` is `"2.0"`, the `id` echoes the request,
exactly one of `result`/`error` is present) and the notification envelope
(`ValidateNotificationEnvelope`: `jsonrpc`, the exact method name, a `params` object).

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
    "inputs": [
      {
        "kind": "source-asset",
        "id": "7ec00b5e-7b9f-4ebd-8a31-f018340bb0aa",
        "mediaType": "audio/webm",
        "contentHandle": "spool://2b9e",
        "sha256": "<64 hex>",
        "byteSize": 2481000,
        "role": "audio"
      }
    ],
    "options": {
      "language": "auto",
      "qualityTier": "balanced",
      "capability": { "wordTimestamps": false, "diarization": false }
    },
    "limits": {
      "deadlineUtc": "2026-08-30T03:00:00Z",
      "maxWallTimeMs": 600000,
      "maxOutputBytes": 5000000
    }
  }
}
```

**Inputs** are a list of typed references — `source-asset`, `representation`, or `context-snapshot`
(a bounded, host-prepared projection of domain state such as aliases and recent targets, delivered by
handle only). A source asset or representation input carries `mediaType`, `sha256` and `byteSize` so the
processor can refuse before reading; `role` names the input's part when a capability takes several
(`audio` + `transcript` for `audio.align`). **Options** common to every capability are `language` and
`qualityTier`; capability-specific settings (diarisation, speaker caps, OCR languages) travel in the
`capability` object, which the host validates against the manifest's
`capabilityContracts[capability].optionsSchema` (CF-04).

`WorkerProtocolValidator.ValidateRunParams` rejects: a protocol version other than 1; a capability
outside `ProcessingCapability.All`; an empty input list, a null input, an unknown input kind, an empty
id, a duplicated id; a missing or non-host-issued content handle; for source-asset and representation
inputs a missing media type, a digest that is not 64 hex characters, or a non-positive byte size;
a non-object `options.capability`; non-positive limits.

### 3.3 `processor.progress` (notification, processor → host)

```json
{
  "jsonrpc": "2.0",
  "method": "processor.progress",
  "params": { "jobId": "job-7f41", "phase": "transcribing", "fraction": 0.54, "messageCode": "audio.transcribing" }
}
```

`messageCode` is a stable, translatable code — never free text derived from content.
`ValidateProgress` requires a non-blank `jobId` and `phase` and a `fraction` within `[0, 1]` when
present; anything else is dropped before it can touch progress state.

### 3.4 `processor.cancel` (request, host → processor)

`params: { "jobId": "job-7f41" }` (`ProcessorCancelParams`, `ValidateCancel`). The processor must stop
within the host's cancellation grace period (a host setting, CF-04 — not a manifest field) and answer
the original `processor.run` with `status: "cancelled"`; the host terminates the process if it does not.

### 3.5 Result

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "result": {
    "status": "completed",
    "processor": { "id": "taskdeck.whisperx", "version": "1.0.0", "model": "large-v3-turbo", "configurationHash": "sha256:…" },
    "outputs": [
      {
        "type": "representation",
        "kind": "Transcript",
        "schemaVersion": 1,
        "language": "en",
        "text": "…",
        "segments": [
          { "charStart": 0, "charEnd": 76, "startMs": 4200, "endMs": 11840, "speakerLabel": "SPEAKER_00", "confidence": 0.93 }
        ]
      },
      { "type": "diagnostic", "code": "ALIGNMENT_MODEL_MISSING", "severity": "warning", "safeDetail": "No alignment model for 'en-XX'; segment timestamps only." }
    ],
    "warnings": [],
    "usage": { "wallTimeMs": 18420, "audioDurationMs": 181000, "billableUnits": 3.02, "billableUnitKind": "minute", "peakRamMb": 3210, "peakVramMb": 5740 }
  }
}
```

`status` is one of `completed | failed | cancelled`. A `completed` run emits at least one
representation or candidate batch (diagnostics alone are not a result) and carries a non-empty
`processor.configurationHash` (provenance and cache identity).

**Outputs** are a discriminated union on `type`, dispatched in any member order
(`ProcessorOutputJsonConverter`; an unknown `type` is reported by the validator, never thrown):

| `type` | Payload | Emitted by |
| --- | --- | --- |
| `representation` | `kind` (a `RepresentationKind` name), `schemaVersion ≥ 1`, `language?`; **text kinds** (`NormalizedText`, `Transcript`, `OcrText`, `ImageDescription`) carry `text` (may be empty, never absent) with optional `segments` (char + time) and `regions` (page/image geometry); **structured kinds** (`DocumentStructure`, `StructuredEvent`) carry a `structured` object and no segments | every representation-producing capability |
| `candidate-batch` | `schemaVersion`, `candidates[]` — each `kind` (a `SemanticCandidateKind` name), `statement`, `fields?` (object), `derivation` (`extractive` \| `inferred`), `confidence?`, `evidence[]` | `semantic.extract` only |
| `diagnostic` | `code`, `severity` (`info` \| `warning` \| `error`), content-free `safeDetail?` | any |

Segment rules: ordered by `charStart`, non-overlapping, `0 ≤ charStart ≤ charEnd ≤ text.length`
(UTF-16 code units — the unit the shipped transcript evidence spans use), non-negative timestamps with
`startMs ≤ endMs`, confidence in `[0, 1]`. Region rules: a normalised rectangle (`0 ≤ x, y`;
`width, height > 0`; `x + width ≤ 1`; `y + height ≤ 1`), `pageNumber ≥ 1` when present, an optional
char range within `text`. Candidate evidence cites **exactly one** of `representationId` (an input) or
`outputIndex` (a representation emitted in this result) and an `anchorKind` (an `EvidenceAnchorKind`
name) whose location fields must be present — `TextSpan` char range within the referenced text,
`TimeRange` millisecond range, `PageRegion` page + region, `ImageRegion` region, `JsonPointer` a pointer
starting with `/`, `WholeSource` nothing; an extractive candidate cites at least one anchor. `usage`
values are non-negative; `billableUnits` needs a `billableUnitKind`; `estimatedCost` needs a
three-letter `currency`. A `null` entry in `outputs`, `segments`, `regions`, `candidates`, `evidence` or
`warnings` is a validation error, never a host exception (`ValidateResult`). Every enum-valued string is
matched against `Enum.GetNames`, so `"1"` is not a kind.

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
| -32012 | `UnsupportedInput` | Input kind, role or count the capability does not take |
| -32020 | `ResourceExhausted` | Model/RAM/VRAM could not be allocated (usually retryable on a smaller route) |
| -32021 | `DeadlineExceeded` | `limits.deadlineUtc` or `maxWallTimeMs` passed |
| -32022 | `OutputTooLarge` | Result would exceed `maxOutputBytes` |
| -32030 | `Cancelled` | Cancelled by the host |
| -32040 | `ProtocolVersionMismatch` | Processor cannot speak the requested version |

## 5. Manifest (v1)

See the JSON schema — the published contract. It is **not** evaluated at runtime: `ProcessorManifest`
parses with unknown members disallowed (the runtime form of `additionalProperties: false`) and exact
kebab-case enum spellings, and `ProcessorManifestValidator` enforces the schema's bounds and
enumerations in code, plus the rules a schema cannot express:

- capabilities are drawn from `ProcessingCapability.All` and declared once; a `sidecar` or `remote`
  manifest may declare only externalizable capabilities;
- **`capabilityContracts`** carries exactly one entry per declared capability (and none for an
  undeclared one): `outputs` (the families it emits — `representation` | `candidate-batch` |
  `diagnostic`, never diagnostics alone; `candidate-batch` only for `semantic.extract`, which must
  emit it), `outputSchemas` (≥ 1, unique) and an optional `optionsSchema`;
- a `local` processor cannot declare `networkRequired: true` and cannot `execute: remote`;
- a `remote` processor must declare `networkRequired: true` **and** at least one `allowedHosts` entry;
- a processor with `networkRequired: false` must not list any `allowedHosts` entry (an empty array is
  fine, as in the example);
- `resources.gpu: none` cannot require VRAM (`minVramMb` must be absent or 0); `costModel.type` is
  required when `costModel` is present; a `free-local` cost model cannot carry a non-zero unit price;
- data classes are `text | audio | image | document | metadata`; currency is a three-letter ISO code.

Example: `backend/src/Taskdeck.Application/Processing/Schemas/whisperx-processor.example.json`
(`ProcessorManifestValidatorTests` reads it through `ProcessorManifestResources`, so drift is a test
failure). The copy under `docs/analysis/2026-08-30-context-fabric/` is the planning pack as received.

## 6. Conformance tests (CF-04 — every processor must pass; v1 is fixed once PdfPig and WhisperX both do)

1. manifest/schema validation and `processor.describe` agreement;
2. declared MIME, capability and input-shape enforcement (`-32010` / `-32011` / `-32012`);
3. cancellation and process termination within the grace period;
4. deadline and output-size limits (`-32021` / `-32022`);
5. no content written to stderr in normal operation;
6. deterministic result hashing for fixed fixtures and configuration;
7. malformed-result rejection (the validator above), including out-of-order and unknown `type`;
8. network denial for a local-only manifest;
9. crash recovery and idempotent replay (same job id, same result hash);
10. processor/model/configuration provenance round-trip into `ProcessingRun`;
11. capability-specific options validated against the declared `optionsSchema`.

## 7. Transport notes

- **stdio (desktop):** one JSON object per line; the host writes requests, reads results and
  notifications, and never interleaves two runs on one process unless the manifest declares the
  reserved feature name `concurrent-jobs` (`features` is free-form in v1; the host starts honouring
  this name in CF-04).
- **queue (hosted, later):** the same `params` object is the job payload, `result`/`error` the
  completion record, and `processor.progress` a progress event; `contentHandle` resolves to an object
  store URL through `IBlobStore` (ADR-0061 stage 3 only).
