# Untrusted Artefact Threat Model

Last Updated: 2026-07-13

Owner: Taskdeck maintainers

Status: scoped control plan; runtime prompt rails and artefact extraction are not yet shipped

Related work: REVIVAL-00 `#1311`, transcript triage `#1304` / PR `#1312`, GEN-01 `#1315`, GEN-02 `#1316`, GEN-03 `#1317`, GEN-04 `#1318`, GEN-06 `#1320`, GEN-09 `#1323`

## Purpose and scope

This document is the content-ingestion submodel for the REVIVAL-00 beta threat model. REVIVAL-00 owns the overall deployment and beta decision; this document owns attacker-controlled PDFs, images, text files, transcripts, pasted text, file names, extracted URLs, and future shared-page content from ingress through review.

The honest posture is layered mitigation, not a claim that prompt injection can be solved. Human review is a real security boundary, but it must not be the only boundary once extracted artefact text enters an LLM prompt.

This slice adds the model and reusable canary fixtures only. It does **not** change PR `#1312` prompt/parser code, wire an extractor, serve an artefact, change consent copy, or claim the injection suite is green. Those gates remain owned by the dependency matrix below.

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
- Automation-originated board writes remain proposal-first. Preview must equal the effective revision Apply executes.
- Escape rendered text by construction; do not render extracted HTML. Treat extracted URLs as inert text until an explicit safe-link interaction.
- Do not log artefact bytes, base64, raw extracted text, or secrets recovered from content.
- Tell the user what content leaves the device before provider egress and preserve provider/model/prompt provenance.

## Threat and control matrix

| Threat scenario | Existing control as of 2026-07-13 | Required new control and owner | Accepted residual risk after controls |
| --- | --- | --- | --- |
| Embedded prompt instructions such as “ignore previous instructions,” system-role mimicry, or fake policy text | Review-first proposal approval and deterministic fallback exist. The LLM transcript lane is still an open PR and therefore not a shipped control. | PR `#1312` follow-up: delimit untrusted data, state that content is never instructions, run the canaries in this PR, and preserve provider/prompt provenance. GEN-04 `#1318` must not merge before these rails. | Models can still follow novel injection patterns or extract a malicious sentence as a plausible task. Human review and operation containment remain mandatory. |
| Tool/operation-vocabulary mimicry embedded in text (`delete board`, JSON, XML, fake tool calls) | Board mutation is proposal-first; the executor has a finite apply-time registry and revalidates permission. | Strict LLM output validation must reject extra fields, tool-call envelopes, operation payloads, and vocabulary escapes before proposal creation. Fixtures `response-extra-field.json` and `response-vocabulary-escape.json` are future regression inputs. | A malicious phrase may appear as inert task title/evidence. It must never become executable vocabulary without a separately validated proposal operation. |
| Indirect injection asking the model to reveal system prompts, other captures, secrets, or connector data | Provider requests are purpose-scoped; telemetry/log redaction policies prohibit secret content. | Prompt discipline must forbid disclosure and provide only the current bounded artefact text. Provider adapters must not attach tools or unrelated workspace context to extraction calls. | The provider necessarily receives the consented content. Provider-side retention and compromise remain third-party risks disclosed to the user. |
| Decompression bomb, oversized image, extreme pixel dimensions, PDF page/object bomb, or huge extracted text | Text-oriented validators cap current text paths; API rate limiting exists. Those controls do not validate arbitrary binary containers. | GEN-01 `#1315`: streaming byte cap/quota and magic-byte allowlist. GEN-02 `#1316`: page, decoded-pixel, extracted-character, memory, and wall-clock budgets with cancellation. | A payload inside every individual limit can still consume meaningful local CPU or provider quota. Conservative defaults and observable cancellation are required. |
| Malformed, truncated, encrypted, cyclic, or polyglot container exploiting a parser | Unhandled failures are expected to fail the request/job rather than write board state. | Isolate extraction behind `IArtefactTextExtractor`; catch typed parser failures, cap recursion/object traversal, keep libraries patched, and record a safe failed-extraction status without raw payload logs. | Third-party parser vulnerabilities remain possible. Dependency scanning and prompt-free sandbox/process isolation may be needed if real incidents justify it. |
| Declared MIME does not match bytes; executable renamed `.png` or `.pdf` | No general artefact upload is shipped on `main`. `FileContentValidator` is intentionally text-oriented and must not be reused for binary proof. | GEN-01 `#1315`: allow both declared type and magic-byte signature, reject mismatch, use `nosniff`, and never execute or transform unsupported formats. | Magic-byte checks do not prove a complex file is benign; they only prevent simple type confusion. |
| Stored or preview XSS through file names, extracted text, OCR output, SVG/HTML payloads, or Markdown-like links | API CSP is deny-by-default, scripts/styles exclude `unsafe-inline`, and `X-Content-Type-Options: nosniff` is emitted. Vue escapes normal text interpolation. | GEN-06 `#1320`: render file names/text as text nodes, prohibit `v-html`, cover hostile names/text in component tests, and make extracted URLs inert by default. GEN-01 serves non-images as attachments. SVG/HTML are not allowed upload kinds. | Browser bugs and future unsafe rendering regressions remain possible; CSP is defense in depth, not a sanitizer. |
| Stored-XSS or content-sniffing through the content endpoint | No artefact endpoint is shipped on `main`. | GEN-01 `#1315`: correct allowlisted `Content-Type`; `Content-Disposition: attachment` for PDF/text/non-image content; `nosniff`; authorized lookup before bytes. Review whether image responses need a separate origin before any richer inline preview. | Allowed raster-image decoders still process hostile bytes in the browser. Downloading a malicious allowed file transfers risk to the user's local viewer. |
| Link trap, phishing URL, custom-scheme launch, or future server-side fetch/SSRF from extracted content | Extracted URLs have no authority and no shared-page fetcher is shipped. | Render URLs as inert text until explicit user action. If links become clickable, allow only reviewed `https` targets with visible host and safe opener attributes. Any future fetcher needs DNS/IP revalidation, private-network denial, redirect/size/time limits, and a separate threat review. | A permitted public HTTPS destination can still be malicious or change after review. The UI must never label it trusted merely because it was extracted. |
| Cross-user metadata/content enumeration or blob retrieval | Claims-first identity and board authorization are stable repository invariants. | GEN-01 `#1315`: every repository/controller query is user-scoped; cross-user tests cover metadata, bytes, export, and deletion without existence leaks. | A compromised authenticated account can access its own permitted content; local database/OS compromise is outside this model. |
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

