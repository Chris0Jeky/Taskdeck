# Ideas Seed For Taskdeck Research

Last Updated: 2026-04-24
Source: derived from `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

This is a candidate pool, not a roadmap and not an externally validated vendor
matrix. The deep-research pass should verify current license, maturity,
maintenance status, pricing, and benchmarks from primary sources.

## 1. Natural Language To Proposal

- Structured outputs for capture extraction: OpenAI JSON-schema strict output,
  Gemini controlled generation, Anthropic tool-use schemas, or local constrained
  decoding libraries.
- A canonical `IntentEnvelope`: source, raw text, candidate operations,
  confidence, ambiguity, source spans, and rationale.
- Dynamic examples from accepted proposals: retrieve similar prior examples and
  include them in the extraction prompt.
- Calibrated abstention: propose when confident, ask a short clarification when
  ambiguous, store as capture when not actionable.
- A versioned intelligence fixture corpus under `tests/fixtures/intelligence/`.

## 2. Semantic Memory

- Local embeddings for cards, captures, knowledge chunks, and accepted proposal
  examples.
- SQLite-friendly vector options to investigate: sqlite-vec, sqlite-vss, LibSQL
  embeddings, DuckDB VSS, or a lightweight sidecar only if local-first remains
  intact.
- Short-text embedding models to evaluate locally through ONNX or HTTP sidecars:
  all-MiniLM-L6-v2, bge-small, e5-small, gte-small, or similar current models.
- Duplicate detection: new capture vs existing cards/captures.
- Retrieval-grounded proposal explanations with citations to capture/card IDs.

## 3. Capture Modalities

- Transcript improvements first: better transcript paste/file handling,
  diarization metadata, and meeting-action extraction fixtures.
- Voice capture: local transcription via whisper.cpp, Whisper.net,
  faster-whisper, Vosk, or ONNX-based Whisper.
- Browser extension: selected text/URL -> local Taskdeck capture.
- IDE extension: selected code/TODO/comment -> Taskdeck capture, potentially
  reusing MCP.
- OS-level capture: global hotkey/menu-bar capture with explicit consent.
- OCR/screenshot intake: Tesseract, PaddleOCR, or local/opt-in vision models.
- Email/calendar ingestion only after the local capture loop proves useful.

## 4. Bounded Agents

- One useful recurring assistant before broad agent breadth: Inbox Triage
  Assistant that clusters captures, extracts proposals, and records a trace.
- Run event trace model: input, tool calls, policy decisions, proposal IDs,
  errors, duration, and token/cost metadata.
- Scheduler/trigger model with review-first output only.
- Agent framework candidates to research: Microsoft Semantic Kernel, LangGraph,
  OpenAI Agents SDK, Pydantic AI, DSPy. Prefer fit with .NET or clean HTTP
  boundaries over framework novelty.

## 5. Product Legibility

- Guided mode as the primary novice model: Home, Today, Inbox, Review, Boards.
- Advanced capabilities reachable through command palette and contextual links.
- Review UX inspired by code review: clear diff, source, risk, rationale,
  accept/edit/reject, and what-happens-next.
- Empty-state design that starts the next useful action rather than explaining
  the page.

## 6. Measurement

- Local-first friction metrics: time from capture to proposal decision, proposal
  acceptance/edit/rejection rates, abandoned captures, direct-board-edit ratio,
  clarification frequency, and time to first useful proposal.
- Product telemetry must remain opt-in, content-free, and inspectable.
- Content-bearing traces belong in local audit/proposal systems, not remote
  telemetry.

## Anti-Ideas

- Silent autonomous board mutation.
- Cloud-only vector stores or model calls for the local-first persona.
- More top-level routes before reducing current route confusion.
- Rewrites.
- Heavy enterprise infrastructure before beta learning.
