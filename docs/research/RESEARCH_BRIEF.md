# Taskdeck Deep-Research Brief

**Purpose:** Self-contained brief to paste into a deep-research LLM (Claude/GPT/Gemini/Perplexity deep-research or similar) to generate a prioritized dossier of ideas, technologies, and techniques that can upgrade Taskdeck from a "Trello-with-chat" into the personal workflow OS its thesis describes.

**How to use:** Copy the whole `## Prompt to the research agent` block into the deep-research tool. Amend the "What you should return" section if you want a different output shape. Ancillary files in `docs/research/` (AUDIT.md, LIMITATIONS.md, IDEAS_SEED.md) provide the context you may also want to paste or summarise for the agent.

---

## Prompt to the research agent

You are helping the maintainer of **Taskdeck** decide where to invest research time. Taskdeck is a local-first execution workspace for developers. The **stated thesis** is:

> "A local-first execution system for developers where capture is near-zero friction and the system maintains the board via reviewable proposals."

It is not a Trello clone. It is positioned as a **personal workflow OS**: board as system-of-record + structured intake + safe automation, with four non-negotiables:

1. **Review-first automation** — LLM/system produces diffs (proposals), user approves, nothing silently mutates board state.
2. **Local-first data ownership** — SQLite, single-user, on-device, privacy preserved.
3. **Keyboard-first, low-friction capture** — "dump messy notes" in <10 s.
4. **Traceable, inspectable intelligence** — every automated action links to a run, policy posture, and proposal.

### Current reality (as of 2026-04-24)

**Engineering is mature.** .NET 8 + Vue 3 + SQLite. ~160K LOC. ~7,070 automated tests. Clean architecture with enforced layer boundaries. 30 ADRs. 27 CI workflows. Zero open Priority I issues. Delivered: proposal lifecycle, SignalR realtime, Mock/OpenAI/Gemini LLM providers, tool-calling orchestrator (11 tools), MCP server (stdio + HTTP), PWA, agent tool registry + policy evaluator + one bounded template (`InboxTriageAssistant`), starter packs, outreach CRM primitives, integrations registry, calendar view, OIDC/MFA, account deletion/data export, webhook subsystem.

**Intelligence layer is thin.** Despite the LLM-first marketing framing:

- The **intent classifier** (`LlmIntentClassifier`) is compiled-regex + keyword substring matching with stemming/plurals. All three LLM providers (Mock/OpenAI/Gemini) share this static classifier to decide "is this actionable?". The LLM itself is **not asked to extract structured instructions** for the planner.
- The **planner** (`AutomationPlannerService.ParseInstructionAsync`) is rule/regex based, expecting near-literal syntax like `create card "title"`. It receives the raw user message, not an LLM-structured one. Natural language ("create onboarding tasks for non-technical folks") fails parse.
- **No embeddings, no local ML, no ONNX.** Grep for `embedding|SentenceTransformer|ONNX|MiniLM` returns zero hits in production code.
- **Knowledge docs use SQLite FTS5** (lexical full-text search) — there is no semantic / vector search.
- **Voice/transcript capture exist only as enum values** (`CaptureSource.Voice`, `CaptureSource.TranscriptPaste`, `CaptureSource.MeetingIntegration`). No Whisper, no transcription pipeline, no audio intake.
- **Agent substrate is partial.** Tool registry + policy evaluator + `InboxTriageAssistant` shipped. Missing: `AgentProfile`/`AgentRun`/`AgentRunEvent` entities, inspectable run traces, agent mode surfaces.
- **Tool-calling loop** exists (up to 5 rounds, 60 s timeout, OpenAI strict mode, Gemini) but is scoped to chat; capture/inbox triage does not use it.
- **Proposal summaries** are application-layer templated strings, not LLM-generated natural language.
- **No personalization.** No user-history model for suggesting columns/labels/priorities. No duplicate detection across captures. No temporal reasoning (due-date inference). No auto-grouping / topic clustering of captures.
- **"Too many tabs" product legibility problem.** 18 top-level views (Home, Today, Boards, Inbox, Review, Automations, Chat, Agents, Runs, Knowledge(planned), Notifications, Activity, Ops, Metrics, Calendar, Integrations, Archive, Settings). Three workspace modes (guided/workbench/agent) attempt to filter this but the filtering is partial; novice users still see raw IDs on some paths and dead-end empty states.
- **Zero external users** have validated the thesis yet.

