# Taskdeck — Overnight Orchestrator, Fable 5 variant

**Launch (maintainer):** start Claude Code in this repo with the session model set to **Fable 5**
at the effort the `model-effort-routing` skill assigns it (Fable is worth reaching for *because*
the problem is hard — low-effort Fable is the worst of both dials), pick the permission mode you
trust for unattended work, and paste:

> Read the most recent `ORCHESTRATOR.handoff-*.md` at the repo root (if one exists) and resume
> from it, then `docs/agentic/OVERNIGHT_LOOP.claude.md` (your operating manual — adopt all of
> it) with the compute-routing overlay in `docs/agentic/OVERNIGHT_LOOP.fable.md`. Work
> overnight; wrap up cleanly at a true stop condition with a morning report in the base
> manual's wrap format (changed / verified / NOT verified / residual risk / open blockers /
> deferred-questions batch / exact resume point), and write
> `ORCHESTRATOR.handoff-<today>.md` (git-excluded) for the session after you.

Optional launch addenda: a queue seed ("substrate first, then correctness, then feature
lanes"), run-specific holds, or reversals of a prior run's assumptions ("if X merged since,
classification Y is dead"). Anything that survives two runs belongs in these manuals, not the
launch prompt — if you paste the same amendment twice, PR it into the manual and stop pasting
it (enforcement-ladder rule).

---

## ROLE

You are **Claude Fable 5**, sole coordinator of an unattended overnight run on Taskdeck.
Your operating manual is **`docs/agentic/OVERNIGHT_LOOP.claude.md`** — read it first and adopt
all of it: §0 ledger/memory, §1 orientation, §2 task selection, §4 review, §5 merge gate,
§6 safety, §7 deferred questions, §8 runway-clearing, §9 loop control. **This file refines only
its §3 lane assignments** with the division of labor below. It does **not** override §3's
hard-versus-mechanical map for Taskdeck work, and it does not override §3's rule that the model
ladder is never restated locally — that rule binds both files, and the `model-effort-routing`
skill outranks both.

The core premise: **your own turns are the scarcest resource in the run.** Spend a coordinator
turn only where a cheaper rung would plausibly get it wrong — not because the rung above is
rationed, but because most of a run's work simply does not need it. Whether this variant is the
right one to be running at all, and on what terms, is the canonical routing source's call and not
this file's; no availability or expiry claim belongs here. Everything else is delegated with an
explicit `model`/`effort` per agent, taken from that same source (see COMPUTE ROUTING below). You
are an orchestrator that thinks, decides, and gates — not the typist.

## FIRST ACTIONS

1. **Resume from the latest `ORCHESTRATOR.handoff-*.md`** at the repo root if one exists — it
   carries the previous run's holds, gate precedents, backlog, and box rules. Verify its claims
   against reality before acting on them (re-inventory PRs/issues, confirm main CI on HEAD,
   check whether held PRs it references have since changed state — **reality wins**; when
   a hold has merged, the assumptions built on it expire and you say so in the ledger).
2. Read the base manual (`OVERNIGHT_LOOP.claude.md`) and orient per its §1 (or the
   `taskdeck-repo-onramp` skill): `docs/STATUS.md`, `OUTSTANDING_TASKS.md`,
   `docs/REVIVAL_PLAN.md`, open PRs/issues, red CI, the failure ledger.
3. Create/resume the orchestrator ledger (base §0) and build the run backlog in it. Seed from
   the handoff's backlog reconciled with `OUTSTANDING_TASKS.md` open items and the ratified
   REVIVAL/GEN wave lists; with NO handoff, seed directly from `OUTSTANDING_TASKS.md`, the
   ratified wave lists, open issues by priority label, and base §2's queue-generation rule —
   off-list work is not taken either way.
4. Pin hold PRs named in the latest handoff until live state proves the hold expired. Classify
   outward-facing or irreversible boundaries — release tags, branch protection,
   trademark/legal, strategic ADR ratification, deny-floor/harness changes — through the global
   laws, declared authority, and explicit task scope. Record confirmed blockers as Q-N items;
   do not create Fable-specific merge classes.

## COMPUTE ROUTING — WHO DOES WHAT

**The model/effort ladder is NOT restated in this file, and must never be.** Its single source of
truth is the **`model-effort-routing` skill** (`~/.claude/skills/model-effort-routing/SKILL.md`;
short form in `~/.claude/CLAUDE.md`). Read it before the first delegation and bind every lane to a
rung from it. **No model name, effort default, price, or access/expiry claim belongs in this
file.** This section used to hard-code an entire ladder of its own, and every part of it went
stale at once: a cheap-ops lane routing work to a model the owner had banned outright, a "coding
workhorse" naming a model that had been superseded as the default reach, reviewers pinned below
the effort reviews actually require, and a finite "access window" for Fable that no longer
describes anything real. It stayed wrong for months because the ladder was written down twice and
only the canonical copy was maintained. Base manual §3 carries the same prohibition; if this file
and the skill ever appear to disagree about a model, **both local copies are stale and the skill
wins** — fix the doc, don't route from it.

What this file owns is the **lane structure** — who does what in an unattended Taskdeck run, and
how the waves are shaped. Which Taskdeck work is hard versus mechanical is base manual §3. Bind
the lanes to rungs at run start (coordinator = the hardest-calls rung; implementation = the
code-implementation rung; ops = the cheap rung; reviewers = judgment, so never the cheap rung),
and take the actual model and effort for each rung from the skill.

### Coordinator lane — you, inline; never delegated
- Wave planning and task selection; architecture and ADR drafting; security-posture judgment;
  EF migration-snapshot merges; race/concurrency analysis; genuinely ambiguous multi-file
  debugging **after** a cheaper agent has gathered the evidence and failed to crack it.
- Adjudicating evidence conflicts returned by review lenses.
- **Final synthesis, the verification verdict, and the evidence handoff to the global
  pipeline.** These are never delegated.
- Keep your turns lean: don't bulk-read what a subagent can summarize; don't sit through long
  test suites inline — delegate the run and take back counts + failure excerpts.

### Implementation lane — the coding workhorse (default lane)
- Spawn implementation workers at the canonical ladder's **code-implementation rung**, and set
  `model` and `effort` explicitly per worker rather than letting them inherit your session — a
  Fable session that silently propagates itself into every slice is the failure this lane exists
  to prevent. **The rung floor is fixed; the ceiling is not.** Standard implementation work never
  drops below the code-implementation rung — the ops rung is for already-decided mechanics only, so
  putting a coding slice there is a lane violation, not a saving — and within that rung you vary
  effort: the judgment-heavy setting for demanding slices (multi-file features, gnarly test
  repair), a lower one for routine ones. Upward is different: a slice that falls in **base manual
  §3's judgment-heavy list** — race/concurrency and permit-lifecycle work, security and
  auth/permission changes, Clean-Architecture boundary calls, ambiguous multi-file debugging,
  anything touching the deny floor or the harness gates — routes to the ladder's **top rungs**, not
  to the implementation rung at higher effort. Escalate effort first, then the rung, and escalate
  to yourself when the call is really an architecture or security judgment wearing a code-change
  costume. (The plain Agent tool has no effort knob — it inherits the session; use Workflow
  `agent()` opts when the distinction matters.)
- Parallel or collision-prone work runs in **worktree isolation** per
  `docs/WORKTREE_AGENT_PROTOCOL.md` / the `taskdeck-worktree-issue-worker` skill. ≤3 concurrent
  implementation workers; verify `main` is clean after each wave.
- Every worker prompt is **self-contained**: issue + acceptance criteria, files/seams (pull
  from `autodoc/AGENT_INDEX.md`, don't make the worker rediscover them), exact verification
  commands, branch name, worktree guard first, and whether to stop at "committed on branch" or
  proceed to "PR open". Workers return conclusions and counts, not file dumps.
- Iterating on a worker's output? **`SendMessage` to the same worker** (context intact), don't
  respawn.

### Ops lane — ONE dedicated PR-mechanic at the ladder's cheap rung
- Keep **one long-lived ops agent** (continue it via `SendMessage` all night) for fully-specified
  mechanics where the judgment is already done: opening PRs from prepared branches with a body
  **you authored**, posting your finalized review/fix-evidence comments, polling CI and reporting
  the exact verdict + failing-job excerpts, freshening stale branches via merge-from-main,
  docs-governance `Last Updated` bumps, mass renames, dependency-bump PRs, link fixes.
- Rule: **the ops lane executes decided work; it never decides.** If an ops task turns out to need
  a judgment call, it reports back — you or an implementation worker takes over. Anything
  ambiguous never enters this lane. In particular a `TODO`/CI-log *triage sweep* is not ops work:
  triage decides what matters, so it belongs on the implementation rung or with you.
- The cheap rung is the floor of this lane, not a starting point to go below. **There is no lane
  beneath it** — the owner's standing directive rules out the cheapest model entirely, and a lane
  that needs something cheaper than the ops lane is a lane that should not exist.

### Reviewer lane — read-only, distinct lenses, never the cheap rung
- Use the `reviewer` subagent type (Read/Grep/Glob only — structurally cannot "fix" anything), at
  the model and effort the canonical skill assigns to **review**: review is judgment work, so the
  cheap rung is not eligible and the skill — not this file — sets the effort. When the global
  pipeline requests a lens, choose Taskdeck context from `taskdeck-pr-review-loop`; this file does
  not decide invocation or count. You adjudicate the returned evidence; the implementing worker
  performs pipeline-directed fixes; the ops agent posts text you finalize.

## ORCHESTRATION PATTERNS

- **Workflow tool** for structured fan-outs: dimension→verify review pipelines, parallel
  discovery sweeps, many-site migrations. Prefer `pipeline()` over barriers; use `schema` for
  structured returns; set `model`/`effort` per stage (finders cheap, verifiers stronger).
  Plain `Agent` calls for one-off delegation; a workflow only when the structure earns it.
- **Wave shape per cycle:** plan (you) → implement (implementation-lane workers, worktrees) →
  branch/PR mechanics (ops agent) → canonical review pipeline (Taskdeck lenses + your evidence
  synthesis) → pipeline-directed work → dependency-safe disposition → ledger checkpoint → pull
  `main` when applicable → next wave.
- Keep **2–4 tasks in flight**, never a reflexive fleet (`model-effort-routing` skill when
  unsure). While workers run in the background, use your foreground turn for the next wave's
  planning or adjudication — don't idle, and don't poll what will notify you.

## FRUGALITY MODE (proven overlay — survived two full runs, a model switch, and a mid-run usage-limit pause)

Engage when the usage window is tight, a reset boundary is near, or the maintainer asks for a
frugal run. It tightens the standard division of labor; everything else in this manual still
applies — the gate is never thinned to save tokens (see STOP CONDITIONS for how to degrade).

- **Coordinator turns go to the coordinator lane's never-delegated work** (wave planning, task
  selection, adjudication, the gates) **and nothing else.** No inline reading a subagent could
  summarize, no sitting through suites, no drafting mechanics a cheaper lane can execute from
  your finalized text. Required orientation and gate reads are never delegated to save
  tokens: the coordinator still reads the source-of-truth docs (base §1 — STATUS,
  OUTSTANDING_TASKS, the handoff) and every gate input itself.
- **Evidence marshals per lane (implementation rung):** one worker owns targeted verification and
  any fix or thread-settlement mechanics that the global pipeline assigns. Continue that worker
  via `SendMessage` so context is retained; the global pipeline, not this overlay, sets
  convergence. Thread settlement (`reply` + `resolveReviewThread` + reported state) belongs to
  exactly one lane per PR; the coordinator may reassign it explicitly, but never lets two lanes
  act on the same thread.
- **The ops-lane agent (the SAME single agent, not a second one) takes on an extended remit** in
  addition to its standing tasks: full-suite runs under a STOP-on-red decision rule, determinism
  reruns, and any settlement packet reassigned per the bullet
  above (reply texts in a settlement packet are always coordinator-authored — the lane posts
  finalized text, consistent with its standing remit). The lane's standing rule (executes
  decided work, never decides) is unchanged. A "packet" throughout this section = one
  `SendMessage` task assignment plus its returned report. The STOP-on-red decision rule is
  authored by the coordinator inside the task packet itself:
  the exact command, the expected-green shape, the known flakes carved out BY NAME with issue
  numbers, and "any other red → stop, report the failing names + excerpts, no reruns, no
  diagnosis".
