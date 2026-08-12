# Dogfooding (#1271) — how we find out whether this sticks

**Last Updated: 2026-07-25**

`#1271` asks for **≥10 days of real personal use**. It is the acceptance test for the whole
revival direction (ADR-0044): at the checkpoint, "no organic traction **and** dogfooding has not
stuck" is what sends the project back to the archive plan. Every other item in the backlog is
scaffolding for a product this question decides the fate of.

**When does the checkpoint clock start? The canonical docs disagree, and it changes the answer.**

- `docs/REVIVAL_PLAN.md:149` — *"Checkpoint (~8 weeks from **Phase 0**)"*, and `:49` says "from start".
- `docs/decisions/ADR-0044` Decision 6 — *"after Phase 2 ships and the **beta launches** (~8 weeks at demonstrated velocity)"*.

Dogfooding **is** Phase 0, so these are weeks apart: the ADR reading postpones the evidence review
by the entire build-and-launch period. **This guide follows `REVIVAL_PLAN.md`**, which `CLAUDE.md`
names the active planning spine — so the clock starts when dogfooding starts, i.e. now.

The conflict itself should be resolved in the canonical docs rather than papered over here; it is
flagged so the next reader does not silently pick the other one.

It is also the one item that cannot be delegated, which is exactly why it needs structure. The
failure mode is not "dogfooding goes badly" — a bad result is a *useful* result. The failure mode
is **arriving at the checkpoint with nothing but a recollection**, and having to guess.

---

## The baseline, measured 2026-07-25

Before writing any of this, we read the local database. It is worth stating plainly:

```
Active days (all time):      8      | target is >=10
Active days (last 28):       0
Longest streak:              5 days
First activity:              2026-03-27
Last activity:               2026-04-23   (93 days ago)

Boards: 13 total, 10 demo/test residue, 3 plausibly real
Proposals created: 20  ->  status: Dismissed 18 | Approved 1 | Applied 1
Reached Apply: 17/20 (85%)   <- 16 of them later filed away, so status reads "Dismissed"
```

Read honestly, that says: **use has not been sustained.** Note carefully what it does *not* say —
the loop itself worked. **17 of 20 proposals reached Apply**, which is a healthy funnel, not a
broken one.

> **Counting applies by status is wrong, and this is the trap.** `Dismiss()` accepts an already-
> `Applied` proposal (`CanBeDismissed` includes `Applied`) and **overwrites** `Status` with
> `Dismissed` — so filing away a finished item erases the evidence that it ever applied. A
> status-based count reports **1/20**; `AppliedAt`, which survives the transition, reports
> **17/20**. The first draft of this document made exactly that error and concluded "the core loop
> completed once, ever". It didn't. If you take one thing from this file, take this: **count applies
> by `AppliedAt`.**

What has genuinely not happened is *sustained* use: 8 active days, none in the last 28, last
activity three months ago, and most of the boards are fixtures.

On the boards: the classifier flags **10 of 13** as `DEMO:`/`Test Board`/`Browser Test` residue.
Of the three it passes, two (`onboarding`, `calendar`) are lowercase single-word boards created
eleven minutes apart alongside the seeded set, and are near-certainly residue the matcher misses —
`NOISE_BOARD_PREFIXES` is case-sensitive and matches only at the start of the name, so it
**under-counts**. That leaves one plausibly-real board, `product sprint`, created on the last
active day, three months ago. The tool's number is the conservative one and the one to quote;
this paragraph is why it should be read as a floor, not a measurement.

Two things follow, and they shaped everything below.

1. **The evidence was available all along.** Nobody had to wait 8 weeks to learn this; it was
   sitting in a file. Measure early and often, not at the checkpoint.
2. **Dev traffic and real use are indistinguishable in one database.** Most of those applies
   happened on `DEMO:` boards, so they measure the *engine* working, not a person choosing to use
   it — and nothing in the data separates the two. That contamination has to be designed out, or
   the checkpoint number will be unreadable.
3. **The measurement can be wrong in the flattering direction too.** The status-based apply count
   understated the funnel by 17×. Instruments need the same adversarial reading as the code.

---

## Design: separate the dogfooding database

Run real use against its own database so dev, demo and E2E traffic never mixes in:

PowerShell (the primary shell on this machine):

```powershell
# a path outside the repo, so `clean-workspace` and E2E runs cannot touch it
$env:TASKDECK_DOGFOOD_DB = "$env:USERPROFILE\taskdeck-dogfood\taskdeck.db"
New-Item -ItemType Directory -Force (Split-Path $env:TASKDECK_DOGFOOD_DB) | Out-Null

$env:ConnectionStrings__DefaultConnection = "Data Source=$env:TASKDECK_DOGFOOD_DB"
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

**That starts the API only.** From a source checkout the API project has no built `wwwroot`, so
you also need the frontend in a second terminal — otherwise there is nothing to dogfood *with*:

```bash
cd frontend/taskdeck-web && npm run dev     # http://localhost:5173
```

**Use a Windows-style path.** Under Git Bash `$HOME` expands to `/c/Users/<you>`, which .NET does
not resolve — the app silently falls back to a relative `taskdeck.db` beside the working directory,
which is exactly the contamination this separation exists to avoid. If you do use bash, spell the
path out (`/c/Users/<you>/...` will not work; `C:/Users/<you>/...` will):

```bash
export TASKDECK_DOGFOOD_DB="C:/Users/<you>/taskdeck-dogfood/taskdeck.db"
mkdir -p "$(dirname "$TASKDECK_DOGFOOD_DB")"   # SQLite creates the file, not the directory

