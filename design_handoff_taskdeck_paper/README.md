# Handoff · Taskdeck — Paper & Graphite, Ember Edition

## Overview

Taskdeck is a local-first task manager built around a strict **Capture → Review → Apply** loop. No input — neither the user's own captures nor an LLM's proposals — silently mutates the board. Every change flows through an explicit, undoable review step that the user actively presses Enter (or Backspace) to resolve.

The "Paper & Graphite, Ember Edition" is the visual + interaction system that expresses this thesis. The product looks like a logbook kept in a quiet office: cream paper, ink-deep blacks, hairline rules, italic serif headlines, and a single seal-red accent ("ember") that earns its place only on **proposals**, **applied stamps**, and **decision moments**.

This bundle covers the full surface area:
- Foundation (frontispiece, design tokens, typography)
- 9 desktop surfaces (Home, Board, Review, Inbox, Card detail, Command palette, Shortcuts, Toasts, Empty states) — each with one or two card/composition variants
- A signature LLM thinking-state ("ink bleed") with a full motion spec
- A deep Review surface — the central screen of the product — with provenance, conflicts, side-effects, history, confidence breakdown
- A Today / End-of-day dossier with stats, ledger, cadence, streak
- A dark companion ("Paper at Night") for the strongest surfaces
- Narrow companions at 768 (tablet) and 375 (phone)

## About the Design Files

Everything in this bundle is a **design reference**. The HTML prototypes are React + JSX rendered through Babel-standalone, with all styling in a single CSS token sheet. They are **not production code** and should not be shipped as-is.

The task is to **recreate these designs in the target codebase's existing environment** (React + Tailwind, Next.js, SwiftUI, etc.) using that codebase's component primitives, routing, and state patterns. If no environment exists yet, choose the framework that best fits the team — the design has no opinion about implementation.

## Fidelity

**High-fidelity.** Final colors, typography scale, spacing, motion specs, and copy are decided. The prototypes use real measurements (hex values, px, ms). Recreate pixel-perfectly within the target framework's idioms.

The one exception: **icons are letterform/hairline placeholders**. The system intentionally uses serif glyphs ("✎", "◐", "❡", "⌘") for sidebar nav and a small SVG hairline set for actions. Replace with the codebase's existing icon library if one exists, but maintain the **hairline (1.25–1.5 stroke), no-fill** treatment.

## The System in One Page

| Pillar | Decision |
|---|---|
| **Substrate** | Cream paper (`#f3eee5`), ink-deep blacks, no rounded corners > 4px. Paper does not round. |
| **Type** | Fraunces (italic display), Inter (UI), JetBrains Mono (serial numbers / metadata). |
| **Accent** | Single seal-red "ember" (`#a8421f`). Use only on: proposed stamps, decision CTAs, italicised proposal headlines, undo windows. Never decorative. |
| **Motion** | Pages turn. Ink dries. Stamps press. The signature is **ink bleed** for LLM thinking. |
| **Trust** | Encoded in materials, not labels. The applied stamp un-embosses on undo. The dotted "reversibility" line literally erases as the 6-hour window closes. |
| **Density** | High. Index-card boards survive 200 cards. Sidebar is 220px, collapsible. |
| **Theme** | Light is canonical. Dark ("Paper at Night") inverts the substrate while keeping all metaphors. |

## Files in this Bundle

