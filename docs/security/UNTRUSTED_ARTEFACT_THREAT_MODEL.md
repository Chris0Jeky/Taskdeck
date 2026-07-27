# Untrusted Artefact Threat Model

Last Updated: 2026-07-27

Owner: Taskdeck maintainers

Status: current-control and residual-risk register; artefact upload/content and bounded text/PDF extraction are implemented, while the `#1323` prompt amendment remains Proposed and image/OCR, UI, and consent gates stay separately owned

Related work: REVIVAL-00 `#1311`, transcript triage `#1304` / PR `#1312`, GEN-01 `#1315`, GEN-02 `#1316`, GEN-03 `#1317`, GEN-04 `#1318`, GEN-05 `#1319` / PR `#1339`, GEN-06 `#1320`, GEN-09 `#1323`

## Purpose and scope

This document is the content-ingestion submodel for the REVIVAL-00 beta threat model. REVIVAL-00 owns the overall deployment and beta decision; this document owns attacker-controlled PDFs, images, text files, transcripts, pasted text, file names, extracted URLs, and future shared-page content from ingress through review.

The honest posture is layered mitigation, not a claim that prompt injection can be solved. Human review is a real security boundary, but it must not be the only boundary once extracted artefact text enters an LLM prompt.

The `#1323` candidate implementation uses `llm-triage.v2`: collision-resistant untrusted-data framing, an explicit capture-triage-only raw response mode, exact raw-JSON response containment, ordinal evidence grounding, bounded provider responses under one headers-plus-body deadline, and review-visible fallback when an empty verdict contradicts a finite conservative source task-signal vocabulary. The amendment remains **Proposed pending maintainer ratification** in ADR-0045. Its canary fixtures exercise the effective candidate path with deterministic provider responses; they are a bounded regression rail, not evidence that every model resists every injection. Unrecognized task phrasing can still be accepted as a genuine empty verdict.

Artefact upload, metadata/content/delete, and local plain-text/PDF text-layer extraction now exist. Image OCR/vision extraction does not. The shipped PDF lane has byte, parser-stack, page, output-character, request-time, and concurrent-worker limits, but its synchronous in-process parser can continue after a timed-out request and still lacks a decompressed-byte/object-count or single-parse memory ceiling. This document keeps that residual risk explicit rather than treating the existing budgets as decompression-bomb containment.

## Assets and trust boundaries

Protected assets:

- board and card integrity, including the review-before-apply invariant;
- user identity, board authorization, API keys, connector credentials, and provider secrets;
- private captures, artefact bytes, extracted text, proposal provenance, and audit history;
- local CPU, memory, disk, SQLite/WAL availability, provider quota, and user attention;
- UI integrity: no script execution, deceptive link activation, or fabricated trusted provenance.

Trust boundaries:

1. **Ingress:** an authenticated user supplies bytes, pasted text, a transcript, a file name, or a URL-like string.
2. **Storage:** metadata and bytes cross into SQLite and later leave it through metadata/content/export/delete paths.
3. **Extraction:** a parser, PDF library, OCR/vision provider, or future page fetch turns untrusted bytes into untrusted text.
4. **Prompt/egress:** untrusted text may leave the device and enter a provider request as data.
5. **Output:** model output crosses back into Taskdeck and may become a proposal only after strict containment and provenance checks.
6. **Review/apply:** a human sees a preview; only approved operations may reach the existing executor and authorization checks.
7. **Render/serve:** file names, extracted text, links, thumbnails, and downloaded bytes reach a browser or external export.

Attacker capability assumed: the attacker can choose every byte of an uploaded or shared artefact, its declared MIME type and file name, visible and hidden text, URLs, page/object counts, compression ratios, and instruction-like phrases. The attacker may know Taskdeck's prompt and operation vocabulary. The attacker does not start with OS/root access or another user's authenticated session.

## Control principles

- Treat extracted content as data at every layer; it never acquires instruction or authority status.
- Claims-first authorization precedes metadata, content, export, or delete access.
- Declared MIME type is a hint, not proof; binary validation has its own lane.
- Bound work before and during parsing, not only after a full payload is materialized.
- Strict output containment precedes proposal persistence. Unknown fields, operation vocabulary, tool calls, or malformed items fail closed to the deterministic path.
- Apply-time authorization must bind every effective card, board, and column identifier carried in operation parameters to the authorized proposal scope; checking proposal metadata alone is insufficient.
- Automation-originated board writes remain proposal-first. Preview must equal the effective revision Apply executes.
- Escape rendered text by construction; do not render extracted HTML. Treat extracted URLs as inert text until an explicit safe-link interaction.
- Do not log artefact bytes, base64, raw extracted text, or secrets recovered from content.
- Tell the user what content leaves the device before provider egress and preserve provider/model/prompt provenance.

