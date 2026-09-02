import unittest
from _load import load

m = load('03_IMPLEMENTATION_CANDIDATES/smart-ci/weekly_ci_report.py', 'weekly_ci_report')


class WeeklyCiReportTests(unittest.TestCase):
    def test_report_contains_aggregates_flakes_and_duplicate_sha(self):
        receipts = [
            {
                'head_sha': 'abc',
                'summary': {'critical_path_seconds': 60, 'aggregate_runner_seconds': 120, 'hosted_minutes': 2, 'hosted_cost_estimate': 0.02, 'self_hosted_wall_seconds': 0, 'flake_detected': False},
                'jobs': [{'lane': 'core', 'result': 'success', 'queue_seconds': 2, 'rerun': False, 'cache_hit': True, 'artifact_bytes': 10}],
            },
            {
                'head_sha': 'abc',
                'summary': {'critical_path_seconds': 180, 'aggregate_runner_seconds': 300, 'hosted_minutes': 5, 'hosted_cost_estimate': 0.05, 'self_hosted_wall_seconds': 30, 'flake_detected': True},
                'jobs': [{'lane': 'core', 'result': 'failure', 'queue_seconds': 8, 'rerun': True, 'cache_hit': False, 'artifact_bytes': 20}],
            },
        ]
        report = m.build_report(receipts)
        self.assertIn('Receipts: **2**', report)
        self.assertIn('Flaky receipts / rerun jobs: **1 / 1**', report)
        self.assertIn('Duplicate exact-SHA qualification: **1 SHA(s)**', report)
        self.assertIn('| core | 2 | 1 | 50.0% |', report)

    def test_empty_receipts_are_supported(self):
        report = m.build_report([])
        self.assertIn('Receipts: **0**', report)


if __name__ == '__main__':
    unittest.main()
