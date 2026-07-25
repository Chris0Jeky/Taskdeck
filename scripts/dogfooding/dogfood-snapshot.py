#!/usr/bin/env python3
"""Objective dogfooding metrics for #1271, read straight from a Taskdeck SQLite DB.

Why this exists
---------------
#1271 (">=10 days of real personal use") is the acceptance test for the whole
revival-vs-archive decision, and it is the one item that cannot be delegated. The
failure mode it invites is arriving at the checkpoint with only a recollection.
This reads the database and reports what actually happened.

It is READ-ONLY (opens the DB with mode=ro) and reports COUNTS AND DATES ONLY --
never card text, comments, chat content, or transcripts. It is safe to paste the
output into an issue.

The demo problem
----------------
A dev-run database is full of E2E fixtures, seeded demo boards and test accounts.
Mixed together with real use, the numbers mean nothing. This script separates the
two and reports both, so you can see the contamination rather than average it away.
Genuine dogfooding is best done against its own database -- see
docs/dogfooding/README.md.

Usage
-----
    python scripts/dogfooding/dogfood-snapshot.py [--db PATH] [--markdown]

    --db        database to read (default: $TASKDECK_DOGFOOD_DB, else the first
                taskdeck.db found in the usual spots)
    --markdown  emit a block ready to paste into docs/dogfooding/LOG.md
    --days N    window for the "recent" section (default 28)
"""

from __future__ import annotations

import argparse
import os
import sqlite3
import sys
from datetime import date, datetime, timedelta

# Boards/users matching these are treated as demo, test or E2E residue rather than
# real use. Deliberately broad: over-excluding understates dogfooding, which is the
# safe direction for an acceptance test.
NOISE_BOARD_PREFIXES = ("DEMO:", "Test Board", "Browser Test", "E2E", "Playwright", "Scenario:")
NOISE_USER_PREFIXES = ("demo", "test", "e2e", "playwright", "collab", "seed")

PROPOSAL_STATUS = {
    0: "PendingReview",
    1: "Approved",
    2: "Rejected",
    3: "Applied",
    4: "Failed",
    5: "Expired",
    6: "Dismissed",
}

DEFAULT_DB_CANDIDATES = (
    "taskdeck.db",
    os.path.join("backend", "src", "Taskdeck.Api", "taskdeck.db"),
)


def find_db(explicit: str | None) -> str:
    if explicit:
        return explicit
    env = os.environ.get("TASKDECK_DOGFOOD_DB")
    if env:
        return env
    for c in DEFAULT_DB_CANDIDATES:
        if os.path.exists(c):
            return c
    sys.exit(
        "No database found. Pass --db PATH or set TASKDECK_DOGFOOD_DB.\n"
        "If you have not run Taskdeck yet, that is itself the finding."
    )


def connect(path: str) -> sqlite3.Connection:
    if not os.path.exists(path):
        sys.exit(f"No such database: {path}")
    return sqlite3.connect(f"file:{path}?mode=ro", uri=True)


def has_table(con: sqlite3.Connection, name: str) -> bool:
    return (
        con.execute(
            "select 1 from sqlite_master where type='table' and name=?", (name,)
        ).fetchone()
        is not None
    )


def columns(con: sqlite3.Connection, table: str) -> set[str]:
    return {r[1] for r in con.execute(f'pragma table_info("{table}")')}


def q1(con: sqlite3.Connection, sql: str, args: tuple = ()):  # scalar or None
    try:
        row = con.execute(sql, args).fetchone()
        return row[0] if row else None
    except sqlite3.Error:
        return None


def day_set(con: sqlite3.Connection, table: str, col: str) -> set[str]:
    if not has_table(con, table) or col not in columns(con, table):
        return set()
    try:
        return {
            r[0]
            for r in con.execute(f'select distinct substr("{col}",1,10) from "{table}"')
            if r[0]
        }
    except sqlite3.Error:
        return set()


def longest_streak(days: set[str]) -> int:
    if not days:
        return 0
    parsed = sorted({d for d in (safe_date(x) for x in days) if d})
    best = run = 1
    for prev, cur in zip(parsed, parsed[1:]):
        run = run + 1 if cur - prev == timedelta(days=1) else 1
        best = max(best, run)
    return best


def safe_date(s: str):
    try:
        return date.fromisoformat(s)
    except (ValueError, TypeError):
        return None