```
design_handoff_taskdeck_paper/
├── README.md                          (this file)
├── Taskdeck Paper Edition.html        (entry point — open in a browser)
├── design-canvas.jsx                  (presentation shell — pan/zoom artboards)
└── paper/
    ├── tokens.css                     (THE source of truth — all design tokens)
    ├── icons.jsx                      (PaperIcons — hairline SVG set)
    ├── components.jsx                 (Sidebar, TopBar, Stamp, HLBtn, Card primitives)
    ├── surface-home.jsx               (Home / morning reset)
    ├── surface-board.jsx              (Kanban + 2 card variants)
    ├── surface-review.jsx             (Review — 2 variants: letterpress diff, side-by-side)
    ├── surface-review-deep.jsx        ★ Review — deep · the central surface
    ├── surface-inbox.jsx              (Inbox / capture — 2 variants)
    ├── surface-misc.jsx               (Card detail, Command palette, Shortcuts, Toasts, Empty states, Thinking comparators)
    ├── surface-narrow.jsx             (375 phone + 768 tablet)
    ├── surface-motion.jsx             ★ Motion spec · ink bleed (variant B, chosen)
    └── surface-today.jsx              ★ Today / end-of-day dossier
```

The starred files are the deepest, most specced ones. Read `tokens.css` first, then `components.jsx`, then any surface.

---

## Design Tokens

Source of truth: `paper/tokens.css`. Both light and dark themes are defined as `.paper { … }` and `.paper-night { … }` selectors over the same variable names — port them straight to your token system.

### Colors · Light (`.paper`)

| Token | Value | Use |
|---|---|---|
| `--paper` | `#f3eee5` | Page substrate (cream) |
| `--paper-card` | `#fbf7ee` | Cards, raised surfaces |
| `--paper-2` | `#ebe5d8` | Sunken / sidebar background |
| `--ink-deep` | `#0a0908` | Display headings |
| `--ink` | `#1a1814` | Body text, primary |
| `--ink-2` | `#3a352d` | Secondary text |
| `--mute` | `#6c6557` | Tertiary, captions |
| `--faint` | `#a39c8b` | Quaternary, disabled |
| `--line` | `#d8d0bf` | Hairline borders |
| `--line-soft` | `#e2dccd` | Inner dividers |
| `--whisper` | `#efe9dd` | Quietest tint |
| `--ember` | `#a8421f` | THE accent — proposals, decisions |
| `--ember-ink` | `#6e2810` | Ember on ember (text on tint) |
| `--ember-tint` | `#f7e6dd` | Ember halo / pill background |
| `--ember-bloom` | `#fbeee5` | Outermost bleed |
| `--applied` | `#4a6b3f` | Sealed / applied state (sage) |
| `--applied-tint` | `#e7ecdf` | Applied background |
| `--overdue` | `#8c4a26` | Warning / past-due (rust, NOT red) |
| `--overdue-tint` | `#f1dfd0` | Overdue tint |
| `--cream` | `#fbf3df` | Brand cream (text on ember) |

### Colors · Dark (`.paper-night`)

Same variable names, inverted for night. Notable shifts:

| Token | Light | Dark |
|---|---|---|
| `--paper` | `#f3eee5` | `#14110d` |
| `--paper-card` | `#fbf7ee` | `#1c1813` |
| `--ink-deep` | `#0a0908` | `#fbf3df` |
| `--ember` | `#a8421f` | `#d96a3e` (warmer, brighter) |
| `--applied` | `#4a6b3f` | `#8aae72` |

### Typography

```
--serif: "Fraunces", Georgia, "Times New Roman", serif;
--sans:  "Inter", system-ui, -apple-system, "Segoe UI", sans-serif;
--mono:  "JetBrains Mono", "SF Mono", Menlo, monospace;
```

Fraunces uses **italic + low optical size** for character (display, headlines, decisions). Inter is the workhorse. JetBrains Mono is reserved for **serial numbers, eyebrow labels, timestamps, ledger entries** — never for body copy.

Type scale (utility classes in `tokens.css`):