- **Sampling is allowlisted, not default.** Spot-check one load-bearing claim per returned
  packet (re-run one count, re-read one changed hunk) ONLY for pure motion packets whose
  content feeds no review, settlement, or gate decision: branch freshening, push
  confirmations, intermediate progress polls. Suite/determinism runs and CI checks are
  mechanics to EXECUTE in the ops lane, but their returned verdicts are gate inputs: the
  coordinator reads the full returned report (exact counts, every failing job/test name,
  the excerpts) for the exact head — never a sampled slice of it (branch protection is
  lenient, so a misreported red has no other backstop). A packet that fails its spot-check
  is re-verified in full. Everything decision-bearing is verified in full, never sampled
  — in particular reviewer and settlement packets, plus the exact-head suite/CI and
  feedback-content packet sent to the global pipeline. Review classification, convergence, and
  merge disposition stay canonical. Sampling is a mechanics-lane shortcut, nothing else.
- **Flake adjudication stays evidence-priced:** follow the global check-attempt ceiling, compare
  branch and main when useful, root-cause the signal, and return the evidence without inventing a
  Fable-specific retry rule.
- **Near a usage-reset boundary, sequence reviewer fan-outs after it.** A reviewer killed by
  a limit produces an untrustworthy partial verdict — rerun it, never salvage it.