The user (maintainer) has stated: *"Currently, it's just a mediocre Trello board with way too many tabs and things happening... the automated 'intelligent' system that allows to generate content on the boards or change stuff around is not great."*

### The core research question

**Given the thesis, the delivered substrate, and the intelligence gap: what should be the next 3–12 months of investment to make Taskdeck meaningfully "intelligent" while preserving its review-first, local-first trust model?**

Specifically, research and recommend along these axes. Include concrete technologies (libraries, model families, packages, with licences and maintenance notes), implementation sketches (2–6 lines of pseudocode where it helps), and evidence (benchmarks or references where available). Do not recommend anything that violates the local-first or review-first posture.

#### Research axis 1 — Natural-language → structured proposal

The single biggest reported gap. When the user types natural language, the system must reliably produce a structured proposal diff, not a regex parse failure. Investigate:

- **Structured-output techniques**: OpenAI strict JSON-schema mode, Gemini controlled generation, Anthropic tool-use schema, local options (`outlines`, `guidance`, `llguidance`, `jsonformer`, `lm-format-enforcer`). Tradeoffs for correctness, latency, cost, self-hostability.
- **Prompt patterns that work for low-friction intake**: few-shot with board-context examples; dynamic example selection via retrieval; "plan-then-execute"; self-consistency for ambiguity; self-critique before proposing.
- **Intent classification upgrades**: move from substring regex to embedding-based nearest-neighbour intent matching, or a small fine-tuned classifier. Compare `all-MiniLM-L6-v2` / `bge-small` / `e5-small` via ONNX Runtime in .NET. Cost/accuracy/latency budget.
- **Multi-intent and batch extraction**: "create cards for meeting setup, onboarding, and HR orientation" must produce N operations. What prompting or parsing strategies reliably handle list extraction without hallucination?
- **Ambiguity handling**: when the system is uncertain, how should it ask clarifying questions? (Taskdeck already has `ClarificationDetector` — what can replace its heuristic with something calibrated?) Research calibrated abstention / selective prediction.
- **Eval harness for NL→proposal**: how should Taskdeck measure whether its NLP is getting better? What open datasets or synthetic-data techniques apply? Consider using existing benchmarks (WebShop, ToolBench, BFCL, Gorilla, MetaTool) for reference.

#### Research axis 2 — Making the board understand itself (semantic + memory layer)

Currently the board is a flat relational model. Investigate how a semantic layer could enable:

- **Semantic search across cards/captures/knowledge** (not just FTS5). Local vector store options: SQLite-vec, sqlite-vss, LibSQL embeddings, Faiss via C# bindings, LanceDB, Chroma embedded, DuckDB VSS. Evaluate in-process vs sidecar. Embedding models for local, CPU-only inference via ONNX Runtime: `bge-small-en-v1.5`, `all-MiniLM-L6-v2`, `snowflake-arctic-embed-xs`, `gte-small`. Disk footprint, CPU cost, quality on short-text (task titles).
- **Duplicate detection / merge suggestions** — when capture produces something close to an existing card, propose merge instead of duplicate.
- **Topic / cluster discovery** across Inbox for batch triage: BERTopic, HDBSCAN + embeddings, or online clustering. Should this run on capture, on triage, or on a background job?
- **Entity extraction**: people, projects, dates, URLs, code symbols, file paths. Compare SpaCy / flair / local small LLM / regex + heuristics for the task-extraction domain. Cost tradeoff.
- **Temporal reasoning**: "due Friday", "by end of sprint", "next week" — date-parsing + calendar context. Compare `SugarDateTime`, `chrono.js` port, `dateutil`, or LLM date normalization with structured output.
- **Memory for personalization**: a small per-user model of "which column does this user tend to put new work in? which labels do they apply? which priorities?". Could be a simple Bayesian/kNN model in-process. Avoid cloud training loops.

#### Research axis 3 — Richer capture modalities (low-friction intake)

"Near-zero-friction capture" is the product's first promise. Research:

- **Voice capture**. Local-first transcription options: `whisper.cpp`, `faster-whisper` (CTranslate2), Whisper via ONNX Runtime in .NET. Real-time streaming vs recorded. Wake-word / push-to-talk patterns. Diarization (WhisperX, pyannote). Privacy posture — stays on device.
- **Clipboard watcher / global hotkey**. Patterns for Windows/macOS/Linux background capture with explicit-consent pipelines. OS-level hotkey libraries for Electron/Tauri/PWA.
- **Browser extension**. Chrome/Firefox extension that sends selected text / URL / page context to Taskdeck capture. Review trust model for site-permission boundaries.
- **IDE/editor plugins**. VS Code extension, JetBrains plugin — capture from current selection or TODO comments. Reuse MCP?
- **Email ingestion**. Email-to-capture via unique per-user forwarding address, or IMAP poll. Privacy/spam tradeoffs.
- **Meeting tools**. Local meeting transcription → capture. How to stay meeting-platform-agnostic? MCP-first? Consider Phonic, Recall.ai, or local audio with Whisper.
- **Screenshot/OCR intake**. Tesseract, PaddleOCR, or local LLM vision. Use case: screenshot a Slack/email, get captures.
- **Structured paste**. Markdown checklist → board (already delivered). Extend: paste a JIRA/Linear/GitHub issue URL → extract issue → create proposal; paste a Notion page → extract structure.

#### Research axis 4 — Safe agent substrate (beyond single-turn chat)

Taskdeck has the scaffolding for an agent layer (tool registry, policy evaluator, `InboxTriageAssistant`) but no runtime. Investigate:

- **Inspectable agent run architectures**. LangGraph, DSPy, Pydantic-AI agents, OpenAI Agents SDK, Microsoft Semantic Kernel. Focus on traceability: step-by-step run events, replayability, policy gates between steps.
- **Proposal-first write pattern for agents**. How do other systems (e.g., Cursor's rules, Aider's review mode, Anthropic's Computer Use, Devin's planning layer) balance autonomy vs human review? Pull design patterns, not implementations.
- **Long-running triage agents**. When the user isn't interacting, what work can an agent do safely on the inbox? Research "background cognition" patterns (Reflect, Mem, Notion AI Q&A) and how they handle trust.
- **Small-task local LLMs**. `llama.cpp`, `Ollama`, `LM Studio`, `mistral.rs`, `ONNX Runtime Generative AI`. Evaluate `Phi-3.5-mini`, `Qwen2.5-3B`, `Llama-3.2-3B`, `Gemma-2-2B` for classification/extraction on consumer CPUs. What tasks are feasible at <5 s on a laptop? Privacy posture.
- **Cost routing**. Route cheap/deterministic queries to local model, escalate to cloud only on uncertainty. "LLM router" patterns.
- **Eval harness for agents**. How should Taskdeck measure whether an agent run was "good"? Consider BFCL, AgentBench, SWE-Bench-style evaluation for the Taskdeck domain.

#### Research axis 5 — Product legibility, friction, retention

The "too many tabs" complaint is a real product problem. Research:

- **Progressive disclosure patterns** in power-tool software (Linear, Raycast, Arc, Obsidian). How do they hide depth from novices without hiding power?
- **First-run experience patterns** that teach the product-as-you-use-it (contextual onboarding, Arc-style interactive tours, Linear's in-app hints).
- **Retention/habit-formation** in daily-use tools. BJ Fogg, Nir Eyal hook model — but applied to a tool that explicitly doesn't want to addict. Research anti-addiction positive-friction design.
- **Command-first UIs**. Raycast/Superhuman/Linear/Arc command palette design. How to make the palette the *primary* interface for power users while the GUI remains inviting for novices.
- **Keyboard-first navigation standards**. WAI-ARIA, Vim-style binding schemes, chord vs sequence.
- **Aesthetic-usability effect**. Framework-level design token systems (Radix, shadcn, Reka) vs bespoke. Taskdeck already has Reka + shadcn-vue + `--td-*` tokens — what are the remaining gaps?
- **Dead-end empty states**. Research "action-oriented empty state" patterns.
- **"Maintenance overhead" score**. Can we measure how much admin tax users are paying? What telemetry would surface the guilt/attrition loop described in the thesis?

#### Research axis 6 — Trust, provenance, explainability

Review-first only works if the user trusts the review surface. Investigate:

- **Human-readable diffs for board changes**. What visualizations of "here's what will change" beat a flat diff? Compare Linear's change-preview, GitHub's PR preview, Notion's AI inline suggestions.
- **Proposal explanations**. Per-operation "why" rationales generated by the LLM — how to keep them grounded (not hallucinated). Research retrieval-grounded explanation.
- **Counterfactuals**. "If you approve this, here's what it will look like; if you reject, here's the current state." UX patterns and data-model implications.
- **Policy-as-code**. OPA/Cedar/Rego patterns for evaluating "is this agent allowed to do X on board Y?". Currently Taskdeck uses a simple `AgentPolicyEvaluator`.
- **Audit-chain UX**. How to make the `AgentRunEvent` timeline legible to a non-technical user.
- **Risk/impact scoring for proposals**. Current review card surfaces "risk/impact/source" but how is the score computed? Research calibrated risk scores for automation.

#### Research axis 7 — Platform, packaging, reach (secondary for this brief)

Briefly note:

- **Self-contained single-file distribution** for Windows/macOS/Linux (the v0.1.0 release goal). .NET native AOT, self-contained publish, Tauri 2.0 wrap, Electron.
- **PostgreSQL migration** path from SQLite for any multi-user future (already ADR'd).
- **Mobile**: PWA (already shipped) → Capacitor → Tauri Mobile. Tradeoffs.
- **Synchronization** for multi-device single-user: CRDT (Automerge, Yjs, Loro), Rqlite, Litestream, Turso/LibSQL replication. Preserve local-first.

### What you should return

Produce a **prioritized research dossier** organized as follows:

1. **Top-10 highest-leverage investments** — ranked. For each: the gap it closes, the recommended technique/tool, the reason it beats alternatives, an implementation sketch, realistic effort estimate, and risks to the trust model.

2. **Per-axis deep dives** — one section per research axis above. Within each: a table of 3–8 candidate technologies/techniques with fit/maturity/licence/maintainer notes, a recommended starting point, and 2–3 evaluation criteria.

3. **Combined reference architecture** — a one-page diagram-in-prose describing how the pieces connect: where intent classifier lives, where embeddings live, where voice capture lives, how agent runs are recorded, how local and cloud LLMs coexist. Keep it faithful to the review-first / local-first posture.

4. **12-week phased plan** — week-by-week (or fortnight-by-fortnight) sequence of experiments and integrations. Each phase: what to ship, what to measure, what would cause a pivot.

5. **Eval / measurement appendix** — how Taskdeck can measure friction, capture latency, proposal acceptance rate, intent-classifier accuracy, agent run-success rate. Concrete metric definitions and logging recommendations.

6. **Reading list** — 20–40 links (papers, blog posts, docs) grouped by axis. Prefer primary sources and well-maintained OSS projects.

### Constraints and anti-goals

- **Do not** recommend anything that silently mutates board state. All writes produce proposals.
- **Do not** recommend sending user data to third-party services beyond the LLM provider the user already chose.
- **Do not** recommend large infrastructure investments (Kubernetes, separate GPU servers, enterprise observability stacks) unless they pay back clearly in user-visible value.
- **Do not** recommend rewrites. The existing clean-architecture substrate is solid.
- **Prefer** techniques that can run offline or with local models for the privacy-conscious persona.
- **Prefer** techniques with first-class .NET 8 bindings or well-documented HTTP/stdio interfaces. Taskdeck's language substrate is C# backend + TypeScript frontend.
- **Assume** solo/small-team maintenance. No idea should require a dedicated ML engineer.

### Format

Markdown. Tables where comparisons matter. Pseudocode only where it clarifies an interface. Cite sources inline as `[label](url)`. Do not invent library names, versions, or paper citations — if you are uncertain, say so and explain what to verify.

---

## Companion context for the research agent (optional paste)

If the research tool accepts larger context, also attach:

- `docs/research/AUDIT.md` — current vs intended state of Taskdeck (one page)
- `docs/research/LIMITATIONS.md` — structured limitation inventory with severities
- `docs/research/IDEAS_SEED.md` — my own seed list of ideas/questions to validate
- `docs/STATUS.md` (sections: Project Summary, Current Implementation Snapshot, Known Gaps and Risks)
- `docs/GOLDEN_PRINCIPLES.md` (all 9 principles — non-negotiable)
- `docs/InReview/HUMAN/01_PRODUCT_THESIS.md` (the north-star thesis)
- `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md` (a dedicated gap analysis for the core NLP problem)
