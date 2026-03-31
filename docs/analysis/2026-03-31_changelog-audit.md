# Changelog Audit: 2026-03-28 to 2026-03-31

Last Updated: 2026-03-31

## Scope

This document covers all changes merged to `main` between 2026-03-28 and 2026-03-31, spanning **30+ merged PRs** across features, fixes, testing, ops, docs, and dependency updates. The audit groups changes by domain, assesses risk, and provides commentary on patterns and considerations.

---

## 1. Chat & NLP Pipeline (High Impact)

### PRs: #602, #591, #589

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| `NaturalLanguageInstructionExtractor` (#602) | `Taskdeck.Application/Services`, MockLlmProvider, OpenAiLlmProvider, GeminiLlmProvider | Closes the intent-classification-to-parsing gap. Natural language like "create onboarding tasks" now produces proposals instead of parse errors. This is the single biggest UX improvement for chat usability. |
| Multi-instruction batch parsing (#591) | `IAutomationPlannerService`, `ChatService`, planner | Users can now send a single message with multiple instructions and get multiple proposals. Unlocks practical bulk task creation from chat. |
| Board-context LLM prompting (#589) | `BoardContextBuilder`, `LlmSystemPromptBuilder`, OpenAI/Gemini providers | LLM now sees the current board state (columns, cards, labels) so it can make contextually relevant proposals instead of guessing column names. |

**Risk assessment**: Medium. Three PRs touch the LLM provider layer simultaneously. The NLP extractor uses regex-based heuristics (not LLM) for the Mock fallback path, so there's inherent fragility with edge-case natural language. Board context is bounded (column/card/label names only) which is the right call for now.

**Remaining gap**: Conversational refinement (#576) is the last undelivered piece of the chat NLP wave. This means users still can't say "actually put it in the second column instead" to modify a proposal in-flight.

---

## 2. Inbox & Capture Workflow (High Impact)

### PRs: #607, #592

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Batch triage + suggestion editing (#607) | `CaptureController`, `CaptureService`, `InboxView`, `captureStore` | Multi-select triage replaces one-at-a-time workflow. Suggestion editing lets users fix capture text before triage. This is the capture velocity improvement the inbox needed. |
| Transcript capture source (#592) | `CaptureSource` enum, `CaptureModal`, backend validation | Users can now paste or upload transcript files as capture input. Expands capture beyond typed text. |

**Risk assessment**: Low-Medium. Batch triage has good validation (size limit 50, duplicate rejection, per-item auth, state-transition guards). The 207 partial-success response path is well-designed. Suggestion editing has state-transition guards preventing edits after triage starts.

**Consideration**: The batch triage endpoint returns 200/207/422 which is a more nuanced contract than most endpoints. Ensure consumers (including future mobile clients) handle 207 correctly.

---

## 3. Search & Navigation (Medium Impact)

### PRs: #603

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Global search + Ctrl+K launcher (#603) | `SearchController`, `SearchService`, `ShellCommandPalette`, `searchApi`, `useGlobalSearch` | Cross-board search from anywhere in the app. Boards and cards are now findable without navigating to each board. The command palette evolves from a navigation shortcut to a productivity hub. |

**Risk assessment**: Low. Search respects existing authorization boundaries. 200ms debounce and abort-on-supersede prevent request spam. Description field made nullable to handle cards without descriptions.

**Consideration**: Search currently queries all user-accessible boards. As board count grows, this could become a performance concern. No pagination/limit was added to the API (the `maxResults` parameter was actually removed in a follow-up commit). Worth monitoring.

---

## 4. Board Interaction (Medium Impact)

### PRs: #590

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Keyboard card movement + move-to menu (#590) | `useBoardKeyboardNav`, `CardItem`, `ColumnLane`, `BoardCanvas`, `BoardView` | Alt+Arrow moves cards between columns without drag. Click-based move-to menu provides an alternative to drag-and-drop. Keyboard-first accessibility improvement. |

**Risk assessment**: Low. Well-scoped change. Escape handling and focus restoration are covered. Column lane test prop fix caught an existing bug.

---

## 5. Accessibility (Medium Impact, Cross-Cutting)

### PRs: #604

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| WCAG 2.1 AA audit + remediation (#604) | Skip-link, sr-only utility, ESLint a11y rules, HomeView, TodayView, ReviewView, InboxView, CaptureModal, ToastContainer, BoardView, axe-core E2E tests | First systematic accessibility pass. Establishes automated WCAG regression via axe-core in CI. |

**Risk assessment**: Low. The `color-contrast` rule is intentionally disabled in axe-core tests because CSS custom properties can't be statically resolved. This is a known axe limitation, not a real accessibility gap, but should be validated manually.

**Consideration**: The ESLint a11y rules are tuned for "gradual rollout" — some rules are warnings, not errors. Over time these should be tightened. The login test required a fresh browser context to avoid `addInitScript` re-injecting auth tokens — a subtle Playwright pattern worth documenting for future E2E authors.

---

## 6. Developer Experience & Documentation (Medium Impact)

### PRs: #605, #606

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Developer portal + OpenAPI (#605) | 7 controllers (annotations), Swagger config, `docs/api/` (7 new docs), CI workflow, export script | External developers can now understand the API from generated OpenAPI specs and human-written guides. JWT auth flow, error contracts, and webhook integration are documented. |
| SBOM + release provenance (#606) | CI workflows (3 modified), reusable SBOM workflow, docs, dependency policy | Supply chain security baseline. CycloneDX SBOMs and SLSA provenance manifests are now generated on release and security scan runs. |

**Risk assessment**: Low for docs. Medium for SBOM workflow — multiple CI fix commits were needed (shellcheck glob fix, GitHub expressions in env, frontend CLI flags, provenance JSON generation). The SBOM workflow is now stable but was iterated on 4+ times post-merge.

---

## 7. Testing Infrastructure (Medium Impact)

### PRs: #601

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Property-based + fuzz testing pilot (#601) | FsCheck packages, Domain.Tests, Application.Tests (new PropertyBased/ and Fuzz/ directories) | First property-based testing in the project. Board/Card/Column/Label entity invariants and AutomationProposal state machine are now validated against random inputs. Fuzz tests cover regex safety in LlmIntentClassifier and serialization roundtrip contracts. |

**Risk assessment**: Low. FsCheck tests are additive. One post-merge fix was needed (parameterless Property tests converted to Fact, missing `using Xunit`).

**Consideration**: Property-based tests can be slow with large iteration counts. Ensure CI timeout budgets account for this, especially on Windows runners.

---

## 8. Domain Extensions (Low Impact)

### PRs: #588

| Change | What It Touches | Why It Matters |
|--------|----------------|----------------|
| Contact card YAML parser (#588) | `ContactCardYamlParser`, `ContactCardFrontMatter`, YamlDotNet dependency | Foundation for card-first outreach CRM. Parse/serialize YAML front matter for contact metadata. |

**Risk assessment**: Low. Self-contained module. YamlDotNet is the standard .NET YAML library. Static caching of serializer/deserializer is a good performance choice.

**Consideration**: This adds a new NuGet dependency (`YamlDotNet`). The `.csproj` merge conflict resolution also upgraded identity tokens to 8.17.0 in the same commit — coupling unrelated changes.

---

## 9. Dependency Updates (Maintenance)

### PRs: #593–#600

| Package | Old | New | Notes |
|---------|-----|-----|-------|
| `@eslint/js` | 9.39.4 | 10.0.1 | Major version bump. Required ESLint v10 rule violation fixes in demo scripts and playwright config. |
| `@types/node` | 24.10.1 | 25.5.0 | Major version bump for type definitions. |
| GitHub Actions | various | grouped | 5 action updates in one group. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | 18.3.0 | Major version bump for test infrastructure. |
| `Swashbuckle.AspNetCore` | 6.9.0 | 10.1.7 | **Major version jump** (4 majors). Required OpenApi v2.x compatibility fix. |
| `Microsoft.IdentityModel.Tokens` / `System.IdentityModel.Tokens.Jwt` | — | 8.17.0 | Identity token upgrade. |
| `xunit.runner.visualstudio` | 2.8.2 | 3.1.5 | Major version bump for test runner. |

**Risk assessment**: Medium-High collectively. Four major version bumps in one wave is aggressive. The Swashbuckle 6→10 jump is particularly notable — the intermediate API changes could affect consumers of the OpenAPI spec if the generated output format changed subtly.

**Consideration**: Swashbuckle 10.x uses a significantly different internal architecture. The compatibility fix in `Program.cs` removed 12 lines and simplified the config, suggesting the old Swashbuckle API surface was incompatible. Validate that the generated OpenAPI spec hasn't regressed in structure.

---

## 10. Fix Commits (Post-Merge Stabilization)

Several commits landed directly on `main` after PR merges to fix issues discovered in CI:

| Commit | Fix |
|--------|-----|
| `81233a53` | Fix Swashbuckle version and OpenApi v2.x compatibility |
| `3c868522` | Fix shellcheck SC2035: prefix glob with `./` in checksum step |
| `0f7e8cd7` | Fix frontend SBOM CLI flags to use `--package-dir` |
| `fc4d457d` | Fix provenance manifest generation to produce clean JSON |
| `ccd4958c` | Move GitHub expressions from `run:` to `env:` in SBOM workflow |
| `1c99ea1d` | Convert parameterless Property tests to Fact |
| `4a02be1c` | Add missing `using Xunit` for `[Fact]` attribute |
| `af95cf92` | Revert titleHint init — CaptureItem type lacks this field |
| `d7ab1bd1` | Preserve ClientCreatedAt when updating capture suggestion |
| `197c7c85` | Fix text length validation and edit-button status scope |
| `00877259` | Fix board create extraction bug and remove unused regex field |
| `45b9c152` | Initialize editedTitleHint from existing item value |
| `2e0e03ed` | Make SearchCardHitDto.Description nullable |
| `d3b6a25f` | Update test assertion for AbortSignal parameter |
| `ab63773b` | Remove unused hasSearchResults computed property |
| `b841d73d` | Fix webhook signature verification docs to match implementation |

**Commentary**: 16 fix commits across 13 PRs suggests a pattern: PRs are merging before CI fully validates all paths. The SBOM workflow alone needed 4 fix commits. The batch triage feature needed 3 fixes (titleHint, ClientCreatedAt, text validation). This is not unusual for a high-velocity merge wave, but is worth noting as a process observation.

---

## Summary Statistics

| Metric | Value |
|--------|-------|
| PRs merged (feature/fix) | 14 |
| PRs merged (dependency) | 7 |
| PR merged (accessibility) | 1 |
| Total commits on main since last docs update | 70+ |
| Post-merge fix commits | 16 |
| New backend test files | 8+ (property-based, fuzz, NLP extractor, batch triage, board context) |
| New frontend test files | 5+ (search, command palette, batch triage, accessibility) |
| New API endpoints | 3 (`/api/search`, `/api/capture/items/batch-triage`, `/api/capture/items/{id}/suggestion`) |
| New docs | 10+ (`docs/api/` portal, SBOM provenance) |
| New CI workflows | 2 (SBOM/provenance, developer portal) |
| New NuGet dependencies | 2 (FsCheck, YamlDotNet) |

---

## Cross-Cutting Observations

### Velocity vs. Stability Trade-off
30+ PRs in ~48 hours is extremely high velocity. The 16 post-merge fixes suggest some PRs would benefit from an additional CI validation pass before merge. Tracked in `#608` (OPS-26): require `ci-extended` for workflow and infrastructure PRs.

### Chat Pipeline Maturity
The chat-to-proposal pipeline has taken a major leap: NLP extraction, multi-instruction, and board context were the three biggest gaps. Only conversational refinement (#576) remains. The chat surface is approaching production readiness for the core use case.

### Supply Chain Security Posture
SBOM generation and release provenance close the last gap identified in the testing/hardening strategy analysis. Combined with the dependency update wave, the supply chain security posture is now stronger than most projects at this stage.

### Accessibility as a Regression Gate
The axe-core E2E tests are a significant addition. They create an automated regression gate for WCAG violations, which will prevent accessibility regressions as new views are added. The gradual ESLint a11y rule rollout is pragmatic but should be tightened over time.

### API Surface Growth
Three new endpoints in one wave. The `/api/search` endpoint is the first cross-board query surface, which is architecturally significant — it's the first endpoint that aggregates data across the user's entire workspace rather than operating within a single board.

### Dependency Risk
Four major-version NuGet bumps in one wave (Swashbuckle 6→10, Test SDK 17→18, xunit runner 2→3, identity tokens) is aggressive. Each individually is fine, but collectively they change a lot of the testing and API infrastructure simultaneously. The Swashbuckle migration is functionally clean (no deprecated APIs) but the exported OpenAPI artifact is stale (last generated 2025-02-24). Tracked in `#609` (DOC-04): regenerate and validate OpenAPI spec artifact.

### Search Scalability
Global search has hard-coded limits (10 boards, 20 cards) so queries are bounded, but there's no cursor/offset pagination for paging through results. The `maxResults` API parameter is accepted but silently ignored. Fine for current scale, but tracked in `#610` (UX-16) for future growth.
