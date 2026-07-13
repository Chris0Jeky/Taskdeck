# Generalist Copy Deck

Last Updated: 2026-07-13

## Purpose

This deck records the GEN-10 plain-language audit for the default guided workspace. It follows the revival direction documented as ADR-0044 in [docs PR #1296](https://github.com/Chris0Jeky/Taskdeck/pull/1296) and the proposed ADR-0046 generalist expansion in its [stacked docs PR #1328](https://github.com/Chris0Jeky/Taskdeck/pull/1328). Those direction records are intentionally maintainer-owned and have not landed on `main`. The implementation applies only low-risk wording changes; vocabulary that would change the product model remains a maintainer decision.

The review-first promise stays explicit: Taskdeck can suggest a change, but the user decides what reaches a board.

## Applied now

| Surface | Current term | Proposed term | Rationale | Disposition |
| --- | --- | --- | --- | --- |
| Paper sidebar status | `Precision Mode - active` | `Review before changes - active` | States the trust behavior without an unexplained product-mode name. | Applied |
| Legacy sidebar status | `Precision Mode Active` | `Review before changes` | Uses the same plain-language trust cue in both shipped shells. | Applied |
| Guided Paper group | `Workbench tools` | `More tools` | Avoids developer vocabulary on the default guided path. | Applied in guided mode only; workbench and agent remain unchanged |
| Guided navigation disclosure | No single entry point | `Advanced` / `Show` / `Hide` | Gives technical users one discoverable escape hatch without deleting routes. | Applied |
| Guided mode switch | Mode selector only | `Use advanced workspace` | Makes the workbench opt-in visible where advanced destinations are disclosed. | Applied; the existing selector remains available |
| Workspace preferences fallback | `Failed to load workspace preferences` | `We couldn't load your workspace preferences` | Describes the user-facing object and removes system-style phrasing. | Applied |
| Workspace mode fallback | `Failed to save workspace mode` | `We couldn't save this workspace mode` | Explains the attempted action in familiar language. | Applied |
| Home fallback | `Failed to load workspace summary` | `We couldn't load your workspace overview` | `Overview` matches what the page provides better than the data-oriented `summary`. | Applied |
| Today fallback | `Failed to load today agenda` | `We couldn't load today's overview` | Avoids implying a calendar agenda when the page also contains review and blocked work. | Applied |
| Setup fallback | `Failed to update onboarding state` | `We couldn't update the setup guide` | Replaces internal state-model language with the visible feature name. | Applied |

API-provided error messages remain unchanged. The new fallback copy appears only when a failed request has no usable message.

## Retained deliberately

| Term | Reason |
| --- | --- |
| `Today`, `Inbox`, `Review`, `Boards` | These are the established capture-review-apply loop and are already understandable on first contact. |
| `Settings`, `Preferences`, `Appearance` | These escape hatches must stay visible and use familiar platform language. |
| `Guided`, `Workbench`, `Agent` | They are persisted API values and established workspace choices; changing them is wider than a copy-only slice. |
| `Advanced` destinations in the command palette | Keyboard and command reachability is intentional even when guided sidebar navigation is collapsed. |

## Maintainer sign-off required

These proposals are discussion material only. GEN-10 does not apply them because each rename crosses UI, API, documentation, support, or mental-model boundaries.

| Current term | Candidate | Why it is contested | Recommendation |
| --- | --- | --- | --- |
| `Proposal` | `Suggestion` | `Proposal` is a domain object with approval, execution, provenance, and API vocabulary; `suggestion` may understate that contract. | Keep `Proposal` until a full vocabulary decision covers UI and API together. |
| `Capture` | `Note` or `Item` | Captures can hold more than notes and are the provenance root of the review loop. | Keep `Capture`; explain it contextually on first use. |
| `Triage` | `Organize` | Triage includes automation and proposal creation, not only sorting. | Test helper copy before renaming the action. |
| `Automation` | `Suggestions` | Automation also includes providers, queues, and execution semantics. | Keep as an advanced/system term; use task-specific copy on guided surfaces. |
| `Workbench` | `Advanced` | `Workbench` is an API value and a daily-driver identity for the maintainer. | Keep the mode name; use `Use advanced workspace` as the guided affordance. |
| `Agent` | `Assistant` or `Tools` | The mode represents a capability boundary, while `assistant` implies a conversational product promise. | Defer to ADR-0046 ratification and later agent-surface work. |
| `Board` | `Project` | A board is a concrete shipped object; projects may require a broader hierarchy. | Do not rename without a domain decision. |
| `Ops` | `Diagnostics` | Some routes are diagnostic, while CLI and endpoint tooling are broader operational controls. | Keep `Ops` inside Advanced; use plain descriptions within individual screens. |

## Navigation audit boundary

In guided mode, the default Paper navigation keeps the core loop, everyday tools, and Settings/Preferences/Appearance visible. Agents, Metrics/Cohorts, Integrations, Ops/Endpoints/Logs, API Keys, and Dev Tools sit behind one `Advanced` disclosure. Workbench and agent navigation are unchanged. Deep links, feature guards, keyboard shortcuts, and command-palette destinations remain functional; this is information architecture, not authorization.

New users already default to `guided` in both the frontend local fallback and backend preference model. GEN-10 preserves stored modes for existing users and does not add a migration. REVIVAL-05 (#1301) remains the owner of the first-board onboarding path.
