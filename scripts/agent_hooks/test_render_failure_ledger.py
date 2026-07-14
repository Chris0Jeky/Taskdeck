import unittest

from render_failure_ledger import project_latest_entries


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
            {"surface": "tooling", "future_fix": "Investigate", "status": "open"},
            {"surface": "tooling", "future_fix": "Investigate", "status": "resolved"},
        ]

        projected = project_latest_entries(entries)

        self.assertEqual(projected, entries)


if __name__ == "__main__":
    unittest.main()
