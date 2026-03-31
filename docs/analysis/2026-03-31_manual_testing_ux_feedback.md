# Manual Testing UX Feedback — 2026-03-31

Session owner: Chris (manual testing, dark theme, demo board with seeded data)
Date: 2026-03-31
Screenshots: `.claude/*.JPG`

---

## Summary

Eight areas surfaced from hands-on testing. They range from quick CSS fixes to strategic architectural decisions about LLM integration. Each section below includes the raw observation, root-cause analysis from code, proposed solution(s), priority, and acceptance criteria.

---

## 1. Review Section — Applied Items Accumulate with No Way to Clear

### Observation
The Review tab shows all proposals including those already "Applied to board". Over time this list becomes overcrowded and unusable. There is no dismiss, archive, or clear button.

### Screenshot
`.claude/reviewReadability.JPG` — shows 11 Applied + 12 Capture-linked items all visible simultaneously.

### Root Cause
- `ReviewView.vue` loads all proposals via `automationApi.getProposals({ limit: 200 })` with no status filter.
- The API (`AutomationProposalsController`) returns all statuses including `Applied`.
- No delete/archive/dismiss endpoint exists in `automationApi.ts`.
- Applied items persist indefinitely in the database.

### Proposed Solutions

**A. Quick win — Hide Applied by default (frontend-only)**
Add a toggle "Show completed" (default: off) that filters out `Applied` and `Rejected` proposals from `visibleProposals`. This requires zero backend changes.

**B. Medium term — Batch dismiss/archive action**
Add a "Clear applied" button that marks Applied proposals as dismissed. Backend: add `POST /automation/proposals/dismiss` accepting an array of IDs. Frontend: button in the summary header next to the "Applied" count.

**C. Long term — Auto-archive policy**
Add a `ProposalHousekeepingWorker` rule (one already exists) that auto-archives Applied proposals older than N days (configurable, default 7). Already-applied proposals are historical; they should age out.

### Priority
**P2** — Usability degrades over time; worsens with every triage cycle.

### Acceptance Criteria
- [ ] Applied/Rejected proposals hidden by default with a visible toggle
- [ ] User can clear all Applied items in one action
- [ ] Review list shows only actionable items (Pending Review, Approved/Ready to Execute) on first load
- [ ] Proposal count badges update when filter changes

---

## 2. Starter Packs — Light Theme Disconnect

### Observation
The Starter Pack catalog modal uses a bright white/light-gray theme (`bg-white`, `text-gray-*`, `border-gray-*` Tailwind classes) while the rest of the app is dark-themed. This creates a jarring visual disconnect.

### Screenshot
`.claude/readabilityAndScrollingBar.JPG` — context shows the board header area with "Starter Packs" button.

### Root Cause
`StarterPackCatalogModal.vue` uses **hardcoded Tailwind color classes** (`bg-white`, `text-gray-900`, `border-gray-200`, etc.) instead of the design token system (`var(--td-surface-*)`, `var(--td-text-*)`). The modal was likely built quickly without integrating into the token pipeline.

Lines 602-856 of `StarterPackCatalogModal.vue` are entirely light-themed Tailwind:
- `bg-white` (backgrounds)
- `text-gray-600/700/900` (text)
- `border-gray-200/300` (borders)
- `bg-blue-50`, `bg-green-100`, `bg-red-100`, `bg-amber-100` (status colors)

### Proposed Solution
Rewrite the modal's Tailwind classes to use the existing design token CSS custom properties. The dark-theme palette already covers all needed tiers:

| Current Tailwind | Replacement Token |
|---|---|
| `bg-white` | `var(--td-surface-container)` |
| `text-gray-900` | `var(--td-text-primary)` |
| `text-gray-600` | `var(--td-text-secondary)` |
| `border-gray-200` | `var(--td-border-default)` |
| `bg-blue-50` (selected) | `var(--td-color-ember-dim)` with ember border |
| `bg-green-100` (success) | `var(--td-color-success-light)` |
| `bg-red-100` (error) | `var(--td-color-error-light)` |
| `bg-amber-100` (warning) | `var(--td-color-warning-light)` |