| Class | Family | Size · weight · style | Use |
|---|---|---|---|
| `.tk-display` | serif | 64px / 1.04 / italic 400 | Frontispiece only |
| `.tk-h1` | serif | 36px / 1.1 / italic optional | Surface titles |
| `.tk-h2` | serif | 26px / 1.15 / italic optional | Section headings |
| `.tk-h3` | serif | 18px / 1.25 / 500 | Card-section heads |
| `.tk-lede` | serif | 17px / 1.55 / italic 400 | Subtitle paragraphs |
| `.tk-body` | sans | 14px / 1.55 / 400 | Body |
| `.tk-meta` | mono | 11px / 1.4 / 400, letter-spacing .04em, uppercase | Timestamps, captions |
| `.tk-eyebrow` | mono | 10.5px / 1.4 / 500, letter-spacing .14em, uppercase | Section labels |
| `.tk-serial` | mono | 11px / 1 / 500, letter-spacing .12em, uppercase | Card serial numbers |
| `.tagstamp` | mono | 9px / 1 / 600, letter-spacing .18em, uppercase, 1px border | Pill labels (PROPOSED, APPLIED) |

### Spacing

No formal scale — use multiples of 4px. Common values: 4, 6, 8, 10, 12, 14, 18, 22, 28, 36, 48, 56.

### Border radius

Small — paper does not round. **Maximum 4px** anywhere. Most cards use 2px. Stamps are circular (50%) only.

### Shadows / elevation

Minimal. Cards use a 1px hairline border + 1px inset highlight to feel "letterpressed". One halo class:

```
.halo-ember { box-shadow: 0 0 0 4px var(--ember-bloom), 0 1px 0 var(--line); }
```

---

## Components

Read `paper/components.jsx` for implementations.

### Sidebar
- 220px wide, `var(--paper-2)` background, full height, 1px right border `var(--line)`.
- Vertical sections, each preceded by a `.tk-eyebrow` label.
- Items: 36px height, 12px horizontal padding, hairline icon (16px) + 13px Inter Medium label.
- Active item: 2px left border `--ember`, background `linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%)`.
- Footer: collapsed user pill + theme toggle (☼ / ☾).

### TopBar
- 56px height, paper background, 1px bottom border `--line`.
- Left: breadcrumbs in serif italic (`Workspace › Review › Proposal #014`).
- Right: search slot + ⌘K hint + actions cluster.

### Stamp (`<Stamp kind={...}>`)
- Round, 64px default, ember-color border (2px), centered text.
- Three kinds with copy:
  - `applied` → "Applied" / date / time / `#NNN`. Pressed inset shadow.
  - `proposed` → "Proposed" / date / time / `#NNN`. Lifted (no inset).
  - `captured` → "Captured" / date / time. Sage applied color.
- Always rotated **−7° to −9°** for hand-stamped feel.
- Optical illusion: applied stamps use `box-shadow: inset 0 1px 0 rgba(0,0,0,.15)` to feel embossed; on undo they should crossfade to the proposed look in 240ms.

### HLBtn (Hairline button)
- Default: 1px ember border, transparent fill, 11px serif italic text + 10px mono kbd hint.
- Padding: 8px 12px. Radius 2px.
- `ember` prop → fills with `--ember`, text becomes cream.
- Always render the keyboard hint (`⏎`, `⌫`, `E`, `D`) inline, separated by a 1px vertical divider.

### Card primitives
- `.card` — paper-card background, 1px `--line` border, 2px radius, no shadow.
- `.card-lift` — adds inset top highlight `inset 0 1px 0 rgba(255,255,255,.6)` and a 1px bottom shadow.
- `.rule-ledger` — striped horizontal rules every 24px (the ledger book metaphor).
- `.diff-add` / `.diff-rem` — applied/overdue tint backgrounds, mono font, 1px left border in same hue.
- `.hr-double` — two stacked 1px rules with 2px gap (frontispiece divider).
- `.hr-soft` — single `--line-soft` 1px rule.

### Stamps, ribbons, labels
- `.tagstamp` — mono micro-pill with 1px border in any color (used for status tags). Letter-spacing **.18em** is non-negotiable.

---

## Screens & Views

The HTML canvas is paginated by `<DCSection>`s. Open `Taskdeck Paper Edition.html` in a browser to navigate. Below is per-surface spec.

### 1. Home / Reset
File: `paper/surface-home.jsx`