## Threat and control matrix

| Threat scenario | Existing control as of 2026-07-27 | Remaining control and owner | Accepted residual risk after controls |
| --- | --- | --- | --- |
| Embedded prompt instructions such as “ignore previous instructions,” system-role mimicry, or fake policy text | Review-first proposal approval and deterministic fallback exist. The Proposed `llm-triage.v2` candidate frames source text inside a fresh random boundary and states that all enclosed content is data, never instructions. | Maintainers must ratify or reject the ADR-0045 amendment. If ratified, keep the hostile-source canaries bound to the effective extractor and require GEN-04 `#1318` to reuse the same rails and provenance. | Models can still follow novel injection patterns, extract a malicious sentence as a plausible task, or return empty for task phrasing outside the finite contradiction-signal vocabulary. Human review and operation containment remain mandatory. |
| Tool/operation-vocabulary mimicry embedded in text (`delete board`, JSON, XML, fake tool calls) | Board mutation is proposal-first and the executor has a finite apply-time registry. The Proposed v2 parser accepts one root `tasks` array whose task objects contain only `title` and `evidence`; prose, fences, duplicates, unknown fields, non-objects, unsafe title controls, over-limit values, and operation/tool envelopes take deterministic fallback. | Keep the response canaries and custom-prompt provider regressions bound to the service fallback path. GEN-05 `#1319` / PR `#1339` must continue binding effective parameter targets to authorized proposal scope. | A malicious phrase may appear as inert task title/evidence. It must never become executable vocabulary without a separately validated proposal operation, and apply-time authorization remains mandatory even after strict output containment. |
| Indirect injection asking the model to reveal system prompts, other captures, secrets, or connector data | Provider requests are purpose-scoped; telemetry/log redaction policies prohibit secret content. The Proposed v2 extractor explicitly requests raw response preservation for capture triage, while ordinary ChatService custom prompts retain legacy instruction parsing/classification. | Consent/egress ownership remains GEN-03 `#1317`; GEN-04 must provide only the current bounded artefact text and no tools or unrelated workspace context. | The provider necessarily receives the consented content. Provider-side retention and compromise remain third-party risks disclosed to the user. |
| Decompression bomb, oversized image, extreme pixel dimensions, PDF page/object bomb, or huge extracted text | Upload is bounded to 10 MiB per artefact and a 200 MiB per-user quota. Plain text extraction caps 1 MiB input and 102,400 characters. PDF extraction caps 10 MiB input, parser stack depth 64, 100 pages, and 51,200 extracted characters; the service defaults to a 30-second request budget and two concurrent parse workers. | PDF parsing still needs an enforceable decompressed-byte/object-count or isolated-process memory boundary (tracked separately by ADR-0048 / `#1379`). Define decoded-pixel/dimension limits before any image OCR/vision extractor ships. | Timeout returns the request but cannot stop PdfPig's synchronous in-process parse. The concurrency gate caps accumulated spinning workers, not one parse's peak memory; a decompression/object bomb can still exhaust the process. |
| Malformed, truncated, encrypted, cyclic, or polyglot container exploiting a parser | Extraction is isolated behind `IArtefactTextExtractor`; PdfPig uses strict parsing and stack depth 64. Parser faults and timeouts become bounded, content-free warning records and do not write board state. | Keep parser dependencies patched and retain malformed/encrypted/cancellation tests. Consider a separate low-privilege process if incidents or fuzzing show the in-process boundary is insufficient. | Third-party parser vulnerabilities and one-parse memory exhaustion remain possible even with typed failure handling. |
| Declared MIME does not match bytes; executable renamed `.png` or `.pdf` | The binary-aware upload validator allowlists PNG, JPEG, WebP, PDF, plain text, and Markdown; it requires a matching extension and signature/strict UTF-8, rejects unsupported kinds, and hashes bounded bytes. | Retain mismatch/polyglot regressions and never treat the allowlist/signature check as proof that a complex container is benign. | Magic-byte checks prevent simple type confusion but do not validate every object or decoder path inside an allowed container. |
| Stored or preview XSS through file names, extracted text, OCR output, SVG/HTML payloads, or Markdown-like links | File names reject path/meta/control/Unicode-format characters. SVG/HTML are not upload kinds. API CSP is deny-by-default, `nosniff` is emitted, non-images download as attachments, and Vue escapes ordinary interpolation. | GEN-06 `#1320`: keep file names/text in text nodes, prohibit `v-html`, cover hostile names/text in component tests, and make extracted URLs inert by default. | Browser bugs and future unsafe rendering regressions remain possible; CSP is defense in depth, not a sanitizer. |
| Stored-XSS or content-sniffing through the content endpoint | The authenticated content endpoint performs a user-scoped lookup, emits the allowlisted stored `Content-Type`, serves images inline and all other allowed kinds as attachments, and inherits `nosniff`. | Review whether hostile raster images need a separate origin before any richer inline preview; keep cross-user and header regressions green. | Allowed raster-image decoders still process hostile bytes in the browser. Downloading a malicious allowed file transfers risk to the user's local viewer. |
| Link trap, phishing URL, custom-scheme launch, or future server-side fetch/SSRF from extracted content | Extracted URLs have no authority and no shared-page fetcher is shipped. | Render URLs as inert text until explicit user action. If links become clickable, allow only reviewed `https` targets with visible host and safe opener attributes. Any future fetcher needs DNS/IP revalidation, private-network denial, redirect/size/time limits, and a separate threat review. | A permitted public HTTPS destination can still be malicious or change after review. The UI must never label it trusted merely because it was extracted. |
| Cross-user metadata/content enumeration or blob retrieval | The authenticated artefact controller derives identity from claims; metadata, content, extraction history, export, and delete service/repository paths are user-scoped. | Keep cross-user metadata, bytes, extraction, export, and deletion tests green without existence leaks. | A compromised authenticated account can access its own permitted content; local database/OS compromise is outside this model. |
| Provider egress without informed consent, or a local-only expectation silently becoming remote | Egress disclosure infrastructure exists; live provider use is configuration-gated. | GEN-03 `#1317`: pre-egress copy names the provider, content class, purpose, and local alternative; consent is explicit and revocable. GEN-04 must preserve the choice. | The user can consent without reading; provider jurisdiction, retention, and abuse monitoring remain external risks. |
| Misleading provenance or an output presented as a trusted source statement | Capture/proposal provenance and the review gate exist. | Store artefact/extraction/provider/model/prompt linkage. UI distinguishes source quote, extracted candidate, model inference, and approved board state; never show model output as verbatim evidence unless exact-span verification succeeds. | A source document itself can lie. Provenance proves origin and processing path, not truth. |

