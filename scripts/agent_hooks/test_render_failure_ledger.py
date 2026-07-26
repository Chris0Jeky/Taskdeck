import io
import json
import tempfile
import unittest
from collections import Counter
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch

import render_failure_ledger as ledger
from render_failure_ledger import load_entries, project_latest_entries, render_markdown


class FailureLedgerProjectionTests(unittest.TestCase):
    def test_latest_state_supersedes_same_surface_and_tracking_issue(self) -> None:
        entries = [
            {
                "surface": "ci/extended-workflow",
                "future_fix": "#1330: repair workflow permissions",
                "status": "open",
            },
            {
                "surface": "ci/extended-workflow",
                "future_fix": "Resolved by 66382e6c; #1330 closed",
                "status": "resolved",
            },
        ]

        projected = project_latest_entries(entries)

        self.assertEqual([entry["status"] for entry in projected], ["resolved"])

    def test_same_surface_with_different_tracking_issues_remains_visible(self) -> None:
        entries = [
            {
                "surface": "ci/extended-workflow",
                "future_fix": "#1330: repair workflow permissions",
                "status": "resolved",
            },
            {
                "surface": "ci/extended-workflow",
                "future_fix": "#1400: repair a separate workflow fault",
                "status": "open",
            },
        ]

        projected = project_latest_entries(entries)

        self.assertEqual(projected, entries)

    def test_rows_without_tracking_issues_remain_unique(self) -> None:
        entries = [
            {
                "class": "invalid_signal",
                "surface": "ci/e2e-smoke",
                "future_fix": "Stabilize the archive-to-restore seed",
                "status": "open",
            },
            {
                "class": "invalid_signal",
                "surface": "ci/e2e-smoke",
                "future_fix": "Stabilize the archive-to-restore seed",
                "status": "resolved",
            },
        ]

        projected = project_latest_entries(entries)

        self.assertEqual(projected, entries)

    def test_seed_only_input_renders_no_failure_rows(self) -> None:
        entries = [
            {
                "ts": "2026-05-11T00:00:00Z",
                "class": "seed",
                "surface": "agentic-pack",
                "failure": "Ledger created",
                "status": "open",
            }
        ]

        rendered = render_markdown(entries)
        data_rows = rendered.split("| --- | --- | --- | --- | --- | --- | --- |", 1)[1]

        self.assertNotIn("| seed |", rendered)
        self.assertFalse(any(line.startswith("| ") for line in data_rows.splitlines()))

    def test_mixed_input_renders_issue_less_row_but_not_seed(self) -> None:
        entries = [
            {
                "ts": "2026-05-11T00:00:00Z",
                "class": "seed",
                "surface": "agentic-pack",
                "failure": "Ledger created",
                "status": "open",
            },
            {
                "ts": "2026-07-25T00:00:00Z",
                "class": "invalid_signal",
                "surface": "ci/e2e-smoke",
                "failure": "Archive-to-restore seed was transiently unavailable",
                "workaround": "Rerun after seed creation",
                "future_fix": "Stabilize the archive-to-restore seed",
                "status": "open",
            },
        ]

        rendered = render_markdown(entries)

        self.assertNotIn("| seed |", rendered)
        self.assertIn("| invalid_signal | ci/e2e-smoke |", rendered)

    def test_checked_in_markdown_matches_jsonl_projection(self) -> None:
        entries = load_entries(ledger.JSONL)

        self.assertEqual(ledger.MD.read_text(encoding="utf-8"), render_markdown(entries))

    def test_reconciled_targets_project_exact_latest_state(self) -> None:
        entries = [
            {
                "class": "seed",
                "surface": "agentic-pack",
                "future_fix": "Start recording recurring failures",
                "status": "open",
            },
            {
                "class": "invalid_signal",
                "surface": "ci/e2e-smoke",
                "future_fix": "Stabilize the archive-to-restore seed",
                "status": "open",
            },
            {
                "class": "pre_existing_noise",
                "surface": "frontend/workspace-mode-ordering",
                "future_fix": "#1343: guard summary mode application",
                "status": "open",
            },
            {
                "class": "blocker",
                "surface": "frontend/paper-review-contract",
                "future_fix": "#1347: align enum wire contracts",
                "status": "open",
            },
            {
                "class": "blocker",
                "surface": "backend/similar-past",
                "future_fix": "#1348: repair the SQLite query",
                "status": "open",
            },
            {
                "class": "non_blocking_risk",
                "surface": "agent/tool-command-composition",
                "future_fix": "#1490: document safe composition",
                "status": "open",
            },
            {
                "class": "pre_existing_noise",
                "surface": "frontend/workspace-mode-ordering",
                "future_fix": "#1343 resolved by PR #1386",
                "status": "resolved",
            },
            {
                "class": "blocker",
                "surface": "frontend/paper-review-contract",
                "future_fix": "#1347 resolved by PR #1360",
                "status": "resolved",
            },
            {
                "class": "blocker",
                "surface": "backend/similar-past",
                "future_fix": "#1348 resolved by PR #1361 and PR #1362",
                "status": "resolved",
            },
            {
                "class": "non_blocking_risk",
                "surface": "agent/tool-command-composition",
                "future_fix": "#1490 resolved by PR #1491",
                "status": "resolved",
            },
        ]

        projected = project_latest_entries(entries)
        target_surfaces = {
            "#1343": "frontend/workspace-mode-ordering",
            "#1347": "frontend/paper-review-contract",
            "#1348": "backend/similar-past",
            "#1490": "agent/tool-command-composition",
        }

        self.assertEqual(len(entries), 10)
        self.assertEqual(len(projected), 5)
        self.assertEqual(Counter(entry["status"] for entry in projected), {"resolved": 4, "open": 1})
        self.assertFalse(any(entry.get("class") == "seed" for entry in projected))

        for issue, surface in target_surfaces.items():
            matches = [
                entry
                for entry in projected
                if entry.get("surface") == surface
                and str(entry.get("future_fix", "")).startswith(issue)
            ]
            self.assertEqual(len(matches), 1, issue)
            self.assertEqual(matches[0]["status"], "resolved", issue)

        open_entries = [entry for entry in projected if entry["status"] == "open"]
        self.assertEqual(
            [(entry["class"], entry["surface"]) for entry in open_entries],
            [("invalid_signal", "ci/e2e-smoke")],
        )


