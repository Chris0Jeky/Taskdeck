#!/usr/bin/env python3
"""Objective dogfooding metrics for #1271, read straight from a Taskdeck SQLite DB.

Why this exists
---------------
#1271 (">=10 days of real personal use") is the acceptance test for the whole
revival-vs-archive decision, and it is the one item that cannot be delegated. The
failure mode it invites is arriving at the checkpoint with only a recollection.
This reads the database and reports what actually happened.

It is READ-ONLY (opens the DB with mode=ro) and reports COUNTS AND DATES ONLY --
never card text, comments, chat content, or transcripts. The database path is
printed home-relative. It is safe to paste the output into an issue.

Known limitation: days are grouped by the stored UTC date, not local time, so
activity either side of local midnight can land on the neighbouring calendar day.
That shifts which day a session counts as; it does not change whether it counts,
so day TOTALS are unaffected and only streak boundaries can move by one.

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
import urllib.parse
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

QUERY_ERRORS: list[str] = []

DEFAULT_DB_CANDIDATES = (
    "taskdeck.db",
    os.path.join("backend", "src", "Taskdeck.Api", "taskdeck.db"),
    # A published/self-contained run resolves its DB here via FirstRunBootstrapper rather than
    # into the repo, so a maintainer dogfooding the packaged exe would otherwise get
    # "No database found" while their real data sat in LOCALAPPDATA.
    os.path.join(os.environ.get("LOCALAPPDATA", ""), "Taskdeck", "taskdeck.db"),
    # scripts/dev-up.ps1 and dev-up.sh -- the documented launchers -- use a *-dev.db name.
    os.path.join(os.environ.get("LOCALAPPDATA", ""), "Taskdeck", "taskdeck-dev.db"),
    os.path.join(
        os.environ.get("XDG_DATA_HOME") or os.path.join(os.path.expanduser("~"), ".local", "share"),
        "taskdeck", "taskdeck-dev.db",
    ),
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
    # The path goes into a URI, so `?`, `#` and friends would otherwise change which file
    # SQLite opens (or silently drop the mode=ro).
    uri = "file:" + urllib.parse.quote(os.path.abspath(path).replace("\\", "/")) + "?mode=ro"
    con = sqlite3.connect(uri, uri=True)
    # Every metric must come from ONE snapshot. Without a transaction each SELECT sees its own
    # WAL state, so a proposal created mid-run can make the total disagree with its own status
    # breakdown -- internally inconsistent numbers pasted in as checkpoint evidence.
    con.isolation_level = None
    con.execute("BEGIN DEFERRED")
    return con


def redact(path: str) -> str:
    """Home-relative display path. Output is meant to be pasted into public issues, and an
    absolute path carries the OS username and often a client-specific directory name."""
    try:
        home = os.path.abspath(os.path.expanduser("~")).rstrip("\\/")
        full = os.path.abspath(path)
        rest = full[len(home):]
        # Boundary matters: with home /home/alice, the path /home/alice-client/acme/db would
        # otherwise render as "~-client/acme/db" and leak the directories it was meant to hide.
        if full.lower().startswith(home.lower()) and (rest == "" or rest[0] in "\\/"):
            return "~" + rest.replace("\\", "/")
        return os.path.basename(full)
    except Exception:
        return os.path.basename(path)


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
    """None means "no rows". A FAILED query is reported, never coerced to 0 -- a partially
    migrated or corrupt DB must not read as "no activity", which is the one wrong answer
    this tool can give."""
    try:
        row = con.execute(sql, args).fetchone()
        return row[0] if row else None
    except sqlite3.Error as exc:
        QUERY_ERRORS.append(str(exc))
        return None


def day_set(con: sqlite3.Connection, table: str, col: str) -> set[str]:
    """A MISSING table is fine (older schema). A table that exists without its expected
    timestamp column is NOT fine -- that is a schema mismatch, and swallowing it would make a
    partially migrated database report "no activity", the single most misleading verdict this
    tool can produce."""
    if not has_table(con, table):
        return set()
    if col not in columns(con, table):
        QUERY_ERRORS.append(f'{table} exists but has no "{col}" column - activity not counted')
        return set()
    try:
        return {
            r[0]
            for r in con.execute(f'select distinct substr("{col}",1,10) from "{table}"')
            if r[0]
        }
    except sqlite3.Error as exc:
        QUERY_ERRORS.append(f"{table}.{col}: {exc}")
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
        QUERY_ERRORS.append("Boards has neither Name nor Title - demo detection disabled")
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
    if args.days < 1:
        sys.exit("--days must be >= 1; a zero or negative window still counts today, which "
                 "would report a nonsensical result against the checkpoint rubric.")

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
        # A day spent only REVIEWING touches none of the CreatedAt columns, so a pure review
        # day could vanish from the count entirely. Only HUMAN-decision timestamps qualify:
        # AutomationProposals.UpdatedAt is deliberately EXCLUDED because
        # ProposalHousekeepingWorker.Expire() bumps it unattended, which would record the
        # machine ticking over as personal use -- the exact opposite of what this measures.
        ("AutomationProposals", "DecidedAt"),
        ("AutomationProposals", "AppliedAt"),
        ("Cards", "UpdatedAt"),
        ("ChatMessages", "CreatedAt"),
        # Captures (including transcript captures) persist as LlmRequests rows, so a day
        # spent only capturing would otherwise not count as an active day at all.
        ("LlmRequests", "CreatedAt"),
    ):
        days |= day_set(con, table, col)

    sorted_days = sorted(d for d in days if safe_date(d))
    last = sorted_days[-1] if sorted_days else None
    stale = (date.today() - safe_date(last)).days if last else None
    # `days - 1` so `--days 28` is a 28-day window inclusive of today, not 29.
    cutoff = (date.today() - timedelta(days=max(args.days - 1, 0))).isoformat()
    recent_days = [d for d in sorted_days if d >= cutoff]

    p(f"**Database:** `{redact(path)}`")
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
                p(f"  - {PROPOSAL_STATUS.get(status, f'status {status}')}: {n} ({100*n/total:.1f}%)")
            # Status is NOT the way to count applies. Dismiss() accepts an Applied proposal
            # (CanBeDismissed includes Applied) and OVERWRITES Status with Dismissed, so
            # filing away a completed item erases the evidence that it ever applied. AppliedAt
            # survives that transition, so it is the only reliable signal.
            applied = q1(con, "select count(*) from AutomationProposals where AppliedAt is not null") or 0
            filed = q1(
                con, "select count(*) from AutomationProposals where AppliedAt is not null and Status=6"
            ) or 0
            decided = (
                q1(con, "select count(*) from AutomationProposals where DecidedAt is not null") or 0
            )
            p("")
            p(f"**Reached Apply:** {applied}/{total} ({100*applied/total:.1f}%) "
              "-- this is the number that says whether the review-first loop pays off. "
              "Counted by `AppliedAt`, not status.")
            if filed:
                p(f"  - of those, **{filed}** were later filed away and now read as `Dismissed`. "
                  "A status-based count would have reported them as never applied.")
            p(f"**Ever decided:** {decided}/{total}")
            if "DecidedAt" in columns(con, "AutomationProposals"):
                # The LIMIT must be parity-aware. With a hardcoded `limit 2` this averages
                # the true median with the next-larger value on every ODD count, and
                # time-to-decision is heavily right-skewed, so a single proposal left over a
                # weekend drags the reported figure by orders of magnitude.
                med = q1(
                    con,
                    """select avg(j) from (
                         select (julianday(DecidedAt)-julianday(CreatedAt))*24 j
                         from AutomationProposals
                         where DecidedAt is not null
                         order by j
                         limit (
                           select 2-(count(*)%2) from AutomationProposals where DecidedAt is not null)
                         offset (
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
    # The rubric scores "active days in the window", so the verdict must use the window too --
    # an all-time count lets long-dead activity satisfy a threshold about current use.
    n = len(recent_days)
    if QUERY_ERRORS and not sorted_days:
        p("**Inconclusive.** No activity was readable, but queries failed against this database "
          "(see the warning below). Do NOT read this as \"not started\".")
    elif not sorted_days:
        p("**Not started.** No recorded activity at all.")
    elif stale is not None and stale > 30:
        p(f"**Stalled.** Last activity was {stale} days ago. Whatever the totals say, this is "
          "not sustained use, and #1271 asks for sustained use.")
    elif n < 10:
        p(f"**In progress:** {n} of the 10 active days #1271 asks for, in the last "
          f"{args.days} days ({len(sorted_days)} all time).")
    else:
        p(f"**Threshold met on volume:** {n} active days in the last {args.days}. Judge quality "
          "from the loop numbers above and the friction log, not from the day count alone.")

    if QUERY_ERRORS:
        p("")
        p(f"> :warning: **{len(QUERY_ERRORS)} query/queries failed or were skipped** against this "
          "database, so the counts above are incomplete and must not be read as low activity.")
        for e in QUERY_ERRORS[:5]:
            p(f">   - `{e[:150]}`")

    text = "\n".join(out)
    if args.markdown:
        print(f"<!-- generated {datetime.now().isoformat(timespec='seconds')} -->")
    print(text)


if __name__ == "__main__":
    main()
