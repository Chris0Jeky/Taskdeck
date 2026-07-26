import unittest
from collections import Counter

from render_failure_ledger import project_latest_entries, render_markdown


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


if __name__ == "__main__":
    unittest.main()
