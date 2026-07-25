# Dogfooding (#1271) — how we find out whether this sticks

**Last Updated: 2026-07-25**

`#1271` asks for **≥10 days of real personal use**. It is the acceptance test for the whole
revival direction (ADR-0044): at the ~8-week checkpoint (**~2026-09-04**), "no organic traction
**and** dogfooding has not stuck" is what sends the project back to the archive plan. Every other
item in the backlog is scaffolding for a product this question decides the fate of.

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
Proposals created: 20  ->  Dismissed 18 (90%) | Approved 1 (5%) | Applied 1 (5%)
Reached Apply: 1/20
```

Read honestly, that says: **dogfooding has not started.** Twelve of thirteen boards are
`DEMO:`/`Test Board`/`Browser Test` artefacts; the one plausibly-real board (`product sprint`) was
created on the last active day, three months ago. The core loop — capture → proposal → approve →
apply — completed **once**, ever.

Two things follow, and they shaped everything below.

1. **The evidence was available all along.** Nobody had to wait 8 weeks to learn this; it was
   sitting in a file. Measure early and often, not at the checkpoint.
2. **Dev traffic and real use are indistinguishable in one database.** Ninety percent of the
   proposals were dismissed, which most likely means "cleaning up test noise" rather than
   "rejecting bad proposals" — but nothing in the data can tell those apart. That contamination
   has to be designed out, or the checkpoint number will be unreadable.

---

## Design: separate the dogfooding database

Run real use against its own database so dev, demo and E2E traffic never mixes in:

```bash
# pick a path outside the repo so `clean-workspace` and E2E runs cannot touch it
export TASKDECK_DOGFOOD_DB="$HOME/taskdeck-dogfood/taskdeck.db"

# run the app against it
ConnectionStrings__DefaultConnection="Data Source=$TASKDECK_DOGFOOD_DB" \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

Everything below assumes that separation. Without it the numbers mean what they mean today: not much.

---

## The three instruments

### 1. Objective usage — `scripts/dogfooding/dogfood-snapshot.py`

```bash
python scripts/dogfooding/dogfood-snapshot.py            # uses $TASKDECK_DOGFOOD_DB
python scripts/dogfooding/dogfood-snapshot.py --markdown # paste-ready block
```

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

## Checkpoint rubric (set 2026-07-25, for ~2026-09-04)

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

**Any mixed outcome requires an explicit written assessment** (ADR-0044 already says this —
silence is not a decision).

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