Morning reset surface. Shows:
- Greeting in serif italic: "Good morning, Daniel."
- Today's queue count, focus block reminder, weather of work
- 3 "queued for you" cards (proposals + carry-overs)
- Quick capture single-line at the bottom

### 2. Board (kanban)
File: `paper/surface-board.jsx`

- Standard 4-column kanban (`Backlog → Today → In Progress → Done`)
- Columns: 280px wide, paper-2 background, 1px border, 16px gap
- Header: serial number (`§ 04`) + name + count badge
- **Card variant A — Index card** (default): serial number across the top in mono, title in serif 14.5px medium, 11px metadata strip, optional 1-line body. Used for high-density boards.
- **Card variant B — Tag ribbon**: same body but with a colored ribbon down the left edge keyed to label. Better for designers' boards.
- Column footer: "+ capture" hairline button.

### 3. Review (★ central surface)
Files: `paper/surface-review.jsx` (variants A and B), `paper/surface-review-deep.jsx` (the deep version)

This is the most important screen. Implement the **deep version** as the canonical Review.

Layout: **3 columns** — `280px queue rail | flex main | 320px right rail`.

#### Queue rail (left)
- "Queue · 3 awaiting · 2 stale" eyebrow + filter pills (`All / Mine / Stale`).
- Vertical list of proposals: serial, age, title (serif 13.5px medium), author + confidence + reach metadata.
- Active item: 2px ember left border + ember-bloom gradient background.
- "Recently applied · undoable" section with countdown timers.
- "This week" cadence sparkline (7 days, last day = ember).

#### Main (center)
- Header: PROPOSED tagstamp + serial + title (serif italic 36px) + lede + 200px confidence dial card (top-right).
- **Decision rail**, sticky: tagstamp DECISION + summary + 4 buttons (Reject ⌫, Request edit E, Defer D, Apply ⏎ ember).
- **§ I The change** — before/after grid. Left: original card (subdued). Right: 3 new cards (ember-bloom gradient bg). Each new card has 2px left border (ember for kept, applied for new).
- **Per-field changes** strip below: title / subtasks / labels / due / assignee with strikethrough old + bold new.
- **§ II Provenance** — 5-row table: what was read (primary, contextual, excluded, inferred). Each row: 32px icon + 200px italic key + flex value.
- **§ III Side effects** — 2-column: 7-row table (Cards, Subtasks, Comments, etc.) + Reversibility card with **dashed undo-window timeline** (the dashes literally erase as time passes — animate this).
- **§ IV Conflicts & warnings** — colored rows (warn rust, info mute, ok sage).
- **§ V History** — ledger table styled with `.rule-ledger`: serial, event, age, status pill.

#### Right rail
- Author card (haiku · local, confidence breakdown bars: pattern match / reach / reversibility / recency).
- "Why now" explanation card with link to heuristics.
- "Similar past decisions" — 3 prior proposals with applied/rejected verdicts and apply rate.
- Decide-with-keys card in ember tint: ⏎ Apply, E Edit, ⌫ Reject, D Defer, P Provenance, Space Preview.

#### Confidence dial
- 84px SVG circle, 2px stroke, ember stroke-dasharray driven by confidence.
- Center: serif italic value (`0.84`), mono caption "CONF".

### 4. Inbox / Capture
File: `paper/surface-inbox.jsx`

- **Variant A — single-line nib**: a focus-mode capture screen with one giant italic-serif input centered on the page. After typing, haiku structures the capture into title + tags via the **ink bleed motion**.
- **Variant B — composer ledger**: a multi-line composer with metadata sidebar (board picker, label picker, due date, attachments). For desktop power users.

Triage table below the composer: 11 captured items with auto-suggested cards, tagstamps, accept/reject hairline buttons.

### 5. Card detail (focus mode)
File: `paper/surface-misc.jsx` → `CardDetailSurface`