def noise_board_ids(con: sqlite3.Connection) -> set[str]:
    if not has_table(con, "Boards"):
        return set()
    cols = columns(con, "Boards")
    namecol = "Name" if "Name" in cols else ("Title" if "Title" in cols else None)
    if not namecol:
        return set()
    ids = set()
    for bid, name in con.execute(f'select Id,"{namecol}" from Boards'):
        if name and any(str(name).startswith(p) for p in NOISE_BOARD_PREFIXES):
            ids.add(bid)
    return ids


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--db")
    ap.add_argument("--markdown", action="store_true")
    ap.add_argument("--days", type=int, default=28)
    args = ap.parse_args()

    path = find_db(args.db)
    con = connect(path)
    out: list[str] = []
    p = out.append

    # ---- activity ---------------------------------------------------------
    days: set[str] = set()
    for table, col in (
        ("AuditLogs", "Timestamp"),
        ("Cards", "CreatedAt"),
        ("AutomationProposals", "CreatedAt"),
        ("ChatMessages", "CreatedAt"),
    ):
        days |= day_set(con, table, col)

    sorted_days = sorted(d for d in days if safe_date(d))
    last = sorted_days[-1] if sorted_days else None
    stale = (date.today() - safe_date(last)).days if last else None
    cutoff = (date.today() - timedelta(days=args.days)).isoformat()
    recent_days = [d for d in sorted_days if d >= cutoff]

    p(f"**Database:** `{path}`")
    p(f"**Active days (all time):** {len(sorted_days)}  |  target for #1271 is **>=10**")
    p(f"**Active days (last {args.days}):** {len(recent_days)}")
    p(f"**Longest consecutive streak:** {longest_streak(days)} day(s)")
    p(f"**First activity:** {sorted_days[0] if sorted_days else 'n/a'}")
    p(f"**Last activity:** {last or 'n/a'}" + (f"  ({stale} days ago)" if stale is not None else ""))

    # ---- demo contamination ----------------------------------------------
    noise = noise_board_ids(con)
    total_boards = q1(con, "select count(*) from Boards") or 0
    p("")
    p(f"**Boards:** {total_boards} total, {len(noise)} look like demo/test residue, "
      f"**{total_boards - len(noise)} plausibly real**")
    if noise and total_boards and len(noise) / total_boards > 0.5:
        p("")
        p("> :warning: More than half the boards are demo/test artefacts. These numbers are "
          "measuring development, not usage. Run dogfooding against its own database "
          "(`TASKDECK_DOGFOOD_DB`) so the signal is readable.")

    # ---- the review loop --------------------------------------------------
    p("")
    p("### The core loop")
    if has_table(con, "AutomationProposals"):
        total = q1(con, "select count(*) from AutomationProposals") or 0
        p(f"Proposals created: **{total}**")
        if total:
            rows = list(
                con.execute(
                    "select Status,count(*) from AutomationProposals group by Status order by 2 desc"
                )
            )
            for status, n in rows:
                p(f"  - {PROPOSAL_STATUS.get(status, f'status {status}')}: {n} ({100*n//total}%)")
            applied = q1(con, "select count(*) from AutomationProposals where Status=3") or 0
            decided = (
                q1(con, "select count(*) from AutomationProposals where DecidedAt is not null") or 0
            )
            p("")
            p(f"**Reached Apply:** {applied}/{total} ({100*applied//total if total else 0}%) "
              "-- this is the number that says whether the review-first loop pays off.")
            p(f"**Ever decided:** {decided}/{total}")
            if "DecidedAt" in columns(con, "AutomationProposals"):
                med = q1(
                    con,
                    """select avg(j) from (
                         select (julianday(DecidedAt)-julianday(CreatedAt))*24 j
                         from AutomationProposals
                         where DecidedAt is not null
                         order by j limit 2 offset (
                           select (count(*)-1)/2 from AutomationProposals where DecidedAt is not null))""",
                )
                if med is not None:
                    p(f"**Median time to decision:** {med:.1f} hours")
    else:
        p("_No AutomationProposals table — this database predates the proposal engine._")

    # ---- volume -----------------------------------------------------------
    p("")
    p("### Volume")
    for label, table in (("Cards", "Cards"), ("Comments", "CardComments"),
                         ("Chat messages", "ChatMessages"), ("LLM requests", "LlmRequests"),
                         ("Artefacts", "SourceArtefacts")):
        if has_table(con, table):
            p(f"  - {label}: {q1(con, f'select count(*) from \"{table}\"') or 0}")

    # ---- verdict ----------------------------------------------------------
    p("")
    p("### Read")
    n = len(sorted_days)
    if n == 0:
        p("**Not started.** No recorded activity at all.")
    elif stale is not None and stale > 30:
        p(f"**Stalled.** Last activity was {stale} days ago. Whatever the totals say, this is "
          "not sustained use, and #1271 asks for sustained use.")
    elif n < 10:
        p(f"**In progress:** {n} of the 10 active days #1271 asks for.")
    else:
        p(f"**Threshold met on volume:** {n} active days. Judge quality from the loop numbers "
          "above and the friction log, not from the day count alone.")

    text = "\n".join(out)
    if args.markdown:
        print(f"<!-- generated {datetime.now().isoformat(timespec='seconds')} -->")
    print(text)


if __name__ == "__main__":
    main()
