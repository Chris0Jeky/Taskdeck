# Platform Local-First scavenge — 2026-09-23

**Scout:** Geek Scouts Platform Local-First (docs/ADR/acceptance only)  
**Box pack:** `/workspace/handoffs/platform-localfirst-scavenge-2026-09-23/`  
**Fences:** No second Action Stack · no product rewrites · no UX QA · no design-system · never auto-merge

## Spoken TLDR

Chris already has the hard parts: Taskdeck review-first proposals (GP-06); Action Stack Notion authority + SQLite order/receipts/outbox/Later with no multi-device replication; Alibi device-local IndexedDB + player-owned backup and "two alternatives" on conflict. Public research (Ink & Switch, Electric write patterns, Replicache rebase, Linear Triage, Things Someday, 2026 LWW+conflict-copy practice) **reaffirms** those fences. Steal ethics language and acceptance drills — not Automerge/Electric/Replicache runtimes.

## Top steals

1. Sync-ethics baseline: no auto-admit · no silent cloud overwrite · no browser cloud tokens · no default multi-device CRDT.
2. Taskdeck actionable chat must terminate in Review or explicit refusal (#2004 / draft ADR-PLF-02).
3. Keep Action Stack Later + rank-insert as scarce-attention reference; do not fork Priority Stack.
4. Alibi future sync = revision-conditional + forked boards (already in ARCHITECTURE).
5. Weekly dogfood: offline kill-switch · disposable write · forced conflict · redacted receipt.

## Draft ADRs (candidates)

See box `ADRS_DRAFT.md`:

- **ADR-PLF-01** Sync ethics baseline (all three)
- **ADR-PLF-02** Taskdeck chat → Review termination
- **ADR-PLF-03** Reaffirm action-stack ARCHITECTURE split
- **ADR-PLF-04** Alibi device-local + future sync shape
- **ADR-PLF-05** Shared Active / Later / Triage vocabulary (docs only)

## Acceptance

See box `ACCEPTANCE.md` for checkboxes. Action Stack live Notion evidence remains **#10** — research does not close it.

## Sources (primary)

- https://www.inkandswitch.com/essay/local-first/
- https://electric.ax/docs/sync/guides/writes
- https://doc.replicache.dev/concepts/how-it-works
- https://steveackley.org/blog/lww-rows-not-crdts
- https://linear.app/docs/triage
- https://culturedcode.com/things/support/articles/4001304/
- In-repo: action-stack `ARCHITECTURE.md` · Alibi `docs/ARCHITECTURE.md` · Taskdeck `GOLDEN_PRINCIPLES.md` / `docs/research/RESEARCH_BRIEF.md`

## Non-goals

No code changes in this PR. No competing queue product. No CRDT rollout. No merges from scout automation.