- Wide single-column layout, max-width 720px, centered on `--paper-2` page.
- Title in serif 28px, body in 15px Inter, subtasks as a checklist with `.rule-ledger` styling.
- Activity log on the right as a vertical ledger.
- Pending proposal banner at top if one exists for this card.

### 6. Command palette (⌘K)
File: `paper/surface-misc.jsx` → `CommandPaletteSurface`

- 640px wide, centered, `--paper-card` with 4px radius.
- Top: 13px Inter input, no border, 16px padding.
- Result rows: 40px height, hairline icon, label, mono kbd hint.
- AI-action rows have an ember dot + "haiku" mono label.

### 7. Shortcuts overlay
File: `paper/surface-misc.jsx` → `ShortcutsSurface`

3-column reference card: Navigate / Capture & Review / Boards. Each row: kbd pill + label + mono note.

### 8. Toasts (stacked)
File: `paper/surface-misc.jsx` → `ToastSurface`

Bottom-right stack. 320px wide, 56px height, paper-card with hairline border. Tagstamp on left, message in 13px, "undo" hairline link on right with countdown.

### 9. Empty states
File: `paper/surface-misc.jsx` → `EmptyStatesSurface`

Centered serif italic copy ("Nothing waiting. Good."), no illustrations, single hairline CTA. Uses ember-tint backgrounds sparingly.

### 10. Today / End-of-day dossier (★ premium)
File: `paper/surface-today.jsx`

A one-page dossier the user reads at the close of work. Sections:
- **Cover** — paper-2→paper gradient, 56px serif italic headline ("Today, you moved nine cards."), lede paragraph, "Seal day" CTA, dossier serial top-right.
- **5 stat cards** — `cards moved · proposals applied · captures triaged · longest focus · overdue`. Each: 38px serif italic number, mono eyebrow label, sub-line, 2px top accent in tone color.
- **§ I Cadence** — 24-hour activity strip (24 bars, peak hour highlighted in ember), first/peak/last action mini-stats below.
- **§ II Ledger** — full event log of the day, serial number per row (`L-NNN`), time, who pill (you/haiku/system), what, dot in tone color.
- **§ III Decisions** — 4-up grid of today's proposals with verdict tagstamps (APPLIED green, REJECTED rust, DEFERRED ember).
- **§ IV Boards touched** — list of boards with move counts and proposal pills. Untouched boards dimmed.
- **§ V Carry-over** — overdue cards in a rust-bordered card. "Pin both to tomorrow's morning" CTA.
- **§ VI Streak** — 90-day grid, 30 cols, ember intensity per day, today highlighted.
- **§ VII A line for tomorrow** — italic serif textarea, autosaved, shown on tomorrow's open.

### 11. Narrow companions
File: `paper/surface-narrow.jsx`

- **Phone (375)**: Single-column. Sidebar collapses to a bottom tab bar with 4 letterform glyphs. Stamps shrink to 48px. Type scales down 10%.
- **Tablet (768)**: Sidebar collapses to icon-only rail (60px). Boards reduce to 2 visible columns with horizontal scroll.

### 12. Dark companion (Paper at Night)
File: `paper/surface-misc.jsx`, all surfaces accept `theme="paper-night"` prop.

Same metaphor, inverted substrate. The ember warms slightly (`#d96a3e`). Stamps gain a subtle glow.

---

## Signature Motion · Ink Bleed (★ chosen)

File: `paper/surface-motion.jsx`

This is the system's voice. **Ink bleed replaces every "loading" state in the product** when an LLM is composing. Total duration **4.6s**, 5 phases:

