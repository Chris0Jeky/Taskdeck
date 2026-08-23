# Dogfooding log (#1271)

Append-only. Newest at the top. Protocol and checkpoint rubric: [`README.md`](README.md).

**Two rules.** Write friction **at the moment**, not in retrospect — a retrospective log records what
you remember being annoyed by, which is a different and much less useful dataset. And **log the
misses**: a week you did not use it is data, and quietly skipping the entry is how the record
becomes flattering.

Format per entry:

```
## YYYY-MM-DD (week N)
<paste `dogfood-snapshot.py --markdown` output>

**Did:** what you actually used it for.
**Friction:** what got in the way. One line each.
**Nearly-used:** times you thought about it and reached for something else instead, and why.
**Would-have-lost:** captures that would otherwise have evaporated.
**Fixed:** at most one thing (see the friction budget).
```

---

## 2026-07-25 — baseline, before dogfooding starts

Not a dogfooding entry. This is the **pre-measurement**, taken so the checkpoint has something to
compare against, and so the starting point is on the record rather than reconstructed later.

Read from the dev database (`backend/src/Taskdeck.Api/taskdeck.db`), because no dedicated
dogfooding database exists yet:

```
Active days (all time):      8      | target for #1271 is >=10
Active days (last 28):       0
Longest consecutive streak:  5 day(s)
First activity:              2026-03-27
Last activity:               2026-04-23   (93 days ago)

Boards: 13 total, 10 demo/test residue, 3 plausibly real

Proposals created: 20
  status:  Dismissed 18  |  Approved 1  |  Applied 1
Reached Apply: 17/20 (85%)   <- counted by AppliedAt
  - of those, 16 were later filed away and now read as "Dismissed"
Ever decided: 19/20
```

**Read:** use has **not been sustained** — but the loop itself worked: 17 of 20 proposals reached
Apply. The classifier flags 10 of 13 boards as `DEMO:` / `Test Board` / `Browser Test` residue; of
the three it passes, two (`onboarding`, `calendar`) are lowercase single-word boards created minutes
apart alongside the seeded set and are near-certainly residue the case-sensitive prefix matcher
misses, leaving one plausibly-real board (`product sprint`), created on the last active day three
months ago.

**What this baseline is worth:** most of those 17 applies happened on `DEMO:` boards, so they
measure the *engine* working rather than a person choosing to use it — and **nothing in the data
separates those two**, which is the point. Dev traffic and real use are indistinguishable in a
shared database, so the first structural decision is a separate `TASKDECK_DOGFOOD_DB` (see README).
Treat every number above as the *dev* baseline, not a dogfooding one.

**Correction, same day:** the first version of this entry read the funnel off `Status` and reported
"1/20 reached Apply — the core loop completed once, ever". That was wrong by 17×. `Dismiss()`
overwrites an `Applied` status, so filing a finished item away erases the evidence it applied;
`AppliedAt` survives and is the correct signal. Caught by a Codex P1 on PR #1478.

**Notably:** none of this required waiting for the checkpoint. It was sitting in a local file the
whole time.

---

<!-- New entries go directly below this line, newest first. -->

## 2026-08-23 — sprint formally started (agent-recorded)

Maintainer declaration (guided walkthrough 2026-08-23, q-2 = A): **the ≥10-day dogfooding sprint
started 2026-08-22** on the v0.1.1 build. Day 1 was not quiet — it produced the workflow/UX-UI
findings now driving the v0.1.2 scope (#1876 + the Priority I tranche; see the v0.1.2 milestone).
The ADR-0044 checkpoint is re-anchored to ≥10 days from this start (no earlier than 2026-09-01),
and the sprint extends to a collaborator once the collaboration surface works, or once the product
feels good and is releasable to macOS (q-8 = A, recorded on #1271). Maintainer-authored entries in
the protocol format follow below/above this record as the days accrue.