## Output-containment requirement

The Proposed `llm-triage.v2` extraction shape is data-only: a bounded list of task candidates with bounded title and verbatim evidence. It is not a proposal-operation envelope and it cannot contain tool calls. ADR-0045's original `llm-triage.v1` decision remains Accepted until maintainers ratify this amendment.

Before GEN-04 routes artefact text through the LLM lane, the effective implementation must prove:

- raw JSON only; no prose/fences accepted as a substitute for strict parsing;
- no additional root or task fields;
- no `operations`, `actionType`, `targetType`, `tool_calls`, provider tool envelope, or unknown vocabulary;
- task and evidence length/count bounds;
- no leading/trailing title whitespace, newline/C0/C1 controls, or bidi control/isolate characters;
- evidence is an exact substring of the delimited source;
- malformed, extra-field, or non-task output records an honest extraction failure and invokes the deterministic fallback;
- an explicit empty task array is distinguishable from parse/validation failure, while an empty verdict that contradicts the finite conservative human-task signal vocabulary takes the review-visible deterministic fallback;
- decoded provider responses are byte-bounded before string/JSON materialization.

The `#1323` candidate path enforces these requirements before it constructs the server-authored versioned envelope. The hostile-source cases replay extracted-text fixture strings through its framed transcript request and return deterministic honest-task or genuine-empty fixtures; they do not exercise real artefact routing, PDF parsing, or image/OCR. Separate empty-verdict tests prove that recognized transcript/image-text task signals fail closed into a review proposal while the no-task PDF-text case remains a genuine empty verdict. Hostile-response fixtures run through the real extractor and service and prove deterministic proposal fallback. Historical `llm-triage.v1` envelopes retain their version-specific title-validation semantics; the candidate stamps new successful or genuine-empty verdicts `llm-triage.v2`, whose stricter title and evidence rules are schema/runtime-aligned.

These tests prove Taskdeck's framing, parser, grounding, and fallback behavior for fixed inputs. They do not execute a live provider and must not be described as universal model resistance.

## Current extraction budgets and residual gaps

Current defaults and hard extractor bounds are:

- upload: 10 MiB per artefact and 200 MiB per user;
- plain text/Markdown extraction: 1 MiB input and 102,400 characters;
- PDF text-layer extraction: 10 MiB input, stack depth 64, 100 pages, and 51,200 extracted characters;
- extraction service: 30-second request budget and two concurrent parse workers by default;
- extraction results: bounded warning/provenance fields and no raw content in failure logs.

