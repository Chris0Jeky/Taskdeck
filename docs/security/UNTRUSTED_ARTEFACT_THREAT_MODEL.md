# Untrusted Artefact Threat Model

Last Updated: 2026-08-01

Owner: Taskdeck maintainers

Status: scoped control plan; artefact storage/upload and local extraction are shipped, while prompt-rail fixture binding, multimodal consent, artefact-to-triage routing, and Paper intake UX remain open

Related work: REVIVAL-00 `#1311`, transcript triage `#1304` / PR `#1312`, GEN-01 `#1315`, GEN-02 `#1316`, GEN-03 `#1317`, GEN-04 `#1318`, GEN-05 `#1319` / PR `#1339`, GEN-06 `#1320`, GEN-09 `#1323`

## Purpose and scope

This document is the content-ingestion submodel for the REVIVAL-00 beta threat model. REVIVAL-00 owns the overall deployment and beta decision; this document owns attacker-controlled PDFs, images, text files, transcripts, pasted text, file names, extracted URLs, and future shared-page content from ingress through review.

The honest posture is layered mitigation, not a claim that prompt injection can be solved. Human review is a real security boundary, but it must not be the only boundary once extracted artefact text enters an LLM prompt.

The original slice added this model and reusable canary fixtures. Subsequent PRs shipped the transcript LLM lane (`#1312`), artefact storage/upload (`#1341`), local extraction (`#1346`), and effective-target authorization (`#1339`). The hostile fixtures are still not bound to the effective prompt/parser path, so `#1323` remains open and this document does not claim the injection suite is green.

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

