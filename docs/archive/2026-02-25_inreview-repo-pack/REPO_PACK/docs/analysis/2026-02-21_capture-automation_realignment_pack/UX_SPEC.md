# UX Spec — Inbox, Capture, and “Maintenance Automation”
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## UX objectives
- Capture must feel effortless.
- Users must not fear “forgetting”: Inbox is a safe buffer.
- Automation must feel trustworthy: proposals + diffs + provenance.
- Keyboard-first flows must be first-class.

## New navigation surface
Add a workspace route:
- `/workspace/inbox` (Capture Inbox)

Add command palette items:
- “Capture to Inbox”
- “Go to Inbox”
- “Triage selected Inbox item”
- “Batch triage…”

## Capture interaction (typed)
### Entry points
- Hotkey (suggest): `c` (or `Ctrl+Shift+C` if single-letter conflicts exist)
- Command palette: “Capture”

### Capture modal
Fields:
- multi-line text input (auto-focus)
- optional title hint
- optional “apply to board” context (future)
Actions:
- `Enter` submits if single-line mode
- `Ctrl+Enter` submits in multi-line mode
- `Escape` closes modal (escape-stack contract)

After submit:
- toast: “Captured”
- optional: keep modal open for rapid capture (toggle)

## Inbox list view
Each item shows:
- created time
- source type icon
- title hint or first-line excerpt
- status chip (`New`, `Triaging`, `Triaged`, `Converted`, `Failed`, `Ignored`)
- actions:
  - “Triage”
  - “View”
  - “Ignore”
  - “Open proposal” (when exists)

Keyboard:
- `j/k` navigate items
- `Enter` opens detail panel
- `t` triage
- `i` ignore
- `Esc` closes panel

## Inbox item detail view
Shows:
- full raw text
- triage history (latest run status, provider, prompt version)
- evidence snippets highlighted (optional)
Actions:
- “Triage now”
- “Create proposal”
- “Open proposal”
- “Re-triage” (explicit; creates new run)
- “Ignore”

## Proposal review UX (reuse existing)
Add to proposal UI:
- source banner:
  - “Generated from Inbox item <id>”
  - show excerpt/evidence quotes
- summary section:
  - number of cards created
  - labels created
  - columns created
- diff viewer:
  - show per-op details

## Provenance display on cards (must-have)
For cards created by triage:
- show a small “source” pill in card modal:
  - “Source: Inbox item”
  - link to the artifact
- show evidence quote(s) in modal (collapsed by default)

## Accessibility and premium feel rules
- Hit targets >= ~24px and forgiving (especially for frequent actions)
- Keyboard navigation never gets stuck
- Focus is always visible and never obscured by overlays
- Avoid accidental destructive actions; confirmations for high-risk ops
- Loading states are explicit (no silent waiting)

## Progressive enhancement roadmap
After MVP:
- batch triage
- inline editing of triage suggestions before proposal generation
- voice capture (opt-in, explicit privacy disclosure)
- transcript upload
