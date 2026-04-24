# Ideas Seed — Taskdeck Intelligence Upgrade

**Purpose:** Seed list of techniques, technologies, and patterns for the maintainer to take into deep research. This is not a roadmap — it is an unfiltered candidate pool. Each idea carries a short "why it might matter for Taskdeck" note. Group ordering is thematic, not prioritised.

Use it alongside `RESEARCH_BRIEF.md` to make sure the deep-research agent considers options the maintainer may not have raised.

---

## 1. Natural language → structured proposal (the core gap)

The reigning pain: regex-based intent classifier + rule-based planner loses any capture that isn't near-literal syntax. All three providers (Mock/OpenAI/Gemini) share the same static classifier.

### 1.1 Structured-output generation

- **OpenAI Structured Outputs** (JSON schema with `strict: true`) — already in tool-calling orchestrator; extend to the capture/triage path that currently uses regex.
- **Gemini controlled generation** — `response_schema` with type-safe JSON output.
- **Anthropic tool use** — Claude's tool schema produces validated arguments; pattern extends naturally to Taskdeck's proposal operations.
- **`outlines`** (Python, via sidecar or HTTP) — regex- and JSON-schema-constrained decoding for open-source models.
- **`llguidance`** (Rust) — high-performance grammar-constrained decoding; can be embedded.
- **`lm-format-enforcer`** — JSON/regex guards for any HF model.
- **`instructor` (Python)** — Pydantic-typed LLM outputs with retry-on-validation-fail. Pattern applicable in .NET via `Instructor.NET`.
- Why it matters: the planner should receive *structured LLM output*, not raw user text. Structured outputs turn "create onboarding cards for three people" into a validated array of operations.

### 1.2 Intent classification via embeddings

- **`all-MiniLM-L6-v2`** / **`bge-small-en-v1.5`** / **`gte-small`** / **`snowflake-arctic-embed-xs`** — small sentence embedders runnable via ONNX Runtime in .NET. Replace keyword classifier with nearest-neighbour intent lookup.
- **`Microsoft.ML.OnnxRuntime`** — native .NET inference.
- **`SBERT.Net`** / **`LLamaSharp`** — .NET wrappers worth evaluating.
- Why: embeddings handle "set up", "build", "spin up" etc. without regex explosion. Maintenance becomes "add an intent example" instead of "add a regex".

### 1.3 Few-shot with dynamic example retrieval

- Maintain a corpus of (user-message → operation) pairs from accepted proposals. At inference, retrieve the top-k most similar prior examples and prepend them to the prompt.
- Effectively self-improving: every approved proposal becomes a training example.
- Libraries: any vector store (see §2) + LLM call. No training loop required.

### 1.4 Chain-of-thought / plan-then-execute

- Taskdeck already has tool-calling. Upgrade to a two-step pattern: first LLM produces a plan (list of intended operations with rationale); second step produces the concrete arguments for each. Reduces hallucinated operations.
- References: ReAct, Plan-and-Solve, `planner-executor` pattern.

### 1.5 Self-consistency and uncertainty

- Sample N completions with temperature; propose only what's consistent across samples. Fall back to clarifying question when disagreement is high.
- Calibrated-abstention literature: Kamath et al. "Selective Question Answering"; "Language Models (Mostly) Know What They Know".

### 1.6 Multi-instruction parsing

- Current planner is single-instruction. Need to parse "create cards for X, Y, Z" or pasted checklists.
- Techniques: constrained-output arrays, or explicit "split the request into atomic operations, then handle each" prompting.

### 1.7 Local-LLM fallback for privacy/cost

- **Ollama**, **LM Studio**, **`llama.cpp`**, **`mistral.rs`**, **ONNX Runtime Generative AI**.
- Small-model candidates: `Phi-3.5-mini` (3.8B), `Qwen2.5-3B`, `Llama-3.2-3B`, `Gemma-2-2B`.
- Task: run the intent classifier and parser locally; reserve cloud LLM for ambiguous/complex cases.
- **LLM routing** patterns: `RouteLLM`, confidence-based escalation.