| Threat scenario | Existing control as of 2026-07-13 | Required new control and owner | Accepted residual risk after controls |
| --- | --- | --- | --- |
| Embedded prompt instructions such as “ignore previous instructions,” system-role mimicry, or fake policy text | Review-first proposal approval, the shipped gated transcript LLM path, and deterministic fallback exist. | `#1323`: bind the hostile transcript/PDF/image and malformed-response fixtures to the effective prompt/parser path, delimit untrusted content as data rather than instructions, and preserve provider/prompt provenance before GEN-04 `#1318` routes artefact text through that lane. | Models can still follow novel injection patterns or extract a malicious sentence as a plausible task. Human review and operation containment remain mandatory. |
| Tool/operation-vocabulary mimicry embedded in text (`delete board`, JSON, XML, fake tool calls) | Board mutation is proposal-first, the executor has a finite apply-time registry, and PR `#1339` closed GEN-05 `#1319` by binding effective parameter targets to the authorized proposal scope. | `#1323` must still prove strict LLM output containment against extra fields, tool-call envelopes, operation payloads, vocabulary escapes, and malformed responses before artefact-aware triage ships. | A malicious phrase may appear as inert task title/evidence. It must never become executable vocabulary without a separately validated proposal operation, and apply-time authorization remains mandatory even after strict output containment. |
| Indirect injection asking the model to reveal system prompts, other captures, secrets, or connector data | Provider requests are purpose-scoped; telemetry/log redaction policies prohibit secret content. | Prompt discipline must forbid disclosure and provide only the current bounded artefact text. Provider adapters must not attach tools or unrelated workspace context to extraction calls. | The provider necessarily receives the consented content. Provider-side retention and compromise remain third-party risks disclosed to the user. |
| Decompression bomb, oversized image, extreme pixel dimensions, PDF page/object bomb, or huge extracted text | GEN-01 `#1315` shipped bounded streaming upload plus allowed-type/signature checks, and GEN-02 `#1316` shipped bounded local text extraction with cancellation and typed failures. Vision/OCR decoded-pixel budgets are not yet shipped. | GEN-03 `#1317` must add conservative decoded-pixel/provider budgets before consent-gated multimodal extraction; keep the existing upload/extraction bounds regression-tested. | A payload inside every individual limit can still consume meaningful local CPU or provider quota. Conservative defaults and observable cancellation are required. |
| Malformed, truncated, encrypted, cyclic, or polyglot container exploiting a parser | Local extraction is isolated behind `IArtefactTextExtractor`; shipped extraction records retain bounded warnings and failures do not write board state. | Keep parser libraries patched and expand hostile-container coverage as real formats are admitted; use stronger process isolation if incident evidence justifies it. | Third-party parser vulnerabilities remain possible. Dependency scanning and prompt-free sandbox/process isolation may be needed if real incidents justify it. |
| Declared MIME does not match bytes; executable renamed `.png` or `.pdf` | GEN-01 `#1315` shipped the artefact upload endpoint with bounded allowlisted kinds, declared-type/signature checks, `nosniff`, and safe content disposition. `FileContentValidator` remains text-oriented and is not the binary proof. | Maintain mismatch/download-header regression coverage as new artefact kinds are admitted. | Magic-byte checks do not prove a complex file is benign; they only prevent simple type confusion. |
| Stored or preview XSS through file names, extracted text, OCR output, SVG/HTML payloads, or Markdown-like links | API CSP is deny-by-default, scripts/styles exclude `unsafe-inline`, and `X-Content-Type-Options: nosniff` is emitted. Vue escapes normal text interpolation. | GEN-06 `#1320`: render file names/text as text nodes, prohibit `v-html`, cover hostile names/text in component tests, and make extracted URLs inert by default. GEN-01 serves non-images as attachments. SVG/HTML are not allowed upload kinds. | Browser bugs and future unsafe rendering regressions remain possible; CSP is defense in depth, not a sanitizer. |
| Stored-XSS or content-sniffing through the content endpoint | GEN-01 `#1315` shipped authorized artefact lookup, allowlisted content types, safe content disposition, and `nosniff`; the richer Paper preview surface remains open. | GEN-06 `#1320` must preserve these response controls and review whether any future inline image preview needs a separate origin. | Allowed raster-image decoders still process hostile bytes in the browser. Downloading a malicious allowed file transfers risk to the user's local viewer. |
| Link trap, phishing URL, custom-scheme launch, or future server-side fetch/SSRF from extracted content | Extracted URLs have no authority and no shared-page fetcher is shipped. | Render URLs as inert text until explicit user action. If links become clickable, allow only reviewed `https` targets with visible host and safe opener attributes. Any future fetcher needs DNS/IP revalidation, private-network denial, redirect/size/time limits, and a separate threat review. | A permitted public HTTPS destination can still be malicious or change after review. The UI must never label it trusted merely because it was extracted. |
| Cross-user metadata/content enumeration or blob retrieval | Claims-first identity and board authorization are stable repository invariants; GEN-01 `#1315` shipped user-scoped repository/controller reads plus cross-user metadata/content/export/deletion coverage. | Maintain the same user scope for GEN-03/04/06 and the remaining Transcript linkage/API work in `#1305`. | A compromised authenticated account can access its own permitted content; local database/OS compromise is outside this model. |
| Provider egress without informed consent, or a local-only expectation silently becoming remote | Egress disclosure infrastructure exists; live provider use is configuration-gated. | GEN-03 `#1317`: pre-egress copy names the provider, content class, purpose, and local alternative; consent is explicit and revocable. GEN-04 must preserve the choice. | The user can consent without reading; provider jurisdiction, retention, and abuse monitoring remain external risks. |
| Misleading provenance or an output presented as a trusted source statement | Capture/proposal provenance and the review gate exist. | Store artefact/extraction/provider/model/prompt linkage. UI distinguishes source quote, extracted candidate, model inference, and approved board state; never show model output as verbatim evidence unless exact-span verification succeeds. | A source document itself can lie. Provenance proves origin and processing path, not truth. |

## Output-containment requirement

The accepted LLM extraction shape is data-only: a bounded list of task candidates with bounded title and verbatim evidence. It is not a proposal-operation envelope and it cannot contain tool calls.

Before GEN-04 routes artefact text through the LLM lane, the effective implementation must prove:

- raw JSON only; no prose/fences accepted as a substitute for strict parsing;
- no additional root or task fields;
- no `operations`, `actionType`, `targetType`, `tool_calls`, provider tool envelope, or unknown vocabulary;
- task and evidence length/count bounds;
- evidence is an exact substring of the delimited source;
- malformed, extra-field, or non-task output records an honest extraction failure and invokes the deterministic fallback;
- an explicit empty task array is distinguishable from parse/validation failure.