This is a contained refactor — one file, no behavior changes, purely visual.

### Priority
**P2** — Visual coherence issue; looks like a different app.

### Acceptance Criteria
- [ ] Starter Pack modal uses design tokens, not hardcoded Tailwind colors
- [ ] Modal renders correctly in dark theme (current default)
- [ ] Modal renders correctly in light theme (`[data-theme="light"]`)
- [ ] Status indicators (success/warning/error) remain visually distinct
- [ ] No functional regression in starter pack apply/dry-run flow

---

## 3. Review Section — Selection/Triage UX Regression

### Observation
Clicking on a captured item in Review no longer allows single-click triage (approve/reject/execute). The only path forward is through checkbox multi-selection. This is unintuitive — users expect to click an item and see its action buttons.

### Clarification from Code Analysis
ReviewView actually does **not** use checkbox selection — it shows all proposals in a flat list with action buttons (View Diff, Approve, Reject, Apply) directly on each card. The confusion may stem from:

1. **InboxView** uses checkbox selection for batch triage, and the two surfaces feel similar
2. The action buttons on review cards are at the bottom of each card, which can be long (title + metadata + cues + affected entities + planned changes). When there are many items, the buttons may not be visible without scrolling within the card.
3. The "Apply to Board" button only appears after "Approve" — a two-step flow that isn't obvious

### Proposed Solutions

**A. Sticky action footer on review cards**
Pin the action buttons (View Diff / Approve / Reject / Apply) to the bottom of each card viewport with `position: sticky; bottom: 0`. This way actions are always visible regardless of card content length.

**B. Collapsible card detail**
Make the detail sections (Affected, Planned Changes, Provenance) collapsible by default. Show only: title, status badge, risk level, and action buttons. Click to expand details. This reduces visual noise dramatically and makes actions immediately discoverable.

**C. Quick-action hover bar**
On card hover, show a floating action bar (similar to how GitHub shows quick actions on PR lists). This puts approve/reject within one click without needing to find the card's bottom.

