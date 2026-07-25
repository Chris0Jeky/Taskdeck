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
  - Dismissed: 18 (90%)
  - Approved:   1  (5%)
  - Applied:    1  (5%)
Reached Apply: 1/20
Ever decided: 19/20
```

**Read:** dogfooding has **not started**. The core loop — capture → proposal → approve → apply —
has completed **once, ever**. The classifier flags 10 of 13 boards as `DEMO:` / `Test Board` /
`Browser Test` residue; of the three it passes, two (`onboarding`, `calendar`) are lowercase
single-word boards created minutes apart alongside the seeded set and are near-certainly residue
the case-sensitive prefix matcher misses, leaving one plausibly-real board (`product sprint`),
created on the last active day three months ago.

**What this baseline is worth:** the 90% dismissal rate is almost certainly test-noise cleanup
rather than genuine rejection, but **nothing in the data can distinguish those two**, and that is
the point. Dev traffic and real use are indistinguishable in a shared database, so the first
structural decision is a separate `TASKDECK_DOGFOOD_DB` (see README). Treat every number above as
the *dev* baseline, not a dogfooding one.

**Notably:** none of this required waiting for the checkpoint. It was sitting in a local file the
whole time.

---

<!-- New entries go directly below this line, newest first. -->