### 1.8 Eval harness

- Build a "NL → expected proposal" dataset. Start hand-written, grow with approved proposals.
- Benchmarks to learn from: **BFCL** (tool calling), **ToolBench**, **MetaTool**, **Gorilla**.
- Regression test intent accuracy, parse success, and proposal acceptance rate per change.
- Use **Ragas** / **DeepEval** / **promptfoo** patterns for LLM evals in CI.

---

## 2. Semantic board / memory layer

Taskdeck has FTS5 (lexical). Zero embeddings. This blocks duplicate detection, semantic search, "find related", clustering, personalisation.

### 2.1 In-process vector stores (compatible with single-SQLite)

- **`sqlite-vec`** — Rust-based SQLite extension, C-linkable, single-user friendly. Most promising for Taskdeck.
- **`sqlite-vss`** — FAISS-backed, a bit heavier.
- **LibSQL** — SQLite fork with embeddings column type and sync.
- **DuckDB VSS** — if willing to co-host a DuckDB read-model.
- **LanceDB** — embedded, columnar, good for scale; less traditional.
- **Chroma embedded** — Python-focused, sidecar.

### 2.2 Sidecar vector services

- **Qdrant**, **Weaviate**, **Milvus Lite**, **Vespa**. Probably overkill for local-first.

### 2.3 Uses in Taskdeck

- **Semantic search** across cards/captures/knowledge ("all cards about onboarding, whether they say that word or not").
- **Duplicate detection** on capture: embed the new capture, cosine-search prior cards/captures, if score > θ propose merge.
- **Cross-board context**: "find cards on other boards that mention this person".
- **In-context example selection** for §1.3 few-shot prompting.
- **Retrieval-augmented proposal summaries** — ground the summary in the cards it touches.

### 2.4 Personalisation (per-user model)

- **Column predictor**: which column does this user usually put new "bug / idea / meeting-note / todo" work in? Small classifier, or kNN over prior moves.
- **Label predictor**: predict likely labels from card text + prior patterns.
- **Priority predictor**: if user historically bumps certain phrasing to high priority.
- All feasible as simple in-process models — no cloud training.

### 2.5 Topic / cluster discovery

- **BERTopic** (Python) — topic modeling over captures for batch triage.
- **HDBSCAN + embeddings** — density-based clustering.
- **Incremental clustering** (BIRCH, online-kmeans) for streaming capture.
- Use case: "you have 47 inbox items; they cluster into 3 topics — here are proposals".

### 2.6 Entity extraction

- **SpaCy** / **flair** / **`NER` via small LLM**.
- For task domain: people (for @mentions), projects, dates, URLs, code symbols, file paths.
- Regex + heuristics still fine for URLs/file paths.

### 2.7 Temporal reasoning

- **`SugarDateTime`** (.NET) — fuzzy "next Friday".
- **`chrono.js`** port patterns.
- **LLM-based date normalisation** with constrained output `{iso: "2026-04-26", confidence: "high"}`.
- Calendar-aware: "end of sprint" should know sprint cadence.

---

## 3. Capture modalities (near-zero-friction intake)

### 3.1 Voice capture (local)

- **`whisper.cpp`** — cross-platform, CPU, offline.
- **`faster-whisper`** (CTranslate2) — faster CPU/GPU inference.
- **Whisper via ONNX** — native .NET path.
- **`Whisper.net`** — .NET binding for whisper.cpp.
- **`Vosk`** — older but lightweight streaming recogniser.
- **WhisperX / pyannote** — diarization (speaker separation) for meetings.
- **Silero VAD** — voice activity detection, lightweight.
- Interaction patterns: global push-to-talk hotkey; always-on wake word (disabled by default for privacy); menu-bar record button.

### 3.2 Clipboard / global hotkey