The fixture-contract unit test proves the files are present, parseable where promised, uniquely identified, bounded, and carry stable canary markers. It does **not** claim an LLM resisted them. The prompt/extractor follow-up must execute each source fixture and assert either honest extraction grounded in the legitimate text or an empty verdict; every response fixture must take the deterministic fallback path.

## Delivery gates and ownership

| Gate | Owner | Required evidence before dependent merge |
| --- | --- | --- |
| Upload authz, streaming caps/quota, signature validation, safe content disposition | GEN-01 `#1315` | Cross-user, cap, signature-mismatch, download-header, export/delete tests |
| Local extraction budgets and typed failures | GEN-02 `#1316` | Page/char/pixel/time/cancellation tests against hostile fixtures |
| Consent and provider egress | GEN-03 `#1317` | Copy review plus off/on/revoke and no-egress-without-consent tests |
| Prompt rails and strict output containment | PR `#1312` follow-up, no later than GEN-04 `#1318` | Hostile transcript/PDF/image fixture suite plus malformed/extra-field fallback tests |
| Safe file-name/text/link rendering | GEN-06 `#1320` | Component tests proving escaping, inert URLs, and no unsafe HTML path |
| Overall beta posture and accepted residuals | REVIVAL-00 `#1311` | Link this submodel, confirm owners/gates, and record any accepted exception explicitly |

## Verification for this documentation-and-fixture slice

- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter "FullyQualifiedName~UntrustedArtefactFixtureContractTests"`
- `node scripts/check-docs-governance.mjs`
- verify the new relative links in `SECURITY.md` and `docs/security/README.md` resolve to this file
- `git diff --check`

Not verified in this slice: model behavior, PR `#1312` parser strictness, PDF/image extraction, content endpoint headers, UI escaping, resource enforcement, consent flow, or end-to-end artefact triage.