These are not complete decompression-bomb controls. PdfPig opens and parses synchronously in the API process; cancellation is observed around that call, not inside every parser operation. After timeout the worker can continue and retain memory/CPU until it returns. The permit stays held and therefore bounds concurrent runaway workers, but no decompressed-byte/object-count or single-worker memory limit exists. Image bytes can be stored and served, but no OCR/vision extractor exists, so decoded-pixel/dimension budgets remain a precondition for that future lane.

## Fixture contract

Fixtures live under `backend/tests/Taskdeck.Application.Tests/Fixtures/untrusted-artefacts/`.

- `hostile-transcript.txt`: hidden/system-role and tool-vocabulary mimicry beside one honest commitment.
- `hostile-pdf-text.txt`: PDF-extracted prose containing an override/exfiltration demand and no genuine action item.
- `hostile-image-text.txt`: OCR-like text containing a fake tool call beside one legitimate next step.
- `response-extra-field.json`: task-shaped output with forbidden additional fields.
- `response-vocabulary-escape.json`: an operation/tool-shaped response instead of task candidates.
- `response-malformed.txt`: non-JSON model output.
- `manifest.json`: stable IDs, source kinds, allowed verdicts, forbidden outcomes, and expected fallback classifications.

The fixture-contract tests independently pin each case ID, file, canary, source kind, allowed verdict, forbidden outcome, and hostile semantic signal. They prove the manifest and fixture directory agree exactly, every referenced payload is bounded strict UTF-8, and JSON is parseable only where the manifest declares the exact `json` format. They bind each source fixture to the framed extractor path with a deterministic honest-task or genuine-empty completion, prove a contradictory empty verdict is review-visible for the transcript/image cases, and bind each response fixture through the strict extractor to deterministic service fallback. They do **not** run a live model or prove that an LLM resisted the fixture text.

## Delivery gates and ownership

| Gate | Owner | Required evidence before dependent merge |
| --- | --- | --- |
| Upload authz, streaming caps/quota, signature validation, safe content disposition | GEN-01 `#1315` (implemented) | Keep cross-user, cap, signature-mismatch, download-header, export/delete tests green |
| Local extraction budgets and typed failures | GEN-02 `#1316` (text/PDF implemented) | Keep byte/page/char/time/cancellation/concurrency tests green; decompressed-object/memory isolation remains ADR-0048 / `#1379`; pixel budgets precede OCR |
| Consent and provider egress | GEN-03 `#1317` | Copy review plus off/on/revoke and no-egress-without-consent tests |
| Prompt rails and strict output containment | `#1323` Proposed `llm-triage.v2` amendment; GEN-04 `#1318` must reuse it if ratified | Maintainer ratification plus provider wrapper/prose/fence, response-byte, hostile source/response, unsafe-title, grounding, and review-fallback tests |
| Apply-time effective-target authorization | GEN-05 `#1319` / PR `#1339` | Cross-board tests proving every parameter `cardId`, `boardId`, and `columnId` is bound to the proposal's authorized scope before execution |
| Safe file-name/text/link rendering | GEN-06 `#1320` | Component tests proving escaping, inert URLs, and no unsafe HTML path |
| Overall beta posture and accepted residuals | REVIVAL-00 `#1311` | Link this submodel, confirm owners/gates, and record any accepted exception explicitly |

## Verification for the prompt-rail slice

- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~OpenAiLlmProviderTests|FullyQualifiedName~GeminiLlmProviderTests|FullyQualifiedName~OllamaLlmProviderTests|FullyQualifiedName~LlmProviderResilienceTests|FullyQualifiedName~LlmProviderResponseReaderTests|FullyQualifiedName~LlmProviderResponseDeadlineTests|FullyQualifiedName~ChatServiceProductionProviderRegressionTests|FullyQualifiedName~LlmCaptureTriagePromptTests|FullyQualifiedName~LlmCaptureTriageExtractorTests|FullyQualifiedName~CaptureTriageOutputContractTests|FullyQualifiedName~CaptureTriageServiceTests|FullyQualifiedName~UntrustedArtefactFixtureContractTests"`
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter "FullyQualifiedName~TranscriptTriageLlmGoldenPathIntegrationTests"`
- `node scripts/check-docs-governance.mjs`
- `git diff --check`

Not re-verified by this prompt-rail slice: live-model behavior, shipped upload/content/extraction controls, UI escaping, consent flow, or future image/OCR triage. The current-control statements above are code-backed inventory, not new end-to-end evidence from `#1323`. Prompt framing and strict output containment reduce risk; they do not solve prompt injection, the finite empty-verdict signal vocabulary can miss novel task phrasing, and the PDF decompression/object-memory residual remains open.
