"""Proving tests for ``check_contract_drafts.py``.

Run: ``py -3 -B -m unittest discover -s scripts/context_fabric -p "test_check_contract_drafts.py"``
"""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

import check_contract_drafts as sut


class ManifestPassesTests(unittest.TestCase):
    def test_default_manifest_is_green(self) -> None:
        errors, report = sut.check(sut.DEFAULT_MANIFEST)
        self.assertEqual(errors, [], "\n".join(errors))
        self.assertGreaterEqual(len(report), 7)


class SemanticRuleTests(unittest.TestCase):
    def test_route_receipt_requires_exactly_one_chosen_alternative(self) -> None:
        receipt = {
            "chosenProcessorId": "a",
            "alternatives": [
                {"processorId": "a", "eligibility": "chosen"},
                {"processorId": "b", "eligibility": "chosen"},
            ],
        }
        self.assertIn(
            "chosenProcessorId does not match exactly one chosen alternative",
            sut.semantic_route_receipt(receipt),
        )

    def test_route_receipt_rejects_duplicate_processors(self) -> None:
        receipt = {
            "chosenProcessorId": None,
            "alternatives": [
                {"processorId": "a", "eligibility": "ineligible"},
                {"processorId": "a", "eligibility": "ineligible"},
            ],
        }
        self.assertIn("duplicate processor alternatives", sut.semantic_route_receipt(receipt))

    def test_forced_rerun_requires_reason(self) -> None:
        self.assertIn(
            "forced rerun requires forcedRerunReason",
            sut.semantic_route_receipt({"forcedRerun": True, "alternatives": []}),
        )

    def test_authority_receipt_never_executes(self) -> None:
        self.assertEqual([], sut.semantic_authority_shadow_receipt({"executionPerformed": False}))
        self.assertEqual(1, len(sut.semantic_authority_shadow_receipt({"executionPerformed": True})))
        self.assertEqual(1, len(sut.semantic_authority_shadow_receipt({})))

    def test_content_free_flags_text_bearing_keys_at_any_depth(self) -> None:
        findings = sut.semantic_content_free({"metrics": [{"count": 1, "evidenceQuote": "x"}]})
        self.assertEqual(["$.metrics[0].evidenceQuote: content-bearing key in a metric document"], findings)
        self.assertEqual([], sut.semantic_content_free({"sampleSize": {"processingRuns": 3}}))

    def test_policy_snapshot_digest_is_canonical_sha256(self) -> None:
        policy = {"b": 1, "a": [1, 2]}
        good = {"policy": policy, "policyDigest": sut.canonical_digest(policy)}
        self.assertEqual([], sut.semantic_policy_snapshot(good))
        bad = {"policy": policy, "policyDigest": "sha256:" + "0" * 64}
        self.assertEqual(1, len(sut.semantic_policy_snapshot(bad)))
        # Key order must not change the digest.
        self.assertEqual(sut.canonical_digest({"a": [1, 2], "b": 1}), sut.canonical_digest(policy))


class ManifestFailureTests(unittest.TestCase):
    def test_missing_fixture_and_invalid_document_are_reported(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp)
            (base / "s").mkdir()
            (base / "f").mkdir()
            (base / "s" / "thing.schema.json").write_text(
                json.dumps(
                    {
                        "$schema": "https://json-schema.org/draft/2020-12/schema",
                        "$id": "taskdeck.test.thing.v1",
                        "type": "object",
                        "required": ["id"],
                        "properties": {"id": {"type": "string"}},
                    }
                ),
                encoding="utf-8",
            )
            (base / "f" / "bad.json").write_text(json.dumps({"id": 5}), encoding="utf-8")
            manifest = base / "contracts.manifest.json"
            manifest.write_text(
                json.dumps(
                    {
                        "contracts": [
                            {
                                "schema": "s/thing.schema.json",
                                "fixtures": ["f/bad.json", "f/missing.json"],
                                "semantic": [],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            errors, _ = sut.check(manifest)
        self.assertEqual(2, len(errors), errors)
        self.assertTrue(any("missing fixture" in error for error in errors))
        self.assertTrue(any("$.id" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