| Phase | Time | What |
|---|---|---|
| **Drop** | 0 — 0.4s | A single seal-red droplet falls from above the headline. Page settles 1px on impact. |
| **Bloom** | 0.4 — 1.4s | First bleed grows to ~40% radius. Edge irregularity is hand-drawn (use 4 droplets at varied positions, not a radial). |
| **Compose** | 1.4 — 3.4s | Subsequent droplets land on rhythm with token streaming. Headline reveals **through a wet/dry mask** (`mask-image: linear-gradient(90deg, #000 X%, transparent X+12%)`). |
| **Settle** | 3.4 — 4.2s | Bleed desaturates from ember `#a8421f` → ink-deep `#1a1814` as it "dries". Each droplet has its own dry curve. |
| **Stamp** | 4.2 — 4.6s | Round seal embosses with 1px shadow (`scale(.96) translateY(1px)` then released). Audible if sound enabled. |

### Implementation specs

- **Easing**: drop `cubic-bezier(.45,0,.15,1)` 260ms · bloom-scale `cubic-bezier(.2,.65,.25,1)` 1000ms · bloom-opacity linear 1400ms · reveal-mask `cubic-bezier(.3,.8,.3,1)` 2000ms · stamp-press `cubic-bezier(.4,0,.15,1)` 320ms.
- **Droplets**: 4 at irregular positions (not symmetric). See `surface-motion.jsx → BleedStage` for the math.
- **Bloom radius**: min 80px, max 240px, responsive to container.
- **Mix-blend-mode**: `multiply` always.
- **Filter**: `blur(6px)` growing to `blur(10px)` during the dry phase.
- **Reduced motion** (`@media (prefers-reduced-motion)`): replace with a 200ms opacity fade. WCAG 2.3.3.
- **No-JS fallback**: render the static dried + stamped state at t = 4.6s.

### Where it appears

| Surface | Use | Duration |
|---|---|---|
| Review | Awaiting proposal · headline of the proposal card | full · 4.6s · auto-pauses if user scrolls |
| Inbox / capture | After ⌘; while haiku structures a capture | compose only · 1.4–3.4s loop |
| Command palette | AI-action row, before proposal preview | drop + bloom · 0–1.4s, single droplet |
| Card detail | Opening a card with attached pending proposal | drop only · 0–0.4s flash on the badge |
| Toasts | "Proposed" notification | bloom only · 0.6s |

### Don'ts
- Don't loop the bloom indefinitely. The bleed must dry. If the model is still composing past 4.6s, hold dried state and pulse the eyebrow only.
- Don't tint the bloom anything but ember. The metaphor is single-pigment ink.
- Don't run two bleeds simultaneously on the same view.

---

## Interactions & Behavior

### Capture → Review → Apply loop
This is the product's spine. Implementation requirements:

1. **Nothing mutates without review.** Every change (user-typed or LLM-proposed) must produce a Review entry. Direct edits in the board UI are still routed through Review, but with confidence = 1.0 and auto-applied if user enables "trust my own edits".
2. **Apply is atomic and reversible.** Default undo window: **6 hours**. Configurable per workspace.
3. **Undo restores everything.** Original card body, subtasks (with checkmark state), comments, activity log entries — all preserved on the archived parent during the undo window.
4. **Apply emits one ledger entry**, never N entries for N cards changed.

### Keyboard
The product is keyboard-first. Required global shortcuts:
- `⌘K` Command palette
- `⌘;` Quick capture
- `⌘⇧;` Composer ledger (Inbox B)
- `⌘L` Open year ledger
- `?` Shortcuts overlay
- `T` Today, `B` Board, `R` Review, `I` Inbox (sidebar nav)

In Review (modal-like, no need for `⌘`):
- `⏎` Apply · `⌫` Reject · `E` Request edit · `D` Defer 1h · `P` Toggle provenance · `Space` Preview diff in card detail

### Animations & transitions
- **Page navigation**: 220ms `cubic-bezier(.4,0,.2,1)` translateX + paper-1px parallax.
- **Card appear**: 180ms fade + 4px upward translate.
- **Stamp press**: 320ms (specced above).
- **Toast in/out**: 200ms slide + fade.
- **Undo dashes erasure**: linear over the undo window duration. Each dash crossfades to `--line` color one-by-one, right to left.

