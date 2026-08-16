#!/usr/bin/env python3
"""Emit a bounded, content-free timing summary from a .NET TRX file."""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as element_tree
from collections import defaultdict
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any


DEFAULT_MAX_RESULTS = 10_000
DEFAULT_MAX_IDENTITY_LENGTH = 256
MAX_OUTCOME_LENGTH = 64
_DURATION_PATTERN = re.compile(
    r"^(?P<hours>\d+):(?P<minutes>[0-5]\d):(?P<seconds>[0-5]\d)(?:\.(?P<fraction>\d{1,7}))?$"
)


def _local_name(tag: str) -> str:
    """Return an XML tag's local name, regardless of its namespace."""

    return tag.rsplit("}", 1)[-1]


def _bounded(value: str, *, label: str, limit: int) -> str:
    if len(value) > limit:
        raise ValueError(f"{label} exceeds the {limit}-character limit")
    return value


def _declaring_method_name(raw_name: str) -> str:
    """Drop adapter-rendered theory arguments from a method declaration."""

    return raw_name.split("(", 1)[0].strip()


def _duration_seconds(raw_duration: str | None) -> float | None:
    """Parse the TRX duration shape, returning None for absent/invalid values."""

    if not raw_duration:
        return None
    match = _DURATION_PATTERN.fullmatch(raw_duration.strip())
    if match is None:
        return None

    fraction = match.group("fraction") or ""
    try:
        value = (
            Decimal(match.group("hours")) * Decimal(3600)
            + Decimal(match.group("minutes")) * Decimal(60)
            + Decimal(match.group("seconds"))
            + (Decimal(f"0.{fraction}") if fraction else Decimal(0))
        )
    except InvalidOperation:
        return None
    return float(value.quantize(Decimal("0.000001")))


def _definition_map(
    root: element_tree.Element, *, identity_limit: int
) -> dict[str, tuple[str, str]]:
    definitions: dict[str, tuple[str, str]] = {}
    for unit_test in root.iter():
        if _local_name(unit_test.tag) != "UnitTest":
            continue
        test_id = unit_test.attrib.get("id")
        if not test_id:
            continue
        test_method = next(
            (child for child in unit_test.iter() if _local_name(child.tag) == "TestMethod"),
            None,
        )
        if test_method is None:
            continue
        class_name = test_method.attrib.get("className", "")
        method_name = _declaring_method_name(test_method.attrib.get("name", ""))
        if not class_name or not method_name:
            continue
        class_name = _bounded(class_name, label="class identity", limit=identity_limit)
        method_name = _bounded(method_name, label="method identity", limit=identity_limit)
        fully_qualified_name = _bounded(
            f"{class_name}.{method_name}",
            label="fully-qualified identity",
            limit=identity_limit,
        )
        if test_id in definitions and definitions[test_id] != (class_name, method_name):
            raise ValueError(f"TRX contains conflicting definitions for test id {test_id}")
        definitions[test_id] = (class_name, method_name)
    return definitions


def summarize_trx(
    trx_path: Path,
    *,
    max_results: int = DEFAULT_MAX_RESULTS,
    identity_limit: int = DEFAULT_MAX_IDENTITY_LENGTH,
) -> dict[str, Any]:
    """Parse only test definitions and result metadata into a deterministic summary."""

    if max_results < 1:
        raise ValueError("max_results must be positive")
    if identity_limit < 1:
        raise ValueError("identity_limit must be positive")

    root = element_tree.parse(trx_path).getroot()
    definitions = _definition_map(root, identity_limit=identity_limit)
    result_rows: list[dict[str, Any]] = []

    for result in root.iter():
        if _local_name(result.tag) != "UnitTestResult":
            continue
        if len(result_rows) >= max_results:
            raise ValueError(f"TRX contains more than the {max_results}-result limit")

        test_id = result.attrib.get("testId", "")
        definition = definitions.get(test_id)
        duration = _duration_seconds(result.attrib.get("duration"))
        outcome = _bounded(
            result.attrib.get("outcome", "Unknown"),
            label="outcome",
            limit=MAX_OUTCOME_LENGTH,
        )
        if definition is None:
            result_rows.append(
                {
                    "fullyQualifiedName": None,
                    "className": None,
                    "methodName": None,
                    "identityStatus": "missing-definition",
                    "outcome": outcome,
                    "durationSeconds": duration,
                }
            )
            continue

        class_name, method_name = definition
        result_rows.append(
            {
                "fullyQualifiedName": f"{class_name}.{method_name}",
                "className": class_name,
                "methodName": method_name,
                "identityStatus": "resolved",
                "outcome": outcome,
                "durationSeconds": duration,
            }
        )

    if not result_rows:
        raise ValueError(f"{trx_path} contains no UnitTestResult records")

    result_rows.sort(
        key=lambda row: (
            -(row["durationSeconds"] if row["durationSeconds"] is not None else -1),
            row["fullyQualifiedName"] or "",
            row["outcome"],
        )
    )

    class_totals: dict[str, dict[str, float | int]] = defaultdict(
        lambda: {"resultCount": 0, "timedResultCount": 0, "summedTestDurationSeconds": 0.0}
    )
    for row in result_rows:
        class_name = row["className"]
        if class_name is None:
            continue
        totals = class_totals[class_name]
        totals["resultCount"] += 1
        if row["durationSeconds"] is not None:
            totals["timedResultCount"] += 1
            totals["summedTestDurationSeconds"] += row["durationSeconds"]

    classes = [
        {
            "className": class_name,
            "resultCount": int(totals["resultCount"]),
            "timedResultCount": int(totals["timedResultCount"]),
            "summedTestDurationSeconds": round(
                float(totals["summedTestDurationSeconds"]), 6
            ),
        }
        for class_name, totals in class_totals.items()
    ]
    classes.sort(
        key=lambda row: (-row["summedTestDurationSeconds"], row["className"])
    )

    timed_durations = [
        row["durationSeconds"]
        for row in result_rows
        if row["durationSeconds"] is not None
    ]
    return {
        "schemaVersion": 1,
        "resultCount": len(result_rows),
        "timedResultCount": len(timed_durations),
        "missingDurationCount": len(result_rows) - len(timed_durations),
        "summedTestDurationSeconds": round(sum(timed_durations), 6),
        "workflowWallTimeSeconds": None,
        "classes": classes,
        "results": result_rows,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--max-results", type=int, default=DEFAULT_MAX_RESULTS)
    parser.add_argument(
        "--identity-limit", type=int, default=DEFAULT_MAX_IDENTITY_LENGTH
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        summary = summarize_trx(
            args.trx,
            max_results=args.max_results,
            identity_limit=args.identity_limit,
        )
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(summary, indent=2, sort_keys=False) + "\n", encoding="utf-8"
        )
    except (OSError, ValueError, element_tree.ParseError) as error:
        print(f"TRX timing summary failed: {error}", file=sys.stderr)
        return 1

    print(f"TRX timing summary written to {args.output}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