- Windows: `RegisterHotKey` via P/Invoke.
- macOS: `NSEvent.addGlobalMonitor` (requires accessibility permission).
- Linux: X11 `XGrabKey` / Wayland via compositor-specific APIs.
- Cross-platform libraries: `InputSimulator`, `GlobalHotKey.NET`, `node-global-key-listener` (for PWA/Electron).
- Tauri has built-in `globalShortcut`; Electron has `globalShortcut`.

### 3.3 Browser extension

- Manifest V3 Chrome/Firefox extension. Send selected text + URL + optional screenshot to Taskdeck HTTP API (local).
- Pair with a local-only trust model (extension talks only to `localhost:5000`).

### 3.4 IDE / editor capture

- **VS Code extension** — capture from selection; capture TODO/FIXME comments; MCP client reuse.
- **JetBrains plugin** — same surface.
- Taskdeck already has an MCP server over HTTP + stdio → any MCP-capable editor (Cursor, Claude Code, Windsurf) can both read and write Taskdeck.

### 3.5 Email ingestion

- Unique per-user forwarding address (requires optional hosted component); or IMAP poll for local.
- Email → structured capture with subject/body/attachments → proposal.

### 3.6 Meeting intake

- **`Recall.ai`** — bot-joins-meeting SaaS (hurts local-first, but covers Zoom/Teams/Meet uniformly).
- **Local audio capture** + Whisper → capture. Better privacy posture.
- **`Phonic`**, **`Vapi`**, others.

### 3.7 Screenshot / OCR

- **Tesseract** / **Tesseract.NET**.
- **PaddleOCR** — strong on mixed-language.
- **LLM vision** (GPT-4o-mini, Gemini Flash, Claude Haiku 4.5) — quick and flexible but latency/cost.
- Use case: screenshot a Slack / email / PDF → extracted text → capture.

### 3.8 Structured paste extensions

- Paste Linear/Jira/GitHub URL → fetch → extract → propose.
- Paste Notion page → traverse structure → propose.
- Paste transcript (already enum-declared, not wired) → LLM extract action items → propose.
- Paste code diff / PR → extract review checklist → propose.

---

## 4. Agent substrate (making the agent layer real)

### 4.1 Runtime primitives (known gap)

- `AgentProfile`, `AgentRun`, `AgentRunEvent` entities (already scheduled as Horizon D).
- Run trace schema: step ID, tool called, arguments, result, duration, policy decision, outcome.
- Replay / dry-run mode.

### 4.2 Agent frameworks to study

- **LangGraph** (Python) — state-machine agent graphs; run trace is first class.
- **DSPy** — programmatic prompt engineering with compiled optimisers.
- **OpenAI Agents SDK** — tool registry + handoffs.
- **Microsoft Semantic Kernel** (.NET native) — most architecturally aligned with Taskdeck.
- **Pydantic AI** — typed agents.
- **Autogen** / **CrewAI** — multi-agent; probably out of scope.

### 4.3 Proposal-first agent patterns

- Inspiration: Cursor's rule files, Aider's review mode, Anthropic's Computer Use, Devin's planning layer, MetaGPT's review gate.
- Key pattern: write tools emit *pending-proposal* entities, never direct mutations. Already Taskdeck's convention — extend it to all agent-initiated writes.

### 4.4 Background triage agent

- Periodic `InboxTriageAssistant` runs over un-triaged captures; produces batched proposals the user can approve en-masse.
- Research: "autonomous inbox" patterns in Reflect, Mem, Notion AI Q&A; consent & UI patterns.

### 4.5 Specialised agents worth building

- **Daily planner** — at morning, assemble today's agenda from cards + captures + calendar.
- **Duplicate resolver** — after capture, find near-duplicate cards and propose merges.
- **Stale-card watcher** — identify cards untouched for N days, propose archive or refocus.
- **Meeting debrief** — take a pasted transcript, produce per-attendee action-item proposals.
- **Retro helper** — weekly retrospective from the week's audit log.
- **Project bootstrap** — from a one-paragraph description, propose a full board (columns + labels + template cards).

### 4.6 Policy / safety