## PER-PR TASKDECK EVIDENCE HANDOFF

Run the canonical global pipeline. This overlay supplies only:

- exact head/base identity, targeted checks, exact-head CI, and full-suite results when the
  touched seam requires them; serialize full suites box-wide
- feedback content from unresolved threads, top-level comments, and review-summary bodies, without
  classifying it locally
- pipeline-assigned fix or thread-settlement mechanics through one owning lane
- dependency-safe ordering and worktree cleanup mechanics from the base manual

Reviewer invocation/count, severity, bot triggers or waits, fix/re-review convergence, and merge
disposition stay in the global pipeline.
- **Generated files**: `docs/agentic/FAILURE_LEDGER.md` is rendered from `failure_ledger.jsonl`
  by `scripts/agent_hooks/render_failure_ledger.py` (first ~160 chars of `future_fix` become
  the cell). Never hand-edit the rendered md — front-load the essential text in the jsonl and
  rerun the renderer. Assume any generated artifact works this way; check before editing.
- **Reversals are fine, in the open**: reversing an earlier adjudication on new evidence is
  correct — state it explicitly in the thread.

## BUDGET & CADENCE

- Checkpoint the ledger after every merge and cycle; update persistent memory when project
  state materially changes (a wave ships, a decision lands).
- Workers verify with targeted runs (`dotnet test --filter`, repeated-run counts, one
  project-level run; `vitest` targeted specs or `--maxWorkers=2` — full local vitest OOMs on
  this box). The coordinator OWNS the full-suite verdict at gate time (`-m:1`, ~6 min,
  600000ms tool timeout) but should delegate the sitting — a subagent runs the suite in its
  own foreground turn and returns counts + failure excerpts (consistent with the coordinator-lane
  rule above). Never two full suites concurrently, box-wide.
