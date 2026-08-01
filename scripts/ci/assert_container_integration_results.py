#!/usr/bin/env python3
"""Fail closed on PostgreSQL container-integration test result contracts."""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as element_tree
from pathlib import Path


POSTGRES_TEST_NAMESPACE = "Taskdeck.Integration.Tests."
NON_POSTGRES_TEST_PREFIXES = (
    "Taskdeck.Integration.Tests.Fixtures.",
    "Taskdeck.Integration.Tests.SQLiteNativeVersionTests.",
)


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def read_results(path: Path) -> list[dict[str, str]]:
    root = element_tree.parse(path).getroot()
    results: list[dict[str, str]] = []

    for element in root.iter():
        if _local_name(element.tag) != "UnitTestResult":
            continue

        message = "\n".join(text.strip() for text in element.itertext() if text.strip())
        results.append(
            {
                "name": element.attrib.get("testName", ""),
                "outcome": element.attrib.get("outcome", ""),
                "message": message,
            }
        )

    if not results:
        raise ValueError(f"{path} contains no UnitTestResult records")

    return results


def assert_positive(results: list[dict[str, str]], minimum_postgres_results: int) -> None:
    skipped = [result for result in results if result["outcome"] in {"NotExecuted", "Skipped"}]
    postgres_results = [
        result
        for result in results
        if result["name"].startswith(POSTGRES_TEST_NAMESPACE)
        and not any(
            result["name"].startswith(prefix)
            for prefix in NON_POSTGRES_TEST_PREFIXES
        )
    ]
    non_passing_postgres = [result for result in postgres_results if result["outcome"] != "Passed"]

    if skipped:
        raise ValueError(f"expected zero skipped tests, found {len(skipped)}")
    if len(postgres_results) < minimum_postgres_results:
        raise ValueError(
            "expected at least "
            f"{minimum_postgres_results} PostgreSQL test results, found {len(postgres_results)}"
        )
    if non_passing_postgres:
        names = ", ".join(result["name"] for result in non_passing_postgres)
        raise ValueError(f"PostgreSQL tests did not all pass: {names}")


def assert_negative(results: list[dict[str, str]], required_message: str) -> None:
    failures = [result for result in results if result["outcome"] == "Failed"]
    if not failures:
        raise ValueError("expected the forced Docker-unavailable control to fail")
    if not any(required_message in result["message"] for result in failures):
        raise ValueError("forced Docker-unavailable control failed for an unexpected reason")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--mode", choices=("positive", "negative"), required=True)
    parser.add_argument("--minimum-postgres-results", type=int, default=28)
    parser.add_argument("--required-message", default="Docker is required for this test run but is unavailable.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        results = read_results(args.trx)
        if args.mode == "positive":
            assert_positive(results, args.minimum_postgres_results)
        else:
            assert_negative(results, args.required_message)
    except (OSError, ValueError, element_tree.ParseError) as error:
        print(f"Container integration result contract failed: {error}", file=sys.stderr)
        return 1

    print(f"Container integration {args.mode} contract passed for {args.trx}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
