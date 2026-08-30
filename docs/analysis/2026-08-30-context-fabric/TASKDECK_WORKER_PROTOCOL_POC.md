# Taskdeck Worker Protocol v1 — proof-of-concept contract

The protocol separates processor capability from transport. Desktop can use supervised JSON-RPC over stdio; hosted deployments can map the same envelope onto a queue and object storage.

## Security model

- Taskdeck starts the sidecar and owns its lifetime.
- The sidecar receives a per-process random session secret.
- Large inputs are supplied through a Taskdeck-managed spool directory or a short-lived authenticated local content handle, not arbitrary user-provided filesystem paths.
- The worker declares required network hosts; local-only manifests run with network disabled where practical.
- Standard output is protocol-only. Diagnostic logs go to standard error and must be content-free/redacted.

## Job request

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
      "contentHandle": "spool://2b9e...",
      "sha256": "...",
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

## Progress notification

```json
{
  "jsonrpc": "2.0",
  "method": "processor.progress",
  "params": {
    "jobId": "job-7f41",
    "phase": "transcribing",
    "fraction": 0.54,
    "messageCode": "audio.transcribing"
  }
}
```

## Result

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "result": {
    "status": "completed",
    "processor": {
      "id": "taskdeck.whisperx",
      "version": "1.0.0",
      "model": "large-v3-turbo",
      "configurationHash": "sha256:..."
    },
    "representations": [
      {
        "kind": "Transcript",
        "schemaVersion": 1,
        "language": "en",
        "text": "...",
        "segments": [
          {
            "charStart": 0,
            "charEnd": 76,
            "startMs": 4200,
            "endMs": 11840,
            "speakerLabel": "SPEAKER_00",
            "confidence": 0.93
          }
        ]
      }
    ],
    "warnings": [],
    "usage": {
      "wallTimeMs": 18420,
      "audioDurationMs": 181000,
      "peakRamMb": 3210,
      "peakVramMb": 5740
    }
  }
}
```

## Failure

```json
{
  "jsonrpc": "2.0",
  "id": "job-7f41",
  "error": {
    "code": -32020,
    "message": "Processor could not allocate the requested model",
    "data": {
      "errorCode": "RESOURCE_EXHAUSTED",
      "retryable": true,
      "safeDetail": "Try a smaller model or CPU fallback."
    }
  }
}
```

## Required conformance tests

- manifest/schema validation;
- declared MIME and capability enforcement;
- cancellation and process termination;
- deadline and output-size limits;
- no content written to standard error in normal operation;
- deterministic result hashing for fixed fixtures/configuration;
- malformed result rejection;
- network denial for a local-only processor;
- crash recovery and idempotent replay;
- processor/model/configuration provenance round-trip.