### Hover / focus states
- Cards: lift via 1px shadow `0 1px 0 var(--line)` + inset highlight on hover. No scale, no translate.
- Buttons: ember intensifies on hover (no fill change for ghost; fill darkens for ember).
- Focus rings: 2px outline `--ember` with 1px gap. No blue browser default.

### Loading states
**Replaced entirely by ink bleed.** Skeletons and spinners are forbidden. If the load is < 200ms, render nothing. If > 200ms but no LLM involved (e.g. fetching a board), show a **single dried-ink dot** that pulses at 0.6Hz, no bloom.

---

## State Management

Per-surface state requirements (framework-agnostic):

- **Review queue**: list of `{id, sn, title, author, confidence, reach, age, status: 'awaiting' | 'stale' | 'applied' | 'rejected' | 'deferred'}`. Sorted by status then age.
- **Active proposal**: full proposal object with `provenance[]`, `sideEffects{}`, `confidenceBreakdown{}`, `conflicts[]`, `history[]`, `similarPast[]`, `diff{before, after, fields[]}`.
- **Undo registry**: `{id, sn, appliedAt, expiresAt, snapshot}` — drives the right rail's "Recently applied · undoable" list and the dashed-erasure timeline.
- **Capture inbox**: `{id, raw, proposedTitle, proposedTags, proposedBoard, capturedAt}`. Triage state per item.
- **Today dossier**: derived from the day's ledger; computed at end-of-day or on-demand.
- **Streak**: 90-day rolling array of `{date, sealedAt | null}`.

### Data fetching
The product is **local-first**. All boards, cards, captures, ledger entries persist in IndexedDB or SQLite (via `wa-sqlite` / `cr-sqlite`). LLM calls are local (Ollama / web LLM) by default, with cloud fallback opt-in. Provenance tracking is essential: every LLM call records its full read-set for the proposal record.

---

## Assets

The bundle uses **no external assets** — no images, no icon sprites, no fonts beyond the Google Fonts (Fraunces, Inter, JetBrains Mono) that should be self-hosted in production.

**Icons** are inline hairline SVGs in `paper/icons.jsx` (`PaperIcons`):
- `Plus, Search, Stamp, Sparkle, ArrowRight, X, Check, Pages, Pen, Cursor, Tag, Dot, Eye, Bell` — all 16px, 1.25–1.5 stroke, no fill, `currentColor`.

**Sidebar nav glyphs** are letterforms in serif italic — keep this character. Replace only if the codebase has equivalent serif-letterform glyphs.

**Fonts**:
- Fraunces (italic 400, 500) — display / serif
- Inter (400, 500, 600) — UI / sans
- JetBrains Mono (400, 500) — meta / mono

Self-host via `@font-face` in production. The HTML prototype uses Google Fonts CDN via `tokens.css`.

---

## Open Questions for the Developer

These were intentionally left for implementation:

1. **Local LLM choice**: `haiku` is a placeholder name in copy. Decide between Ollama (Llama 3.2 / Phi-3 / Mistral), in-browser (web-llm with WebGPU), or cloud-default (Anthropic Haiku, OpenAI 4o-mini) with explicit user opt-in.
2. **Undo window default**: 6h is generous; consider 1h default with workspace setting.
3. **Confidence threshold for auto-apply**: spec says > 0.95 with user opt-in; tune in beta.
4. **Sound design**: stamp press, page turn, pen cap — implement only if sound-on toggle is on, default off.
5. **Multiplayer**: out of scope for v1. Local-first only. CRDT path documented separately.

---

## Final Notes

Trust signals are encoded in **materials**, not labels. Don't add helper text saying "this is undoable" — render the dashed timeline. Don't say "AI-generated" — show the ember stamp. Don't put "loading…" anywhere — bleed ink.

The first time a user undoes an apply and watches the stamp lift off, they should understand the thesis without reading any marketing copy. If they don't, the implementation has missed the brief.

— Designed Apr 25, 2026 · Paper & Graphite, Ember Edition · v1
