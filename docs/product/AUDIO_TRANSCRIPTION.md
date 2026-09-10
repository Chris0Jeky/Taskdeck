# Explicit audio transcription

This #2808 continuation adds an optional speech request to the same private originals used by
Thinking Decks and the original-recording library. Uploading an original never queues or sends it.
The owner opens transcription options, sees the configured destination/model and daily allowance,
and explicitly consents to one request. Consent belongs to that configuration hash; refreshing to
a different configuration clears it, even if the previous configuration is restored later.
Existing written answers and board cards stay unchanged.

## Review and recovery

Each request has an owner-scoped ID and immutable request/configuration hashes. Admission and the
daily attempt/byte reservation commit together before transport. The same request ID returns its
receipt without another dispatch; changed payloads conflict. A competing request cannot start while
the recording has a running attempt. Failure requires a new explicit attempt; a lost response can be
recovered by refreshing receipts or explicitly retrying the same request. No client or provider
automatic retry, background task, restart redispatch or unattended transcription is registered.

An attempt has a two-minute publication deadline. The HTTP provider has a separately bounded timeout.
An interrupted process leaves a running receipt that expires on the next status/admission read;
expired or superseded attempts cannot publish late text. Original bytes are checked for exact size
and SHA-256 before dispatch. Source ownership/access is checked at admission, immediately before
dispatch and inside the result transaction. Account/source erasure cannot resurrect a transcript.

Successful output becomes an immutable `Provisional` transcript representation, parented to the
original source asset and carrying provider, model, configuration hash and attempt identity.
`ThinkingAudioAnswer.RepresentationId` is not changed by processing. Selecting a candidate copies it
into a guarded written draft. Saving the reviewed/edited draft creates a `human-reviewed-transcript`
representation with the candidate as its parent; only the existing separate confirmation action
answers the question in private memory. A changed or removed question cannot receive an old answer.
Retained originals can still be transcribed from the library without answering a different question.

## Operator configuration

The `SpeechTranscription` section is disabled by default. It is separate from text-chat provider
selection. Supply credentials through the deployment's existing secret configuration; never commit
them. Enabled invalid settings reject API startup without echoing the credential or endpoint.

| Setting | Default and bound |
| --- | --- |
| `Enabled` | `false`; opt in deliberately |
| `BaseUrl` | `https://api.openai.com/v1/`; HTTPS without credentials, query or fragment |
| `ApiKey` | No default credential |
| `Model` | `gpt-4o-mini-transcribe`; an operator-selected compatible transcription model |
| `TimeoutSeconds` | 60; 1–90 seconds |
| `DailyAttempts` | 5 per owner/UTC day; 1–20 |
| `DailyInputBytes` | 10 MiB per owner/UTC day; 1 byte–40 MiB |
| `AllowLocalhostInDevelopment` | `false`; explicit `true` permits localhost only in Development |

The adapter sends one multipart `POST` to `BaseUrl + audio/transcriptions`, with `file`, `model` and
JSON response format. The original filename is replaced by a constant format-specific filename.
WebM, Ogg, WAV/x-wav, MP3 and MP4 use the original bytes; no lossy conversion or invented transcript
is substituted. This follows the [speech endpoint contract](https://platform.openai.com/docs/api-reference/audio/updateVoiceConsent?lang=javascript).
Compatible services may differ in model availability and decoding; their actual acceptance must be
tested before use. This delivery does not install a local WhisperX processor or make a live model call.

The existing 2 MiB upload limit remains. Output is capped at 64 KiB JSON and 8,000 text characters;
invalid or oversized responses fail without silently truncating an answer. Up to 20 attempt receipts
are retained per recording. Daily counters count admitted attempts and input bytes, including failed
attempts. They survive original/capture erasure and are deleted only with the account. These counters
are **not** a duration, token or currency guarantee; provider billing controls remain necessary for
an operator's actual deployment budget.

The global, identity and Worker kill switches apply before admission and again before dispatch. The
speech client reuses the shared circuit tracker, egress disclosure/envelope, DNS/private-address
checks and protected telemetry. Redirects, proxy use and trace propagation are disabled. Failure
receipts contain fixed codes rather than provider response bodies, credentials or original words.

The bounded backend review recorded two nonblocking limits. Erasure prevents local publication but
cannot recall an admitted network request or guarantee cancellation in the final interval between
the access check and socket dispatch. The shared circuit counts transport/408/5xx failures; a 429 or
malformed/oversized successful body is rejected but does not currently open that circuit. Every
admitted attempt still consumes the daily attempt/byte allowance. These limits do not justify an
automatic retry, retention promise or claim that external provider data has been erased.

## Persistence and portability

Migration `20260910062750_AddAudioTranscriptionReceipts` adds `AudioTranscriptionAttempts` and
`AudioTranscriptionBudgets`. Attempts link to owner, recording, capture, source and resulting
representation. `ProcessingRunId` on a derived representation identifies the dedicated attempt;
this does not claim a generic Worker Protocol run store or processor supervisor has shipped.

Buffered and streaming account exports include `sourceStorage.audioTranscriptionAttempts` and
`sourceStorage.audioTranscriptionBudgets`, alongside original bytes, transcripts and representation
provenance. Account deletion erases both new tables in its transaction. Capture erasure deletes
attempts before source-dependent rows while preserving the owner's daily allowance counter.
Source-storage import/replay and a complete restore acceptance drill remain separate #2808 work.

## Verification

The focused suites cover domain deadlines and UTC limits, every accepted recording media type,
malformed/oversized output, timeouts/cancellation, exact retry, competing requests, failed attempts,
paused/changed settings, owner/source disappearance, retained deleted-board originals, draft
provenance, both exports and account erasure. Actual localhost HTTP tests exercise multipart bytes,
disclosure, trace suppression, blocked redirects and the shared circuit.

`tests/e2e/audio-transcription.spec.ts` normally proves disabled-by-default behavior across all four
experiences. Set `TASKDECK_SPEECH_TRANSPORT_PROOF=1` and explicitly configure the isolated API's
speech endpoint to `http://localhost:5349/v1/`, synthetic key/model and Development localhost opt-in
to run the real fixture continuation. The test starts/stops its own localhost provider; the API and
database must be synthetic. It proves provider failure, explicit retry, a lost success response,
receipt recovery, draft correction, confirmation, all experiences, 375px layout and scoped automated
accessibility. No paid model, physical microphone or subjective transcription-quality acceptance is
implied. Final totals belong to the [validation ledger](WORKSPACE_OVERHAUL_VALIDATION.md).

Human deployment and acceptance decisions remain in [OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md).
