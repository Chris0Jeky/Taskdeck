# ADR-0038: Paper UI Is the Canonical Frontend

- **Status**: Accepted
- **Date**: 2026-06-13
- **Deciders**: Chris0Jeky (maintainer directive, 2026-06-13)

## Context

Taskdeck has carried two parallel UI systems since the Paper & Graphite overhaul (#996):

- **Legacy** (Obsidian & Ember tokens, ADR-0011): the default. All ~31 workspace routes render it; onboarding, help callouts, and the E2E suite are built against its DOM.
- **Paper** (`design_handoff_taskdeck_paper/` spec): ~11.7k lines across 38 paper views and 20 paper components, gated behind a per-browser `localStorage` flag (`td.paper.mode`, default `off`). Five core routes (Home, Today, Board, Inbox, Review) plus the whole shell (sidebar, top bar, command palette, shortcuts overlay, toasts) have Paper variants. The only in-app way to turn Paper on is the unlinked `/styleguide/paper` route.

Issue #1136 (from the 2026-05-31 repo audit) named this the structural core of the "messy / every fix done twice" feeling and demanded a decision: commit to one canonical UI with a cutover plan, or quarantine the experiment. Concrete duplication costs already observed: `PaperReviewView` reimplements apply/reject actionability locally while Legacy uses shared composables (the #1124 Approved+expired bug class must be fixed twice), ~1.7k lines of dead/styleguide-only Paper code, and an E2E suite coupled to Legacy selectors.

On 2026-06-13 the maintainer set the project's final direction: Taskdeck will not be distributed; it is being finished as a personal-use tool and then archived. The explicit instruction is to **finish the UX/UI revamp and activate it ("the paper feel")**.

## Decision

**Paper is the canonical Taskdeck UI.** Specifically:

1. **Default mode flips to `paper`** (`paperThemeStore` fallback changes from `off` to `paper`). The storage key is bumped (`td.paper.mode` → `td.paper.mode.v2`) with a **one-time migration that preserves real choices**: when `td.paper.mode.v2` is absent, read the legacy `td.paper.mode` — a stored `paper`, `paper-night`, or `auto` carries over verbatim (a deliberate opt-in is honored), while a stored `off` (the old default, which almost always means "never chose anything") or an unset key resolves to the new `paper` default. The legacy key is then cleared. After the flip, any explicit user choice is honored.
2. **`paper` (light), not `auto`, is the default.** Paper-at-Night was only ever audited and E2E-verified on the styleguide route (PAPER_NIGHT_AUDIT.md's surface-scope follow-up never happened). Night remains one click away via the existing sidebar toggle and becomes default-eligible only after a surface re-audit.
3. **Legacy is frozen, not deleted.** It remains reachable through a real settings toggle (to be added — today the only switch lives on the unlinked styleguide route) as an escape hatch. No new UX work or fixes land in Legacy; **all new frontend UX work targets Paper**. Legacy removal is explicitly *not planned* — the project is being archived, and deleting ~30k lines of working UI buys nothing at this point.
4. **Coverage boundary**: the ~26 routes without Paper variants (Metrics, Integrations, Calendar, Automation Queue/Chat, Ops, Settings, Archive, Notifications, Agents, etc.) keep rendering their Legacy views inside the Paper shell. They get a contrast/legibility verification pass on the paper substrate, not rewrites. Building 25+ new Paper views is out of scope for a personal-use archive.
5. **Activation prerequisites** (must land before or with the default flip): the #1161 dismiss affordance in Paper Review (without it, completed/expired proposals are unremovable in the canonical review surface), shared actionability logic extracted so Paper and Legacy stop drifting, the three remaining hardcoded light-theme files tokenized, and the Paper review stubs de-stubbed (fabricated author metadata removed, confidence wired to the shipped endpoint).
6. **Onboarding is not ported.** First-run guidance (WorkspaceSetupModal, help callouts) exists only in Legacy views and stays there. Accepted loss: the sole user built the product. Documented here so nobody mistakes it for an oversight.
7. **Dead Paper code is wired or deleted** during the polish wave (per #1136 AC3): Ink Bleed gets wired into real LLM flows (its design purpose); `useShortcutContext`, `useVoiceCapture`, and unused Paper primitives are deleted unless a polish slice consumes them.
8. **E2E migration**: Legacy-coupled specs pin `td.paper.mode.v2 = 'off'` at the flip so CI stays green, then the highest-value journeys (inbox, review, board) are ported to Paper DOM incrementally. The dark-mode specs are repointed at Paper-Night, which is the product's only dark mode.

## Alternatives Considered

- **Keep the status quo (default `off`)**: rejected — it leaves the finished revamp invisible, keeps every UX fix doubled, and directly contradicts the maintainer's activation directive.
- **Default `auto` (OS-driven light/night)**: rejected for now — paper-night was never re-audited beyond the styleguide; defaulting prefers-dark users onto unverified surfaces risks a worse first impression than a deliberate light default. Revisit after the night surface audit.
- **Delete Legacy at the flip**: rejected — high-blast-radius deletion (~30k lines incl. tests) during an archive push, with zero payoff while the E2E suite still runs on Legacy selectors. Freezing is reversible; deleting is not.
- **Build Paper variants for all remaining routes**: rejected — weeks of work to re-skin admin/diagnostic surfaces a single user rarely visits, against the archive timeline.
- **Build-time flag excluding Paper from the default bundle** (the #1136 "experiment" branch): rejected — the experiment succeeded; gating it off contradicts the activation directive.

## Consequences

**Positive**: one canonical surface ends the doubled-fix tax; the drift class behind #1124 gets eliminated by shared composables; dead code is removed; the archive snapshot shows the product as designed (paper feel) rather than the scaffold it was migrating from.

**Negative**: Legacy bugs found after the freeze are wontfix unless they also affect shared composables; ~26 routes render Obsidian-styled content inside the Paper shell (mixed aesthetic, mitigated by the contrast pass); E2E porting is real work and until done the ported-spec coverage temporarily thins.

**Neutral**: the `--td-*` Obsidian token system stays in the codebase (Paper tokens live alongside under `.paper`/`.paper-night` scopes), consistent with the #996 non-goal of not removing it.

## References

- #1136 (Paper vs Legacy decision — this ADR delivers AC1; AC2 lands with the STATUS.md statement in the same PR; AC3 lands in the polish wave)
- #996 (PAPER-00 master tracker), #1161 (Paper review dismiss affordance)
- `design_handoff_taskdeck_paper/README.md` (canonical Paper spec)
- `frontend/taskdeck-web/src/components/paper/PAPER_NIGHT_AUDIT.md` (night-theme audit scope and pending surface follow-up)
- ADR-0011 (Obsidian & Ember tokens — remains in force for Legacy/frozen surfaces)
- Maintainer directive 2026-06-13 (finish + activate the paper feel; personal use; archive)
