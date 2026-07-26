#!/usr/bin/env python3
"""Render docs/agentic/failure_ledger.jsonl into FAILURE_LEDGER.md table rows."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
JSONL = ROOT / "docs" / "agentic" / "failure_ledger.jsonl"
MD = ROOT / "docs" / "agentic" / "FAILURE_LEDGER.md"

HEADER = """# Agent Failure Ledger

This is the human-readable view of recurring agent, tool, test, CI, and workflow failures.
Machine-appended raw entries live in `docs/agentic/failure_ledger.jsonl`.
Rows sharing a surface and first tracking issue in `future_fix` show only their latest state here; the JSONL retains append-only history.

## Entries

| Date | Class | Surface | Failure | Workaround | Future fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
"""

FOOTER = """

## Classification

- `blocker`: work cannot safely continue.
- `non_blocking_risk`: work can continue, but verification confidence is reduced.
- `pre_existing_noise`: unrelated existing failure that should still be visible.
- `invalid_signal`: false alarm, stale check, or non-applicable warning.

## Promotion Rule

A ledger entry should become a guide or skill update only when it is reproducible, project-specific, and likely to recur.
Use `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`; do not mutate root instructions after a single ambiguous failure.
"""

TRACKING_ISSUE = re.compile(r"#\d+")


class LedgerFormatError(ValueError):
    """Raised when a nonblank JSONL line is not a JSON object."""


def cell(value: object, limit: int = 160) -> str:
    text = str(value or "").replace("\n", " ").replace("|", "\\|")
    return text[:limit] + ("..." if len(text) > limit else "")


def projection_key(entry: dict[str, object], index: int) -> tuple[str, ...]:
    tracking_issue = TRACKING_ISSUE.search(str(entry.get("future_fix", "")))
    if tracking_issue is None:
        return ("row", str(index))

    return ("tracked_failure", str(entry.get("surface", "")), tracking_issue.group(0))


def project_latest_entries(entries: list[dict[str, object]]) -> list[dict[str, object]]:
    """Hide seed metadata and keep the latest tracked surface/issue state."""
    visible_entries = [entry for entry in entries if entry.get("class") != "seed"]
    latest_indexes: dict[tuple[str, ...], int] = {}
    keys: list[tuple[str, ...]] = []

    for index, entry in enumerate(visible_entries):
        key = projection_key(entry, index)
        keys.append(key)
        latest_indexes[key] = index

    return [
        entry
        for index, (entry, key) in enumerate(zip(visible_entries, keys, strict=True))
        if latest_indexes[key] == index
    ]


def render_markdown(entries: list[dict[str, object]]) -> str:
    """Render projected ledger entries without inventing fallback history."""
    rows: list[str] = []
    for entry in project_latest_entries(entries):
        date = str(entry.get("ts", ""))[:10] or "unknown"
        rows.append(
            f"| {cell(date, 20)} | {cell(entry.get('class'), 40)} | {cell(entry.get('surface'), 80)} | "
            f"{cell(entry.get('failure'))} | {cell(entry.get('workaround'))} | {cell(entry.get('future_fix'))} | {cell(entry.get('status'), 40)} |"
        )

    return HEADER + "\n".join(rows) + FOOTER


def load_entries(path: Path) -> list[dict[str, object]]:
    """Load and validate every nonblank JSONL line before rendering."""
    try:
        content = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return []

    entries: list[dict[str, object]] = []
    for line_number, line in enumerate(content.splitlines(), start=1):
        if not line.strip():
            continue

        try:
            entry = json.loads(line)
        except json.JSONDecodeError as exc:
            raise LedgerFormatError(
                f"{path}: line {line_number}: invalid JSON: {exc.msg} at column {exc.colno}"
            ) from exc

        if not isinstance(entry, dict):
            raise LedgerFormatError(
                f"{path}: line {line_number}: expected a JSON object, got {type(entry).__name__}"
            )

        entries.append(entry)

    return entries


def main() -> int:
    try:
        entries = load_entries(JSONL)
    except (LedgerFormatError, OSError, UnicodeError) as exc:
        print(f"Failure ledger render failed: {exc}", file=sys.stderr)
        return 1

    MD.parent.mkdir(parents=True, exist_ok=True)
    MD.write_text(render_markdown(entries), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
