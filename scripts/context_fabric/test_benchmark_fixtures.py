"""Tests for the Context Fabric benchmark corpus contract and scorer."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import benchmark_fixtures as sut


CORPUS = Path(__file__).resolve().parents[2] / "tests" / "fixtures" / "context_fabric" / "benchmark"
FIXTURES = CORPUS / "fixtures.json"


class CorpusContractTests(unittest.TestCase):
    def test_schema_is_a_valid_draft_2020_12_schema(self) -> None:
        from jsonschema import Draft202012Validator

        schema = sut.load_json(Path(__file__).resolve().parent / "evaluation_fixture.schema.json")
        Draft202012Validator.check_schema(schema)

    def test_checked_in_corpus_passes_and_covers_all_candidate_kinds(self) -> None:
        fixtures, byte_count, errors = sut.validate_corpus(FIXTURES)
        self.assertEqual([], errors, "\n".join(errors))
        self.assertEqual(9, len(fixtures))
        self.assertEqual(934, byte_count)
        references = [
            sut._candidate_map(
                sut.load_json(CORPUS / fixture["referenceOutputPath"]), fixture["id"]
            )
            for fixture in fixtures
        ]
        self.assertTrue(all(any(group[kind] for group in references) for kind in sut.CANDIDATE_KINDS))
        self.assertEqual(1, sum(fixture["hostileInjection"] for fixture in fixtures))

    def test_missing_license_and_hostile_marker_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "corpus").mkdir()
            (root / "references").mkdir()
            source = b"Synthetic fixture source."
            (root / "corpus" / "one.txt").write_bytes(source)
            (root / "references" / "one.json").write_text(
                json.dumps({"fixtureId": "one-001", "candidates": []}), encoding="utf-8"
            )
            fixture = {
                "id": "one-001",
                "capability": "semantic.extract",
                "sourceType": "text",
                "sourcePath": "corpus/one.txt",
                "sourceHash": hashlib.sha256(source).hexdigest(),
                "license": "synthetic",
                "referenceOutputPath": "references/one.json",
                "measurementMethod": "exact set",
                "measurementDate": "2026-09-08",
                "hostileInjection": "yes",
            }
            path = root / "fixtures.json"
            path.write_text(json.dumps([fixture]), encoding="utf-8")
            _, _, errors = sut.validate_corpus(path)
        self.assertTrue(any("hostileInjection" in error for error in errors), errors)

    def test_blank_license_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "corpus").mkdir()
            (root / "references").mkdir()
            source = b"Synthetic fixture source."
            (root / "corpus" / "one.txt").write_bytes(source)
            (root / "references" / "one.json").write_text(
                json.dumps({"fixtureId": "one-001", "candidates": []}), encoding="utf-8"
            )
            fixture = {
                "id": "one-001", "capability": "semantic.extract", "sourceType": "text",
                "sourcePath": "corpus/one.txt", "sourceHash": hashlib.sha256(source).hexdigest(),
                "license": "", "referenceOutputPath": "references/one.json",
                "measurementMethod": "exact set", "measurementDate": "2026-09-08",
                "hostileInjection": False,
            }
            path = root / "fixtures.json"
            path.write_text(json.dumps([fixture]), encoding="utf-8")
            _, _, errors = sut.validate_corpus(path)
        self.assertTrue(any("license" in error for error in errors), errors)

    def test_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "corpus").mkdir()
            (root / "references").mkdir()
            (root / "corpus" / "one.txt").write_text("original", encoding="utf-8")
            (root / "references" / "one.json").write_text(
                json.dumps({"fixtureId": "one-001", "candidates": []}), encoding="utf-8"
            )
            fixture = {
                "id": "one-001",
                "capability": "semantic.extract",
                "sourceType": "text",
                "sourcePath": "corpus/one.txt",
                "sourceHash": "0" * 64,
                "license": "synthetic",
                "referenceOutputPath": "references/one.json",
                "measurementMethod": "exact set",
                "measurementDate": "2026-09-08",
                "hostileInjection": False,
            }
            path = root / "fixtures.json"
            path.write_text(json.dumps([fixture]), encoding="utf-8")
            _, _, errors = sut.validate_corpus(path)
        self.assertTrue(any("sourceHash" in error and "actual" in error for error in errors), errors)


class ScoringTests(unittest.TestCase):
    def test_score_is_partitioned_by_candidate_kind(self) -> None:
        expected = {kind: set() for kind in sut.CANDIDATE_KINDS}
        expected["Action"].add("action:one")
        actual = {kind: set() for kind in sut.CANDIDATE_KINDS}
        actual["Action"].update(("action:one", "action:wrong"))
        actual["Decision"].add("decision:wrong-kind")
        score = sut.score_fixture("fixture", expected, actual)
        self.assertEqual(0.5, score["byKind"]["Action"]["precision"])
        self.assertEqual(1.0, score["byKind"]["Action"]["recall"])
        self.assertIsNone(score["byKind"]["Decision"]["recall"])
        self.assertEqual(1.0, score["byKind"]["Fact"]["f1"])

    def test_no_predictions_make_metrics_unavailable(self) -> None:
        report = sut.build_report(FIXTURES, None, 16 * 1024)
        self.assertEqual("unavailable", report["metricsStatus"])
        self.assertFalse(report["predictionsProvided"])
        self.assertEqual([], report["scores"])
        self.assertTrue(all(value is None for value in report["byKind"].values()))

    def test_example_predictions_report_wrong_candidates_without_self_scoring(self) -> None:
        report = sut.build_report(
            FIXTURES,
            CORPUS / "predictions.example.json",
            16 * 1024,
        )
        self.assertEqual("available", report["metricsStatus"])
        self.assertIn("does not execute a processor", report["accuracyClaim"])
        by_kind = report["byKind"]
        self.assertLess(by_kind["Action"]["precision"], 1.0)
        self.assertLess(by_kind["Question"]["recall"], 1.0)

    def test_duplicate_prediction_candidates_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "predictions.json"
            path.write_text(
                json.dumps(
                    {
                        "fixtures": [
                            {
                                "fixtureId": "fixture",
                                "candidates": [
                                    {"kind": "Action", "key": "action:one"},
                                    {"kind": "Action", "key": "action:one"},
                                ],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaises(sut.CorpusError):
                sut._load_outputs(path)

    def test_partial_predictions_fail_closed_instead_of_becoming_no_action(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "predictions.json"
            path.write_text(
                json.dumps({"fixtures": [{"fixtureId": "text-action-001", "candidates": []}]}),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(sut.CorpusError, "missing fixtureId"):
                sut.build_report(FIXTURES, path, 16 * 1024)


if __name__ == "__main__":
    unittest.main()
