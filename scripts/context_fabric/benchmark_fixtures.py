#!/usr/bin/env python3
"""Validate and score the Context Fabric v0.5 synthetic benchmark corpus.

The command is local and deterministic. It validates fixture metadata, reads each source
file as bytes, verifies its SHA-256 digest, loads the referenced adjudicated output, and
optionally compares processor predictions by ``SemanticCandidateKind``. It never runs a
processor and therefore never presents reference-output comparisons as processor accuracy.

Example::

    py -3 -B scripts/context_fabric/benchmark_fixtures.py \
      tests/fixtures/context_fabric/benchmark/fixtures.json \
      --predictions tests/fixtures/context_fabric/benchmark/predictions.example.json \
      --out benchmark-report.json
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import date
from pathlib import Path
from typing import Any, Iterable

CANDIDATE_KINDS = ("Action", "Decision", "Question", "Risk", "Fact", "Reference")
_KIND_SET = frozenset(CANDIDATE_KINDS)
_SHA256 = re.compile(r"^[a-f0-9]{64}$")
_ID = re.compile(r"^[a-z0-9][a-z0-9._-]+$")
_KEY = re.compile(r"^\S.{0,199}$", re.DOTALL)
_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class CorpusError(ValueError):
    """One or more corpus contract violations."""


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CorpusError(f"{path}: cannot read JSON: {exc}") from exc


def _relative_path(base: Path, value: Any, field: str) -> Path:
    if not isinstance(value, str) or not value or value.startswith(("/", "\\")):
        raise CorpusError(f"{field}: must be a non-empty relative POSIX path")
    candidate = Path(value)
    if candidate.is_absolute() or "\\" in value or any(part == ".." for part in candidate.parts):
        raise CorpusError(f"{field}: path must stay within the corpus and use '/' separators")
    resolved = (base / candidate).resolve()
    try:
        resolved.relative_to(base.resolve())
    except ValueError as exc:
        raise CorpusError(f"{field}: path escapes corpus root") from exc
    return resolved


def _validate_candidate(candidate: Any, location: str) -> tuple[str, str]:
    if not isinstance(candidate, dict):
        raise CorpusError(f"{location}: candidate must be an object")
    kind = candidate.get("kind")
    key = candidate.get("key")
    if kind not in _KIND_SET:
        raise CorpusError(f"{location}.kind: expected one of {', '.join(CANDIDATE_KINDS)}")
    if not isinstance(key, str) or not _KEY.fullmatch(key):
        raise CorpusError(f"{location}.key: must be a non-empty string of at most 200 characters")
    return kind, key


def _candidate_map(value: Any, location: str) -> dict[str, set[str]]:
    if not isinstance(value, dict) or not isinstance(value.get("fixtureId"), str):
        raise CorpusError(f"{location}: reference/prediction must contain fixtureId")
    candidates = value.get("candidates")
    if not isinstance(candidates, list):
        raise CorpusError(f"{location}.candidates: must be an array")
    grouped: dict[str, set[str]] = {kind: set() for kind in CANDIDATE_KINDS}
    for index, candidate in enumerate(candidates):
        kind, key = _validate_candidate(candidate, f"{location}.candidates[{index}]")
        if key in grouped[kind]:
            raise CorpusError(f"{location}.candidates[{index}]: duplicate ({kind}, {key})")
        grouped[kind].add(key)
    return grouped


def _records(value: Any) -> list[dict[str, Any]]:
    if isinstance(value, list):
        records = value
    elif isinstance(value, dict) and isinstance(value.get("fixtures"), list):
        records = value["fixtures"]
    else:
        raise CorpusError("fixtures: expected an array or an object with a fixtures array")
    if not records:
        raise CorpusError("fixtures: corpus must contain at least one record")
    return records


def _validate_fixture_shape(fixture: Any, index: int) -> None:
    location = f"fixtures[{index}]"
    if not isinstance(fixture, dict):
        raise CorpusError(f"{location}: fixture must be an object")
    allowed = {
        "id", "capability", "sourceType", "sourcePath", "sourceHash", "license",
        "referenceOutputPath", "measurementMethod", "measurementDate", "hostileInjection", "notes",
    }
    unknown = sorted(set(fixture) - allowed)
    if unknown:
        raise CorpusError(f"{location}: unknown field(s): {', '.join(unknown)}")
    required = (
        "id", "capability", "sourceType", "sourcePath", "sourceHash", "license",
        "referenceOutputPath", "measurementMethod", "measurementDate", "hostileInjection",
    )
    missing = [name for name in required if name not in fixture]
    if missing:
        raise CorpusError(f"{location}: missing required field(s): {', '.join(missing)}")
    if not isinstance(fixture["id"], str) or not _ID.fullmatch(fixture["id"]):
        raise CorpusError(f"{location}.id: invalid fixture id")
    if fixture["capability"] != "semantic.extract":
        raise CorpusError(f"{location}.capability: must be semantic.extract")
    if fixture["sourceType"] not in ("text", "transcript"):
        raise CorpusError(f"{location}.sourceType: must be text or transcript")
    if not isinstance(fixture["sourceHash"], str) or not _SHA256.fullmatch(fixture["sourceHash"]):
        raise CorpusError(f"{location}.sourceHash: expected 64 lowercase SHA-256 hex characters")
    if not isinstance(fixture["license"], str) or not fixture["license"].strip():
        raise CorpusError(f"{location}.license: required and must not be blank")
    if not isinstance(fixture["measurementMethod"], str) or not fixture["measurementMethod"].strip():
        raise CorpusError(f"{location}.measurementMethod: required and must not be blank")
    if not isinstance(fixture["measurementDate"], str) or not _DATE.fullmatch(fixture["measurementDate"]):
        raise CorpusError(f"{location}.measurementDate: expected YYYY-MM-DD")
    try:
        date.fromisoformat(fixture["measurementDate"])
    except ValueError as exc:
        raise CorpusError(f"{location}.measurementDate: invalid calendar date") from exc
    if not isinstance(fixture["hostileInjection"], bool):
        raise CorpusError(f"{location}.hostileInjection: required boolean marker")


def validate_corpus(fixtures_path: Path, max_bytes: int = 16 * 1024) -> tuple[list[dict[str, Any]], int, list[str]]:
    """Validate fixture records, source hashes, reference outputs, and the byte budget."""
    if max_bytes < 1:
        raise CorpusError("max_bytes must be positive")
    root = fixtures_path.resolve().parent
    records = _records(load_json(fixtures_path))
    errors: list[str] = []
    seen_ids: set[str] = set()
    total_bytes = 0
    validated: list[dict[str, Any]] = []
    for index, fixture in enumerate(records):
        try:
            _validate_fixture_shape(fixture, index)
            fixture_id = fixture["id"]
            if fixture_id in seen_ids:
                raise CorpusError(f"fixtures[{index}].id: duplicate fixture id {fixture_id}")
            seen_ids.add(fixture_id)
            source_path = _relative_path(root, fixture["sourcePath"], f"fixtures[{index}].sourcePath")
            reference_path = _relative_path(root, fixture["referenceOutputPath"], f"fixtures[{index}].referenceOutputPath")
            source = source_path.read_bytes()
            actual_hash = hashlib.sha256(source).hexdigest()
            if actual_hash != fixture["sourceHash"]:
                raise CorpusError(
                    f"fixtures[{index}].sourceHash: expected {fixture['sourceHash']}, actual {actual_hash}"
                )
            reference = load_json(reference_path)
            if not isinstance(reference, dict) or reference.get("fixtureId") != fixture_id:
                raise CorpusError(
                    f"fixtures[{index}].referenceOutputPath: reference fixtureId must be {fixture_id}"
                )
            _candidate_map(reference, f"reference {fixture['referenceOutputPath']}")
            if not source:
                raise CorpusError(f"fixtures[{index}].sourcePath: source must not be empty")
            total_bytes += len(source)
            validated.append(fixture)
        except (CorpusError, OSError) as exc:
            errors.append(str(exc))
    if total_bytes > max_bytes:
        errors.append(f"corpus byte budget exceeded: {total_bytes} > {max_bytes}")
    return validated, total_bytes, errors


def _load_outputs(path: Path) -> dict[str, dict[str, set[str]]]:
    value = load_json(path)
    if isinstance(value, list):
        rows = value
    elif isinstance(value, dict) and isinstance(value.get("fixtures"), list):
        rows = value["fixtures"]
    else:
        raise CorpusError("predictions: expected an array or an object with a fixtures array")
    outputs: dict[str, dict[str, set[str]]] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict) or not isinstance(row.get("fixtureId"), str):
            raise CorpusError(f"predictions[{index}]: fixtureId is required")
        fixture_id = row["fixtureId"]
        if fixture_id in outputs:
            raise CorpusError(f"predictions[{index}]: duplicate fixtureId {fixture_id}")
        outputs[fixture_id] = _candidate_map(row, f"predictions[{index}]")
    return outputs


def _metric(expected: set[str], actual: set[str]) -> dict[str, float | int | None]:
    true_positive = len(expected & actual)
    expected_count = len(expected)
    actual_count = len(actual)
    precision: float | None = true_positive / actual_count if actual_count else None
    recall: float | None = true_positive / expected_count if expected_count else None
    if expected_count == 0 and actual_count == 0:
        precision = recall = 1.0
    f1 = None if precision is None or recall is None else (
        0.0 if precision + recall == 0 else 2 * precision * recall / (precision + recall)
    )
    return {
        "truePositive": true_positive,
        "expected": expected_count,
        "actual": actual_count,
        "precision": precision,
        "recall": recall,
        "f1": f1,
    }


def score_fixture(fixture_id: str, expected: dict[str, set[str]], actual: dict[str, set[str]]) -> dict[str, Any]:
    return {
        "fixtureId": fixture_id,
        "byKind": {kind: _metric(expected[kind], actual[kind]) for kind in CANDIDATE_KINDS},
    }


def _aggregate(scores: Iterable[dict[str, Any]]) -> dict[str, dict[str, float | int | None]]:
    totals = {kind: {"truePositive": 0, "expected": 0, "actual": 0} for kind in CANDIDATE_KINDS}
    for score in scores:
        for kind in CANDIDATE_KINDS:
            for field in totals[kind]:
                totals[kind][field] += score["byKind"][kind][field]
    result: dict[str, dict[str, float | int | None]] = {}
    for kind in CANDIDATE_KINDS:
        total = totals[kind]
        tp = total["truePositive"]
        precision = tp / total["actual"] if total["actual"] else None
        recall = tp / total["expected"] if total["expected"] else None
        if total["expected"] == 0 and total["actual"] == 0:
            precision = recall = 1.0
        f1 = None if precision is None or recall is None else (
            0.0 if precision + recall == 0 else 2 * precision * recall / (precision + recall)
        )
        result[kind] = {**total, "precision": precision, "recall": recall, "f1": f1}
    return result


def build_report(fixtures_path: Path, predictions_path: Path | None, max_bytes: int) -> dict[str, Any]:
    fixtures, total_bytes, errors = validate_corpus(fixtures_path, max_bytes)
    if errors:
        raise CorpusError("\n".join(errors))
    root = fixtures_path.resolve().parent
    predictions: dict[str, dict[str, set[str]]] = {}
    if predictions_path is not None:
        predictions = _load_outputs(predictions_path.resolve())
        fixture_ids = {fixture["id"] for fixture in fixtures}
        unknown_predictions = sorted(set(predictions) - fixture_ids)
        if unknown_predictions:
            raise CorpusError(
                "predictions contain unknown fixtureId(s): " + ", ".join(unknown_predictions)
            )
        missing_predictions = sorted(fixture_ids - set(predictions))
        if missing_predictions:
            raise CorpusError(
                "predictions are missing fixtureId(s): " + ", ".join(missing_predictions)
            )
    fixture_scores: list[dict[str, Any]] = []
    for fixture in sorted(fixtures, key=lambda item: item["id"]):
        reference_path = _relative_path(root, fixture["referenceOutputPath"], fixture["referenceOutputPath"])
        expected = _candidate_map(load_json(reference_path), f"reference {fixture['referenceOutputPath']}")
        actual = predictions.get(fixture["id"], {kind: set() for kind in CANDIDATE_KINDS})
        fixture_scores.append(score_fixture(fixture["id"], expected, actual))
    report: dict[str, Any] = {
        "contract": "context-fabric-evaluation-fixture.v1",
        "fixtureCount": len(fixtures),
        "sourceBytes": total_bytes,
        "byteBudget": max_bytes,
        "candidateKinds": list(CANDIDATE_KINDS),
        "predictionsProvided": predictions_path is not None,
        "metricsStatus": "available" if predictions_path is not None else "unavailable",
        "accuracyClaim": "none; this command compares supplied predictions with adjudicated references and does not execute a processor",
        "scores": fixture_scores if predictions_path is not None else [],
        "byKind": _aggregate(fixture_scores) if predictions_path is not None else {kind: None for kind in CANDIDATE_KINDS},
    }
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("--predictions", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--max-bytes", type=int, default=16 * 1024)
    args = parser.parse_args(argv)
    try:
        report = build_report(args.fixtures, args.predictions, args.max_bytes)
    except CorpusError as exc:
        print(f"benchmark corpus: FAIL\n{exc}", file=sys.stderr)
        return 1
    rendered = json.dumps(report, indent=2, sort_keys=False) + "\n"
    if args.out:
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    status = report["metricsStatus"]
    print(f"benchmark corpus: PASS ({report['fixtureCount']} fixtures, {report['sourceBytes']} bytes; metrics {status})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