**D. Single-click card expansion (inbox-style)**
Clicking a card opens a focused detail panel (like InboxView's detail pane) with all metadata + actions. The list becomes a compact summary. This unifies the interaction model with Inbox.

### Priority
**P2** — Core review flow should be friction-free; this is the approval gate.

### Acceptance Criteria
- [ ] Action buttons visible without scrolling on standard-height proposals
- [ ] The approve -> execute two-step flow is clearly communicated (visual state change, button label update)
- [ ] Detail information is available but doesn't push actions out of view
- [ ] Works on both desktop and mobile viewports

---

## 4 & 5. Quick Capture Triage Fails for Natural-Language Input

### Observation
Two captures failed immediately after triage:
1. "I need to move all the cards in the onboarding board to the next stages and then create a new task for re-hydrating documentation" — user-typed, natural language
2. "ACME Ltd - year-end checklist - Chase outstanding VAT receipts from Q3 - Confirm payroll submissions are current - Schedule pre-year-end review call with director" — copy-pasted from auto-generated text

Both show "FAILED" tag in the inbox immediately after triage attempt.

### Root Cause
`CaptureTriageService.cs` uses **regex-based text extraction** (lines 226-268) to find task candidates:
- Checklist: `^\s*[-*]\s+\[[xX ]\]\s+(.+?)\s*$`
- Numbered: `^\s*\d+[.)]\s+(.+?)\s*$`
- Bullet: `^\s*[-*\u2022]\s+(.+?)\s*$`
- Fallback: entire text as one task

For input #1: The text is a single sentence with no bullets/numbers. The fallback treats the entire sentence as one task title. If the title exceeds 180 chars or evidence exceeds 280 chars, validation fails.

For input #2: The text uses ` - ` (space-dash-space) as separators, not line-leading `- ` bullets. The regex requires `^\s*[-*]`, which needs the dash at line start. Inline dashes don't match. Falls back to one task with the entire text, which is within limits but the hyphen-separated format confuses title extraction.

The deeper issue: **triage is purely mechanical regex, not semantic**. It can't understand natural-language instructions like "move cards" or parse ad-hoc formatting like "ACME Ltd - checklist - item1 - item2".

### Proposed Solutions

**A. Short term — Improve regex fallback (low effort)**
When the full text falls through as a single task candidate:
1. Split on ` - ` (space-dash-space) as an additional delimiter pattern
2. Split on `;` and `\n` as additional delimiters
3. Treat items after the first as individual tasks, first item as context/title hint
4. Add a "freeform text" task type that preserves the original text as description rather than requiring structured title/evidence

**B. Medium term — LLM-assisted triage (moderate effort)**
When live providers are enabled, send the raw capture text to the LLM with a triage prompt:
```
Given this raw text, extract individual actionable tasks. For each task provide:
- title (max 180 chars)
- evidence/context (max 280 chars)
- suggested board column
```
Fall back to regex when the LLM is unavailable (mock provider). This uses the existing `ILlmProvider` infrastructure.

**C. Long term — Semantic capture pipeline**
Build a two-stage pipeline:
1. **Classification**: Is this text a command (move cards), a task list, a note, or a question?
2. **Extraction**: Route to the appropriate handler:
   - Commands → chat/instruction pipeline (already exists)
   - Task lists → triage extraction (current flow)
   - Notes → store as-is, optionally link to board
   - Questions → route to chat

This would eliminate the "wrong pipeline" problem where commands enter the triage flow.

### Priority
**P1** — Capture is the entry point. If capture fails on natural text, the core value proposition ("near-zero-friction capture") breaks.

### Acceptance Criteria
- [ ] Dash-separated text (like "ACME Ltd - item1 - item2") produces individual task cards
- [ ] Free-form single-sentence captures don't fail — they create a single task card with the text as description
- [ ] Failed captures show a meaningful error message (not just "FAILED" tag)
- [ ] Retry after failure preserves the original text
- [ ] When LLM providers are active, natural-language captures produce better-quality task breakdowns

---

## 6. LLM Chat — Lacks Board Context Awareness and Tool Calling

### Observation
Chat conversation demonstrated:
1. LLM says it can't access card IDs or determine "next stages" — lacks board awareness
2. Response got truncated mid-JSON: `{ "reply": "I understand...` then cut off
3. LLM can only suggest "create card" but can't list, move, or operate on existing cards
4. User had to manually specify which columns map to which "next stages"

### Root Cause

**Board context is minimal:**
- `BoardContextBuilder.cs` includes column names and card **titles only** — no card IDs, no card positions, no card metadata
- Context budget is only 2000 chars, which gets truncated for boards with many cards
- The board context is appended to the system prompt, not as structured tool input

**No function calling / tool use:**
- The system uses hardcoded regex-based intent classification, not LLM function calling
- Supported patterns are limited to 8 instruction types (create/move/archive/update card, rename board, reorder column)
- The LLM is asked to output JSON with `instructions[]` but has no ability to query the board state
- When the LLM can't extract a card ID from the user's message, it gives up

**Token limit causes truncation:**
- `MaxTokens = 1024` (in `ILlmProvider.cs`) — too low for complex responses
- With JSON mode enabled, partial JSON is a known issue when the response is truncated at token limit
- The frontend renders whatever it receives, including partial/invalid JSON

### Proposed Solutions

**A. Quick win — Increase token limit and fix truncation (low effort)**
1. Increase `MaxTokens` to 2048 or 4096
2. Add a response completeness check: if JSON mode is enabled and the response doesn't parse as valid JSON, mark the message as `degraded` with reason "Response was truncated" and show a user-friendly message instead of raw JSON
3. Frontend: detect `{` at start of message content and don't render raw JSON to users

**B. Medium term — Richer board context (moderate effort)**
Expand `BoardContextBuilder` to include:
- Card IDs (truncated to 8 chars for readability, full ID in structured data)
- Card positions within columns
- Card labels/tags
- Increase budget to 4000 chars
- Structure as a clear reference table:
```
Board: Client Onboarding
Columns: New Intake → Waiting on Client → Ready for Review → In Progress → Completed
Cards in "New Intake": [abc12345] Send engagement letter, [def67890] Schedule onboarding call, ...
```

**C. Long term — Tool-calling / MCP integration (high effort, strategic)**
Replace the regex instruction extraction with proper LLM function calling:
1. Define tools: `list_cards(column?)`, `get_card(id)`, `move_card(id, target_column)`, `create_card(title, column?)`, `search_cards(query)`
2. The LLM can query board state, reason about it, and issue structured commands
3. Commands still go through the proposal flow (review-first safety preserved)
4. This naturally solves "move all cards to next stage" — the LLM can enumerate cards, batch moves, and create a multi-operation proposal

This aligns with point 7 below (MCP considerations).

### Priority
**P1** (truncation fix) / **P2** (richer context) / **P3** (tool-calling architecture)

### Acceptance Criteria
- [ ] Chat responses never show raw JSON to the user
- [ ] Truncated responses are detected and shown as degraded with retry suggestion
- [ ] LLM can reference specific cards by name when board context is available
- [ ] Complex multi-card operations produce proposals (not "I can't do that" responses)

---

## 7. Strategic: MCP / Tool-Calling / Active Assistant Architecture

### Observation
The LLM chat acts as a passive text-in/text-out interface. For Taskdeck's thesis ("reduce maintenance overhead"), the assistant needs to be an active participant that can:
- Query board state (cards, columns, labels)
- Understand the user's workflow context
- Suggest multi-step operations
- Learn patterns from past interactions

### Analysis

The current architecture has a **clean separation point** that makes tool-calling feasible without compromising review-first safety:

```
User Message → LLM (with tools) → Structured Instructions → Proposal → Review → Execute
                                    ↑                         ↑
                              tool calls go here        safety gate stays here
```

The LLM can freely query board state (read-only tools) without any review gate. Write operations (move, create, archive) always produce proposals. This preserves GP-06 (Review-First Automation Safety).

### Architecture Options

**Option A: Native function calling (OpenAI/Gemini tools)**
- Define tool schemas in the provider-specific format
- Tools: `list_boards`, `get_board_columns`, `list_cards_in_column`, `search_cards`, `get_card_details`
- Write tools: `propose_card_create`, `propose_card_move`, `propose_card_archive`, `propose_bulk_move`
- Provider-specific implementation; Mock provider simulates tool calls deterministically
- Fits existing `ILlmProvider` interface with extension

**Option B: MCP server (Model Context Protocol)**
- Build a Taskdeck MCP server that exposes board/card/column resources
- LLM connects via MCP to query state and propose changes
- More standardized and provider-agnostic
- Could be exposed externally (OpenClaw compatibility, third-party agent integration)
- Higher implementation cost but more strategic value

**Option C: Hybrid — Internal tools + MCP for external agents**
- Use native function calling for the built-in chat (fast, provider-optimized)
- Expose an MCP server for external agents/tools that want to interact with Taskdeck
- Best of both worlds: fast internal, interoperable external

### Recommendation
Start with **Option A** (native function calling) for the built-in chat, then build toward **Option C** as the product matures. MCP server becomes valuable when third-party integrations are a priority (v0.4.0+ per the platform expansion strategy).

### Priority
**P3** — Strategic. Requires spike/design document before implementation.

### Acceptance Criteria for Spike
- [ ] Document: supported tool inventory (read tools, write tools, safety boundaries)
- [ ] Document: provider-specific tool schema format (OpenAI vs Gemini vs Mock)
- [ ] Document: interaction flow diagrams (user → LLM → tool call → response → proposal)
- [ ] Document: MCP server scope and timeline
- [ ] Prototype: one read tool (`list_cards_in_column`) working end-to-end in chat
- [ ] Validate: review-first gate is preserved for all write operations

---

## 8. UI/UX Issues (Multiple)

### 8a. Card Drag Handle Makes Card Appear to Shorten

**Screenshot:** `.claude/cardShortens.JPG`

**Observation:** When hovering over the drag handle, a "DRAG CARD" label expands, making the card appear to change size. The handle area itself is quite prominent.

**Root Cause:** `CardItem.vue` lines 393-401 — `.td-board-card__drag-label--hidden` has `width: 0; overflow: hidden` by default, expanding to `width: auto` on hover. The action bar uses negative margins (`margin: -0.5rem -0.5rem var(--td-space-2) -0.5rem`) which creates a layout shift.

**Proposed Solution:**
1. Remove the label expansion animation — the drag cursor (`cursor: grab`) is sufficient affordance
2. Or: reserve the label space with `visibility: hidden` instead of `width: 0` so layout doesn't shift
3. Reduce the action bar negative margin to prevent visual jump
4. Consider using a grip icon (six-dot pattern `⠿`) instead of a text label — more compact, universally understood

**Priority:** P3

---

### 8b. Today Section — Overcrowded with Similar Cards

**Screenshot:** `.claude/easierToUnderstandToday.JPG`

**Observation:** Today shows 5 stat cards + 5 agenda cards simultaneously. All cards look similar with minimal visual differentiation. The section feels dense and hard to scan.

**Root Cause:** `TodayView.vue` renders all sections at once with auto-fit grids and tight spacing:
- Stats: `grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))` with 0.6rem gap
- Agenda: `grid-template-columns: repeat(auto-fit, minmax(280px, 1fr))` with 0.8rem gap
- Items within cards: only 0.25rem internal gap

**Proposed Solutions:**
1. **Prioritize by urgency**: Show only cards with non-zero counts prominently. Zero-count stats can be a compact summary row instead of full cards
2. **Visual hierarchy**: Use the ember color for the most urgent card, muted backgrounds for zero-count cards
3. **Collapse empty sections**: If "Blocked cards" is 0, show it as a small chip not a full card
4. **Progressive disclosure**: Show top 2-3 most important sections expanded, rest collapsed with "Show more" affordance
5. **Increase spacing**: Bump gaps from 0.6-0.8rem to 1.0-1.2rem for breathing room

**Priority:** P2

---

### 8c. Board Horizontal Scrollbar Below Viewport

**Screenshot:** `.claude/readabilityAndScrollingBar.JPG`

**Observation:** The horizontal scrollbar for the board canvas appears below the visible viewport, requiring users to scroll down to reach it. Even with only 5 columns, horizontal scrolling is necessary.

**Root Cause:** `BoardCanvas.vue` line 88: `height: calc(100vh - 120px)` with `overflow-x: auto`. The 120px offset doesn't account for all header elements (toolbar, action rail, help callout, filter panel). When vertical content also overflows, the native scrollbar sits below the container's bottom edge.

Additionally, columns are fixed at `width: 20rem` (320px) × 5 = 1600px + gaps + padding, which exceeds typical viewport widths.

**Proposed Solutions:**
1. **Fix container height**: Change to `height: calc(100vh - var(--td-board-header-height, 180px))` and measure actual header height dynamically. Ensure the scrollbar is always visible within the viewport.
2. **Responsive column widths**: Use `min-width: 16rem; flex: 1; max-width: 22rem` so columns shrink to fit when possible, only scrolling when truly necessary.
3. **Sticky scrollbar**: Use CSS `overflow-x: auto; overflow-y: hidden` on the canvas and move card overflow to within columns (which already have `overflow-y: auto`). This ensures the horizontal scrollbar is always at the bottom of the visible area.
4. **Scroll arrows / drag scroll**: Add left/right scroll buttons at the board edges (like Trello) for discoverability. Also support drag-to-scroll on the canvas background.

**Priority:** P2

---

### 8d. Sidebar Shortcuts/Logout Buttons Scroll Away

**Screenshot:** Visible in multiple screenshots — sidebar extends below viewport on long pages.

**Observation:** The Shortcuts and Logout buttons at the bottom of the sidebar scroll out of view when the page content is long.

**Root Cause Analysis Update:** The sidebar itself uses `flex-direction: column` with `flex: 1` on nav and the footer at the bottom. The sidebar **should not** scroll — it's a fixed-height flex container. However, if the sidebar's total content exceeds viewport height (many nav items), the footer gets pushed below.

**Proposed Solution:**
1. Add `overflow-y: auto` to `.td-sidebar__nav` (not the whole sidebar) so only the nav section scrolls
2. Ensure `.td-sidebar__footer` stays visible with `flex-shrink: 0`
3. Test with many workspace tools sections expanded

```css
.td-sidebar {
  height: 100vh;
  display: flex;
  flex-direction: column;
}
.td-sidebar__nav {
  flex: 1;
  overflow-y: auto;
}
.td-sidebar__footer {
  flex-shrink: 0;
  /* already has border-top */
}
```

**Priority:** P3

---

### 8e. Inbox — Font Fatigue and Monochrome Tags

**Screenshot:** `.claude/readabilityFontAndColouredTags.JPG`

**Observation:** Long white text in the inbox list causes eye fatigue. All tags (Failed, Typed, Applied to Board, Ignored) use the same neutral gray color, requiring users to read the text to distinguish status.

**Root Cause:** `InboxView.vue` lines 1064-1075 — `.td-status-chip` and `.td-meta-chip` share identical styling:
```css
background: var(--td-surface-container-highest);
color: var(--td-text-secondary);
```
No status-specific color differentiation exists.

**Proposed Solutions:**
1. **Color-coded status chips:**
   - Failed: red background (`var(--td-color-error-light)`) with error text
   - Applied to Board: green/success
   - Triaging: amber/warning (in-progress)
   - Ignored: muted gray (current style, appropriate for dismissed items)
   - New: ember/primary (needs attention)
2. **Text truncation with ellipsis**: Long excerpts should truncate at 2 lines with `line-clamp: 2`
3. **Reduce font weight**: Use `font-weight: 400` for excerpt text (currently inherits) to reduce visual intensity
4. **Alternating row backgrounds**: Subtle alternation between `--td-surface-container` and `--td-surface-container-low` to help scanning

**Priority:** P2

---

### 8f. Notifications — Long Undifferentiated List

**Screenshot:** `.claude/readabilityInNotifications.JPG`

**Observation:** Opening Notifications shows a long list of similar-looking items. All say "Automation proposal updated" with similar sub-text. Hard to scan at a glance.

**Root Cause:** `NotificationInboxView.vue` renders all notifications in identical cards. The title comes from `item.title` which is often generic ("Automation proposal updated"). Notification metadata (type, cadence) is shown as small gray text that doesn't help with quick scanning.

**Proposed Solutions:**
1. **Type-specific icons/colors**: Use distinct left-border colors or icons for different notification types:
   - Proposal updates → amber
   - Mentions → blue
   - Board changes → green
   - System/health → gray
2. **Smart grouping**: Group consecutive notifications of the same type: "3 automation proposals updated" instead of 3 separate cards
3. **Batch "Mark all read"**: Add a top-level button to mark all visible notifications as read
4. **Summary preview**: Show the board name and affected card count in the title, not just "Automation proposal updated". E.g., "3 cards proposed for Client Onboarding"
5. **Time-based grouping**: "Today", "Yesterday", "This week" headers to break up the list
6. **Max visible count with pagination**: Show last 20 with "Load more" instead of dumping everything

**Priority:** P2

---

### 8g. Review Section — Tag Overload and Provenance Noise

**Screenshot:** `.claude/reviewReadability.JPG`

**Observation:** Review cards show many pill-shaped tags: "7 changes touching 7 target surfaces", "Medium risk", "Check the affected items before approving", "Created from Inbox capture triage", plus Capture-linked, Open Capture, Review Link, Open Board, Triage Run ID. And then Affected cards as separate pills. This is visually overwhelming.

**Root Cause:** `ReviewView.vue` lines 605-719 render all metadata inline:
- Cue pills for impact/risk/source (lines 620-630)
- Provenance section with multiple buttons and IDs (lines 634-658)
- Affected entity chips for every card (lines 660-671)
- Planned changes listed as text (lines 675-695)

All use similar styling (`.td-review-cue` with gray backgrounds), creating a wall of similar-looking pills.

**Proposed Solutions:**
1. **Collapsible detail sections**: Show title, status, risk, and actions. Everything else collapsed by default:
   - "Affected (7 cards)" → click to expand list
   - "Provenance" → click to expand links
   - "Planned changes" → click to expand operation list
2. **Visual hierarchy for risk**: Use color-coded risk badges:
   - Low: green
   - Medium: amber (current, but more prominent)
   - High/Critical: red with subtle background
3. **Hide provenance by default**: Correlation IDs and triage run IDs are debug information. Show them under a "Technical details" toggle or in a tooltip.
4. **Consolidate action links**: Merge "Open Capture", "Review Link", "Open Board" into a single dropdown menu ("Links ▾") to reduce button clutter.
5. **Card-count badge on section header**: Replace the wall of affected-card chips with "7 affected cards" badge, expandable to see the list.

**Priority:** P2

---

### 8h. Home — "Next Step" Red Card is Alarming

**Screenshot:** `.claude/tooMuchRedInHome.JPG`

**Observation:** The "Next Step" section uses `--td-color-ember-glow` (#ff5352) as a full card background, making "Triage new captures" look like an error/emergency. The card reads as alarming rather than guiding.

**Root Cause:** `HomeView.vue` lines 560-568 — `.td-home-action--primary` uses:
```css
background: var(--td-color-ember-glow);  /* #ff5352 */
color: var(--td-text-inverse);           /* dark text on red */
```
The hover state adds a red glow: `box-shadow: 0 0 20px rgba(255, 83, 82, 0.25)`.

**Proposed Solutions:**
1. **Softer primary action tone**: Use a subtle ember accent instead of full-red:
   ```css
   .td-home-action--primary {
     background: var(--td-color-ember-dim); /* rgba(255, 77, 77, 0.1) */
     color: var(--td-text-primary);
     border-left: 3px solid var(--td-color-ember);
   }
   ```
   This provides a warm nudge without alarm.

2. **Gradient approach**: Use a dark-to-ember gradient that's dramatic but not alarming:
   ```css
   background: linear-gradient(135deg, var(--td-surface-container) 0%, var(--td-color-ember-dim) 100%);
   ```

3. **Reserve full-red for actual urgency**: Only use `--td-color-ember-glow` background for overdue/blocked items. Use the softer variant for "next recommended action".

**Priority:** P2

---

## Cross-Cutting Themes

### Theme 1: Information Density vs. Scannability
Surfaces (Review, Today, Notifications, Inbox) show too much data at the same visual level. The fix pattern is consistent: **progressive disclosure** — show summary by default, detail on demand.

### Theme 2: Color Differentiation
Tags, badges, and status indicators across Inbox, Review, and Notifications all use the same neutral gray. Each surface needs a semantic color vocabulary:
- Error/Failed: red
- Success/Applied: green
- Warning/In-progress: amber
- Info/Neutral: blue/gray
- Attention/Action-needed: ember

### Theme 3: Capture Pipeline Intelligence
The regex-only triage pipeline breaks on natural language. This is a fundamental limitation that requires LLM integration to solve properly. Short-term regex improvements + long-term LLM-assisted extraction.

### Theme 4: LLM as Active Assistant
The chat is currently a thin wrapper around text completion. The path to a genuinely useful assistant requires tool-calling / function-calling capability. This is a strategic investment that should be spiked before committing to implementation.

---

## Issue Seeding Plan

| ID | Title | Labels | Priority |
|---|---|---|---|
| UX-17 | Review: hide applied proposals by default, add clear/dismiss action | `ux`, `frontend` | P2 |
| UX-18 | Starter Pack modal: migrate from light Tailwind to design tokens | `ux`, `frontend` | P2 |
| UX-19 | Review: improve proposal card action visibility and detail density | `ux`, `frontend` | P2 |
| CAP-01 | Capture triage: handle natural-language and dash-separated text | `backend`, `llm`, `feature` | P1 |
| CAP-02 | Capture triage: show meaningful error messages for failures | `frontend`, `ux` | P2 |
| LLM-01 | Chat: fix response truncation and raw JSON display | `frontend`, `backend`, `llm` | P1 |
| LLM-02 | Chat: expand board context with card IDs and structured reference | `backend`, `llm` | P2 |
| LLM-03 | Spike: LLM tool-calling / function-calling architecture for chat | `llm`, `feature`, `strategy` | P3 |
| LLM-04 | Spike: MCP server for external agent integration | `llm`, `feature`, `strategy` | P3 |
| UX-20 | Board: fix horizontal scrollbar visibility and column responsiveness | `ux`, `frontend` | P2 |
| UX-21 | Card drag handle: eliminate layout shift on hover | `ux`, `frontend` | P3 |
| UX-22 | Today: reduce card density and add visual hierarchy | `ux`, `frontend` | P2 |
| UX-23 | Sidebar: keep footer (shortcuts/logout) always visible | `ux`, `frontend` | P3 |
| UX-24 | Inbox: add color-coded status tags and reduce text fatigue | `ux`, `frontend` | P2 |
| UX-25 | Notifications: add type differentiation, grouping, and batch actions | `ux`, `frontend` | P2 |
| UX-26 | Review: collapsible detail sections and provenance toggle | `ux`, `frontend` | P2 |
| UX-27 | Home: soften "Next Step" primary action color | `ux`, `frontend` | P2 |

---

## Sequencing Recommendation

### Wave 1 — Quick Wins (can ship this week)
1. **LLM-01**: Fix truncation detection + raw JSON guard (frontend + backend token limit)
2. **UX-27**: Soften home primary action color (CSS-only change)
3. **UX-21**: Fix card drag handle layout shift (CSS-only change)
4. **UX-23**: Sidebar footer sticky fix (CSS-only change)
5. **CAP-02**: Show error message for failed captures (frontend display change)

### Wave 2 — Targeted Improvements (1-2 weeks)
6. **UX-17**: Review hide applied + dismiss action
7. **UX-24**: Inbox color-coded tags
8. **UX-18**: Starter Pack dark theme migration
9. **UX-20**: Board scrollbar fix
10. **UX-22**: Today visual hierarchy

### Wave 3 — Deeper Work (2-4 weeks)
11. **CAP-01**: Capture triage intelligence (regex improvements + LLM fallback)
12. **LLM-02**: Richer board context for chat
13. **UX-19**: Review card action visibility
14. **UX-25**: Notification improvements
15. **UX-26**: Review collapsible sections

### Wave 4 — Strategic Spikes (4-8 weeks)
16. **LLM-03**: Tool-calling / function-calling spike
17. **LLM-04**: MCP server spike

---

## Related Existing Issues
- `#249` UI-07: Inbox premium primitives pass — overlaps with UX-24
- `#242` UI-00: Frontend premium UI wave tracker — parent tracker for UX improvements
- `#576` Conversational refinement loop — overlaps with LLM-01/LLM-02
- `#329` MVP-03: Secondary MVP follow-through — broader context for these findings