- **OPA** (Rego), **Cedar** — policy-as-code engines. Overkill now but worth noting.
- **Taskdeck's current `AgentPolicyEvaluator`** is allowlist + risk-level. Candidate upgrades: per-board policy, per-tool rate limits, dry-run-only mode, "explain why blocked" messages.

### 4.7 Evaluation

- **BFCL** (Berkeley Function-Calling Leaderboard) — tool-use eval.
- **AgentBench**, **AgentHarness** — multi-step agent evals.
- **Taskdeck golden scenarios** — record 20–50 realistic multi-step workflows and replay on every change.

---

## 5. Product legibility, friction, retention

### 5.1 Progressive disclosure

- Study: Linear (hidden depth), Raycast (command-first), Arc (hidden tabs via Spaces), Obsidian (plugin-gated power), Notion (templates hide complexity).
- Pattern: default to the "guided" mode's shape but reveal workbench power on discovery (chord key, palette command, menu item).

### 5.2 Retention / habit design

- **BJ Fogg behavior model**, **Nir Eyal hook model** — but inverted: design for *voluntary* daily return, not compulsion.
- Research "positive-friction design" (Cal Newport, Tiago Forte "Building a Second Brain" mental models).

### 5.3 Command-first UI

- **Raycast extensions**, **Superhuman keyboard shortcuts**, **Linear Cmd-K**, **Arc Command Bar**.
- Taskdeck already has Ctrl+K palette — research how to make it the primary interface for power users.

### 5.4 Empty states / onboarding