PR `#1312` currently owns the shared prompt/parser seam. This document intentionally does not edit that file from a competing branch. The canary manifest records the expected future verdicts; until a follow-up binds those fixtures to the effective parser/extractor, output containment for the LLM lane is an **open gate**, not verified behavior.

## Resource budgets required before extraction ships

GEN-02 `#1316` must choose and test concrete defaults for:

- maximum accepted upload bytes (coordinated with GEN-01);
- maximum PDF pages and parsed objects;
- maximum raster dimensions/decoded pixels before OCR;
- maximum extracted characters sent onward;
- per-extraction wall-clock timeout and cancellation propagation;
- per-user concurrent extraction work and provider quota;
- bounded error/provenance records with no raw content logging.

The limits belong in `docs/platform/CONFIGURATION_REFERENCE.md` when implemented. This threat-model slice deliberately does not invent defaults before the extractor exists.

## Fixture contract

Fixtures live under `backend/tests/Taskdeck.Application.Tests/Fixtures/untrusted-artefacts/`.

- `hostile-transcript.txt`: hidden/system-role and tool-vocabulary mimicry beside one honest commitment.
- `hostile-pdf-text.txt`: PDF-extracted prose containing an override/exfiltration demand and no genuine action item.
- `hostile-image-text.txt`: OCR-like text containing a fake tool call beside one legitimate next step.
- `response-extra-field.json`: task-shaped output with forbidden additional fields.
- `response-vocabulary-escape.json`: an operation/tool-shaped response instead of task candidates.
- `response-malformed.txt`: non-JSON model output.
- `manifest.json`: stable IDs, source kinds, allowed verdicts, forbidden outcomes, and expected fallback classifications.

The fixture-contract unit test independently pins each case ID, file, canary, source kind, allowed verdict, forbidden outcome, and hostile semantic signal. It also proves the manifest and fixture directory agree exactly, every referenced payload is bounded strict UTF-8, and JSON is parseable only where the manifest declares the exact `json` format. It does **not** claim an LLM resisted the fixtures. The prompt/extractor follow-up must execute each source fixture and assert either honest extraction grounded in the legitimate text or an empty verdict; every response fixture must take the deterministic fallback path.

## Delivery gates and ownership

| Gate | Owner | Required evidence before dependent merge |
| --- | --- | --- |
| Upload authz, streaming caps/quota, signature validation, safe content disposition | GEN-01 `#1315` — delivered by PR `#1341` | Cross-user, cap, signature-mismatch, download-header, export/delete tests shipped; preserve them |
| Local extraction budgets and typed failures | GEN-02 `#1316` — delivered by PR `#1346` | Bounded text extraction, cancellation, typed failure, and extraction-record tests shipped; vision pixel budgets remain GEN-03 scope |
| Consent and provider egress | GEN-03 `#1317` | Copy review plus off/on/revoke and no-egress-without-consent tests |
| Prompt rails and strict output containment | GEN-09 `#1323`, before GEN-04 `#1318` | Bind hostile transcript/PDF/image fixtures and malformed/extra-field responses to the effective prompt/parser path |
| Apply-time effective-target authorization | GEN-05 `#1319` — delivered by PR `#1339` | Cross-board tests bind effective parameter targets to the proposal's authorized scope; preserve them |
| Safe file-name/text/link rendering | GEN-06 `#1320` | Component tests proving escaping, inert URLs, and no unsafe HTML path |
| Overall beta posture and accepted residuals | REVIVAL-00 `#1311` | Link this submodel, confirm owners/gates, and record any accepted exception explicitly |

## Verification for this documentation-and-fixture slice

- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~UntrustedArtefactFixtureContractTests"`
- `node scripts/check-docs-governance.mjs`
- verify the new relative links in `SECURITY.md` and `docs/security/README.md` resolve to this file
- `git diff --check`

Not verified in this slice: model behavior, PR `#1312` parser strictness, apply-time effective-target authorization from `#1319` / PR `#1339`, PDF/image extraction, content endpoint headers, UI escaping, resource enforcement, consent flow, or end-to-end artefact triage.