class FailureLedgerEntryPointTests(unittest.TestCase):
    def assert_invalid_input_preserves_target(
        self,
        payload: str,
        expected_line: int,
        expected_error: str,
    ) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "failure_ledger.jsonl"
            target = Path(directory) / "FAILURE_LEDGER.md"
            source.write_text(payload, encoding="utf-8")
            target.write_text("sentinel", encoding="utf-8")
            stderr = io.StringIO()

            with (
                patch.object(ledger, "JSONL", source),
                patch.object(ledger, "MD", target),
                redirect_stderr(stderr),
            ):
                return_code = ledger.main()

            self.assertNotEqual(return_code, 0)
            self.assertEqual(target.read_text(encoding="utf-8"), "sentinel")
            self.assertIn(str(source), stderr.getvalue())
            self.assertIn(f"line {expected_line}", stderr.getvalue())
            self.assertIn(expected_error, stderr.getvalue())

    def test_malformed_json_returns_nonzero_without_overwriting_markdown(self) -> None:
        self.assert_invalid_input_preserves_target(
            '{"status": "open"}\n{not-json}\n',
            2,
            "invalid JSON",
        )

    def test_non_object_json_returns_nonzero_without_overwriting_markdown(self) -> None:
        self.assert_invalid_input_preserves_target(
            "[]\n",
            1,
            "expected a JSON object, got list",
        )

    def test_missing_empty_and_seed_only_sources_remain_valid(self) -> None:
        seed = {
            "ts": "2026-05-11T00:00:00Z",
            "class": "seed",
            "surface": "agentic-pack",
            "failure": "Ledger created",
            "status": "open",
        }
        cases = {
            "missing": None,
            "empty": "\n",
            "seed-only": json.dumps(seed) + "\n",
        }

        for name, payload in cases.items():
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / "failure_ledger.jsonl"
                target = Path(directory) / "FAILURE_LEDGER.md"
                if payload is not None:
                    source.write_text(payload, encoding="utf-8")

                with (
                    patch.object(ledger, "JSONL", source),
                    patch.object(ledger, "MD", target),
                ):
                    return_code = ledger.main()

                self.assertEqual(return_code, 0)
                self.assertTrue(target.exists())
                self.assertNotIn("| seed |", target.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
