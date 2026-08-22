# HORIZON JOURNEY — Taskdeck golden end-to-end session

**Instance:** `http://127.0.0.1:5000/` · **Build reported by sidebar:** `v0.7.2` (wrong, see #1948)
**Run date:** 2026-08-22 · **Account:** `demo` (`c6a77122-0afc-428a-bfc4-27edf9b36847`)
**Skin:** Paper (light) · **Workspace mode:** Guided · **Locale:** English
**Evidence style:** network requests, DOM assertions, and API reads. Screenshots are not the proof mechanism.

> **Tested revision: NOT RECORDED — a gap in this record.** The only build identifier captured was the
> sidebar string, and that string is itself the subject of #1948, so it identifies nothing. The run can
> only be *bounded*, by inference from the observations below rather than by measurement: it is after
> `eab59fd27` (PR #1952 — Step 14b records #1944 as fixed) and before `7a0776ebd` (PR #1957 — Step 16
> still finds `Tune heuristics →` dead, which #1957 fixes). PRs #1954, #1956, and #1959 all merged after
> the run and all touch surfaces recorded here, which is why several verdicts below carry a
> "does not reproduce / gap in PR #xxxx" note. **Any replay must record `git rev-parse HEAD` of the
> tree it built, in this header, before step 1.**

> **Safety contract used for this run.** Additive-only against pre-existing data. Every entity created
> was prefixed `[HZN]`. Native `window.confirm/alert/prompt` were shimmed before any destructive or
> decision action. Baseline captured before step 1 and re-verified after cleanup (see §Cleanup).

---

## 1. Persona and scenario

**Nadia Ferrante**, senior developer at a two-person studio. She runs three concurrent workstreams and
uses Taskdeck as the place where *unstructured input becomes reviewed, trusted board state*.

| Workstream | Why it needs its own board |
| --- | --- |
| Payments API migration | Client work, hard Thursday cutover, auditable trail required |
| Internal dev-tools side quest | Low-ceremony idea capture, single lane |
| On-call follow-ups | Ops chores that arrive mid-flow and must not be lost |

**Her week, compressed into one session:**

1. **Monday 09:40** — sets the workspace up: three boards, columns per board's ceremony level.
2. **Monday 10:25** — the architecture sync ends. She pastes the transcript into Taskdeck and lets
   triage extract the action items instead of retyping six tasks.
3. **Monday, mid-flow** — two thoughts hit her while coding. She uses the quick-capture nib without
   leaving the keyboard, one with no destination, one linked to a board with a label and a due date.
4. **Monday 14:00** — she works the review backlog: approves what is right, applies it, rejects the
   duplicate, and checks the evidence behind each proposal before deciding.
5. **Monday 17:50** — plans and closes the day in Today, leaves a line for tomorrow-self.
6. **Monday 21:00** — her Italian co-founder pairs with her remotely. She switches to Paper Night and
   Italiano, then back.
7. **Throughout** — a second monitor mirrors the Payments board; keyboard-first navigation.

This scenario is chosen because it cannot be completed without exercising capture, transcript
extraction, triage, proposals, evidence/provenance, the two-phase apply, boards, realtime,
appearance, i18n, keyboard, and the command palette.

---

## 2. Prerequisites (exact seed content)

### 2.1 Boards to create

| Name | Columns | Created via |
| --- | --- | --- |
| `[HZN] Payments API Migration` | To Do · In Progress · Done | Enter key in name field, then "Add starter columns" |
| `[HZN] Devtools Side Quest` | Backlog (single, deliberate) | "Create" button, then "Add first column" |
| `[HZN] On-Call Follow-ups` | To Do · In Progress · Done | Enter key, then "Add starter columns" |

### 2.2 Quick captures

- **C1 (no board):** `[HZN] Rotate the sandbox Stripe webhook secret before Thursday's cutover`
- **C2 (board-linked):** `[HZN] Add contract tests for /v2/payment_intents idempotency-key replay before the cutover`
  · board `[HZN] Payments API Migration` · label `payments` · due `2026-08-27`
- **C3 (column-scoped):** `[HZN] Prototype the ripgrep-backed symbol index for the internal CLI`
  captured via a column's `+ CAPTURE` affordance on `[HZN] Devtools Side Quest / Backlog`

### 2.3 Transcript (paste verbatim — 1,937 characters, contains exactly 6 extractable action items)

```text
[HZN] Architecture Sync - Payments API Migration
Monday 24 August 2026, 10:00-10:25 CEST
Present: Nadia Ferrante (NF), Marco Bellini (MB), Priya Raman (PR)

NF: Right, twenty minutes, let's keep it tight. Cutover is Thursday and I want the unknowns off the board today.

MB: Biggest unknown for me is the webhook signing. We're still verifying against the v1 secret in staging.

NF: That's mine. I will rotate the sandbox Stripe webhook secret and re-point staging before Wednesday standup.

MB: Good. Second thing, the retry path. PaymentsClient backs off linearly right now, so when the gateway browns out we hammer it.

PR: We saw that in the June incident. It needs exponential backoff with jitter.

NF: Agreed. Marco, can you take that one?

MB: Yes. I will rewrite PaymentsClient retry to exponential backoff with jitter and add a unit test for the jitter bound.

NF: Priya, where are we on the idempotency replay tests?

PR: Not started. I will write contract tests for /v2/payment_intents idempotency-key replay - the same key twice must return the original intent, not a duplicate.

NF: Perfect, that is the one auditors will ask about.

PR: One more, the legacy shim. /v1/charges is still proxying. Do we drop it at cutover?

MB: Not at cutover. Give it two weeks of dual-run.

NF: Then someone needs to own the deletion. I will open a follow-up to remove the /v1/charges shim two weeks after cutover, with a dated reminder.

PR: And we should document the rollback. If Thursday goes badly, what is the actual sequence?

NF: Fair. Priya, draft a one-page rollback runbook for the payments cutover and put it in the repo before Wednesday.

PR: Will do.

MB: Last one from me, the dashboards. We have no alert on payment_intent failure rate.

NF: Add a Grafana alert for payment_intent failure rate above two percent over five minutes. Marco, you have the dashboard access.

MB: Taking it.

NF: Good. That is six things. Ship it.
```

**Expected extraction:** 6 `create card` operations. Four are `I will …` first-person commitments;
two are imperatives directed at another person (`Priya, draft …`, `Add a Grafana alert …`). A correct
extractor must catch both grammatical forms and must *not* emit a card for the discussion turns.

### 2.4 Tomorrow-note

```text
[HZN] Cutover is Thursday: confirm the webhook secret rotation landed before standup, then chase Marco on the jitter test.
```

### 2.5 Pre-run baseline (must be recorded before step 1)

```js
await fetch('/api/boards', {headers:{Authorization:'Bearer '+localStorage.getItem('taskdeck_token')}})
```

This run's baseline: **3 boards** (`Enter Key Test Board`, `Calendar QA Board`, `demo`),
**4 captures**, **3 proposals**. Nothing outside `[HZN]` may change.

> **Also snapshot the tomorrow-note — this run did not, and that is a hole in the safety contract.**
> Step 21 *overwrites* the note, and the note is a single free-text field with no `[HZN]` prefix to
> protect it: unlike boards, captures, and proposals, it cannot be made additive. Read and store its
> exact prior value from the Today surface before step 1, and restore that value verbatim at cleanup
> (§5). Because this run captured only the three collections above, it had no prior value to restore
> and cleared the field to `""` instead — destructive on any account where a note already existed.
> A replay must either restore the snapshot or run against a disposable account.

---

## 3. The journey

Each step: **Action → Expected → Observed → Evidence**. Verdicts: **PASS / DEGRADED / BROKEN**.

### Step 0 — Shim native dialogs

**Action.** Before any decision or delete action, run:

```js
window.__confirms=[];window.__prompts=[];
window.confirm=m=>{window.__confirms.push(String(m));return true};
window.alert=m=>{window.__alerts.push(String(m))};
window.prompt=(m,d)=>{window.__prompts.push(String(m));return d||'HZN reason'};
```

**Why it is part of the golden path:** two flows in this journey call native dialogs. Reject calls
`window.prompt`; without the shim the run blocks. See Step 17.

---

### Step 1 — Home / onboarding state

**Action.** Open `http://127.0.0.1:5000/`.
**Expected.** A workspace home reflecting current state.
**Observed.** Redirects to `/workspace/home`. Renders "Good evening, Demo.", `1 awaiting triage`,
then a dominant `III · YOUR FIRST LOOP` onboarding checklist reading **4/4 complete** — a finished
tutorial still occupying the primary surface. Quick-capture hint renders `⌘ ;` on Windows.
**Evidence.** `get_page_text` → `"4/4 complete … Complete ×4"`; hint glyph `⌘`.
**Verdict.** DEGRADED — known **#1936** (home reads as onboarding), **#1935** (⌘ glyph on Windows).

---

### Step 2 — Sidebar, version string, header controls

**Action.** Read the sidebar; click each header control.
**Expected.** Nav works; header trio (theme / bell / account) works; version matches the release.

**Observed.**

| Control | Result | Evidence |
| --- | --- | --- |
| Sidebar nav links | PASS — all route correctly | `href` values resolve |
| Sidebar version | **`v0.7.2`** | `innerText` tail — known **#1948** |
| Sun icon (`aria-label="Switch to dark Paper theme"`) | **PASS — works** | `td.paper.mode.v2` `paper`→`paper-night`; body class flips; label becomes "Switch to light Paper theme" |
| Bell (`aria-label="Notifications"`) | **BROKEN** — inert | no navigation, no dialog, despite `/workspace/notifications` existing |
| Avatar (`aria-label="Settings"`) | **BROKEN** — inert | no navigation, no dialog |

**Verdict.** **#1932 is now only two-thirds true** — the theme toggle works on this build; the bell and
the account avatar remain dead.

---

### Step 3 — Workspace modes (Guided / Workbench / Agent)

**Action.** Cycle the topbar mode selector on both a board and Home; diff `main` and the sidebar.
**Expected.** Each mode meaningfully changes the workspace.
**Observed.** Mode changes **only the sidebar tool list**:

- **Guided** — `MORE TOOLS`: Views, Notifications, Chat, Calendar, Activity, plus a collapsed `Advanced SHOW`.
- **Workbench** — `WORKBENCH TOOLS`: adds Metrics (M), Integrations (X), Ops (O), API Keys (K) inline.
- **Agent** — **byte-identical to Workbench.**

`main` content is identical across all three modes on both surfaces.

**Evidence.**
```
board:  guidedHtml=16300  workbenchHtml=16300  agentHtml=16300   sameText=true
home:   guided==workbench==agent  (783 chars each)
agent vs workbench: sidebarIdentical=true, mainIdentical=true, htmlSame=true
```
**Verdict.** DEGRADED — see finding **H-10** (Agent mode is a no-op alias of Workbench).

---

### Step 4 — Board creation via Enter key

**Action.** Boards → `+ New Board` → type `[HZN] Payments API Migration` → press **Enter**.
**Expected.** Board created.
**Observed.** Created **and** auto-navigated into it at `/workspace/boards/f6572b65-…`.
**Evidence.** `POST /api/boards → 201`; URL changed; `hasBoard=true`, `formStillOpen=false`.
**Verdict.** **PASS — #1933 does not reproduce on this build.**

*Note:* the create form exposes **name only** — no description field, though the board list renders
"No description" for every board.

---

### Step 5 — Empty board is no longer a dead end

**Action.** Observe the freshly created, zero-column board.
**Expected (per #1765).** Dead end with no add-column affordance.
**Observed.** A proper empty state: *"Columns are the lanes work moves through. Add the first one to
make this board usable."* with a `Column name` field, **Add first column**, and
**Add starter columns (To Do · In Progress · Done)**.
**Evidence.** `main.innerText` contains all three affordances.
**Verdict.** **PASS — #1765 appears fixed.**

---

### Step 6 — Starter columns

**Action.** Click **Add starter columns**.
**Observed.** Three columns created; header reads `0 cards · 3 columns`.
**Evidence.** `POST /api/boards/{id}/columns → 201` **×3**, then `GET /{id}`, `/cards`, `/labels` → 200.
**Verdict.** PASS.

---

### Step 7 — The add-column trap (single-column board)

**Action.** Create `[HZN] Devtools Side Quest`; use **Add first column** with `Backlog`; then look for
any way to add a second column.
**Expected.** An "add column" affordance persists.
**Observed.** Once ≥1 column exists, the entire add-column UI disappears. `main` contains **zero**
input elements and only three buttons.
**Evidence.**
```
{"main":"… 0 cards · 1 columns … § 01 Backlog 0 — EMPTY — + CAPTURE",
 "inputs":0, "buttons":["Capture hereC","ReviewR","+ capture"], "canAddColumn":false}
```
**Verdict.** **BROKEN — finding H-03.** The board is permanently one-lane through the Paper UI.

---

### Step 7b — The third board (On-Call)

**Action.** Boards → `+ New Board` → type `[HZN] On-Call Follow-ups` → **Enter** → **Add starter columns**.
**Expected.** Third board created with To Do · In Progress · Done, per §2.1.
**Observed.** Created; same behaviour as Steps 4 and 6 — auto-navigation into the board, then three columns.
**Evidence.** `POST /api/boards → 201`; `POST /api/boards/{id}/columns → 201` ×3. Cleanup deleted
**3** boards, which is the record that this board existed for the run.
**Verdict.** PASS — no new capability; it re-runs the Step 4 + Step 6 paths.

*Why this step is written out even though it proves nothing new:* the scenario in §1 promises three
workstreams and cleanup deletes three boards, but the original narrative only ever created two. A
literal replay stopped at two boards and then could not reconcile the cleanup table. This board is
also the third option in the Step 14b picker, which is why Step 14b now names the board it selects.

---

### Step 8 — What board management actually exposes (refines #1945)

**Action.** Enumerate every control on a populated board; then open a card.
**Observed.**

*Board surface — 5 controls only:* `Capture here (C)`, `Review (R)`, `+ capture` ×3.
No board settings/rename/delete, no column rename/delete/reorder, no direct "add card", no add-column.

*Card modal — a full editor:* Title, Description, Due Date, "Mark as blocked", Labels, Comments,
**Delete Card**, Share, Cancel, Save Changes — **plus a `CAPTURE ORIGIN` block** with
`Proposal status: Applied`, `Open Capture`, `Open Proposal`, and `Triage run: <guid>`.

**Verdict.** **#1945 is narrower than filed.** Direct card *edit* and *delete* ship in Paper today.
What is genuinely missing: board settings/rename/delete, column editing, direct card add, and
add-column-after-the-first (H-03).

---

### Step 9 — Quick-capture surfaces and their two different shortcuts

**Action.** From `main` focus, press `Ctrl+;` then `Ctrl+Shift+C`.
**Observed.** They open **different** surfaces:

| Shortcut | Target | Element |
| --- | --- | --- |
| `Ctrl+;` | Home's inline capture field | `INPUT` placeholder `Capture a thought...` |
| `Ctrl+Shift+C` | Global Quick Capture modal | `TEXTAREA` placeholder `Capture a thought, task, or follow-up...` |

The modal (`.td-capture-modal`) has two tabs — **Quick Capture** and **Transcript** — a
`SAVE CAPTURE` button, `CANCEL`, and the hint *"Press Ctrl/Cmd+Enter to save."*

**Verdict.** PASS for both. **#1937's "no submit affordance" does not reproduce** on this modal.
The keyboard map documents only `Ctrl/Cmd+Shift+C`; Home advertises `⌘ ;`. Both work, but nothing
tells the user they are different surfaces.

---

### Step 10 — Capture C1 (nib, no board)

**Action.** Type C1 in the modal, press `Ctrl+Enter`.
**Observed.** Saved; auto-navigated to `/workspace/inbox`.
**Evidence.** `POST /api/capture/items → 201`. Toast: `✓ APPLIED — Capture saved to inbox`.
**Verdict.** PASS (**#1938 did not reproduce**) — but the toast says **"APPLIED"** for an inbox save;
see finding **H-08**.

---

### Step 11 — Sidebar badge staleness

**Action.** Compare the sidebar Inbox badge before and after a reload.
**Observed.** Badge reads `Inbox · 1` immediately after saving; after a full reload it reads
`Inbox · 2`, matching the two `NEW` rows. Meanwhile the page header reads `5 IN QUEUE`.
**Verdict.** DEGRADED — finding **H-12**.

---

### Step 12 — Capture C2 via the Inbox Composer (board + label + due)

**Action.** Inbox → Composer tab → body, board `[HZN] Payments API Migration`, label `payments`,
due `2026-08-27` → `Ctrl+Enter`.
**Observed.** All fields bound correctly; capture saved.
**Evidence.** `{board:"[HZN] Payments API Migration", labels:"LABELS PAYMENTS ×", due:"2026-08-27"}`;
`POST /api/capture/items → 201`.
**Verdict.** PASS.

> **Important semantic:** the composer states *"Linking to a board creates a proposal, not a card."*
> In practice **no proposal exists at capture time** — the Review queue stayed at 0. The proposal is
> created when the capture is **accepted** in the Inbox (Step 14). The copy describes the eventual
> destination, not the moment.

---

### Step 13 — "Open capture" is inert

**Action.** Click a capture row (`aria-label="Open capture <guid>"`).
**Expected.** A detail/pre-triage view.
**Observed.** Nothing. No dialog, no navigation, no DOM change, **zero network requests**.
**Evidence.** `read_network_requests` with `clear:true` before the click → no `/api/` requests after.
**Verdict.** BROKEN — supports **#1944** ("no pre-triage edit") with a concrete dead control.
API confirms `canEditSuggestion: false`.

---

### Step 14 — Inbox triage (accept with and without a board)

**14a — capture that already has a board.** Click `Accept`.
**Observed.** Status `NEW → READY FOR REVIEW`. `POST /api/capture/items/{id}/triage → 202`.
**Verdict.** PASS.

**14b — capture with no board (the #1944 repro).** This is **C1**. Click `Accept`.
**Observed.** The row now reveals an inline **`BOARD` / `Select a board…`** picker with
**`Accept on board`** and **`Cancel`**. With nothing selected, `Accept on board` is
**`disabled: true`** and fires zero requests. Selecting a board enables it; clicking then yields
`POST …/triage → 202` and status `TRIAGING`.
**Evidence.** `{before:{selVal:"Select a board…", disabled:true}}` → after selection `{nowEnabled:true}`.

> **Select `[HZN] Payments API Migration` here — the choice is load-bearing, not illustrative.** All
> three boards are offered by this point. Steps 17, 18 and 19 all assume C1's proposal landed on
> Payments; routing C1 to Devtools or On-Call changes the Payments review queue, the proposal that
> Step 17 rejects, and the 6→7 card count that Step 19 asserts.
**Verdict.** **PASS — #1944 is fixed.** It is no longer a silent no-op on an enabled control; the
button is correctly disabled and the required input is surfaced inline.

---

### Step 15 — Transcript capture and extraction (the centrepiece)

**Action.** `Ctrl+Shift+C` → **Transcript** tab → paste §2.3 → **SAVE CAPTURE** → in Inbox,
`Accept` → select `[HZN] Payments API Migration` → `Accept on board`.

**Observed.**
- Transcript surface accepts up to **200,000 characters**; counter tracked the paste (`1,937 / 200,000`);
  a `.txt` upload path also exists.
- Capture stored with source badge **`TRANSCRIPT`**.
- `POST …/triage → 202`, then the client **polls** `GET /api/capture/items/{id}`.
- Extraction completed in ≈2s and produced a proposal with **exactly 6 operations**.

**All six action items were extracted correctly**, including both grammatical forms:

| # | Extracted card title |
| --- | --- |
| 0 | Rotate the sandbox Stripe webhook secret and re-point staging before Wednesday standup. |
| 1 | Rewrite PaymentsClient retry to exponential backoff with jitter and add a unit test for the jitter bound. |
| 2 | Write contract tests for /v2/payment_intents idempotency-key replay to ensure the same key returns the original intent. |
| 3 | Open a follow-up to remove the /v1/charges shim two weeks after cutover, with a dated reminder. |
| 4 | Draft a one-page rollback runbook for the payments cutover and put it in the repo before Wednesday. |
| 5 | Add a Grafana alert for payment_intent failure rate above two percent over five minutes. |

Each card's `description` is the **verbatim transcript sentence**, and each operation carries
**evidence links with character spans** into the source transcript
(e.g. `{sourceType:"Transcript", spanStart:1541, spanEnd:1647, viewable:true}`).

**Provenance recorded:**
```json
{"promptVersion":"llm-triage.v2","provider":"OpenAI","model":"gpt-4o-mini",
 "triageRunId":"9938378f-…","proposalId":"8efe8562-…","sourceSurface":"capture"}
```

**Verdict.** **PASS — and the strongest part of the product.** Note op 2 is a genuine paraphrase, not
a pattern match.

> **This step also produced finding H-01.** The provenance above is *honest*: source tracing confirms
> `llm-triage.v2` + `OpenAI` is only reachable after a completed, non-degraded live call (the
> deterministic path stamps `deterministic-extractor` / `triage.v1`). The Chat page independently
> reports **"Live LLM configured — OpenAI (gpt-4o-mini)"**. Yet the Review provenance panel tells the
> user captures are handled by *"a deterministic offline extractor"*. See HORIZON_FINDINGS H-01.

---

### Step 16 — Review: evidence and decision surface

**Action.** Open `/workspace/review`; select the 6-op proposal; read every section.

**Observed — a genuinely rich decision surface:**

- Header: `QUEUE · 3 AWAITING · 0 STALE`, filters `All / Mine / Stale`, note that
  *"Sorting only changes presentation; review actions remain manual."*
- Confidence `.81` with *"Above your apply threshold (set 0.70 · Settings)"*.
- `DECISION — 6 operations · explicit review · atomic apply`
- **`Step 1 of 2 · approving does not change the board`**
- `§ I The change` — **BEFORE · TODAY** vs **AFTER · ON APPLY**, plus `PER-FIELD CHANGES`
- `§ II Provenance` — per-operation inference + confidence, `View full read-set →`
- `§ III Side effects` — CARDS / SUBTASKS / COMMENTS / ACTIVITY LOG / NOTIFICATIONS / WEBHOOKS / CALENDAR
- `§ IV Conflicts & warnings` — `✓ CLEAR · No conflicts detected`
- `§ V History · this card`
- `Technical details` — `CONFIDENCE BREAKDOWN`: Pattern match 1.00, Reach 1.00, Operation safety 0.90, Recency 1.00
- `DECIDE WITH KEYS` legend

**Verdict.** PASS for content. Defects found in this pane: **H-01** (false provenance footnote),
**H-05** (dead links), **H-15** (UTC vs local in the same pane).

Dead links confirmed here: both **`Tune heuristics →`** (known **#1941**) and
**`View full read-set →`** (**not previously filed**) are `href="#"`; clicking both produced
`urlChanged:false`, `htmlDelta:0`, zero network requests.

---

### Step 17 — The decision actions

**Queue state entering this step — three Payments proposals**, exactly the `QUEUE · 3 AWAITING` that
Step 16 reads:

| Tag | Origin | Overlaps |
| --- | --- | --- |
| **P-T** | the transcript capture, 6 operations (Step 15) | — |
| **P-C1** | capture C1, accepted onto Payments in Step 14b | duplicates transcript op 0 (rotate the webhook secret) |
| **P-C2** | capture C2, accepted in Step 14a | duplicates transcript op 2 (idempotency-key contract tests) |

**Prescribed action order — follow it literally; the later steps depend on it:**

1. **Approve + Confirm apply `P-T`** → the 6 cards Step 18 verifies.
2. **Reject `P-C1`** — this is the "rejects the duplicate" beat in §1.
3. Leave `P-C2` in the queue; Step 19 approves and applies it as "the remaining Payments proposal",
   producing the 7th card.

> **Honesty note on this ordering.** `P-C1` and `P-C2` both duplicate a transcript operation, and the
> run's captured evidence does **not** preserve which of the two actually took the reject — only that
> one was rejected and one was applied. The order above is therefore **normative for replay**, not a
> transcription of the original run. It is the assignment consistent with every recorded count
> (7 Payments cards, 4 proposals, 8 cards deleted at cleanup); pick the other assignment and the counts
> still hold, but two runs would no longer agree on the seventh card's title.

| Action | Binding | Result | Evidence |
| --- | --- | --- | --- |
| **Approve** (`P-T`) | `⏎` | **PASS** | `POST …/approve → 200`; status → `approved`; no native dialog |
| **Confirm apply** (`P-T`) | `⏎` again | **PASS** | in-app `.td-dialog`, then `POST …/execute → 200` |
| **Reject** (`P-C1`) | button | **PASS, but native prompt** | `window.prompt("Optional rejection reason:")` — finding **H-07** |
| **Request edit** | `E` / button | **BROKEN** | see below — finding **H-02** |
| **Defer** | `D` | not exercised | — |
| **Toggle provenance** | `P` | **BROKEN** | `×` closes the drawer; `P` will not reopen it |

**The two-phase apply is now legible.** After approving, the pane states:
*"Approved — not yet applied to the board. Press ⏎ (or 'Confirm apply') to execute it on the board;
you will be asked to confirm."*, the step label becomes **`Step 2 of 2 · confirm to write it to the
board`**, and the button **relabels from `Approve` to `Confirm apply`**. The final confirmation is an
**in-app dialog**, not a native `confirm()`:

> *"Apply to the board? This is the second and final step: it executes the approved proposal on your
> board. Nothing has been written to the board yet. 6 operations will be applied."*

**Verdict on #1818 / #1942:** substantially improved. The flow no longer "reads as clicking the same
button three times" — the label, the step counter, and the copy all change between phases, and the
native confirm is gone.

---

### Step 18 — Verify the apply actually materialised

**Action.** Open `[HZN] Payments API Migration` after applying.
**Observed.** `6 cards · 3 columns`; all six extracted cards present in **To Do**, each with the
verbatim transcript sentence as its description and a `C-<id>` serial.
**Verdict.** **PASS — full loop proven:** transcript → capture → triage → proposal → approve →
confirm → apply → board.

---

### Step 19 — Realtime (per-board SignalR)

**Action.** Open the Payments board in a **second tab**; record card count; in tab 1 approve+apply
**`P-C2`** — the one Payments proposal Step 17 left in the queue; re-read tab 2 **without reloading**.
**Observed.**
```
{"baselineWas":"6","nowShows":"7","updatedWithoutReload":true,"hasNewCard":true}
```
`POST /hubs/boards/negotiate?negotiateVersion=1 → 200` confirms the per-board hub.
**Verdict.** **PASS.**

---

### Step 20 — Column-scoped capture round trip

**Action.** On `[HZN] Devtools Side Quest / Backlog`, click `+ CAPTURE`; capture C3; accept; approve; apply.
**Observed.** `+ CAPTURE` deep-links to
`/workspace/inbox?boardId=a7408e6f-…&columnId=1ce6374c-…` with the board picker **correctly
preselected** to `[HZN] Devtools Side Quest`. After apply, the card landed in **Backlog**.
**Verdict.** PASS for the round trip — but the same URL silently filters the Inbox to
`0 IN QUEUE` with no filter chip (finding **H-04**).

---

### Step 21 — Today view: plan, note, seal

**Action.** Open `/workspace/today`.
**Observed.**
- Header `DOSSIER · DAY'S LEDGER · SEALED AT END OF SESSION`; the day was already sealed as
  `D-2026-08-22-001`, `Sealed for the day`.
- Counters: `1 captures need triage`, `0 proposals await review`, `0 overdue`, `0 due today`, `0 blocked`.
- `§ I Cadence` — 24h strip rendering `FIRST ACTION --:-- UTC`, `PEAK HOUR no peak`,
  `LAST ACTION --:-- UTC` **despite a full day of real activity**.
- `§ II Ledger` — *"A live day ledger is not available yet. **No events are being invented.**"*
- `§ III Decisions`, `§ IV Boards touched` — "not available yet" placeholders.
- `§ VI Streak` — "1 days."
- `§ VII A line for tomorrow` — textarea, `Saved · auto`.

**Tomorrow-note.** Typed §2.4 → `PUT /api/today/tomorrow-note → 200`, `Saved · auto`. **PASS.**
**"Write a note" button.** Focuses the note textarea. **PASS** (subtle but functional).
**"Day sealed" button.** Still **enabled** after sealing, and **completely inert** — zero network,
zero DOM change, no dialog. **BROKEN — finding H-13.**
**Unseal.** None anywhere on the page — confirms **#1939**.

**Verdict.** DEGRADED. The honesty of §II ("No events are being invented") is a genuine strength;
§I Cadence contradicts it by rendering an empty chart instead of the same disclosure.

---

### Step 22 — Appearance: theme

**Action.** `/workspace/settings/appearance` → click each theme; assert computed `background-color`.
**Observed.**

| Theme | `td.paper.mode.v2` | body class | computed background |
| --- | --- | --- | --- |
| Paper (Light) | `paper` | `paper` | `rgb(243, 238, 229)` |
| Paper Night (Dark) | `paper-night` | `paper-night` | `rgb(20, 17, 13)` |
| Off (Legacy / Obsidian) | `off` | *(cleared)* | `rgb(19, 19, 19)` |
| Auto (match system) | `auto` | `paper-night` | `rgb(20, 17, 13)` |

**Verdict.** **PASS** — all four themes apply real, distinct styling and round-trip cleanly.

---

### Step 23 — Appearance: language

**Action.** Switch to **Italiano**; sample five surfaces; switch back to English.
**Observed — translation quality is high where it exists:**

| Surface | Italian |
| --- | --- |
| Appearance | `IMPOSTAZIONI · Aspetto · Scegli l'aspetto di Taskdeck.` |
| Home | `Area Di Lavoro · Sera · Buonasera, Demo. · 1 da smistare · IL TUO PRIMO CICLO` |
| Inbox | `INBOX · SUPERFICIE DI CATTURA · 7 IN CODA · Niente arriva alla bacheca senza la tua approvazione.` |
| Review | `CODA · 0 IN ATTESA · Ordine di rischio: basso, medio, alto, critico.` |
| Boards | `AREA DI LAVORO · Le mie bacheche · + Nuova bacheca · Nessuna descrizione` |

**Gaps:**
- The **entire sidebar stays English** (Home, Today, Review, Boards, Inbox, `PRIMARY LOOP`, `MORE TOOLS`, Settings…).
- The **Inbox capture composer stays English inside an Italian page**:
  `CAPTURE · DRAFT | local-only · saves to inbox | BODY | Drop files here, or | BROWSE | BOARD |
  No board · land in inbox | LABELS | DUE (OPTIONAL) | Captures land in Inbox. Linking to a board
  creates a proposal, not a card.`

The page discloses this: *"Taskdeck viene tradotto una superficie alla volta."*
**Verdict.** DEGRADED (disclosed) — finding **H-14**.

---

### Step 24 — Keyboard and command palette

| Binding | Documented in `?` map | Works | Evidence |
| --- | --- | --- | --- |
| `?` open keyboard map | yes | **PASS** | dialog `HELP · KEYBOARD MAP` |
| `Ctrl+K` command palette | yes (`⌘K`) | **PASS** | dialog with full route catalog |
| Palette search | — | **PASS** | typing `payments` returned the board **and** a matching card with `board / column` context |
| `Ctrl+;` | Home hint only | **PASS** | focuses Home inline input |
| `Ctrl+Shift+C` | yes | **PASS** | opens Quick Capture modal |
| `⏎` approve / confirm | yes | **PASS** | `POST …/approve → 200`, `…/execute → 200` |
| `H` `T` `B` `I` `R` navigate | **yes** | **BROKEN** | `t`, `r` with `main` focused → URL unchanged |
| `G T` go to Today | **yes** | **BROKEN** | URL unchanged |
| `P` toggle provenance | **yes** | **BROKEN** | `×` closes drawer; `P` does not reopen (real + synthetic keys) |
| `C` capture / `R` review on board badge | shown on board | **BROKEN** | no effect |

**Verdict.** DEGRADED — finding **H-06**. The keyboard map is partly aspirational; the modifier-based
bindings all work, the single-letter navigation set does not.

---

### Step 25 — Remaining nav surfaces

| Surface | State |
| --- | --- |
| Views | Renders; "New View" + `WHAT IS THIS?` explainer. PASS |
| Notifications | Renders `0 unread · No notifications found` — after 2 applies + 1 reject. Finding **H-17** |
| Calendar | Renders; Grid / Timeline tabs. PASS |
| Activity | **PASS — audit trail is real.** Board history shows each card creation traced to `Automation proposal 8efe8562-…, sequence 2: create card. Parameters: {…}` `by demo` |
| Chat | Renders; reports **"Live LLM configured — OpenAI (gpt-4o-mini)"** with an honest caveat that the health check does not prove a live request succeeded |
| Metrics | Renders with board picker + date range. PASS |
| Integrations | Renders; honestly states connectors "do not yet ingest external content" |
| Export / Import | Renders — but **requires a raw `BOARD ID`** typed in, while every other surface offers a picker. Finding **H-18** |

---

## 4. Capability coverage matrix

| # | Capability | Exercised | Verdict | Evidence | Issue |
| --- | --- | --- | --- | --- | --- |
| 1 | Home / onboarding state | yes | DEGRADED | 4/4 checklist still dominant | #1936, #1935 |
| 1b | Sidebar navigation | yes | PASS | all links route | — |
| 1c | Workspace modes Guided/Workbench/Agent | yes | DEGRADED | sidebar-only diff; Agent ≡ Workbench | **#1972** (H-10) |
| 2 | Board create (Enter + button) | yes | PASS | `POST /api/boards → 201` | #1933 not reproduced |
| 2b | Starter columns | yes | PASS | 3 × `POST /columns → 201` | #1765 fixed |
| 2c | Add column after the first | yes | **BROKEN** | `inputs:0`, `canAddColumn:false` | **#1965** (H-03; gap in PR #1959) |
| 2d | Board settings / rename / delete (UI) | yes | **BROKEN** | absent from Paper | #1945 |
| 2e | Column edit / reorder / delete (UI) | yes | **BROKEN** | absent from Paper | #1945 |
| 2f | Direct card add (UI) | yes | **BROKEN** | only `+ CAPTURE` | #1945 |
| 2g | Card edit / delete / comments | yes | **PASS** | full modal; `DELETE …/cards/{id} → 204` | refines #1945 |
| 3 | Quick-capture nib | yes | PASS | `Ctrl+Shift+C`, `POST /capture/items → 201` | #1937/#1938 not reproduced |
| 3b | Inbox composer (board+label+due) | yes | PASS | fields bound; 201 | — |
| 3c | Column-scoped `+ CAPTURE` | yes | PASS | deep-link preselects board | — |
| 4 | Transcript capture + extraction | yes | **PASS** | 6/6 items, evidence spans | — |
| 5 | Inbox triage accept (with board) | yes | PASS | `POST …/triage → 202` | — |
| 5b | Accept with no board | yes | **PASS (fixed)** | button correctly `disabled` | **#1944 fixed** |
| 5c | Pre-triage edit / "Open capture" | yes | **BROKEN** | zero network, no view | #1944 |
| 5d | Reject capture | not exercised | — | avoided to protect pre-existing rows | — |
| 6 | Review evidence / provenance panes | yes | PASS | §I–§V + confidence breakdown | — |
| 6b | Before / After diff | yes | PASS | `BEFORE · TODAY` / `AFTER · ON APPLY` | — |
| 6c | Approve → confirm → apply | yes | PASS | `/approve → 200`, `/execute → 200` | #1818/#1942 improved |
| 6d | Reject proposal | yes | PASS (native prompt) | reason persisted | **#1969** (H-07) |
| 6e | Request edit | yes | **BROKEN** | disables all 4 buttons + the whole review keymap; composer renders below the fold | **#1964** (H-02, mechanism corrected) |
| 6f | Applied proposal materialises | yes | **PASS** | 6 cards in To Do | — |
| 6g | Reopen an applied proposal | yes | **BROKEN** | `RECENTLY APPLIED` rows inert | **#1967** (H-05) |
| 7 | Today plan / counters | yes | DEGRADED | Cadence empty | #1939 |
| 7b | Tomorrow note | yes | PASS | `PUT /today/tomorrow-note → 200` | — |
| 7c | Daily seal / unseal | yes | **BROKEN** | seal button enabled + inert; no unseal | #1939 (H-13 folded in as a comment) |
| 8 | Theme Paper / Night / Legacy / Auto | yes | **PASS** | computed backgrounds differ | — |
| 8b | Language English ↔ Italiano | yes | DEGRADED | sidebar + composer untranslated | H-14 — not filed (LOW), on #1947 |
| 9 | Command palette `Ctrl+K` | yes | PASS | full catalog + card search | — |
| 9b | Keyboard map `?` | yes | PASS | dialog renders | — |
| 9c | Letter navigation `H/T/B/I/R`, `G T` | yes | **BROKEN** | URL unchanged; no handler exists anywhere | **#1968** (H-06) |
| 9d | `P` provenance toggle | yes | **BROKEN** | `×` works, `P` does not — handler *does* exist; likely silenced by #1964's `busy` lock | **#1968** (H-06), see **#1964** |
| 10 | Realtime per-board SignalR | yes | **PASS** | 6→7 cards, no reload | — |
| 11 | Views / Calendar / Metrics / Integrations | yes | PASS | all render | — |
| 11b | Activity / audit log | yes | **PASS** | proposal-traced entries | — |
| 11c | Notifications | yes | DEGRADED | 0 after 3 decisions | H-17 — not filed (LOW), on #1947 |
| 11d | Export / Import | partial | DEGRADED | raw GUID required | H-18 — not filed (LOW), folds into #1644 |
| 12 | Sidebar version string | yes | **BROKEN** | `v0.7.2` | #1948 (PR #1956) |
| 12b | Header trio (theme/bell/avatar) | yes | PARTIAL | theme works; bell + avatar dead | #1932 (narrowing note posted) |
| 12c | `Tune heuristics →` | yes | **BROKEN** | `href="#"`, inert | #1941 |
| 12d | `View full read-set →` | yes | **BROKEN** | `href="#"`, inert | **#1967** (H-05c) |

**Totals — 44 capability rows:** **19 PASS**, **11 DEGRADED**, **14 BROKEN**, 0 blocked.
Distinct capability areas from the brief: **12 of 12 exercised**.

**Filed 2026-08-22:** H-01..H-12 → **#1963–#1974** in order. H-13 folded into #1939; H-14..H-19 recorded
on tracker #1947 rather than filed. Two findings were corrected by source trace before filing — see
#1964 (`Request edit` is an escapable lock with an off-screen composer, not a brick) and #1973 (board
delete is a **soft archive with no cascade**; the captures and proposals were filtered out, not
destroyed). Tracker summary: https://github.com/Chris0Jeky/Taskdeck/issues/1947#issuecomment-5381868022

---

## 5. Cleanup

**Deleted (all `[HZN]`-prefixed):**

| Entity | Count | Method | Result |
| --- | --- | --- | --- |
| Cards | 8 | 1 via card modal `Delete Card` (in-app confirm), 7 via `DELETE /api/boards/{b}/cards/{c}` | all `204` |
| Boards | 3 | `DELETE /api/boards/{id}` | all `204` |
| Captures | 4 | vanished with their boards (**not** a cascade delete — see below) | gone from the list API |
| Proposals | 4 | vanished with their boards (**not** a cascade delete — see below) | gone from the list API |
| Tomorrow-note | 1 | **cleared**, not restored — see the warning below (`PUT` rejects an empty body — needs a `date` field) | `Saved · auto`, value `""` |

**Settings restored:** theme `paper`, workspace mode `guided`, language `en`.
**Second tab closed.**

> **The tomorrow-note was cleared, not restored — the one place this run broke its own additive-only
> contract.** §2.5's baseline recorded boards, captures, and proposals but not the note, so no prior
> value existed to put back and the field was left at `""`. On this account that cost nothing
> observable; on any account with an existing note it would have destroyed user content that no
> snapshot could recover. **Fix before replaying:** snapshot the note in §2.5 and restore the exact
> string here, or run the journey against a disposable account.

**Post-cleanup verification:**
```json
{"boards":["Enter Key Test Board","Calendar QA Board","demo"], "boardCount":3,
 "captures":["Test capture from dogfooding audit…","calendar QA tes","ggg","demo"],
 "proposals":[{"id":"b635f362"},{"id":"67b35260"},{"id":"d55eef25"}],
 "anyHZNleft": false}
```

**Exactly matches the pre-run baseline *as the list endpoints report it*.** That is the honest scope of
this check, and it is weaker than "nothing remains":

> **The rows are still in the database.** `DELETE /api/boards/{id}` is a **soft archive**, not a delete
> (`BoardService.cs:357`, `board.Archive(); // Soft delete`), and the captures and proposals were
> *filtered out* alongside the archived board rather than destroyed — see the #1973 note below. A
> snapshot comparison against these list endpoints therefore reports equality while the run's boards,
> captures, and proposals persist, invisibly, forever. **Repeated replays accumulate.**
>
> **For a repeatable suite this teardown is not sufficient.** Use a disposable database (a throwaway
> SQLite file per run is the cheap option) or purge the rows directly and assert their absence by
> direct inspection, not by re-reading the list APIs that hid them in the first place.

> **Behaviour worth noting during cleanup → filed as #1973.** Deleting a board made its captures and its
> proposals vanish from every list. Inbox captures are presented as user-owned records that live
> independently of boards ("It will sit here, untouched, until you triage it"), and applied proposals are
> the decision ledger. Both vanished silently with the board, with no warning at delete time.
>
> **Corrected during filing — this is NOT cascade deletion.** `DELETE /api/boards/{id}` is a **soft
> archive** (`BoardService.cs:357`, `board.Archive(); // Soft delete`), and no cascade to either dependent
> exists: captures (`LlmRequest`) use `DeleteBehavior.SetNull`, and `AutomationProposal.BoardId` is a
> scalar with no FK relationship at all. The records were almost certainly filtered out of their list
> endpoints alongside the archived board, not destroyed. Same symptom, different fix — #1973's first
> acceptance criterion is to confirm row survival by direct inspection.

---

## 6. Replaying this as a test suite

1. Snapshot `/api/boards`, `/api/capture/items`, `/api/automation/proposals` — assert unchanged at teardown.
2. Shim `confirm`/`alert`/`prompt` before Step 17 (Reject calls a native `prompt`).
3. Prefer status-code assertions on `POST /api/capture/items`, `…/triage`, `…/approve`, `…/execute`
   over screenshots; the UI text for these is stable but the toasts auto-dismiss in ~2s.
4. The transcript in §2.3 is the fixture: assert **exactly 6 operations** and that operations 4 and 5
   (the imperative forms) are present — a regression to naive `I will …` matching drops those two.
5. Realtime: assert the second tab's card count changes **without** a reload.
6. Teardown by deleting the boards; captures and proposals disappear from the list endpoints with them
   (via board archiving, not cascade deletion — see #1973 before relying on this for cleanup).