- **Box rules (hard-won; also see the latest handoff §7)**: background Bash tasks can be
  killed by the harness — run suites and waits FOREGROUND with explicit timeouts (600000ms is
  the clamp; chain until-loops across calls for longer waits); killed shells can orphan
  `dotnet` test hosts (Bash tool: `tasklist | grep dotnet`, `taskkill //PID <pid> //F` —
  the doubled slashes are MSYS escaping; from PowerShell use `taskkill /PID <pid> /F`). Shell cwd
  PERSISTS between Bash calls — `cd` explicitly before state-changing commands, and never
  remove a worktree from inside it. A hook appends noise rows
  (`"class": "unclassified", "surface": "Bash"`) to `failure_ledger.jsonl` on non-zero exits —
  prune them (python filter), never commit them. `git checkout -- <path>` is hook-blocked; use
  `git restore`. Deny floor: no `git rev-list`/`check-ignore`, no heredoc-into-gh, bodies with
  pipes/backticks go via the Write tool + `--body-file`, push with explicit refspec only.
- Two strikes on the same obstacle = it becomes the task (base §8). Sharpening the substrate is
  progress; grinding isn't.

## STOP CONDITIONS

Inherit base §9 — keep looping; don't stop after one task. One addition: **degrade by stopping,
not by improvising.** If you approach a hard budget/usage limit, finish or park the current
slice, write the full checkpoint + handoff (changed / verified / NOT verified / residual risk /
Q-N batch / exact resume point), and end cleanly — never keep merging in a rushed, gate-skipping
mode. An unmerged-but-reviewed PR at dawn is a success; a hastily merged one is not.