# the snapshot script reads TASKDECK_DOGFOOD_DB; the API does NOT -- it needs its own key
export ConnectionStrings__DefaultConnection="Data Source=$TASKDECK_DOGFOOD_DB"
```

Everything below assumes that separation. Without it the numbers mean what they mean today: not much.

---

## The three instruments

### 1. Objective usage — `scripts/dogfooding/dogfood-snapshot.py`

```bash
# ALWAYS pass --db explicitly, or persist TASKDECK_DOGFOOD_DB for your user.
# The export above is session-scoped: run this from a fresh terminal a week later and
# find_db() falls back to a repo-local dev database, silently re-introducing exactly the
# fixture contamination this protocol exists to prevent -- and the output would look fine.
python scripts/dogfooding/dogfood-snapshot.py --db "$TASKDECK_DOGFOOD_DB"
python scripts/dogfooding/dogfood-snapshot.py --db "$TASKDECK_DOGFOOD_DB" --markdown
```

To persist it once (PowerShell), so the bare command is safe from any shell:

```powershell
[Environment]::SetEnvironmentVariable("TASKDECK_DOGFOOD_DB", "$env:USERPROFILE\taskdeck-dogfood\taskdeck.db", "User")
```

**Check the `Database:` line in the output every time.** It is the first line for exactly this
reason — if it does not name your dogfooding database, the numbers below it are measuring
development.

Read-only (`mode=ro`), and reports **counts and dates only** — never card text, comments, chat
content or transcripts — so its output is safe to paste into an issue. It reports active days,
streaks, days-since-last-use, the full proposal funnel, median time-to-decision, and a demo-
contamination warning when the boards look like test residue.

This is the half that cannot flatter itself. Run it weekly.

### 2. Subjective friction — [`LOG.md`](LOG.md)

The numbers say *whether* it stuck. Only you can say *why*. One line per friction moment, written
**at the moment** — retrospective friction logs record what you remember being annoyed by, which is
a different and much less useful dataset.

### 3. The checkpoint rubric — decided **now**, below

Thresholds are set before the data exists. Deciding what counts as success after seeing the
numbers is how a project talks itself into continuing.

---

## Checkpoint rubric (set 2026-07-25)

> Set *before any dogfooding data exists* — which is the point — but **not** in ignorance of the
> dev baseline above. The 8 active days, 5-day streak and 85% apply rate were already measured when
> these thresholds were written. They are calibrated against a *dev* database and a *fresh*
> `TASKDECK_DOGFOOD_DB`, which will start empty; the honesty claim is that the bars were fixed
> before any real usage data existed to tune them against, not that they were set blind.

| signal | archive | ambiguous | revive |
|---|---|---|---|
| Active days in the window | < 10 | 10–20 | > 20 |
| Longest streak | ≤ 2 days | 3–6 days | ≥ 7 days |
| Days since last use, at checkpoint | > 14 | 4–14 | ≤ 3 |
| Proposals reaching Apply | < 10% | 10–40% | > 40% |
| Captures you'd otherwise have lost | ~0 | a few | routinely |
| Would you notice if it vanished? | no | shrug | yes |

**The tie-breaker is the last row.** Every other line can be gamed by deciding to use it; that one
cannot. If the honest answer at the checkpoint is "no", the numbers do not matter.

**Any mixed outcome requires an explicit written assessment** — `docs/REVIVAL_PLAN.md` §149 says so
in as many words ("Any mixed outcome requires an explicit maintainer assessment and plan amendment
rather than an automatic archive decision"), mirrored in the masterplan's Checkpoint step and
`OUTSTANDING_TASKS.md` §F. Silence is not a decision.

---

## Ideas worth trying, roughly by value per unit of effort

Not a plan — a menu. The point of dogfooding is to find out what is true, and picking only the
comfortable experiments defeats it.

**Make capture cost nothing.** The product's own thesis is near-zero-friction capture; if capturing
into Taskdeck is slower than a scratch file, that is a finding, and the most important one available.
Try a global hotkey, a CLI one-liner aliased to something short, or the MCP server so capture happens
from wherever you already are.

**Feed it the thing it was built for.** It is positioned as a transcript → action-item engine. Run
real WhisperX transcripts of real meetings through it. If the proposals are not worth reviewing,
that is the product thesis failing its own test — far better to learn in August than after launch.

**Dogfood the backlog with it.** `OUTSTANDING_TASKS.md` and this walkthrough are exactly the workload
Taskdeck claims to serve. Running them through it is both real use and a direct comparison against
the incumbent (a markdown file), which is the honest competitor.

**Record the near-misses.** The highest-signal moment is when you *thought about* using it and
didn't. One line — "wanted to capture X, used Obsidian instead because Y" — is worth more than a
week of dutiful use, because it names the actual gap.

**Keep a "would have lost this" tally.** Count captures that would otherwise have evaporated. This
is the closest thing to a direct measure of whether the product delivers its promise.

**Deliberately try to abandon it for a week.** Around week 5, stop using it on purpose. If you drift
back, that is the strongest possible signal. If you do not notice, that is the answer.

**Timebox a friction fix budget.** Cap yourself at (say) 2 hours/week fixing what dogfooding
surfaces. Unbounded, dogfooding degenerates into development — which is precisely how the current
database ended up 90% demo boards.

---

## Weekly loop

1. `python scripts/dogfooding/dogfood-snapshot.py --markdown`
2. Paste it into `LOG.md` under a dated heading.
3. Add 2–3 sentences: what you actually did with it, and what got in the way.
4. Fix at most one friction item (see the budget above).

Ten minutes. If a week is missed, **log the miss** — a gap is data, and quietly skipping it is how
the record becomes flattering.