- Taskdeck already recognises "dead-end empty states" as a violation. Research "contextual onboarding" (Intercom, Appcues patterns) and "empty-state-as-primary-surface" (Linear's blank board).

### 5.5 Maintenance-overhead telemetry

- Key question the product thesis raises: are users paying admin tax? Measure it.
- Metrics: time from capture → first proposal decision, approval rate, dismissal rate, session length, return cadence, "abandoned captures" count.
- Compare to Notion's "template accept" funnel, Linear's "issue aging" views.

### 5.6 "Focus mode" and productive use

- Cal Newport deep-work patterns; "Today" view is already part of this.
- Consider "one-thing" mode (zero-distraction single-card view) like Things 3 or Things-for-Mac.

---

## 6. Trust, provenance, explainability

### 6.1 Diff visualisation

- Study: Linear change-preview, GitHub PR diff, Notion inline AI suggestions, Grammarly's accept/reject-per-suggestion UI.
- Card-level vs board-level diff grouping: what makes review pleasant, not a chore?

### 6.2 Grounded explanations

- "Why this proposal?" generated by the LLM, grounded in the exact cards/captures it references. Research retrieval-grounded generation.
- Avoid hallucinated rationales — require citation of source capture/card ID.

### 6.3 Counterfactual preview

- "If you approve: here's the new state / if you reject: unchanged." Reduce decision anxiety.

### 6.4 Risk/impact scoring

- Ordinal risk labels (low/medium/high) already exist in review cards. Research calibration: measured from operation type, board-state scope of change, destructiveness.
- Evidence: Google's ML-ops "impact prediction" patterns.

### 6.5 Audit-chain UX

- AgentRunEvent timeline — visual & filterable.
- Provenance chain: capture → triage → proposal → execution → audit event. One glance view.

---

## 7. Small-model local inference (privacy-first path)

### 7.1 Runtimes

- **`Ollama`** — zero-config, HTTP API.
- **`llama.cpp`** — CPU/GPU, gguf format.
- **`ONNX Runtime Generative AI`** — native .NET path.
- **`mistral.rs`** — Rust, fast.
- **`LM Studio`** — GUI + HTTP server; developer-friendly.

### 7.2 Model families viable for CPU-only

- `Phi-3.5-mini-instruct` (3.8B, strong for size)
- `Qwen2.5-3B-Instruct`
- `Llama-3.2-3B-Instruct`
- `Gemma-2-2B`
- `SmolLM2-1.7B`
- For classification/NER: distilled BERT variants, `DistilRoBERTa-NLI`.

### 7.3 Taskdeck-shaped tasks a small model can do

- Intent classification (currently regex).
- Entity extraction (people, dates, URLs).
- Capture deduplication triage (lightweight).
- Proposal summary ("what changed in 20 words").
- Short-form clarification questions.
- Title cleanup / casing normalisation.

### 7.4 Routing / orchestration

- **`RouteLLM`** patterns.
- **Taskdeck-specific router**: local small model first; escalate to cloud on uncertainty/complexity.

---

## 8. Data richness and external context

### 8.1 Integrations with proposal-first semantics

- GitHub (issues/PRs → captures with code context).
- GitLab, Linear, Jira, Asana, Trello (bidirectional import/export).
- Google Calendar, iCal (temporal context).
- Slack / Discord (via export or bot).
- Obsidian / Notion (markdown round-trip).
- **Zapier / n8n / Pipedream**-style inbound webhooks already supported — broaden.

### 8.2 Knowledge intake

- Transcript paste (enum exists, not wired).
- PDF ingestion (pdfpig, PdfPlumber, Marker).
- Markdown/Obsidian vault sync.
- Long-term context → knowledge docs → searchable via FTS + semantic.

### 8.3 "Second brain" patterns

- Tiago Forte BASB (Build a Second Brain).
- Zettelkasten / Roam Research / Obsidian graph patterns.
- **How does Taskdeck differ?** It is *execution*-first, not notes-first. Notes serve the board, not vice versa. Important positioning.

---

## 9. Collaboration and sync (when thesis expands past solo)

### 9.1 CRDT stacks

- **`Automerge`** / **`Yjs`** / **`Loro`** — conflict-free collaborative editing.
- Patterns for Taskdeck board state: per-card CRDT vs per-board.

### 9.2 Local-first sync

- **Litestream** — SQLite replication to S3.
- **LibSQL / Turso** — replicated SQLite.
- **Rqlite** — clustered SQLite.
- **ElectricSQL** — Postgres ↔ SQLite sync with CRDT.

### 9.3 Peer-to-peer

- **Iroh**, **libp2p** — for truly server-less local-first multi-device.

---

## 10. Observability for a personal tool

### 10.1 Per-user insights (not surveillance)

- "This week: 47 captures, 38 approved proposals, 4 rejected, 3 stale." Show users their own flow.
- Privacy stance: local-only analytics by default.

### 10.2 Dev-side observability

- Already have OpenTelemetry. Research lightweight self-hosted OTLP receivers (SigNoz, Jaeger) and dashboarding (Grafana).

### 10.3 LLM observability

- **Langfuse**, **Helicone**, **LangSmith**, **OpenLLMetry**.
- Track per-provider latency, cost, token usage, failure mode.

---

## 11. Eval / measurement

### 11.1 NLP pipeline eval

- Intent classification accuracy (held-out set).
- Parser success rate on held-out NL inputs.
- Proposal acceptance rate per NL input class.
- Clarification-loop success rate.

### 11.2 Product KPIs mapped to the thesis

- Time from capture to decision.
- Rate of "decayed" captures (never decided).
- Weekly active retention vs control group.
- User-reported friction (micro-survey).

### 11.3 Eval tooling

- **promptfoo**, **Ragas**, **DeepEval**, **`gpt-judge`** patterns.
- Human-in-the-loop labelling UI for correctness.

---

## 12. Cross-cutting / ambient ideas

### 12.1 "Why is this here?" for every card

- Every card should answer: where did you come from, why are you here, what happens if I don't act. Research provenance-UI patterns.

### 12.2 Board-as-graph views

- Cards have dependencies, references, labels. Study **Neo4j** patterns or in-process graph view.
- Visual graph: a view that shows "blocked-by" chains as a DAG.

### 12.3 Pluggable "capture transformers"

- Each capture goes through a pipeline: detect type → extract entities → enrich → triage → propose. Pipeline should be extensible.

### 12.4 User-programmable shortcuts

- Raycast-style "snippets" that expand into operations: `;onb` → proposal to create 3 onboarding cards from template.

### 12.5 "Offline-first" AI routing

- Default: local small model. Cloud LLM only on explicit opt-in per session or per prompt.

### 12.6 "Privacy tier" controls

- Per-card sensitivity flag. Sensitive cards never leave device regardless of provider config.

### 12.7 Bidirectional streaming UX

- As user types in capture, start showing a live proposal preview. Think Superhuman's Inbox AI.

### 12.8 Positive notification model

- Not "you have 47 things", but "here's the most useful next action". Research "gentle nudge" patterns.

### 12.9 Demo-as-onboarding

- Taskdeck has extensive demo tooling. Research whether demo playback can *become* the first-run experience (interactive tour vs canned walkthrough).

### 12.10 Reverse-search onboarding

- Show existing power users' flows ("see how others use Inbox") — when scale allows.

### 12.11 Dark-mode / focus-time theming

- Honeycomb-style auto-switch based on time of day; already partially shipped.

---

## 13. "Unknown unknowns" worth asking the research agent about

Things the maintainer didn't necessarily raise but the agent should surface:

- What are emerging patterns in "review-first" AI products from 2025–2026 that didn't exist when Taskdeck's thesis was first written?
- Are there open-source projects pursuing the exact same thesis ("local-first execution workspace with review-first automation")? What can be learned / reused?
- How are Raycast, Superhuman, Linear, Notion actually using small models vs frontier models in production in 2026?
- What is the state of on-device inference on typical developer hardware (M-series Mac, consumer GPU) as of the research date? What model sizes are comfortably realtime?
- Are there regulatory / privacy developments (EU AI Act, US state privacy laws) that should shape a local-first AI workspace's posture?
- What retention studies exist for personal productivity tools? What does "30-day stick" actually require?
- What distribution channels reach the "solo developer / CS student / indie builder" persona best in 2026?

---

## 14. Anti-ideas (things to research against)

Things Taskdeck should probably **not** adopt even if they're trendy:

- **Full autopilot agents that mutate board state.** Violates GP-06.
- **Cloud-only collaborative model.** Violates local-first thesis.
- **Heavy enterprise RBAC/SSO focus before single-user is polished.** Premature scale.
- **Embedding every piece of user data to a cloud vector DB.** Privacy.
- **Chat-only UI.** Board-as-execution-center is the thesis.
- **Multi-agent orchestration frameworks** as a product surface. Complexity out-of-scope.
- **Blockchain-backed audit trails.** No.
- **Plugin marketplace** before core product legibility is proven.

---

## Pointers for the maintainer's own reading

A few curated starting points, roughly tiered by value/density:

- [The Bitter Lesson — Rich Sutton](http://www.incompleteideas.net/IncIdeas/BitterLesson.html) — context for why LLM-first beats rule-first in the long run.
- [Anthropic "Building effective agents"](https://www.anthropic.com/research/building-effective-agents) — pragmatic agent patterns.
- [Simon Willison's Datasette blog](https://simonwillison.net/) — local-first + SQLite + LLM patterns.
- [How Linear builds product](https://linear.app/now) — command-first UI.
- [Raycast Engineering posts](https://www.raycast.com/blog) — extension and keyboard-first patterns.
- [Superhuman keyboard shortcut design](https://blog.superhuman.com/productivity/) — friction reduction.
- [Phil Eaton notes](https://notes.eatonphil.com/) — local-first / SQLite engineering depth.
- [Simon Willison: LLM structured output](https://simonwillison.net/tags/structuredoutput/) — current best practices.
- [HuggingFace embedding model leaderboard (MTEB)](https://huggingface.co/spaces/mteb/leaderboard).
- [BFCL — Berkeley Function Calling Leaderboard](https://gorilla.cs.berkeley.edu/leaderboard.html).
- [sqlite-vec](https://github.com/asg017/sqlite-vec).
- [whisper.cpp](https://github.com/ggerganov/whisper.cpp).
